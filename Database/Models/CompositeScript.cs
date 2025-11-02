using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 合成播放脚本实体
    /// 用于存储合成播放的时间控制参数
    /// </summary>
    [Table("composite_scripts")]
    public class CompositeScript
    {
        /// <summary>
        /// 脚本ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 所属图片ID（唯一）
        /// </summary>
        [Required]
        [Column("image_id")]
        public int ImageId { get; set; }

        /// <summary>
        /// 总时长（秒）
        /// - 当没有关键帧时，这是用户手动设定的默认值（默认为100秒）
        /// - 当有关键帧时，这是关键帧时间的累计值（自动计算）
        /// </summary>
        [Required]
        [Column("total_duration")]
        public double TotalDuration { get; set; } = 100.0;

        /// <summary>
        /// 是否自动计算总时长（有关键帧时为true，无关键帧时为false）
        /// </summary>
        [Column("auto_calculate")]
        public bool AutoCalculate { get; set; } = false;

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 导航属性：所属图片
        /// </summary>
        [ForeignKey("ImageId")]
        public virtual MediaFile MediaFile { get; set; }
    }
}

