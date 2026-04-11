using System.Diagnostics;
using System;

namespace ImageColorChanger.Services.LiveCaption
{
    internal static class LiveCaptionDebugLogger
    {
        private static readonly bool Enabled =
#if DEBUG
            string.Equals(Environment.GetEnvironmentVariable("CANVAS_LIVECAPTION_DEBUG"), "1", StringComparison.Ordinal);
#else
            false;
#endif

        public static void Log(string message)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Debug.WriteLine(message);
        }
    }
}
