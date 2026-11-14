using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using ImageColorChanger.UI;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Canvas.TextEditor.Tests.UI;

[Collection("Lyrics image isolation exclusive")]
public sealed class LyricsLibraryVisibilityTests
{
    [Fact]
    public void ImageLyrics_SaveAndReopenKeepContentWithoutAddingRootSong()
    {
        WithDatabase((db, path) =>
        {
            Assert.Empty(LoadTree(db));
            var image = AddImage(db);
            var manager = new LyricsProjectManager(db);
            var project = manager.LoadOrCreateImageProject(image.Id);
            project.Content = "图片专属歌词\n第二行";
            manager.SaveImageProject(project, image.Id);
            using var reopened = new CanvasDbContext(path);
            var loaded = new LyricsProjectManager(reopened).LoadOrCreateImageProject(image.Id);
            Assert.Equal(project.Id, loaded.Id);
            Assert.Equal(project.Content, loaded.Content);
            var tree = LoadTree(reopened);
            Console.WriteLine($"IMAGE_ROOT: nodes={tree.Count}; databaseRows={reopened.LyricsProjects.Count()}; contentPreserved={loaded.Content == project.Content}");
            Assert.Empty(tree);
            Assert.Single(reopened.LyricsProjects);
        });
    }

    [Fact]
    public void TreeKeepsStandaloneSongsAndLegacyGroupsButHidesImageBindings()
    {
        WithDatabase((db, _) =>
        {
            var group = AddGroup(db);
            var image = AddImage(db);
            var first = AddSong(db, "歌词_图片_合法独立歌曲", 1, sort: 0);
            var second = AddSong(db, "独立歌曲", 1, sort: 2);
            var legacy = AddSong(db, "历史分组歌曲", 0, group: group.Id);
            var bound = AddSong(db, "图片绑定但有分组", 0, image.Id, group.Id);
            var inconsistent = AddSong(db, "仍然绑定图片", 1, image.Id);
            var tree = LoadTree(db);
            Assert.Equal(new[] { first.Id, second.Id }, tree.Where(n => n.Type == TreeItemType.LyricsSong).Select(n => n.Id));
            var children = Assert.Single(tree.Where(n => n.Type == TreeItemType.LyricsGroup)).Children;
            Assert.Equal(legacy.Id, Assert.Single(children).Id);
            Assert.Equal(5, db.LyricsProjects.Count());
        });
    }

    [Fact]
    public void DeletedImageRecordStaysHiddenWithoutDeletingItsLyrics()
    {
        WithDatabase((db, _) =>
        {
            var image = AddImage(db);
            var manager = new LyricsProjectManager(db);
            var project = manager.LoadOrCreateImageProject(image.Id);
            project.Content = "删除图片后保留的数据";
            manager.SaveImageProject(project, image.Id);
            db.MediaFiles.Remove(image);
            db.SaveChanges();
            db.ChangeTracker.Clear();
            Assert.Null(db.LyricsProjects.Single().ImageId);
            Assert.Empty(LoadTree(db));
            Assert.Equal(project.Content, db.LyricsProjects.Single().Content);
            Assert.Null(manager.LoadOrCreateImageProject(image.Id));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchExcludesBoundImagesInAllAndGroupScopes(bool groupScope)
    {
        WithDatabase((db, _) =>
        {
            var group = AddGroup(db);
            var image = AddImage(db);
            var song = AddSong(db, "匹配独立歌曲", 1, group: group.Id);
            AddSong(db, "匹配图片歌词", 0, image.Id, group.Id);
            var results = Search(db, "匹配", groupScope ? group.Id : null);
            Assert.Equal(song.Id, Assert.Single(results).Id);
        });
    }

    [Fact]
    public void SearchWithoutGroupsKeepsRootSongsAndRejectsMissingScope()
    {
        WithDatabase((db, _) =>
        {
            var song = AddSong(db, "根目录独立歌曲", 1);
            AddSong(db, "根目录图片残留", 0);
            Assert.Equal(song.Id, Assert.Single(Search(db, "根目录", null)).Id);
            Assert.Empty(Search(db, "根目录", 99999));
        });
    }

    [Fact]
    public void SystemImageGroupOrphansNeverBecomeSearchableSongs()
    {
        WithDatabase((db, _) =>
        {
            var userGroup = AddGroup(db);
            var systemGroup = new LyricsGroup { Name = "图片关联歌词", IsSystem = true };
            db.LyricsGroups.Add(systemGroup); db.SaveChanges();
            AddSong(db, "图片残留", 0, group: systemGroup.Id);
            Assert.Empty(Search(db, "图片残留", null));
            Assert.Empty(LoadTree(db).SelectMany(n => n.Children ?? new ObservableCollection<ProjectTreeItem>()));
            Assert.Single(db.LyricsProjects);
        });
    }

    private static MediaFile AddImage(CanvasDbContext db)
    {
        var image = new MediaFile { Name = "图片", Path = @"D:\fixture\image.png", FileTypeString = "image" };
        db.MediaFiles.Add(image); db.SaveChanges(); return image;
    }

    private static LyricsGroup AddGroup(CanvasDbContext db)
    {
        var group = new LyricsGroup { Name = "普通歌词库", IsSystem = false };
        db.LyricsGroups.Add(group); db.SaveChanges(); return group;
    }

    private static LyricsProject AddSong(CanvasDbContext db, string name, int source, int? image = null, int? group = null, int sort = 0)
    {
        var song = new LyricsProject { Name = name, Content = name, SourceType = source, ImageId = image, GroupId = group, SortOrder = sort };
        db.LyricsProjects.Add(song); db.SaveChanges(); return song;
    }

    private static ObservableCollection<ProjectTreeItem> LoadTree(CanvasDbContext db)
    {
        var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var tree = new ObservableCollection<ProjectTreeItem>();
        Set(host, "_dbContext", db); Set(host, "_projectTreeItems", tree);
        Invoke(host, "LoadLyricsLibraryToTree");
        return tree;
    }

    private static ObservableCollection<ProjectTreeItem> Search(CanvasDbContext db, string text, int? group)
    {
        var database = (DatabaseManager)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseManager));
        Set(database, "_context", db);
        var search = new SearchManager(database);
        var results = new ObservableCollection<ProjectTreeItem>();
        Invoke(search, "AppendLyricsSearchResults", results, text, group);
        return results;
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string method, params object[] args)
    {
        try { target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
    }

    private static void WithDatabase(Action<CanvasDbContext, string> action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), "canvas-lyrics-visibility-" + Guid.NewGuid().ToString("N") + ".db");
            try { using var db = new CanvasDbContext(path); db.Database.EnsureCreated(); action(db, path); }
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
