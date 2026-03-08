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
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void ShouldUseHistorySlideFlow_RequiresTextEditorVisible(
            bool isTextEditorVisible,
            bool expected)
        {
            var actual = BibleUiBehaviorResolver.ShouldUseHistorySlideFlow(isTextEditorVisible);
            Assert.Equal(expected, actual);
        }
    }
}
