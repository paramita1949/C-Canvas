using System.Text.Json;
using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class LyricsNdiConfigTests
    {
        [Fact]
        public void AppConfig_LyricsNdi_Defaults_AreExpected()
        {
            var config = new AppConfig();

            Assert.False(config.LyricsNdiEnabled);
            Assert.Equal("CanvasCast-Lyrics", config.LyricsNdiSenderName);
            Assert.Equal(1920, config.LyricsNdiWidth);
            Assert.Equal(1080, config.LyricsNdiHeight);
            Assert.Equal(30, config.LyricsNdiFps);
            Assert.True(config.LyricsNdiPreferAlpha);

            Assert.False(config.ProjectionNdiEnabled);
            Assert.Equal("CanvasCast-Projection", config.ProjectionNdiSenderName);
            Assert.True(config.ProjectionNdiLyricsTransparentEnabled);
            Assert.True(config.ProjectionNdiBibleTransparentEnabled);
        }

        [Fact]
        public void AppConfig_LyricsNdi_JsonRoundTrip_Works()
        {
            const string json = """
            {
              "LyricsNdiEnabled": true,
              "LyricsNdiSenderName": "Stage-Lyrics",
              "LyricsNdiWidth": 1280,
              "LyricsNdiHeight": 720,
              "LyricsNdiFps": 60,
              "LyricsNdiPreferAlpha": false
            }
            """;

            var config = JsonSerializer.Deserialize<AppConfig>(json);

            Assert.NotNull(config);
            Assert.True(config.LyricsNdiEnabled);
            Assert.Equal("Stage-Lyrics", config.LyricsNdiSenderName);
            Assert.Equal(1280, config.LyricsNdiWidth);
            Assert.Equal(720, config.LyricsNdiHeight);
            Assert.Equal(60, config.LyricsNdiFps);
            Assert.False(config.LyricsNdiPreferAlpha);
        }
    }
}
