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
    }
}
