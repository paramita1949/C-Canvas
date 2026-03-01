using SkiaSharp;

namespace ImageColorChanger.Services.Lyrics.Output
{
    /// <summary>
    /// 占位发送器：当前版本仅打通链路，不发送真实 NDI 数据。
    /// </summary>
    public sealed class NoopLyricsNdiSender : ILyricsNdiSender
    {
        public bool IsRunning { get; private set; }

        public bool Start(LyricsNdiOutputOptions options)
        {
            _ = options;
            IsRunning = true;
            return true;
        }

        public bool SendFrame(SKBitmap frame)
        {
            return IsRunning && frame != null;
        }

        public void Stop()
        {
            IsRunning = false;
        }
    }
}
