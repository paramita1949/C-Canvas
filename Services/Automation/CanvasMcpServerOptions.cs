using System;
using System.Globalization;

namespace ImageColorChanger.Services.Automation
{
    public sealed class CanvasMcpServerOptions
    {
        public bool Enabled { get; init; } = true;
        public int Port { get; init; } = 8765;

        public static CanvasMcpServerOptions FromEnvironment()
        {
            bool enabled = !string.Equals(
                Environment.GetEnvironmentVariable("CANVAS_MCP_ENABLED"),
                "0",
                StringComparison.OrdinalIgnoreCase);

            int port = 8765;
            string rawPort = Environment.GetEnvironmentVariable("CANVAS_MCP_PORT");
            if (!string.IsNullOrWhiteSpace(rawPort) &&
                int.TryParse(rawPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                parsed >= 0 &&
                parsed <= 65535)
            {
                port = parsed;
            }

            return new CanvasMcpServerOptions
            {
                Enabled = enabled,
                Port = port
            };
        }
    }
}
