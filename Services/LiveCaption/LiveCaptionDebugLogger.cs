using System;
using System.Diagnostics;

namespace ImageColorChanger.Services.LiveCaption
{
    internal static class LiveCaptionDebugLogger
    {
        private static readonly string SessionId = Guid.NewGuid().ToString("N")[..8];

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [LiveCaption] [S:{SessionId}] {message}";

            try
            {
                Debug.WriteLine(line);
            }
            catch
            {
                // ignored
            }
        }
    }
}
