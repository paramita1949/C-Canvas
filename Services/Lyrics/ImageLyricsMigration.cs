using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Services.Lyrics
{
    public static class ImageLyricsMigration
    {
        /// <summary>可重复执行：只处理仍存在的旧记录；档案、复制和删除属于同一事务。</summary>
        public static void Run(CanvasDbContext db)
        {
            ImageLyricsSchema.EnsureCreated(db);
            using var transaction = db.Database.CurrentTransaction == null ? db.Database.BeginTransaction() : null;
            try
            {
                var plan = ImageLyricsMigrationPlanner.Plan(db);
                var eligible = plan.EligibleProjectIds.ToHashSet();
                var ids = eligible.Concat(plan.QuarantineProjectIds).ToHashSet();
                if (ids.Count == 0) { transaction?.Commit(); return; }
                var rows = db.LyricsProjects.AsNoTracking().Where(p => ids.Contains(p.Id)).ToList();
                foreach (var row in rows)
                {
                    var document = LyricsDocument.FromProject(row);
                    var reason = row.ImageId.HasValue ? "LegacyConflict" : "LegacyUnowned";
                    if (eligible.Contains(row.Id))
                    {
                        var stored = db.ImageLyrics.SingleOrDefault(p => p.ImageId == row.ImageId.Value);
                        if (stored == null && !db.ImageLyricsArchives.Any(a => a.OriginalImageId == row.ImageId && a.Reason == "LegacyConflict"))
                        {
                            stored = new ImageLyrics { ImageId = row.ImageId.Value, CreatedTime = row.CreatedTime, ModifiedTime = row.ModifiedTime };
                            document.ApplyTo(stored); db.ImageLyrics.Add(stored);
                            reason = "LegacyMigrated";
                        }
                        else if (stored != null && document.SameAs(LyricsDocument.FromImage(stored))) reason = "LegacyMigrated";
                        // 已有不同正文绝不覆盖，以冲突档案保存旧内容。
                    }
                    var path = row.ImageId.HasValue ? db.MediaFiles.AsNoTracking().Where(m => m.Id == row.ImageId).Select(m => m.Path).FirstOrDefault() : "";
                    var snapshot = JsonSerializer.Serialize(row);
                    Archive(db, "legacy:" + (string.IsNullOrEmpty(row.ExternalId) ? row.Id.ToString() : row.ExternalId) + ":" + Hash(snapshot), row.ImageId, path, reason, document, snapshot);
                    var entry = db.LyricsProjects.Local.FirstOrDefault(p => p.Id == row.Id) ?? row;
                    db.LyricsProjects.Remove(entry);
                }
                db.SaveChanges();
                // 持久层验证：每个迁移正文与原始文档等价；归档完整存在，否则整个事务撤回。
                foreach (var row in rows)
                {
                    var snapshot = JsonSerializer.Serialize(row);
                    if (!db.ImageLyricsArchives.AsNoTracking().Any(a => a.SnapshotSha256 == Hash(snapshot)))
                        throw new InvalidOperationException("图片歌词迁移归档验证失败，未提交迁移。");
                }
                transaction?.Commit();
            }
            catch { db.ChangeTracker.Clear(); throw; }
        }

        internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        internal static void Archive(CanvasDbContext db, string key, int? imageId, string path, string reason, LyricsDocument document, string snapshot)
        {
            if (db.ImageLyricsArchives.Local.Any(a => a.SourceKey == key) || db.ImageLyricsArchives.Any(a => a.SourceKey == key)) return;
            db.ImageLyricsArchives.Add(new ImageLyricsArchive
            {
                SourceKey = key, OriginalImageId = imageId, OriginalImagePath = path ?? "", Reason = reason,
                DocumentJson = JsonSerializer.Serialize(document), SnapshotJson = snapshot, SnapshotSha256 = Hash(snapshot)
            });
        }

        /// <summary>在EF统一提交边界准备归档；同步/异步以及文件夹批量删除共用同一事务。</summary>
        public static void PrepareImageDeletion(CanvasDbContext db)
        {
            db.ChangeTracker.DetectChanges();
            var deleted = db.ChangeTracker.Entries<MediaFile>().Where(e => e.State == EntityState.Deleted).Select(e => e.Entity).ToList();
            foreach (var image in deleted)
            {
                foreach (var row in db.ImageLyrics.Where(p => p.ImageId == image.Id).ToList())
                {
                    var doc = LyricsDocument.FromImage(row);
                    Archive(db, "deleted:" + Guid.NewGuid(), image.Id, image.Path, "DeletedImage", doc, JsonSerializer.Serialize(doc));
                    db.ImageLyrics.Remove(row);
                }
            }
        }
    }
}
