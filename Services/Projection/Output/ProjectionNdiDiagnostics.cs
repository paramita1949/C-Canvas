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
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                Debug.WriteLine(line);
            }
            catch
            {
            }
        }

        public static void LogException(string title, Exception ex)
        {
            Log($"{title}: {ex.GetType().Name} - {ex.Message}");
        }
    }
}
