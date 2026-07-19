using System;
using System.IO;
using System.Text;

namespace ImageColorChanger.Services
{
    internal static class AuthFailureLogger
    {
        public static void Log(string context, string reason, string message = null)
        {
            try
            {
                string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, $"auth-failures-{DateTime.Now:yyyyMMdd}.log");
                var builder = new StringBuilder();
                builder.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
                builder.Append("context=").Append(context ?? "unknown");
                builder.Append("; reason=").Append(reason ?? "unknown");
                if (!string.IsNullOrWhiteSpace(message))
                {
                    builder.Append("; message=").Append(message.Replace("\r", " ").Replace("\n", " "));
                }
                builder.Append("; user=").Append(Environment.UserName);
                builder.Append("; machine=").Append(Environment.MachineName);
                File.AppendAllText(logFile, builder + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never affect authentication behavior.
            }
        }
    }
}
