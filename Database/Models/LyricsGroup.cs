using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 歌词分组（虚拟分组，不对应磁盘目录）。
    /// </summary>
    [Table("lyrics_groups")]
    public class LyricsGroup
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = "";

        [Column("external_id")]
        public string ExternalId { get; set; } = Guid.NewGuid().ToString();

        [Column("sort_order")]
        public int SortOrder { get; set; }

        [Column("created_time")]
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        [Column("modified_time")]
        public DateTime? ModifiedTime { get; set; }

        [Column("highlight_color")]
        public string HighlightColor { get; set; }

        [Column("is_system")]
        public bool IsSystem { get; set; }
    }
}
