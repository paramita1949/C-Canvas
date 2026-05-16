using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionDebugLoggerTests
    {
        [Fact]
        public void IsEnabledForEnvironment_DefaultsToFalseEvenInDebugBuild()
        {
            Assert.False(LiveCaptionDebugLogger.IsEnabledForEnvironment(null, debugBuild: true));
            Assert.False(LiveCaptionDebugLogger.IsEnabledForEnvironment(string.Empty, debugBuild: true));
            Assert.False(LiveCaptionDebugLogger.IsEnabledForEnvironment("0", debugBuild: true));
        }

        [Fact]
        public void IsEnabledForEnvironment_RequiresExplicitOptIn()
        {
            Assert.True(LiveCaptionDebugLogger.IsEnabledForEnvironment("1", debugBuild: true));
            Assert.False(LiveCaptionDebugLogger.IsEnabledForEnvironment("1", debugBuild: false));
        }
    }
}
