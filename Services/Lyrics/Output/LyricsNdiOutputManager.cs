using SkiaSharp;

namespace ImageColorChanger.Services.Lyrics.Output
{
    /// <summary>
    /// NDI 歌词输出编排器：负责读取配置、管理 sender 生命周期、发送帧。
    /// </summary>
    public sealed class LyricsNdiOutputManager
    {
        private readonly ILyricsNdiConfigProvider _configProvider;
        private readonly ILyricsNdiSender _sender;

        public LyricsNdiOutputManager(ILyricsNdiConfigProvider configProvider, ILyricsNdiSender sender)
        {
            _configProvider = configProvider;
            _sender = sender;
        }

        public bool PublishFrame(SKBitmap frame)
        {
            if (!_configProvider.LyricsNdiEnabled || frame == null)
            {
                return false;
            }

            if (!_sender.IsRunning)
            {
                bool started = _sender.Start(CreateOptions());
                if (!started)
                {
                    return false;
                }
            }

            return _sender.SendFrame(frame);
        }

        public void Stop()
        {
            _sender.Stop();
        }

        private LyricsNdiOutputOptions CreateOptions()
        {
            return new LyricsNdiOutputOptions
            {
                SenderName = _configProvider.LyricsNdiSenderName,
                Width = _configProvider.LyricsNdiWidth,
                Height = _configProvider.LyricsNdiHeight,
                Fps = _configProvider.LyricsNdiFps,
                PreferAlpha = _configProvider.LyricsNdiPreferAlpha
            };
        }
    }
}
