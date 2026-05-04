namespace ImageColorChanger.Services.Ndi
{
    public static class NdiFeatureFlags
    {
        public const bool EnableSlide = false;
        public const bool EnableVideo = false;
        public const bool EnableCaption = true;
        public const bool EnableWatermark = true;
        public const bool EnableTransparent = true;

        public static bool IsChannelEnabled(NdiChannel channel)
        {
            return channel switch
            {
                NdiChannel.Slide => EnableSlide,
                NdiChannel.Video => EnableVideo,
                NdiChannel.Caption => EnableCaption,
                NdiChannel.Watermark => EnableWatermark,
                NdiChannel.Transparent => EnableTransparent,
                _ => false
            };
        }
    }
}
