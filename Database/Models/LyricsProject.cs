using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 歌词项目实体（极简版）
    /// 用于存储歌谱文字内容
    /// </summary>
    [Table("lyrics_projects")]
    public class LyricsProject
    {
        /// <summary>
        /// 项目ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 项目名称
        /// </summary>
        [Required]
        [Column("name")]
        public string Name { get; set; }

        /// <summary>
        /// 关联的图片ID（每张图片对应一个歌词）
        /// </summary>
        [Column("image_id")]
        public int? ImageId { get; set; }

        /// <summary>
        /// 所属歌词分组ID（可空：兼容历史数据）
        /// </summary>
        [Column("group_id")]
        public int? GroupId { get; set; }

        /// <summary>
        /// 外部唯一标识（用于导入导出冲突匹配）
        /// </summary>
        [Column("external_id")]
        public string ExternalId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 组内排序
        /// </summary>
        [Column("sort_order")]
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// 来源类型（0=图片关联歌词，1=歌词库独立歌曲）
        /// </summary>
        [Column("source_type")]
        public int SourceType { get; set; } = 0;

        /// <summary>
        /// 歌词文本内容
        /// </summary>
        [Column("content")]
        public string Content { get; set; } = "";

        /// <summary>
        /// 字号（默认48）
        /// </summary>
        [Column("font_size")]
        public double FontSize { get; set; } = 48;

        /// <summary>
        /// 文本对齐方式（Left/Center/Right，默认Center）
        /// </summary>
        [Column("text_align")]
        public string TextAlign { get; set; } = "Center";

        /// <summary>
        /// 显示模式（0=全图模式, 1=原图模式）
        /// </summary>
        [Column("view_mode")]
        public int ViewMode { get; set; } = 0;

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("created_time")]
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后修改时间
        /// </summary>
        [Column("modified_time")]
        public DateTime? ModifiedTime { get; set; }

        /// <summary>
        /// 导航属性：关联的媒体文件
        /// </summary>
        [ForeignKey("ImageId")]
        public virtual MediaFile MediaFile { get; set; }

        /// <summary>
        /// 导航属性：所属歌词分组
        /// </summary>
        [ForeignKey("GroupId")]
        public virtual LyricsGroup LyricsGroup { get; set; }
    }
}

