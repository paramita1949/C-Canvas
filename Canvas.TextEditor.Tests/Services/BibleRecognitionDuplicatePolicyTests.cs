using ImageColorChanger.Services;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BibleRecognitionDuplicatePolicyTests
    {
        [Theory]
        [InlineData(50, 4, 4, 4, 50, 4, 4, 4, true)]
        [InlineData(50, 4, 4, 4, 50, 4, 4, 5, false)]
        [InlineData(50, 4, 4, 4, 50, 4, 0, 0, false)]
        public void IsDuplicateReference_ReturnsExpected(
            int prevBook,
            int prevChapter,
            int prevStart,
            int prevEnd,
            int currBook,
            int currChapter,
            int currStart,
            int currEnd,
            bool expected)
        {
            var previous = new BibleSpeechReference(prevBook, prevChapter, prevStart, prevEnd);
            var current = new BibleSpeechReference(currBook, currChapter, currStart, currEnd);

            bool actual = BibleRecognitionDuplicatePolicy.IsDuplicateReference(previous, current);
            Assert.Equal(expected, actual);
        }
    }
}
