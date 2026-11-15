using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ImageColorChanger.Core;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.Lyrics;
using ImageColorChanger.UI;
using ImageColorChanger.Managers;
using ImageColorChanger.UI.Composition;
using ImageColorChanger.UI.Modules;
using ImageColorChanger.Services.Licensing;
using ImageColorChanger.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Xunit;

namespace Canvas.TextEditor.Tests.UI;

[Collection("Lyrics F6 exclusive")]
public class ImageLyricsNavigationTests
{
    [Fact]
    public void TransactionProbe() => WithHost((h, db, images) =>
    {
        Editor(h).Text = "图片1最新编辑";
        Click(h, images[1]);
        Console.WriteLine($"NAVIGATION_PROBE: image={Get(h, "_currentImageId")}; entry={Get(h, "_lyricsEntryId")}; lyrics={h.IsInLyricsMode}; editor2={Editor(h).Text == "图片2自己的歌词"}; oldSaved={SavedBody(new ImageLyricsService(db).Load(images[0].Id).Content) == "图片1最新编辑"}; bitmap2={Processor(h).CurrentImagePath == images[1].Path}");
    });

    [Fact]
    public void ClickFollowsTargetLyricsAndSavesOldOwner() => WithHost((h, db, images) =>
    {
        Editor(h).Text = "图片1最新编辑";
        Click(h, images[1]);
        Assert.Equal(images[1].Id, Get(h, "_lyricsEntryId"));
        Assert.Equal(images[1].Id, Get(h, "_currentImageId"));
        Assert.True(h.IsInLyricsMode);
        Assert.Equal("图片2自己的歌词", Editor(h).Text);
        Assert.Equal(73d, Get(h, "_lyricsProjectionFontSize"));
        Assert.Equal(TextAlignment.Left, Editor(h).TextAlignment);
        Assert.Equal("图片1最新编辑", SavedBody(new ImageLyricsService(db).Load(images[0].Id).Content));
        Assert.NotEqual("图片1最新编辑", SavedBody(new ImageLyricsService(db).Load(images[1].Id).Content));
        Assert.Equal(images[1].Path, Processor(h).CurrentImagePath);
        using var rendered = (SKBitmap)Call(h, "BuildSingleLyricsProjectionBitmap", 640d, 360d, false);
        Assert.NotNull(rendered);
        var evidence = Environment.GetEnvironmentVariable("CANVAS_NAVIGATION_EVIDENCE");
        if (!string.IsNullOrEmpty(evidence))
        {
            Directory.CreateDirectory(evidence);
            using var png = SKImage.FromBitmap(rendered).Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(evidence, "image2-lyrics-native.png")); png.SaveTo(file);
        }
        Call(h, "CompleteLyricsModeExit");
        Assert.False(h.IsInLyricsMode);
        Assert.Equal(images[1].Path, Processor(h).CurrentImagePath);
        Console.WriteLine("NAVIGATION: old-owner-saved=True; image2-own-text-style=True; native-render=True; exit-bitmap2=True");
    });

    [Theory]
    [InlineData(null)]
    [InlineData("   \r\n")]
    [InlineData("__LYRICS_MODE_V1__{}")]
    [InlineData("__LYRICS_SPLIT_V1__{}")]
    [InlineData("__LYRICS_PAGES_V2__{}")]
    public void EmptyTargetReturnsToImageWithoutCreatingRow(string content) => WithHost((h, db, images) =>
    {
        var service = new ImageLyricsService(db);
        if (content != null) service.Save(images[2].Id, new LyricsDocument { Content = content });
        var before = service.GetRecordId(images[2].Id);
        Click(h, images[2]);
        Assert.False(h.IsInLyricsMode);
        Assert.Equal(images[2].Id, Get(h, "_currentImageId"));
        Assert.Equal(images[2].Path, Processor(h).CurrentImagePath);
        Assert.Equal("", Editor(h).Text);
        Assert.Null(Get(h, "_currentLyricsDocument"));
        Assert.Equal(before, service.GetRecordId(images[2].Id));
    });

    [Fact]
    public void OrdinaryImagesAndIndependentSongsKeepTheirModes() => WithHost((h, db, images) =>
    {
        Set(h, "_isLyricsMode", false); Click(h, images[1]); Assert.False(h.IsInLyricsMode);
        var song = new LyricsProject { Name = "独立歌", Content = "独立歌歌词", SourceType = 1 };
        db.LyricsProjects.Add(song); db.SaveChanges(); Call(h, "SetLyricsEntryBySong", song.Id);
        Call(h, "LoadOrCreateLyricsProject"); Set(h, "_isLyricsMode", true);
        Click(h, images[0]); Call(h, "HandleImageFileDoubleClick", Item(images[0]), Stopwatch.StartNew());
        Assert.Equal("独立歌歌词", Editor(h).Text); Assert.True(h.IsInLyricsMode);
        Assert.Equal(song.Id, Get(h, "_lyricsEntryId"));
    });

    [Fact]
    public void SameImageAndDoubleClickDoNotResetEditor() => WithHost((h, db, images) =>
    {
        Editor(h).Select(1, 2); Click(h, images[0]); Assert.Equal(1, Editor(h).SelectionStart);
        Click(h, images[1]); Editor(h).Select(1, 2);
        Call(h, "HandleImageFileDoubleClick", Item(images[1]), Stopwatch.StartNew());
        Assert.True(h.IsInLyricsMode); Assert.Equal(1, Editor(h).SelectionStart);
        Assert.Equal(images[1].Id, Get(h, "_lyricsEntryId"));
    });

    [Theory]
    [InlineData("ime")]
    [InlineData("deleted-current")]
    [InlineData("invalid-target")]
    [InlineData("missing-target")]
    [InlineData("malformed")]
    [InlineData("conflict")]
    [InlineData("decode")]
    public void FailureKeepsOldBindingAndEditedText(string failure) => WithHost((h, db, images) =>
    {
        Editor(h).Text = "尚未切换的原正文";
        var target = images[1];
        if (failure == "ime") ((HashSet<TextBox>)Get(h, "_lyricsComposingEditors")).Add(Editor(h));
        if (failure == "deleted-current") { db.ImageLyrics.Where(x => x.ImageId == images[0].Id).ExecuteDelete(); }
        if (failure == "invalid-target") { db.MediaFiles.Where(x => x.Id == target.Id).ExecuteUpdate(x => x.SetProperty(m => m.FileTypeString, "video")); }
        if (failure == "missing-target") { db.ImageLyrics.Where(x => x.ImageId == target.Id).ExecuteDelete(); db.MediaFiles.Where(x => x.Id == target.Id).ExecuteDelete(); }
        if (failure == "malformed") new ImageLyricsService(db).Save(target.Id, new LyricsDocument { Content = "__LYRICS_MODE_V1__{bad" });
        if (failure == "conflict")
        {
            db.ImageLyrics.Where(x => x.ImageId == target.Id).ExecuteDelete();
            db.ImageLyricsArchives.Add(new ImageLyricsArchive { OriginalImageId = target.Id, Reason = "LegacyConflict" }); db.SaveChanges();
        }
        if (failure == "decode") File.WriteAllText(target.Path, "invalid image bytes");
        Click(h, target);
        Assert.True(h.IsInLyricsMode); Assert.Equal(images[0].Id, Get(h, "_lyricsEntryId"));
        Assert.Equal(images[0].Id, Get(h, "_currentImageId"));
        Assert.Equal("尚未切换的原正文", Editor(h).Text);
        Assert.Equal(images[0].Path, Processor(h).CurrentImagePath);
    });

    [Fact]
    public void RapidClicksInvalidateOldQueuedWork() => WithHost((h, db, images) =>
    {
        Click(h, images[1]); Click(h, images[0]); Click(h, images[1]);
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        Assert.Equal(images[1].Id, Get(h, "_lyricsEntryId")); Assert.Equal("图片2自己的歌词", Editor(h).Text);
    });

    [Fact]
    public void DecodeFailureRetainsDecodedOldImageEvenWhenOldFileIsGone() => WithHost((h, db, images) =>
    {
        var bitmap = Processor(h).CurrentImage; var photo = Processor(h).CurrentPhoto;
        File.Delete(images[0].Path); File.WriteAllText(images[1].Path, "not an image");
        Click(h, images[1]);
        Assert.Same(bitmap, Processor(h).CurrentImage); Assert.Same(photo, Processor(h).CurrentPhoto);
        Assert.Equal(images[0].Path, Processor(h).CurrentImagePath);
        Assert.Equal(images[0].Id, Get(h, "_lyricsEntryId"));
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SplitTargetRestoresItsVisibleBodyAndRegionStyle(int mode) => WithHost((h, db, images) =>
    {
        var split = new MainWindow.LyricsSplitContentData { SplitMode = mode, Regions = new[] { "目标分区一", "目标分区二", "目标分区三", "目标分区四" }, RegionStyles = Enumerable.Range(0, 4).Select(_ => new MainWindow.LyricsSplitRegionStyle { FontSize = 89, TextAlign = "Right", ColorHex = "#FF0000" }).ToArray() };
        var data = new MainWindow.LyricsModeContentData { ActiveMode = mode, SingleContent = "保留的单画面", SplitContents = new() { split } };
        new ImageLyricsService(db).Save(images[1].Id, new LyricsDocument { Content = "__LYRICS_MODE_V1__" + JsonSerializer.Serialize(data) });
        Click(h, images[1]); Assert.True(h.IsInLyricsMode); Assert.Equal(mode, Get(h, "_lyricsSplitMode"));
        var region = (TextBox)Get(h, "LyricsSplitTextBox1"); Assert.Equal("目标分区一", region.Text); Assert.Equal(TextAlignment.Right, region.TextAlignment);
        Assert.Equal(89d, ((double[])Get(h, "_lyricsSplitProjectionFontSizes"))[0]);
    });

    [Theory]
    [InlineData("mode")]
    [InlineData("pages")]
    public void HiddenOrUnloadedBodyDoesNotOpenBlankLyrics(string format) => WithHost((h, db, images) =>
    {
        var empty = new MainWindow.LyricsSplitContentData { SplitMode = 1, Regions = new[] { "", "", "隐藏正文", "" } };
        string content = format == "mode" ? "__LYRICS_MODE_V1__" + JsonSerializer.Serialize(new MainWindow.LyricsModeContentData { ActiveMode = 1, SplitContents = new() { empty } })
            : "__LYRICS_PAGES_V2__" + JsonSerializer.Serialize(new MainWindow.LyricsPagesContentData { Pages = new() { empty, new MainWindow.LyricsSplitContentData { SplitMode = 1, Regions = new[] { "未加载旧页", "", "", "" } } } });
        new ImageLyricsService(db).Save(images[1].Id, new LyricsDocument { Content = content });
        Click(h, images[1]); Assert.False(h.IsInLyricsMode);
    });

    [Fact]
    public void OriginalModeDoesNotPublishIntermediateImageToNativeProjection() => WithHost((h, db, images) =>
    {
        var projection = (ProjectionManager)Get(h, "_projectionManager");
        Set(projection, "_projectionWindow", new Window()); Set(projection, "_mainWindow", h);
        Set(projection, "_imageProcessor", Processor(h));
        Set(projection, "_projectionImageControl", new System.Windows.Controls.Image()); Set(projection, "_projectionContainer", new Grid());
        var viewport = new ScrollViewer { Width = 640, Height = 360 }; viewport.Measure(new Size(640, 360)); viewport.Arrange(new Rect(0, 0, 640, 360));
        Set(projection, "_projectionScrollViewer", viewport);
        Set(h, "_mainWindowServices", new MainWindowServices(new LicenseFixtureProvider()));
        Set(h, "_originalMode", true); Set(h, "_originalModeZoomRatio", 1d);
        Click(h, images[1]);
        Assert.Equal(images[1].Id, Get(h, "_lyricsEntryId")); Assert.Null(Get(projection, "_currentImage"));
        Assert.NotNull(((System.Windows.Controls.Image)Get(projection, "_projectionImageControl")).Source);
        Console.WriteLine("NATIVE_PROJECTION: finalTextFrame=True; intermediateImage=False; originalMode=True");
    });

    [Fact]
    public void FolderPolicyAppliesAfterSaveAndRollsBackOnFailure() => WithHost((h, db, images) =>
    {
        var folder = new Folder { Name = "普通目标目录", Path = Path.GetDirectoryName(images[1].Path) + "/folder" }; db.Folders.Add(folder); db.SaveChanges();
        db.MediaFiles.Where(m => m.Id == images[2].Id || m.Id == images[1].Id).ExecuteUpdate(s => s.SetProperty(m => m.FolderId, folder.Id));
        var manager = new DatabaseManager(db, new FolderRepository(db), new MediaRepository(db), new SettingsRepository(db), new OriginalMarkRepository(db), new KeyframeRepository(db), new DatabaseMaintenanceRepository(db));
        var original = new OriginalManager(manager, h); Set(h, "_originalManager", original);
        Set(h, "_projectTreeSelectionStateController", new ProjectTreeSelectionStateController(manager, original));
        Set(h, "_originalMode", true); Set(h, "_isColorEffectEnabled", true); Set(h, "_currentFolderId", (int?)999);
        File.WriteAllText(images[1].Path, "bad image"); Click(h, images[1]);
        Assert.True((bool)Get(h, "_originalMode")); Assert.True((bool)Get(h, "_isColorEffectEnabled")); Assert.Equal(999, Get(h, "_currentFolderId"));
        Click(h, images[2]); Assert.False(h.IsInLyricsMode); Assert.False((bool)Get(h, "_originalMode")); Assert.False((bool)Get(h, "_isColorEffectEnabled")); Assert.Equal(folder.Id, Get(h, "_currentFolderId"));
    });

    private sealed class LicenseFixtureProvider : IServiceProvider, IFeatureGate
    {
        public object GetService(Type serviceType) => serviceType == typeof(IFeatureGate) ? this : null;
        public FeatureGateResult Check(PremiumFeature feature) => new(true, "fixture", "fixture", "", null, false);
        public FeatureGateResult Require(PremiumFeature feature) => Check(feature);
        public Task<FeatureGateResult> CheckAsync(PremiumFeature feature, CancellationToken cancellationToken = default) => Task.FromResult(Check(feature));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void LegacyFirstLayoutAndBodyStayTogether(int mode, bool emptyHorizontal) => WithHost((h, db, images) =>
    {
        var pages = new MainWindow.LyricsPagesContentData { Pages = new() { new MainWindow.LyricsSplitContentData { SplitMode = mode, Regions = new[] { "旧版首屏正文", "", "", "" } } } };
        if (emptyHorizontal) pages.Pages.Add(new MainWindow.LyricsSplitContentData { SplitMode = 1 });
        new ImageLyricsService(db).Save(images[1].Id, new LyricsDocument { Content = "__LYRICS_PAGES_V2__" + JsonSerializer.Serialize(pages) });
        Click(h, images[1]); Assert.True(h.IsInLyricsMode); Assert.Equal(mode, Get(h, "_lyricsSplitMode"));
        Assert.Equal("旧版首屏正文", ((TextBox)Get(h, "LyricsSplitTextBox1")).Text);
    });

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static string SavedBody(string content) => content.StartsWith("__LYRICS_MODE_V1__") ? JsonSerializer.Deserialize<MainWindow.LyricsModeContentData>(content["__LYRICS_MODE_V1__".Length..]).SingleContent : content;
    private static object Get(object o, string name) => o.GetType().GetField(name, Flags)?.GetValue(o) ?? o.GetType().GetProperty(name, Flags)?.GetValue(o);
    private static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);
    private static object Call(object o, string name, params object[] args)
    {
        var method = o.GetType().GetMethod(name, Flags); Assert.NotNull(method);
        try { return method.Invoke(o, args); }
        catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static TextBox Editor(MainWindow h) => (TextBox)Get(h, "LyricsTextBox");
    private static ImageProcessor Processor(MainWindow h) => (ImageProcessor)Get(h, "_imageProcessor");
    private static ProjectTreeItem Item(MediaFile m) => new() { Id = m.Id, Path = m.Path, Name = m.Name, Type = TreeItemType.File, FileType = ImageColorChanger.Database.Models.FileType.Image };
    private static void Click(MainWindow h, MediaFile m) => ((Task)Call(h, "HandleImageFileSingleClickAsync", Item(m), m.Id)).GetAwaiter().GetResult();
    private static void FillControls(object o)
    {
        foreach (var field in o.GetType().GetFields(Flags))
        {
            if (field.GetValue(o) != null || typeof(UserControl).IsAssignableFrom(field.FieldType) || typeof(Window).IsAssignableFrom(field.FieldType) || field.FieldType.Namespace?.StartsWith("System.Windows") != true) continue;
            if (typeof(FrameworkElement).IsAssignableFrom(field.FieldType) && field.FieldType.GetConstructor(Type.EmptyTypes) != null)
                field.SetValue(o, Activator.CreateInstance(field.FieldType));
        }
    }
    private static void WithHost(Action<MainWindow, CanvasDbContext, MediaFile[]> action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "canvas-navigation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            MainWindow h = null;
            try
            {
                using var db = new CanvasDbContext(Path.Combine(dir, "test.db")); db.Database.EnsureCreated();
                h = (MainWindow)typeof(LyricsImageShortcutTests).GetMethod("CreateSaveHost", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { db, Path.Combine(dir, "config.json") });
                // MainWindow's constructor opens app services/user DBs. Initialize only its native WPF base state.
                var windowBase = new Window();
                for (var type = typeof(Window); type != null; type = type.BaseType)
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                        field.SetValue(h, field.GetValue(windowBase));
                FillControls(h); FillControls(Get(h, "LyricsSectionView"));
                h.Resources = new ResourceDictionary();
                foreach (var key in new[] { "IconLucidePause", "IconLucidePlay", "IconLucideStop" }) h.Resources[key] = Geometry.Parse("M0,0 L10,10");
                foreach (var name in new[] { "TextEditorSectionView", "MediaPlayerSectionView" })
                {
                    var f = typeof(MainWindow).GetField(name, Flags); var view = RuntimeHelpers.GetUninitializedObject(f.FieldType); FillControls(view); f.SetValue(h, view);
                }
                ((FrameworkElement)Get(h, "TextEditorPanel")).Visibility = Visibility.Collapsed;
                ((FrameworkElement)Get(h, "VideoContainer")).Visibility = Visibility.Collapsed;
                Set(h, "ImageScaleTransform", new ScaleTransform(1, 1));
                Set(h, "_projectionManager", RuntimeHelpers.GetUninitializedObject(typeof(ProjectionManager)));
                var scroll = (ScrollViewer)Get(h, "ImageScrollViewer"); scroll.Measure(new Size(640, 360)); scroll.Arrange(new Rect(0, 0, 640, 360));
                var processor = new ImageProcessor(null, new GPUContext(), scroll, (System.Windows.Controls.Image)Get(h, "ImageDisplay"), new Grid()); Set(h, "_imageProcessor", processor);
                var images = Enumerable.Range(1, 3).Select(i => new MediaFile { Name = "图片" + i, Path = Path.Combine(dir, i + ".png"), FileTypeString = "image" }).ToArray();
                foreach (var image in images) { using var bitmap = new SKBitmap(64, 36); bitmap.Erase(SKColors.DarkBlue); using var png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(image.Path); png.SaveTo(file); }
                db.MediaFiles.AddRange(images); db.SaveChanges();
                var service = new ImageLyricsService(db); service.Save(images[0].Id, new LyricsDocument { Content = "图片1原歌词" });
                service.Save(images[1].Id, new LyricsDocument { Content = "图片2自己的歌词", FontSize = 73, TextAlign = "Left" });
                Set(h, "_currentImageId", images[0].Id); Set(h, "_imagePath", images[0].Path); Assert.True(processor.LoadImage(images[0].Path));
                Call(h, "SetLyricsEntryByImage", images[0].Id); Call(h, "LoadOrCreateLyricsProject"); Set(h, "_isLyricsMode", true);
                ((FrameworkElement)Get(h, "LyricsEditorPanel")).Visibility = Visibility.Visible;
                action(h, db, images);
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                if (h != null) { ((DispatcherTimer)Get(h, "_lyricsAutoSaveTimer"))?.Stop(); Processor(h)?.Dispose(); }
                Dispatcher.CurrentDispatcher.InvokeShutdown(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "Native navigation fixture timed out");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
