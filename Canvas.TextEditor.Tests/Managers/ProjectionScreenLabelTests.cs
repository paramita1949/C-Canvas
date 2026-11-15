using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImageColorChanger.Managers;

namespace ImageColorChanger.CanvasTextEditor.Tests.Managers;

public class ProjectionScreenLabelTests
{
    static Type Policy => typeof(ProjectionManager).Assembly.GetType("ImageColorChanger.Managers.ProjectionScreenLabel");
    static WpfScreenInfo Screen(string name, bool primary, string device = "DEVICE")
    {
        var s = new WpfScreenInfo { IsPrimary = primary, DeviceName = device };
        var property = typeof(WpfScreenInfo).GetProperty("FriendlyName"); Assert.NotNull(property); property.SetValue(s, name); return s;
    }
    static string Label(WpfScreenInfo screen, int index)
    { Assert.NotNull(Policy); return (string)Policy.GetMethod("GetLabel").Invoke(null, new object[] { screen, index }); }

    [Theory]
    [InlineData("DELL U2720Q", true, 0, "DELL U2720Q（主显示器）")]
    [InlineData("EPSON PJ", false, 1, "EPSON PJ（扩展显示器）")]
    [InlineData(null, true, 0, "显示器1（主显示器）")]
    [InlineData("  ", false, 1, "显示器2（扩展显示器）")]
    [InlineData(" \r\nDELL\t U2720Q ", false, 2, "DELL U2720Q（扩展显示器）")]
    public void FriendlyNameAndRoleHaveOneCanonicalMapping(string name, bool primary, int index, string expected)
        => Assert.Equal(expected, Label(Screen(name, primary), index));

    [Fact]
    public void PublicMonitorListUsesSameLabels()
    {
        var s = Screen("EPSON PJ", false);
        var manager = (ProjectionManager)RuntimeHelpers.GetUninitializedObject(typeof(ProjectionManager));
        typeof(ProjectionManager).GetField("_screens", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, new List<WpfScreenInfo> { s });
        Assert.Equal(Label(s, 0), manager.GetMonitorInfo()[0].Name);
    }

    [Fact]
    public void DuplicateProductNamesNeverChangeDeviceIdentitySelection()
    {
        var one = Screen("Same Model", false, "ONE"); var two = Screen("Same Model", false, "TWO");
        Assert.Equal(Label(one, 0), Label(two, 1));
        Assert.Equal(1, ProjectionPlacementPolicy.ResolveSelectedIndex(new[] { one, two }, 0, new[] { two, one }));
    }

    [Theory]
    [InlineData("PathInfo", 72)]
    [InlineData("ModeInfo", 64)]
    [InlineData("SourceName", 84)]
    [InlineData("TargetName", 420)]
    public void NativeFriendlyNameInteropHasCorrectPacketSizes(string name, int size)
    {
        var resolver = typeof(ProjectionManager).Assembly.GetType("ImageColorChanger.Managers.MonitorDisplayNameResolver"); Assert.NotNull(resolver);
        Assert.Equal(size, Marshal.SizeOf(resolver.GetNestedType(name, BindingFlags.NonPublic)));
    }
}
