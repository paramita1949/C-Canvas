using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using ImageColorChanger.UI;

namespace Canvas.TextEditor.Tests.UI;

public class ProjectionScreenFileTabTests
{
    static string Source(string path)
    {
        var root=new DirectoryInfo(Environment.GetEnvironmentVariable("CANVAS_MEDIA_SOURCE_ROOT")??AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"UI/MainWindow.xaml")))root=root.Parent;
        Assert.NotNull(root);return File.ReadAllText(Path.Combine(root.FullName,path));
    }
    [Fact] public void WholeFooterIsNamedAndDefaultsToFileTabVisible()
    {
        XNamespace w="http://schemas.microsoft.com/winfx/2006/xaml/presentation",x="http://schemas.microsoft.com/winfx/2006/xaml";
        var selector=XElement.Parse(Source("UI/MainWindow.xaml")).Descendants(w+"ComboBox").Single(e=>(string)e.Attribute(x+"Name")=="ScreenSelector");
        Assert.Equal("ScreenSelectorPanel",(string)selector.Parent.Attribute(x+"Name"));
        Assert.DoesNotContain("Collapsed",(string)selector.Parent.Attribute("Visibility")??"Visible");
    }
    [Fact] public void AllTabPresentationUsesCanonicalModeVisibilityUpdater()
    {
        var source=Source("UI/MainWindow.NavigationView.cs");
        var start=source.IndexOf("private void UpdateViewModeButtons()",StringComparison.Ordinal);
        var end=source.IndexOf("private void LoadTextProjectsToTree()",start,StringComparison.Ordinal);
        Assert.Contains("UpdateScreenSelectorVisibility();",source[start..end]);
        Assert.Contains("_currentViewMode == NavigationViewMode.Files",source);
    }
    [Fact] public void FileBibleSlidesInvalidAndReturnPreserveSelectionAndCollapseWholeFooter()
    {
        Exception failure=null;var thread=new Thread(()=>
        {
            try
            {
                var window=(MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                var mode=typeof(MainWindow).GetField("_currentViewMode",BindingFlags.NonPublic|BindingFlags.Instance);
                var update=typeof(MainWindow).GetMethod("UpdateScreenSelectorVisibility",BindingFlags.NonPublic|BindingFlags.Instance);
                Assert.NotNull(update);
                var panelField=typeof(MainWindow).GetField("ScreenSelectorPanel",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance);
                Assert.NotNull(panelField);
                var combo=new ComboBox();combo.Items.Add("TARGET（扩展显示器）");combo.SelectedIndex=0;
                var panel=new Border{Child=combo,Padding=new Thickness(8)};panelField.SetValue(window,panel);
                foreach(var value in new[]{0,2,1,0,99,0})
                {
                    mode.SetValue(window,Enum.ToObject(mode.FieldType,value));update.Invoke(window,null);
                    Assert.Equal(value==0?Visibility.Visible:Visibility.Collapsed,panel.Visibility);
                    Assert.Equal(0,combo.SelectedIndex);Assert.Equal("TARGET（扩展显示器）",combo.SelectedItem);
                }
                combo.Items.Clear();mode.SetValue(window,Enum.ToObject(mode.FieldType,2));update.Invoke(window,null);
                Assert.Equal(Visibility.Collapsed,panel.Visibility);Assert.Equal(-1,combo.SelectedIndex);
                panelField.SetValue(window,null);update.Invoke(window,null); // 初始化/缺失控件安全路径
            }
            catch(Exception e){failure=e;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
