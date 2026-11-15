using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>图片私有歌词。不是歌曲库条目，没有名称、分组和列表排序。</summary>
    [Table("image_lyrics")]
    public class ImageLyrics
    {
        [Key, Column("id")]
        public int Id { get; set; }

        [Column("image_id")]
        public int ImageId { get; set; }

        [Column("content")]
        public string Content { get; set; } = "";

        [Column("font_size")]
        public double FontSize { get; set; } = 60;

        [Column("text_align")]
        public string TextAlign { get; set; } = "Center";

        [Column("view_mode")]
        public int ViewMode { get; set; }

        [Column("projection_watermark_path")]
        public string ProjectionWatermarkPath { get; set; } = "";

        [Column("created_time")]
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        [Column("modified_time")]
        public DateTime? ModifiedTime { get; set; }

        public virtual MediaFile MediaFile { get; set; }
    }
}
