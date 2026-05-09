using System;

namespace ImageColorChanger.Core
{
    public static class FontSizeControlValue
    {
        public const double Step = 0.5;

        public static double SnapToStep(double value, double minimum, double maximum)
        {
            return SnapToStep(value, minimum, maximum, Step);
        }

        public static double SnapToStep(double value, double minimum, double maximum, double step)
        {
            if (minimum > maximum)
            {
                throw new ArgumentException("Minimum cannot be greater than maximum.", nameof(minimum));
            }
            if (step <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step), "Step must be greater than zero.");
            }

            double clamped = Math.Max(minimum, Math.Min(maximum, value));
            return Math.Round(clamped / step, MidpointRounding.AwayFromZero) * step;
        }
    }
}
