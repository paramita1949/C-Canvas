using System;

namespace ImageColorChanger.Services.Automation
{
    /// <summary>
    /// 本机 MCP 工具调用结果，统一返回 ok/message/data/error。
    /// </summary>
    public sealed class CanvasMcpToolResponse
    {
        public bool Ok { get; init; }
        public string Tool { get; init; }
        public string Message { get; init; }
        public object Data { get; init; }
        public CanvasMcpToolError Error { get; init; }
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

        public static CanvasMcpToolResponse Success(string tool, string message, object data = null)
        {
            return new CanvasMcpToolResponse
            {
                Ok = true,
                Tool = tool ?? string.Empty,
                Message = message ?? string.Empty,
                Data = data
            };
        }

        public static CanvasMcpToolResponse Failure(string tool, string message, string code = "tool_error", object data = null)
        {
            return new CanvasMcpToolResponse
            {
                Ok = false,
                Tool = tool ?? string.Empty,
                Message = message ?? string.Empty,
                Data = data,
                Error = new CanvasMcpToolError
                {
                    Code = string.IsNullOrWhiteSpace(code) ? "tool_error" : code,
                    Message = message ?? string.Empty
                }
            };
        }
    }

    public sealed class CanvasMcpToolError
    {
        public string Code { get; init; }
        public string Message { get; init; }
    }
}
