using System;

namespace ImageColorChanger.Database.Models.DTOs
{
    /// <summary>
    /// 关键帧时间序列DTO
    /// 用于播放逻辑中传递关键帧时间数据
    /// </summary>
    public class TimingSequenceDto
    {
        /// <summary>
        /// 关键帧ID
        /// </summary>
        public int KeyframeId { get; set; }

        /// <summary>
        /// 持续时间（秒）
        /// </summary>
        public double Duration { get; set; }

        /// <summary>
        /// 序列顺序（0开始）
        /// </summary>
        public int SequenceOrder { get; set; }

        /// <summary>
        /// 关键帧相对位置（0.0-1.0）
        /// </summary>
        public double Position { get; set; }

        /// <summary>
        /// Y轴位置（像素）
        /// </summary>
        public int YPosition { get; set; }

        /// <summary>
        /// 循环次数提示
        /// </summary>
        public int? LoopCount { get; set; }

        /// <summary>
        /// 暂停累计时间（秒）
        /// </summary>
        public double PausedTime { get; set; } = 0;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }
}

