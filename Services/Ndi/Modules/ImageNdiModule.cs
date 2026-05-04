using ImageColorChanger.Core;
using ImageColorChanger.Services.Projection.Output;
using SkiaSharp;

namespace ImageColorChanger.Services.Ndi.Modules
{
    public sealed class ImageNdiModule : IImageNdiModule
    {
        private readonly ConfigManager _configManager;
        private readonly INdiRouter _ndiRouter;
        private readonly INdiTransportCoordinator _ndiTransportCoordinator;

        public ImageNdiModule(
            ConfigManager configManager,
            INdiRouter ndiRouter,
            INdiTransportCoordinator ndiTransportCoordinator)
        {
            _configManager = configManager;
            _ndiRouter = ndiRouter;
            _ndiTransportCoordinator = ndiTransportCoordinator;
        }

        public bool IsEnabled()
        {
            return (_configManager?.ProjectionNdiEnabled ?? false)
                   && (_ndiRouter?.IsChannelEnabled(NdiChannel.Slide) ?? false);
        }

        public bool PublishImageFrame(SKBitmap frame)
        {
            if (!IsEnabled() || frame == null || _ndiTransportCoordinator == null)
            {
                return false;
            }

            return _ndiTransportCoordinator.PublishFrame(
                NdiChannel.Slide,
                frame,
                ProjectionNdiContentType.Image);
        }
    }
}
