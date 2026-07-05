using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Automation
{
    /// <summary>
    /// MCP JSON-RPC 子集路由器：initialize、tools/list、tools/call。
    /// </summary>
    public sealed class CanvasMcpJsonRpcRouter
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        private readonly Func<CanvasMcpToolCall, CancellationToken, Task<CanvasMcpToolResponse>> _executeToolAsync;

        public CanvasMcpJsonRpcRouter(Func<CanvasMcpToolCall, CancellationToken, Task<CanvasMcpToolResponse>> executeToolAsync)
        {
            _executeToolAsync = executeToolAsync ?? throw new ArgumentNullException(nameof(executeToolAsync));
        }

        public async Task<string> HandleAsync(string requestJson, CancellationToken cancellationToken)
        {
            try
            {
                using var document = JsonDocument.Parse(requestJson);
                var root = document.RootElement;
                var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : default;
                string method = root.TryGetProperty("method", out var methodElement)
                    ? methodElement.GetString()
                    : string.Empty;

                object result = await HandleMethodAsync(method, root, cancellationToken).ConfigureAwait(false);
                return WriteResult(id, result);
            }
            catch (Exception ex)
            {
                return WriteError(default, -32603, ex.Message);
            }
        }

        private async Task<object> HandleMethodAsync(string method, JsonElement root, CancellationToken cancellationToken)
        {
            switch (method)
            {
                case "initialize":
                    return new
                    {
                        protocolVersion = "2025-06-18",
                        capabilities = new { tools = new { } },
                        serverInfo = new
                        {
                            name = "CanvasCast Local Control",
                            version = "0.1.0"
                        }
                    };

                case "tools/list":
                    return new
                    {
                        tools = CanvasMcpToolCatalog.GetToolDefinitions()
                    };

                case "tools/call":
                    var call = CreateToolCall(root);
                    var response = await _executeToolAsync(call, cancellationToken).ConfigureAwait(false);
                    return new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = JsonSerializer.Serialize(response, JsonOptions)
                            }
                        },
                        isError = !response.Ok
                    };

                case "notifications/initialized":
                    return new { };

                default:
                    throw new InvalidOperationException($"不支持的 MCP 方法: {method}");
            }
        }

        private static CanvasMcpToolCall CreateToolCall(JsonElement root)
        {
            if (!root.TryGetProperty("params", out var parameters) ||
                parameters.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("tools/call 缺少 params。");
            }

            string name = parameters.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("tools/call 缺少 params.name。");
            }

            JsonElement arguments = parameters.TryGetProperty("arguments", out var argsElement)
                ? argsElement
                : default;

            return new CanvasMcpToolCall(name, arguments);
        }

        private static string WriteResult(JsonElement id, object result)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", "2.0");
                writer.WritePropertyName("id");
                WriteId(writer, id);
                writer.WritePropertyName("result");
                JsonSerializer.Serialize(writer, result, JsonOptions);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string WriteError(JsonElement id, int code, string message)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", "2.0");
                writer.WritePropertyName("id");
                WriteId(writer, id);
                writer.WritePropertyName("error");
                writer.WriteStartObject();
                writer.WriteNumber("code", code);
                writer.WriteString("message", message ?? string.Empty);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteId(Utf8JsonWriter writer, JsonElement id)
        {
            if (id.ValueKind == JsonValueKind.Undefined || id.ValueKind == JsonValueKind.Null)
            {
                writer.WriteNullValue();
                return;
            }

            id.WriteTo(writer);
        }
    }
}
