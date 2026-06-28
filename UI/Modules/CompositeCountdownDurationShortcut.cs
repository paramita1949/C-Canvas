using System;

namespace ImageColorChanger.UI.Modules
{
    public static class CompositeCountdownDurationShortcut
    {
        public static bool TryNormalizeElapsedDuration(double elapsedSeconds, out double duration)
        {
            duration = 0;

            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
            {
                return false;
            }

            double rounded = Math.Round(elapsedSeconds, 1, MidpointRounding.AwayFromZero);
            if (rounded <= 0)
            {
                return false;
            }

            duration = rounded;
            return true;
        }

        public static double AdjustDurationByWheelDelta(double currentDuration, int wheelDelta)
        {
            const double stepSeconds = 5.0;
            const double minimumSeconds = 1.0;

            if (double.IsNaN(currentDuration) || double.IsInfinity(currentDuration) || currentDuration < minimumSeconds)
            {
                currentDuration = minimumSeconds;
            }

            if (wheelDelta > 0)
            {
                currentDuration += stepSeconds;
            }
            else if (wheelDelta < 0)
            {
                currentDuration -= stepSeconds;
            }

            return Math.Round(Math.Max(minimumSeconds, currentDuration), 1, MidpointRounding.AwayFromZero);
        }
    }
}
