using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using ImageColorChanger.UI;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI;

public sealed class MainWindowInputMethodAiForwardingTests
{
    [Fact]
    public void PendingCompositionRetraction_DoesNotFlushObsoleteTextToAi()
    {
        WithPendingRetraction((main, input, inputType, window) =>
        {
            inputType.GetMethod("Observe").Invoke(input, new object[] { "撤回前的候选文字", DateTimeOffset.Now.AddSeconds(-2) });
            typeof(MainWindow).GetMethod("FlushPendingAiAsrTurn", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(main, null);
            object[] args = { DateTimeOffset.Now, null };
            // 待发布回缩时定时发送必须暂缓，不能把旧候选取走送给coordinator。
            Assert.True((bool)inputType.GetMethod("TryFlush").Invoke(input, args));
        });
    }

    [Fact]
    public void NewReceptionDuringRetraction_BaselinesActualEditorAndIgnoresOldDisplaySnapshot()
    {
        WithPendingRetraction((main, input, inputType, window) =>
        {
            typeof(MainWindow).GetMethod("ResetAiCaptionInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(main, null);
            var forward = typeof(MainWindow).GetMethod("ForwardInputMethodTextToAi", BindingFlags.Instance | BindingFlags.NonPublic);
            forward.Invoke(main, new object[] { window.InputMethodText });
            object[] pending = { DateTimeOffset.Now.AddSeconds(8), null };
            Assert.False((bool)inputType.GetMethod("TryFlush").Invoke(input, pending));
            // 合成回缩落定后，实际编辑器已有文本仍属于会话启动前的基线。
            var timer = (System.Windows.Threading.DispatcherTimer)typeof(LiveCaptionOverlayWindow).GetField("_inputMethodSnapshotTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            timer.Stop();
            forward.Invoke(main, new object[] { "撤回前" });
            object[] settled = { DateTimeOffset.Now.AddSeconds(8), null };
            Assert.False((bool)inputType.GetMethod("TryFlush").Invoke(input, settled));
        });
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void ActualForwarder_RequiresReceivingAndActiveInputAndCoordinator(bool receiving, bool active, bool coordinator, bool expected)
    {
        OnSta(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"ime-ai-{Guid.NewGuid():N}.json");
            try
            {
                var main = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                var config = new ConfigManager(path);
                Set(main, "_configManager", config);
                Set(main, "_inputMethodSessionActive", active);
                Set(main, "_aiSermonReceiveAsr", receiving);
                if (coordinator) Set(main, "_aiSermonCoordinator", RuntimeHelpers.GetUninitializedObject(typeof(AiSermonConversationCoordinator)));
                var type = typeof(AiAsrTurnAggregator).Assembly.GetType("ImageColorChanger.Services.Ai.AiInputMethodTurnAggregator");
                Assert.NotNull(type);
                object input = Activator.CreateInstance(type);
                Set(main, "_aiInputMethodTurnAggregator", input);
                var forward = typeof(MainWindow).GetMethod("ForwardInputMethodTextToAi", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(forward);
                forward.Invoke(main, new object[] { "这是输入法送给AI解读的文字" });
                object[] args = { DateTimeOffset.Now.AddSeconds(10), null };
                Assert.Equal(expected, (bool)type.GetMethod("TryFlush").Invoke(input, args));
                Assert.True(config.LiveCaptionInputMethodEnabled);
                Assert.False(config.LiveCaptionRealtimeEnabled);
                Assert.False(config.LiveCaptionShortPhraseEnabled);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
    }

    [Fact]
    public void PanelCanReapplySameSpeakerAfterReceptionEnds()
    {
        OnSta(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"ime-ai-panel-{Guid.NewGuid():N}.json");
            var panel = new AiAssistantPanelWindow(new ConfigManager(path));
            try
            {
                int selected = 0;
                panel.SpeakerApplied += _ => selected++;
                var apply = typeof(AiAssistantPanelWindow).GetMethod("ApplySpeakerSelection", BindingFlags.Instance | BindingFlags.NonPublic);
                apply.Invoke(panel, new object[] { "测试传道人", true });
                panel.SetReceiveAsr(true);
                apply.Invoke(panel, new object[] { "测试传道人", true });
                Assert.Equal(1, selected);
                panel.SetReceiveAsr(false);
                apply.Invoke(panel, new object[] { "测试传道人", true });
                Assert.Equal(2, selected);
            }
            finally { panel.Close(); if (File.Exists(path)) File.Delete(path); }
        });
    }

    [Fact]
    public void CaptionSnapshot_IsConnectedToAiInput_WithoutEnteringAsrCallback()
    {
        string source = Read("UI/MainWindow.LiveCaption.InputMethod.cs");
        string changed = source[source.IndexOf("private void OnInputMethodTextChanged", StringComparison.Ordinal)..];
        Assert.Contains("ForwardInputMethodTextToAi(text);", changed);
        Assert.DoesNotContain("OnLiveCaptionSubtitleUpdated(", changed);
    }

    [Fact]
    public void AiEntries_PreserveSelectedInputSource()
    {
        string source = Read("UI/MainWindow.AiSermon.cs");
        foreach (var (start, end) in new[] {
            ("private async Task StartAiProjectRequestAsync", "private async Task StartPendingAiProjectAfterSpeakerSelectionAsync"),
            ("private async Task ApplyAiSpeakerAsync", "private async Task DeleteAiSpeakerAsync") })
        {
            string method = Slice(source, start, end);
            Assert.DoesNotContain("LiveCaptionRealtimeEnabled = true", method);
            Assert.Contains("EnsureAiCaptionInputStarted();", method);
        }
        Assert.Contains("_aiSermonReceiveAsr || _aiSermonCoordinator?.HasActiveSession == true", source);
    }

    [Fact]
    public void AiInputLifecycle_ClearsPendingOnStopAndModeChanges()
    {
        string main = Read("UI/MainWindow.AiSermon.cs");
        Assert.Contains("ResetAiCaptionInput();", Slice(main, "private void SetAiSermonReceiveAsr", "private void ForwardLiveCaptionAsrToAi"));
        Assert.Contains("ResetAiCaptionInput();", Read("UI/MainWindow.LiveCaption.InputMethod.cs"));
        Assert.Contains("ResetAiCaptionInput();", Slice(Read("UI/MainWindow.LiveCaption.cs"), "private void StopLiveCaption()", "private void ApplyRecognitionStateCore()"));
    }

    [Fact]
    public void RemovingCurrentSpeaker_EndsReceptionBeforeArchiving()
    {
        string method = Slice(Read("UI/MainWindow.AiSermon.cs"), "private async Task DeleteAiSpeakerAsync", "private async Task RenameAiSpeakerAsync");
        int stop = method.IndexOf("await EndAiSermonSessionFromPanelAsync();", StringComparison.Ordinal);
        Assert.True(stop >= 0);
        Assert.True(stop < method.IndexOf("await _aiSermonCoordinator.DeleteSpeakerAsync", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, false, true, "输入法已连接")]
    [InlineData(false, false, true, "AI未接收字幕")]
    [InlineData(true, true, false, "ASR已连接")]
    public void PanelStatus_DescribesActualCaptionSource(bool receiving, bool realtime, bool input, string expected)
    {
        var method = typeof(AiAssistantPanelWindow).GetMethod("BuildAsrConnectionStatus", new[] { typeof(bool), typeof(bool), typeof(bool) });
        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, new object[] { receiving, realtime, input }));
    }

    private static string Read(string path)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "ImageColorChanger.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root.FullName, path));
    }
    private static string Slice(string source, string from, string to)
    {
        int start = source.IndexOf(from, StringComparison.Ordinal);
        int end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static void Set(object target, string field, object value) => typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void WithPendingRetraction(Action<MainWindow, object, Type, LiveCaptionOverlayWindow> check)
    {
        OnSta(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"ime-ai-pending-{Guid.NewGuid():N}.json");
            var window = new LiveCaptionOverlayWindow();
            try
            {
                window.SetInputMethodMode(true);
                var editor = (TextBox)typeof(LiveCaptionOverlayWindow).GetField("_inputMethodEditor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                editor.Text = "撤回前的候选文字";
                typeof(LiveCaptionOverlayWindow).GetField("_inputMethodCompositionActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, true);
                editor.Text = "撤回前";
                Assert.Equal("撤回前的候选文字", window.InputMethodText);
                var main = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                Set(main, "_configManager", new ConfigManager(path));
                Set(main, "_inputMethodSessionActive", true);
                Set(main, "_aiSermonReceiveAsr", true);
                Set(main, "_liveCaptionOverlayWindow", window);
                Set(main, "_aiSermonCoordinator", RuntimeHelpers.GetUninitializedObject(typeof(AiSermonConversationCoordinator)));
                Set(main, "_aiAsrTurnAggregator", new AiAsrTurnAggregator());
                Type inputType = typeof(AiAsrTurnAggregator).Assembly.GetType("ImageColorChanger.Services.Ai.AiInputMethodTurnAggregator");
                Assert.NotNull(inputType);
                object input = Activator.CreateInstance(inputType);
                Set(main, "_aiInputMethodTurnAggregator", input);
                check(main, input, inputType, window);
            }
            finally { window.Close(); if (File.Exists(path)) File.Delete(path); }
        });
    }
    private static void OnSta(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
