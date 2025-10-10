using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 关键帧实体
    /// </summary>
    [Table("keyframes")]
    public class Keyframe
    {
        /// <summary>
        /// 关键帧ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 所属图片ID
        /// </summary>
        [Required]
        [Column("image_id")]
        public int ImageId { get; set; }

        /// <summary>
        /// 相对位置（0.0-1.0）
        /// </summary>
        [Required]
        [Column("position")]
        public double Position { get; set; }

        /// <summary>
        /// Y轴位置（像素）
        /// </summary>
        [Required]
        [Column("y_position")]
        public int YPosition { get; set; }

        /// <summary>
        /// 关键帧顺序
        /// </summary>
        [Column("order_index")]
        public int? OrderIndex { get; set; }

        /// <summary>
        /// 导航属性：所属图片
        /// </summary>
        [ForeignKey("ImageId")]
        public virtual MediaFile MediaFile { get; set; }

        /// <summary>
        /// 导航属性：时间记录
        /// </summary>
        public virtual ICollection<KeyframeTiming> Timings { get; set; } = new List<KeyframeTiming>();
    }

    /// <summary>
    /// 关键帧时间记录实体
    /// </summary>
    [Table("keyframe_timings")]
    public class KeyframeTiming
    {
        /// <summary>
        /// 记录ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 所属图片ID
        /// </summary>
        [Required]
        [Column("image_id")]
        public int ImageId { get; set; }

        /// <summary>
        /// 关键帧ID
        /// </summary>
        [Required]
        [Column("keyframe_id")]
        public int KeyframeId { get; set; }

        /// <summary>
        /// 持续时间（秒）
        /// </summary>
        [Required]
        [Column("duration")]
        public double Duration { get; set; }

        /// <summary>
        /// 序列顺序
        /// </summary>
        [Required]
        [Column("sequence_order")]
        public int SequenceOrder { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 导航属性：所属图片
        /// </summary>
        [ForeignKey("ImageId")]
        public virtual MediaFile MediaFile { get; set; }

        /// <summary>
        /// 导航属性：关键帧
        /// </summary>
        [ForeignKey("KeyframeId")]
        public virtual Keyframe Keyframe { get; set; }
    }
}

