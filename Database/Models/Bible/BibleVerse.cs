using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models.Bible
{
    /// <summary>
    /// 圣经经文模型
    /// </summary>
    [Table("Bible")]
    public class BibleVerse
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 书卷编号 (1-66)
        /// </summary>
        public int Book { get; set; }

        /// <summary>
        /// 章节号
        /// </summary>
        public int Chapter { get; set; }

        /// <summary>
        /// 节号
        /// </summary>
        public int Verse { get; set; }

        /// <summary>
        /// 经文内容
        /// </summary>
        public string Scripture { get; set; }

        /// <summary>
        /// 书卷名称（计算属性，不存储在数据库）
        /// </summary>
        [NotMapped]
        public string BookName => Core.BibleBookConfig.GetBook(Book)?.Name ?? $"书卷{Book}";

        /// <summary>
        /// 引用格式（计算属性）：如"创世记 1:1"
        /// </summary>
        [NotMapped]
        public string Reference => $"{BookName} {Chapter}:{Verse}";

        /// <summary>
        /// 章节引用格式（计算属性）：如"创世记 1 章"
        /// </summary>
        [NotMapped]
        public string ChapterReference => $"{BookName} {Chapter} 章";
    }
}

