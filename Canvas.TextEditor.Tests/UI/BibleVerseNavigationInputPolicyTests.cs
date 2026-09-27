using ImageColorChanger.UI.Modules;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class BibleVerseNavigationInputPolicyTests
    {
        [Theory]
        [InlineData(-3, -1)]
        [InlineData(-1, -1)]
        [InlineData(1, 1)]
        [InlineData(5, 1)]
        public void NormalizeDirection_WhenDirectionHasSign_ReturnsScrollStep(int direction, int expected)
        {
            int actual = BibleVerseNavigationInputPolicy.NormalizeDirection(direction);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void NormalizeDirection_WhenDirectionIsZero_ReturnsZero()
        {
            int actual = BibleVerseNavigationInputPolicy.NormalizeDirection(0);

            Assert.Equal(0, actual);
        }

        [Theory]
        [InlineData(20d, 0d, 110d, 130d)]
        [InlineData(20d, 0.5d, 110d, 130d)]
        [InlineData(20d, 96d, 110d, 116d)]
        public void AccumulateVerseHeight_UsesEstimateUntilContainerIsLaidOut(
            double currentOffset,
            double measuredHeight,
            double estimatedHeight,
            double expected)
        {
            Assert.Equal(
                expected,
                BibleVerseNavigationInputPolicy.AccumulateVerseHeight(currentOffset, measuredHeight, estimatedHeight));
        }
    }
}
