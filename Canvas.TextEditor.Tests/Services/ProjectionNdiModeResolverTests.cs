using ImageColorChanger.Services.Projection.Output;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class ProjectionNdiModeResolverTests
    {
        [Fact]
        public void Resolve_WhenDisabled_ReturnsDisabled()
        {
            var resolver = new ProjectionNdiModeResolver(new FakeConfig { ProjectionNdiEnabled = false });

            Assert.Equal(ProjectionNdiTransmissionMode.Disabled, resolver.Resolve(ProjectionNdiContentType.Lyrics));
            Assert.Equal(ProjectionNdiTransmissionMode.Disabled, resolver.Resolve(ProjectionNdiContentType.Image));
        }

        [Fact]
        public void Resolve_WhenEnabled_TextCanBeTransparent_OthersFullFrame()
        {
            var resolver = new ProjectionNdiModeResolver(new FakeConfig
            {
                ProjectionNdiEnabled = true,
                ProjectionNdiLyricsTransparentEnabled = true,
                ProjectionNdiBibleTransparentEnabled = false
            });

            Assert.Equal(ProjectionNdiTransmissionMode.Transparent, resolver.Resolve(ProjectionNdiContentType.Lyrics));
            Assert.Equal(ProjectionNdiTransmissionMode.FullFrame, resolver.Resolve(ProjectionNdiContentType.Bible));
            Assert.Equal(ProjectionNdiTransmissionMode.FullFrame, resolver.Resolve(ProjectionNdiContentType.Slide));
            Assert.Equal(ProjectionNdiTransmissionMode.FullFrame, resolver.Resolve(ProjectionNdiContentType.Image));
            Assert.Equal(ProjectionNdiTransmissionMode.FullFrame, resolver.Resolve(ProjectionNdiContentType.Video));
        }

        private sealed class FakeConfig : IProjectionNdiConfigProvider
        {
            public bool ProjectionNdiEnabled { get; set; }
            public string ProjectionNdiSenderName { get; set; } = "CanvasCast-Projection";
            public int ProjectionNdiWidth { get; set; } = 1920;
            public int ProjectionNdiHeight { get; set; } = 1080;
            public int ProjectionNdiFps { get; set; } = 30;
            public bool ProjectionNdiPreferAlpha { get; set; } = true;
            public bool ProjectionNdiLyricsTransparentEnabled { get; set; } = true;
            public bool ProjectionNdiBibleTransparentEnabled { get; set; } = true;
            public string ProjectionNdiIdleFrameWatermarkText { get; set; } = string.Empty;
            public string ProjectionNdiIdleFrameWatermarkPosition { get; set; } = "RightBottom";
            public double ProjectionNdiIdleFrameWatermarkFontSize { get; set; } = 48.0;
            public string ProjectionNdiIdleFrameWatermarkFontFamily { get; set; } = "Microsoft YaHei UI";
            public double ProjectionNdiIdleFrameWatermarkOpacity { get; set; } = 43.0;
            public bool ProjectionNdiAudioEnabled { get; set; }
            public string ProjectionNdiAudioSourceMode { get; set; } = "system";
            public string ProjectionNdiAudioInputDeviceId { get; set; } = string.Empty;
            public string ProjectionNdiAudioSystemDeviceId { get; set; } = string.Empty;
        }
    }
}

