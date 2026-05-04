using ImageColorChanger.Core;
using ImageColorChanger.Services.Projection.Output;
using SkiaSharp;

namespace ImageColorChanger.Services.Ndi.Modules
{
    public sealed class SlideNdiModule : ISlideNdiModule
    {
        private readonly ConfigManager _configManager;
        private readonly INdiRouter _ndiRouter;
        private readonly INdiTransportCoordinator _ndiTransportCoordinator;

        public SlideNdiModule(
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

        public bool PublishSlideFrame(SKBitmap frame, bool transparentOutput = false, SKColor? transparencyKeyColor = null)
        {
            if (!IsEnabled() || frame == null || _ndiTransportCoordinator == null)
            {
                return false;
            }

            var contentType = transparentOutput
                ? ProjectionNdiContentType.SlideTransparent
                : ProjectionNdiContentType.Slide;

            return _ndiTransportCoordinator.PublishFrame(
                transparentOutput ? NdiChannel.Transparent : NdiChannel.Slide,
                frame,
                contentType,
                transparencyKeyColor);
        }

        public void PushIdleFrame()
        {
            if (!(_configManager?.ProjectionNdiEnabled ?? false))
            {
                return;
            }

            _ndiTransportCoordinator?.PushTransparentIdleFrame(NdiChannel.Slide);
        }
    }
}
