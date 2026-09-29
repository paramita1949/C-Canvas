using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services.Ai
{
    public interface IAiTransportDiagnosticSource
    {
        event Action<string> DiagnosticEmitted;
    }

    public class OpenAiChatClient : IDeepSeekChatClient, IAiTransportDiagnosticSource, IDisposable
    {
        private readonly ConfigManager _config;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public event Action<string> DiagnosticEmitted;

        public OpenAiChatClient(ConfigManager config)
            : this(config, new HttpClient { Timeout = TimeSpan.FromSeconds(90) }, ownsHttpClient: true)
        {
        }

        internal OpenAiChatClient(ConfigManager config, HttpClient httpClient, bool ownsHttpClient = false)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = ownsHttpClient;
        }

        public async Task<AiChatStreamResult> StreamChatAsync(
            AiChatRequest request,
            Action<string> onContentDelta,
            CancellationToken cancellationToken)
        {
            // ResponseHeadersRead only bounds the headers. Keep a deadline alive through body reads.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_httpClient.Timeout == Timeout.InfiniteTimeSpan
                ? TimeSpan.FromSeconds(90) : _httpClient.Timeout);
            try
            {
                return await StreamChatCoreAsync(request, onContentDelta, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                EmitDiagnostic("AI请求超时或取消（包含响应流读取阶段）");
                throw;
            }
            catch (Exception ex)
            {
                EmitDiagnostic($"AI响应处理失败：{ex.Message}");
                throw;
            }
        }

        private async Task<AiChatStreamResult> StreamChatCoreAsync(
            AiChatRequest request, Action<string> onContentDelta, CancellationToken cancellationToken)
        {
            string model = _config.AiSermonModel;
            string apiKey = _config.AiSermonApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("AI Key 未配置");
            }

            if (IsGeminiModel(model))
            {
                return await StreamGeminiChatAsync(request, onContentDelta, cancellationToken, apiKey, model)
                    .ConfigureAwait(false);
            }

            if (string.Equals(_config.AiSermonProtocol, AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase))
            {
                return await StreamResponsesChatAsync(request, onContentDelta, cancellationToken, apiKey, model)
                    .ConfigureAwait(false);
            }

            var payload = BuildCompletionsPayload(request);
            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{_config.AiSermonBaseUrl}/chat/completions");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            ApplyOpenCodeHeaders(message, request);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
            EmitDiagnostic($"POST /chat/completions：请求已提交，等待响应头（model={model}）");

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            EmitDiagnostic($"POST /chat/completions：已收到响应头（HTTP {(int)response.StatusCode}）");

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(BuildErrorMessage(response.StatusCode, response.ReasonPhrase, body));
            }

            return await ReadStreamAsync(response, onContentDelta, cancellationToken).ConfigureAwait(false);
        }

        public async Task<DeepSeekBalanceSnapshot> GetBalanceAsync(CancellationToken cancellationToken)
        {
            if (IsGeminiModel(_config.AiSermonModel) || !string.Equals(_config.AiSermonProviderId, "deepseek", StringComparison.OrdinalIgnoreCase))
            {
                return new DeepSeekBalanceSnapshot
                {
                    IsAvailable = false
                };
            }

            string apiKey = _config.AiSermonApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("AI Key 未配置");
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, $"{_config.AiSermonBaseUrl}/user/balance");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            EmitDiagnostic("GET /user/balance：请求已提交，等待响应头");

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
            EmitDiagnostic($"GET /user/balance：已收到响应头（HTTP {(int)response.StatusCode}）");

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(BuildErrorMessage(response.StatusCode, response.ReasonPhrase, body));
            }

            return ParseBalanceSnapshot(body);
        }

        private void ApplyOpenCodeHeaders(HttpRequestMessage message, AiChatRequest request)
        {
            var uri = message.RequestUri;
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
                !string.Equals(uri.Host, "opencode.ai", StringComparison.OrdinalIgnoreCase) ||
                !(uri.AbsolutePath == "/zen/go/v1/chat/completions" || uri.AbsolutePath == "/zen/go/v1/responses"))
                return;

            // Provider routing identity is not the project/user id or prompt content.
            string identity = string.IsNullOrWhiteSpace(request?.ConversationId)
                ? Guid.NewGuid().ToString("N") : request.ConversationId;
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                "CanvasCast/opencode/session/v1\0" + uri.AbsolutePath + "\0" + identity))).ToLowerInvariant();
            message.Headers.Add("x-opencode-session", "canvas_" + digest.Substring(0, 32));
            message.Headers.UserAgent.ParseAdd("CanvasCast/" +
                (typeof(OpenAiChatClient).Assembly.GetName().Version?.ToString() ?? "1.0"));
            EmitDiagnostic("OpenCode：已附加会话路由标识和 CanvasCast 客户端标识（Go 用途限制仍适用）");
        }

        private object BuildCompletionsPayload(AiChatRequest request)
        {
            bool omitOrdinaryMessageNames = IsOpenCodeProvider();
            var messages = (request?.Messages ?? Array.Empty<AiConversationMessage>())
                .Where(m => !string.IsNullOrWhiteSpace(m?.Content))
                .Select(m =>
                {
                    var message = new Dictionary<string, object>
                    {
                        ["role"] = string.IsNullOrWhiteSpace(m.Role) ? "user" : m.Role,
                        ["content"] = m.Content
                    };
                    if (!omitOrdinaryMessageNames && !string.IsNullOrWhiteSpace(m.Name))
                    {
                        message["name"] = m.Name;
                    }
                    return message;
                })
                .ToList();

            bool isDeepSeek = string.Equals(_config.AiSermonProviderId, "deepseek", StringComparison.OrdinalIgnoreCase);
            var payload = new Dictionary<string, object>
            {
                ["model"] = _config.AiSermonModel,
                ["messages"] = messages,
                ["stream"] = true,
                ["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true }
            };

            if (isDeepSeek)
            {
                payload["thinking"] = new Dictionary<string, object> { ["type"] = "enabled" };
                payload["reasoning_effort"] = "high";
            }
            else
            {
                payload["temperature"] = 0.2;
            }

            if (request?.EnableScriptureTool == true)
            {
                payload["tools"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "function",
                        ["function"] = new Dictionary<string, object>
                        {
                            ["name"] = "propose_scripture_candidate",
                            ["description"] = "提出可能需要写入圣经历史记录的经文候选。本函数只提出候选，不执行写入。",
                            ["strict"] = true,
                            ["parameters"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["bookName"] = new Dictionary<string, object> { ["type"] = "string" },
                                    ["chapter"] = new Dictionary<string, object> { ["type"] = "integer" },
                                    ["startVerse"] = new Dictionary<string, object> { ["type"] = "integer" },
                                    ["endVerse"] = new Dictionary<string, object> { ["type"] = "integer" },
                                    ["confidence"] = new Dictionary<string, object> { ["type"] = "number" },
                                    ["reason"] = new Dictionary<string, object> { ["type"] = "string" },
                                    ["evidenceText"] = new Dictionary<string, object> { ["type"] = "string" }
                                },
                                ["required"] = new[] { "bookName", "confidence", "reason", "evidenceText" }
                            }
                        }
                    }
                };
                payload["tool_choice"] = "auto";
            }

            return payload;
        }

        private bool IsOpenCodeProvider()
        {
            if (string.Equals(_config.AiSermonProviderId, "opencode-go", StringComparison.OrdinalIgnoreCase)
                || string.Equals(_config.AiSermonProviderId, "opencode-zen", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!Uri.TryCreate(_config.AiSermonBaseUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, "opencode.ai", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string path = uri.AbsolutePath.TrimEnd('/');
            return path.Equals("/zen/v1", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/zen/go/v1", StringComparison.OrdinalIgnoreCase);
        }

        private object BuildResponsesPayload(AiChatRequest request, string model)
        {
            var input = (request?.Messages ?? Array.Empty<AiConversationMessage>())
                .Where(m => !string.IsNullOrWhiteSpace(m?.Content))
                .Select(m => new Dictionary<string, object>
                {
                    ["role"] = string.IsNullOrWhiteSpace(m.Role) ? "user" : m.Role,
                    ["content"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["type"] = "input_text",
                            ["text"] = m.Content
                        }
                    }
                })
                .ToList();

            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["input"] = input,
                ["stream"] = true,
                ["store"] = false,
                ["user"] = string.IsNullOrWhiteSpace(request?.UserId) ? "canvas-sermon" : request.UserId
            };

            if (string.Equals(_config.AiSermonProviderId, "deepseek", StringComparison.OrdinalIgnoreCase))
            {
                payload["reasoning"] = new Dictionary<string, object> { ["effort"] = "high" };
            }

            if (request?.EnableScriptureTool == true)
            {
                payload["tools"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "function",
                        ["name"] = "propose_scripture_candidate",
                        ["description"] = "提出可能需要写入圣经历史记录的经文候选。本函数只提出候选，不执行写入。",
                        ["strict"] = true,
                        ["parameters"] = BuildScriptureToolParameters()
                    }
                };
                payload["tool_choice"] = "auto";
            }

            return payload;
        }

        private static object BuildScriptureToolParameters()
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["bookName"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["chapter"] = new Dictionary<string, object> { ["type"] = "integer" },
                    ["startVerse"] = new Dictionary<string, object> { ["type"] = "integer" },
                    ["endVerse"] = new Dictionary<string, object> { ["type"] = "integer" },
                    ["confidence"] = new Dictionary<string, object> { ["type"] = "number" },
                    ["reason"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["evidenceText"] = new Dictionary<string, object> { ["type"] = "string" }
                },
                ["required"] = new[] { "bookName", "confidence", "reason", "evidenceText" },
                ["additionalProperties"] = false
            };
        }

        private async Task<AiChatStreamResult> StreamResponsesChatAsync(
            AiChatRequest request,
            Action<string> onContentDelta,
            CancellationToken cancellationToken,
            string apiKey,
            string model)
        {
            var payload = BuildResponsesPayload(request, model);
            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{_config.AiSermonBaseUrl}/responses");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            ApplyOpenCodeHeaders(message, request);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
            EmitDiagnostic($"POST /responses：请求已提交，等待响应头（model={model}）");

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            EmitDiagnostic($"POST /responses：已收到响应头（HTTP {(int)response.StatusCode}）");

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(BuildErrorMessage(response.StatusCode, response.ReasonPhrase, body));
            }

            return await ReadResponsesStreamAsync(response, onContentDelta, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AiChatStreamResult> ReadResponsesStreamAsync(
            HttpResponseMessage response,
            Action<string> onContentDelta,
            CancellationToken cancellationToken)
        {
            var content = new StringBuilder();
            var toolArguments = new List<StringBuilder>();
            int cacheHit = 0;
            int cacheMiss = 0;

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                string line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string data = line.Substring("data:".Length).Trim();
                if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                using JsonDocument document = JsonDocument.Parse(data);
                JsonElement root = document.RootElement;
                ThrowIfStreamError(root);
                string type = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;

                if (string.Equals(type, "response.completed", StringComparison.Ordinal) &&
                    root.TryGetProperty("response", out var completedResponse) &&
                    completedResponse.TryGetProperty("usage", out var usage) &&
                    usage.ValueKind == JsonValueKind.Object)
                {
                    int inputTokens = GetInt32OrZero(usage, "input_tokens");
                    if (usage.TryGetProperty("input_tokens_details", out var inputDetails) &&
                        inputDetails.ValueKind == JsonValueKind.Object)
                    {
                        cacheHit = GetInt32OrZero(inputDetails, "cached_tokens");
                    }
                    cacheMiss = Math.Max(0, inputTokens - cacheHit);
                }

                if (string.Equals(type, "response.completed", StringComparison.Ordinal))
                {
                    break;
                }

                if (string.Equals(type, "response.output_text.delta", StringComparison.Ordinal))
                {
                    string chunk = root.TryGetProperty("delta", out var deltaElement)
                        ? deltaElement.GetString() ?? string.Empty
                        : string.Empty;
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        content.Append(chunk);
                        onContentDelta?.Invoke(chunk);
                    }
                    continue;
                }

                if (string.Equals(type, "response.function_call_arguments.delta", StringComparison.Ordinal))
                {
                    EnsureToolArgumentBuffer(toolArguments, root, appendFullArguments: false);
                    continue;
                }

                if (string.Equals(type, "response.function_call_arguments.done", StringComparison.Ordinal))
                {
                    EnsureToolArgumentBuffer(toolArguments, root, appendFullArguments: true);
                    continue;
                }

                if (string.Equals(type, "response.output_item.done", StringComparison.Ordinal) &&
                    root.TryGetProperty("item", out var item) &&
                    item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("type", out var itemType) &&
                    string.Equals(itemType.GetString(), "function_call", StringComparison.Ordinal) &&
                    item.TryGetProperty("arguments", out var arguments) &&
                    arguments.ValueKind == JsonValueKind.String)
                {
                    toolArguments.Add(new StringBuilder(arguments.GetString() ?? string.Empty));
                }
            }

            return new AiChatStreamResult
            {
                Content = content.ToString(),
                ScriptureCandidates = ParseCandidates(toolArguments.Select(value => value.ToString())),
                PromptCacheHitTokens = cacheHit,
                PromptCacheMissTokens = cacheMiss
            };
        }

        private static void ThrowIfStreamError(JsonElement root)
        {
            string type = root.TryGetProperty("type", out var eventType) && eventType.ValueKind == JsonValueKind.String
                ? eventType.GetString() : string.Empty;
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            {
                throw new HttpRequestException($"AI流返回错误：{TrimForError(error.ToString())}");
            }
            if (type == "error" || type == "response.failed" || type == "response.incomplete")
            {
                string detail = type;
                if (root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
                {
                    if (response.TryGetProperty("error", out var responseError) && responseError.ValueKind != JsonValueKind.Null)
                        detail = responseError.ToString();
                    else if (response.TryGetProperty("incomplete_details", out var incomplete))
                        detail = incomplete.ToString();
                }
                else if (root.TryGetProperty("message", out var message))
                    detail = message.ToString();
                throw new HttpRequestException($"AI流未成功完成（{type}）：{TrimForError(detail)}");
            }
        }

        private static void EnsureToolArgumentBuffer(
            List<StringBuilder> toolArguments,
            JsonElement root,
            bool appendFullArguments)
        {
            int index = root.TryGetProperty("output_index", out var outputIndex) && outputIndex.ValueKind == JsonValueKind.Number
                ? outputIndex.GetInt32()
                : 0;
            while (toolArguments.Count <= index)
            {
                toolArguments.Add(new StringBuilder());
            }

            if (root.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
            {
                toolArguments[index].Append(delta.GetString());
            }
            else if (appendFullArguments && root.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.String)
            {
                toolArguments[index].Clear();
                toolArguments[index].Append(arguments.GetString());
            }
        }

        private object BuildGeminiPayload(AiChatRequest request)
        {
            var sourceMessages = (request?.Messages ?? Array.Empty<AiConversationMessage>())
                .Where(m => !string.IsNullOrWhiteSpace(m?.Content))
                .ToList();

            string systemInstruction = sourceMessages
                .Where(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Content.Trim())
                .FirstOrDefault();

            var contents = sourceMessages
                .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                .Select(m => new Dictionary<string, object>
                {
                    ["role"] = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                    ["parts"] = new object[]
                    {
                        new Dictionary<string, object> { ["text"] = m.Content }
                    }
                })
                .ToList();

            if (contents.Count == 0)
            {
                contents.Add(new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["parts"] = new object[]
                    {
                        new Dictionary<string, object> { ["text"] = "请继续。"}
                    }
                });
            }

            var payload = new Dictionary<string, object>
            {
                ["contents"] = contents,
                ["generationConfig"] = new Dictionary<string, object>
                {
                    ["temperature"] = 0.2
                }
            };

            if (!string.IsNullOrWhiteSpace(systemInstruction))
            {
                payload["system_instruction"] = new Dictionary<string, object>
                {
                    ["parts"] = new object[]
                    {
                        new Dictionary<string, object> { ["text"] = systemInstruction }
                    }
                };
            }

            if (request?.EnableScriptureTool == true)
            {
                payload["tools"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["functionDeclarations"] = new object[]
                        {
                            new Dictionary<string, object>
                            {
                                ["name"] = "propose_scripture_candidate",
                                ["description"] = "提出可能需要写入圣经历史记录的经文候选。本函数只提出候选，不执行写入。",
                                ["parameters"] = new Dictionary<string, object>
                                {
                                    ["type"] = "object",
                                    ["properties"] = new Dictionary<string, object>
                                    {
                                        ["bookName"] = new Dictionary<string, object> { ["type"] = "string" },
                                        ["chapter"] = new Dictionary<string, object> { ["type"] = "integer" },
                                        ["startVerse"] = new Dictionary<string, object> { ["type"] = "integer" },
                                        ["endVerse"] = new Dictionary<string, object> { ["type"] = "integer" },
                                        ["confidence"] = new Dictionary<string, object> { ["type"] = "number" },
                                        ["reason"] = new Dictionary<string, object> { ["type"] = "string" },
                                        ["evidenceText"] = new Dictionary<string, object> { ["type"] = "string" }
                                    },
                                    ["required"] = new[] { "bookName", "confidence", "reason", "evidenceText" }
                                }
                            }
                        }
                    }
                };
                payload["toolConfig"] = new Dictionary<string, object>
                {
                    ["functionCallingConfig"] = new Dictionary<string, object>
                    {
                        ["mode"] = "AUTO"
                    }
                };
            }

            return payload;
        }

        private async Task<AiChatStreamResult> StreamGeminiChatAsync(
            AiChatRequest request,
            Action<string> onContentDelta,
            CancellationToken cancellationToken,
            string apiKey,
            string model)
        {
            var payload = BuildGeminiPayload(request);
            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            string modelName = string.IsNullOrWhiteSpace(model) ? "gemini-3.5-flash" : model.Trim();
            string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(apiKey)}";
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(BuildGeminiErrorMessage(response.StatusCode, response.ReasonPhrase, body));
            }

            return await ReadGeminiStreamAsync(response, onContentDelta, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AiChatStreamResult> ReadGeminiStreamAsync(
            HttpResponseMessage response,
            Action<string> onContentDelta,
            CancellationToken cancellationToken)
        {
            var content = new StringBuilder();
            var candidates = new List<AiScriptureCandidate>();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                string line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string data = line.Substring("data:".Length).Trim();
                if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                using JsonDocument doc = JsonDocument.Parse(data);
                if (!doc.RootElement.TryGetProperty("candidates", out var responseCandidates) ||
                    responseCandidates.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var candidate in responseCandidates.EnumerateArray())
                {
                    if (!candidate.TryGetProperty("content", out var contentNode) ||
                        !contentNode.TryGetProperty("parts", out var parts) ||
                        parts.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textNode) &&
                            textNode.ValueKind == JsonValueKind.String)
                        {
                            string chunk = textNode.GetString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                content.Append(chunk);
                                onContentDelta?.Invoke(chunk);
                            }
                        }

                        if (part.TryGetProperty("functionCall", out var functionCall) &&
                            functionCall.ValueKind == JsonValueKind.Object &&
                            functionCall.TryGetProperty("name", out var functionName) &&
                            string.Equals(functionName.GetString(), "propose_scripture_candidate", StringComparison.Ordinal))
                        {
                            if (functionCall.TryGetProperty("args", out var args) &&
                                args.ValueKind == JsonValueKind.Object)
                            {
                                try
                                {
                                    string argsJson = args.GetRawText();
                                    var scriptureCandidate = JsonSerializer.Deserialize<AiScriptureCandidate>(argsJson, new JsonSerializerOptions
                                    {
                                        PropertyNameCaseInsensitive = true
                                    });
                                    if (scriptureCandidate != null)
                                    {
                                        candidates.Add(scriptureCandidate);
                                    }
                                }
                                catch (JsonException)
                                {
                                    // ignore malformed candidate
                                }
                            }
                        }
                    }
                }
            }

            return new AiChatStreamResult
            {
                Content = content.ToString(),
                ScriptureCandidates = candidates
            };
        }

        private async Task<AiChatStreamResult> ReadStreamAsync(
            HttpResponseMessage response,
            Action<string> onContentDelta,
            CancellationToken cancellationToken)
        {
            var content = new StringBuilder();
            var toolArguments = new Dictionary<int, StringBuilder>();
            int cacheHit = 0;
            int cacheMiss = 0;

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                string line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string data = line.Substring("data:".Length).Trim();
                if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                using JsonDocument doc = JsonDocument.Parse(data);
                ThrowIfStreamError(doc.RootElement);
                if (doc.RootElement.TryGetProperty("usage", out var usage) &&
                    usage.ValueKind == JsonValueKind.Object)
                {
                    cacheHit = GetInt32OrZero(usage, "prompt_cache_hit_tokens");
                    cacheMiss = GetInt32OrZero(usage, "prompt_cache_miss_tokens");
                }

                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    continue;
                }

                var choice = choices[0];
                if (!choice.TryGetProperty("delta", out var delta))
                {
                    continue;
                }

                if (delta.TryGetProperty("content", out var contentElement) &&
                    contentElement.ValueKind == JsonValueKind.String)
                {
                    string chunk = contentElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        content.Append(chunk);
                        onContentDelta?.Invoke(chunk);
                    }
                }

                if (delta.TryGetProperty("tool_calls", out var toolCalls) &&
                    toolCalls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        int index = GetInt32OrZero(toolCall, "index");
                        if (!toolArguments.TryGetValue(index, out var builder))
                        {
                            builder = new StringBuilder();
                            toolArguments[index] = builder;
                        }

                        if (toolCall.TryGetProperty("function", out var fn) &&
                            fn.TryGetProperty("arguments", out var args) &&
                            args.ValueKind == JsonValueKind.String)
                        {
                            builder.Append(args.GetString());
                        }
                    }
                }
            }

            return new AiChatStreamResult
            {
                Content = content.ToString(),
                ScriptureCandidates = ParseCandidates(toolArguments.Values.Select(v => v.ToString())),
                PromptCacheHitTokens = cacheHit,
                PromptCacheMissTokens = cacheMiss
            };
        }

        private static IReadOnlyList<AiScriptureCandidate> ParseCandidates(IEnumerable<string> argumentPayloads)
        {
            var candidates = new List<AiScriptureCandidate>();
            foreach (string payload in argumentPayloads)
            {
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                try
                {
                    var candidate = JsonSerializer.Deserialize<AiScriptureCandidate>(payload, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (candidate != null)
                    {
                        candidates.Add(candidate);
                    }
                }
                catch (JsonException)
                {
                    // Malformed tool calls are ignored; local validation still gates all writes.
                }
            }

            return candidates;
        }

        private static DeepSeekBalanceSnapshot ParseBalanceSnapshot(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json ?? "{}");
            bool available = doc.RootElement.TryGetProperty("is_available", out var availableElement) &&
                             availableElement.ValueKind == JsonValueKind.True;

            if (doc.RootElement.TryGetProperty("balance_infos", out var infos) &&
                infos.ValueKind == JsonValueKind.Array)
            {
                JsonElement? selected = null;
                foreach (var info in infos.EnumerateArray())
                {
                    if (info.TryGetProperty("currency", out var currencyElement) &&
                        string.Equals(currencyElement.GetString(), "CNY", StringComparison.OrdinalIgnoreCase))
                    {
                        selected = info;
                        break;
                    }

                    selected ??= info;
                }

                if (selected.HasValue)
                {
                    var item = selected.Value;
                    string currency = item.TryGetProperty("currency", out var currencyElement)
                        ? currencyElement.GetString() ?? string.Empty
                        : string.Empty;
                    decimal total = 0m;
                    if (item.TryGetProperty("total_balance", out var totalElement) &&
                        totalElement.ValueKind == JsonValueKind.String)
                    {
                        decimal.TryParse(
                            totalElement.GetString(),
                            System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out total);
                    }

                    return new DeepSeekBalanceSnapshot
                    {
                        IsAvailable = available,
                        Currency = currency,
                        TotalBalance = total
                    };
                }
            }

            return new DeepSeekBalanceSnapshot { IsAvailable = available };
        }

        private static int GetInt32OrZero(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return 0;
            }

            if (!element.TryGetProperty(propertyName, out var value))
            {
                return 0;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out int n) => n,
                _ => 0
            };
        }

        private static string TrimForError(string value)
        {
            string text = (value ?? string.Empty).Trim();
            return text.Length <= 300 ? text : text.Substring(0, 300);
        }

        private string BuildErrorMessage(System.Net.HttpStatusCode statusCode, string reasonPhrase, string body)
        {
            string text = body ?? string.Empty;
            string providerId = _config.AiSermonProviderId;
            string providerName = AiProviderCatalog.Find(providerId)?.DisplayName ?? "AI";
            if (statusCode == System.Net.HttpStatusCode.Unauthorized ||
                statusCode == System.Net.HttpStatusCode.Forbidden ||
                text.IndexOf("Authentication", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("api key", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return string.Equals(providerId, "deepseek", StringComparison.OrdinalIgnoreCase)
                    ? "DeepSeek 认证失败：API Key 无效或已过期。请打开 AI平台 > AI DeepSeek 配置中心，重新填写密钥后再试。"
                    : $"{providerName} 认证失败：API Key 无效或已过期。请打开 AI平台重新填写密钥后再试。";
            }

            return $"{providerName} 请求失败：{(int)statusCode} {reasonPhrase} {TrimForError(text)}";
        }

        private static string BuildGeminiErrorMessage(System.Net.HttpStatusCode statusCode, string reasonPhrase, string body)
        {
            string text = body ?? string.Empty;
            if (statusCode == System.Net.HttpStatusCode.Unauthorized ||
                statusCode == System.Net.HttpStatusCode.Forbidden ||
                text.IndexOf("api key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Gemini 认证失败：API Key 无效或权限不足。请打开 AI平台 重新填写密钥。";
            }

            return $"Gemini 请求失败：{(int)statusCode} {reasonPhrase} {TrimForError(text)}";
        }

        private static bool IsGeminiModel(string model)
        {
            return !string.IsNullOrWhiteSpace(model) &&
                   model.Trim().StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);
        }

        private void EmitDiagnostic(string message)
        {
            string safeMessage = message ?? string.Empty;
            Debug.WriteLine($"[AiTransport] {safeMessage}");
            try
            {
                DiagnosticEmitted?.Invoke(safeMessage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiTransport] 诊断回调异常：{ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}

