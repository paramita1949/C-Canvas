using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 位置类型枚举
    /// </summary>
    public enum LocationType
    {
        /// <summary>文件夹内</summary>
        Folder,
        /// <summary>根目录</summary>
        Root
    }

    /// <summary>
    /// 图片显示位置实体（支持同一张图片在多个位置显示）
    /// </summary>
    [Table("image_display_locations")]
    public class ImageDisplayLocation
    {
        /// <summary>
        /// 位置ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 图片ID
        /// </summary>
        [Required]
        [Column("image_id")]
        public int ImageId { get; set; }

        /// <summary>
        /// 位置类型：folder/root
        /// </summary>
        [Required]
        [Column("location_type")]
        public string LocationTypeString { get; set; }

        /// <summary>
        /// 文件夹ID（可为NULL，当location_type='root'时）
        /// </summary>
        [Column("folder_id")]
        public int? FolderId { get; set; }

        /// <summary>
        /// 显示顺序
        /// </summary>
        [Column("order_index")]
        public int? OrderIndex { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column("created_time")]
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 位置类型枚举（不映射到数据库）
        /// </summary>
        [NotMapped]
        public LocationType LocationType
        {
            get => LocationTypeString?.ToLower() == "root" ? LocationType.Root : LocationType.Folder;
            set => LocationTypeString = value == LocationType.Root ? "root" : "folder";
        }

        /// <summary>
        /// 导航属性：所属图片
        /// </summary>
        [ForeignKey("ImageId")]
        public virtual MediaFile MediaFile { get; set; }

        /// <summary>
        /// 导航属性：所属文件夹（可为NULL）
        /// </summary>
        [ForeignKey("FolderId")]
        public virtual Folder Folder { get; set; }
    }
}

