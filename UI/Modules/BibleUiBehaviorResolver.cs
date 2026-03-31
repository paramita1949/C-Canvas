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
                // 圣经“滚动标题”模式下，标题已经在经文内容里（随内容一起投影），
                // 这里不再叠加顶部 Overlay 标题，避免出现双标题。
                return isBibleFixedTitle;
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
