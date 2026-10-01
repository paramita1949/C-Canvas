using System.Linq;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai;

public sealed class AiProviderProtocolCapabilityTests
{
    [Theory]
    [InlineData("deepseek", "https://api.deepseek.com", "deepseek-flash", true)]
    [InlineData("qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen3.8-max", true)]
    [InlineData("zhipu", "https://open.bigmodel.cn/api/paas/v4", "glm-4.5-air", true)]
    [InlineData("moonshot", "https://api.moonshot.cn/v1", "kimi-k3", true)]
    [InlineData("minimax", "https://api.minimaxi.com/v1", "MiniMax-M3", false)]
    [InlineData("baidu", "https://qianfan.baidubce.com/v2", "ernie-4.0-8k", false)]
    [InlineData("doubao", "https://ark.cn-beijing.volces.com/api/v3", "doubao-1-5-pro-32k-250115", false)]
    [InlineData("siliconflow", "https://api.siliconflow.cn/v1", "Qwen/Qwen3-8B", false)]
    [InlineData("opencode-zen", "https://opencode.ai/zen/v1", "gpt-5.6-luna", true)]
    [InlineData("opencode-go", "https://opencode.ai/zen/go/v1", "glm-5.3-flash", true)]
    public void Preset_ExposesVerifiedBaseUrlModelAndProtocolCapabilities(
        string providerId,
        string baseUrl,
        string model,
        bool supportsResponses)
    {
        AiProviderPreset preset = AiProviderCatalog.Find(providerId);

        Assert.NotNull(preset);
        Assert.Equal(baseUrl, preset.BaseUrl);
        Assert.Equal(model, preset.RecommendedModels.First());
        Assert.Contains(AiProviderProtocol.OpenAiCompletions, preset.SupportedProtocols);
        Assert.Equal(supportsResponses, preset.SupportsProtocol(AiProviderProtocol.OpenAiResponses));
        Assert.Equal(
            supportsResponses ? 2 : 1,
            preset.SupportedProtocols.Count);
    }

    [Theory]
    [InlineData("qwen", AiProviderProtocol.OpenAiResponses, AiProviderProtocol.OpenAiResponses)]
    [InlineData("deepseek", AiProviderProtocol.OpenAiResponses, AiProviderProtocol.OpenAiResponses)]
    [InlineData("opencode-go", AiProviderProtocol.OpenAiResponses, AiProviderProtocol.OpenAiCompletions)]
    [InlineData("custom", AiProviderProtocol.OpenAiResponses, AiProviderProtocol.OpenAiResponses)]
    [InlineData("compatible", AiProviderProtocol.OpenAiResponses, AiProviderProtocol.OpenAiResponses)]
    public void NormalizeProtocol_UsesProviderCapabilityMatrix(
        string providerId,
        string requestedProtocol,
        string expectedProtocol)
    {
        Assert.Equal(expectedProtocol, AiProviderCatalog.NormalizeProtocol(providerId, requestedProtocol));
    }

    [Theory]
    [InlineData("opencode-go", "glm-5.3-flash", AiProviderProtocol.OpenAiResponses, AiProviderProtocol.OpenAiCompletions)]
    [InlineData("opencode-go", "deepseek-v4.1-flash", AiProviderProtocol.OpenAiResponses, AiProviderProtocol.OpenAiCompletions)]
    [InlineData("opencode-go", "kimi-k3", AiProviderProtocol.OpenAiResponses, AiProviderProtocol.OpenAiCompletions)]
    [InlineData("opencode-go", "minimax-m3", AiProviderProtocol.OpenAiResponses, AiProviderProtocol.OpenAiCompletions)]
    [InlineData("opencode-go", "gpt-5.6-luna", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses)]
    [InlineData("opencode-zen", "grok-4.5", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses)]
    public void NormalizeProtocol_UsesModelCapabilityMatrix(
        string providerId,
        string modelId,
        string requestedProtocol,
        string expectedProtocol)
    {
        Assert.Equal(expectedProtocol, AiProviderCatalog.NormalizeProtocol(providerId, requestedProtocol, modelId));
    }

    [Fact]
    public void OpenCodeModelCapabilities_DefaultUnknownModelsToCompletions()
    {
        Assert.True(AiProviderCatalog.SupportsModelProtocol(
            "opencode-go", "glm-5.3-flash", AiProviderProtocol.OpenAiCompletions));
        Assert.False(AiProviderCatalog.SupportsModelProtocol(
            "opencode-go", "glm-5.3-flash", AiProviderProtocol.OpenAiResponses));
        Assert.Equal(
            AiProviderProtocol.OpenAiCompletions,
            AiProviderCatalog.NormalizeProtocol("opencode-go", AiProviderProtocol.OpenAiResponses, "unknown-model"));
    }

    [Fact]
    public void CustomAndCompatiblePresets_AllowBothOpenAiProtocols()
    {
        foreach (string providerId in new[] { "custom", "compatible" })
        {
            AiProviderPreset preset = AiProviderCatalog.Find(providerId);

            Assert.NotNull(preset);
            Assert.Equal(2, preset.SupportedProtocols.Count);
            Assert.True(preset.SupportsProtocol(AiProviderProtocol.OpenAiCompletions));
            Assert.True(preset.SupportsProtocol(AiProviderProtocol.OpenAiResponses));
        }
    }
}
