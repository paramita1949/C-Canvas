using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionDebugLoggerTests
    {
        [Fact]
        public void Enabled_IsTrueInDebugBuildUnlessExplicitlyDisabled()
        {
#if DEBUG
            Assert.True(LiveCaptionDebugLogger.Enabled);
#else
            Assert.False(LiveCaptionDebugLogger.Enabled);
#endif
        }
    }
}
