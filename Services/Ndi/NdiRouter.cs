using ImageColorChanger.Core;
using ImageColorChanger.Services.Projection.Output;
using SkiaSharp;

namespace ImageColorChanger.Services.Ndi
{
    public sealed class NdiRouter : INdiRouter
    {
        private readonly ConfigManager _configManager;
        private readonly ProjectionNdiOutputManager _projectionNdiOutputManager;
        private bool _lyricsEnabled = NdiFeatureFlags.EnableLyrics;
        private bool _captionEnabled = NdiFeatureFlags.EnableCaption;

        public NdiRouter(ConfigManager configManager, ProjectionNdiOutputManager projectionNdiOutputManager)
        {
            _configManager = configManager;
            _projectionNdiOutputManager = projectionNdiOutputManager;
        }

        public bool IsChannelEnabled(NdiChannel channel)
        {
            return channel switch
            {
                NdiChannel.Lyrics => _lyricsEnabled,
                NdiChannel.Caption => _captionEnabled,
                _ => NdiFeatureFlags.IsChannelEnabled(channel)
            };
        }

        public void SetChannelEnabled(NdiChannel channel, bool enabled)
        {
            switch (channel)
            {
                case NdiChannel.Lyrics:
                    _lyricsEnabled = enabled;
                    break;
                case NdiChannel.Caption:
                    _captionEnabled = enabled;
                    break;
            }
        }

        public bool PublishLyricsFrame(SKBitmap frame, bool transparentEnabled, bool transparentLyricsMode, SKColor backgroundColor)
        {
            if (!IsChannelEnabled(NdiChannel.Lyrics) || _projectionNdiOutputManager == null || frame == null)
            {
                return false;
            }

            if (!(_configManager?.ProjectionNdiEnabled ?? false))
            {
                return false;
            }

            if (transparentEnabled && !transparentLyricsMode)
            {
                _projectionNdiOutputManager.PushTransparentIdleFrame();
                return true;
            }

            return transparentLyricsMode
                ? _projectionNdiOutputManager.PublishFrame(frame, ProjectionNdiContentType.Lyrics, backgroundColor)
                : _projectionNdiOutputManager.PublishFrame(frame, ProjectionNdiContentType.Slide);
        }

        public void PushLyricsTransparentIdleFrame()
        {
            if (!IsChannelEnabled(NdiChannel.Lyrics) || _projectionNdiOutputManager == null)
            {
                return;
            }

            if (!(_configManager?.ProjectionNdiEnabled ?? false))
            {
                return;
            }

            _projectionNdiOutputManager.PushTransparentIdleFrame();
        }

        public bool PublishCaptionFrame(SKBitmap frame)
        {
            if (!IsChannelEnabled(NdiChannel.Caption) || _projectionNdiOutputManager == null || frame == null)
            {
                return false;
            }

            if (!(_configManager?.LiveCaptionNdiEnabled ?? false))
            {
                return false;
            }

            return _projectionNdiOutputManager.PublishFrameDirect(frame, transparent: false, transparencyKeyColor: null);
        }

        public void PushCaptionIdleFrame()
        {
            if (!IsChannelEnabled(NdiChannel.Caption) || _projectionNdiOutputManager == null)
            {
                return;
            }

            _projectionNdiOutputManager.PushTransparentIdleFrame(startSenderIfNeeded: false);
        }

        public void StopAll()
        {
            _projectionNdiOutputManager?.Stop();
        }

        public int GetConnectionCount() => _projectionNdiOutputManager?.GetClientConnectionCount() ?? 0;
    }
}
