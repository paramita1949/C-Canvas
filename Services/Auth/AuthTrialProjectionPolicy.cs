using System;

namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthTrialProjectionPolicy
    {
        internal const int StatusValid = 0x1A2B3C4D;
        internal const int StatusTokenInvalid = unchecked((int)0xDEADBEEF);
        internal const int StatusExpired = unchecked((int)0xBADC0DE0);
        internal const int StatusTimeAnomaly = unchecked((int)0xBADC0DE1);

        public int GetStatus(
            bool isAuthenticated,
            long startTick,
            int durationSeconds,
            int maxDurationSeconds,
            bool tokenValid,
            long currentTick)
        {
            if (isAuthenticated)
            {
                return StatusValid;
            }

            if (startTick == 0)
            {
                return StatusValid;
            }

            if (!tokenValid)
            {
                return StatusTokenInvalid;
            }

            int effectiveDuration = Math.Min(durationSeconds, maxDurationSeconds);
            var elapsedMs = currentTick - startTick;
            var elapsedSeconds = elapsedMs / 1000;

            if (elapsedSeconds >= effectiveDuration)
            {
                return StatusExpired;
            }

            if (startTick > currentTick)
            {
                return StatusTimeAnomaly;
            }

            return StatusValid;
        }

        public int GetRemainingSeconds(
            bool isAuthenticated,
            long startTick,
            int statusCode,
            int validStatusCode,
            long currentTick,
            int durationSeconds,
            int maxDurationSeconds)
        {
            if (isAuthenticated || startTick == 0)
            {
                return -1;
            }

            if (statusCode != validStatusCode)
            {
                return 0;
            }

            var elapsedMs = currentTick - startTick;
            var elapsedSeconds = (int)(elapsedMs / 1000);
            int effectiveDuration = Math.Min(durationSeconds, maxDurationSeconds);
            var remaining = effectiveDuration - elapsedSeconds;
            return Math.Max(0, remaining);
        }
    }
}
