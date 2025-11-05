using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models.Bible
{
    /// <summary>
    /// 圣经章节标题模型
    /// </summary>
    [Table("Titles")]
    public class BibleTitle
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
        /// 标题起始节号
        /// </summary>
        public int Verse { get; set; }

        /// <summary>
        /// 标题内容
        /// </summary>
        public string Scripture { get; set; }

        /// <summary>
        /// 书卷名称（计算属性，不存储在数据库）
        /// </summary>
        [NotMapped]
        public string BookName => Core.BibleBookConfig.GetBook(Book)?.Name ?? $"书卷{Book}";
    }
}

