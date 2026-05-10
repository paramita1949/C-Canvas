using SkiaSharp;

namespace ImageColorChanger.Services.Projection.Output
{
    public interface IProjectionNdiSender
    {
        bool IsRunning { get; }
        bool Start(ProjectionNdiOutputOptions options);
        bool SendFrame(SKBitmap frame);
        bool SendAudio(ProjectionNdiAudioFrame audioFrame);
        void Stop();
    }
}

