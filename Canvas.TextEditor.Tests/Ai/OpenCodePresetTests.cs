using System.Linq;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai;

public sealed class OpenCodePresetTests
{
    [Theory]
    [InlineData("opencode-zen", "https://opencode.ai/zen/v1", "按量付费", "gpt-5.6-luna")]
    [InlineData("opencode-go", "https://opencode.ai/zen/go/v1", "编程订阅", "glm-5.3-flash")]
    public void OpenCode_PresetProvidesDistinctConnectionDefaults(string id, string url, string label, string firstModel)
    {
        var preset = AiProviderCatalog.Find(id);
        Assert.NotNull(preset);
        Assert.Equal(url, preset.BaseUrl);
        Assert.Equal(AiProviderProtocol.OpenAiCompletions, preset.Protocol);
        Assert.Equal(firstModel, preset.RecommendedModels.First());
        Assert.Contains(firstModel, preset.RecommendedModels);
        Assert.Contains(id == "opencode-go" ? "deepseek-v4.1-flash" : "deepseek-v4-pro", preset.RecommendedModels);
        Assert.Equal("聚合与网关", preset.Group);
        Assert.Contains(label, preset.DisplayName);
        Assert.Same(preset, AiProviderCatalog.Find(id.ToUpperInvariant()));
    }

    [Fact]
    public void Presets_KeepDefaultsUniqueAndZenBeforeGo()
    {
        var all = AiProviderCatalog.GetAll().ToList();
        Assert.Equal(all.Count, all.Select(x => x.Id).Distinct().Count());
        Assert.Equal("deepseek", all.First().Id);
        Assert.NotNull(AiProviderCatalog.Find("custom"));
        Assert.Null(AiProviderCatalog.Find("unknown"));
        Assert.True(all.FindIndex(x => x.Id == "opencode-zen") >= 0);
        Assert.True(all.FindIndex(x => x.Id == "opencode-zen") < all.FindIndex(x => x.Id == "opencode-go"));
        Assert.Contains("编程代理", AiProviderCatalog.Find("opencode-go").Description);
    }
}
