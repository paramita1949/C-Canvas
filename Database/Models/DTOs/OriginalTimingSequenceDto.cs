using System;

namespace ImageColorChanger.Database.Models.DTOs
{
    /// <summary>
    /// 原图模式时间序列DTO
    /// 用于原图模式播放逻辑中传递时间数据
    /// </summary>
    public class OriginalTimingSequenceDto
    {
        /// <summary>
        /// 记录ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 基础图片ID（主图）
        /// </summary>
        public int BaseImageId { get; set; }

        /// <summary>
        /// 起始图片ID（从哪张图片切换）
        /// </summary>
        public int FromImageId { get; set; }

        /// <summary>
        /// 目标图片ID（切换到哪张图片）
        /// </summary>
        public int ToImageId { get; set; }

        /// <summary>
        /// 相似图片ID（原图） - 兼容性属性，等同于 ToImageId
        /// </summary>
        public int SimilarImageId
        {
            get => ToImageId;
            set => ToImageId = value;
        }

        /// <summary>
        /// 持续时间（秒）
        /// </summary>
        public double Duration { get; set; }

        /// <summary>
        /// 序列顺序（0开始）
        /// </summary>
        public int SequenceOrder { get; set; }

        /// <summary>
        /// 暂停累计时间（秒）
        /// </summary>
        public double PausedTime { get; set; } = 0;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 相似图片路径（用于快速访问）
        /// </summary>
        public string SimilarImagePath { get; set; }
    }
}

