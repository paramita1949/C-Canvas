using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
        /// 分割图片拉伸模式（false=适中显示Uniform, true=拉伸显示Fill）
        /// </summary>
        [Column("split_stretch_mode")]
        public bool SplitStretchMode { get; set; } = false;

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

        /// <summary>
        /// 辅助属性：缩略图图片源（用于UI绑定）
        /// </summary>
        [NotMapped]
        public System.Windows.Media.ImageSource ThumbnailImage
        {
            get
            {
                if (string.IsNullOrEmpty(ThumbnailPath) || !System.IO.File.Exists(ThumbnailPath))
                    return null;
                
                try
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                    bitmap.UriSource = new Uri(ThumbnailPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}

