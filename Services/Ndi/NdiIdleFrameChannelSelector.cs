using System.Collections.Generic;

namespace ImageColorChanger.Services.Ndi
{
    public static class NdiIdleFrameChannelSelector
    {
        public static IEnumerable<NdiChannel> SelectChannels(
            bool slideEnabled,
            bool transparentEnabled,
            bool captionEnabled,
            bool videoEnabled,
            bool watermarkEnabled)
        {
            if (slideEnabled)
            {
                yield return NdiChannel.Slide;
            }

            if (transparentEnabled)
            {
                yield return NdiChannel.Transparent;
            }

            if (captionEnabled)
            {
                yield return NdiChannel.Caption;
            }

            if (videoEnabled)
            {
                yield return NdiChannel.Video;
            }

            if (watermarkEnabled)
            {
                yield return NdiChannel.Watermark;
            }
        }
    }
}
