using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class BibleTextInsertConfigTests
    {
        [Fact]
        public void PopupParameters_DefaultValues_AreExpected()
        {
            var config = new BibleTextInsertConfig();

            Assert.Equal(3, config.PopupDurationMinutes);
            Assert.Equal(3, config.PopupVerseCount);
        }

        [Theory]
        [InlineData(-10, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(6, 6)]
        [InlineData(10, 10)]
        [InlineData(11, 10)]
        public void PopupDurationMinutes_IsClampedToRange1To10(int input, int expected)
        {
            var config = new BibleTextInsertConfig
            {
                PopupDurationMinutes = input
            };

            Assert.Equal(expected, config.PopupDurationMinutes);
        }

        [Theory]
        [InlineData(-5, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(4, 4)]
        [InlineData(10, 10)]
        [InlineData(15, 10)]
        public void PopupVerseCount_IsClampedToRange1To10(int input, int expected)
        {
            var config = new BibleTextInsertConfig
            {
                PopupVerseCount = input
            };

            Assert.Equal(expected, config.PopupVerseCount);
        }
    }
}
