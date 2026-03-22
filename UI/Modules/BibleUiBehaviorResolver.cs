namespace ImageColorChanger.UI.Modules
{
    public static class BibleUiBehaviorResolver
    {
        public static BibleSearchResultDisplayMode ResolveSearchDisplayMode(
            BibleSearchResultDisplayMode preferredMode,
            bool isTextEditorVisible)
        {
            if (isTextEditorVisible && preferredMode == BibleSearchResultDisplayMode.Embedded)
            {
                return BibleSearchResultDisplayMode.Floating;
            }

            return preferredMode;
        }

        public static bool ShouldUseHistorySlideFlow(bool isTextEditorVisible, bool isProjectionActive)
        {
            return isTextEditorVisible && isProjectionActive;
        }

        public static bool ShouldUseF3BibleClearScreen(bool isBibleMode)
        {
            return isBibleMode;
        }
    }
}
