using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Xml.Linq;
using ImageColorChanger.Core;
using Xunit;
namespace ImageColorChanger.CanvasTextEditor.Tests.UI;
public sealed class BibleSpringScrollTests
{
    private static Type Required(string name) { var t=typeof(ConfigManager).Assembly.GetType("ImageColorChanger.UI.Modules."+name); Assert.NotNull(t); return t; }
    private static object Motion(double x=0) => Activator.CreateInstance(Required("BibleVerseSpringMotion"),new object[]{x});
    private static void Call(object m,string method,double v) => m.GetType().GetMethod(method).Invoke(m,new object[]{v});
    private static double Get(object m,string name) => (double)m.GetType().GetProperty(name).GetValue(m);
    private static PropertyInfo Algorithm { get {var p=typeof(ConfigManager).GetProperty("BibleVerseScrollAlgorithm");Assert.NotNull(p);return p;} }
    private static void Mode(ConfigManager c,int value) => Algorithm.SetValue(c,Enum.ToObject(Algorithm.PropertyType,value));
    private static int Mode(ConfigManager c) => Convert.ToInt32(Algorithm.GetValue(c));
    private static PropertyInfo SpeedLevel { get {var p=typeof(ConfigManager).GetProperty("BibleVerseSpringSpeedLevel");Assert.NotNull(p);return p;} }
    private static int Level(ConfigManager c) => (int)SpeedLevel.GetValue(c);
    private static void SetSpeed(object m,int level) => m.GetType().GetMethod("SetSpeedLevel").Invoke(m,new object[]{level});
    [Fact] public void SpeedLevel_DefaultSmoothOneAndSavedValueRemainIndependent() => Temporary(p=>{
        var c=new ConfigManager(p);Assert.Equal(1,Level(c));Assert.Equal(1,Mode(c));c.BibleVerseScrollSpeed=2;c.BibleVerseScrollAnimationEnabled=false;
        SpeedLevel.SetValue(c,2);var reload=new ConfigManager(p);Assert.Equal(2,Level(reload));Assert.Equal(2,reload.BibleVerseScrollSpeed);Assert.False(reload.BibleVerseScrollAnimationEnabled);Assert.Equal(1,Mode(reload));
    });
    [Theory] [InlineData(-1,1)] [InlineData(0,1)] [InlineData(4,3)] [InlineData(999,3)]
    public void SpeedLevel_OutOfRangeClamped(int input,int expected) => Temporary(p=>{
        File.WriteAllText(p,$"{{\"BibleVerseSpringSlowLevel\":{input}}}");var c=new ConfigManager(p);Assert.Equal(expected,Level(c));SpeedLevel.SetValue(c,input);Assert.Equal(expected,Level(c));Assert.Equal(expected,Level(new ConfigManager(p)));
    });
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void SpeedLevel_HistoricalTenLevelsUseBaselineWithoutLoadWrite(int old) => Temporary(p=>{
        // 使用已完成既有AI/NDI迁移的配置，隔离本次圣经档位读取的写盘行为。
        var seed=new ConfigManager(p);seed.LoadConfig();seed.SaveConfig();var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(p)).AsObject();
        node.Remove("BibleVerseSpringSlowLevel");node["BibleVerseSpringSpeedLevel"]=old;node["BibleVerseScrollAlgorithm"]=1;node["BibleVerseScrollSpeed"]=2;node["BibleVerseScrollAnimationEnabled"]=false;
        string json=node.ToJsonString();
        File.WriteAllText(p,json);var c=new ConfigManager(p);Assert.Equal(1,Level(c));Assert.Equal(json,File.ReadAllText(p));Assert.Equal(1,Mode(c));
        SpeedLevel.SetValue(c,1);var reload=new ConfigManager(p);Assert.Equal(1,Level(reload));Assert.Equal(2,reload.BibleVerseScrollSpeed);Assert.False(reload.BibleVerseScrollAnimationEnabled);
        using var parsed=System.Text.Json.JsonDocument.Parse(File.ReadAllText(p));Assert.Equal(old,parsed.RootElement.GetProperty("BibleVerseSpringSpeedLevel").GetInt32());
    });
    [Fact] public void SpeedLevel_NullNewFieldUsesDefaultOne() => Temporary(p=>{File.WriteAllText(p,"{\"BibleVerseSpringSlowLevel\":null}");Assert.Equal(1,Level(new ConfigManager(p)));});
    [Theory] [InlineData(0,0,1)] [InlineData(0,2,3)] [InlineData(1,1,2)] [InlineData(1,2,3)]
    public void Config_ExplicitAlgorithmAndSlowChoiceSurviveDefaults(int mode,int speed,int level) => Temporary(p=>{
        var c=new ConfigManager(p);Mode(c,mode);c.BibleVerseScrollSpeed=speed;SpeedLevel.SetValue(c,level);
        var reload=new ConfigManager(p);Assert.Equal(mode,Mode(reload));Assert.Equal(speed,reload.BibleVerseScrollSpeed);Assert.Equal(level,Level(reload));
    });
    [Theory] [InlineData(0,0)] [InlineData(0,1)] [InlineData(0,2)] [InlineData(1,1)] [InlineData(1,2)] [InlineData(1,3)]
    public void Menu_AnyLeafActivatesCorrespondingAlgorithmFromHistoricalDisabled(int mode,int value) => Temporary(p=>{
        var c=new ConfigManager(p);c.BibleVerseScrollAnimationEnabled=false;var policy=Required("BibleVerseScrollMenuPolicy");
        policy.GetMethod(mode==0?"SelectLegacy":"SelectTestSpeed").Invoke(null,new object[]{c,value});
        var reload=new ConfigManager(p);Assert.Equal(mode,Mode(reload));Assert.True(reload.BibleVerseScrollAnimationEnabled);
        Assert.Equal(value,mode==0?reload.BibleVerseScrollSpeed:Level(reload));
    });
    [Fact]
    public void SpeedLevel_ThreeSlowLevelsAndThreeMatchesOriginalOne()
    {
        double previous=0;
        for(int level=1;level<=3;level++){
            var m=Motion();SetSpeed(m,level);Call(m,"Retarget",100);Call(m,"Advance",.1);
            double x=Get(m,"Position");Assert.True(x>previous);Assert.InRange(x,0,100);previous=x;
            double w=2.5+2.5*level;Assert.Equal(100*(1-(1+w*.1)*Math.Exp(-w*.1)),x,10);
            if(level==3)Assert.Equal(100*(1-2*Math.Exp(-1)),x,10);
            for(int i=0;i<240;i++)Call(m,"Advance",1d/120);Assert.Equal(100,Get(m,"Position"));
        }
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void SpeedLevel_SlowModesAreFrameRateIndependent(int level){double Sample(int hz){var m=Motion();SetSpeed(m,level);Call(m,"Retarget",100);for(int i=0;i<hz/5;i++)Call(m,"Advance",1d/hz);return Get(m,"Position");}Assert.Equal(Sample(60),Sample(120),8);Assert.Equal(Sample(120),Sample(240),8);}
    [Fact]
    public void SpeedLevel_ChangingDuringMotionKeepsPositionVelocityAndTarget()
    {
        var m=Motion();Call(m,"Retarget",100);Call(m,"Advance",.1);double x=Get(m,"Position"),v=Get(m,"Velocity");
        SetSpeed(m,1);Assert.Equal(x,Get(m,"Position"));Assert.Equal(v,Get(m,"Velocity"));Assert.Equal(100,Get(m,"Target"));
    }
    [Fact]
    public void SpeedLevel_ActiveDriverChangesDoNotRestartOrLoseCompletion() => Sta(()=>{
        var viewer=new ScrollViewer{Content=new Border{Height=2000}};
        var window=new System.Windows.Window{Width=250,Height=250,Content=viewer};object driver=null;
        try{
            window.Show();window.UpdateLayout();var type=Required("BibleVerseSpringScroller");driver=Activator.CreateInstance(type,new object[]{viewer});int completed=0;
            type.GetMethod("ScrollTo").Invoke(driver,new object[]{100d,(Action)(()=>completed++)});
            var render=type.GetMethod("OnRendering",BindingFlags.NonPublic|BindingFlags.Instance);
            void Frame(double seconds)=>render.Invoke(driver,new object[]{null,Activator.CreateInstance(typeof(System.Windows.Media.RenderingEventArgs),BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{TimeSpan.FromSeconds(seconds)},null)});
            var motion=type.GetField("_motion",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(driver);
            Frame(10);Frame(10+1d/120);
            foreach(int level in new[]{3,1,3}){
                double x=Get(motion,"Position"),v=Get(motion,"Velocity");SetSpeed(driver,level);
                Assert.Equal(x,Get(motion,"Position"));Assert.Equal(v,Get(motion,"Velocity"));Assert.Equal(100,Get(motion,"Target"));
                Assert.True((bool)type.GetProperty("IsRunning").GetValue(driver));Assert.Equal(0,completed);
            }
            for(int i=2;i<=240;i++){Frame(10+i/120d);Assert.InRange(Get(motion,"Position"),0,100);}
            window.UpdateLayout();Assert.Equal(100,viewer.VerticalOffset);Assert.Equal(1,completed);Assert.False((bool)type.GetProperty("IsRunning").GetValue(driver));
        }finally{if(driver!=null)((IDisposable)driver).Dispose();window.Close();}
    });
    [Fact]
    public void SpeedLevel_MenuSelectionSavesAndReturnsToOldFast() => Sta(()=>Temporary(p=>{
        var c=new ConfigManager(p);Mode(c,0);c.BibleVerseScrollAnimationEnabled=false;c.BibleVerseScrollSpeed=2;
        var parent=new MenuItem();for(int i=1;i<=3;i++)parent.Items.Add(new MenuItem{Tag=i.ToString(),IsCheckable=true});
        var policy=Required("BibleVerseScrollMenuPolicy");
        void Refresh()=>policy.GetMethod("RefreshTestSpeed").Invoke(null,new object[]{c,parent});
        Refresh();Assert.Equal("柔滑滚动",parent.Header.ToString());Assert.False(parent.IsChecked);Assert.All(parent.Items.Cast<MenuItem>(),x=>Assert.False(x.IsChecked));
        policy.GetMethod("SelectTestSpeed").Invoke(null,new object[]{c,2});Refresh();
        Assert.Equal(2,Level(new ConfigManager(p)));Assert.Equal(1,Mode(c));Assert.True(c.BibleVerseScrollAnimationEnabled);Assert.Equal(2,c.BibleVerseScrollSpeed);
        Assert.True(parent.IsChecked);Assert.Single(parent.Items.Cast<MenuItem>().Where(x=>x.IsChecked));Assert.True(((MenuItem)parent.Items[1]).IsChecked);
        policy.GetMethod("SelectLegacy").Invoke(null,new object[]{c,2});Refresh();Assert.Equal(2,Level(c));Assert.False(parent.IsChecked);Assert.All(parent.Items.Cast<MenuItem>(),x=>Assert.False(x.IsChecked));
    }));
    [Fact]
    public void SpeedLevel_RealMenuAndConfigRouteAreWired()
    {
        string root=FindRoot();var x=XDocument.Load(Path.Combine(root,"UI/Views/BibleSectionView.xaml"));XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation",xn="http://schemas.microsoft.com/winfx/2006/xaml";
        var menu=x.Descendants(ns+"MenuItem").Single(e=>(string)e.Attribute(xn+"Name")=="MenuBibleVerseScrollTest");
        Assert.Equal(Enumerable.Range(1,3).Select(i=>i.ToString()),menu.Elements(ns+"MenuItem").Select(e=>(string)e.Attribute("Tag")));
        Assert.All(menu.Elements(ns+"MenuItem"),e=>{Assert.Equal("True",(string)e.Attribute("IsCheckable"));Assert.Null(e.Attribute("Style"));});
        string helpers=File.ReadAllText(Path.Combine(root,"UI/MainWindow.Bible.Helpers.cs")),bindings=File.ReadAllText(Path.Combine(root,"UI/MainWindow.Bible.ViewBindings.cs"));
        Assert.Contains("_bibleSpringScroller.SetSpeedLevel(_configManager.BibleVerseSpringSpeedLevel)",helpers);
        Assert.Contains("item.Click += BibleVerseScrollTestSpeed_Click",bindings);Assert.Contains("RefreshTestSpeed",bindings);Assert.Contains("SelectTestSpeed",bindings);
    }
    private static void Temporary(Action<string> action) {var p=Path.Combine(Path.GetTempPath(),"canvas-scroll-test-"+Guid.NewGuid().ToString("N")+".json");try{action(p);}finally{if(File.Exists(p))File.Delete(p);}}
    [Fact] public void Config_DefaultUsesSmoothOneKeepsClassicSpeedMemory() => Temporary(p=>{var c=new ConfigManager(p);Assert.Equal(1,Mode(c));Assert.Equal(1,Level(c));Assert.Equal(1,c.BibleVerseScrollSpeed);});
    [Fact] public void Config_OldJsonMissingAlgorithmUsesSmoothKeepsSpeedAndCompatibilityFlag() => Temporary(p=>{File.WriteAllText(p,"{\"BibleVerseScrollSpeed\":2,\"BibleVerseScrollAnimationEnabled\":false}");var c=new ConfigManager(p);Assert.Equal(1,Mode(c));Assert.Equal(1,Level(c));Assert.Equal(2,c.BibleVerseScrollSpeed);Assert.False(c.BibleVerseScrollAnimationEnabled);});
    [Fact] public void Config_TestSelectionPersistsWithoutChangingSpeed() => Temporary(p=>{var c=new ConfigManager(p);c.BibleVerseScrollSpeed=2;Mode(c,1);var reload=new ConfigManager(p);Assert.Equal(1,Mode(reload));Assert.Equal(2,reload.BibleVerseScrollSpeed);});
    [Fact] public void Config_UnknownAlgorithmFallsBackToLegacy() => Temporary(p=>{File.WriteAllText(p,"{\"BibleVerseScrollAlgorithm\":99}");Assert.Equal(0,Mode(new ConfigManager(p)));});
    [Fact] public void Motion_InitiallyAtRest() {var m=Motion(15);Assert.Equal(15,Get(m,"Position"));Assert.Equal(0,Get(m,"Velocity"));}
    [Fact] public void Motion_ZeroElapsedDoesNotMove() {var m=Motion();Call(m,"Retarget",100);Call(m,"Advance",0);Assert.Equal(0,Get(m,"Position"));}
    [Fact]
    public void Motion_ShortScrollHasVisibleMiddleInsteadOfFrontLoadedJump()
    {
        var m=Motion();Call(m,"Retarget",100);
        Call(m,"Advance",.1);
        Console.WriteLine($"SHORT_SCROLL_100MS={Get(m,"Position"):F6}");
        Assert.InRange(Get(m,"Position"),30,65);
        Call(m,"Advance",.1);Assert.InRange(Get(m,"Position"),75,95);
    }
    [Fact]
    public void Driver_DelayedFirstFrameDoesNotSkipVisibleAnimation() => Sta(()=>{
        var viewer=new ScrollViewer{Content=new Border{Height=2000}};
        var window=new System.Windows.Window{Width=250,Height=250,Content=viewer};object driver=null;
        try{
            window.Show();window.UpdateLayout();
            var type=Required("BibleVerseSpringScroller");driver=Activator.CreateInstance(type,new object[]{viewer});
            type.GetMethod("ScrollTo").Invoke(driver,new object[]{100d,null});
            Thread.Sleep(150); // Deliberately block UI before the first visible render.
            var render=type.GetMethod("OnRendering",BindingFlags.NonPublic|BindingFlags.Instance);
            void Frame(double seconds)=>render.Invoke(driver,new object[]{null,Activator.CreateInstance(typeof(System.Windows.Media.RenderingEventArgs),BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{TimeSpan.FromSeconds(seconds)},null)});
            var motion=type.GetField("_motion",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(driver);
            Frame(10);Console.WriteLine($"DELAYED_FIRST_FRAME={Get(motion,"Position"):F6}");
            Assert.Equal(0,Get(motion,"Position"));
            for(int i=1;i<=12;i++)Frame(10+i/120d);
            window.UpdateLayout();Assert.InRange(viewer.VerticalOffset,30,65);
            double x=Get(motion,"Position");Frame(10.1);Assert.Equal(x,Get(motion,"Position"));
            Frame(10.6);Assert.InRange(Get(motion,"Position")-x,0,20); // No catch-up jump after a stall.
            double before=Get(motion,"Position"),velocity=Get(motion,"Velocity");
            Thread.Sleep(150);type.GetMethod("ScrollTo").Invoke(driver,new object[]{200d,null});
            Assert.Equal(before,Get(motion,"Position"));Assert.Equal(velocity,Get(motion,"Velocity"));
        }finally{if(driver!=null)((IDisposable)driver).Dispose();window.Close();}
    });
    [Theory] [InlineData(60)] [InlineData(120)] [InlineData(240)]
    public void Motion_MonotonicSingleStepAndExactRest(int hz) {var m=Motion();Call(m,"Retarget",100);double previous=0;for(int i=0;i<hz;i++){Call(m,"Advance",1d/hz);double x=Get(m,"Position");Assert.InRange(x,previous,100);previous=x;}Assert.Equal(100,Get(m,"Position"));Assert.Equal(0,Get(m,"Velocity"));}
    [Fact]
    public void Driver_RealRenderingProducesIntermediateVisibleOffsets() => Sta(()=>{
        var viewer=new ScrollViewer{Content=new Border{Height=2000}};
        var window=new System.Windows.Window{Width=250,Height=250,Content=viewer};object driver=null;
        var frame=new System.Windows.Threading.DispatcherFrame();
        var offsets=new System.Collections.Generic.List<double>();
        var timeout=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
        bool complete=false;EventHandler observe=(_,__)=>offsets.Add(viewer.VerticalOffset);
        timeout.Tick+=(_,__)=>frame.Continue=false;
        try{
            window.Show();window.UpdateLayout();var type=Required("BibleVerseSpringScroller");
            driver=Activator.CreateInstance(type,new object[]{viewer});
            type.GetMethod("ScrollTo").Invoke(driver,new object[]{100d,(Action)(()=>{complete=true;frame.Continue=false;})});
            System.Windows.Media.CompositionTarget.Rendering+=observe;
            Thread.Sleep(150);timeout.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
            window.UpdateLayout();
            Console.WriteLine($"REAL_RENDER_FRAMES={offsets.Count} INTERMEDIATE_OFFSETS={offsets.Count(x=>x>1&&x<99)} FINAL={viewer.VerticalOffset:F6}");
            Assert.True(complete);Assert.True(offsets.Count(x=>x>1&&x<99)>=3);
            Assert.Equal(100,viewer.VerticalOffset);
        }finally{timeout.Stop();System.Windows.Media.CompositionTarget.Rendering-=observe;if(driver!=null)((IDisposable)driver).Dispose();window.Close();}
    });
    [Fact] public void Motion_AnalyticUpdateDoesNotDependOnFrameRate() {double Sample(int hz){var m=Motion();Call(m,"Retarget",100);for(int i=0;i<hz/5;i++)Call(m,"Advance",1d/hz);return Get(m,"Position");}Assert.Equal(Sample(60),Sample(120),8);Assert.Equal(Sample(120),Sample(240),8);}
    [Fact] public void Motion_RetargetKeepsPositionAndVelocity() {var m=Motion();Call(m,"Retarget",100);Call(m,"Advance",.1);double x=Get(m,"Position"),v=Get(m,"Velocity");Assert.True(v>0);Call(m,"Retarget",200);Assert.Equal(x,Get(m,"Position"));Assert.Equal(v,Get(m,"Velocity"));}
    [Fact] public void Motion_ReverseEventuallySettlesWithoutInvalidNumbers() {var m=Motion();Call(m,"Retarget",100);Call(m,"Advance",.08);Call(m,"Retarget",0);for(int i=0;i<240;i++){Call(m,"Advance",1d/120);Assert.True(double.IsFinite(Get(m,"Position")));Assert.True(double.IsFinite(Get(m,"Velocity")));}Assert.Equal(0,Get(m,"Position"));Assert.Equal(0,Get(m,"Velocity"));}
    [Fact] public void Motion_RepeatedSameTargetDoesNotRestart() {var m=Motion();Call(m,"Retarget",100);Call(m,"Advance",.1);double v=Get(m,"Velocity");Call(m,"Retarget",100);Assert.Equal(v,Get(m,"Velocity"));}
    [Fact] public void Motion_LongPauseSettlesExactly() {var m=Motion();Call(m,"Retarget",100);Call(m,"Advance",10);Assert.Equal(100,Get(m,"Position"));Assert.Equal(0,Get(m,"Velocity"));}
    [Theory] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void Motion_InvalidElapsedRejected(double dt) {var m=Motion();var ex=Assert.Throws<TargetInvocationException>(()=>Call(m,"Advance",dt));Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);}
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void Motion_InvalidTargetRejected(double value) {var m=Motion();var ex=Assert.Throws<TargetInvocationException>(()=>Call(m,"Retarget",value));Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);}
    [Fact] public void Menu_TwoAlgorithmGroupsKeepNativeStyle()
    {string root=FindRoot();var x=XDocument.Load(Path.Combine(root,"UI/Views/BibleSectionView.xaml"));XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation",xn="http://schemas.microsoft.com/winfx/2006/xaml";var menu=x.Descendants(ns+"MenuItem").Single(e=>(string)e.Attribute(xn+"Name")=="MenuBibleVerseScroll");Assert.Equal(new[]{"经典滑动","柔滑滚动"},menu.Elements(ns+"MenuItem").Select(e=>(string)e.Attribute("Header")));var legacy=menu.Elements(ns+"MenuItem").ElementAt(0);Assert.Equal(new[]{"慢","中","快"},legacy.Elements(ns+"MenuItem").Select(e=>(string)e.Attribute("Header")));var test=menu.Elements(ns+"MenuItem").ElementAt(1);Assert.Null(test.Attribute("Style"));Assert.Equal(new[]{"1档（最慢）","2档（较慢）","3档（基准，原1档）"},test.Elements(ns+"MenuItem").Select(e=>(string)e.Attribute("Header")));}
    [Fact] public void Menu_DefaultAndSelectionsAreExclusiveAndSaved() => Sta(()=>Temporary(p=>{
        var c=new ConfigManager(p);var items=Enumerable.Range(0,4).Select(_=>new MenuItem{IsCheckable=true}).ToArray();var policy=Required("BibleVerseScrollMenuPolicy");
        void Refresh(){var method=policy.GetMethod("Refresh");Assert.Equal(5,method.GetParameters().Length);method.Invoke(null,new object[]{c,items[0],items[1],items[2],items[3]});}
        Refresh();Assert.Equal(new[]{false,false,false,true},items.Select(i=>i.IsChecked));
        policy.GetMethod("SelectLegacy").Invoke(null,new object[]{c,2});Refresh();Assert.Equal(new[]{false,false,true,false},items.Select(i=>i.IsChecked));Assert.Equal(0,Mode(c));Assert.Equal(2,c.BibleVerseScrollSpeed);
        policy.GetMethod("SelectTest").Invoke(null,new object[]{c});Refresh();Assert.Equal(new[]{false,false,false,true},items.Select(i=>i.IsChecked));Assert.Equal(1,Mode(new ConfigManager(p)));
        c.BibleVerseScrollAnimationEnabled=false;Refresh();Assert.True(items[3].IsChecked);
    }));
    [Fact] public void Menu_LegacyParentUsesAlgorithmNotEnabledOrHeader() => Sta(()=>Temporary(p=>{
        var c=new ConfigManager(p);var parent=new MenuItem{Header="不作为状态来源"};var policy=Required("BibleVerseScrollMenuPolicy");
        void Refresh()=>policy.GetMethod("RefreshLegacy").Invoke(null,new object[]{c,parent});
        Refresh();Assert.False(parent.IsChecked);Mode(c,0);Refresh();Assert.True(parent.IsChecked);c.BibleVerseScrollAnimationEnabled=false;Refresh();Assert.True(parent.IsChecked);
        Mode(c,1);Refresh();Assert.False(parent.IsChecked);Mode(c,99);Refresh();Assert.True(parent.IsChecked);
    }));
    [Fact] public void Route_NewBranchPreservesLegacyAndStopsOnUnloaded()
    {string root=FindRoot();string helpers=File.ReadAllText(Path.Combine(root,"UI/MainWindow.Bible.Helpers.cs"));string bindings=File.ReadAllText(Path.Combine(root,"UI/MainWindow.Bible.ViewBindings.cs"));Assert.Contains("CriticalDampedTest",helpers);Assert.Contains("_bibleSpringScroller.ScrollTo",helpers);Assert.Contains("TimeSpan.FromMilliseconds(16)",helpers);Assert.Contains("1 - Math.Pow(1 - progress, 3)",helpers);Assert.DoesNotContain("MenuBibleVerseScrollTest.Click +=",bindings);Assert.Contains("BibleVerseScrollViewer.Unloaded",bindings);Assert.Contains("StopBibleVerseScrollAnimation();",bindings);}
    [Fact]
    public void Route_ContentReplacementStopsOldAnimationAndRejectsStaleCorrection()
    {
        string root=FindRoot(),core=File.ReadAllText(Path.Combine(root,"UI/MainWindow.Bible.Core.cs"));
        foreach(string name in new[]{"LoadChapterVersesAsync","LoadVerseRangeAsync"})
        {
            int start=core.IndexOf("private async Task "+name,StringComparison.Ordinal);
            Assert.True(start>=0);
            int clear=core.IndexOf("_mergedVerses.Clear();",start,StringComparison.Ordinal);
            Assert.Contains("StopBibleVerseScrollAnimation();",core.Substring(start,clear-start));
        }
        string helpers=File.ReadAllText(Path.Combine(root,"UI/MainWindow.Bible.Helpers.cs"));
        Assert.Contains("generation != _bibleScrollGeneration",helpers);
        int stop=helpers.IndexOf("private void StopBibleVerseScrollAnimation()",StringComparison.Ordinal);
        Assert.Contains("_bibleScrollGeneration++",helpers.Substring(stop,helpers.IndexOf("private void StopLegacy",stop,StringComparison.Ordinal)-stop));
        int completed=helpers.IndexOf("if (progress >= 1)",StringComparison.Ordinal);
        string finish=helpers.Substring(completed,helpers.IndexOf("else",completed,StringComparison.Ordinal)-completed);
        Assert.True(finish.IndexOf("StopBibleVerseScrollAnimation();",StringComparison.Ordinal)<finish.IndexOf("QueueBibleScrollCorrection(anchorIndex);",StringComparison.Ordinal),"Legacy completion must stop before queueing its valid correction generation.");
    }
    [Fact]
    public void Driver_StoppedCallbackCannotOverwriteReplacementContent() => Sta(()=>{
        var viewer=new ScrollViewer{Content=new Border{Height=2000}};
        var window=new System.Windows.Window{Width=250,Height=250,Content=viewer};object driver=null;
        try{
            window.Show();window.UpdateLayout();var type=Required("BibleVerseSpringScroller");driver=Activator.CreateInstance(type,new object[]{viewer});
            type.GetMethod("ScrollTo").Invoke(driver,new object[]{100d,null});
            var motion=type.GetField("_motion",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(driver);
            Call(motion,"Advance",.1);type.GetMethod("Stop").Invoke(driver,null);
            viewer.Content=new Border{Height=1500};viewer.ScrollToTop();window.UpdateLayout();
            type.GetMethod("OnRendering",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(driver,new object[]{null,Activator.CreateInstance(typeof(System.Windows.Media.RenderingEventArgs),BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{TimeSpan.FromSeconds(11)},null)});
            window.UpdateLayout();Assert.Equal(0,viewer.VerticalOffset);
        }finally{if(driver!=null)((IDisposable)driver).Dispose();window.Close();}
    });
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Driver_DoesNotSubscribeForAlreadyHiddenOrUnloadedViewer(bool unload) => Sta(()=>{
        var viewer=new System.Windows.Controls.ScrollViewer{Content=new System.Windows.Controls.Border{Height=2000}};
        var window=new System.Windows.Window{Width=250,Height=250,Content=viewer};object driver=null;
        try{
            window.Show();window.UpdateLayout();window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.True(viewer.ScrollableHeight>0);
            if(unload){window.Close();window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);}else window.Hide();
            Assert.False(viewer.IsVisible);Assert.True(viewer.ScrollableHeight>0);
            var type=Required("BibleVerseSpringScroller");driver=Activator.CreateInstance(type,new object[]{viewer});
            type.GetMethod("ScrollTo").Invoke(driver,new object[]{100d,null});
            Assert.False((bool)type.GetProperty("IsRunning").GetValue(driver));
        }finally{if(driver!=null)((IDisposable)driver).Dispose();window.Close();}
    });
    [Fact]
    public void Driver_ExtentShrinkPreservesInBoundsTargetAfterCollision() => Sta(()=>{
        var content=new System.Windows.Controls.Border{Height=2000};var viewer=new System.Windows.Controls.ScrollViewer{Content=content};
        var window=new System.Windows.Window{Width=250,Height=250,Content=viewer};object driver=null;
        try{
            window.Show();window.UpdateLayout();window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var type=Required("BibleVerseSpringScroller");driver=Activator.CreateInstance(type,new object[]{viewer});type.GetMethod("ScrollTo").Invoke(driver,new object[]{300d,null});
            // Controlled mid-animation reverse fixture; actual ScrollViewer extent then shrinks.
            var motion=type.GetField("_motion",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(driver);Call(motion,"Reset",1500);Call(motion,"Retarget",300);
            content.Height=650;window.UpdateLayout();Assert.InRange(viewer.ScrollableHeight,301,1499);
            type.GetMethod("AdvanceFrame",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(driver,new object[]{0d});
            Assert.Equal(300,Get(motion,"Target"));Assert.True(Get(motion,"Position")>300);
        }finally{if(driver!=null)((IDisposable)driver).Dispose();window.Close();}
    });
    private static string FindRoot(){var p=new DirectoryInfo(AppContext.BaseDirectory);while(p!=null){if(File.Exists(Path.Combine(p.FullName,"ImageColorChanger.csproj")))return p.FullName;p=p.Parent;}throw new Exception("root missing");}
    private static void Sta(Action action){Exception error=null;var th=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}});th.SetApartmentState(ApartmentState.STA);th.Start();th.Join();if(error!=null)ExceptionDispatchInfo.Capture(error).Throw();}
}
