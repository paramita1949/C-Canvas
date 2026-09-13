namespace ImageColorChanger.UI.Modules
{
    internal static class BibleVerseScrollAnimationPolicy
    {
        public const int SlowSpeed = 0;
        public const int MediumSpeed = 1;
        public const int FastSpeed = 2;

        public static bool ResolveEnabled(bool? configured)
        {
            return configured ?? true;
        }

        public static bool ShouldAnimate(int direction, bool enabled)
        {
            return direction > 0 && enabled;
        }

        public static int GetDurationMilliseconds(int speed)
        {
            switch (speed)
            {
                case SlowSpeed:
                    return 420;
                case FastSpeed:
                    return 140;
                default:
                    return 260;
            }
        }
    }
}
