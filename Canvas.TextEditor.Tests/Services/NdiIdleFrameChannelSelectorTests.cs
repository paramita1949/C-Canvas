using System.Linq;
using ImageColorChanger.Services.Ndi;

namespace Canvas.TextEditor.Tests.Services
{
    public sealed class NdiIdleFrameChannelSelectorTests
    {
        [Fact]
        public void SelectChannels_IncludesTransparent_WhenTransparentEnabledAndSlideDisabled()
        {
            var channels = NdiIdleFrameChannelSelector.SelectChannels(
                    slideEnabled: false,
                    transparentEnabled: true,
                    captionEnabled: false,
                    videoEnabled: false,
                    watermarkEnabled: true)
                .ToArray();

            Assert.Contains(NdiChannel.Transparent, channels);
            Assert.Contains(NdiChannel.Watermark, channels);
            Assert.DoesNotContain(NdiChannel.Slide, channels);
        }
    }
}
