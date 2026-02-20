using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 文件夹-素材映射（支持一个素材属于多个导入目录）
    /// </summary>
    [Table("folder_images")]
    public class FolderImage
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("folder_id")]
        public int FolderId { get; set; }

        [Column("image_id")]
        public int ImageId { get; set; }

        [Column("order_index")]
        public int? OrderIndex { get; set; }

        [Column("relative_path")]
        public string RelativePath { get; set; }

        [Column("discovered_at")]
        public DateTime? DiscoveredAt { get; set; }

        [Column("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [Column("is_hidden")]
        public bool IsHidden { get; set; }

        [ForeignKey(nameof(FolderId))]
        public virtual Folder Folder { get; set; }

        [ForeignKey(nameof(ImageId))]
        public virtual MediaFile MediaFile { get; set; }
    }
}
