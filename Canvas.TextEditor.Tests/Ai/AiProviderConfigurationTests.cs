using System;
using System.IO;
using System.Text.Json;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiProviderConfigurationTests
    {
        [Fact]
        public void AppConfig_UsesOpenAiCompatibleDefaults()
        {
            var config = new AppConfig();

            Assert.Equal("deepseek", config.AiSermonProviderId);
            Assert.Equal(AiProviderProtocol.OpenAiCompletions, config.AiSermonProtocol);
            Assert.Equal("https://api.deepseek.com", config.AiSermonBaseUrl);
            Assert.Equal("deepseek-flash", config.AiSermonModel);
        }

        [Fact]
        public void ConfigManager_LegacyDeepSeekConfig_IsReadableThroughCanonicalFields()
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-provider-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    DeepSeekApiKey = "legacy-key",
                    DeepSeekBaseUrl = "https://legacy.example/v1/",
                    DeepSeekModel = "legacy-model"
                }));

                var config = new ConfigManager(path);

                Assert.Equal("deepseek", config.AiSermonProviderId);
                Assert.Equal(AiProviderProtocol.OpenAiCompletions, config.AiSermonProtocol);
                Assert.Equal("legacy-key", config.AiSermonApiKey);
                Assert.Equal("https://legacy.example/v1", config.AiSermonBaseUrl);
                Assert.Equal("legacy-model", config.AiSermonModel);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void ConfigManager_LegacyDeepSeekFlashAlias_IsNormalized()
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-provider-alias-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(new
                {
                    AiSermonProviderId = "deepseek",
                    AiSermonProtocol = AiProviderProtocol.OpenAiCompletions,
                    AiSermonBaseUrl = "https://api.deepseek.com",
                    AiSermonModel = "deepseek-v4-flash"
                }));

                var config = new ConfigManager(path);

                Assert.Equal("deepseek-flash", config.AiSermonModel);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void ProviderCatalog_DoesNotExposeForeignPresets()
        {
            Assert.Null(AiProviderCatalog.Find("openai"));
            Assert.Null(AiProviderCatalog.Find("gemini"));
        }

        [Fact]
        public void ProviderCatalog_ContainsDeepSeekPreset()
        {
            AiProviderPreset preset = AiProviderCatalog.Find("deepseek");

            Assert.NotNull(preset);
            Assert.Equal(AiProviderProtocol.OpenAiCompletions, preset.Protocol);
            Assert.Contains("deepseek-flash", preset.RecommendedModels);
        }

        [Fact]
        public void ProviderCatalog_ContainsDomesticProvidersOnly()
        {
            string[] providerIds = { "qwen", "zhipu", "moonshot", "minimax", "baidu", "doubao", "siliconflow" };

            foreach (string providerId in providerIds)
            {
                AiProviderPreset preset = AiProviderCatalog.Find(providerId);

                Assert.NotNull(preset);
                Assert.Equal(AiProviderProtocol.OpenAiCompletions, preset.Protocol);
                Assert.NotEmpty(preset.RecommendedModels);
            }

            string[] excludedProviderIds = { "openai", "gemini", "openrouter", "groq", "mistral", "xai", "ollama" };
            foreach (string providerId in excludedProviderIds)
            {
                Assert.Null(AiProviderCatalog.Find(providerId));
            }
        }
    }
}
