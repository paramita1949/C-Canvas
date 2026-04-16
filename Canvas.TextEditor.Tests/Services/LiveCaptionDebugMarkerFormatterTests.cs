using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionDebugMarkerFormatterTests
    {
        [Theory]
        [InlineData(false, false, "")]
        [InlineData(true, false, "[实时语音]")]
        [InlineData(false, true, "【实时短语】")]
        [InlineData(true, true, "[实时语音]【实时短语】")]
        public void Format_ReturnsExpectedText(bool realtimeEnabled, bool shortPhraseEnabled, string expected)
        {
            string actual = LiveCaptionDebugMarkerFormatter.Format(realtimeEnabled, shortPhraseEnabled);
            Assert.Equal(expected, actual);
        }
    }
}
