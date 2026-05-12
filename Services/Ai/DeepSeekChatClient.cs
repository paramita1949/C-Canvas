using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services.Ai
{
    public sealed class DeepSeekChatClient : IDeepSeekChatClient, IDisposable
    {
        private readonly ConfigManager _config;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public DeepSeekChatClient(ConfigManager config)
            : this(config, new HttpClient { Timeout = TimeSpan.FromSeconds(90) }, ownsHttpClient: true)
        {
        }

        internal DeepSeekChatClient(ConfigManager config, HttpClient httpClient, bool ownsHttpClient = false)
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
            string apiKey = (_config.DeepSeekApiKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("DeepSeek API Key 未配置");
            }

            var payload = BuildPayload(request);
            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{_config.DeepSeekBaseUrl}/chat/completions");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException(BuildErrorMessage(response.StatusCode, response.ReasonPhrase, body));
            }

            return await ReadStreamAsync(response, onContentDelta, cancellationToken).ConfigureAwait(false);
        }

        private object BuildPayload(AiChatRequest request)
        {
            var messages = (request?.Messages ?? Array.Empty<AiConversationMessage>())
                .Where(m => !string.IsNullOrWhiteSpace(m?.Content))
                .Select(m =>
                {
                    var message = new Dictionary<string, object>
                    {
                        ["role"] = string.IsNullOrWhiteSpace(m.Role) ? "user" : m.Role,
                        ["content"] = m.Content
                    };
                    if (!string.IsNullOrWhiteSpace(m.Name))
                    {
                        message["name"] = m.Name;
                    }
                    return message;
                })
                .ToList();

            var payload = new Dictionary<string, object>
            {
                ["model"] = _config.DeepSeekModel,
                ["messages"] = messages,
                ["stream"] = true,
                ["temperature"] = 0.2,
                ["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true },
                ["user_id"] = string.IsNullOrWhiteSpace(request?.UserId) ? "canvas-sermon" : request.UserId
            };

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
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
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

        private static string BuildErrorMessage(System.Net.HttpStatusCode statusCode, string reasonPhrase, string body)
        {
            string text = body ?? string.Empty;
            if (statusCode == System.Net.HttpStatusCode.Unauthorized ||
                statusCode == System.Net.HttpStatusCode.Forbidden ||
                text.IndexOf("Authentication", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("api key", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "DeepSeek 认证失败：API Key 无效或已过期。请打开 AI平台 > AI DeepSeek 配置中心，重新填写密钥后再试。";
            }

            return $"DeepSeek 请求失败：{(int)statusCode} {reasonPhrase} {TrimForError(text)}";
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
