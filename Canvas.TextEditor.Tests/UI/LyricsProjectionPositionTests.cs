using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using ImageColorChanger.UI;
using ImageColorChanger.UI.Views;
using SkiaSharp;
using Xunit;

namespace Canvas.TextEditor.Tests.UI;

[Collection("Lyrics input exclusive")]
public sealed class LyricsProjectionPositionTests
{
    [Fact]
    public void OldConfig_PreservesExistingDefaultLayout() => WithHost((host, config, path, slider) =>
    {
        Assert.NotNull(PositionProperty);
        Assert.Null(PositionProperty.GetValue(config));
        using var frame = Render(host);
        Assert.InRange(Top(frame), 40, 75);
        var canvas = Split(host);
        Assert.InRange(canvas.Children[0].RenderSize.Height, 1, 360);
    });

    [Theory]
    [InlineData(0d)]
    [InlineData(.25d)]
    [InlineData(.75d)]
    [InlineData(1d)]
    public void Position_PersistsAcrossConfigRecreation(double position) => WithHost((host, config, path, slider) =>
    {
        Assert.NotNull(PositionProperty);
        PositionProperty.SetValue(config, position);
        var reloaded = new ConfigManager(path);
        Assert.Equal(position, (double)PositionProperty.GetValue(reloaded));
        Console.WriteLine($"CONFIG_RELOAD: expected={position}; actual={PositionProperty.GetValue(reloaded)}");
    });

    [Theory]
    [InlineData(-.5, 0d)]
    [InlineData(1.5, 1d)]
    [InlineData(double.NaN, null)]
    [InlineData(double.PositiveInfinity, null)]
    public void InvalidPosition_IsSafeAndSerializable(double input, double? expected) => WithHost((host, config, path, slider) =>
    {
        Assert.NotNull(PositionProperty);
        PositionProperty.SetValue(config, input);
        Assert.Equal(expected, (double?)PositionProperty.GetValue(config));
        config.SaveConfig();
        Assert.Equal(expected, (double?)PositionProperty.GetValue(new ConfigManager(path)));
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Slider_IsVisibleOnlyDuringLyricsProjection(bool lyrics, bool projection, bool visible) => WithHost((host, config, path, slider) =>
    {
        Set(host, "_isLyricsMode", lyrics);
        Invoke(host, "UpdateLyricsProjectionPositionControl", projection);
        var view = Get(host, "LyricsSectionView");
        Assert.Equal(visible ? Visibility.Visible : Visibility.Collapsed,
            ((FrameworkElement)Get(view, "LyricsProjectionPositionPanel")).Visibility);
    });

    [Fact]
    public void SliderChange_PersistsAndQueuesProjectionWithoutEditingLyrics() => WithHost((host, config, path, slider) =>
    {
        Set(host, "_isLyricsMode", true);
        var editor = ((LyricsSectionView)Get(host, "LyricsSectionView")).LyricsTextBoxEditor;
        editor.Select(2, 2);
        var before = editor.Text;
        Invoke(host, "InitializeLyricsSectionBindings");
        Invoke(host, "UpdateLyricsProjectionPositionControl", true);
        slider.Value = 73;
        Assert.NotNull(PositionProperty);
        Assert.Equal(.73, (double)PositionProperty.GetValue(new ConfigManager(path)), 8);
        Assert.True((bool)Get(host, "_lyricsProjectionRenderQueued"));
        Assert.Equal(before, editor.Text);
        Assert.Equal(2, editor.SelectionStart); Assert.Equal(2, editor.SelectionLength);
        Set(host, "_isLyricsMode", false);
        Invoke(host, "UpdateLyricsProjectionPositionControl", false);
        Set(host, "_isLyricsMode", true);
        Invoke(host, "UpdateLyricsProjectionPositionControl", true);
        Assert.Equal(73d, slider.Value);
    });

    [Fact]
    public void SliderRestore_DoesNotOverwriteStoredPosition() => WithHost((host, config, path, slider) =>
    {
        Assert.NotNull(PositionProperty);
        PositionProperty.SetValue(config, .66d);
        Invoke(host, "InitializeLyricsSectionBindings");
        Set(host, "_isLyricsMode", true);
        Invoke(host, "UpdateLyricsProjectionPositionControl", true);
        Assert.Equal(66d, slider.Value);
        Assert.Equal(.66, (double)PositionProperty.GetValue(new ConfigManager(path)), 8);
        Assert.False((bool)Get(host, "_lyricsProjectionRenderQueued"));
    });

    [Fact]
    public void SingleFrame_MovesDownAndKeepsEditorAndWatermarkAnchors() => WithHost((host, config, path, slider) =>
    {
        SetPosition(config, 0);
        using var top = Render(host);
        SetPosition(config, .75);
        using var lower = Render(host);
        Console.WriteLine($"POSITION_FRAME: top={Top(top)}; lower={Top(lower)}; height={lower.Height}");
        Save(top, "projection-top.png"); Save(lower, "projection-lower.png");
        Assert.True(Top(lower) - Top(top) > 100, "Moving the slider must move rendered lyrics down.");
        Assert.Equal(360, lower.Height);
        var editor = ((LyricsSectionView)Get(host, "LyricsSectionView")).LyricsTextBoxEditor;
        Assert.Equal("第一行歌词\r\n第二行歌词", editor.Text);
    });

    [Fact]
    public void LongLyrics_KeepFullScrollableHeight() => WithHost((host, config, path, slider) =>
    {
        var editor = ((LyricsSectionView)Get(host, "LyricsSectionView")).LyricsTextBoxEditor;
        editor.Text = string.Join("\r\n", Enumerable.Repeat("长歌词仍然完整", 25));
        SetPosition(config, 0); using var top = Render(host);
        SetPosition(config, 1); using var bottom = Render(host);
        Assert.True(top.Height > 360);
        Assert.Equal(top.Height, bottom.Height);
        Assert.Equal(Top(top), Top(bottom));
        Assert.Equal(top.Pixels, bottom.Pixels);
    });

    [Fact]
    public void TransparentNdi_KeepsInvertedPositionMapping() => WithHost((host, config, path, slider) =>
    {
        SetPosition(config, .2); using var first = Render(host, true);
        SetPosition(config, .8); using var second = Render(host, true);
        Console.WriteLine($"NDI_POSITION: first={Top(first)}; second={Top(second)}");
        Assert.True(Top(first) - Top(second) > 80);
    });

    [Fact]
    public void SplitRegion_PositionMovesInsideItsOwnBounds() => WithHost((host, config, path, slider) =>
    {
        SetPosition(config, 0); var top = Split(host);
        SetPosition(config, 1); var bottom = Split(host);
        int TopOf(System.Windows.Controls.Canvas canvas)
        {
            var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
            byte[] pixels = new byte[640 * 360 * 4]; bitmap.CopyPixels(pixels, 640 * 4, 0);
            for (int y = 0; y < 360; y++) for (int x = 0; x < 640; x++)
                if (pixels[(y * 640 + x) * 4] > 200) return y;
            return -1;
        }
        Console.WriteLine($"SPLIT_POSITION: top={TopOf(top)}; bottom={TopOf(bottom)}");
        Assert.True(TopOf(bottom) - TopOf(top) > 200);
        Assert.InRange(TopOf(bottom), 0, 359);
    });

    [Theory]
    [InlineData(0d)]
    [InlineData(1d)]
    public void SplitRegion_LongLyricsDoNotPaintOverOtherRegions(double position) => WithHost((host, config, path, slider) =>
    {
        SetPosition(config, position);
        var canvas = new System.Windows.Controls.Canvas { Width = 640, Height = 360, Background = Brushes.Black };
        Invoke(host, "AddSplitRegionTextElement", canvas, new Rect(0, 0, 640, 180),
            new TextBox { Foreground = Brushes.White }, string.Join("\r\n", Enumerable.Repeat("长歌词分区边界", 12)), 1d, 30d);
        canvas.Measure(new Size(640, 360)); canvas.Arrange(new Rect(0, 0, 640, 360)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
        byte[] pixels = new byte[640 * 360 * 4]; bitmap.CopyPixels(pixels, 640 * 4, 0);
        int inside = 0, outside = 0;
        for (int y = 0; y < 360; y++) for (int x = 0; x < 640; x++)
        {
            if (pixels[(y * 640 + x) * 4] <= 200) continue;
            if (y < 180) inside++; else outside++;
        }
        Console.WriteLine($"SPLIT_CLIP: position={position}; pixelsInside={inside}; pixelsOutside={outside}");
        Assert.True(inside > 0);
        Assert.Equal(0, outside);
    });

    [Theory]
    [InlineData(1600, 760)]
    [InlineData(1024, 640)]
    [InlineData(800, 480)]
    [InlineData(640, 280)]
    public void EditorMarkup_FloatingSliderDoesNotResizeEditorAndStaysInsideBounds(int width, int height) => WithHost((host, config, path, slider) =>
    {
        string root = Environment.GetEnvironmentVariable("CANVAS_TEST_SOURCE_ROOT");
        if (string.IsNullOrEmpty(root))
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ImageColorChanger.csproj")))
                {
                    root = directory.FullName;
                    break;
                }
            }
        }
        Assert.False(string.IsNullOrEmpty(root));
        var xml = XElement.Load(Path.Combine(root, "UI/Views/LyricsSectionView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        xml.Attribute(x + "Class")?.Remove();
        xml.AddFirst(new XElement(wpf + "UserControl.Resources",
            new XElement(wpf + "ResourceDictionary",
                new XElement(wpf + "ResourceDictionary.MergedDictionaries",
                    new XElement(wpf + "ResourceDictionary", new XAttribute("Source",
                        "pack://application:,,,/CanvasCast;component/UI/Resources/Styles/MainWindow.BaseStyles.xaml"))))));
        var view = (UserControl)XamlReader.Parse(xml.ToString());
        var actualSlider = view.FindName("LyricsProjectionPositionSlider") as Slider;
        Assert.NotNull(actualSlider);
        Assert.Equal(Orientation.Vertical, actualSlider.Orientation);
        Assert.True(actualSlider.IsDirectionReversed);
        Assert.Equal(1, xml.Descendants(wpf + "Slider").Count());
        var panel = (FrameworkElement)view.FindName("LyricsProjectionPositionPanel");
        Assert.Equal(Visibility.Collapsed, panel.Visibility);
        ((Grid)view.FindName("LyricsEditorPanel")).Visibility = Visibility.Visible;
        ((FrameworkElement)view.FindName("LyricsSlicePanel")).Visibility = Visibility.Visible;
        ((TextBox)view.FindName("LyricsTextBox")).Text = "第一行歌词\r\n第二行歌词";
        var editor = (ScrollViewer)view.FindName("LyricsScrollViewer");
        void Layout()
        {
            view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        }
        Layout();
        var editorSizeWithoutSlider = editor.RenderSize;
        panel.Visibility = Visibility.Visible;
        actualSlider.Value = 73;
        Layout();
        var editorSizeWithSlider = editor.RenderSize;
        Assert.InRange(actualSlider.ActualWidth, 12, 60);
        Assert.True(actualSlider.ActualHeight > 30);
        Console.WriteLine($"FLOATING_LAYOUT: window={width}x{height}; hidden-editor={editorSizeWithoutSlider.Width}x{editorSizeWithoutSlider.Height}; visible-editor={editorSizeWithSlider.Width}x{editorSizeWithSlider.Height}; slider-panel={panel.ActualWidth}x{panel.ActualHeight}");
        string folder = Environment.GetEnvironmentVariable("CANVAS_POSITION_EVIDENCE");
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            using var stream = File.Create(Path.Combine(folder, $"editor-floating-{width}x{height}.png"));
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        }
        Assert.Equal(editorSizeWithoutSlider, editorSizeWithSlider);
        Point position = panel.TranslatePoint(new Point(0, 0), editor);
        Assert.InRange(position.X, 0, editor.ActualWidth - panel.ActualWidth);
        Assert.InRange(position.Y, 0, editor.ActualHeight - panel.ActualHeight);
        Assert.InRange(panel.ActualHeight, 60, 320);
        var slices = (FrameworkElement)view.FindName("LyricsSlicePanel");
        Assert.True(panel.TranslatePoint(new Point(panel.ActualWidth, 0), view).X
            < slices.TranslatePoint(new Point(0, 0), view).X);
        // 浮层外仍能命中编辑器，不能用覆盖整个编辑区的透明容器拦截输入。
        var hit = VisualTreeHelper.HitTest(view, editor.TranslatePoint(new Point(10, 10), view))?.VisualHit;
        Assert.NotNull(hit);
        while (hit != null && !ReferenceEquals(hit, editor))
        {
            Assert.NotSame(panel, hit);
            hit = VisualTreeHelper.GetParent(hit);
        }
        Assert.Same(editor, hit);
        panel.Visibility = Visibility.Collapsed;
        Layout();
        Assert.Equal(editorSizeWithoutSlider, editor.RenderSize);
    });

    private static PropertyInfo PositionProperty => typeof(ConfigManager).GetProperty("LyricsProjectionVerticalPosition");
    private static void SetPosition(ConfigManager config, double value) => PositionProperty?.SetValue(config, value);
    private static SKBitmap Render(MainWindow host, bool inverted = false) =>
        (SKBitmap)Invoke(host, "BuildSingleLyricsProjectionBitmap", 640d, 360d, inverted);
    private static System.Windows.Controls.Canvas Split(MainWindow host)
    {
        var canvas = new System.Windows.Controls.Canvas { Width = 640, Height = 360, Background = Brushes.Black };
        Invoke(host, "AddSplitRegionTextElement", canvas, new Rect(0, 0, 640, 360), new TextBox { Foreground = Brushes.White }, "分区歌词", 1d, 30d);
        canvas.Measure(new Size(640, 360)); canvas.Arrange(new Rect(0, 0, 640, 360)); canvas.UpdateLayout();
        return canvas;
    }
    private static int Top(SKBitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).Red > 200) return y;
        return -1;
    }
    private static void Save(SKBitmap bitmap, string name)
    {
        string folder = Environment.GetEnvironmentVariable("CANVAS_POSITION_EVIDENCE");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(folder, name)); data.SaveTo(file);
    }
    private static object Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(obj, value);
    private static object Invoke(object obj, string name, params object[] args)
    {
        var method = obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);
        try { return method.Invoke(obj, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static void WithHost(Action<MainWindow, ConfigManager, string, Slider> action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "lyrics-position-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{}");
                var config = new ConfigManager(path);
                var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(host, Dispatcher.CurrentDispatcher);
                var view = (LyricsSectionView)RuntimeHelpers.GetUninitializedObject(typeof(LyricsSectionView));
                Set(host, "LyricsSectionView", view); Set(host, "_configManager", config);
                Set(host, "_lyricsProjectionFontSize", 30d); Set(host, "_lyricsSplitMode", (int)ViewSplitMode.Single);
                Set(host, "_lyricsThemeBackgroundHex", "#000000");
                Set(view, "LyricsTextBox", new TextBox { Text = "第一行歌词\r\n第二行歌词", Foreground = Brushes.White, ContextMenu = new ContextMenu() });
                var slider = new Slider { Minimum = 0, Maximum = 100, Orientation = Orientation.Vertical, IsDirectionReversed = true };
                typeof(LyricsSectionView).GetField("LyricsProjectionPositionSlider", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(view, slider);
                typeof(LyricsSectionView).GetField("LyricsProjectionPositionPanel", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(view, new Border());
                var projection = (ProjectionManager)RuntimeHelpers.GetUninitializedObject(typeof(ProjectionManager));
                var viewport = new ScrollViewer { Width = 640, Height = 360 };
                viewport.Measure(new Size(640, 360)); viewport.Arrange(new Rect(0, 0, 640, 360));
                Set(projection, "_projectionScrollViewer", viewport); Set(projection, "_projectionWindow", new Window());
                Set(host, "_projectionManager", projection);
                action(host, config, path, slider);
            }
            catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); if (File.Exists(path)) File.Delete(path); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF fixture timed out");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
