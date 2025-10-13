using System;

namespace ImageColorChanger.Services.Interfaces
{
    /// <summary>
    /// 倒计时服务接口
    /// </summary>
    public interface ICountdownService
    {
        /// <summary>
        /// 当前剩余时间（秒）
        /// </summary>
        double RemainingTime { get; }

        /// <summary>
        /// 是否正在倒计时
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 倒计时更新事件（高频更新，建议5-10ms）
        /// </summary>
        event EventHandler<CountdownUpdateEventArgs> CountdownUpdated;

        /// <summary>
        /// 倒计时完成事件
        /// </summary>
        event EventHandler CountdownCompleted;

        /// <summary>
        /// 启动倒计时
        /// </summary>
        void Start(double duration);

        /// <summary>
        /// 暂停倒计时
        /// </summary>
        void Pause();

        /// <summary>
        /// 继续倒计时
        /// </summary>
        void Resume();

        /// <summary>
        /// 停止倒计时
        /// </summary>
        void Stop();

        /// <summary>
        /// 重置倒计时
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// 倒计时更新事件参数
    /// </summary>
    public class CountdownUpdateEventArgs : EventArgs
    {
        /// <summary>剩余时间（秒）</summary>
        public double RemainingTime { get; set; }

        /// <summary>已过去时间（秒）</summary>
        public double ElapsedTime { get; set; }

        /// <summary>总时长（秒）</summary>
        public double TotalDuration { get; set; }

        /// <summary>进度百分比（0-100）</summary>
        public double ProgressPercentage => TotalDuration > 0 ? (ElapsedTime / TotalDuration * 100) : 0;
    }
}

