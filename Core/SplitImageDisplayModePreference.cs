namespace ImageColorChanger.Core
{
    public static class SplitImageDisplayModePreference
    {
        public static SplitImageDisplayMode ResolveInitialPreference(SplitImageDisplayMode configuredMode)
        {
            return IsSupported(configuredMode) ? configuredMode : SplitImageDisplayMode.FitTop;
        }

        public static SplitImageDisplayMode ResolveSlideMode(
            SplitImageDisplayMode slideMode,
            SplitImageDisplayMode fallbackPreference)
        {
            return IsSupported(slideMode) ? slideMode : ResolveInitialPreference(fallbackPreference);
        }

        private static bool IsSupported(SplitImageDisplayMode mode)
        {
            return mode == SplitImageDisplayMode.FitCenter
                   || mode == SplitImageDisplayMode.Fill
                   || mode == SplitImageDisplayMode.FitTop;
        }
    }
}
