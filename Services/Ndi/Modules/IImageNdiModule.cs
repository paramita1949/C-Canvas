using SkiaSharp;

namespace ImageColorChanger.Services.Ndi.Modules
{
    public interface IImageNdiModule
    {
        bool IsEnabled();
        bool PublishImageFrame(SKBitmap frame);
    }
}

