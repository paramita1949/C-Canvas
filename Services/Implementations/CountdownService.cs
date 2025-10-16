using System;
using System.Diagnostics;
using System.Windows.Threading;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services.Implementations
{
    /// <summary>
    /// 倒计时服务实现
    /// 参考Python版本：LOGIC_ANALYSIS_03 行629-687
    /// </summary>
    public class CountdownService : ICountdownService
    {
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _stopwatch;
        private double _totalDuration;
        private double _pausedElapsedTime;
        private double _lastRemainingTime = -1;

        /// <summary>
        /// 当前剩余时间（秒）
        /// </summary>
        public double RemainingTime { get; private set; }

        /// <summary>
        /// 是否正在倒计时
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// 倒计时更新事件
        /// </summary>
        public event EventHandler<CountdownUpdateEventArgs> CountdownUpdated;

        /// <summary>
        /// 倒计时完成事件
        /// </summary>
        public event EventHandler CountdownCompleted;

        public CountdownService()
        {
            _stopwatch = new Stopwatch();
            
            // 创建高频定时器（10ms间隔，100次/秒）
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            _timer.Tick += OnTimerTick;
        }

        /// <summary>
        /// 启动倒计时
        /// </summary>
        public void Start(double duration)
        {
            if (duration <= 0)
            {
                //System.Diagnostics.Debug.WriteLine($"⚠️ [倒计时] 时长无效: {duration}秒，忽略启动");
                return;
            }

            _totalDuration = duration;
            _pausedElapsedTime = 0;
            RemainingTime = duration;
            _lastRemainingTime = -1;
            IsRunning = true;

            _stopwatch.Restart();
            _timer.Start();
            
            //System.Diagnostics.Debug.WriteLine($"⏱️ [倒计时] 启动倒计时: {duration:F1}秒");
        }

        /// <summary>
        /// 暂停倒计时
        /// </summary>
        public void Pause()
        {
            if (!IsRunning)
                return;

            _stopwatch.Stop();
            _timer.Stop();
            _pausedElapsedTime = _stopwatch.Elapsed.TotalSeconds;
            IsRunning = false;
            
            //System.Diagnostics.Debug.WriteLine($"⏸️ [倒计时] 暂停倒计时，剩余: {RemainingTime:F1}秒");
        }

        /// <summary>
        /// 继续倒计时
        /// </summary>
        public void Resume()
        {
            if (IsRunning)
                return;

            IsRunning = true;
            _stopwatch.Start();
            _timer.Start();
            
            //System.Diagnostics.Debug.WriteLine($"▶️ [倒计时] 继续倒计时，剩余: {RemainingTime:F1}秒");
        }

        /// <summary>
        /// 停止倒计时
        /// </summary>
        public void Stop()
        {
            _stopwatch.Stop();
            _timer.Stop();
            IsRunning = false;
            RemainingTime = 0;
            _lastRemainingTime = -1;
        }

        /// <summary>
        /// 重置倒计时
        /// </summary>
        public void Reset()
        {
            Stop();
            _pausedElapsedTime = 0;
        }

        /// <summary>
        /// 定时器Tick事件处理
        /// </summary>
        private void OnTimerTick(object sender, EventArgs e)
        {
            if (!IsRunning)
                return;

            // 计算已过去的时间
            var elapsedTime = _pausedElapsedTime + _stopwatch.Elapsed.TotalSeconds;

            // 计算剩余时间
            RemainingTime = _totalDuration - elapsedTime;

            // 值变化检测：只在值变化时更新UI（优化性能）
            var currentRemainingSeconds = Math.Floor(RemainingTime * 10) / 10.0; // 保留1位小数
            if (Math.Abs(currentRemainingSeconds - _lastRemainingTime) < 0.01)
                return;

            _lastRemainingTime = currentRemainingSeconds;

            // 检查是否倒计时结束
            if (RemainingTime <= 0)
            {
                RemainingTime = 0;
                Stop();
                CountdownCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            // 触发更新事件
            CountdownUpdated?.Invoke(this, new CountdownUpdateEventArgs
            {
                RemainingTime = RemainingTime,
                ElapsedTime = elapsedTime,
                TotalDuration = _totalDuration
            });
        }
    }
}

