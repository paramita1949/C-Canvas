using System.Reflection;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Canvas.TextEditor.Tests.Database;

public class ImageLyricsMigrationPlannerTests
{
    [Fact]
    public void LegacyOwnershipPlanSeparatesUniqueBindingsConflictsAndUncertainRowsWithoutWrites()
    {
        var path = Path.Combine(Path.GetTempPath(), "canvas-legacy-plan-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new CanvasDbContext(path); db.Database.EnsureCreated();
            var a = new MediaFile { Name = "a", Path = "D:/fixture/plan-a.png" };
            var b = new MediaFile { Name = "b", Path = "D:/fixture/plan-b.png" };
            var c = new MediaFile { Name = "c", Path = "D:/fixture/plan-c.png" };
            db.MediaFiles.AddRange(a, b, c); db.SaveChanges();
            var unique = new LyricsProject { Name = "普通名字", ImageId = a.Id, SourceType = 0, Content = "唯一图片完整正文" };
            var duplicate1 = new LyricsProject { Name = "不同名字一", ImageId = b.Id, SourceType = 0, Content = "不覆盖甲" };
            var duplicate2 = new LyricsProject { Name = "不同名字二", ImageId = b.Id, SourceType = 0, Content = "不覆盖乙" };
            var orphan = new LyricsProject { Name = "歌词_图片_7303", SourceType = 0, Content = "失联保留" };
            var mixed = new LyricsProject { Name = "来源冲突", ImageId = c.Id, SourceType = 1, Content = "待确认" };
            var song = new LyricsProject { Name = "歌词_图片_合法歌曲", SourceType = 1, Content = "真歌曲" };
            db.LyricsProjects.AddRange(unique, duplicate1, duplicate2, orphan, mixed, song); db.SaveChanges();
            var before = db.LyricsProjects.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.ImageId, p.SourceType, p.Content }).ToArray();
            var type = typeof(CanvasDbContext).Assembly.GetType("ImageColorChanger.Services.Lyrics.ImageLyricsMigrationPlanner");
            Assert.True(type != null, "Missing read-only migration planner; never start destructive migration before ownership classification.");
            var report = type.GetMethod("Plan", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { db });
            var eligible = (IEnumerable<int>)report.GetType().GetProperty("EligibleProjectIds").GetValue(report);
            var conflicts = (IEnumerable<int>)report.GetType().GetProperty("ConflictImageIds").GetValue(report);
            var retained = (IEnumerable<int>)report.GetType().GetProperty("RetainedProjectIds").GetValue(report);
            Assert.Equal(new[] { unique.Id }, eligible.OrderBy(x => x));
            Assert.Equal(new[] { b.Id, c.Id }, conflicts.OrderBy(x => x));
            Assert.Contains(orphan.Id, retained); Assert.Contains(song.Id, retained);
            var after = db.LyricsProjects.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.ImageId, p.SourceType, p.Content }).ToArray();
            Assert.Equal(before, after);
            Assert.False(db.ChangeTracker.HasChanges());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}
