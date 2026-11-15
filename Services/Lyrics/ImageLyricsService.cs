using System;
using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ImageColorChanger.Services.Lyrics
{
    /// <summary>图片歌词专属读写边界。所有权由调用方绑定的ImageId确定，不访问独立歌曲库。</summary>
    public sealed class ImageLyricsService
    {
        private readonly CanvasDbContext _db;

        public ImageLyricsService(CanvasDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public LyricsDocument Load(int imageId)
        {
            if (imageId <= 0 || !_db.MediaFiles.Any(m => m.Id == imageId && m.FileTypeString == "image")) return null;
            var stored = _db.ImageLyrics.AsNoTracking().SingleOrDefault(p => p.ImageId == imageId);
            if (stored == null) ThrowIfLegacyConflict(imageId);
            if (stored == null) return new LyricsDocument();
            return new LyricsDocument
            {
                Content = stored.Content,
                FontSize = stored.FontSize,
                TextAlign = stored.TextAlign,
                ViewMode = stored.ViewMode,
                ProjectionWatermarkPath = stored.ProjectionWatermarkPath
            };
        }

        public void Save(int imageId, LyricsDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (imageId <= 0 || !_db.MediaFiles.Any(m => m.Id == imageId && m.FileTypeString == "image"))
                throw new InvalidOperationException("对应图片已不存在，未保存歌词。");

            var stored = _db.ImageLyrics.SingleOrDefault(p => p.ImageId == imageId);
            if (stored == null)
            {
                ThrowIfLegacyConflict(imageId);
                stored = new ImageLyrics { ImageId = imageId };
                _db.ImageLyrics.Add(stored);
            }
            stored.Content = document.Content;
            stored.FontSize = document.FontSize;
            stored.TextAlign = document.TextAlign;
            stored.ViewMode = document.ViewMode;
            stored.ProjectionWatermarkPath = document.ProjectionWatermarkPath;
            stored.ModifiedTime = DateTime.Now;
            // 数据库唯一/FK约束是最后一道防线；失败向调用方报告，不吞掉丢失的保存。
            _db.SaveChanges();
        }

        private void ThrowIfLegacyConflict(int imageId)
        {
            if (_db.ImageLyricsArchives.Any(a => a.OriginalImageId == imageId && a.Reason == "LegacyConflict"))
                throw new InvalidOperationException("该图片存在旧歌词冲突，内容已保留在内部归档，未自动选择或覆盖。");
        }

        public int GetRecordId(int imageId) => _db.ImageLyrics.AsNoTracking().Where(p => p.ImageId == imageId).Select(p => p.Id).SingleOrDefault();

        public void SaveLoaded(int imageId, LyricsDocument document, int expectedRecordId)
        {
            using var transaction = _db.Database.CurrentTransaction == null ? _db.Database.BeginTransaction() : null;
            int currentId = GetRecordId(imageId);
            if (currentId != expectedRecordId)
                throw new InvalidOperationException("图片歌词已删除或被其他入口建立，请重新进入后再保存。编辑器正文仍保留。");
            Save(imageId, document);
            transaction?.Commit();
        }

        public void Restore(string archiveExternalId, int imageId)
        {
            var archive = _db.ImageLyricsArchives.AsNoTracking().SingleOrDefault(a => a.ExternalId == archiveExternalId);
            var image = _db.MediaFiles.AsNoTracking().SingleOrDefault(m => m.Id == imageId);
            if (archive == null || image == null || archive.Reason != "DeletedImage" ||
                !string.Equals(archive.OriginalImagePath, image.Path, StringComparison.OrdinalIgnoreCase) ||
                _db.ImageLyrics.Any(p => p.ImageId == imageId))
                throw new InvalidOperationException("只能将删除归档恢复到同路径且没有已有歌词的图片。");
            Save(imageId, ReadArchiveDocument(archive));
        }

        public static LyricsDocument ReadArchiveDocument(ImageLyricsArchive archive)
        {
            if (archive == null || ImageLyricsMigration.Hash(archive.SnapshotJson) != archive.SnapshotSha256)
                throw new InvalidOperationException("歌词归档摘要不一致，未恢复。");
            try
            {
                var document = JsonSerializer.Deserialize<LyricsDocument>(archive.DocumentJson);
                using var snapshot = JsonDocument.Parse(archive.SnapshotJson);
                var original = snapshot.RootElement.TryGetProperty("Name", out _) ?
                    LyricsDocument.FromProject(JsonSerializer.Deserialize<LyricsProject>(archive.SnapshotJson)) :
                    JsonSerializer.Deserialize<LyricsDocument>(archive.SnapshotJson);
                if (document == null || !document.SameAs(original))
                    throw new InvalidOperationException("歌词归档正文与原始快照不一致，未恢复。");
                return document;
            }
            catch (JsonException ex) { throw new InvalidOperationException("歌词归档格式无效，未恢复。", ex); }
        }

        /// <summary>用户明确选择版本才允许解除冲突。保留所有原档案，记录选择，不覆盖正式正文。</summary>
        public void RecoverArchive(string archiveExternalId, int imageId)
        {
            using var transaction = _db.Database.CurrentTransaction == null ? _db.Database.BeginTransaction() : null;
            var archive = _db.ImageLyricsArchives.AsNoTracking().SingleOrDefault(a => a.ExternalId == archiveExternalId);
            var image = _db.MediaFiles.AsNoTracking().SingleOrDefault(m => m.Id == imageId && m.FileTypeString == "image");
            if (archive == null || image == null || archive.Reason == "RecoveryChosen" ||
                (archive.Reason != "LegacyUnowned" && !string.Equals(archive.OriginalImagePath, image.Path, StringComparison.OrdinalIgnoreCase)) ||
                _db.ImageLyrics.Any(p => p.ImageId == imageId))
                throw new InvalidOperationException("图片不存在、路径不匹配或已有歌词，未恢复；不允许覆盖已有正文。");
            var document = ReadArchiveDocument(archive);
            var row = new ImageLyrics { ImageId = imageId }; document.ApplyTo(row); _db.ImageLyrics.Add(row);
            ImageLyricsMigration.Archive(_db, "recovery:" + archive.ExternalId + ":" + Guid.NewGuid(), imageId, image.Path,
                "RecoveryChosen", document, JsonSerializer.Serialize(document));
            _db.SaveChanges(); transaction?.Commit();
        }
    }
}
