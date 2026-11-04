using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using ImageColorChanger.UI;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TextBox = System.Windows.Controls.TextBox;

namespace Canvas.TextEditor.Tests.UI;

[Collection("Lyrics image isolation exclusive")]
public sealed class LyricsImageIsolationTests
{
    [Fact]
    public void Images_SaveSeparateModePayloads_ReopenDatabase_ThirdImageStartsEmpty()
    {
        WithDatabase((db, path, images) =>
        {
            var manager = new LyricsProjectManager(db);
            var one = OpenImage(manager, images[0]);
            Assert.Equal("", one.Content);
            one.Content = ModeContent("图片1完整歌词\n第二行", "图1上区", "图1下区");
            SaveImage(manager, one, images[0]);
            var two = OpenImage(manager, images[1]);
            Assert.Equal("", two.Content);
            two.Content = ModeContent("图片2不同歌词", "图2上区", "图2下区");
            SaveImage(manager, two, images[1]);
            Assert.NotEqual(one.Id, two.Id);

            using var reopened = new CanvasDbContext(path);
            var fresh = new LyricsProjectManager(reopened);
            var loadedOne = OpenImage(fresh, images[0]);
            var loadedTwo = OpenImage(fresh, images[1]);
            var three = OpenImage(fresh, images[2]);
            Assert.Equal(one.Content, loadedOne.Content);
            Assert.Equal(two.Content, loadedTwo.Content);
            Assert.Equal(images[0], loadedOne.ImageId);
            Assert.Equal(images[1], loadedTwo.ImageId);
            Assert.Equal(0, loadedOne.SourceType);
            Assert.Equal(0, loadedTwo.SourceType);
            Assert.Equal("", three.Content);
            Assert.Equal(images[2], three.ImageId);
            Assert.Equal(0, three.Id); // 只进入空白图片不提前写入空记录。
            Assert.Equal(2, reopened.LyricsProjects.Count());
            Console.WriteLine("IMAGE_SQLITE: image1=own-full-payload; image2=different-full-payload; image3=empty; reopened-db=True; independent-ids=True");
        });
    }

    [Fact]
    public void SameImage_ReentryLoadsSavedProjectWithoutDuplicate()
    {
        WithDatabase((db, _, images) =>
        {
            var manager = new LyricsProjectManager(db);
            var original = OpenImage(manager, images[0]);
            original.Content = "同一图片保存的歌词";
            SaveImage(manager, original, images[0]);
            manager.ClearTracking();
            var reopened = OpenImage(manager, images[0]);
            Assert.Equal(original.Id, reopened.Id);
            Assert.Equal(original.Content, reopened.Content);
            SaveImage(manager, reopened, images[0]);
            Assert.Single(db.LyricsProjects.Where(p => p.ImageId == images[0]));
            Console.WriteLine("IMAGE_REENTRY: same-project-id=True; duplicate-count=1");
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999999)]
    public void InvalidOrMissingImage_ReturnsNoProjectAndDoesNotWrite(int imageId)
    {
        WithDatabase((db, _, images) =>
        {
            Assert.Null(OpenImage(new LyricsProjectManager(db), imageId));
            Assert.Empty(db.LyricsProjects);
            Console.WriteLine($"IMAGE_INVALID: id={imageId}; target=none; database-unmodified=True");
        });
    }

    [Fact]
    public void SaveCannotMoveExistingLyricsFromOneImageToAnother()
    {
        WithDatabase((db, _, images) =>
        {
            var manager = new LyricsProjectManager(db);
            var project = OpenImage(manager, images[0]);
            project.Content = "图1必须保留";
            SaveImage(manager, project, images[0]);
            int id = project.Id;
            project.ImageId = images[1];
            project.Content = "禁止覆盖图2";
            Assert.Throws<InvalidOperationException>(() => SaveImage(manager, project, images[1]));
            var stored = db.LyricsProjects.AsNoTracking().Single(p => p.Id == id);
            Assert.Equal(images[0], stored.ImageId);
            Assert.Equal("图1必须保留", stored.Content);
            Assert.False(db.LyricsProjects.AsNoTracking().Any(p => p.ImageId == images[1]));
            Console.WriteLine("IMAGE_SAVE_GUARD: cross-image-rebind=rejected; original-database-content=unchanged");
        });
    }

    [Fact]
    public void DeletedProject_IsNotResurrectedByLateSave_AndReentryIsEmpty()
    {
        WithDatabase((db, _, images) =>
        {
            var manager = new LyricsProjectManager(db);
            var project = OpenImage(manager, images[0]);
            project.Content = "已删除歌词";
            SaveImage(manager, project, images[0]);
            db.LyricsProjects.Remove(project); db.SaveChanges();
            SaveImage(manager, project, images[0]);
            Assert.Empty(db.LyricsProjects);
            Assert.Equal("", OpenImage(manager, images[0]).Content);
            Console.WriteLine("IMAGE_DELETE: late-save-resurrects=False; next-entry=empty");
        });
    }

    [Fact]
    public void LibrarySong_StaysIndependentOfImageProjects()
    {
        WithDatabase((db, _, images) =>
        {
            var manager = new LyricsProjectManager(db);
            var song = new LyricsProject { Name = "歌词库歌曲", Content = "独立库歌词", SourceType = 1, ImageId = null };
            manager.Save(song);
            var image = OpenImage(manager, images[0]);
            Assert.Equal("", image.Content);
            image.Content = "图片专属歌词";
            SaveImage(manager, image, images[0]);
            manager.ClearTracking();
            var loadedSong = manager.FindById(song.Id);
            Assert.Null(loadedSong.ImageId);
            Assert.Equal(1, loadedSong.SourceType);
            Assert.Equal("独立库歌词", loadedSong.Content);
            Assert.Equal("图片专属歌词", OpenImage(manager, images[0]).Content);
            Console.WriteLine("IMAGE_LIBRARY: library=independent; library-content=unchanged; image=own-content");
        });
    }

    [Fact]
    public void ImageEntry_CapturesImageIdAndDoesNotReuseSongProjectId()
    {
        var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        Invoke(host, "SetLyricsEntryBySong", 42);
        Invoke(host, "SetLyricsEntryByImage", 2);
        Assert.Equal(0, Field(host, "_currentLyricsProjectId"));
        Assert.Equal(2, Field(host, "_lyricsEntryId"));
        Assert.Equal("ImageBound", Field(host, "_lyricsEntryKind").ToString());
        SetField(host, "_currentImageId", 3); // 显示图片变化不能改变已绑定的保存目标。
        Assert.Equal(2, Field(host, "_lyricsEntryId"));
        Console.WriteLine("IMAGE_ENTRY: image-id=2; previous-song-id=cleared; mutable-display-id-does-not-rebind=True");
    }

    [Fact]
    public void ProjectLoadReset_ClearsRealWpfEditorsPagesAndRawSliceSnapshot()
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                // MainWindow 的编辑器是代理属性；使用实际 LyricsSectionView 的字段承载原生 TextBox。
                var view = (ImageColorChanger.UI.Views.LyricsSectionView)RuntimeHelpers.GetUninitializedObject(typeof(ImageColorChanger.UI.Views.LyricsSectionView));
                SetField(host, "LyricsSectionView", view);
                string[] names = { "LyricsTextBox", "LyricsSplitTextBox1", "LyricsSplitTextBox2", "LyricsSplitTextBox3", "LyricsSplitTextBox4" };
                foreach (string name in names) SetField(view, name, new TextBox { Text = "上张图片遗留" });
                SetField(host, "_lyricsSplitPages", new List<MainWindow.LyricsSplitContentData> { new() { Regions = new[] { "旧区域", "", "", "" } } });
                var sliceField = typeof(MainWindow).GetField("_lyricsSliceItems", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(sliceField);
                var sliceItems = Activator.CreateInstance(sliceField.FieldType);
                var itemType = sliceField.FieldType.GetGenericArguments()[0];
                sliceField.FieldType.GetMethod("Add").Invoke(sliceItems, new[] { Activator.CreateInstance(itemType, true) });
                sliceField.SetValue(host, sliceItems);
                SetField(host, "_lyricsSliceCutPoints", new HashSet<int> { 2, 4 });
                SetField(host, "_lyricsSingleRawText", "旧切片原文");
                SetField(host, "_lyricsSingleRawTextInitialized", true);
                SetField(host, "_lyricsSliceLinesPerPage", 3);
                SetField(host, "_lyricsCurrentSliceIndex", 4);
                SetField(host, "_lyricsCurrentPageIndex", 2);
                SetField(host, "_lyricsPagingMode", true);
                Invoke(host, "ResetLyricsEditorForProjectLoad");
                foreach (string name in names) Assert.Equal("", ((TextBox)Field(host, name)).Text);
                Assert.Empty((List<MainWindow.LyricsSplitContentData>)Field(host, "_lyricsSplitPages"));
                Assert.Equal(0, sliceField.FieldType.GetProperty("Count").GetValue(sliceItems));
                Assert.Empty((HashSet<int>)Field(host, "_lyricsSliceCutPoints"));
                Assert.Equal("", Field(host, "_lyricsSingleRawText"));
                Assert.Equal(0, Field(host, "_lyricsCurrentSliceIndex"));
                Assert.Equal(0, Field(host, "_lyricsCurrentPageIndex"));
                Assert.Equal(0, Field(host, "_lyricsSliceLinesPerPage"));
                Assert.False((bool)Field(host, "_lyricsPagingMode"));
                Assert.Same(Field(host, "LyricsTextBox"), Field(host, "_activeLyricsEditor"));
                Console.WriteLine("IMAGE_WPF_RESET: editors=5empty; pages=0; slices=0; cutpoints=0; raw=empty; indexes=0; active=main");
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void HostWiring_BindsBeforeEntry_ResetsBeforeLookup_AndBlocksOldSplitWrites()
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root != null && !File.Exists(Path.Combine(root.FullName, "ImageColorChanger.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        string menu = File.ReadAllText(Path.Combine(root.FullName, "UI/MainWindow.ContextMenu.cs"));
        string source = File.ReadAllText(Path.Combine(root.FullName, "UI/MainWindow.Lyrics.cs"));
        int capture = menu.IndexOf("SetLyricsEntryByImage(_currentImageId);", StringComparison.Ordinal);
        Assert.True(capture >= 0 && capture < menu.IndexOf("EnterLyricsMode();", capture, StringComparison.Ordinal));
        var load = source.Substring(source.IndexOf("private void LoadOrCreateLyricsProject()", StringComparison.Ordinal));
        Assert.True(load.IndexOf("ResetLyricsEditorForProjectLoad();", StringComparison.Ordinal) < load.IndexOf("LoadOrCreateImageProject(_lyricsEntryId)", StringComparison.Ordinal));
        Assert.Contains("SaveImageProject(_currentLyricsProject, _lyricsEntryId)", source);
        Assert.Contains("if (!_isLoadingLyricsProject && _lyricsSplitMode != (int)ViewSplitMode.Single)", source);
        var changed = source.Substring(source.IndexOf("private void LyricsTextBox_TextChanged(", StringComparison.Ordinal), 260);
        Assert.Contains("if (_isLoadingLyricsProject) return;", changed);
        var switchEntry = source.Substring(source.IndexOf("private void SwitchLyricsEntry(", StringComparison.Ordinal), 850);
        Assert.True(switchEntry.IndexOf("SaveLyricsProject(\"EntrySwitch\")", StringComparison.Ordinal) < switchEntry.IndexOf("_lyricsEntryKind = kind;", StringComparison.Ordinal));
        Console.WriteLine("IMAGE_HOST: captures-before-entry=True; resets-before-lookup=True; loading-blocks-old-split-write=True; saves-before-switch=True");
    }

    private static string ModeContent(string text, string first, string second) => "__LYRICS_MODE_V1__" + JsonSerializer.Serialize(new MainWindow.LyricsModeContentData
    {
        SingleContent = text,
        SplitContents = new() { new() { SplitMode = 4, Regions = new[] { first, second, "", "" } } },
        SliceLinesPerPage = 2,
        ActiveMode = 4
    });

    private static LyricsProject OpenImage(LyricsProjectManager manager, int imageId) => (LyricsProject)Invoke(manager, "LoadOrCreateImageProject", imageId);
    private static void SaveImage(LyricsProjectManager manager, LyricsProject project, int imageId) => Invoke(manager, "SaveImageProject", project, imageId);
    private static object Invoke(object instance, string name, params object[] args)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try { return method.Invoke(instance, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static object Field(object instance, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var field = instance.GetType().GetField(name, flags);
        return field != null ? field.GetValue(instance) : instance.GetType().GetProperty(name, flags).GetValue(instance);
    }
    private static void SetField(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);

    private static void WithDatabase(Action<CanvasDbContext, string, int[]> action)
    {
        string path = Path.Combine(Path.GetTempPath(), "canvas-image-lyrics-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new CanvasDbContext(path);
            db.Database.EnsureCreated();
            var folder = new Folder { Name = "fixture", Path = @"D:\fixture\lyrics", NormalizedPath = @"d:\fixture\lyrics", VideoPlayMode = "random", ScanPolicy = "full", LastScanStatus = "success" };
            db.Folders.Add(folder); db.SaveChanges();
            var images = Enumerable.Range(1, 3).Select(i => new MediaFile { Name = "图片" + i, Path = @"D:\fixture\lyrics\" + i + ".jpg", FolderId = folder.Id, FileTypeString = "image" }).ToArray();
            db.MediaFiles.AddRange(images); db.SaveChanges();
            action(db, path, images.Select(i => i.Id).ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}

[CollectionDefinition("Lyrics image isolation exclusive", DisableParallelization = true)]
public sealed class LyricsImageIsolationCollection { }
