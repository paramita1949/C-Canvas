using System;
using System.IO;
using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class ProjectionNdiLegacyMigrationTests
    {
        [Fact]
        public void LoadConfig_MigratesLegacyLyricsNdi_ToProjectionNdi()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
            try
            {
                const string legacyJson = """
                {
                  "LyricsNdiEnabled": true,
                  "LyricsNdiSenderName": "Legacy-Lyrics",
                  "LyricsNdiWidth": 1280,
                  "LyricsNdiHeight": 720,
                  "LyricsNdiFps": 60,
                  "LyricsNdiPreferAlpha": false
                }
                """;
                File.WriteAllText(tempFile, legacyJson);

                var config = new ConfigManager(tempFile);

                Assert.True(config.ProjectionNdiEnabled);
                Assert.Equal("Legacy-Lyrics", config.ProjectionNdiSenderName);
                Assert.Equal(1280, config.ProjectionNdiWidth);
                Assert.Equal(720, config.ProjectionNdiHeight);
                Assert.Equal(60, config.ProjectionNdiFps);
                Assert.False(config.ProjectionNdiPreferAlpha);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}

