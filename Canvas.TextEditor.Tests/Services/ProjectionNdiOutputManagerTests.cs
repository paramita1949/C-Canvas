using ImageColorChanger.Services.Projection.Output;
using ImageColorChanger.Services.Ndi.Audio;
using SkiaSharp;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class ProjectionNdiOutputManagerTests
    {
        [Fact]
        public void PublishFrame_FullFrame_ForceOpaque()
        {
            var config = new FakeConfig();
            var resolver = new FixedResolver(ProjectionNdiTransmissionMode.FullFrame);
            var sender = new CaptureSender();
            var manager = new ProjectionNdiOutputManager(config, resolver, sender);
            using var frame = new SKBitmap(2, 1);
            frame.SetPixel(0, 0, new SKColor(10, 20, 30, 80));
            frame.SetPixel(1, 0, new SKColor(40, 50, 60, 255));

            var ok = manager.PublishFrame(frame, ProjectionNdiContentType.Slide);

            Assert.True(ok);
            Assert.NotNull(sender.LastFrame);
            Assert.Equal((byte)255, sender.LastFrame.GetPixel(0, 0).Alpha);
            Assert.Equal((byte)255, sender.LastFrame.GetPixel(1, 0).Alpha);
        }

        [Fact]
        public void PublishFrame_Transparent_ColorKeyToAlphaZero()
        {
            var config = new FakeConfig();
            var resolver = new FixedResolver(ProjectionNdiTransmissionMode.Transparent);
            var sender = new CaptureSender();
            var manager = new ProjectionNdiOutputManager(config, resolver, sender);
            using var frame = new SKBitmap(2, 1);
            frame.SetPixel(0, 0, new SKColor(0, 0, 0, 255));
            frame.SetPixel(1, 0, new SKColor(255, 255, 255, 255));

            var ok = manager.PublishFrame(frame, ProjectionNdiContentType.Lyrics, new SKColor(0, 0, 0, 255));

            Assert.True(ok);
            Assert.NotNull(sender.LastFrame);
            Assert.Equal((byte)0, sender.LastFrame.GetPixel(0, 0).Alpha);
            Assert.Equal((byte)255, sender.LastFrame.GetPixel(1, 0).Alpha);
        }

        [Fact]
        public void PublishAudio_ForwardsAudioFrame()
        {
            var config = new FakeConfig();
            var resolver = new FixedResolver(ProjectionNdiTransmissionMode.FullFrame);
            var sender = new CaptureSender();
            var manager = new ProjectionNdiOutputManager(config, resolver, sender);
            var audio = new ProjectionNdiAudioFrame(new float[] { 0.1f, 0.2f, 0.3f, 0.4f }, 48000, 2, 2);

            var ok = manager.PublishAudio(audio);

            Assert.True(ok);
            Assert.NotNull(sender.LastAudio);
            Assert.Equal(48000, sender.LastAudio.SampleRate);
            Assert.Equal(2, sender.LastAudio.ChannelCount);
            Assert.Equal(2, sender.LastAudio.SamplesPerChannel);
        }

        [Fact]
        public void NdiAudioSampleConverter_ConvertsStereoPcmToPlanarFloat()
        {
            byte[] pcm =
            {
                0x00, 0x40, // 0.5
                0x00, 0x80, // -1.0
                0x00, 0x20, // 0.25
                0x00, 0x00  // 0.0
            };

            bool ok = NdiAudioSampleConverter.TryConvertToPlanarFloat(
                pcm,
                pcm.Length,
                new NAudio.Wave.WaveFormat(48000, 16, 2),
                out var frame);

            Assert.True(ok);
            Assert.NotNull(frame);
            Assert.Equal(48000, frame.SampleRate);
            Assert.Equal(2, frame.ChannelCount);
            Assert.Equal(2, frame.SamplesPerChannel);
            Assert.Equal(0.5f, frame.PlanarSamples[0], 2);
            Assert.Equal(0.25f, frame.PlanarSamples[1], 2);
            Assert.Equal(-1.0f, frame.PlanarSamples[2], 2);
            Assert.Equal(0.0f, frame.PlanarSamples[3], 2);
        }

        private sealed class FakeConfig : IProjectionNdiConfigProvider
        {
            public bool ProjectionNdiEnabled => true;
            public string ProjectionNdiSenderName => "UT";
            public int ProjectionNdiWidth => 1920;
            public int ProjectionNdiHeight => 1080;
            public int ProjectionNdiFps => 30;
            public bool ProjectionNdiPreferAlpha => true;
            public bool ProjectionNdiLyricsTransparentEnabled => true;
            public bool ProjectionNdiBibleTransparentEnabled => true;
            public string ProjectionNdiIdleFrameWatermarkText => string.Empty;
            public string ProjectionNdiIdleFrameWatermarkPosition => "RightBottom";
            public double ProjectionNdiIdleFrameWatermarkFontSize => 48.0;
            public string ProjectionNdiIdleFrameWatermarkFontFamily => "Microsoft YaHei UI";
            public double ProjectionNdiIdleFrameWatermarkOpacity => 43.0;
            public bool ProjectionNdiAudioEnabled => false;
            public string ProjectionNdiAudioSourceMode => "system";
            public string ProjectionNdiAudioInputDeviceId => string.Empty;
            public string ProjectionNdiAudioSystemDeviceId => string.Empty;
        }

        private sealed class FixedResolver : IProjectionNdiModeResolver
        {
            private readonly ProjectionNdiTransmissionMode _mode;
            public FixedResolver(ProjectionNdiTransmissionMode mode) => _mode = mode;
            public ProjectionNdiTransmissionMode Resolve(ProjectionNdiContentType contentType) => _mode;
        }

        private sealed class CaptureSender : IProjectionNdiSender
        {
            public bool IsRunning { get; private set; }
            public SKBitmap LastFrame { get; private set; }
            public ProjectionNdiAudioFrame LastAudio { get; private set; }

            public bool Start(ProjectionNdiOutputOptions options)
            {
                IsRunning = true;
                return true;
            }

            public bool SendFrame(SKBitmap frame)
            {
                LastFrame?.Dispose();
                LastFrame = frame.Copy();
                return true;
            }

            public bool SendAudio(ProjectionNdiAudioFrame audioFrame)
            {
                LastAudio = audioFrame;
                return audioFrame != null && audioFrame.PlanarSamples.Length > 0;
            }

            public void Stop()
            {
                IsRunning = false;
                LastFrame?.Dispose();
                LastFrame = null;
                LastAudio = null;
            }
        }
    }
}

