using ImageColorChanger.Services.Projection.Output;
using SkiaSharp;

namespace ImageColorChanger.Services.Ndi
{
    public interface INdiTransportCoordinator
    {
        NdiChannelOutputConfig GetChannelConfig(NdiChannel channel);
        bool PublishFrame(NdiChannel channel, SKBitmap frame, ProjectionNdiContentType contentType, SKColor? transparencyKeyColor = null);
        bool PublishFrameDirect(NdiChannel channel, SKBitmap frame, bool transparent = false, SKColor? transparencyKeyColor = null);
        bool PublishAudio(ProjectionNdiAudioFrame audioFrame);
        void PushTransparentIdleFrame(NdiChannel channel, bool startSenderIfNeeded = true);
        void StopChannel(NdiChannel channel);
        void StopAll();
        int GetConnectionCount(NdiChannel channel);
    }
}
