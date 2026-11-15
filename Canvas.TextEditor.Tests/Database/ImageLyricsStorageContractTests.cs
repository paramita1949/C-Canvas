using System.Reflection;
using System.Runtime.ExceptionServices;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Canvas.TextEditor.Tests.Database;

// Reflection keeps the identical contract compilable against the released baseline.
// Every database is an isolated generated fixture, never the installed application's DB.
public class ImageLyricsStorageContractTests
{
    private static readonly Assembly App = typeof(CanvasDbContext).Assembly;
    private const string Payload = "__LYRICS_MODE_V1__{\"FullPages\":[\"第一节\\n第二行\"],\"OriginalPages\":[\"原图分页\"],\"FontFamily\":\"微软雅黑\",\"SplitRegions\":[{\"Color\":\"#FF00AA\"}],\"Theme\":\"dark\"}";

    [Fact]
    public void NativeModelHasDedicatedNonNullableUniqueOwnershipWithoutSongMetadata() => WithDb((db, _) =>
    {
        var entity = db.Model.FindEntityType(RequiredType("ImageColorChanger.Database.Models.ImageLyrics"));
        Assert.NotNull(entity);
        Assert.Equal("image_lyrics", entity.GetTableName());
        var image = entity.FindProperty("ImageId");
        Assert.NotNull(image);
        Assert.False(image.IsNullable);
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0] == image);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(entity.GetForeignKeys()).DeleteBehavior);
        foreach (var field in new[] { "Name", "GroupId", "SortOrder", "SourceType" }) Assert.Null(entity.FindProperty(field));
    });

    [Fact]
    public void EmptyImageLoadsUnsavedDocumentWithoutCreatingSong() => WithDb((db, _) =>
    {
        var image = AddImage(db, "empty");
        var service = Service(db);
        var document = Invoke(service, "Load", image.Id);
        Assert.NotNull(document);
        Assert.Equal("", Get(document, "Content"));
        Assert.Equal(60d, Get(document, "FontSize"));
        Assert.Equal(0L, Count(db));
        Assert.Empty(db.LyricsProjects);
        Assert.Null(Invoke(service, "Load", 999999));
    });

    [Fact]
    public void FullSerializedDocumentSurvivesSaveAndReopenWithoutSongInsertion() => WithDb((db, path) =>
    {
        var image = AddImage(db, "document");
        db.LyricsProjects.Add(new LyricsProject { Name = "独立歌曲", SourceType = 1, Content = "歌曲库原文" });
        db.SaveChanges();
        Invoke(Service(db), "Save", image.Id, Document(Payload));
        using var reopened = new CanvasDbContext(path);
        var loaded = Invoke(Service(reopened), "Load", image.Id);
        AssertDocument(loaded, Payload);
        Assert.Equal("歌曲库原文", Assert.Single(reopened.LyricsProjects).Content);
        Assert.Null(reopened.LyricsProjects.Single().ImageId);
        Assert.Equal(1L, Count(reopened));
        Console.WriteLine("IMAGE_STORAGE: imageRows=1; songRows=1; fullDocumentPreserved=True");
    });

    [Fact]
    public void RepeatedSaveUpdatesSingleImageRecord() => WithDb((db, _) =>
    {
        var image = AddImage(db, "upsert");
        var service = Service(db);
        Invoke(service, "Save", image.Id, Document(Payload));
        Invoke(service, "Save", image.Id, Document("修订正文"));
        Assert.Equal(1L, Count(db));
        Assert.Equal("修订正文", Get(Invoke(service, "Load", image.Id), "Content"));
        Assert.Empty(db.LyricsProjects);
    });

    [Fact]
    public void TwoImagesKeepIndependentDocuments() => WithDb((db, _) =>
    {
        var a = AddImage(db, "a"); var b = AddImage(db, "b");
        var service = Service(db);
        Invoke(service, "Save", a.Id, Document("甲图片"));
        Invoke(service, "Save", b.Id, Document("乙图片"));
        Assert.Equal("甲图片", Get(Invoke(service, "Load", a.Id), "Content"));
        Assert.Equal("乙图片", Get(Invoke(service, "Load", b.Id), "Content"));
        Assert.Equal(2L, Count(db));
        Assert.Empty(db.LyricsProjects);
    });

    [Fact]
    public void InvalidImageAndNullDocumentCannotWriteEitherStore() => WithDb((db, _) =>
    {
        var image = AddImage(db, "negative"); var service = Service(db);
        Assert.Throws<InvalidOperationException>(() => Invoke(service, "Save", 0, Document(Payload)));
        Assert.Throws<InvalidOperationException>(() => Invoke(service, "Save", 999999, Document(Payload)));
        Assert.Throws<ArgumentNullException>(() => Invoke(service, "Save", image.Id, null));
        Assert.Equal(0L, Count(db));
        Assert.Empty(db.LyricsProjects);
    });

    [Fact]
    public void PhysicalDatabaseRejectsDuplicateImageOwnership() => WithDb((db, _) =>
    {
        var image = AddImage(db, "unique");
        Invoke(Service(db), "Save", image.Id, Document(Payload));
        var ex = Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw(
            "INSERT INTO image_lyrics (image_id, content, font_size, text_align, view_mode, projection_watermark_path, created_time) SELECT image_id, content, font_size, text_align, view_mode, projection_watermark_path, created_time FROM image_lyrics"));
        Assert.Equal(19, ex.SqliteErrorCode);
        Assert.Equal(1L, Count(db));
    });

    [Fact]
    public void SchemaBootstrapIsIdempotentAndPreservesExistingSongs() => WithDb((db, _) =>
    {
        RequiredType("ImageColorChanger.Database.ImageLyricsSchema");
        db.LyricsProjects.Add(new LyricsProject { Name = "旧歌曲", SourceType = 1, Content = "不删除" }); db.SaveChanges();
        db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS image_lyrics");
        var schema = RequiredType("ImageColorChanger.Database.ImageLyricsSchema");
        schema.GetMethod("EnsureCreated").Invoke(null, new object[] { db });
        schema.GetMethod("EnsureCreated").Invoke(null, new object[] { db });
        var image = AddImage(db, "bootstrap");
        Invoke(Service(db), "Save", image.Id, Document(Payload));
        Assert.Equal(1L, Count(db));
        Assert.Equal("不删除", Assert.Single(db.LyricsProjects).Content);
    });

    [Fact]
    public void UnhandledImageDeletionIsBlockedRatherThanOrphaningLyrics() => WithDb((db, _) =>
    {
        var image = AddImage(db, "deletion");
        Invoke(Service(db), "Save", image.Id, Document(Payload));
        Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw("DELETE FROM images WHERE id = {0}", image.Id));
        Assert.Equal(1L, Count(db));
        Assert.Equal(Payload, Get(Invoke(Service(db), "Load", image.Id), "Content"));
        Assert.Empty(db.LyricsProjects);
    });

    private static Type RequiredType(string name)
    {
        var type = App.GetType(name);
        Assert.True(type != null, "Missing dedicated image-storage type: " + name);
        return type;
    }
    private static object Service(CanvasDbContext db) => Activator.CreateInstance(RequiredType("ImageColorChanger.Services.Lyrics.ImageLyricsService"), db);
    private static object Document(string content)
    {
        var doc = Activator.CreateInstance(RequiredType("ImageColorChanger.Services.Lyrics.LyricsDocument"));
        foreach (var item in new (string, object)[] { ("Content", content), ("FontSize", 73d), ("TextAlign", "Left"), ("ViewMode", 1), ("ProjectionWatermarkPath", "data/watermarks/fixture.png") })
            doc.GetType().GetProperty(item.Item1).SetValue(doc, item.Item2);
        return doc;
    }
    private static object Get(object obj, string field) => obj.GetType().GetProperty(field).GetValue(obj);
    private static object Invoke(object obj, string method, params object[] args)
    {
        try { return obj.GetType().GetMethod(method).Invoke(obj, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static void AssertDocument(object doc, string content)
    {
        Assert.Equal(content, Get(doc, "Content")); Assert.Equal(73d, Get(doc, "FontSize"));
        Assert.Equal("Left", Get(doc, "TextAlign")); Assert.Equal(1, Get(doc, "ViewMode"));
        Assert.Equal("data/watermarks/fixture.png", Get(doc, "ProjectionWatermarkPath"));
    }
    private static MediaFile AddImage(CanvasDbContext db, string suffix)
    {
        var image = new MediaFile { Name = suffix, Path = "D:/fixture/" + suffix + ".png", FileTypeString = "image" };
        db.MediaFiles.Add(image); db.SaveChanges(); return image;
    }
    private static long Count(CanvasDbContext db)
    {
        db.Database.OpenConnection();
        using var cmd = db.Database.GetDbConnection().CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM image_lyrics";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
    private static void WithDb(Action<CanvasDbContext, string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "canvas-image-storage-contract-" + Guid.NewGuid().ToString("N") + ".db");
        try { using var db = new CanvasDbContext(path); db.Database.EnsureCreated(); action(db, path); }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
