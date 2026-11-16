using System;
using System.IO;
using ImageColorChanger.Core;
using Xunit;
namespace ImageColorChanger.CanvasTextEditor.Tests.Core;
public sealed class BibleDefaultsUpgradeTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"canvas_bible_defaults_{Guid.NewGuid():N}.json");
    public void Dispose() { if (File.Exists(path)) File.Delete(path); }
    private static void Defaults(ConfigManager c)
    {
        Assert.False(c.BibleFixedTitle);
        Assert.Equal(1, c.BibleScrollTopOffset);
        Assert.Equal(30d, c.BibleVerseSpacing);
        Assert.Equal(39d, c.BiblePreviewFontSize);
    }
    [Fact] public void NewConfig_UsesRequestedDefaults() => Defaults(new ConfigManager(path));
    [Fact] public void MissingFields_UseDefaults() { File.WriteAllText(path, "{}"); Defaults(new ConfigManager(path)); }
    [Fact] public void Defaults_SurviveRestart() { Defaults(new ConfigManager(path)); Defaults(new ConfigManager(path)); }
    [Fact] public void SavedChoices_AreNotResetByNewDefaults()
    {
        File.WriteAllText(path, "{\"BibleFixedTitle\":true,\"BibleScrollTopOffset\":0,\"BibleVerseSpacing\":50,\"BiblePreviewFontSize\":35}");
        var c = new ConfigManager(path); c.SaveConfig(); c.LoadConfig();
        Assert.True(c.BibleFixedTitle); Assert.Equal(0,c.BibleScrollTopOffset);
        Assert.Equal(50d,c.BibleVerseSpacing); Assert.Equal(35d,c.BiblePreviewFontSize);
    }
    [Fact] public void ExplicitFalseAndZero_ArePreserved()
    {
        File.WriteAllText(path, "{\"BibleFixedTitle\":false,\"BibleScrollTopOffset\":0,\"BibleVerseSpacing\":0,\"BiblePreviewFontSize\":15}");
        var c = new ConfigManager(path); c.SaveConfig(); c = new ConfigManager(path);
        Assert.False(c.BibleFixedTitle); Assert.Equal(0,c.BibleScrollTopOffset); Assert.Equal(0d,c.BibleVerseSpacing); Assert.Equal(15d,c.BiblePreviewFontSize);
    }
    [Fact] public void PartialOldConfig_OnlyMissingFieldsUseNewDefaults()
    {
        File.WriteAllText(path, "{\"BibleFixedTitle\":true,\"BibleScrollTopOffset\":4}");
        var c = new ConfigManager(path); Assert.True(c.BibleFixedTitle); Assert.Equal(4,c.BibleScrollTopOffset); Assert.Equal(30d,c.BibleVerseSpacing); Assert.Equal(39d,c.BiblePreviewFontSize);
    }
    [Theory] [InlineData(0)] [InlineData(-1)] public void InvalidPreview_UsesDefault(int raw)
    { File.WriteAllText(path, "{\"BiblePreviewFontSize\":"+raw+"}"); Assert.Equal(39d,new ConfigManager(path).BiblePreviewFontSize); }
    [Fact] public void DeletedConfig_UsesNewDefaults() { new ConfigManager(path); File.Delete(path); Defaults(new ConfigManager(path)); }
    [Fact] public void MalformedConfig_UsesDefaultsWithoutOverwritingOnLoad()
    { File.WriteAllText(path,"not json"); Defaults(new ConfigManager(path)); Assert.Equal("not json",File.ReadAllText(path)); }
}
