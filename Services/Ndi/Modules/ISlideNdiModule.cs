using SkiaSharp;

namespace ImageColorChanger.Services.Ndi.Modules
{
    public interface ISlideNdiModule
    {
        bool IsEnabled();
        bool PublishSlideFrame(SKBitmap frame, bool transparentOutput = false, SKColor? transparencyKeyColor = null);
        void PushIdleFrame();
    }
}

