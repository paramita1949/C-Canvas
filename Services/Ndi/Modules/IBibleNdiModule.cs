using SkiaSharp;

namespace ImageColorChanger.Services.Ndi.Modules
{
    public interface IBibleNdiModule
    {
        bool IsEnabled();
        bool PublishBibleFrame(SKBitmap frame, SKColor? transparencyKeyColor = null);
    }
}

