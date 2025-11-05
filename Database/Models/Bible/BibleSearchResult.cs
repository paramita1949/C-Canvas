namespace ImageColorChanger.Database.Models.Bible
{
    /// <summary>
    /// 圣经搜索结果模型
    /// </summary>
    public class BibleSearchResult
    {
        /// <summary>
        /// 书卷编号
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
        /// 书卷名称
        /// </summary>
        public string BookName { get; set; }

        /// <summary>
        /// 引用格式（如：创世记 1:1）
        /// </summary>
        public string Reference { get; set; }

        /// <summary>
        /// 版本ID（用于多版本支持）
        /// </summary>
        public string VersionId { get; set; }
    }
}

