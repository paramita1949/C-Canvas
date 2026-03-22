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
    }
}
