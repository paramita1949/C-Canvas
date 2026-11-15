using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Canvas.TextEditor.Tests.Database;

public class ImageLyricsTransactionBehaviorTests
{
    [Fact]
    public void SaveAndReopenSameImagePreservesFullDocumentAndReportsActualStorage()
    {
        const string input = "__LYRICS_MODE_V1__{\"SingleContent\":\"原文第一节\\n第二节\",\"ThemeName\":\"白色\",\"SplitContents\":[{\"Regions\":[\"甲\",\"乙\"]}]}";
        string path = Path.Combine(Path.GetTempPath(), "canvas-transaction-behavior-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            int id;
            using (var db = new CanvasDbContext(path))
            {
                db.Database.EnsureCreated();
                var image = new MediaFile { Name = "7303", Path = "D:/fixture/7303.png", FileTypeString = "image" };
                db.MediaFiles.Add(image); db.SaveChanges(); id = image.Id;
                var manager = new LyricsProjectManager(db);
#pragma warning disable CS0618 // 同一适配器输入，比较重构前后真实存储；不作为UI生产入口。
                var document = manager.LoadOrCreateImageProject(id);
                document.Content = input; document.FontSize = 73; document.TextAlign = "Left";
                document.ProjectionWatermarkPath = "data/watermarks/a.png";
                manager.SaveImageProject(document, id);
            }
            using (var db = new CanvasDbContext(path))
            {
                var loaded = new LyricsProjectManager(db).LoadOrCreateImageProject(id);
#pragma warning restore CS0618
                Assert.Equal(input, loaded.Content); Assert.Equal(73, loaded.FontSize);
                Assert.Equal("Left", loaded.TextAlign); Assert.Equal("data/watermarks/a.png", loaded.ProjectionWatermarkPath);
                db.Database.OpenConnection();
                using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='image_lyrics'";
                bool dedicated = Convert.ToInt32(command.ExecuteScalar()) == 1;
                command.CommandText = dedicated ? "SELECT COUNT(*) FROM image_lyrics" : "SELECT 0";
                int privateRows = Convert.ToInt32(command.ExecuteScalar());
                int mixedRows = db.LyricsProjects.Count(p => p.ImageId == id);
                Assert.Equal(1, privateRows + mixedRows);
                Console.WriteLine($"IMAGE_LYRICS_BEHAVIOR: imageRows={privateRows}; mixedSongRows={mixedRows}; fullContentPreserved=True; fontSize=73; textAlign=Left; watermarkPreserved=True");
            }
        }
        finally { SqliteConnection.ClearAllPools(); foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }
}
