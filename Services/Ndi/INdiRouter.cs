using SkiaSharp;

namespace ImageColorChanger.Services.Ndi
{
    public interface INdiRouter
    {
        bool IsChannelEnabled(NdiChannel channel);
        void SetChannelEnabled(NdiChannel channel, bool enabled);
        bool PublishLyricsFrame(SKBitmap frame, SKBitmap transparentFrame, bool transparentEnabled, bool transparentLyricsMode, SKColor backgroundColor);
        void PushLyricsTransparentIdleFrame();
        bool PublishCaptionFrame(SKBitmap frame);
        void PushCaptionIdleFrame();
        void StopAll();
        int GetConnectionCount();
    }
}
