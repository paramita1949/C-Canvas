using System.Windows.Input;
using ImageColorChanger.UI.Modules;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class BiblePopupOverlayInputPolicyTests
    {
        [Fact]
        public void GetDirectionFromKey_MapsUpAndDownOnly()
        {
            Assert.Equal(-1, BiblePopupOverlayInputPolicy.GetDirectionFromKey(Key.Up));
            Assert.Equal(1, BiblePopupOverlayInputPolicy.GetDirectionFromKey(Key.Down));
            Assert.Equal(0, BiblePopupOverlayInputPolicy.GetDirectionFromKey(Key.Left));
        }

        [Fact]
        public void TryGetClickedVerseIndex_UsesAnchorsAndScrollOffset()
        {
            double[] anchors = { 0, 42, 96 };
            double[] heights = { 40, 50, 44 };

            bool found = BiblePopupOverlayInputPolicy.TryGetClickedVerseIndex(
                viewportTop: 100,
                clickY: 128,
                scrollOffset: 20,
                anchors,
                heights,
                fallbackLineHeight: 40,
                out int index);

            Assert.True(found);
            Assert.Equal(1, index);
        }

        [Fact]
        public void TryGetClickedVerseIndex_FallsBackToLineHeightWhenGapIsClicked()
        {
            double[] anchors = { 0, 80 };
            double[] heights = { 30, 30 };

            bool found = BiblePopupOverlayInputPolicy.TryGetClickedVerseIndex(
                viewportTop: 0,
                clickY: 60,
                scrollOffset: 0,
                anchors,
                heights,
                fallbackLineHeight: 40,
                out int index);

            Assert.True(found);
            Assert.Equal(1, index);
        }

        [Fact]
        public void TryMoveHighlightedVerse_MovesByDirectionAndReturnsAnchorOffset()
        {
            double[] anchors = { 0, 45, 90 };

            bool moved = BiblePopupOverlayInputPolicy.TryMoveHighlightedVerse(
                currentIndex: 1,
                currentScrollOffset: 45,
                anchors,
                direction: 1,
                maxScroll: 80,
                out int targetIndex,
                out double nextOffset);

            Assert.True(moved);
            Assert.Equal(2, targetIndex);
            Assert.Equal(80, nextOffset);
        }

        [Fact]
        public void TryMoveHighlightedVerse_WhenNoCurrentIndex_UsesScrollOffsetAnchor()
        {
            double[] anchors = { 0, 45, 90 };

            bool moved = BiblePopupOverlayInputPolicy.TryMoveHighlightedVerse(
                currentIndex: -1,
                currentScrollOffset: 50,
                anchors,
                direction: -1,
                maxScroll: 90,
                out int targetIndex,
                out double nextOffset);

            Assert.True(moved);
            Assert.Equal(0, targetIndex);
            Assert.Equal(0, nextOffset);
        }

        [Fact]
        public void TryMoveHighlightedVerse_WhenAtBoundary_ReturnsFalse()
        {
            double[] anchors = { 0, 45, 90 };

            bool moved = BiblePopupOverlayInputPolicy.TryMoveHighlightedVerse(
                currentIndex: 0,
                currentScrollOffset: 0,
                anchors,
                direction: -1,
                maxScroll: 90,
                out int targetIndex,
                out double nextOffset);

            Assert.False(moved);
            Assert.Equal(0, targetIndex);
            Assert.Equal(0, nextOffset);
        }
    }
}
