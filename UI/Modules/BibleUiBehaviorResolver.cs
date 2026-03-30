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

        public static bool ShouldKeepProjectionBibleTitleVisible(
            bool isBibleMode,
            bool isBibleFixedTitle,
            bool isTextEditorVisible,
            bool hasCurrentTextProject,
            bool hasTitle)
        {
            if (!hasTitle)
            {
                return false;
            }

            if (isBibleMode)
            {
                return true;
            }

            // 幻灯片投影场景下，固定标题模式应保持可见。
            return isBibleFixedTitle && isTextEditorVisible && hasCurrentTextProject;
        }

        public static bool ShouldAutoLoadProjectOnProjectsViewEntry(bool hasCurrentTextProject)
        {
            return !hasCurrentTextProject;
        }

        public static bool ShouldClearBibleProjectionWhenSwitchingToSlides(
            bool wasInBibleMode,
            bool isProjectionActive)
        {
            return wasInBibleMode && isProjectionActive;
        }

        public static bool ShouldRestoreLockedSlideOnProjectsViewEntry(
            bool isProjectionLocked,
            bool hasLockedProjectAnchor,
            bool hasLockedSlideAnchor)
        {
            return isProjectionLocked &&
                   hasLockedProjectAnchor &&
                   hasLockedSlideAnchor;
        }

        public static bool ShouldAutoLoadBlankSlideOnProjectsViewEntry(
            bool wasInBibleMode,
            bool isProjectionActive,
            bool isProjectionLocked)
        {
            return wasInBibleMode && isProjectionActive && !isProjectionLocked;
        }
    }
}
