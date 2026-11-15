using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Canvas.TextEditor.Tests.UI;

public class ProjectionScreenSelectorTests
{
    static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    static XElement Selector()
    {
        var env = Environment.GetEnvironmentVariable("CANVAS_MEDIA_SOURCE_ROOT");
        var root = new DirectoryInfo(env ?? AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "UI/MainWindow.xaml"))) root = root.Parent;
        Assert.NotNull(root);
        return XElement.Load(Path.Combine(root.FullName, "UI/MainWindow.xaml")).Descendants(Wpf + "ComboBox").Single(e => (string)e.Attribute(X + "Name") == "ScreenSelector");
    }
    [Fact]
    public void CompactSelectorHasNoSeparateProjectionHeading()
    {
        var selector = Selector();
        Assert.DoesNotContain(selector.Ancestors().First(e => e.Name == Wpf + "Border").Descendants(Wpf + "TextBlock"), e => (string)e.Attribute("Text") == "投影屏幕");
        Assert.Equal(Wpf + "Border", selector.Parent.Name);
    }
    [Fact]
    public void LongNamesKeepExistingStyleAndHaveFullTooltip()
    {
        var selector = Selector();
        Assert.Equal("32", (string)selector.Attribute("Height")); Assert.Equal("12", (string)selector.Attribute("FontSize"));
        Assert.Equal("{StaticResource WhiteComboBoxPopup}", (string)selector.Attribute("Style"));
        Assert.Equal("{StaticResource WhiteComboBoxItem}", (string)selector.Attribute("ItemContainerStyle"));
        Assert.Contains("SelectedItem", (string)selector.Attribute("ToolTip"));
        Assert.Contains(selector.Descendants(Wpf + "TextBlock"), e => (string)e.Attribute("TextTrimming") == "CharacterEllipsis");
        Assert.Contains(selector.Descendants(Wpf + "TextBlock"), e => (string)e.Attribute("ToolTip") == "{Binding}");
    }
}
