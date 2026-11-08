using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using ImageColorChanger.UI;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using WpfCanvas = System.Windows.Controls.Canvas;

namespace Canvas.TextEditor.Tests.UI;

[Collection("Lyrics image isolation exclusive")]
public sealed class LyricsWatermarkTests
{
    [Theory]
    [InlineData("001.奇异恩典.jpg", "001.奇异恩典")]
    [InlineData("歌词_我的赞美.PNG", "歌词_我的赞美")]
    [InlineData("12、主爱永远.jpeg", "12、主爱永远")]
    [InlineData("新歌曲_恩典.v2.webp", "新歌曲_恩典.v2")]
    public void ImageEntry_UsesFilenameAndPreservesItsPrefixes(string filename, string expected)
    {
        WithFixture((host, db, image) =>
        {
            image.Path = @"D:\fixture\" + filename; db.SaveChanges();
            AssertWatermark(host, $"《{expected}》");
            Assert.Equal("歌词_图片_1", GetProject(host).Name);
            Assert.Empty(db.LyricsProjects); // 仅显示水印不能写入或重命名项目。
        });
    }

    [Fact]
    public void ImageEntry_IgnoresOldProjectTitleAndOtherDisplayedImage()
    {
        WithFixture((host, db, image) =>
        {
            GetProject(host).Name = "歌词_以前的标题";
            Set(host, "_currentImageId", 999);
            Set(host, "_imagePath", @"D:\other\错误的图片.jpg");
            AssertWatermark(host, "《原图赞美诗》");
        });
    }

    [Fact]
    public void ImageEntry_RenameAndReentryUseLatestBoundFile()
    {
        WithFixture((host, db, image) =>
        {
            var manager = new LyricsProjectManager(db);
            var project = GetProject(host); project.Content = "保存的歌词";
            manager.SaveImageProject(project, image.Id);
            AssertWatermark(host, "《原图赞美诗》");
            image.Path = @"D:\fixture\改名后的诗歌.png"; db.SaveChanges();
            manager.ClearTracking();
            Set(host, "_currentLyricsProject", manager.LoadOrCreateImageProject(image.Id));
            AssertWatermark(host, "《改名后的诗歌》");
            Assert.Equal("保存的歌词", GetProject(host).Content);
        });
    }

    [Fact]
    public void ImageSwitch_EmptyThirdImageStillUsesItsOwnFilename()
    {
        WithFixture((host, db, image) =>
        {
            foreach (var name in new[] { "图二", "图三" })
            {
                var next = new MediaFile { Name = name, Path = @"D:\fixture\" + name + ".jpg" };
                db.MediaFiles.Add(next); db.SaveChanges();
                Invoke(host, "SetLyricsEntryByImage", next.Id);
                Set(host, "_currentLyricsProject", new LyricsProjectManager(db).LoadOrCreateImageProject(next.Id));
                Assert.Equal("", GetProject(host).Content);
                AssertWatermark(host, $"《{name}》");
            }
        });
    }

    [Fact]
    public void MissingImage_DoesNotLeakProjectNameOrPreviousImage()
    {
        WithFixture((host, db, image) =>
        {
            db.MediaFiles.Remove(image); db.SaveChanges();
            AssertWatermark(host, "");
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9999)]
    public void InvalidImageEntry_DoesNotUseStaleImage(int id)
    {
        WithFixture((host, db, image) =>
        {
            Invoke(host, "SetLyricsEntryByImage", id);
            AssertWatermark(host, "");
        });
    }

    [Fact]
    public void ImageWithMissingPath_FallsBackToItsStoredFilename()
    {
        WithFixture((host, db, image) =>
        {
            image.Path = ""; image.Name = "013.备用文件名"; db.SaveChanges();
            AssertWatermark(host, "《013.备用文件名》");
        });
    }

    [Fact]
    public void NonImageBinding_DoesNotShowAVideoFilename()
    {
        WithFixture((host, db, image) =>
        {
            image.FileTypeString = "video"; db.SaveChanges();
            AssertWatermark(host, "");
        });
    }

    [Theory]
    [InlineData("歌词_12、独立歌曲", "《独立歌曲》")]
    [InlineData("普通歌曲", "《普通歌曲》")]
    [InlineData("", "")]
    public void LibrarySong_UsesSongNameAndNeverPreviousImage(string name, string expected)
    {
        WithFixture((host, db, image) =>
        {
            Invoke(host, "SetLyricsEntryBySong", 25);
            Set(host, "_currentLyricsProject", new LyricsProject { Id = 25, SourceType = 1, Name = name });
            AssertWatermark(host, expected);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectionCanvas_RendersBoundFilenameWithExistingWatermarkStyle(bool placeTop)
    {
        WithFixture((host, db, image) =>
        {
            Set(host, "_lyricsTextWatermarkColorHex", "#FFFFFF");
            Set(host, "_lyricsTextWatermarkFontSize", 30d);
            var canvas = new WpfCanvas { Width = 640, Height = 360, Background = Brushes.Black };
            Invoke(host, "AddSongNameWatermarkToProjection", canvas, 640d, 360d, 1d, placeTop);
            var block = Assert.IsType<TextBlock>(Assert.Single(canvas.Children.Cast<UIElement>()));
            Console.WriteLine($"WATERMARK_CANVAS: top={placeTop}; text={block.Text}; font={block.FontSize}; z={Panel.GetZIndex(block)}");
            Assert.Equal("《原图赞美诗》", block.Text);
            Assert.Equal(30d, block.FontSize);
            Assert.Equal(1200, Panel.GetZIndex(block));
            Assert.Equal(22d, WpfCanvas.GetLeft(block));
            Assert.Equal(placeTop ? 18d : 360d - block.DesiredSize.Height - 18d, WpfCanvas.GetTop(block));
            canvas.Measure(new Size(640, 360)); canvas.Arrange(new Rect(0, 0, 640, 360)); canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
            var pixels = new byte[640 * 360 * 4]; bitmap.CopyPixels(pixels, 640 * 4, 0);
            Assert.Contains(pixels.Where((_, i) => i % 4 != 3), value => value > 0);
            string output = Environment.GetEnvironmentVariable("CANVAS_WATERMARK_EVIDENCE");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                using var stream = File.Create(Path.Combine(output, placeTop ? "watermark-top.png" : "watermark-bottom.png"));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
            }
        });
    }

    private static void AssertWatermark(MainWindow host, string expected)
    {
        var actual = (string)Invoke(host, "GetCurrentLyricsSongWatermarkText");
        Console.WriteLine($"WATERMARK: expected={expected}; actual={actual}");
        Assert.Equal(expected, actual);
    }
    private static LyricsProject GetProject(MainWindow host) => (LyricsProject)typeof(MainWindow).GetField("_currentLyricsProject", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(obj, value);
    private static object Invoke(object obj, string name, params object[] args)
    {
        try { return obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(obj, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static void WithFixture(Action<MainWindow, CanvasDbContext, MediaFile> action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "lyrics-watermark-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using var db = new CanvasDbContext(path); db.Database.EnsureCreated();
                var image = new MediaFile { Name = "旧显示别名", Path = @"D:\fixture\原图赞美诗.jpg" };
                db.MediaFiles.Add(image); db.SaveChanges();
                var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                Set(host, "_dbContext", db); Set(host, "_currentImageId", image.Id); Set(host, "_imagePath", image.Path);
                Invoke(host, "SetLyricsEntryByImage", image.Id);
                Set(host, "_currentLyricsProject", new LyricsProjectManager(db).LoadOrCreateImageProject(image.Id));
                action(host, db, image);
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                SqliteConnection.ClearAllPools();
                foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
