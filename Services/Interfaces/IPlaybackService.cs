using System;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.Services.Interfaces
{
    /// <summary>
    /// 播放服务接口
    /// </summary>
    public interface IPlaybackService
    {
        /// <summary>
        /// 当前播放模式
        /// </summary>
        PlaybackMode Mode { get; }

        /// <summary>
        /// 是否正在播放
        /// </summary>
        bool IsPlaying { get; }

        /// <summary>
        /// 是否已暂停
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// 播放次数设置（-1表示无限循环）
        /// </summary>
        int PlayCount { get; set; }

        /// <summary>
        /// 已完成播放次数
        /// </summary>
        int CompletedPlayCount { get; }

        /// <summary>
        /// 播放进度更新事件
        /// </summary>
        event EventHandler<PlaybackProgressEventArgs> ProgressUpdated;

        /// <summary>
        /// 播放完成事件
        /// </summary>
        event EventHandler PlaybackCompleted;

        /// <summary>
        /// 开始播放
        /// </summary>
        Task StartPlaybackAsync(int imageId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 暂停播放
        /// </summary>
        Task PausePlaybackAsync();

        /// <summary>
        /// 继续播放
        /// </summary>
        Task ResumePlaybackAsync();

        /// <summary>
        /// 停止播放
        /// </summary>
        Task StopPlaybackAsync();
    }

    /// <summary>
    /// 播放进度事件参数
    /// </summary>
    public class PlaybackProgressEventArgs : EventArgs
    {
        /// <summary>当前项索引</summary>
        public int CurrentIndex { get; set; }

        /// <summary>总项数</summary>
        public int TotalCount { get; set; }

        /// <summary>当前项剩余时间（秒）</summary>
        public double RemainingTime { get; set; }

        /// <summary>当前项ID</summary>
        public int CurrentItemId { get; set; }
    }
}

