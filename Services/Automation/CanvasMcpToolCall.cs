using System.Text.Json;

namespace ImageColorChanger.Services.Automation
{
    /// <summary>
    /// 本机 MCP 工具调用请求。
    /// </summary>
    public sealed class CanvasMcpToolCall
    {
        public CanvasMcpToolCall(string tool, JsonElement arguments)
        {
            Tool = tool ?? string.Empty;
            Arguments = arguments.ValueKind == JsonValueKind.Undefined
                ? default
                : arguments.Clone();
        }

        public string Tool { get; }

        public JsonElement Arguments { get; }
    }
}
