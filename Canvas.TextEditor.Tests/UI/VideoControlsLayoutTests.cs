using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace Canvas.TextEditor.Tests.UI;

public sealed class VideoControlsLayoutTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void NativeProbeReportsScreenSelectorDisplacement() => OnSta(() =>
    {
        var f = CreateFixture(1600, 900, 370, false);
        var before = Bounds(f.Selector, f.Root);
        f.Panel.Visibility = Visibility.Visible;
        Layout(f.Root, 1600, 900);
        var after = Bounds(f.Selector, f.Root);
        Console.WriteLine($"MEDIA_LAYOUT_PROBE: hiddenSelectorY={before.Y:F2}; visibleSelectorY={after.Y:F2}; displacement={after.Y - before.Y:F2}; controlsHeight={f.Panel.ActualHeight:F2}; selectorHit={IsSelectorHit(f)}");
        Save(f.Root, "probe-visible");
        Assert.True(f.Panel.ActualHeight > 40);
    });

    [Theory]
    [InlineData(1600, 900, 370, false)]
    [InlineData(1418, 710, 270, false)]
    [InlineData(1920, 1000, 450, true)]
    [InlineData(1000, 650, 220, false)]
    [InlineData(800, 480, 200, true)]
    public void MediaVisibilityOnlyConsumesRightSideHeight(int width, int height, int navigationWidth, bool emptyTree) => OnSta(() =>
    {
        var f = CreateFixture(width, height, navigationWidth, emptyTree);
        var hiddenSelector = Bounds(f.Selector, f.Root);
        var hiddenTree = Bounds(f.Tree, f.Root);
        var hiddenSplitter = Bounds(f.Splitter, f.Root);
        var hiddenViewport = Bounds(f.Viewport, f.Root);
        foreach (var state in new[] { "video", "audio", "projection-on", "projection-off" })
        {
            f.Panel.Visibility = Visibility.Visible;
            Layout(f.Root, width, height);
            var selector = Bounds(f.Selector, f.Root);
            var controls = Bounds(f.Media, f.Root);
            var viewport = Bounds(f.Viewport, f.Root);
            Console.WriteLine($"MEDIA_LAYOUT: {width}x{height}; navigation={navigationWidth}; empty={emptyTree}; state={state}; hiddenY={hiddenSelector.Y:F2}; visibleY={selector.Y:F2}; barHeight={controls.Height:F2}; viewportBottom={viewport.Bottom:F2}; controlsTop={controls.Top:F2}");
            Save(f.Root, $"visible-{width}x{height}-{state}");
            Assert.Equal(hiddenSelector, selector);
            Assert.Equal(hiddenTree, Bounds(f.Tree, f.Root));
            Assert.Equal(hiddenSplitter, Bounds(f.Splitter, f.Root));
            Assert.InRange(Math.Abs(controls.Left - viewport.Left), 0, 0.01);
            Assert.InRange(Math.Abs(controls.Right - viewport.Right), 0, 0.01);
            Assert.InRange(Math.Abs(viewport.Bottom - controls.Top), 0, 0.01);
            Assert.InRange(Math.Abs(controls.Bottom - height), 0, 0.01);
            Assert.False(controls.IntersectsWith(selector));
            Assert.True(IsSelectorHit(f));
        }
        f.Panel.Visibility = Visibility.Collapsed;
        Layout(f.Root, width, height);
        Assert.Equal(hiddenSelector, Bounds(f.Selector, f.Root));
        Assert.Equal(hiddenViewport, Bounds(f.Viewport, f.Root));
        Save(f.Root, $"hidden-restored-{width}x{height}");
    });

    [Fact]
    public void NavigationResizeAndEmptyFilterDoNotMoveScreenSelector() => OnSta(() =>
    {
        var f = CreateFixture(1418, 710, 270, false);
        f.Panel.Visibility = Visibility.Visible;
        Layout(f.Root, 1418, 710);
        var y = Bounds(f.Selector, f.Root).Y;
        f.Tree.Items.Clear(); // Empty search/filter result, not removal of the navigation host.
        ((Grid)f.Root.FindName("MainContentGrid")).ColumnDefinitions[0].Width = new GridLength(450);
        Layout(f.Root, 1418, 710);
        Assert.Equal(y, Bounds(f.Selector, f.Root).Y);
        Assert.Equal(453, Bounds(f.Media, f.Root).Left);
        Assert.True(IsSelectorHit(f));
    });

    [Fact]
    public void PlaybackControlsAreNotDescendantsOfVideoProjectionSurface()
    {
        var xml = XElement.Load(Path.Combine(SourceRoot(), "UI/MainWindow.xaml"));
        var media = Named(xml, "MediaPlayerSectionView");
        Assert.DoesNotContain(media.Ancestors(), e => (string)e.Attribute(X + "Name") == "VideoContainer");
        Assert.Equal("2", (string)media.Attribute("Grid.Column"));
        Assert.Equal("MainContentGrid", (string)media.Parent.Attribute(X + "Name"));
    }

    private sealed record Fixture(Grid Root, ComboBox Selector, TreeView Tree, GridSplitter Splitter, FrameworkElement Viewport, UserControl Media, Border Panel);

    // Use the actual owning XAML's Grid definitions, spans, footer and playback markup.
    // Heavy image/Bible/lyrics/business surfaces are replaced with an inert viewport;
    // no MainWindow constructor, user database, VLC decoder or external screen is opened.
    private static Fixture CreateFixture(int width, int height, int navigationWidth, bool emptyTree)
    {
        var xml = XElement.Load(Path.Combine(SourceRoot(), "UI/MainWindow.xaml"));
        var main = Named(xml, "MainLayoutGrid");
        var content = Named(xml, "MainContentGrid");
        var sidebar = content.Elements(Wpf + "Border").First(e => (string)e.Attribute("Grid.Column") == "0");
        var sideGrid = sidebar.Element(Wpf + "Grid");
        var footer = sideGrid.Elements(Wpf + "Border").Single(e => e.Descendants().Any(d => (string)d.Attribute(X + "Name") == "ScreenSelector"));
        var split = Named(xml, "NavigationSplitter");
        var right = content.Elements(Wpf + "Grid").Single(e => (string)e.Attribute("Grid.Column") == "2");
        var media = Named(xml, "MediaPlayerSectionView");
        var mediaMarkup = XElement.Load(Path.Combine(SourceRoot(), "UI/Views/MediaPlayerSectionView.xaml"));
        var tree = new XElement(Wpf + "TreeView", new XAttribute(X + "Name", "LayoutTree"), new XAttribute("Grid.Row", "1"));
        if (!emptyTree)
            foreach (var title in new[] { "123", "光线与粒子", "光线与粒子 蓝 1" })
                tree.Add(new XElement(Wpf + "TreeViewItem", new XAttribute("Header", title)));
        var root = Shell(main);
        root.Add(new XElement(Wpf + "Border", new XAttribute("Grid.Row", "0"), new XAttribute("Height", "60"), new XAttribute("Background", "#FAFAFA")));
        var contentCopy = Shell(content);
        contentCopy.Element(Wpf + "Grid.ColumnDefinitions").Elements().First().SetAttributeValue("Width", navigationWidth);
        var sidebarCopy = Shell(sidebar);
        var side = Shell(sideGrid);
        side.Add(new XElement(Wpf + "Border", new XAttribute("Grid.Row", "0"), new XAttribute("Height", "155"),
            new XElement(Wpf + "TextBlock", new XAttribute("Text", "搜索   图片 / 媒体\n全部\n文件    圣经    幻灯片"), new XAttribute("Margin", "8"))));
        side.Add(tree, new XElement(footer));
        sidebarCopy.Add(side);
        contentCopy.Add(sidebarCopy, new XElement(split));
        var rightCopy = Shell(right);
        rightCopy.Add(new XElement(Wpf + "Border", new XAttribute(X + "Name", "LayoutViewport"), new XAttribute("Background", "#103B63")));
        contentCopy.Add(rightCopy);
        var mediaCopy = new XElement(Wpf + "UserControl", media.Attributes(), new XElement(mediaMarkup.Element(Wpf + "Border")));
        // Retain the original parent so the fixture fails on the global Auto-row bug.
        if (ReferenceEquals(media.Parent, main)) root.Add(mediaCopy);
        else contentCopy.Add(mediaCopy);
        root.Add(contentCopy);
        foreach (var e in root.DescendantsAndSelf())
            foreach (var a in e.Attributes().ToArray())
                if (a.Name == X + "Class" || a.Value.Contains("{StaticResource") || a.Value.Contains("{Binding") ||
                    a.Name.LocalName is "DragCompleted" or "Click" or "SizeChanged" or "MouseMove")
                    a.Remove();
        var grid = (Grid)XamlReader.Parse(root.ToString());
        var selector = (ComboBox)grid.FindName("ScreenSelector");
        selector.Items.Add("显示器2"); selector.SelectedIndex = 0;
        var result = new Fixture(grid, selector, (TreeView)grid.FindName("LayoutTree"), (GridSplitter)grid.FindName("NavigationSplitter"),
            (FrameworkElement)grid.FindName("LayoutViewport"), (UserControl)grid.FindName("MediaPlayerSectionView"), (Border)grid.FindName("MediaPlayerPanel"));
        Layout(grid, width, height);
        return result;
    }

    private static XElement Shell(XElement element) => new(element.Name, element.Attributes(),
        element.Elements().Where(e => e.Name.LocalName.EndsWith(".RowDefinitions") || e.Name.LocalName.EndsWith(".ColumnDefinitions")).Select(e => new XElement(e)));
    private static XElement Named(XElement root, string name) => root.DescendantsAndSelf().Single(e => (string)e.Attribute(X + "Name") == name);
    private static Rect Bounds(FrameworkElement element, Visual root) => new(element.TranslatePoint(new Point(), (UIElement)root), element.RenderSize);
    private static void Layout(Grid root, int width, int height) { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
    private static bool IsSelectorHit(Fixture f)
    {
        var hit = VisualTreeHelper.HitTest(f.Root, f.Selector.TranslatePoint(new Point(f.Selector.ActualWidth / 2, f.Selector.ActualHeight / 2), f.Root))?.VisualHit;
        for (var visual = hit; visual != null; visual = VisualTreeHelper.GetParent(visual)) if (ReferenceEquals(visual, f.Selector)) return true;
        return false;
    }
    private static string SourceRoot()
    {
        var source = Environment.GetEnvironmentVariable("CANVAS_MEDIA_SOURCE_ROOT");
        if (!string.IsNullOrEmpty(source)) return source;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "UI/MainWindow.xaml"))) return dir.FullName;
        throw new DirectoryNotFoundException("Canvas source root not found.");
    }
    private static void Save(Grid root, string name)
    {
        var folder = Environment.GetEnvironmentVariable("CANVAS_MEDIA_EVIDENCE"); if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(file);
    }
    private static void OnSta(Action action)
    {
        Exception error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Layout fixture timed out.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
