using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using ImageColorChanger.Managers;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Managers;
public sealed class ProjectionPlacementPolicyTests
{
    private static object Invoke(string method, params object[] args)
    {
        var type = typeof(WpfScreenInfo).Assembly.GetType("ImageColorChanger.Managers.ProjectionPlacementPolicy");
        Assert.NotNull(type);
        try { return type.GetMethod(method).Invoke(null, args); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }

    [Theory]
    [InlineData(-1920, 0, 1.0)]
    [InlineData(-1920, 0, 1.5)]
    [InlineData(-1920, 0, 3.0)]
    [InlineData(1920, 0, 1.0)]
    [InlineData(1920, 0, 1.5)]
    [InlineData(1920, 0, 3.0)]
    [InlineData(0, -1080, 1.0)]
    [InlineData(0, -1080, 1.5)]
    [InlineData(0, -1080, 3.0)]
    [InlineData(0, 1080, 1.0)]
    [InlineData(0, 1080, 1.5)]
    [InlineData(0, 1080, 3.0)]
    [InlineData(-1280, -720, 1.25)]
    [InlineData(1200, -400, 2.0)]
    public void PlacementUsesPhysicalDesktopOriginAndSizeRegardlessOfTargetDpi(int x, int y, double dpi)
    {
        var screen = new WpfScreenInfo {DeviceName="TARGET", PhysicalBounds=new Rect(x,y,1920,1080), DpiScale=(dpi,dpi)};
        Assert.Equal(screen.PhysicalBounds, (Rect)Invoke("GetPhysicalBounds", screen));
    }

    [Fact]
    public void RejectsFractionalPhysicalOrigin()
    {
        var screen=new WpfScreenInfo {PhysicalBounds=new Rect(0.5,0,1920,1080)};
        Assert.Throws<ArgumentException>(()=>Invoke("GetPhysicalBounds",screen));
    }

    [Fact]
    public void RejectsEmptyPhysicalSize()
    {
        var screen=new WpfScreenInfo {PhysicalBounds=new Rect(0,0,0,1080)};
        Assert.Throws<ArgumentException>(()=>Invoke("GetPhysicalBounds",screen));
    }

    private static WpfScreenInfo Screen(string device,bool primary=false,int x=0) => new WpfScreenInfo {DeviceName=device,IsPrimary=primary,PhysicalBounds=new Rect(x,0,1920,1080)};
    [Fact]
    public void ReorderingUsesSelectedDeviceNotListPositionOrLeftRightOrder()
    {
        var cached=new List<WpfScreenInfo>{Screen("HOST",true),Screen("TARGET"),Screen("OTHER")};
        var fresh=new List<WpfScreenInfo>{Screen("HOST",true),Screen("OTHER",false,-1920),Screen("TARGET",false,1920)};
        Assert.Equal(2,(int)Invoke("ResolveSelectedIndex",cached,1,fresh));
    }
    [Fact]
    public void UpdatedCoordinatesComeFromFreshDeviceInstance()
    {
        var cached=new List<WpfScreenInfo>{Screen("HOST",true),Screen("TARGET",false,1920)};
        var fresh=new List<WpfScreenInfo>{Screen("TARGET",false,-1920),Screen("HOST",true)};
        int index=(int)Invoke("ResolveSelectedIndex",cached,1,fresh);
        Assert.Equal(0,index);Assert.Equal(-1920,fresh[index].PhysicalBounds.X);
    }
    [Theory]
    [InlineData(-1)] [InlineData(2)]
    public void InvalidSelectionDoesNotFallBackToPrimaryOrDifferentExternal(int selected)
    {
        var screens=new List<WpfScreenInfo>{Screen("HOST",true),Screen("TARGET")};
        Assert.Equal(-1,(int)Invoke("ResolveSelectedIndex",screens,selected,screens));
    }
    [Fact]
    public void DisconnectedSelectedDeviceDoesNotRedirectToDifferentExternal()
    {
        var cached=new List<WpfScreenInfo>{Screen("HOST",true),Screen("TARGET")};
        var fresh=new List<WpfScreenInfo>{Screen("HOST",true),Screen("OTHER")};
        Assert.Equal(-1,(int)Invoke("ResolveSelectedIndex",cached,1,fresh));
    }
    [Fact]
    public void PrimaryStateIsReadFromFreshDevice()
    {
        var cached=new List<WpfScreenInfo>{Screen("HOST",true),Screen("TARGET")};
        var fresh=new List<WpfScreenInfo>{Screen("HOST"),Screen("TARGET",true)};
        var index=(int)Invoke("ResolveSelectedIndex",cached,1,fresh);
        Assert.Equal(1,index);Assert.True(fresh[index].IsPrimary);
    }
    [Fact]
    public void EmptyFreshDisplaysRejectsSelection()
    {
        Assert.Equal(-1,(int)Invoke("ResolveSelectedIndex",new List<WpfScreenInfo>{Screen("TARGET")},0,new List<WpfScreenInfo>()));
    }
}
