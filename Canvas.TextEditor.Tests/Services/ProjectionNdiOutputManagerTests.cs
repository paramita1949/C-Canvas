using ImageColorChanger.Services.Projection.Output;
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

            public void Stop()
            {
                IsRunning = false;
                LastFrame?.Dispose();
                LastFrame = null;
            }
        }
    }
}

