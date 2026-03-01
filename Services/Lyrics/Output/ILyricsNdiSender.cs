using SkiaSharp;

namespace ImageColorChanger.Services.Lyrics.Output
{
    /// <summary>
    /// 抽象 NDI 发送端，便于后续接入真实 SDK。
    /// </summary>
    public interface ILyricsNdiSender
    {
        bool IsRunning { get; }
        bool Start(LyricsNdiOutputOptions options);
        bool SendFrame(SKBitmap frame);
        void Stop();
    }
}
