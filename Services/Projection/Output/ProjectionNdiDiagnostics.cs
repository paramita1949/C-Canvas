using System;
using System.Diagnostics;
namespace ImageColorChanger.Services.Projection.Output
{
    internal static class ProjectionNdiDiagnostics
    {
        public static string GetLogFilePath()
        {
            return string.Empty;
        }

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Trace.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
        }

        public static void LogException(string title, Exception ex)
        {
            Log($"{title}: {ex.GetType().Name} - {ex.Message}");
        }

    }
}
