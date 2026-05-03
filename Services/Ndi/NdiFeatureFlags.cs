namespace ImageColorChanger.Services.Ndi
{
    public static class NdiFeatureFlags
    {
        public const bool EnableMedia = false;
        public const bool EnableSlide = false;
        public const bool EnableBible = false;
        public const bool EnableVideo = false;
        public const bool EnableLyrics = true;
        public const bool EnableCaption = true;

        public static bool IsChannelEnabled(NdiChannel channel)
        {
            return channel switch
            {
                NdiChannel.Media => EnableMedia,
                NdiChannel.Slide => EnableSlide,
                NdiChannel.Bible => EnableBible,
                NdiChannel.Video => EnableVideo,
                NdiChannel.Lyrics => EnableLyrics,
                NdiChannel.Caption => EnableCaption,
                _ => false
            };
        }
    }
}
