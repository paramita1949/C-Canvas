using ImageColorChanger.UI.Modules;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class BibleUiBehaviorResolverTests
    {
        [Theory]
        [InlineData(BibleSearchResultDisplayMode.Embedded, true, BibleSearchResultDisplayMode.Floating)]
        [InlineData(BibleSearchResultDisplayMode.Embedded, false, BibleSearchResultDisplayMode.Embedded)]
        [InlineData(BibleSearchResultDisplayMode.Floating, true, BibleSearchResultDisplayMode.Floating)]
        [InlineData(BibleSearchResultDisplayMode.Floating, false, BibleSearchResultDisplayMode.Floating)]
        public void ResolveSearchDisplayMode_ReturnsExpectedMode(
            BibleSearchResultDisplayMode preferredMode,
            bool isTextEditorVisible,
            BibleSearchResultDisplayMode expected)
        {
            var actual = BibleUiBehaviorResolver.ResolveSearchDisplayMode(preferredMode, isTextEditorVisible);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void ShouldUseHistorySlideFlow_RequiresTextEditorVisible(
            bool isTextEditorVisible,
            bool isProjectionActive,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldUseHistorySlideFlow(isTextEditorVisible, isProjectionActive);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void ShouldUseF3BibleClearScreen_OnlyWhenBibleMode(
            bool isBibleMode,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldUseF3BibleClearScreen(isBibleMode);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, true, false, false, true, true)]
        [InlineData(true, false, false, false, true, false)]
        [InlineData(false, true, true, true, true, true)]
        [InlineData(false, false, true, true, true, false)]
        [InlineData(false, true, false, true, true, false)]
        [InlineData(false, true, true, false, true, false)]
        [InlineData(false, true, true, true, false, false)]
        public void ShouldKeepProjectionBibleTitleVisible_ReturnsExpected(
            bool isBibleMode,
            bool isBibleFixedTitle,
            bool isTextEditorVisible,
            bool hasCurrentTextProject,
            bool hasTitle,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldKeepProjectionBibleTitleVisible(
                isBibleMode,
                isBibleFixedTitle,
                isTextEditorVisible,
                hasCurrentTextProject,
                hasTitle);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ShouldAutoLoadProjectOnProjectsViewEntry_ReturnsExpected(
            bool hasCurrentTextProject,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldAutoLoadProjectOnProjectsViewEntry(hasCurrentTextProject);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void ShouldClearBibleProjectionWhenSwitchingToSlides_ReturnsExpected(
            bool wasInBibleMode,
            bool isProjectionActive,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldClearBibleProjectionWhenSwitchingToSlides(
                wasInBibleMode,
                isProjectionActive);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, true, true, true)]
        [InlineData(true, true, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(false, true, true, false)]
        public void ShouldRestoreLockedSlideOnProjectsViewEntry_ReturnsExpected(
            bool isProjectionLocked,
            bool hasLockedProjectAnchor,
            bool hasLockedSlideAnchor,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldRestoreLockedSlideOnProjectsViewEntry(
                isProjectionLocked,
                hasLockedProjectAnchor,
                hasLockedSlideAnchor);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, true, false, true)]
        [InlineData(true, true, true, false)]
        [InlineData(true, false, false, false)]
        [InlineData(false, true, false, false)]
        public void ShouldAutoLoadBlankSlideOnProjectsViewEntry_ReturnsExpected(
            bool wasInBibleMode,
            bool isProjectionActive,
            bool isProjectionLocked,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldAutoLoadBlankSlideOnProjectsViewEntry(
                wasInBibleMode,
                isProjectionActive,
                isProjectionLocked);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, true, false, false, true)]
        [InlineData(true, false, true, false, false)]
        [InlineData(true, false, false, true, true)]
        [InlineData(false, true, false, true, false)]
        public void ShouldAllowBibleQuickLocateContext_ReturnsExpected(
            bool isBibleMode,
            bool isActivationKey,
            bool isAlphaKey,
            bool isPinyinInputActive,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldAllowBibleQuickLocateContext(
                isBibleMode,
                isActivationKey,
                isAlphaKey,
                isPinyinInputActive);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(false, true, false, true)]
        [InlineData(false, false, true, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, false, true, false)]
        public void ShouldAutoActivateBibleQuickLocateWhenInactive_ReturnsExpected(
            bool isPinyinInputActive,
            bool isActivationKey,
            bool isAlphaKey,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldAutoActivateBibleQuickLocateWhenInactive(
                isPinyinInputActive,
                isActivationKey,
                isAlphaKey);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("马太福音 mtfy 1 12", true, 12, 0, false)]
        [InlineData("马太福音1 12 1", true, 12, 1, true)]
        [InlineData("mtfy 1:12-15", true, 12, 15, true)]
        [InlineData("mtfy 1:12-", true, 12, 0, false)]
        [InlineData("mtfy 1", false, 0, 0, false)]
        public void TryExtractTypedVerseRangeForPreview_ReturnsExpected(
            string input,
            bool expectedSuccess,
            int expectedStart,
            int expectedEnd,
            bool expectedHasExplicitEnd)
        {
            bool success = BibleUiBehaviorResolver.TryExtractTypedVerseRangeForPreview(
                input,
                out int start,
                out int end,
                out bool hasExplicitEnd);

            Assert.Equal(expectedSuccess, success);
            Assert.Equal(expectedStart, start);
            Assert.Equal(expectedEnd, end);
            Assert.Equal(expectedHasExplicitEnd, hasExplicitEnd);
        }

        [Theory]
        [InlineData(12, 0, false, 28, 28)]
        [InlineData(12, 17, true, 28, 17)]
        [InlineData(12, 1, true, 28, 28)]
        [InlineData(5, 99, true, 20, 20)]
        [InlineData(5, 3, true, 20, 20)]
        public void ResolvePinyinPreviewEndVerse_ReturnsExpected(
            int startVerse,
            int typedEndVerse,
            bool hasExplicitEndVerse,
            int verseCount,
            int expected)
        {
            int actual = BibleUiBehaviorResolver.ResolvePinyinPreviewEndVerse(
                startVerse,
                typedEndVerse,
                hasExplicitEndVerse,
                verseCount);

            Assert.Equal(expected, actual);
        }
    }
}
