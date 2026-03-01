using SkiaSharp;

namespace ImageColorChanger.Services.Projection.Output
{
    /// <summary>
    /// 占位发送器：用于先打通架构，后续替换真实 NDI SDK。
    /// </summary>
    public sealed class NoopProjectionNdiSender : IProjectionNdiSender
    {
        public bool IsRunning { get; private set; }

        public bool Start(ProjectionNdiOutputOptions options)
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

