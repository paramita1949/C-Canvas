using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using ImageColorChanger.Services;
using ImageColorChanger.Services.Lyrics;
using ImageColorChanger.UI;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Canvas.TextEditor.Tests.Database;

public class ImageLyricsRefactorTests
{
    private const string Content = "__LYRICS_MODE_V1__{\"SingleContent\":\"原文\\n第二节\",\"SplitContents\":[{\"Regions\":[\"甲\",\"乙\"]}],\"ThemeName\":\"白色\"}";

    [Fact]
    public void LegacyImportsWithIdenticalRowsButDifferentMediaPathsRemainIndependent() => Db((target, _) =>
    {
        string snapshot=null;
        for(int i=0;i<2;i++)Db((source, _) =>
        {
            var image=Image(source,"identity"+i);var old=Legacy(source,image.Id,0,"相同歌词");old.CreatedTime=new DateTime(2020,1,1);old.ModifiedTime=old.CreatedTime;old.ExternalId="same-fixture-id";source.SaveChanges();
            var current=JsonSerializer.Serialize(source.LyricsProjects.AsNoTracking().Single());if(snapshot!=null)Assert.Equal(snapshot,current);snapshot=current;
            var dst=Image(target,"identity"+i);
            Required("ImageColorChanger.Services.Lyrics.ImageLyricsTransfer").GetMethod("MergeLegacy").Invoke(null,new object[]{source,target,new Dictionary<int,int>{{image.Id,dst.Id}}});
        });
        Assert.Equal(2,target.ImageLyrics.Count());Assert.Equal(2,Archives(target).Count);
    });

    [Fact]
    public async Task RealIncrementalPrivateStagesRollbackTogetherOnArchiveFailure()
    {
        var dir=Path.Combine(Path.GetTempPath(),"canvas-merge-atomic-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var src=Path.Combine(dir,"source.db");var dst=Path.Combine(dir,"target.db");
        try
        {
            using(var db=new CanvasDbContext(src)){db.Database.EnsureCreated();var a=Image(db,"new-private");new ImageLyricsService(db).Save(a.Id,new LyricsDocument{Content=Content});var b=Image(db,"old-private");Legacy(db,b.Id,0,"旧私有");}
            using(var db=new CanvasDbContext(dst)){db.Database.EnsureCreated();db.Database.ExecuteSqlRaw("CREATE TRIGGER refuse_import_archive BEFORE INSERT ON image_lyrics_archive WHEN NEW.source_key LIKE 'legacy-import:%' BEGIN SELECT RAISE(ABORT,'fixture archive stop'); END");}
            var service=(DatabaseMigrationService)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseMigrationService));Set(service,"_defaultDbPath",dst);
            await Assert.ThrowsAnyAsync<Exception>(async()=>await (Task)Invoke(service,"MergeDatabaseIncrementallyAsync",src));
            using(var db=new CanvasDbContext(dst)){Assert.Empty(db.ImageLyrics);Assert.Empty(Archives(db));}
        }
        finally{SqliteConnection.ClearAllPools();foreach(var f in Directory.GetFiles(dir))File.Delete(f);Directory.Delete(dir);}
    }

    [Fact]
    public void ExplicitArchiveSelectionRecoversOnlyChosenDocumentWithoutOverwriting() => Db((db, _) =>
    {
        var image=Image(db,"recover");Legacy(db,image.Id,0,"甲");Legacy(db,image.Id,0,"乙");Migrate(db);
        var archive=Archives(db).First();var service=new ImageLyricsService(db);
        Invoke(service,"RecoverArchive",Value(archive,"ExternalId"),image.Id);
        Assert.Equal(Content,service.Load(image.Id).Content);Assert.Equal(3,Archives(db).Count);
        Assert.Throws<InvalidOperationException>(()=>Invoke(service,"RecoverArchive",Value(archive,"ExternalId"),image.Id));
        Assert.Empty(db.LyricsProjects);
    });

    [Fact]
    public void RecoveryRejectsCorruptDocumentEvenWhenSnapshotDigestIsUntouched() => Db((db, _) =>
    {
        var image=Image(db,"corrupt");Legacy(db,image.Id,0,"甲");Legacy(db,image.Id,0,"乙");Migrate(db);
        var archive=Archives(db).First();archive.GetType().GetProperty("DocumentJson").SetValue(archive,"{\"Content\":\"被篡改\"}");db.SaveChanges();
        Assert.Throws<InvalidOperationException>(()=>Invoke(new ImageLyricsService(db),"RecoverArchive",Value(archive,"ExternalId"),image.Id));
        Assert.Empty(db.ImageLyrics);
    });

    [Fact]
    public async Task RealIncrementalServicePreservesOldImportIdentityAndLocalConflicts()
    {
        var dir=Path.Combine(Path.GetTempPath(),"canvas-real-merge-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var src=Path.Combine(dir,"source.db");var dst=Path.Combine(dir,"target.db");
        try
        {
            using(var db=new CanvasDbContext(src)){db.Database.EnsureCreated();var image=Image(db,"real-merge");Legacy(db,image.Id,0,"图片私有");}
            using(var db=new CanvasDbContext(dst)){db.Database.EnsureCreated();Image(db,"dummy");}
            var service=(DatabaseMigrationService)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseMigrationService));Set(service,"_defaultDbPath",dst);
            for(int i=0;i<2;i++)await (Task)Invoke(service,"MergeDatabaseIncrementallyAsync",src);
            using(var db=new CanvasDbContext(dst)){Assert.Empty(db.LyricsProjects);Assert.Equal(Content,Assert.Single(db.ImageLyrics).Content);Assert.Single(Archives(db));}
            using(var db=new CanvasDbContext(src)){Assert.Single(db.LyricsProjects);Assert.Empty(db.ImageLyrics);}
            using(var db=new CanvasDbContext(dst)){db.ImageLyrics.RemoveRange(db.ImageLyrics);db.SaveChanges();var image=db.MediaFiles.Single(m=>m.Name=="real-merge");Legacy(db,image.Id,0,"本机甲");Legacy(db,image.Id,0,"本机乙");Migrate(db);}
            await (Task)Invoke(service,"MergeDatabaseIncrementallyAsync",src);
            using(var db=new CanvasDbContext(dst)){Assert.Empty(db.ImageLyrics);Assert.Throws<InvalidOperationException>(()=>new ImageLyricsService(db).Load(db.MediaFiles.Single(m=>m.Name=="real-merge").Id));}
        }
        finally{SqliteConnection.ClearAllPools();foreach(var f in Directory.GetFiles(dir))File.Delete(f);Directory.Delete(dir);}
    }

    [Fact]
    public void ImportCannotSilentlyResolveLocalLegacyConflicts() => Db((target, _) =>
    {
        Db((source, _) =>
        {
            var dst = Image(target, "local-conflict"); Legacy(target, dst.Id, 0, "旧甲"); Legacy(target, dst.Id, 0, "旧乙"); Migrate(target);
            var src = Image(source, "local-conflict"); new ImageLyricsService(source).Save(src.Id, new LyricsDocument { Content = "导入不应胜出" });
            Required("ImageColorChanger.Services.Lyrics.ImageLyricsTransfer").GetMethod("Merge").Invoke(null, new object[] { source, target, new Dictionary<int,int>{{src.Id,dst.Id}} });
            Assert.Empty(target.ImageLyrics); Assert.Equal(3, Archives(target).Count);
            Assert.Throws<InvalidOperationException>(() => new ImageLyricsService(target).Load(dst.Id));
        });
    });

    [Fact]
    public void EditorLateSaveCannotResurrectDeletedPrivateDocument() => Db((db, _) =>
    {
        var image = Image(db, "late-save"); var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow)); Set(host, "_dbContext", db);
        new ImageLyricsService(db).Save(image.Id, new LyricsDocument { Content = Content });
        Invoke(host, "SetLyricsEntryByImage", image.Id); var doc = (LyricsDocument)Invoke(host, "LoadLyricsEntryDocument");
        db.ImageLyrics.Remove(db.ImageLyrics.Single()); db.SaveChanges();
        Assert.Throws<InvalidOperationException>(() => Invoke(host, "PersistLyricsEntryDocument", doc));
        Assert.Empty(db.ImageLyrics); Assert.Empty(db.LyricsProjects);
    });

    [Fact]
    public void VideoMediaCannotBeUsedAsImageLyricsOwner() => Db((db, _) =>
    {
        var image = Image(db, "video-negative"); image.FileTypeString = "video"; db.SaveChanges();
        var service = new ImageLyricsService(db); Assert.Null(service.Load(image.Id));
        Assert.Throws<InvalidOperationException>(() => service.Save(image.Id, new LyricsDocument { Content = Content }));
        Assert.Empty(db.ImageLyrics);
    });

    [Fact]
    public void MigrationMovesUniqueBindingAndArchivesExactOriginalOnlyOnce() => Db((db, _) =>
    {
        var image = Image(db, "migrate"); var old = Legacy(db, image.Id, 0, "普通名字");
        // 比较数据库持久化列的原始快照，不把EF临时关系fixup或DateTime.Kind当成数据库字节。
        var original = JsonSerializer.Serialize(db.LyricsProjects.AsNoTracking().Single(p => p.Id == old.Id));
        Migrate(db); Migrate(db);
        Assert.Empty(db.LyricsProjects);
        var row = Assert.Single(db.ImageLyrics); Assert.Equal(image.Id, row.ImageId); Assert.Equal(Content, row.Content);
        var archives = Archives(db); Assert.Single(archives); Assert.Equal(original, Value(archives[0], "SnapshotJson"));
        Console.WriteLine("REFACTOR_MIGRATION: imageRows=1; legacyRows=0; originalArchived=True; secondRunUnchanged=True");
    });

    [Fact]
    public void MigrationQuarantinesConflictsAndOrphansWithoutGuessingSongNames() => Db((db, _) =>
    {
        var image = Image(db, "duplicate"); Legacy(db, image.Id, 0, "甲"); Legacy(db, image.Id, 0, "乙");
        Legacy(db, null, 0, "歌词_图片_7303"); var song = Legacy(db, null, 1, "歌词_图片_真歌曲");
        Migrate(db);
        Assert.Empty(db.ImageLyrics); Assert.Equal(song.Id, Assert.Single(db.LyricsProjects).Id);
        Assert.Equal(3, Archives(db).Count);
        Assert.Throws<InvalidOperationException>(() => new ImageLyricsService(db).Load(image.Id));
        Assert.Throws<InvalidOperationException>(() => new ImageLyricsService(db).Save(image.Id, new LyricsDocument { Content = "不能覆盖" }));
    });

    [Fact]
    public void MigrationFailureRollsBackCopyArchiveAndDeletionTogether() => Db((db, _) =>
    {
        var image = Image(db, "rollback"); Legacy(db, image.Id, 0, "原始");
        Schema(db);
        db.Database.ExecuteSqlRaw("CREATE TRIGGER refuse_legacy_delete BEFORE DELETE ON lyrics_projects BEGIN SELECT RAISE(ABORT, 'fixture stop'); END");
        Assert.ThrowsAny<Exception>(() => Migrate(db)); db.ChangeTracker.Clear();
        Assert.Single(db.LyricsProjects); Assert.Empty(db.ImageLyrics); Assert.Empty(Archives(db));
    });

    [Fact]
    public void StartupUpgradesPreviouslyStampedDatabaseWithoutDestructivePurge() => Db((db, path) =>
    {
        db.InitializeDatabase(); var image = Image(db, "startup"); Legacy(db, image.Id, 0, "旧数据");
        using var reopened = new CanvasDbContext(path); reopened.InitializeDatabase();
        Assert.Equal(Content, new ImageLyricsService(reopened).Load(image.Id).Content);
        Assert.Empty(reopened.LyricsProjects); Assert.Single(Archives(reopened));
    });

    [Fact]
    public void EditorEntryRoutesCommonDocumentByBoundImageIdWithoutSongInsertion() => Db((db, _) =>
    {
        var a = Image(db, "entry-a"); var b = Image(db, "entry-b");
        var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow)); Set(host, "_dbContext", db);
        Invoke(host, "SetLyricsEntryByImage", a.Id); var doc = (LyricsDocument)Invoke(host, "LoadLyricsEntryDocument");
        Assert.NotNull(doc); doc.Content = Content; doc.ProjectionWatermarkPath = "data/watermarks/a.png";
        Set(host, "_currentImageId", b.Id); Invoke(host, "PersistLyricsEntryDocument", doc);
        Assert.Equal(Content, new ImageLyricsService(db).Load(a.Id).Content);
        Assert.Equal("", new ImageLyricsService(db).Load(b.Id).Content); Assert.Empty(db.LyricsProjects);
        Assert.Null(Field(host, "_currentLyricsProject"));
        Assert.Equal(0, Field(host, "_currentLyricsProjectId"));
        Console.WriteLine("REFACTOR_ENTRY: imageStore=1; libraryStore=0; mutableDisplayCannotRebind=True");
    });

    [Fact]
    public void EditorSongEntryStaysInLibraryWhileImageEntryIsPrivate() => Db((db, _) =>
    {
        var image = Image(db, "song-entry"); var song = Legacy(db, null, 1, "独立歌曲");
        var host = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow)); Set(host, "_dbContext", db);
        Invoke(host, "SetLyricsEntryBySong", song.Id); var doc = (LyricsDocument)Invoke(host, "LoadLyricsEntryDocument");
        doc.Content = "歌曲修订"; Invoke(host, "PersistLyricsEntryDocument", doc);
        Invoke(host, "SetLyricsEntryByImage", image.Id); doc = (LyricsDocument)Invoke(host, "LoadLyricsEntryDocument");
        Assert.Equal("", doc.Content); doc.Content = "图片修订"; Invoke(host, "PersistLyricsEntryDocument", doc);
        Assert.Equal("歌曲修订", Assert.Single(db.LyricsProjects).Content);
        Assert.Equal("图片修订", Assert.Single(db.ImageLyrics).Content);
    });

    [Fact]
    public void DeletedImageArchivesPrivateLyricsAndRestoreRequiresMatchingImagePath() => Db((db, _) =>
    {
        var image = Image(db, "delete"); new ImageLyricsService(db).Save(image.Id, new LyricsDocument { Content = Content });
        new ImageColorChanger.Database.Repositories.MediaRepository(db).DeleteMediaFile(image.Id);
        Assert.Empty(db.MediaFiles); Assert.Empty(db.ImageLyrics); Assert.Empty(db.LyricsProjects);
        var archive = Assert.Single(Archives(db)); Assert.Equal("DeletedImage", Value(archive, "Reason"));
        var other = Image(db, "wrong"); var service = new ImageLyricsService(db);
        Assert.Throws<InvalidOperationException>(() => Invoke(service, "Restore", Value(archive, "ExternalId"), other.Id));
        var restored = Image(db, "delete"); Invoke(service, "Restore", Value(archive, "ExternalId"), restored.Id);
        Assert.Equal(Content, service.Load(restored.Id).Content); Assert.Single(Archives(db));
    });

    [Fact]
    public void IncrementalImageTransferRemapsIdsAndArchivesTargetConflictsWithoutOverwrite() => Db((target, _) =>
    {
        Db((source, _) =>
        {
            var src = Image(source, "merge"); var dummy = Image(target, "dummy"); var dst = Image(target, "merge");
            new ImageLyricsService(source).Save(src.Id, new LyricsDocument { Content = Content });
            var type = Required("ImageColorChanger.Services.Lyrics.ImageLyricsTransfer");
            var map = new Dictionary<int, int> { [src.Id] = dst.Id };
            type.GetMethod("Merge").Invoke(null, new object[] { source, target, map });
            Assert.Equal(Content, new ImageLyricsService(target).Load(dst.Id).Content); Assert.Empty(target.LyricsProjects);
            new ImageLyricsService(target).Save(dst.Id, new LyricsDocument { Content = "本机新内容" });
            type.GetMethod("Merge").Invoke(null, new object[] { source, target, map });
            type.GetMethod("Merge").Invoke(null, new object[] { source, target, map });
            Assert.Equal("本机新内容", new ImageLyricsService(target).Load(dst.Id).Content);
            Assert.Single(Archives(target));
        });
    });

    [Fact]
    public void WholeDatabaseBackupIncludesPrivateLyricsAndArchiveTable() => Db((db, path) =>
    {
        var image = Image(db, "backup"); Legacy(db, image.Id, 0, "旧图片"); Migrate(db);
        var copy = path + ".backup";
        try
        {
            using(var source = new SqliteConnection("Data Source=" + path))
            using(var target = new SqliteConnection("Data Source=" + copy)) { source.Open(); target.Open(); source.BackupDatabase(target); }
            using var reopened = new CanvasDbContext(copy);
            Assert.Equal(Content, new ImageLyricsService(reopened).Load(image.Id).Content); Assert.Single(Archives(reopened)); Assert.Empty(reopened.LyricsProjects);
        }
        finally { SqliteConnection.ClearAllPools(); foreach(var suffix in new[]{"", "-wal", "-shm"}) if(File.Exists(copy+suffix)) File.Delete(copy+suffix); }
    });

    [Fact]
    public void SongPackageExportNeverContainsPrivateOrQuarantinedLyrics() => Db((db, path) =>
    {
        var image = Image(db, "export"); Legacy(db, image.Id, 0, "绝不作为歌曲导出"); var song = Legacy(db, null, 1, "真歌曲");
        var export = path + ".lyr";
        try
        {
            var service = new LyricsTransferService(db); var result = service.ExportLibraryAsync(export).GetAwaiter().GetResult();
            Assert.Equal(1, result.Imported);
            Assert.False(service.ExportSongAsync(db.LyricsProjects.First(p=>p.ImageId!=null).Id, export+".invalid").GetAwaiter().GetResult().Success);
        }
        finally { foreach(var p in new[]{export, export+".invalid"}) if(File.Exists(p)) File.Delete(p); }
    });

    private static Type Required(string name) { var t = typeof(CanvasDbContext).Assembly.GetType(name); Assert.True(t != null, "Missing refactor component: " + name); return t; }
    private static void Schema(CanvasDbContext db) => Required("ImageColorChanger.Database.ImageLyricsSchema").GetMethod("EnsureCreated").Invoke(null, new object[] { db });
    private static void Migrate(CanvasDbContext db)
    {
        try { Required("ImageColorChanger.Services.Lyrics.ImageLyricsMigration").GetMethod("Run").Invoke(null, new object[] { db }); }
        catch(TargetInvocationException ex) when(ex.InnerException!=null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
    }
    private static List<object> Archives(CanvasDbContext db)
    {
        var property = db.GetType().GetProperty("ImageLyricsArchives"); Assert.NotNull(property);
        return ((System.Collections.IEnumerable)property.GetValue(db)).Cast<object>().ToList();
    }
    private static object Value(object o, string field) => o.GetType().GetProperty(field).GetValue(o);
    private static object Field(object o,string field)=>o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
    private static void Set(object o,string field,object value)=>o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);
    private static object Invoke(object o,string method,params object[] args)
    {
        var m=o.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); Assert.NotNull(m);
        try { return m.Invoke(o,args); } catch(TargetInvocationException ex) when(ex.InnerException!=null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static MediaFile Image(CanvasDbContext db,string name) { var image=new MediaFile{Name=name,Path="D:/fixture/"+name+".png"};db.MediaFiles.Add(image);db.SaveChanges();return image; }
    private static LyricsProject Legacy(CanvasDbContext db,int? image,int source,string name) { var p=new LyricsProject{Name=name,ImageId=image,SourceType=source,Content=Content,FontSize=73,TextAlign="Left",ProjectionWatermarkPath="data/watermarks/a.png"};db.LyricsProjects.Add(p);db.SaveChanges();return p; }
    private static void Db(Action<CanvasDbContext,string> action)
    {
        var path=Path.Combine(Path.GetTempPath(),"canvas-full-refactor-"+Guid.NewGuid().ToString("N")+".db");
        try { using var db=new CanvasDbContext(path);db.Database.EnsureCreated();action(db,path); }
        finally { SqliteConnection.ClearAllPools();foreach(var s in new[]{"","-wal","-shm"})if(File.Exists(path+s))File.Delete(path+s); }
    }
}
