using System;
using System.IO;
using System.Reflection;
using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core;

public sealed class ConfigManagerLiveCaptionInputMethodTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"caption-input-{Guid.NewGuid():N}.json");
    private static PropertyInfo InputProperty => typeof(ConfigManager).GetProperty("LiveCaptionInputMethodEnabled");
    private static bool Input(ConfigManager config) { Assert.NotNull(InputProperty); return (bool)InputProperty.GetValue(config); }
    private static void SetInput(ConfigManager config, bool value) { Assert.NotNull(InputProperty); InputProperty.SetValue(config, value); }

    [Fact]
    public void OldConfiguration_WithoutInputChoice_DefaultsToInputWithoutRecognition()
    {
        File.WriteAllText(_path, "{\"LiveCaptionRealtimeEnabled\":true}");
        var config = new ConfigManager(_path);
        Assert.True(Input(config));
        Assert.False(config.LiveCaptionRealtimeEnabled);
        Assert.False(config.LiveCaptionShortPhraseEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{broken json")]
    public void NewMissingOrInvalidConfiguration_DefaultsToInputWithoutRecognition(string json)
    {
        if (json != null) File.WriteAllText(_path, json);
        var config = new ConfigManager(_path);
        Assert.True(config.LiveCaptionInputMethodEnabled);
        Assert.False(config.LiveCaptionRealtimeEnabled);
        Assert.False(config.LiveCaptionShortPhraseEnabled);
        config.SaveConfig();
        Assert.True(new ConfigManager(_path).LiveCaptionInputMethodEnabled);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void StartupSourceSelection_UsesInputOnlyWhenNoSourceWasSelected(bool realtime, bool shortPhrase, bool expectedInput)
    {
        File.WriteAllText(_path, $"{{\"LiveCaptionInputMethodEnabled\":false,\"LiveCaptionRealtimeEnabled\":{realtime.ToString().ToLowerInvariant()},\"LiveCaptionShortPhraseEnabled\":{shortPhrase.ToString().ToLowerInvariant()}}}");
        var config = new ConfigManager(_path);
        var select = typeof(ConfigManager).GetMethod("EnsureLiveCaptionSourceSelected");
        Assert.NotNull(select);
        select.Invoke(config, null);
        Assert.Equal(expectedInput, config.LiveCaptionInputMethodEnabled);
        Assert.Equal(realtime, config.LiveCaptionRealtimeEnabled);
        Assert.Equal(shortPhrase, config.LiveCaptionShortPhraseEnabled);
        Assert.Equal(expectedInput, new ConfigManager(_path).LiveCaptionInputMethodEnabled);
    }

    [Fact]
    public void InputMode_DisablesBothRecognitionModesAndPersists()
    {
        var config = new ConfigManager(_path);
        config.LiveCaptionRealtimeEnabled = true;
        config.LiveCaptionShortPhraseEnabled = true;
        SetInput(config, true);
        var reloaded = new ConfigManager(_path);
        Assert.True(Input(reloaded));
        Assert.False(reloaded.LiveCaptionRealtimeEnabled);
        Assert.False(reloaded.LiveCaptionShortPhraseEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectingRecognition_ExitsInputMode(bool realtime)
    {
        var config = new ConfigManager(_path);
        SetInput(config, true);
        if (realtime) config.LiveCaptionRealtimeEnabled = true;
        else config.LiveCaptionShortPhraseEnabled = true;
        Assert.False(Input(new ConfigManager(_path)));
    }

    [Fact]
    public void ConflictingLoadedFlags_InputModeWinsUntilExplicitRecognitionSelection()
    {
        File.WriteAllText(_path, "{\"LiveCaptionInputMethodEnabled\":true,\"LiveCaptionRealtimeEnabled\":true,\"LiveCaptionShortPhraseEnabled\":true}");
        var config = new ConfigManager(_path);
        Assert.True(Input(config));
        Assert.False(config.LiveCaptionRealtimeEnabled);
        Assert.False(config.LiveCaptionShortPhraseEnabled);
        config.LiveCaptionRealtimeEnabled = true;
        Assert.False(Input(config));
        Assert.True(config.LiveCaptionRealtimeEnabled);
    }

    [Fact]
    public void StopThenRestart_PreservesInputMethodChoiceWithoutStartingRecognition()
    {
        var config = new ConfigManager(_path);
        config.LiveCaptionInputMethodEnabled = true;
        // 关闭字幕/退出程序只停止识别，不应改变已选输入法。
        config.LiveCaptionRealtimeEnabled = false;
        config.LiveCaptionShortPhraseEnabled = false;
        var restarted = new ConfigManager(_path);
        Assert.True(restarted.LiveCaptionInputMethodEnabled);
        Assert.False(restarted.LiveCaptionRealtimeEnabled);
        Assert.False(restarted.LiveCaptionShortPhraseEnabled);
    }

    [Fact]
    public void SubtitleMenuEntry_DoesNotOverwritePersistedModeBeforeStarting()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "ImageColorChanger.csproj")))
            root = root.Parent;
        Assert.NotNull(root);
        string source = File.ReadAllText(Path.Combine(root.FullName, "UI", "MainWindow.AiSermon.cs"));
        int start = source.IndexOf("private void OpenAiRealtimeSubtitle()", StringComparison.Ordinal);
        int end = source.IndexOf("private void EnsureAiSermonPanel()", start, StringComparison.Ordinal);
        string entry = source.Substring(start, end - start);
        Assert.DoesNotContain("LiveCaptionRealtimeEnabled = true", entry);
        Assert.Contains("StartLiveCaption(_liveCaptionCurrentSource);", entry);
        Assert.Contains("OpenLiveCaptionPanel();", entry);

        string caption = File.ReadAllText(Path.Combine(root.FullName, "UI", "MainWindow.LiveCaption.cs"));
        int startCaption = caption.IndexOf("private async void StartLiveCaption(", StringComparison.Ordinal);
        int stopCaption = caption.IndexOf("private void StopLiveCaption()", startCaption, StringComparison.Ordinal);
        string startup = caption.Substring(startCaption, stopCaption - startCaption);
        Assert.DoesNotContain("LiveCaptionRealtimeEnabled = true", startup);
        Assert.True(startup.IndexOf("EnsureLiveCaptionSourceSelected();", StringComparison.Ordinal) <
                    startup.IndexOf("EnsureLiveCaptionComponents();", StringComparison.Ordinal));
        Assert.Contains("EnsureLiveCaptionSourceSelected();", startup);

        int buttonStart = caption.IndexOf("private void BtnAiCaption_Click", StringComparison.Ordinal);
        int buttonEnd = caption.IndexOf("public static (string Text", buttonStart, StringComparison.Ordinal);
        Assert.DoesNotContain("LiveCaptionRealtimeEnabled = true", caption.Substring(buttonStart, buttonEnd - buttonStart));
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
