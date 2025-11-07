using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 圣经历史记录实体（对应20个槽位）
    /// </summary>
    [Table("bible_history")]
    public class BibleHistoryRecord
    {
        /// <summary>
        /// 槽位索引（1-20，作为主键）
        /// </summary>
        [Key]
        [Column("slot_index")]
        public int SlotIndex { get; set; }

        /// <summary>
        /// 显示文本（例如："创世记 1:1-5"）
        /// </summary>
        [Column("display_text")]
        public string DisplayText { get; set; }

        /// <summary>
        /// 书卷ID（0表示空槽位）
        /// </summary>
        [Column("book_id")]
        public int BookId { get; set; }

        /// <summary>
        /// 章号（0表示空槽位）
        /// </summary>
        [Column("chapter")]
        public int Chapter { get; set; }

        /// <summary>
        /// 起始节号（0表示空槽位）
        /// </summary>
        [Column("start_verse")]
        public int StartVerse { get; set; }

        /// <summary>
        /// 结束节号（0表示空槽位）
        /// </summary>
        [Column("end_verse")]
        public int EndVerse { get; set; }

        /// <summary>
        /// 是否勾选（用于自动加载）
        /// </summary>
        [Column("is_checked")]
        public bool IsChecked { get; set; }

        /// <summary>
        /// 是否锁定（锁定的记录会自动加载到主屏幕）
        /// </summary>
        [Column("is_locked")]
        public bool IsLocked { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        [Column("updated_time")]
        public DateTime UpdatedTime { get; set; } = DateTime.Now;
    }
}

