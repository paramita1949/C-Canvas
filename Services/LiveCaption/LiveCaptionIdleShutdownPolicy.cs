using System;

namespace ImageColorChanger.Services.LiveCaption
{
    internal static class LiveCaptionIdleShutdownPolicy
    {
        internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(1);

        internal static bool ShouldAutoDisable(
            DateTime lastRecognitionUtc,
            DateTime nowUtc,
            bool realtimeEnabled,
            bool shortPhraseEnabled,
            TimeSpan? idleTimeout = null)
        {
            if (!realtimeEnabled && !shortPhraseEnabled)
            {
                return false;
            }

            if (lastRecognitionUtc == DateTime.MinValue || nowUtc <= lastRecognitionUtc)
            {
                return false;
            }

            TimeSpan timeout = idleTimeout.GetValueOrDefault(DefaultIdleTimeout);
            if (timeout <= TimeSpan.Zero)
            {
                return true;
            }

            return (nowUtc - lastRecognitionUtc) > timeout;
        }
    }
}
