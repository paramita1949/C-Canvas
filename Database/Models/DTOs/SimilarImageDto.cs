namespace ImageColorChanger.Database.Models.DTOs
{
    /// <summary>
    /// 相似图片DTO
    /// 用于原图模式中识别和管理相似图片
    /// </summary>
    public class SimilarImageDto
    {
        /// <summary>
        /// 图片ID
        /// </summary>
        public int ImageId { get; set; }

        /// <summary>
        /// 图片名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 图片完整路径
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// 所属文件夹ID
        /// </summary>
        public int? FolderId { get; set; }

        /// <summary>
        /// 排序索引
        /// </summary>
        public int? OrderIndex { get; set; }

        /// <summary>
        /// 相似度评分（0-100）
        /// </summary>
        public int SimilarityScore { get; set; } = 100;

        /// <summary>
        /// 是否为主图
        /// </summary>
        public bool IsPrimary { get; set; }

        /// <summary>
        /// 是否标记为原图
        /// </summary>
        public bool IsOriginal { get; set; }

        /// <summary>
        /// 匹配模式（如：序号匹配、后缀匹配等）
        /// </summary>
        public string MatchPattern { get; set; }
    }
}

