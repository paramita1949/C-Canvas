using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Services.Lyrics
{
    /// <summary>整库增量导入的图片文档边界：使用媒体映射，不使用图片歌词的人工名称。</summary>
    public static class ImageLyricsTransfer
    {
        private static bool HasTable(CanvasDbContext db, string table)
        {
            bool close = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
            db.Database.OpenConnection();
            try
            {
                using var cmd = db.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
                var parameter = cmd.CreateParameter(); parameter.ParameterName = "$name"; parameter.Value = table; cmd.Parameters.Add(parameter);
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
            finally { if (close) db.Database.CloseConnection(); }
        }

        public static void Merge(CanvasDbContext source, CanvasDbContext target, Dictionary<int, int> mediaMap)
        {
            ImageLyricsSchema.EnsureCreated(target);
            using var transaction = target.Database.CurrentTransaction == null ? target.Database.BeginTransaction() : null;
            if (HasTable(source, "image_lyrics"))
            {
                foreach (var row in source.ImageLyrics.AsNoTracking().ToList())
                {
                    var document = LyricsDocument.FromImage(row);
                    var path = source.MediaFiles.AsNoTracking().Where(m => m.Id == row.ImageId).Select(m => m.Path).FirstOrDefault() ?? "";
                    var snapshot = JsonSerializer.Serialize(document);
                    if (mediaMap.TryGetValue(row.ImageId, out var mappedId) && target.MediaFiles.Any(m => m.Id == mappedId && m.FileTypeString == "image"))
                    {
                        var existing = target.ImageLyrics.SingleOrDefault(p => p.ImageId == mappedId);
                        if (existing == null && !target.ImageLyricsArchives.Any(a => a.OriginalImageId == mappedId && a.Reason == "LegacyConflict"))
                        {
                            var newRow = new ImageLyrics { ImageId = mappedId, CreatedTime = row.CreatedTime, ModifiedTime = row.ModifiedTime };
                            document.ApplyTo(newRow); target.ImageLyrics.Add(newRow); target.SaveChanges();
                            continue;
                        }
                        if (existing != null && document.SameAs(LyricsDocument.FromImage(existing))) continue;
                    }
                    // ID映射缺失或本地已有不同文档：明确保留在档案，不静默丢弃或覆盖。
                    ImageLyricsMigration.Archive(target, "import:" + ImageLyricsMigration.Hash(path + "\n" + snapshot),
                        mediaMap.TryGetValue(row.ImageId, out var archiveId) ? archiveId : null,
                        path, "ImportConflict", document, snapshot);
                }
            }
            if (HasTable(source, "image_lyrics_archive"))
            {
                foreach (var archive in source.ImageLyricsArchives.AsNoTracking().ToList())
                {
                    if (target.ImageLyricsArchives.Any(a => a.ExternalId == archive.ExternalId)) continue;
                    if (ImageLyricsMigration.Hash(archive.SnapshotJson) != archive.SnapshotSha256)
                        throw new InvalidOperationException("导入图片歌词归档摘要不一致，未提交该批文档。");
                    target.ImageLyricsArchives.Add(new ImageLyricsArchive
                    {
                        ExternalId = archive.ExternalId, SourceKey = "archive-import:" + archive.ExternalId,
                        OriginalImageId = archive.OriginalImageId.HasValue && mediaMap.TryGetValue(archive.OriginalImageId.Value, out var mappedId) ? mappedId : null,
                        OriginalImagePath = archive.OriginalImagePath, Reason = archive.Reason,
                        DocumentJson = archive.DocumentJson, SnapshotJson = archive.SnapshotJson,
                        SnapshotSha256 = archive.SnapshotSha256, CreatedTime = archive.CreatedTime
                    });
                }
            }
            target.SaveChanges(); transaction?.Commit();
        }

        public static void MergeLegacy(CanvasDbContext source, CanvasDbContext target, Dictionary<int, int> mediaMap)
        {
            var plan = ImageLyricsMigrationPlanner.Plan(source);
            var safe = plan.EligibleProjectIds.ToHashSet();
            var ids = safe.Concat(plan.QuarantineProjectIds).ToHashSet();
            using var transaction = target.Database.CurrentTransaction == null ? target.Database.BeginTransaction() : null;
            foreach (var row in source.LyricsProjects.AsNoTracking().Where(p => ids.Contains(p.Id)).ToList())
            {
                string snapshot = JsonSerializer.Serialize(row);
                var path = row.ImageId.HasValue ? source.MediaFiles.AsNoTracking().Where(m => m.Id == row.ImageId).Select(m => m.Path).FirstOrDefault() ?? "" : "";
                string key = "legacy-import:" + ImageLyricsMigration.Hash(path.Replace('\\', '/').ToUpperInvariant() + "\n" + snapshot);
                if (target.ImageLyricsArchives.Any(a => a.SourceKey == key)) continue;
                var document = LyricsDocument.FromProject(row);
                int? mapped = row.ImageId.HasValue && mediaMap.TryGetValue(row.ImageId.Value, out var id) ? id : null;
                string reason = row.ImageId.HasValue ? "LegacyConflict" : "LegacyUnowned";
                if (safe.Contains(row.Id) && mapped.HasValue && target.MediaFiles.Any(m => m.Id == mapped && m.FileTypeString == "image"))
                {
                    var existing = target.ImageLyrics.SingleOrDefault(p => p.ImageId == mapped);
                    if (existing == null && !target.ImageLyricsArchives.Any(a => a.OriginalImageId == mapped && a.Reason == "LegacyConflict"))
                    {
                        var newRow = new ImageLyrics { ImageId = mapped.Value, CreatedTime = row.CreatedTime, ModifiedTime = row.ModifiedTime };
                        document.ApplyTo(newRow); target.ImageLyrics.Add(newRow); reason = "LegacyMigrated";
                    }
                    else if (existing != null && document.SameAs(LyricsDocument.FromImage(existing))) reason = "LegacyMigrated";
                }
                ImageLyricsMigration.Archive(target, key, mapped, path, reason, document, snapshot);
                target.SaveChanges();
            }
            transaction?.Commit();
        }
    }
}
