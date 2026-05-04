using System;
using System.Collections.Generic;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Projection.Output;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace ImageColorChanger.Services.Ndi
{
    /// <summary>
    /// NDI 传输协调层：
    /// 每个通道独立持有发送器与输出管理器，避免多业务互相覆盖。
    /// </summary>
    public sealed class NdiTransportCoordinator : INdiTransportCoordinator
    {
        private readonly ConfigManager _configManager;
        private readonly IServiceProvider _services;
        private readonly object _sync = new();
        private readonly Dictionary<NdiChannel, ProjectionNdiOutputManager> _channelManagers = new();

        public NdiTransportCoordinator(
            ConfigManager configManager,
            IServiceProvider services)
        {
            _configManager = configManager;
            _services = services;
        }

        public NdiChannelOutputConfig GetChannelConfig(NdiChannel channel)
        {
            // 阶段A：配置仍兼容旧字段，先提供统一读模型。
            string projectionSender = ResolveSenderName(channel);
            int projectionWidth = _configManager?.ProjectionNdiWidth ?? 1920;
            int projectionHeight = _configManager?.ProjectionNdiHeight ?? 1080;
            int projectionFps = _configManager?.ProjectionNdiFps ?? 30;
            bool projectionPreferAlpha = _configManager?.ProjectionNdiPreferAlpha ?? true;

            bool enabled = channel switch
            {
                NdiChannel.Caption => _configManager?.LiveCaptionNdiEnabled ?? false,
                _ => _configManager?.ProjectionNdiEnabled ?? false
            };

            return new NdiChannelOutputConfig
            {
                Channel = channel,
                Enabled = enabled,
                SenderName = projectionSender,
                Width = projectionWidth,
                Height = projectionHeight,
                Fps = projectionFps,
                PreferAlpha = projectionPreferAlpha
            };
        }

        public bool PublishFrame(NdiChannel channel, SKBitmap frame, ProjectionNdiContentType contentType, SKColor? transparencyKeyColor = null)
        {
            var manager = GetOrCreateManager(channel);
            bool sent = manager?.PublishFrame(frame, contentType, transparencyKeyColor) == true;
            if (!sent)
            {
                ProjectionNdiDiagnostics.Log($"PublishFrame failed: channel={channel}, contentType={contentType}");
            }
            return sent;
        }

        public bool PublishFrameDirect(NdiChannel channel, SKBitmap frame, bool transparent = false, SKColor? transparencyKeyColor = null)
        {
            var manager = GetOrCreateManager(channel);
            bool sent = manager?.PublishFrameDirect(frame, transparent, transparencyKeyColor) == true;
            if (!sent)
            {
                ProjectionNdiDiagnostics.Log($"PublishFrameDirect failed: channel={channel}, transparent={transparent}");
            }
            return sent;
        }

        public void PushTransparentIdleFrame(NdiChannel channel, bool startSenderIfNeeded = true)
        {
            var manager = GetOrCreateManager(channel);
            manager?.PushTransparentIdleFrame(startSenderIfNeeded);
            var cfg = GetChannelConfig(channel);
            ProjectionNdiDiagnostics.Log(
                $"IdleFrame pushed: channel={channel}, sender={cfg.SenderName}, size={cfg.Width}x{cfg.Height}, watermark=\"{_configManager?.ProjectionNdiIdleFrameWatermarkText}\"");
        }

        public void StopChannel(NdiChannel channel)
        {
            lock (_sync)
            {
                if (_channelManagers.TryGetValue(channel, out var manager))
                {
                    manager.Stop();
                    _channelManagers.Remove(channel);
                }
            }
        }

        public void StopAll()
        {
            lock (_sync)
            {
                foreach (var manager in _channelManagers.Values)
                {
                    manager.Stop();
                }

                _channelManagers.Clear();
            }
        }

        public int GetConnectionCount(NdiChannel channel)
        {
            lock (_sync)
            {
                return _channelManagers.TryGetValue(channel, out var manager)
                    ? manager.GetClientConnectionCount()
                    : 0;
            }
        }

        private ProjectionNdiOutputManager GetOrCreateManager(NdiChannel channel)
        {
            lock (_sync)
            {
                if (_channelManagers.TryGetValue(channel, out var existing))
                {
                    return existing;
                }

                var provider = new ChannelProjectionNdiConfigProvider(_configManager, channel, ResolveSenderName(channel));
                var manager = new ProjectionNdiOutputManager(
                    provider,
                    new ProjectionNdiModeResolver(provider),
                    CreateSenderInstance());

                _channelManagers[channel] = manager;
                ProjectionNdiDiagnostics.Log($"Channel manager created: channel={channel}, sender={provider.ProjectionNdiSenderName}");
                return manager;
            }
        }

        private IProjectionNdiSender CreateSenderInstance()
        {
            var prototype = _services.GetRequiredService<IProjectionNdiSender>();
            return prototype switch
            {
                NoopProjectionNdiSender => new NoopProjectionNdiSender(),
                _ => new NativeProjectionNdiSender()
            };
        }

        private string ResolveSenderName(NdiChannel channel)
        {
            string channelLabel = channel switch
            {
                NdiChannel.Media => "媒体",
                NdiChannel.Slide => "投影",
                NdiChannel.Bible => "圣经",
                NdiChannel.Lyrics => "歌词",
                NdiChannel.Caption => "字幕",
                NdiChannel.Video => "视频",
                NdiChannel.Watermark => "水印",
                NdiChannel.Transparent => "透明",
                _ => "NDI"
            };

            // 名称尽量短，避免接收端列表截断；局域网内若存在重名冲突，再考虑加机名后缀。
            return channelLabel;
        }

        private sealed class ChannelProjectionNdiConfigProvider : IProjectionNdiConfigProvider
        {
            private readonly ConfigManager _config;
            private readonly NdiChannel _channel;
            private readonly string _senderName;

            public ChannelProjectionNdiConfigProvider(ConfigManager config, NdiChannel channel, string senderName)
            {
                _config = config;
                _channel = channel;
                _senderName = senderName;
            }

            public bool ProjectionNdiEnabled => _channel switch
            {
                NdiChannel.Caption => _config?.LiveCaptionNdiEnabled == true,
                _ => _config?.ProjectionNdiEnabled == true
            };

            public string ProjectionNdiSenderName => _senderName;
            public int ProjectionNdiWidth => _config?.ProjectionNdiWidth ?? 1920;
            public int ProjectionNdiHeight => _config?.ProjectionNdiHeight ?? 1080;
            public int ProjectionNdiFps => _config?.ProjectionNdiFps ?? 30;
            public bool ProjectionNdiPreferAlpha => _config?.ProjectionNdiPreferAlpha ?? true;
            public bool ProjectionNdiLyricsTransparentEnabled => _config?.ProjectionNdiLyricsTransparentEnabled ?? true;
            public bool ProjectionNdiBibleTransparentEnabled => _config?.ProjectionNdiBibleTransparentEnabled ?? true;
            public string ProjectionNdiIdleFrameWatermarkText =>
                _channel == NdiChannel.Watermark
                    ? (_config?.ProjectionNdiIdleFrameWatermarkText ?? string.Empty)
                    : string.Empty;
            public string ProjectionNdiIdleFrameWatermarkPosition => _config?.ProjectionNdiIdleFrameWatermarkPosition ?? "RightBottom";
            public double ProjectionNdiIdleFrameWatermarkFontSize => _config?.ProjectionNdiIdleFrameWatermarkFontSize ?? 48d;
            public string ProjectionNdiIdleFrameWatermarkFontFamily => _config?.ProjectionNdiIdleFrameWatermarkFontFamily ?? "Microsoft YaHei UI";
            public double ProjectionNdiIdleFrameWatermarkOpacity => _config?.ProjectionNdiIdleFrameWatermarkOpacity ?? 43d;
        }
    }
}
