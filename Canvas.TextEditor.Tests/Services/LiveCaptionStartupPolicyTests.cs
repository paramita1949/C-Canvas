using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionStartupPolicyTests
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        public void ShouldAutoStartRecognition_ReturnsExpected(
            bool realtimeEnabled,
            bool shortPhraseEnabled,
            bool expected)
        {
            bool actual = LiveCaptionStartupPolicy.ShouldAutoStartRecognition(realtimeEnabled, shortPhraseEnabled);
            Assert.Equal(expected, actual);
        }
    }
}
