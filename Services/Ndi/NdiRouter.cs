using System;
using System.Collections.Generic;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Projection.Output;
using SkiaSharp;

namespace ImageColorChanger.Services.Ndi
{
    public sealed class NdiRouter : INdiRouter
    {
        private readonly ConfigManager _configManager;
        private readonly INdiTransportCoordinator _ndiTransportCoordinator;
        private readonly Dictionary<NdiChannel, bool> _channelStates = new();

        public NdiRouter(ConfigManager configManager, INdiTransportCoordinator ndiTransportCoordinator)
        {
            _configManager = configManager;
            _ndiTransportCoordinator = ndiTransportCoordinator;
            _channelStates[NdiChannel.Slide] = _configManager?.ProjectionNdiSlideEnabled ?? NdiFeatureFlags.EnableSlide;
            _channelStates[NdiChannel.Video] = NdiFeatureFlags.EnableVideo;
            _channelStates[NdiChannel.Caption] = _configManager?.LiveCaptionNdiEnabled ?? NdiFeatureFlags.EnableCaption;
            _channelStates[NdiChannel.Watermark] = NdiFeatureFlags.EnableWatermark;
            _channelStates[NdiChannel.Transparent] = NdiFeatureFlags.EnableTransparent;
        }

        public bool IsChannelEnabled(NdiChannel channel)
        {
            return _channelStates.TryGetValue(channel, out bool enabled)
                ? enabled
                : NdiFeatureFlags.IsChannelEnabled(channel);
        }

        public void SetChannelEnabled(NdiChannel channel, bool enabled)
        {
            _channelStates[channel] = enabled;

            if (_configManager == null)
            {
                return;
            }

            switch (channel)
            {
                case NdiChannel.Slide:
                    _configManager.ProjectionNdiSlideEnabled = enabled;
                    break;
                case NdiChannel.Caption:
                    _configManager.LiveCaptionNdiEnabled = enabled;
                    break;
            }
        }

        public bool PublishLyricsFrame(SKBitmap frame, SKBitmap transparentFrame, bool transparentEnabled, bool transparentLyricsMode, SKColor backgroundColor)
        {
            if (!IsChannelEnabled(NdiChannel.Slide) || _ndiTransportCoordinator == null || frame == null)
            {
                ProjectionNdiDiagnostics.Log(
                    $"LyricsNDI router skipped: slideEnabled={IsChannelEnabled(NdiChannel.Slide)}, transportNull={_ndiTransportCoordinator == null}, frameNull={frame == null}");
                return false;
            }

            if (!(_configManager?.ProjectionNdiEnabled ?? false))
            {
                ProjectionNdiDiagnostics.Log("LyricsNDI router skipped: ProjectionNdiEnabled=false");
                return false;
            }

            if (!transparentLyricsMode)
            {
                // 普通歌词路径：始终走投影主通道；若透明通道启用，仅补一帧透明空帧防残留。
                bool sent = _ndiTransportCoordinator.PublishFrame(NdiChannel.Slide, frame, ProjectionNdiContentType.Slide);
                if (transparentEnabled)
                {
                    _ndiTransportCoordinator.PushTransparentIdleFrame(NdiChannel.Transparent);
                }
                ProjectionNdiDiagnostics.Log(
                    $"LyricsNDI router publish: path=SlideNormal, sent={sent}, transparentEnabled={transparentEnabled}, frame={frame.Width}x{frame.Height}");

                return sent;
            }

            // 透明歌词模式：同时输出常规投影源 + 透明源，接收端可按需选择。
            bool fullFrameSent = _ndiTransportCoordinator.PublishFrame(NdiChannel.Slide, frame, ProjectionNdiContentType.Slide);
            bool transparentFrameSent = _ndiTransportCoordinator.PublishFrame(
                NdiChannel.Transparent,
                transparentFrame ?? frame,
                ProjectionNdiContentType.Lyrics,
                backgroundColor);
            ProjectionNdiDiagnostics.Log(
                $"LyricsNDI router publish: path=TransparentDual, fullSent={fullFrameSent}, transparentSent={transparentFrameSent}, frame={frame.Width}x{frame.Height}");
            return fullFrameSent || transparentFrameSent;
        }

        public void PushLyricsTransparentIdleFrame()
        {
            if (!IsChannelEnabled(NdiChannel.Slide) || _ndiTransportCoordinator == null)
            {
                return;
            }

            if (!(_configManager?.ProjectionNdiEnabled ?? false))
            {
                return;
            }

            _ndiTransportCoordinator.PushTransparentIdleFrame(NdiChannel.Transparent);
        }

        public bool PublishCaptionFrame(SKBitmap frame)
        {
            if (!IsChannelEnabled(NdiChannel.Caption) || _ndiTransportCoordinator == null || frame == null)
            {
                return false;
            }

            if (!(_configManager?.LiveCaptionNdiEnabled ?? false))
            {
                return false;
            }

            return _ndiTransportCoordinator.PublishFrameDirect(NdiChannel.Caption, frame, transparent: false, transparencyKeyColor: null);
        }

        public void PushCaptionIdleFrame()
        {
            if (!IsChannelEnabled(NdiChannel.Caption) || _ndiTransportCoordinator == null)
            {
                return;
            }

            _ndiTransportCoordinator.PushTransparentIdleFrame(NdiChannel.Caption, startSenderIfNeeded: false);
        }

        public void StopAll()
        {
            _ndiTransportCoordinator?.StopAll();
        }

        public int GetConnectionCount() => _ndiTransportCoordinator?.GetConnectionCount(NdiChannel.Slide) ?? 0;
    }
}
