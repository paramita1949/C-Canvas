using System;

namespace ImageColorChanger.Core
{
    public static class FontSizeControlValue
    {
        public const double Step = 0.5;

        public static double SnapToStep(double value, double minimum, double maximum)
        {
            if (minimum > maximum)
            {
                throw new ArgumentException("Minimum cannot be greater than maximum.", nameof(minimum));
            }

            double clamped = Math.Max(minimum, Math.Min(maximum, value));
            return Math.Round(clamped / Step, MidpointRounding.AwayFromZero) * Step;
        }
    }
}
