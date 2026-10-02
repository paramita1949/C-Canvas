using System;
using System.IO;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai;

public sealed class ProviderProtocolEndpointTests
{
    [Theory]
    [InlineData("deepseek", "https://api.deepseek.com/chat/completions", "https://api.deepseek.com/responses")]
    [InlineData("qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", "https://dashscope.aliyuncs.com/compatible-mode/v1/responses")]
    [InlineData("zhipu", "https://open.bigmodel.cn/api/paas/v4/chat/completions", "https://open.bigmodel.cn/api/v1/responses")]
    [InlineData("moonshot", "https://api.moonshot.cn/v1/chat/completions", "https://api.moonshot.cn/v1/responses")]
    [InlineData("minimax", "https://api.minimaxi.com/v1/chat/completions", "")]
    [InlineData("baidu", "https://qianfan.baidubce.com/v2/chat/completions", "")]
    [InlineData("doubao", "https://ark.cn-beijing.volces.com/api/v3/chat/completions", "")]
    [InlineData("siliconflow", "https://api.siliconflow.cn/v1/chat/completions", "")]
    public void Preset_EndpointMatrixMatchesVerifiedProtocolSupport(
        string providerId,
        string expectedChatEndpoint,
        string expectedResponsesEndpoint)
    {
        AiProviderPreset preset = AiProviderCatalog.Find(providerId);

        Assert.NotNull(preset);
        Assert.Equal(expectedChatEndpoint, preset.ChatCompletionsEndpoint);
        Assert.Equal(expectedResponsesEndpoint, preset.ResponsesEndpoint);
        Assert.Equal(
            !string.IsNullOrWhiteSpace(expectedResponsesEndpoint),
            preset.SupportsProtocol(AiProviderProtocol.OpenAiResponses));
    }

    [Fact]
    public void Preset_StoresIndependentChatAndResponsesEndpoints()
    {
        AiProviderPreset preset = AiProviderCatalog.Find("deepseek");

        Assert.NotNull(preset);
        Assert.Equal("https://api.deepseek.com/chat/completions", preset.ChatCompletionsEndpoint);
        Assert.Equal("https://api.deepseek.com/responses", preset.ResponsesEndpoint);
        Assert.True(preset.SupportsProtocol(AiProviderProtocol.OpenAiResponses));
    }

    [Fact]
    public void ResolveEndpoint_UsesTheSelectedProtocolFieldWithoutAppendingToTheOther()
    {
        string chat = "https://chat.example/v2/generate";
        string responses = "https://responses.example/api/respond";

        Assert.Equal(chat, AiProviderCatalog.ResolveEndpoint("custom", AiProviderProtocol.OpenAiCompletions, chat, responses));
        Assert.Equal(responses, AiProviderCatalog.ResolveEndpoint("custom", AiProviderProtocol.OpenAiResponses, chat, responses));
    }

    [Fact]
    public void SaveAndReload_PreservesBothProtocolEndpoints()
    {
        string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-endpoints-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigManager(path);
            AiConnectionProfile profile = config.GetActiveAiProfile();
            profile.ProviderId = "custom";
            profile.BaseUrl = "https://legacy.example/v1";
            profile.ChatCompletionsEndpoint = "https://chat.example/v2/generate";
            profile.ResponsesEndpoint = "https://responses.example/api/respond";
            profile.Protocol = AiProviderProtocol.OpenAiResponses;
            profile.ModelId = "example-model";

            config.SaveAiProfile(profile);
            AiConnectionProfile restored = new ConfigManager(path).GetActiveAiProfile();

            Assert.Equal("https://chat.example/v2/generate", restored.ChatCompletionsEndpoint);
            Assert.Equal("https://responses.example/api/respond", restored.ResponsesEndpoint);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ResolveEndpoint_RejectsMissingSelectedProtocolEndpoint()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            AiProviderCatalog.ResolveEndpoint("custom", AiProviderProtocol.OpenAiResponses, "https://chat.example/v2/generate", string.Empty));

        Assert.Contains("Responses", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preset_StoresQwenResponsesEndpointSeparately()
    {
        AiProviderPreset preset = AiProviderCatalog.Find("qwen");

        Assert.NotNull(preset);
        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", preset.ChatCompletionsEndpoint);
        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1/responses", preset.ResponsesEndpoint);
        Assert.True(preset.SupportsProtocol(AiProviderProtocol.OpenAiResponses));
    }

    [Fact]
    public void ResolveEndpointFromBaseUrl_ForPresetUsesIndependentProtocolAddress()
    {
        string endpoint = AiProviderCatalog.ResolveEndpointFromBaseUrl(
            "zhipu",
            AiProviderProtocol.OpenAiResponses,
            "https://open.bigmodel.cn/api/paas/v4");

        Assert.Equal("https://open.bigmodel.cn/api/v1/responses", endpoint);
    }

    [Fact]
    public void Preset_BaseUrlFollowsSelectedProtocol()
    {
        AiProviderPreset preset = AiProviderCatalog.Find("zhipu");

        Assert.NotNull(preset);
        Assert.Equal(
            "https://open.bigmodel.cn/api/paas/v4",
            preset.GetBaseUrl(AiProviderProtocol.OpenAiCompletions));
        Assert.Equal(
            "https://open.bigmodel.cn/api/v1",
            preset.GetBaseUrl(AiProviderProtocol.OpenAiResponses));
    }

    [Theory]
    [InlineData(AiProviderProtocol.OpenAiCompletions, "https://gateway.example/v1", "https://gateway.example/v1/chat/completions")]
    [InlineData(AiProviderProtocol.OpenAiResponses, "https://gateway.example/v1", "https://gateway.example/v1/responses")]
    [InlineData(AiProviderProtocol.OpenAiResponses, "https://gateway.example/custom/responses", "https://gateway.example/custom/responses")]
    public void ResolveEndpointFromBaseUrl_ForCustomBuildsOnlyTheSelectedProtocol(
        string protocol,
        string baseUrl,
        string expected)
    {
        Assert.Equal(expected, AiProviderCatalog.ResolveEndpointFromBaseUrl("custom", protocol, baseUrl));
    }

    [Fact]
    public void BaseUrlChange_DoesNotRewriteIndependentProtocolEndpoints()
    {
        string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-base-url-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigManager(path);
            config.AiSermonProviderId = "custom";
            config.AiSermonChatCompletionsEndpoint = "https://vendor.example/chat/custom";
            config.AiSermonResponsesEndpoint = "https://vendor.example/responses/custom";

            config.AiSermonBaseUrl = "https://vendor.example/new-base";

            Assert.Equal("https://vendor.example/chat/custom", config.AiSermonChatCompletionsEndpoint);
            Assert.Equal("https://vendor.example/responses/custom", config.AiSermonResponsesEndpoint);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ProfileSave_DoesNotInventMissingResponsesEndpointFromBaseUrl()
    {
        string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-profile-endpoint-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigManager(path);
            AiConnectionProfile profile = config.GetActiveAiProfile();
            profile.ProviderId = "minimax";
            profile.Protocol = AiProviderProtocol.OpenAiCompletions;
            profile.BaseUrl = "https://api.minimaxi.com/v1";
            profile.ChatCompletionsEndpoint = "https://api.minimaxi.com/v1/chat/completions";
            profile.ResponsesEndpoint = string.Empty;
            profile.ModelId = "MiniMax-M3";

            config.SaveAiProfile(profile);
            AiConnectionProfile restored = new ConfigManager(path).GetActiveAiProfile();

            Assert.Equal("https://api.minimaxi.com/v1/chat/completions", restored.ChatCompletionsEndpoint);
            Assert.True(string.IsNullOrWhiteSpace(restored.ResponsesEndpoint));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
