using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ImageColorChanger.Core;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 幻灯片实体（类似PPT中的一页）
    /// 每个TextProject可以包含多个Slide
    /// </summary>
    [Table("slides")]
    public class Slide
    {
        /// <summary>
        /// 幻灯片ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 所属项目ID
        /// </summary>
        [Required]
        [Column("project_id")]
        public int ProjectId { get; set; }

        /// <summary>
        /// 幻灯片标题
        /// </summary>
        [Required]
        [Column("title")]
        public string Title { get; set; }

        /// <summary>
        /// 排序顺序
        /// </summary>
        [Column("sort_order")]
        public int SortOrder { get; set; }

        /// <summary>
        /// 背景图路径（可以为每张幻灯片设置不同的背景）
        /// </summary>
        [Column("background_image_path")]
        public string BackgroundImagePath { get; set; }

        /// <summary>
        /// 背景颜色
        /// </summary>
        [Column("background_color")]
        public string BackgroundColor { get; set; }

        /// <summary>
        /// 画面分割模式（0=单画面, 1=左右, 2=上下, 3=四宫格）
        /// </summary>
        [Column("split_mode")]
        public int SplitMode { get; set; } = 0;

        /// <summary>
        /// 分割区域数据（JSON格式，存储各区域的图片路径）
        /// </summary>
        [Column("split_regions_data")]
        public string SplitRegionsData { get; set; }

        /// <summary>
        /// 分割图片显示模式（0=适中居中, 1=拉伸, 2=适中置顶）
        /// </summary>
        [Column("split_stretch_mode")]
        public SplitImageDisplayMode SplitStretchMode { get; set; } = SplitImageDisplayMode.FitCenter;

        /// <summary>
        /// 是否启用视频背景（当此值为true时，BackgroundImagePath应指向视频文件）
        /// </summary>
        [Column("video_background_enabled")]
        public bool VideoBackgroundEnabled { get; set; } = false;

        /// <summary>
        /// 视频循环播放
        /// </summary>
        [Column("video_loop_enabled")]
        public bool VideoLoopEnabled { get; set; } = true;

        /// <summary>
        /// 视频音量（0.0 - 1.0）
        /// </summary>
        [Column("video_volume")]
        public double VideoVolume { get; set; } = 0.5;

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
        /// 导航属性：所属项目
        /// </summary>
        [ForeignKey("ProjectId")]
        public virtual TextProject Project { get; set; }

        /// <summary>
        /// 导航属性：幻灯片中的文本元素
        /// </summary>
        public virtual ICollection<TextElement> Elements { get; set; } = new List<TextElement>();

        /// <summary>
        /// 辅助属性：元素数量（用于UI显示）
        /// </summary>
        [NotMapped]
        public int ElementCount => Elements?.Count ?? 0;

        /// <summary>
        /// 辅助属性：显示信息（用于缩略图）
        /// </summary>
        [NotMapped]
        public string DisplayInfo => ElementCount > 0 ? $"{ElementCount} 个元素" : "";

        /// <summary>
        /// 辅助属性：缩略图路径（运行时生成）
        /// </summary>
        [NotMapped]
        public string ThumbnailPath { get; set; }

    }
}

