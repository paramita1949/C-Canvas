using System;
using System.IO;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai;

public sealed class AiProviderProfileProtocolNormalizationTests
{
    [Fact]
    public void SaveAiProfile_NormalizesUnsupportedResponsesProtocol()
    {
        string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-protocol-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigManager(path);
            AiConnectionProfile profile = config.GetActiveAiProfile();
            profile.ProviderId = "minimax";
            profile.Protocol = AiProviderProtocol.OpenAiResponses;
            profile.BaseUrl = "https://api.minimaxi.com/v1";
            profile.ModelId = "MiniMax-M3";

            config.SaveAiProfile(profile);

            AiConnectionProfile saved = config.GetActiveAiProfile();
            Assert.Equal(AiProviderProtocol.OpenAiCompletions, saved.Protocol);
            Assert.Equal(AiProviderProtocol.OpenAiCompletions, config.AiSermonProtocol);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CustomResponsesProtocol_RemainsAvailableAcrossSaveAndReload()
    {
        string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-protocol-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigManager(path);
            AiConnectionProfile profile = config.CreateAiProfile("自定义 Responses");
            profile.ProviderId = "custom";
            profile.Protocol = AiProviderProtocol.OpenAiResponses;
            profile.BaseUrl = "https://gateway.example/v1";
            profile.ModelId = "gateway-model";

            config.SaveAiProfile(profile);
            Assert.True(config.SetActiveAiProfile(profile.Id));

            var reloaded = new ConfigManager(path);
            AiConnectionProfile restored = reloaded.GetActiveAiProfile();

            Assert.Equal("custom", restored.ProviderId);
            Assert.Equal(AiProviderProtocol.OpenAiResponses, restored.Protocol);
            Assert.Equal(AiProviderProtocol.OpenAiResponses, reloaded.AiSermonProtocol);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveAiProfile_NormalizesOpenCodeProtocolByModel()
    {
        string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-opencode-protocol-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigManager(path);
            AiConnectionProfile profile = config.GetActiveAiProfile();
            profile.ProviderId = "opencode-go";
            profile.Protocol = AiProviderProtocol.OpenAiResponses;
            profile.BaseUrl = "https://opencode.ai/zen/go/v1";
            profile.ModelId = "glm-5.3-flash";

            config.SaveAiProfile(profile);

            Assert.Equal(AiProviderProtocol.OpenAiCompletions, config.GetActiveAiProfile().Protocol);
            Assert.Equal(AiProviderProtocol.OpenAiCompletions, config.AiSermonProtocol);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveAiProfile_PreservesResponsesForOpenCodeResponseModel()
    {
        string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-opencode-response-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigManager(path);
            AiConnectionProfile profile = config.GetActiveAiProfile();
            profile.ProviderId = "opencode-go";
            profile.Protocol = AiProviderProtocol.OpenAiResponses;
            profile.BaseUrl = "https://opencode.ai/zen/go/v1";
            profile.ModelId = "gpt-5.6-luna";

            config.SaveAiProfile(profile);

            Assert.Equal(AiProviderProtocol.OpenAiResponses, config.GetActiveAiProfile().Protocol);
            Assert.Equal(AiProviderProtocol.OpenAiResponses, config.AiSermonProtocol);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
