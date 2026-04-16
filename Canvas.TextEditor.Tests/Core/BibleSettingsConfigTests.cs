using System;
using System.IO;
using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class BibleSettingsConfigTests
    {
        [Fact]
        public void BibleAiVoiceRecognitionEnabled_DefaultsToTrue()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                Assert.True(config.BibleAiVoiceRecognitionEnabled);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void BibleAiVoiceRecognitionEnabled_PersistsAfterSave()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.BibleAiVoiceRecognitionEnabled = false;

                var reloaded = new ConfigManager(tempFile);
                Assert.False(reloaded.BibleAiVoiceRecognitionEnabled);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void LiveCaptionFeatureFlags_DefaultToDisabled()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);

                Assert.False(config.LiveCaptionRealtimeEnabled);
                Assert.False(config.LiveCaptionShortPhraseEnabled);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void LiveCaptionFeatureFlags_PersistAfterSave()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionRealtimeEnabled = true;
                config.LiveCaptionShortPhraseEnabled = true;

                var reloaded = new ConfigManager(tempFile);

                Assert.True(reloaded.LiveCaptionRealtimeEnabled);
                Assert.True(reloaded.LiveCaptionShortPhraseEnabled);
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
