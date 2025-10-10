using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 手动排序文件夹实体
    /// </summary>
    [Table("manual_sort_folders")]
    public class ManualSortFolder
    {
        /// <summary>
        /// 文件夹ID（主键）
        /// </summary>
        [Key]
        [Column("folder_id")]
        public int FolderId { get; set; }

        /// <summary>
        /// 是否手动排序
        /// </summary>
        [Column("is_manual_sort")]
        public bool IsManualSort { get; set; } = false;

        /// <summary>
        /// 最后手动排序时间
        /// </summary>
        [Column("last_manual_sort_time")]
        public DateTime LastManualSortTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 导航属性：所属文件夹
        /// </summary>
        [ForeignKey("FolderId")]
        public virtual Folder Folder { get; set; }
    }
}

