using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class FontSizeControlValueTests
    {
        [Theory]
        [InlineData(13.24, 13.0)]
        [InlineData(13.25, 13.5)]
        [InlineData(13.74, 13.5)]
        [InlineData(13.75, 14.0)]
        public void SnapToStep_RoundsToNearestHalfPoint(double value, double expected)
        {
            double actual = FontSizeControlValue.SnapToStep(value, 8, 40);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(5.0, 8.0)]
        [InlineData(45.0, 40.0)]
        public void SnapToStep_ClampsToRange(double value, double expected)
        {
            double actual = FontSizeControlValue.SnapToStep(value, 8, 40);

            Assert.Equal(expected, actual);
        }
    }
}
