using System;
using System.IO;
using ImageColorChanger.Services.LiveCaption;
using ImageColorChanger.UI;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services;

public sealed class LiveCaptionInputMethodDiagnosticsTests
{
    [Fact]
    public void MetadataLog_WritesRotatesAndContainsNoMultilineInjection()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"caption-log-{Guid.NewGuid():N}");
        string path = Path.Combine(folder, "events.log");
        try
        {
            InputMethodCaptionDiagnostics.Write(path, "mode=true\nchars=4");
            Assert.Single(File.ReadAllLines(path));
            Assert.Contains("mode=true chars=4", File.ReadAllText(path));
            File.WriteAllText(path, new string('x', (int)InputMethodCaptionDiagnostics.MaxLogBytes));
            InputMethodCaptionDiagnostics.Write(path, "mode=false");
            Assert.True(File.Exists(path + ".1"));
            Assert.Contains("mode=false", File.ReadAllText(path));
            Assert.True(new FileInfo(path).Length < 1024);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void MetadataLog_InvalidPathDoesNotInterruptCaptionInput()
    {
        Assert.Null(Record.Exception(() => InputMethodCaptionDiagnostics.Write("\0invalid", "mode=true")));
    }

    [Fact]
    public void InputModeTopButton_IsActiveWithoutAsrAndHonorsFeatureGate()
    {
        var state = MainWindow.BuildAiCaptionButtonStateForTest(false, false, true, inputMethodActive: true);
        Assert.True(state.IsActive);
        Assert.Contains("输入法", state.ToolTip);
        var denied = MainWindow.BuildAiCaptionButtonStateForTest(false, false, true, featureAllowed: false, inputMethodActive: true);
        Assert.False(denied.IsActive);
        Assert.False(denied.IsEnabled);
    }
}
