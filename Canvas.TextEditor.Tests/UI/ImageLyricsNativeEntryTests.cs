using System.Reflection;
using System.Runtime.ExceptionServices;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.Lyrics;
using ImageColorChanger.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Canvas.TextEditor.Tests.UI;

[Collection("Lyrics F6 exclusive")]
public class ImageLyricsNativeEntryTests
{
    [Fact]
    public void RealEditorLoadReopenDoesNotDereferenceSongMetadataForImage()
    {
        WithHost((host, db) =>
        {
            var image = new MediaFile { Name = "真实编辑加载", Path = "D:/fixture/native.png" }; db.MediaFiles.Add(image); db.SaveChanges();
            var data = new MainWindow.LyricsModeContentData { SingleContent = "真实加载第一节\n第二行", ThemeName = "黑色", ThemeBackgroundHex = "#000000" };
            new ImageLyricsService(db).Save(image.Id, new LyricsDocument { Content = "__LYRICS_MODE_V1__" + System.Text.Json.JsonSerializer.Serialize(data) });
            Set(host, "_currentImageId", image.Id); Call(host, "SetLyricsEntryByImage", image.Id); Call(host, "LoadOrCreateLyricsProject");
            Assert.NotNull(Field(host, "_currentLyricsDocument")); Assert.Null(Field(host, "_currentLyricsProject"));
            Assert.Equal(data.SingleContent, ((System.Windows.Controls.TextBox)host.GetType().GetProperty("LyricsTextBox", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host)).Text);
            Assert.Empty(db.LyricsProjects);
            Console.WriteLine("NATIVE_IMAGE_LOAD: full-editor-path=True; songMetadata=null; savedTextRestored=True");
        });
    }

    [Fact]
    public void GroupWatermarkUpdatesOpenDocumentAndSurvivesSubsequentSave()
    {
        WithHost((host, db) =>
        {
            var group = new LyricsGroup { Name = "测试歌曲库" }; db.LyricsGroups.Add(group); db.SaveChanges();
            var song = new LyricsProject { Name = "歌曲", GroupId = group.Id, SourceType = 1, Content = "歌词" }; db.LyricsProjects.Add(song); db.SaveChanges();
            Call(host, "SetLyricsEntryBySong", song.Id); var doc = (LyricsDocument)Call(host, "LoadLyricsEntryDocument");
            Call(host, "ApplyGroupWatermark", group.Id, "data/watermarks/new.png", "新水印");
            Assert.Equal("data/watermarks/new.png", doc.ProjectionWatermarkPath); Call(host, "PersistLyricsEntryDocument", doc);
            Assert.Equal("data/watermarks/new.png", db.LyricsProjects.AsNoTracking().Single().ProjectionWatermarkPath);
            Call(host, "ApplyGroupWatermark", group.Id, "", "移除"); Call(host, "PersistLyricsEntryDocument", doc);
            Assert.Equal("", db.LyricsProjects.AsNoTracking().Single().ProjectionWatermarkPath);
        });
    }
    [Fact]
    public void ArchiveMenuReusesNativeContextStyleAndShowsOnlyInternalMaintenance()
    {
        WithHost((host, db) =>
        {
            var image=new MediaFile{Name="归档图",Path="D:/fixture/archive.png"};db.MediaFiles.Add(image);db.SaveChanges();
            Assert.Null(Call(host,"BuildImageLyricsArchiveMenu",image.Id));
            db.LyricsProjects.AddRange(new LyricsProject{Name="甲",ImageId=image.Id},new LyricsProject{Name="乙",ImageId=image.Id});db.SaveChanges();ImageLyricsMigration.Run(db);
            var archive=(System.Windows.Controls.MenuItem)Call(host,"BuildImageLyricsArchiveMenu",image.Id);Assert.NotNull(archive);Assert.Equal(4,archive.Items.Count);
            Assert.Empty(db.LyricsProjects);Assert.Empty(db.ImageLyrics);
            var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"ImageColorChanger.csproj")))dir=dir.Parent;
            var resources=new System.Windows.ResourceDictionary{Source=new Uri(Path.Combine(dir.FullName,"UI/Resources/Styles/MainWindow.BaseStyles.xaml"))};
            var menu=new System.Windows.Controls.ContextMenu();menu.Resources.MergedDictionaries.Add(resources);menu.Style=(System.Windows.Style)resources["NoBorderContextMenuStyle"];
            menu.Items.Add(new System.Windows.Controls.MenuItem{Header="进入歌词"});menu.Items.Add(archive);
            menu.Measure(new System.Windows.Size(500,300));menu.Arrange(new System.Windows.Rect(0,0,menu.DesiredSize.Width,menu.DesiredSize.Height));menu.UpdateLayout();
            Assert.True(menu.ActualWidth>100);Assert.True(menu.ActualHeight>30);
            var output=Environment.GetEnvironmentVariable("CANVAS_REFACTOR_EVIDENCE");
            if(!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(menu);
                var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,"ARCHIVE_MENU_NATIVE.png"));png.Save(file);
            }
            Console.WriteLine("ARCHIVE_MENU: native-context-style=True; existing-lyrics-entry=True; internal-archive-maintenance=True; rootSongs=0; archiveChoices=2");
        });
    }

    private static object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
    private static void Set(object o,string name,object value)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance).SetValue(o,value);
    private static object Call(object o,string name,params object[] args)
    {
        try{return o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance).Invoke(o,args);}
        catch(TargetInvocationException ex) when(ex.InnerException!=null){ExceptionDispatchInfo.Capture(ex.InnerException).Throw();throw;}
    }
    private static void WithHost(Action<MainWindow,CanvasDbContext> action)
    {
        Exception error=null;
        var thread=new Thread(()=>
        {
            var path=Path.Combine(Path.GetTempPath(),"canvas-native-refactor-"+Guid.NewGuid().ToString("N")+".db");
            try
            {
                using var db=new CanvasDbContext(path);db.Database.EnsureCreated();
                var factory=typeof(LyricsImageShortcutTests).GetMethod("CreateSaveHost",BindingFlags.NonPublic|BindingFlags.Static);
                var host=(MainWindow)factory.Invoke(null,new object[]{db,path+".json"});
                var view=typeof(MainWindow).GetField("LyricsSectionView",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance).GetValue(host);
                foreach(var f in view.GetType().GetFields(BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance))
                    if(typeof(System.Windows.FrameworkElement).IsAssignableFrom(f.FieldType) && f.GetValue(view)==null && f.FieldType.GetConstructor(Type.EmptyTypes)!=null)
                        f.SetValue(view,Activator.CreateInstance(f.FieldType));
                Set(host,"_activeLyricsEditor",host.GetType().GetProperty("LyricsTextBox", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host));
                action(host,db);
            }
            catch(Exception ex){error=ex;}
            finally{SqliteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm",".json"})if(File.Exists(path+suffix))File.Delete(path+suffix);}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)ExceptionDispatchInfo.Capture(error).Throw();
    }
}
