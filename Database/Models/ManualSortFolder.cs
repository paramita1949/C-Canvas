using System;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 手动排序文件夹模型
    /// 用于标记哪些文件夹使用手动排序（拖拽排序），这些文件夹在同步时不会被自动排序
    /// </summary>
    public class ManualSortFolder
    {
        /// <summary>
        /// 文件夹ID（主键，外键关联到Folders表）
        /// </summary>
        public int FolderId { get; set; }

        /// <summary>
        /// 是否为手动排序
        /// </summary>
        public bool IsManualSort { get; set; }

        /// <summary>
        /// 最后一次手动排序的时间
        /// </summary>
        public DateTime LastManualSortTime { get; set; }

        /// <summary>
        /// 关联的文件夹
        /// </summary>
        public Folder Folder { get; set; }
    }
}
