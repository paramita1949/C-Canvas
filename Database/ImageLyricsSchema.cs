using System;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Database
{
    /// <summary>旧库幂等建表入口。仅建新表，不迁移或删除旧歌词；尚未接入自动启动。</summary>
    public static class ImageLyricsSchema
    {
        public static void EnsureCreated(CanvasDbContext db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            using var transaction = db.Database.CurrentTransaction == null ? db.Database.BeginTransaction() : null;
            db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS image_lyrics (
    id INTEGER NOT NULL CONSTRAINT PK_image_lyrics PRIMARY KEY AUTOINCREMENT,
    image_id INTEGER NOT NULL,
    content TEXT NULL,
    font_size REAL NOT NULL,
    text_align TEXT NULL,
    view_mode INTEGER NOT NULL,
    projection_watermark_path TEXT NULL,
    created_time TEXT NOT NULL,
    modified_time TEXT NULL,
    CONSTRAINT FK_image_lyrics_images_image_id FOREIGN KEY (image_id) REFERENCES images (id) ON DELETE RESTRICT
);");
            db.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS idx_image_lyrics_image ON image_lyrics (image_id);");
            db.Database.ExecuteSqlRaw(@"
CREATE TABLE IF NOT EXISTS image_lyrics_archive (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    external_id TEXT NULL, source_key TEXT NULL,
    original_image_id INTEGER NULL, original_image_path TEXT NULL,
    reason TEXT NULL, document_json TEXT NULL, snapshot_json TEXT NULL,
    snapshot_sha256 TEXT NULL, created_time TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_image_lyrics_archive_external ON image_lyrics_archive (external_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_image_lyrics_archive_source ON image_lyrics_archive (source_key);
CREATE INDEX IF NOT EXISTS idx_image_lyrics_archive_image ON image_lyrics_archive (original_image_id);
");
            transaction?.Commit();
        }
    }
}
