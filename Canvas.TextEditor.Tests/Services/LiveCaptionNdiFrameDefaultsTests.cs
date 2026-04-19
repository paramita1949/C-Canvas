using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionNdiFrameDefaultsTests
    {
        [Fact]
        public void TransparentBackground_MustKeepAlphaZero()
        {
            var c = LiveCaptionNdiFrameDefaults.TransparentBackground;

            Assert.Equal((byte)0, c.Alpha);
            Assert.Equal((byte)0, c.Red);
            Assert.Equal((byte)0, c.Green);
            Assert.Equal((byte)0, c.Blue);
        }
    }
}
