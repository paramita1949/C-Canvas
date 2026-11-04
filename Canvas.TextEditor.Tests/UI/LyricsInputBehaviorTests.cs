using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.UI;
using ImageColorChanger.UI.Views;
using Xunit;
using TextBox = System.Windows.Controls.TextBox;

namespace Canvas.TextEditor.Tests.UI;

[Collection("Lyrics input exclusive")]
public sealed class LyricsInputBehaviorTests
{
    [Fact]
    public void IncrementalEnglishInput_DoesNotInsertBackwards() => WithHost((host, editors) =>
    {
        var editor = editors[0];
        foreach (char c in "abc")
        {
            int next = editor.CaretIndex + 1;
            editor.SelectedText = c.ToString();
            editor.CaretIndex = next;
            Pump();
        }
        Console.WriteLine($"INPUT_ENGLISH: text={editor.Text}; caret={editor.CaretIndex}");
        Assert.Equal("abc", editor.Text);
        Assert.Equal(3, editor.CaretIndex);
    });

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void SliceRefresh_PreservesTextCaretSelection_AndLatestPersistence(int rule) => WithHost((host, editors) =>
    {
        var editor = editors[0];
        Set(host, "_lyricsSliceLinesPerPage", rule);
        Set(host, "_lyricsSingleRawText", "旧原文");
        Set(host, "_lyricsSingleRawTextInitialized", true);
        editor.Text = "中文abc\r\n第二行";
        editor.Select(2, 3);
        Pump();
        Assert.Equal("中文abc\r\n第二行", editor.Text);
        Assert.Equal(2, editor.SelectionStart);
        Assert.Equal(3, editor.SelectionLength);
        Assert.Equal(editor.Text, Invoke(host, "GetSingleLyricsContentForPersistence"));
        Assert.Contains("中文abc", (string)Invoke(host, "BuildPlainLyricsTextFromSliceItems"));
        Console.WriteLine($"INPUT_SLICE_{rule}: text=unchanged; selection=2+3; latest-save=True; latest-slice=True");
    });

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void RuleVisualization_DoesNotInjectNumbersOrRewriteEditableText(int rule) => WithHost((host, editors) =>
    {
        Set(host, "_lyricsSliceLinesPerPage", rule);
        editors[0].Text = "主啊\r\n感谢你";
        editors[0].Select(1, 2);
        int start = editors[0].SelectionStart, length = editors[0].SelectionLength;
        Invoke(host, "GenerateLyricsSlicesFromSingleText", true, true);
        Assert.Equal("主啊\r\n感谢你", editors[0].Text);
        Assert.Equal(start, editors[0].SelectionStart);
        Assert.Equal(length, editors[0].SelectionLength);
        Console.WriteLine($"INPUT_VISUAL_{rule}: editable-raw-unchanged=True; no-generated-number-prefix=True");
    });

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void SplitEditor_WhitespaceAndMultilineTypingRemainNative(int index) => WithHost((host, editors) =>
    {
        Set(host, "_lyricsSplitMode", (int)ViewSplitMode.Quad);
        editors[index].Text = "第一行\r\n \t\r\n第二行";
        editors[index].Select(6, 2);
        int start = editors[index].SelectionStart, length = editors[index].SelectionLength;
        Pump();
        Assert.Equal("第一行\r\n \t\r\n第二行", editors[index].Text);
        Assert.Equal(start, editors[index].SelectionStart);
        Assert.Equal(length, editors[index].SelectionLength);
        Console.WriteLine($"INPUT_SPLIT_{index}: spaces-tabs-newlines=preserved; selection=preserved");
    });

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void ImeComposition_DefersDerivedWorkUntilCommit_OnEveryEditor(int index) => WithHost((host, editors) =>
    {
        var editor = editors[index];
        if (index > 0) Set(host, "_lyricsSplitMode", (int)ViewSplitMode.Quad);
        Compose(editor, TextCompositionManager.PreviewTextInputStartEvent, "n");
        editor.Text = "ni";
        editor.CaretIndex = 2;
        Compose(editor, TextCompositionManager.PreviewTextInputUpdateEvent, "ni");
        Pump();
        Assert.Equal(2, editor.CaretIndex);
        Assert.Null(Get(host, "_lyricsTypingSaveTimer"));
        Assert.False((bool)Get(host, "_lyricsProjectionRenderQueued"));
        Compose(editor, TextCompositionManager.PreviewTextInputEvent, "中文");
        // Preview commit 先发生，随后才是 WPF 文本容器的最终提交。
        editor.Text = "中文";
        editor.CaretIndex = 2;
        Pump();
        Assert.Equal("中文", editor.Text);
        Assert.Equal(2, editor.CaretIndex);
        Assert.NotNull(Get(host, "_lyricsTypingSaveTimer"));
        if (index == 0) Assert.Equal("中文", Invoke(host, "GetSingleLyricsContentForPersistence"));
        Console.WriteLine($"INPUT_IME_{index}: preedit-no-save=True; commit=中文; caret=2; derived-work-resumed=True");
    });

    [Fact]
    public void ClipboardCaretCorrection_DoesNotOverwriteLaterUserSelection() => WithHost((host, editors) =>
    {
        editors[0].Text = "abcdef";
        Pump();
        Invoke(host, "CollapseLyricsEditorSelection", editors[0], 2);
        editors[0].Select(4, 2);
        Pump();
        Assert.Equal(4, editors[0].SelectionStart);
        Assert.Equal(2, editors[0].SelectionLength);
        Console.WriteLine("INPUT_CLIPBOARD: subsequent-user-selection=preserved");
    });

    [Fact]
    public void EditorUpDown_IsNotHijackedForSliceNavigation() => WithHost((host, editors) =>
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), 0, Key.Down)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        Invoke(host, "LyricsTextBox_PreviewKeyDown", editors[0], args);
        Assert.False(args.Handled);
        Console.WriteLine("INPUT_ARROWS: native-UpDown=True");
    });

    [Fact]
    public void SaveBeforeBackgroundRefresh_UsesLatestEditorNotOldSnapshot() => WithHost((host, editors) =>
    {
        Set(host, "_lyricsSliceLinesPerPage", 2);
        Set(host, "_lyricsSingleRawText", "旧原文");
        Set(host, "_lyricsSingleRawTextInitialized", true);
        editors[0].Text = "刚刚输入的中文";
        Assert.Equal(editors[0].Text, Invoke(host, "GetSingleLyricsContentForPersistence"));
        Console.WriteLine("INPUT_SAVE_NOW: newest-editor-content=True");
    });

    [Fact]
    public void ImeCandidatePageKeys_AreNotConsumedByEditorNavigation() => WithHost((host, editors) =>
    {
        Compose(editors[0], TextCompositionManager.PreviewTextInputStartEvent, "n");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), 0, Key.PageDown)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        Invoke(host, "LyricsTextBox_PreviewKeyDown", editors[0], args);
        Assert.False(args.Handled);
        Console.WriteLine("INPUT_IME_CANDIDATE: PageDown=unhandled-native");
    });

    [Fact]
    public void CompositionFocusLoss_ResumesWorkAndCannotLeaveStuckState() => WithHost((host, editors) =>
    {
        Compose(editors[0], TextCompositionManager.PreviewTextInputStartEvent, "n");
        editors[0].Text = "取消后保留";
        editors[0].CaretIndex = 5;
        editors[0].RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, editors[0], editors[1])
        { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        Pump();
        Assert.False((bool)Invoke(host, "IsLyricsImeCompositionActive"));
        Assert.Equal("取消后保留", Invoke(host, "GetSingleLyricsContentForPersistence"));
        Assert.NotNull(Get(host, "_lyricsTypingSaveTimer"));
        Console.WriteLine("INPUT_IME_FOCUSLOSS: composing=False; latest-content=preserved; autosave-resumed=True");
    });

    [Fact]
    public void EmptyDeleteAndUndo_PreserveNativeEditingAndRefreshDerivedState() => WithHost((host, editors) =>
    {
        var editor = editors[0];
        editor.Text = "ab"; Pump();
        using var surface = AttachNativeSurface(editor);
        editor.ApplyTemplate();
        editor.Measure(new System.Windows.Size(600, 400));
        editor.Arrange(new Rect(0, 0, 600, 400));
        editor.CaretIndex = 2;
        Console.WriteLine($"INPUT_UNDO_FIXTURE: visible={editor.IsVisible}; loaded={editor.IsLoaded}; undoEnabled={editor.IsUndoEnabled}; backspaceCanExecute={System.Windows.Documents.EditingCommands.Backspace.CanExecute(null, editor)}");
        System.Windows.Documents.EditingCommands.Backspace.Execute(null, editor);
        Pump(); Assert.True(editor.CanUndo);
        editor.Undo(); Pump(); Assert.Equal("ab", editor.Text);
        editor.SelectAll(); editor.SelectedText = ""; Pump();
        Assert.Equal("", Invoke(host, "GetSingleLyricsContentForPersistence"));
        Assert.Equal("", Invoke(host, "BuildPlainLyricsTextFromSliceItems"));
        editor.CaretIndex = 0; editor.SelectedText = "恢复"; editor.CaretIndex = 2; Pump();
        Assert.Equal("恢复", editor.Text); Assert.Equal(2, editor.CaretIndex);
        Console.WriteLine("INPUT_EMPTY_RECOVERY: undo=True; delete=empty; retry=恢复; caret=2");
    });

    [Theory]
    [InlineData(0)] [InlineData(2)]
    public void RealNumberedLyrics_AreNotMistakenForGeneratedLabels(int rule) => WithHost((host, editors) =>
    {
        Set(host, "_lyricsSliceLinesPerPage", rule);
        editors[0].Text = "1. 第一段\r\n2. 第二段"; Pump();
        Assert.Equal(editors[0].Text, Invoke(host, "GetSingleLyricsContentForPersistence"));
        Assert.Contains("1. 第一段", (string)Invoke(host, "BuildPlainLyricsTextFromSliceItems"));
        Assert.Contains("1. 第一段", (string)Invoke(host, "GetCurrentSingleLyricsProjectionText"));
        Console.WriteLine($"INPUT_REAL_NUMBERS_{rule}: saved-and-derived-prefixes=preserved");
    });

    [Fact]
    public void PendingRefreshDuringComposition_DoesNotRepositionOrSavePreedit() => WithHost((host, editors) =>
    {
        editors[0].Text = "旧内容";
        Compose(editors[0], TextCompositionManager.PreviewTextInputStartEvent, "n");
        editors[0].Text = "ni"; editors[0].CaretIndex = 2; Pump();
        Assert.Equal(2, editors[0].CaretIndex);
        Assert.False(((DispatcherTimer)Get(host, "_lyricsTypingSaveTimer")).IsEnabled);
        Compose(editors[0], TextCompositionManager.PreviewTextInputEvent, "你");
        editors[0].Text = "你"; editors[0].CaretIndex = 1; Pump();
        Assert.Equal("你", Invoke(host, "GetSingleLyricsContentForPersistence"));
        Assert.Equal(1, editors[0].CaretIndex);
        Console.WriteLine("INPUT_PENDING_IME: old-queue-skipped=True; preedit-save=False; commit=你");
    });

    [Fact]
    public void CompositionSelection_DoesNotChangeSliceUntilFinalCommit() => WithHost((host, editors) =>
    {
        editors[0].Text = "第一段\r\n\r\n第二段"; Pump();
        Set(host, "_lyricsCurrentSliceIndex", 0);
        Compose(editors[0], TextCompositionManager.PreviewTextInputStartEvent, "n");
        editors[0].CaretIndex = editors[0].Text.Length;
        Assert.Equal(0, Get(host, "_lyricsCurrentSliceIndex"));
        Compose(editors[0], TextCompositionManager.PreviewTextInputEvent, "中文");
        editors[0].Text += "中文"; editors[0].CaretIndex = editors[0].Text.Length; Pump();
        Assert.Equal(1, Get(host, "_lyricsCurrentSliceIndex"));
        Console.WriteLine("INPUT_IME_SLICE: preedit-index=frozen; committed-index=1; caret=unchanged");
    });

    [Fact]
    public void NewlyAddedSlice_FollowsCurrentCaretWithoutMovingIt() => WithHost((host, editors) =>
    {
        Set(host, "_lyricsSliceLinesPerPage", 1);
        editors[0].Text = "a"; Pump();
        editors[0].Text = "a\r\nb"; editors[0].CaretIndex = 4; Pump();
        Assert.Equal(4, editors[0].CaretIndex);
        Assert.Equal(1, Get(host, "_lyricsCurrentSliceIndex"));
        Assert.Equal("b", Invoke(host, "GetCurrentSingleLyricsProjectionText"));
        Console.WriteLine("INPUT_NEW_SLICE: caret=4; slice=1; projection=b");
    });

    [Fact]
    public void ChineseAndEnglishEditor_RendersNativeGlyphsWithoutChangingContent() => WithHost((host, editors) =>
    {
        var editor = editors[0];
        using var surface = AttachNativeSurface(editor);
        editor.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        editor.FontSize = 40;
        editor.Background = System.Windows.Media.Brushes.Black;
        editor.Foreground = System.Windows.Media.Brushes.White;
        editor.TextAlignment = TextAlignment.Center;
        editor.Text = "中文输入 abc\r\n下一行"; editor.CaretIndex = 8; Pump();
        editor.Measure(new System.Windows.Size(600, 400)); editor.Arrange(new Rect(0, 0, 600, 400)); editor.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(600, 400, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(editor);
        byte[] pixels = new byte[600 * 400 * 4]; bitmap.CopyPixels(pixels, 600 * 4, 0);
        int lightPixels = 0;
        for (int i = 0; i < pixels.Length; i += 4) if (pixels[i] > 200 && pixels[i + 1] > 200 && pixels[i + 2] > 200) lightPixels++;
        Assert.True(lightPixels > 50);
        Assert.Equal("中文输入 abc\r\n下一行", editor.Text); Assert.Equal(8, editor.CaretIndex);
        string directory = Environment.GetEnvironmentVariable("LYRICS_INPUT_RENDER_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "editor-chinese-ltr.png");
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(path); encoder.Save(file);
            Console.WriteLine("INPUT_RENDER_PNG=" + path);
        }
        Console.WriteLine("INPUT_RENDER: CJK-Latin-glyphs=True; native-text-caret-unchanged=True");
    });

    private static void Compose(TextBox editor, RoutedEvent kind, string text) => editor.RaiseEvent(
        new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, editor, text))
        { RoutedEvent = kind });

    private static System.Windows.Interop.HwndSource AttachNativeSurface(TextBox editor)
    {
        // 无 WS_VISIBLE、无激活：仅给 WPF 原生文本视图提供 presentation source，不显示或抢占用户窗口。
        var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("CanvasLyricsInputRegression")
        { Width = 600, Height = 400, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000000 });
        source.RootVisual = editor;
        editor.ApplyTemplate(); editor.Measure(new System.Windows.Size(600, 400)); editor.Arrange(new Rect(0, 0, 600, 400));
        editor.UpdateLayout(); Pump();
        return source;
    }

    private static void WithHost(Action<MainWindow, TextBox[]> body)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Exception error = null;
        var thread = new Thread(() =>
        {
            MainWindow host = null;
            try
            {
                host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(host, Dispatcher.CurrentDispatcher);
                var view = (LyricsSectionView)RuntimeHelpers.GetUninitializedObject(typeof(LyricsSectionView));
                Set(host, "LyricsSectionView", view);
                string[] names = { "LyricsTextBox", "LyricsSplitTextBox1", "LyricsSplitTextBox2", "LyricsSplitTextBox3", "LyricsSplitTextBox4" };
                var editors = new TextBox[5];
                for (int i = 0; i < 5; i++)
                {
                    editors[i] = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Width = 600, Height = 400 };
                    Set(view, names[i], editors[i]);
                    // Wire production handlers; a local context menu avoids requiring application resources.
                    editors[i].ContextMenu = new ContextMenu();
                }
                foreach (string name in new[] { "_lyricsSliceItems", "_lyricsSliceCutPoints", "_lyricsSplitPages", "_lyricsComposingEditors" })
                {
                    var field = typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field != null) field.SetValue(host, Activator.CreateInstance(field.FieldType));
                }
                Set(host, "_lyricsSplitMode", (int)ViewSplitMode.Single);
                Set(host, "_lyricsSliceModeEnabled", true);
                Set(host, "_isLyricsMode", true);
                foreach (var editor in editors) Invoke(host, "WireLyricsTextBoxEvents", editor);
                body(host, editors);
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                if (host != null && Get(host, "_lyricsTypingSaveTimer") is DispatcherTimer timer) timer.Stop();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF fixture timed out");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static object Invoke(object host, string name, params object[] args)
    {
        var method = host.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try { return method.Invoke(host, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static object Get(object host, string name) => host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
    private static void Set(object host, string name, object value) => host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(host, value);
    private sealed class TestPresentationSource : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null;
    }
}

[CollectionDefinition("Lyrics input exclusive", DisableParallelization = true)]
public sealed class LyricsInputExclusiveCollection { }
