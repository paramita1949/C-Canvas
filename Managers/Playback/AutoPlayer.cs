using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers.Keyframes;
using ImageColorChanger.UI;

namespace ImageColorChanger.Managers.Playback
{
    /// <summary>
    /// 自动播放器
    /// 负责关键帧的自动播放、循环控制、倒计时显示、暂停/继续
    /// </summary>
    public class AutoPlayer
    {
        private readonly MainWindow _mainWindow;
        private readonly TimeRecorder _timeRecorder;
        private readonly KeyframeManager _keyframeManager;

        #region 播放状态

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// 是否已暂停
        /// </summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// 循环模式是否启用
        /// </summary>
        public bool LoopEnabled { get; set; } = true;

        /// <summary>
        /// 播放速度倍率（0.1-5.0）
        /// </summary>
        public double PlaySpeed { get; set; } = 1.0;

        #endregion

        #region 播放控制

        private int? _currentImageId;
        private List<KeyframeTiming> _timingSequence = new();
        private int _currentSequenceIndex;
        private DispatcherTimer _playTimer;

        #endregion

        #region 播放次数控制

        /// <summary>
        /// 目标播放次数（-1表示无限循环）
        /// </summary>
        public int TargetPlayCount { get; set; } = 5;

        /// <summary>
        /// 已完成的播放次数
        /// </summary>
        public int CompletedPlayCount { get; private set; }

        private int _loopCount;

        #endregion

        #region 倒计时

        /// <summary>
        /// 倒计时是否启用
        /// </summary>
        public bool CountdownEnabled { get; set; } = true;

        private DispatcherTimer _countdownTimer;
        private DateTime? _nextFrameTime;
        private double _currentFrameDuration;
        private readonly int _countdownUpdateInterval = 5; // 5ms更新间隔

        #endregion

        #region 暂停相关

        private DateTime? _pauseStartTime;
        private double _totalPauseDuration;
        private double _originalRemainingTime;
        private DispatcherTimer _pauseAnimationTimer;
        private int? _currentKeyframeId;
        private DateTime? _currentFrameStartTime;

        #endregion

        #region 时间修正

        /// <summary>
        /// 是否启用手动修正
        /// </summary>
        public bool ManualCorrectionEnabled { get; set; } = true;

        private DateTime? _lastManualOperationTime;

        #endregion

        #region 事件

        /// <summary>
        /// 播放完成事件
        /// </summary>
        public event EventHandler PlayFinished;

        #endregion

        public AutoPlayer(MainWindow mainWindow, TimeRecorder timeRecorder, KeyframeManager keyframeManager)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _timeRecorder = timeRecorder ?? throw new ArgumentNullException(nameof(timeRecorder));
            _keyframeManager = keyframeManager ?? throw new ArgumentNullException(nameof(keyframeManager));

            InitializeTimers();
        }

        #region 初始化

        private void InitializeTimers()
        {
            // 播放定时器
            _playTimer = new DispatcherTimer();
            _playTimer.Tick += (s, e) =>
            {
                _playTimer.Stop();
                PlayNextFrame();
            };

            // 倒计时定时器
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_countdownUpdateInterval)
            };
            _countdownTimer.Tick += (s, e) => UpdateCountdown();

            // 暂停动画定时器
            _pauseAnimationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_countdownUpdateInterval)
            };
            _pauseAnimationTimer.Tick += (s, e) => UpdatePauseCountdownAnimation();
        }

        #endregion

        #region 自动播放主循环

        /// <summary>
        /// 开始自动播放
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否成功开始</returns>
        public async Task<bool> StartAutoPlayAsync(int imageId)
        {
            if (IsPlaying)
            {
                Console.WriteLine("⚠️ 已经在播放中");
                return false;
            }

            // 获取时间序列
            _timingSequence = await _timeRecorder.GetTimingSequenceAsync(imageId);
            if (_timingSequence == null || _timingSequence.Count == 0)
            {
                Console.WriteLine($"❌ 图片 {imageId} 没有时间序列数据");
                return false;
            }

            _currentImageId = imageId;
            IsPlaying = true;
            IsPaused = false;
            _currentSequenceIndex = 0;
            _loopCount = 0;
            CompletedPlayCount = 0;

            // 初始化时间修正相关变量
            _currentFrameStartTime = DateTime.Now;
            _lastManualOperationTime = null;

            Console.WriteLine($"🎬 开始自动播放图片 {imageId}，共 {_timingSequence.Count} 个关键帧");

            // 立即播放第一帧
            PlayNextFrame();
            return true;
        }

        /// <summary>
        /// 停止自动播放
        /// </summary>
        /// <returns>是否成功停止</returns>
        public bool StopAutoPlay()
        {
            if (!IsPlaying)
            {
                Console.WriteLine("⚠️ 当前没有在播放");
                return false;
            }

            IsPlaying = false;
            IsPaused = false;

            // 取消定时器
            _playTimer?.Stop();
            StopCountdownTimer();

            Console.WriteLine($"⏹️ 停止自动播放");

            // 调用播放结束事件
            PlayFinished?.Invoke(this, EventArgs.Empty);

            // 重置状态
            _currentImageId = null;
            _timingSequence.Clear();
            _currentSequenceIndex = 0;

            return true;
        }

        /// <summary>
        /// 播放下一帧（核心循环）
        /// </summary>
        private void PlayNextFrame()
        {
            if (!IsPlaying || IsPaused)
                return;

            if (_timingSequence == null || _timingSequence.Count == 0)
            {
                StopAutoPlay();
                return;
            }

            // 检查是否需要循环或首次播放
            bool isDirectJump = false;

            // 首次播放检查
            if (_currentSequenceIndex == 0 && _loopCount == 0)
            {
                isDirectJump = true; // 首次播放直接跳转到第一帧
            }

            // 循环检查
            if (_currentSequenceIndex >= _timingSequence.Count)
            {
                // 一轮播放完成
                CompletedPlayCount++;

                // 播放次数判断逻辑
                bool shouldContinue = false;
                string stopReason = "";

                if (TargetPlayCount == -1) // 无限循环
                {
                    shouldContinue = true;
                }
                else if (CompletedPlayCount < TargetPlayCount) // 还没达到目标次数
                {
                    shouldContinue = true;
                }
                else // 已达到或超过目标次数
                {
                    stopReason = $"已完成{CompletedPlayCount}次播放，达到目标{TargetPlayCount}次";
                }

                if (shouldContinue)
                {
                    _currentSequenceIndex = 0;
                    _loopCount++;
                    isDirectJump = true; // 循环回第一帧时直接跳转
                    
                    var remaining = TargetPlayCount == -1 ? "∞" : (TargetPlayCount - CompletedPlayCount).ToString();
                    Console.WriteLine($"🔄 完成第{CompletedPlayCount}次播放，开始第{CompletedPlayCount + 1}次播放（剩余：{remaining}次）");
                }
                else
                {
                    Console.WriteLine($"✅ 播放完成：{stopReason}");
                    StopAutoPlay();
                    return;
                }
            }

            // 获取当前关键帧信息
            var timing = _timingSequence[_currentSequenceIndex];
            var keyframeId = timing.KeyframeId;
            var duration = timing.Duration;

            // 记录当前关键帧ID（用于暂停时间累加）
            _currentKeyframeId = keyframeId;

            // 重置当前帧的暂停时间累计
            _totalPauseDuration = 0.0;

            // 跳转到关键帧
            JumpToKeyframe(_currentSequenceIndex, isDirectJump);

            // 记录当前帧开始时间（用于手动修正）
            _currentFrameStartTime = DateTime.Now;

            // 计算调整后的等待时间
            double adjustedDuration = duration / PlaySpeed;

            Console.WriteLine($"▶️ 播放第 {_currentSequenceIndex + 1}/{_timingSequence.Count} 帧，等待 {adjustedDuration:F2}秒");

            // 准备播放下一帧
            _currentSequenceIndex++;

            // 安排下一帧播放
            ScheduleNextFrame(adjustedDuration);
        }

        /// <summary>
        /// 跳转到指定序列的关键帧
        /// </summary>
        /// <param name="sequenceIndex">序列索引</param>
        /// <param name="useDirectJump">是否使用直接跳转</param>
        private async void JumpToKeyframe(int sequenceIndex, bool useDirectJump = false)
        {
            try
            {
                if (sequenceIndex >= _timingSequence.Count)
                    return;

                // 获取关键帧信息
                var timing = _timingSequence[sequenceIndex];
                var keyframeId = timing.KeyframeId;
                var duration = timing.Duration;

                // 获取关键帧位置
                var keyframes = await _keyframeManager.GetKeyframesAsync(_currentImageId.Value);
                var targetKeyframe = keyframes.FirstOrDefault(k => k.Id == keyframeId);
                
                if (targetKeyframe == null)
                {
                    Console.WriteLine($"❌ 未找到关键帧 ID={keyframeId}");
                    return;
                }

                var targetIndex = keyframes.IndexOf(targetKeyframe);
                var targetPosition = targetKeyframe.Position;

                // 检测是否是回跳
                if (_keyframeManager.IsBackwardJump(targetIndex))
                {
                    useDirectJump = true;
                    Console.WriteLine("🔙 自动播放检测到回跳，强制使用直接跳转");
                }

                // 智能判断：如果录制的停留时间小于滚动动画时间，则使用直接跳转
                var scrollDuration = _keyframeManager.ScrollDuration;
                var adjustedDuration = duration / PlaySpeed;

                if (adjustedDuration < scrollDuration)
                {
                    useDirectJump = true;
                }

                // 更新关键帧索引
                _keyframeManager.UpdateKeyframeIndex(targetIndex);

                // 执行跳转
                if (useDirectJump || _keyframeManager.ScrollDuration == 0)
                {
                    // 直接跳转
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        var scrollViewer = _mainWindow.ImageScrollViewer;
                        var targetOffset = targetPosition * scrollViewer.ScrollableHeight;
                        scrollViewer.ScrollToVerticalOffset(targetOffset);

                        if (_mainWindow.IsProjectionEnabled)
                        {
                            _mainWindow.UpdateProjection();
                        }
                    });
                }
                else
                {
                    // 平滑滚动
                    _keyframeManager.SmoothScrollTo(targetPosition);
                }

                // 更新UI指示器
                await _keyframeManager.UpdateKeyframeIndicatorsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 跳转到关键帧异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 安排下一帧播放
        /// </summary>
        /// <param name="delaySeconds">延时秒数</param>
        private void ScheduleNextFrame(double delaySeconds)
        {
            if (!IsPlaying || IsPaused)
                return;

            // 启动倒计时显示
            StartCountdownTimer(delaySeconds);

            // 转换为毫秒
            int delayMs = (int)(delaySeconds * 1000);

            // 安排下一帧播放
            _playTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
            _playTimer.Start();
        }

        #endregion

        #region 暂停/继续

        /// <summary>
        /// 暂停自动播放
        /// </summary>
        /// <returns>是否成功暂停</returns>
        public bool PauseAutoPlay()
        {
            if (!IsPlaying || IsPaused)
                return false;

            IsPaused = true;

            // 记录暂停开始时间
            _pauseStartTime = DateTime.Now;

            // 记录暂停时的原始剩余时间
            if (_nextFrameTime.HasValue)
            {
                _originalRemainingTime = (_nextFrameTime.Value - _pauseStartTime.Value).TotalSeconds;
            }
            else
            {
                _originalRemainingTime = 0.0;
            }

            // 取消当前定时器
            _playTimer?.Stop();

            // 取消正常倒计时，开始暂停期间的倒计时增加动画
            _countdownTimer?.Stop();

            // 开始暂停期间的倒计时增加动画
            StartPauseCountdownAnimation();

            Console.WriteLine("⏸️ 暂停自动播放");
            return true;
        }

        /// <summary>
        /// 恢复自动播放
        /// </summary>
        /// <returns>是否成功恢复</returns>
        public async Task<bool> ResumeAutoPlayAsync()
        {
            if (!IsPlaying || !IsPaused)
                return false;

            IsPaused = false;
            var currentTime = DateTime.Now;

            // 停止暂停期间的倒计时增加动画
            _pauseAnimationTimer?.Stop();

            // 计算本次暂停的时长和最终时间
            if (_pauseStartTime.HasValue && _currentKeyframeId.HasValue && _currentFrameStartTime.HasValue)
            {
                var pauseDuration = (currentTime - _pauseStartTime.Value).TotalSeconds;
                _totalPauseDuration += pauseDuration;

                // 计算从帧开始到暂停时的已播放时间
                var playedDuration = (_pauseStartTime.Value - _currentFrameStartTime.Value).TotalSeconds;

                // 正确的最终时间 = 已播放时间 + 总暂停时间
                var finalDisplayTime = playedDuration + _totalPauseDuration;

                // 异步更新数据库中的时间记录
                _ = Task.Run(async () =>
                {
                    if (await _timeRecorder.UpdateKeyframeTimingInDbAsync(
                        _currentImageId.Value, _currentKeyframeId.Value, finalDisplayTime))
                    {
                        // 重新从数据库加载时间序列
                        _timingSequence = await _timeRecorder.GetTimingSequenceAsync(_currentImageId.Value);
                        
                        Console.WriteLine($"⏱️ 暂停时间累加：关键帧 {_currentKeyframeId} 时间调整为 {finalDisplayTime:F2}秒");
                        Console.WriteLine($"  - 已播放时间: {playedDuration:F2}秒");
                        Console.WriteLine($"  - 暂停时剩余时间: {_originalRemainingTime:F2}秒");
                        Console.WriteLine($"  - 累计暂停时间: {_totalPauseDuration:F2}秒");
                    }
                });

                _pauseStartTime = null;
            }

            // 点击继续后立即跳转到下一帧，不再等待倒计时
            Console.WriteLine("▶️ 继续播放：立即跳转到下一帧");
            PlayNextFrame();

            // 重置当前帧开始时间
            _currentFrameStartTime = DateTime.Now;

            return true;
        }

        #endregion

        #region 倒计时

        /// <summary>
        /// 开始倒计时定时器
        /// </summary>
        /// <param name="durationSeconds">倒计时持续时间（秒）</param>
        private void StartCountdownTimer(double durationSeconds)
        {
            if (!CountdownEnabled)
                return;

            // 记录下一帧的预定执行时间
            _nextFrameTime = DateTime.Now.AddSeconds(durationSeconds);
            _currentFrameDuration = durationSeconds;

            // 开始倒计时更新
            _countdownTimer.Start();
        }

        /// <summary>
        /// 停止倒计时定时器
        /// </summary>
        private void StopCountdownTimer()
        {
            _countdownTimer?.Stop();
            
            // 重置倒计时显示为默认状态
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.UpdateCountdownDisplay(-1);
            });
        }

        /// <summary>
        /// 更新倒计时显示
        /// </summary>
        private void UpdateCountdown()
        {
            if (!IsPlaying || IsPaused || !_nextFrameTime.HasValue)
                return;

            var currentTime = DateTime.Now;
            var remainingTime = (_nextFrameTime.Value - currentTime).TotalSeconds;

            // 更新倒计时显示
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.UpdateCountdownDisplay(Math.Max(0, remainingTime));
            });

            // 如果还有剩余时间，继续更新
            if (remainingTime > 0)
            {
                // 动态调整更新间隔：剩余时间少时更频繁更新
                int updateInterval = _countdownUpdateInterval;
                if (remainingTime < 0.5) // 最后500ms使用更高频率
                {
                    updateInterval = 10; // 10ms更新，确保精确显示
                    _countdownTimer.Interval = TimeSpan.FromMilliseconds(updateInterval);
                }
            }
            else
            {
                // 倒计时结束，清理
                _countdownTimer.Stop();
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _mainWindow.UpdateCountdownDisplay(0.0);
                });
            }
        }

        /// <summary>
        /// 开始暂停期间的倒计时增加动画
        /// </summary>
        private void StartPauseCountdownAnimation()
        {
            if (!IsPaused)
                return;

            _pauseAnimationTimer.Start();
        }

        /// <summary>
        /// 更新暂停期间的倒计时增加动画
        /// </summary>
        private void UpdatePauseCountdownAnimation()
        {
            if (!IsPaused || !_pauseStartTime.HasValue)
                return;

            var currentTime = DateTime.Now;
            var currentPauseDuration = (currentTime - _pauseStartTime.Value).TotalSeconds;

            // 计算已播放时间（从帧开始到暂停时的时间）
            double playedTime = 0.0;
            if (_currentFrameStartTime.HasValue)
            {
                playedTime = (_pauseStartTime.Value - _currentFrameStartTime.Value).TotalSeconds;
            }

            // 使用暂停时记录的原始剩余时间
            var remainingTime = _originalRemainingTime;

            // 更新倒计时显示（三参数显示）
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.UpdatePauseCountdownDisplay(currentPauseDuration, remainingTime, playedTime);
            });
        }

        #endregion

        #region 手动操作修正

        /// <summary>
        /// 记录手动操作，用于实时修正播放时间
        /// </summary>
        /// <param name="keyframeId">当前关键帧ID</param>
        /// <returns>是否成功记录</returns>
        public async Task<bool> RecordManualOperationAsync(int keyframeId)
        {
            if (!IsPlaying || !ManualCorrectionEnabled || !_currentImageId.HasValue)
                return false;

            var currentTime = DateTime.Now;

            // 如果有上次手动操作时间，计算实际停留时间
            if (_currentFrameStartTime.HasValue)
            {
                var actualDuration = (currentTime - _currentFrameStartTime.Value).TotalSeconds;

                // 异步更新数据库中的时间记录，避免阻塞UI
                _ = Task.Run(async () =>
                {
                    if (await _timeRecorder.UpdateKeyframeTimingInDbAsync(
                        _currentImageId.Value, keyframeId, actualDuration))
                    {
                        Console.WriteLine($"⏱️ 播放时实时修正：关键帧 {keyframeId} 时间修正为 {actualDuration:F2}秒");
                    }
                });
            }

            // 记录当前操作时间，作为下一帧的开始时间
            _currentFrameStartTime = currentTime;
            _lastManualOperationTime = currentTime;

            return true;
        }

        #endregion

        #region 播放控制方法

        /// <summary>
        /// 设置循环模式
        /// </summary>
        /// <param name="enabled">是否启用循环</param>
        public void SetLoopMode(bool enabled)
        {
            LoopEnabled = enabled;
            Console.WriteLine($"🔁 循环模式: {(enabled ? "启用" : "禁用")}");
        }

        /// <summary>
        /// 设置播放速度
        /// </summary>
        /// <param name="speed">播放速度倍率（0.1-5.0）</param>
        public void SetPlaySpeed(double speed)
        {
            if (speed <= 0)
                speed = 1.0;

            PlaySpeed = Math.Max(0.1, Math.Min(5.0, speed));
            Console.WriteLine($"⚡ 播放速度设置为: {PlaySpeed}x");
        }

        /// <summary>
        /// 获取播放状态信息
        /// </summary>
        /// <returns>播放状态信息</returns>
        public Dictionary<string, object> GetPlayStatus()
        {
            return new Dictionary<string, object>
            {
                { "IsPlaying", IsPlaying },
                { "IsPaused", IsPaused },
                { "LoopEnabled", LoopEnabled },
                { "PlaySpeed", PlaySpeed },
                { "CurrentFrame", _currentSequenceIndex },
                { "TotalFrames", _timingSequence.Count },
                { "LoopCount", _loopCount },
                { "CompletedPlayCount", CompletedPlayCount },
                { "TargetPlayCount", TargetPlayCount }
            };
        }

        #endregion
    }
}

