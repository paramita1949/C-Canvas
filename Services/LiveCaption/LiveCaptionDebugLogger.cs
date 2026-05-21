using System;
using System.Diagnostics;
using System.IO;

namespace ImageColorChanger.Services.LiveCaption
{
    internal static class LiveCaptionDebugLogger
    {
        internal static readonly bool Enabled = false;

        internal static bool IsEnabledForEnvironment(string value, bool debugBuild)
        {
            _ = value;
            _ = debugBuild;
            return false;
        }

        public static void Log(string message)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (!ShouldLogMessage(message))
            {
                return;
            }

            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[LiveCaption][{ts}] {message}";
            Debug.WriteLine(line);
            TryAppendLogFile(line);
        }

        private static void TryAppendLogFile(string line)
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string logDir = Path.Combine(baseDir, "logs");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "livecaption-debug.log");
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
            catch
            {
                // Never let debug logging break recognition pipeline.
            }
        }

        private static bool ShouldLogMessage(string message)
        {
            _ = message;
            return true;
        }
    }
}
