namespace ImageColorChanger.Database.Models.Bible
{
    /// <summary>
    /// 圣经书卷信息模型
    /// </summary>
    public class BibleBook
    {
        /// <summary>
        /// 书卷编号 (1-66)
        /// </summary>
        public int BookId { get; set; }

        /// <summary>
        /// 书卷名称（如：创世记）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 书卷简称（如：创）
        /// </summary>
        public string ShortName { get; set; }

        /// <summary>
        /// 章数
        /// </summary>
        public int ChapterCount { get; set; }

        /// <summary>
        /// 分类（如：摩西五经、历史书、诗歌智慧书等）
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// 新旧约（旧约/新约）
        /// </summary>
        public string Testament { get; set; }

        /// <summary>
        /// 是否为旧约
        /// </summary>
        public bool IsOldTestament => Testament == "旧约";

        /// <summary>
        /// 是否为新约
        /// </summary>
        public bool IsNewTestament => Testament == "新约";
    }
}

