using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 文本项目实体
    /// 类似PPT的文本排版项目
    /// </summary>
    [Table("text_projects")]
    public class TextProject
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
        /// 背景图路径
        /// </summary>
        [Column("background_image_path")]
        public string BackgroundImagePath { get; set; }

        /// <summary>
        /// 画布宽度（默认1920）
        /// </summary>
        [Column("canvas_width")]
        public int CanvasWidth { get; set; } = 1920;

        /// <summary>
        /// 画布高度（默认1080）
        /// </summary>
        [Column("canvas_height")]
        public int CanvasHeight { get; set; } = 1080;

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
        /// 导航属性：项目中的文本元素（兼容旧数据，直接关联项目的元素）
        /// </summary>
        public virtual ICollection<TextElement> Elements { get; set; } = new List<TextElement>();

        /// <summary>
        /// 导航属性：项目中的幻灯片
        /// </summary>
        public virtual ICollection<Slide> Slides { get; set; } = new List<Slide>();
    }
}

