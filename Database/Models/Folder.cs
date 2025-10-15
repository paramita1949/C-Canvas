using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 文件夹实体
    /// </summary>
    [Table("folders")]
    public class Folder
    {
        /// <summary>
        /// 文件夹ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 文件夹名称
        /// </summary>
        [Required]
        [Column("name")]
        public string Name { get; set; }

        /// <summary>
        /// 文件夹路径（唯一）
        /// </summary>
        [Required]
        [Column("path")]
        public string Path { get; set; }

        /// <summary>
        /// 显示顺序索引
        /// </summary>
        [Column("order_index")]
        public int? OrderIndex { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("created_time")]
        public DateTime CreatedTime { get; set; } = DateTime.Now;
        
        /// <summary>
        /// 视频播放模式: sequential/random/loop_all (null表示使用默认模式)
        /// </summary>
        [Column("video_play_mode")]
        public string VideoPlayMode { get; set; }
        
        /// <summary>
        /// 是否自动开启变色效果 (1=开启, 0或null=关闭)
        /// </summary>
        [Column("auto_color_effect")]
        public int? AutoColorEffect { get; set; }
        
        /// <summary>
        /// 自定义高亮颜色 (格式: #RRGGBB, null表示使用默认颜色池)
        /// </summary>
        [Column("highlight_color")]
        public string HighlightColor { get; set; }

        /// <summary>
        /// 导航属性：文件夹下的媒体文件
        /// </summary>
        public virtual ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();

        /// <summary>
        /// 导航属性：手动排序设置
        /// </summary>
        public virtual ManualSortFolder ManualSortFolder { get; set; }

        /// <summary>
        /// 导航属性：原图标记
        /// </summary>
        public virtual ICollection<OriginalMark> OriginalMarks { get; set; } = new List<OriginalMark>();
    }
}

