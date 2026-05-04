using System;
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
            _ = message;
        }

        public static void LogException(string title, Exception ex)
        {
            Log($"{title}: {ex.GetType().Name} - {ex.Message}");
        }

    }
}
