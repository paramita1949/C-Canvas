using System.Text.RegularExpressions;

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

        public static bool ShouldAllowBibleQuickLocateContext(
            bool isBibleMode,
            bool isActivationKey,
            bool isAlphaKey,
            bool isPinyinInputActive)
        {
            _ = isAlphaKey;
            return isBibleMode && (isActivationKey || isPinyinInputActive);
        }

        public static bool ShouldAutoActivateBibleQuickLocateWhenInactive(
            bool isPinyinInputActive,
            bool isActivationKey,
            bool isAlphaKey)
        {
            _ = isAlphaKey;
            return !isPinyinInputActive && isActivationKey;
        }

        public static bool TryExtractTypedVerseRangeForPreview(
            string input,
            out int typedStartVerse,
            out int typedEndVerse,
            out bool hasExplicitEndVerse)
        {
            typedStartVerse = 0;
            typedEndVerse = 0;
            hasExplicitEndVerse = false;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            // 1) 末尾 "start-end"（end 可为空）
            var rangeMatch = Regex.Match(input, @"(?<start>\d+)\s*-\s*(?<end>\d*)\s*$");
            if (rangeMatch.Success)
            {
                if (!int.TryParse(rangeMatch.Groups["start"].Value, out typedStartVerse))
                {
                    typedStartVerse = 0;
                    return false;
                }

                string endPart = rangeMatch.Groups["end"].Value;
                if (!string.IsNullOrWhiteSpace(endPart) && int.TryParse(endPart, out int parsedEnd))
                {
                    typedEndVerse = parsedEnd;
                    hasExplicitEndVerse = true;
                }

                return true;
            }

            var numberMatches = Regex.Matches(input, @"\d+");

            // 2) 输入过程中的 "... 章 起始节 结束节"
            if (numberMatches.Count >= 3)
            {
                string startCandidate = numberMatches[numberMatches.Count - 2].Value;
                string endCandidate = numberMatches[numberMatches.Count - 1].Value;
                if (!int.TryParse(startCandidate, out typedStartVerse))
                {
                    typedStartVerse = 0;
                    return false;
                }

                if (int.TryParse(endCandidate, out int parsedTailEnd))
                {
                    typedEndVerse = parsedTailEnd;
                    hasExplicitEndVerse = true;
                }

                return true;
            }

            // 3) 末尾 "... 章 起始节"（无结束节） -> 预览应从起始节到章末
            if (numberMatches.Count >= 2 &&
                int.TryParse(numberMatches[numberMatches.Count - 1].Value, out typedStartVerse))
            {
                typedEndVerse = 0;
                hasExplicitEndVerse = false;
                return true;
            }

            return false;
        }

        public static int ResolvePinyinPreviewEndVerse(
            int startVerse,
            int typedEndVerse,
            bool hasExplicitEndVerse,
            int verseCount)
        {
            if (verseCount <= 0)
            {
                return startVerse;
            }

            if (!hasExplicitEndVerse || typedEndVerse <= 0)
            {
                return verseCount;
            }

            int normalizedTypedEnd = Math.Clamp(typedEndVerse, 1, verseCount);
            if (normalizedTypedEnd >= startVerse)
            {
                return normalizedTypedEnd;
            }

            // 输入结束节过程中（如 12 -> 1 -> 17）避免回落成单节闪烁，保持到章末预览。
            return verseCount;
        }
    }
}
