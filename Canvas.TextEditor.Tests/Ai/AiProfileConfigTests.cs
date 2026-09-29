using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiProfileConfigTests
    {
        [Fact]
        public void LegacyConfig_MigratesToOneActiveProfile()
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-profiles-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "{\"DeepSeekApiKey\":\"key\",\"DeepSeekModel\":\"deepseek-v4-flash\"}");
                var config = new ConfigManager(path);

                var profiles = config.GetAiProfiles();
                var active = config.GetActiveAiProfile();

                var profile = Assert.Single(profiles);
                Assert.Equal(profile.Id, config.ActiveAiProfileId);
                Assert.Equal("deepseek", profile.ProviderId);
                Assert.Equal("deepseek-flash", profile.ModelId);
                Assert.Equal("key", active.ApiKey);
                Assert.Empty(profile.AvailableModels);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Profiles_SupportCreateActivateAndDelete()
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-profiles-{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(path);
                AiConnectionProfile created = config.CreateAiProfile("测试接口");
                created.ProviderId = "qwen";
                created.ModelId = "qwen-plus";
                config.SaveAiProfile(created);
                Assert.True(config.SetActiveAiProfile(created.Id));
                Assert.Equal(created.Id, config.ActiveAiProfileId);
                Assert.Equal("qwen-plus", config.GetActiveAiProfile().ModelId);
                Assert.Equal("qwen", config.AiSermonProviderId);
                Assert.Equal(AiProviderProtocol.OpenAiCompletions, config.AiSermonProtocol);
                Assert.Equal("qwen-plus", config.AiSermonModel);

                created.Protocol = AiProviderProtocol.OpenAiResponses;
                created.BaseUrl = "https://custom.example/v1";
                created.ApiKey = "custom-key";
                created.ModelId = "qwen-responses";
                config.SaveAiProfile(created);
                Assert.True(config.SetActiveAiProfile(created.Id));
                Assert.Equal(AiProviderProtocol.OpenAiResponses, config.AiSermonProtocol);
                Assert.Equal("https://custom.example/v1", config.AiSermonBaseUrl);
                Assert.Equal("custom-key", config.AiSermonApiKey);
                Assert.Equal("qwen-responses", config.AiSermonModel);

                Assert.True(config.DeleteAiProfile(created.Id));
                Assert.NotEqual(created.Id, config.ActiveAiProfileId);
                Assert.NotEmpty(config.GetAiProfiles());
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Profiles_PersistFullFetchedModelCatalogIndependently()
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-model-cache-{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(path);
                AiConnectionProfile first = config.GetActiveAiProfile();
                first.ModelId = "model-beta";
                first.AvailableModels = new List<AiModelOption>
                {
                    new AiModelOption { Id = "model-alpha", DisplayName = "Alpha", ContextWindow = 32000 },
                    new AiModelOption { Id = "model-beta", DisplayName = "Beta", ContextWindow = 64000 },
                    new AiModelOption { Id = "MODEL-BETA", DisplayName = "Duplicate Beta", ContextWindow = 1 },
                    new AiModelOption { Id = "model-gamma", DisplayName = "Gamma", ContextWindow = 128000 }
                };
                config.SaveAiProfile(first);

                AiConnectionProfile second = config.CreateAiProfile("第二个接口");
                second.ModelId = "other-only";
                second.AvailableModels = new List<AiModelOption>
                {
                    new AiModelOption { Id = "other-only", DisplayName = "Other" }
                };
                config.SaveAiProfile(second);

                var reloaded = new ConfigManager(path);
                AiConnectionProfile restoredFirst = reloaded.GetAiProfiles().Single(profile => profile.Id == first.Id);
                AiConnectionProfile restoredSecond = reloaded.GetAiProfiles().Single(profile => profile.Id == second.Id);

                Assert.Equal(new[] { "model-alpha", "model-beta", "model-gamma" }, restoredFirst.AvailableModels.Select(model => model.Id));
                Assert.Equal("Beta", restoredFirst.AvailableModels[1].DisplayName);
                Assert.Equal(64000, restoredFirst.AvailableModels[1].ContextWindow);
                Assert.Equal("other-only", Assert.Single(restoredSecond.AvailableModels).Id);

                restoredFirst.AvailableModels.Clear();
                Assert.Equal(3, reloaded.GetAiProfiles().Single(profile => profile.Id == first.Id).AvailableModels.Count);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
