using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.Database.Models.DTOs
{
    /// <summary>
    /// 播放状态DTO
    /// 用于在UI层和服务层之间传递播放状态信息
    /// </summary>
    public class PlaybackStateDto
    {
        /// <summary>
        /// 当前播放状态
        /// </summary>
        public PlaybackStatus Status { get; set; }

        /// <summary>
        /// 当前播放模式
        /// </summary>
        public PlaybackMode Mode { get; set; }

        /// <summary>
        /// 当前图片ID
        /// </summary>
        public int? CurrentImageId { get; set; }

        /// <summary>
        /// 当前关键帧索引（关键帧模式下）
        /// </summary>
        public int? CurrentKeyframeIndex { get; set; }

        /// <summary>
        /// 当前相似图片索引（原图模式下）
        /// </summary>
        public int? CurrentSimilarImageIndex { get; set; }

        /// <summary>
        /// 播放次数设置（-1表示无限循环）
        /// </summary>
        public int PlayCount { get; set; } = -1;

        /// <summary>
        /// 已完成播放次数
        /// </summary>
        public int CompletedPlayCount { get; set; } = 0;

        /// <summary>
        /// 当前剩余时间（秒）
        /// </summary>
        public double RemainingTime { get; set; }

        /// <summary>
        /// 是否有时间数据
        /// </summary>
        public bool HasTimingData { get; set; }

        /// <summary>
        /// 暂停开始时间（用于计算暂停时长）
        /// </summary>
        public double? PauseStartTime { get; set; }

        /// <summary>
        /// 是否启用平滑滚动
        /// </summary>
        public bool IsSmoothScrollEnabled { get; set; } = true;
    }
}

