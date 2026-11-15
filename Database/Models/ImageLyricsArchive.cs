using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>内部恢复/迁移档案，无媒体外键，不进入歌曲库或文件树。</summary>
    [Table("image_lyrics_archive")]
    public sealed class ImageLyricsArchive
    {
        [Key, Column("id")] public int Id { get; set; }
        [Column("external_id")] public string ExternalId { get; set; } = Guid.NewGuid().ToString();
        [Column("source_key")] public string SourceKey { get; set; }
        [Column("original_image_id")] public int? OriginalImageId { get; set; }
        [Column("original_image_path")] public string OriginalImagePath { get; set; } = "";
        [Column("reason")] public string Reason { get; set; }
        [Column("document_json")] public string DocumentJson { get; set; }
        [Column("snapshot_json")] public string SnapshotJson { get; set; }
        [Column("snapshot_sha256")] public string SnapshotSha256 { get; set; }
        [Column("created_time")] public DateTime CreatedTime { get; set; } = DateTime.Now;
    }
}
