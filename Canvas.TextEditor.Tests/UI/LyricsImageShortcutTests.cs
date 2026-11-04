using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ImageColorChanger.Core;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using ImageColorChanger.UI;
using ImageColorChanger.UI.Views;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TextBox = System.Windows.Controls.TextBox;

namespace Canvas.TextEditor.Tests.UI;

[Collection("Lyrics F6 exclusive")]
public sealed class LyricsImageShortcutTests
{
    [Fact]
    public void F6_RoundTripSavesNewestChineseText_ImagesStayIndependent_ThirdStartsEmpty() => WithDatabase((db, path, ids) => WithSta(() =>
    {
        var manager = new LyricsProjectManager(db);
        var host = CreateSaveHost(db, Path.ChangeExtension(path, ".json"));
        var editor = (TextBox)Get(host, "LyricsTextBox");
        int mode = 1, currentId = ids[0];
        var controller = Controller(id =>
        {
            Call(host, "SetLyricsEntryByImage", id);
            var project = manager.LoadOrCreateImageProject(id);
            Set(host, "_currentLyricsProject", project);
            Set(host, "_isLyricsMode", true);
            editor.Text = project.Content.StartsWith("__LYRICS_MODE_V1__")
                ? JsonSerializer.Deserialize<MainWindow.LyricsModeContentData>(project.Content[18..]).SingleContent : project.Content;
            mode = 2; return true;
        }, () => (bool)Call(host, "TrySaveLyricsProject", "F6Test", true), () => { mode = 1; Set(host, "_isLyricsMode", false); });
        Assert.True(Handle(controller, mode, currentId)); Assert.Equal(2, mode); Assert.Equal("", editor.Text);
        editor.Text = "图片1最新中文 abc\r\n第二行";
        Assert.True(Handle(controller, mode, currentId)); Assert.Equal(1, mode);
        currentId = ids[1]; Assert.True(Handle(controller, mode, currentId)); Assert.Equal("", editor.Text);
        editor.Text = "图片2独立歌词"; Assert.True(Handle(controller, mode, currentId)); Assert.Equal(1, mode);
        currentId = ids[0]; Assert.True(Handle(controller, mode, currentId)); Assert.Equal("图片1最新中文 abc\r\n第二行", editor.Text);
        Assert.True(Handle(controller, mode, currentId));
        currentId = ids[2]; Assert.True(Handle(controller, mode, currentId)); Assert.Equal("", editor.Text);
        using var fresh = new CanvasDbContext(path);
        Assert.Equal(2, fresh.LyricsProjects.Count());
        var one = fresh.LyricsProjects.Single(p => p.ImageId == ids[0]);
        var two = fresh.LyricsProjects.Single(p => p.ImageId == ids[1]);
        Assert.Contains("图片1最新中文", JsonSerializer.Deserialize<MainWindow.LyricsModeContentData>(one.Content[18..]).SingleContent);
        Assert.Contains("图片2独立歌词", JsonSerializer.Deserialize<MainWindow.LyricsModeContentData>(two.Content[18..]).SingleContent);
        Console.WriteLine("F6_ROUNDTRIP: image1=latest-Chinese; image2=independent; image3=empty; reopened-SQLite=True; saves-before-exit=True");
    }));

    [Theory]
    [InlineData(0, 1, true, false, false)]
    [InlineData(1, 0, true, false, false)]
    [InlineData(1, -1, true, false, false)]
    [InlineData(1, 1, false, false, false)]
    [InlineData(2, 1, true, true, false)]
    [InlineData(2, 1, true, false, true)]
    public void IneligibleContext_DoesNotToggle(int mode, int id, bool foreground, bool composing, bool loading)
    {
        int calls = 0; var c = Controller(_ => { calls++; return true; }, () => { calls++; return true; }, () => calls++);
        Assert.False(Handle(c, mode, id, foreground: foreground, composing: composing, loading: loading));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(Key.Space, ModifierKeys.None)]
    [InlineData(Key.F5, ModifierKeys.None)]
    [InlineData(Key.F6, ModifierKeys.Control)]
    [InlineData(Key.F6, ModifierKeys.Alt)]
    [InlineData(Key.F6, ModifierKeys.Shift)]
    public void SpaceF5AndModifiedF6_AreNotConsumed(Key key, ModifierKeys modifiers)
    {
        int calls = 0; var c = Controller(_ => { calls++; return true; }, () => true, () => calls++);
        Assert.False(Handle(c, 1, 1, key: key, modifiers: modifiers)); Assert.Equal(0, calls);
    }

    [Fact]
    public void LongPressRepeat_DoesNotToggleBack()
    {
        int enters = 0, saves = 0, exits = 0;
        var c = Controller(_ => { enters++; return true; }, () => { saves++; return true; }, () => exits++);
        Assert.True(Handle(c, 1, 1)); Assert.True(Handle(c, 2, 1, repeat: true));
        Assert.Equal(1, enters); Assert.Equal(0, saves); Assert.Equal(0, exits);
        Assert.True(Handle(c, 2, 1)); Assert.Equal(1, saves); Assert.Equal(1, exits);
        Console.WriteLine("F6_REPEAT: initial=enter; held-repeat=no-toggle; released-next-press=save-exit");
    }

    [Fact]
    public void SaveFailure_KeepsLyricsAndDoesNotExit()
    {
        int exits = 0; var c = Controller(_ => true, () => false, () => exits++);
        Assert.True(Handle(c, 2, 1)); Assert.Equal(0, exits);
        Console.WriteLine("F6_SAVE_FAILURE: handled=True; exited=False; editor-kept=True");
    }

    [Fact]
    public void EnterDenied_DoesNotInvokeSaveOrExit()
    {
        int calls = 0; var c = Controller(_ => false, () => { calls++; return true; }, () => calls++);
        Assert.True(Handle(c, 1, 1)); Assert.Equal(0, calls);
    }

    [Fact]
    public void ReentrantRequest_IsConsumedWithoutAnotherTransition_AndGuardRecovers()
    {
        object c = null; int enters = 0;
        c = Controller(_ => { enters++; Assert.True(Handle(c, 1, 1)); return true; }, () => true, () => { });
        Assert.True(Handle(c, 1, 1)); Assert.Equal(1, enters);
        Assert.True(Handle(c, 1, 1)); Assert.Equal(2, enters);
    }

    [Fact]
    public void Exception_ResetsTransitionGuard()
    {
        bool fail = true; int enters = 0;
        var c = Controller(_ => { if (fail) throw new InvalidOperationException("fixture-entry-failure"); enters++; return true; }, () => true, () => { });
        Assert.Throws<InvalidOperationException>(() => Handle(c, 1, 1)); fail = false;
        Assert.True(Handle(c, 1, 1)); Assert.Equal(1, enters);
    }

    [Fact]
    public void RealSave_MissingImageOrDeletedProjectCannotReportSuccess() => WithDatabase((db, path, ids) => WithSta(() =>
    {
        var host = CreateSaveHost(db, Path.ChangeExtension(path, ".json"));
        var manager = new LyricsProjectManager(db);
        Call(host, "SetLyricsEntryByImage", ids[0]);
        var project = manager.LoadOrCreateImageProject(ids[0]); Set(host, "_currentLyricsProject", project);
        ((TextBox)Get(host, "LyricsTextBox")).Text = "未丢失的正文";
        Assert.True((bool)Call(host, "TrySaveLyricsProject", "F6Test", true));
        manager.Delete(project.Id);
        Assert.False((bool)Call(host, "TrySaveLyricsProject", "F6TestDeleted", true));
        Assert.Equal("未丢失的正文", ((TextBox)Get(host, "LyricsTextBox")).Text);
        Set(host, "_currentLyricsProject", manager.LoadOrCreateImageProject(ids[1]));
        Call(host, "SetLyricsEntryByImage", ids[1]);
        Set(host, "_currentLyricsProject", manager.LoadOrCreateImageProject(ids[1]));
        db.MediaFiles.Remove(db.MediaFiles.Single(m => m.Id == ids[1])); db.SaveChanges();
        Assert.False((bool)Call(host, "TrySaveLyricsProject", "F6TestMissing", true));
        Console.WriteLine("F6_REAL_SAVE_NEGATIVE: deleted-project=False; missing-image=False; native-text=preserved");
    }));

    [Fact]
    public void HostEntryFailure_DoesNotHideImageOrStartTimerBeforeSuccessfulLoad()
    {
        string source = File.ReadAllText(Path.Combine(Root(), "UI/MainWindow.Lyrics.cs"));
        int begin = source.IndexOf("private void EnterLyricsMode()", StringComparison.Ordinal);
        int end = source.IndexOf("private void ExitLyricsMode()", begin, StringComparison.Ordinal);
        string entry = source[begin..end];
        int load = entry.IndexOf("LoadOrCreateLyricsProject();", StringComparison.Ordinal);
        int guard = entry.IndexOf("if (_currentLyricsProject == null) return;", StringComparison.Ordinal);
        int hide = entry.IndexOf("ImageScrollViewer.Visibility = Visibility.Collapsed", StringComparison.Ordinal);
        Assert.True(load >= 0 && guard > load && hide > guard);
        Assert.True(entry.IndexOf("StartAutoSaveTimer();", StringComparison.Ordinal) > guard);
        Assert.Contains("&& IsActive", entry);
        string shortcut = File.ReadAllText(Path.Combine(Root(), "UI/MainWindow.Lyrics.Shortcut.cs"));
        Assert.Contains("_currentLyricsProject?.ImageId == imageId", shortcut);
        Assert.Contains("_lyricsEntryKind = previousKind", shortcut);
        Console.WriteLine("F6_LOAD_FAILURE_GATE: load-before-view-change; invalid-project=no-timer-no-hide; entry-identity-restored; stale-focus-requires-active-window");
    }

    [Fact]
    public void ActualHostModeMapping_RejectsMissingVideoLibraryAndMismatchedImage() => WithDatabase((db, path, ids) => WithSta(() =>
    {
        var host = CreateSaveHost(db, Path.ChangeExtension(path, ".json"));
        Set(host, "ImageScrollViewer", new System.Windows.Controls.ScrollViewer { Visibility = Visibility.Visible });
        var videoField = typeof(MainWindow).GetField("VideoContainer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var video = (FrameworkElement)Activator.CreateInstance(videoField.FieldType); video.Visibility = Visibility.Collapsed; Set(host, "VideoContainer", video);
        var textView = (TextEditorSectionView)RuntimeHelpers.GetUninitializedObject(typeof(TextEditorSectionView));
        var textPanel = new System.Windows.Controls.Grid { Visibility = Visibility.Collapsed };
        Set(textView, "TextEditorPanel", textPanel); Set(host, "TextEditorSectionView", textView);
        Set(Get(host, "LyricsSectionView"), "LyricsEditorPanel", new System.Windows.Controls.Grid { Visibility = Visibility.Visible });
        Set(host, "_currentImageId", ids[0]); Assert.Equal(1, Convert.ToInt32(Call(host, "GetLyricsImageShortcutMode")));
        Set(host, "_currentImageId", int.MaxValue); Assert.Equal(0, Convert.ToInt32(Call(host, "GetLyricsImageShortcutMode")));
        Set(host, "_currentImageId", ids[0]); Set(host, "_isBibleMode", true); Assert.Equal(0, Convert.ToInt32(Call(host, "GetLyricsImageShortcutMode")));
        Set(host, "_isBibleMode", false); textPanel.Visibility = Visibility.Visible; Assert.Equal(0, Convert.ToInt32(Call(host, "GetLyricsImageShortcutMode")));
        textPanel.Visibility = Visibility.Collapsed; video.Visibility = Visibility.Visible; Assert.Equal(0, Convert.ToInt32(Call(host, "GetLyricsImageShortcutMode")));
        video.Visibility = Visibility.Collapsed;
        db.MediaFiles.Single(m => m.Id == ids[1]).FileTypeString = "video"; db.SaveChanges();
        Set(host, "_currentImageId", ids[1]); Assert.Equal(0, Convert.ToInt32(Call(host, "GetLyricsImageShortcutMode")));
        Set(host, "_currentImageId", ids[0]); Call(host, "SetLyricsEntryBySong", 100); Set(host, "_isLyricsMode", true);
        Assert.Equal(0, Convert.ToInt32(Call(host, "GetLyricsImageShortcutMode")));
        Call(host, "SetLyricsEntryByImage", ids[0]); Assert.Equal(2, Convert.ToInt32(Call(host, "GetLyricsImageShortcutMode")));
        Set(host, "_currentImageId", ids[2]); Assert.Equal(0, Convert.ToInt32(Call(host, "GetLyricsImageShortcutMode")));
        Console.WriteLine("F6_NATIVE_SCOPE: image=eligible; missing/video/bible/slides/library/mismatched-image=ignored");
    }));

    [Fact]
    public void HostWiring_UsesF6BeforeOtherRoutes_AndPreservesInputAndImageIdentity()
    {
        string root = Root(); string keyboard = File.ReadAllText(Path.Combine(root, "UI/MainWindow.KeyboardInput.cs"));
        Assert.Contains("TryHandleLyricsImageShortcut(e)", keyboard);
        Assert.True(keyboard.IndexOf("TryHandleLyricsImageShortcut(e)") < keyboard.IndexOf("TryHandleProjectionVersePreviewConfirmCancelByKey"));
        string path = Path.Combine(root, "UI/MainWindow.Lyrics.Shortcut.cs"); Assert.True(File.Exists(path));
        string source = File.ReadAllText(path);
        Assert.Contains("Key.F6", source); Assert.Contains("e.IsRepeat", source); Assert.Contains("IsActive", source);
        Assert.Contains("IsLyricsImeCompositionActive()", source); Assert.Contains("LyricsEntryKind.ImageBound", source);
        Assert.Contains("_lyricsEntryId == _currentImageId", source); Assert.Contains("_isBibleMode", source);
        Assert.Contains("FileTypeString", source); Assert.Contains("SetLyricsEntryByImage(imageId)", source);
        Assert.Contains("EnterLyricsMode()", source); Assert.Contains("TrySaveLyricsProject(\"F6\")", source);
        Assert.Contains("CompleteLyricsModeExit", source); Assert.DoesNotContain("RegisterHotKey", source);
        string lyrics = File.ReadAllText(Path.Combine(root, "UI/MainWindow.Lyrics.cs"));
        Assert.Contains("epoch == _lyricsInputEpoch && _isLyricsMode", lyrics);
        Assert.Contains("保存内容校验失败", lyrics);
        Console.WriteLine("F6_HOST: foreground-only; image-bound-only; current-id-matched; IME-and-repeat-protected; shared-enter/save/exit; old-focus-callback-gated");
    }

    private static object Controller(Func<int, bool> enter, Func<bool> save, Action exit)
    {
        var type = typeof(MainWindow).Assembly.GetType("ImageColorChanger.UI.Modules.LyricsImageShortcutController");
        Assert.NotNull(type); return Activator.CreateInstance(type, enter, save, exit);
    }
    private static bool Handle(object c, int mode, int id, Key key = Key.F6, ModifierKeys modifiers = ModifierKeys.None,
        bool repeat = false, bool foreground = true, bool composing = false, bool loading = false)
    {
        var assembly = typeof(MainWindow).Assembly;
        var modeType = assembly.GetType("ImageColorChanger.UI.Modules.LyricsImageShortcutMode"); Assert.NotNull(modeType);
        var stateType = assembly.GetType("ImageColorChanger.UI.Modules.LyricsImageShortcutState"); Assert.NotNull(stateType);
        object state = Activator.CreateInstance(stateType, Enum.ToObject(modeType, mode), id, foreground, composing, loading);
        return (bool)Call(c, "TryHandle", key, modifiers, repeat, state);
    }
    private static MainWindow CreateSaveHost(CanvasDbContext db, string configPath)
    {
        var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(host, Dispatcher.CurrentDispatcher);
        var view = (LyricsSectionView)RuntimeHelpers.GetUninitializedObject(typeof(LyricsSectionView)); Set(host, "LyricsSectionView", view);
        foreach (string name in new[] { "LyricsTextBox", "LyricsSplitTextBox1", "LyricsSplitTextBox2", "LyricsSplitTextBox3", "LyricsSplitTextBox4" })
            Set(view, name, new TextBox { Foreground = System.Windows.Media.Brushes.White, TextAlignment = TextAlignment.Center });
        foreach (string name in new[] { "_lyricsSplitPages", "_lyricsComposingEditors", "_lyricsSliceItems", "_lyricsSliceCutPoints" })
        { var field = typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); Set(host, name, Activator.CreateInstance(field.FieldType)); }
        Set(host, "_lyricsSplitProjectionFontSizes", Enumerable.Repeat(60d, 4).ToArray());
        Set(host, "_lyricsSingleSliceProjectionFontSizes", Enumerable.Repeat(60d, 5).ToArray());
        Set(host, "_configManager", new ConfigManager(configPath)); Set(host, "_dbContext", db);
        Set(host, "_lyricsProjectManager", new LyricsProjectManager(db)); Set(host, "_activeLyricsEditor", Get(host, "LyricsTextBox"));
        Set(host, "_lyricsMainScreenFontSize", 40d); Set(host, "_lyricsProjectionFontSize", 60d); return host;
    }
    private static object Call(object instance, string name, params object[] args)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); Assert.NotNull(method);
        try { return method.Invoke(instance, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static object Get(object instance, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return instance.GetType().GetField(name, flags)?.GetValue(instance) ?? instance.GetType().GetProperty(name, flags).GetValue(instance);
    }
    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ImageColorChanger.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir); return dir.FullName;
    }
    private static void WithSta(Action action)
    {
        Exception error = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static void WithDatabase(Action<CanvasDbContext, string, int[]> action)
    {
        string dir = Path.Combine(Path.GetTempPath(), "canvas-f6-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "fixture.db");
        try
        {
            using var db = new CanvasDbContext(path); db.Database.EnsureCreated();
            var images = Enumerable.Range(1, 3).Select(i => new MediaFile { Name = "图片" + i, Path = Path.Combine(dir, i + ".png"), FileTypeString = "image" }).ToArray();
            db.MediaFiles.AddRange(images); db.SaveChanges(); action(db, path, images.Select(i => i.Id).ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            // 此路径由当前fixture创建；只删除自身临时文件，不触碰用户数据库。
            foreach (string file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir);
        }
    }
}

[CollectionDefinition("Lyrics F6 exclusive", DisableParallelization = true)]
public sealed class LyricsF6ExclusiveCollection { }
