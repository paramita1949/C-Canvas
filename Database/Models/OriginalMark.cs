using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 项目类型枚举
    /// </summary>
    public enum ItemType
    {
        /// <summary>文件夹</summary>
        Folder,
        /// <summary>图片</summary>
        Image
    }

    /// <summary>
    /// 标记类型枚举
    /// </summary>
    public enum MarkType
    {
        /// <summary>循环模式</summary>
        Loop,
        /// <summary>顺序模式</summary>
        Sequence
    }

    /// <summary>
    /// 原图标记实体
    /// </summary>
    [Table("original_marks")]
    public class OriginalMark
    {
        /// <summary>
        /// 标记ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 项目类型：folder/image
        /// </summary>
        [Required]
        [Column("item_type")]
        public string ItemTypeString { get; set; }

        /// <summary>
        /// 项目ID
        /// </summary>
        [Required]
        [Column("item_id")]
        public int ItemId { get; set; }

        /// <summary>
        /// 标记类型：loop/sequence
        /// </summary>
        [Required]
        [Column("mark_type")]
        public string MarkTypeString { get; set; } = "loop";

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("created_time")]
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 项目类型枚举（不映射到数据库）
        /// </summary>
        [NotMapped]
        public ItemType ItemType
        {
            get => ItemTypeString?.ToLower() == "image" ? ItemType.Image : ItemType.Folder;
            set => ItemTypeString = value == ItemType.Image ? "image" : "folder";
        }

        /// <summary>
        /// 标记类型枚举（不映射到数据库）
        /// </summary>
        [NotMapped]
        public MarkType MarkType
        {
            get => MarkTypeString?.ToLower() == "sequence" ? MarkType.Sequence : MarkType.Loop;
            set => MarkTypeString = value == MarkType.Sequence ? "sequence" : "loop";
        }
    }

    /// <summary>
    /// 原图模式时间记录实体
    /// </summary>
    [Table("original_mode_timings")]
    public class OriginalModeTiming
    {
        /// <summary>
        /// 记录ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 基础图片ID
        /// </summary>
        [Required]
        [Column("base_image_id")]
        public int BaseImageId { get; set; }

        /// <summary>
        /// 起始图片ID
        /// </summary>
        [Required]
        [Column("from_image_id")]
        public int FromImageId { get; set; }

        /// <summary>
        /// 目标图片ID
        /// </summary>
        [Required]
        [Column("to_image_id")]
        public int ToImageId { get; set; }

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
        /// 标记类型：loop/sequence
        /// </summary>
        [Column("mark_type")]
        public string MarkTypeString { get; set; } = "loop";

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 标记类型枚举（不映射到数据库）
        /// </summary>
        [NotMapped]
        public MarkType MarkType
        {
            get => MarkTypeString?.ToLower() == "sequence" ? MarkType.Sequence : MarkType.Loop;
            set => MarkTypeString = value == MarkType.Sequence ? "sequence" : "loop";
        }
    }
}

