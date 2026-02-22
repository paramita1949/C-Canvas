using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.DTOs;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Repositories.Interfaces;
using ImageColorChanger.Services.Algorithms;
using ImageColorChanger.Services.Interfaces;
using ImageColorChanger.Utils;

namespace ImageColorChanger.Services.Implementations
{
    /// <summary>
    /// 关键帧播放服务
    /// 参考Python版本：LOGIC_ANALYSIS_03 行219-481
    /// </summary>
    public class KeyframePlaybackService : IPlaybackService
    {
        private readonly ITimingRepository _timingRepository;
        private readonly Stopwatch _stopwatch;
        private CancellationTokenSource _cancellationTokenSource;

        private int _currentImageId;
        private System.Collections.Generic.List<TimingSequenceDto> _timingSequence;
        private int _actualKeyframeCount; // 实际关键帧数量（用于循环判断，避免错误数据干扰）
        private int _currentIndex;
        private bool _isPaused;
        
        // 播放时间修正相关属性（参考Python版本：keytime.py 第654-657行）
        private bool _manualCorrectionEnabled = true; // 是否启用手动修正
        private DateTime? _currentFrameStartTime; // 当前帧开始时间
        private DateTime? _lastManualOperationTime; // 上次手动操作时间
        
        // 暂停时间累加相关属性（参考Python版本：keytime.py 第667-672行）
        private DateTime? _pauseStartTime; // 暂停开始时间（绝对时间戳）
        private double _totalPauseDuration; // 当前帧累计暂停时间
        private int? _currentKeyframeId; // 当前关键帧ID（用于时间修正）
        private int? _currentSequenceOrder; // 当前SequenceOrder（用于精确定位记录，支持跳帧录制）
        
        // 手动跳转标志：用于在播放时立即跳过当前等待（参考Python版本：keyframe_navigation.py 第157-167行）
        private bool _skipCurrentWait = false;
        private bool _justResumed = false; // 标记是否刚从暂停恢复

        /// <summary>
        /// 当前播放模式
        /// </summary>
        public PlaybackMode Mode => PlaybackMode.Keyframe;

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// 是否已暂停
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// 播放次数设置（-1表示无限循环）
        /// </summary>
        public int PlayCount { get; set; } = -1;

        /// <summary>
        /// 已完成播放次数
        /// </summary>
        public int CompletedPlayCount { get; private set; }

        /// <summary>
        /// 播放进度更新事件
        /// </summary>
        public event EventHandler<PlaybackProgressEventArgs> ProgressUpdated;

        /// <summary>
        /// 播放完成事件
        /// </summary>
        public event EventHandler PlaybackCompleted;

        /// <summary>
        /// 请求跳转到关键帧事件
        /// </summary>
        public event EventHandler<JumpToKeyframeEventArgs> JumpToKeyframeRequested;

        public KeyframePlaybackService(ITimingRepository timingRepository)
        {
            _timingRepository = timingRepository ?? throw new ArgumentNullException(nameof(timingRepository));
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// 开始播放
        /// </summary>
        public async Task StartPlaybackAsync(int imageId, CancellationToken cancellationToken = default)
        {
            if (IsPlaying)
            {
                return;
            }

            // 加载时间序列
            _timingSequence = await _timingRepository.GetTimingSequenceAsync(imageId);
            if (_timingSequence == null || !_timingSequence.Any())
            {
                //System.Diagnostics.Debug.WriteLine($" [开始播放] 没有时间序列数据，无法播放");
                return;
            }

            _currentImageId = imageId;
            _currentIndex = 0;
            CompletedPlayCount = 0;
            _isPaused = false;
            
            //  初始化时间修正相关变量（参考Python版本：keytime.py 第701-703行）
            // 注意：_currentFrameStartTime 会在 PlayNextFrameAsync 中为每一帧单独设置
            _currentFrameStartTime = null;
            _lastManualOperationTime = null;

            IsPlaying = true;
            _cancellationTokenSource = new CancellationTokenSource();

            //  计算实际关键帧数量（去重KeyframeId）- 仅用于统计信息
            _actualKeyframeCount = _timingSequence.Select(t => t.KeyframeId).Distinct().Count();
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($" [开始播放] 图片ID: {imageId}, Timing记录: {_timingSequence.Count}条, 实际关键帧: {_actualKeyframeCount}个, 播放次数: {PlayCount}");
            System.Diagnostics.Debug.WriteLine($" [加载时间序列] 初始值:");
            for (int i = 0; i < _timingSequence.Count; i++)
            {
                var t = _timingSequence[i];
                System.Diagnostics.Debug.WriteLine($"   #{i + 1}: KeyframeId={t.KeyframeId}, Duration={t.Duration:F2}秒, Order={t.SequenceOrder}");
            }
            
            if (_timingSequence.Count != _actualKeyframeCount)
            {
                System.Diagnostics.Debug.WriteLine($" [数据警告] Timing记录({_timingSequence.Count})与实际关键帧({_actualKeyframeCount})数量不一致！可能是跳帧录制");
            }
            #endif
            
            // 启动播放循环
            _ = Task.Run(() => PlaybackLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 播放循环（核心逻辑）
        /// </summary>
        private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (IsPlaying && !cancellationToken.IsCancellationRequested)
                {
                    // 判断是否应该继续播放
                    if (!PlayCountJudge.ShouldContinue(PlayCount, CompletedPlayCount))
                    {
                        //System.Diagnostics.Debug.WriteLine($" [播放] 播放完成: 已播放{CompletedPlayCount}轮，设定{PlayCount}轮");
                        break;
                    }

                    // 播放下一帧
                    await PlayNextFrameAsync(cancellationToken);

                    //  检查是否到达最后一帧（使用完整时间序列长度，支持跳帧录制）
                    // 修复：之前使用去重后的关键帧数量会导致跳帧录制时顺序错乱
                    if (_currentIndex >= _timingSequence.Count)
                    {
                        // 完成一轮播放
                        CompletedPlayCount++;
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($" [播放] 完成第{CompletedPlayCount}轮播放（播放了{_timingSequence.Count}个时间记录）");
                        #endif

                        // 回到第一帧索引
                        _currentIndex = 0;

                        //  关键修复：如果这是最后一轮，需要跳转回第一帧（参考Python版本：keytime.py 第1160-1168行）
                        if (!PlayCountJudge.ShouldContinue(PlayCount, CompletedPlayCount))
                        {
                            //System.Diagnostics.Debug.WriteLine($" [播放] 最后一轮结束，跳转回第一帧");
                            
                            // 跳转回第一帧
                            var firstTiming = _timingSequence[0];
                            JumpToKeyframeRequested?.Invoke(this, new JumpToKeyframeEventArgs
                            {
                                KeyframeId = firstTiming.KeyframeId,
                                Position = firstTiming.Position,
                                YPosition = firstTiming.YPosition,
                                UseDirectJump = true  // 最后跳转使用直接跳转
                            });
                            
                            break;
                        }
                    }
                }

                // 播放结束
                await StopPlaybackAsync();
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 播放下一帧
        /// 参考Python版本：keytime.py 第1077-1153行
        /// </summary>
        private async Task PlayNextFrameAsync(CancellationToken cancellationToken)
        {
            if (_currentIndex >= _timingSequence.Count)
            {
                //System.Diagnostics.Debug.WriteLine($" [播放] 索引越界: {_currentIndex} >= {_timingSequence.Count}");
                return;
            }

            var currentTiming = _timingSequence[_currentIndex];
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($" [播放] 当前帧数据: 索引={_currentIndex}, KeyframeId={currentTiming.KeyframeId}, Duration={currentTiming.Duration:F2}秒, SequenceOrder={currentTiming.SequenceOrder}");
            #endif

            // 设置当前关键帧ID和重置暂停累计时间（参考Python版本：keytime.py 第1174-1175行）
            _currentKeyframeId = currentTiming.KeyframeId;
            _currentSequenceOrder = currentTiming.SequenceOrder; // 记录当前SequenceOrder，用于精确更新
            _totalPauseDuration = 0.0;
            
            //  关键修复：记录当前帧的开始时间（用于手动跳转时计算实际停留时间）
            _currentFrameStartTime = DateTime.Now;
            //System.Diagnostics.Debug.WriteLine($" [播放] 记录第{_currentIndex + 1}帧开始时间: {_currentFrameStartTime.Value:HH:mm:ss.fff}");

            //  判断是否应该直接跳转（参考Python版本：keytime.py 第1089-1112行）
            bool useDirectJump = false;
            
            // 1. 首次播放：直接跳转到第一帧
            if (_currentIndex == 0 && CompletedPlayCount == 0)
            {
                useDirectJump = true;
                //System.Diagnostics.Debug.WriteLine(" [播放] 首次播放，直接跳转到第一帧");
            }
            
            // 2. 循环回第一帧：直接跳转（最关键的修复）
            if (_currentIndex == 0 && CompletedPlayCount > 0)
            {
                useDirectJump = true;
                //System.Diagnostics.Debug.WriteLine($" [播放] 循环回第一帧（第{CompletedPlayCount + 1}轮），直接跳转");
            }

            // 3. 任意回跳：如果当前帧位置小于上一帧，强制直接跳转（不参与滚动函数）
            // 典型场景：跳帧录制形成的时序如 12->13->14->13->14->15
            if (!useDirectJump && _currentIndex > 0)
            {
                var previousTiming = _timingSequence[_currentIndex - 1];
                bool isBackwardJump = currentTiming.YPosition < previousTiming.YPosition ||
                                      currentTiming.Position < previousTiming.Position;
                if (isBackwardJump)
                {
                    useDirectJump = true;
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"⬅ [播放] 检测到回跳（索引{_currentIndex - 1}->{_currentIndex}, " +
                        $"Keyframe {previousTiming.KeyframeId}->{currentTiming.KeyframeId}），强制直接跳转");
                    #endif
                }
            }

            // 跳转到关键帧
            //System.Diagnostics.Debug.WriteLine($" [播放跳转] UseDirectJump={useDirectJump}, 索引={_currentIndex}, KeyframeId={currentTiming.KeyframeId}, 完成轮数={CompletedPlayCount}");
            JumpToKeyframeRequested?.Invoke(this, new JumpToKeyframeEventArgs
            {
                KeyframeId = currentTiming.KeyframeId,
                Position = currentTiming.Position,
                YPosition = currentTiming.YPosition,
                UseDirectJump = useDirectJump
            });

            // 触发进度更新（包含倒计时时长）
            //System.Diagnostics.Debug.WriteLine($"⏱ [播放] 开始倒计时: {currentTiming.Duration:F1}秒 (第{_currentIndex + 1}/{_timingSequence.Count}帧)");
            ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
            {
                CurrentIndex = _currentIndex,
                TotalCount = _timingSequence.Count,
                RemainingTime = currentTiming.Duration,
                CurrentItemId = currentTiming.KeyframeId
            });

            // 等待指定时长
            _stopwatch.Restart();
            var targetDuration = currentTiming.Duration;

            while (_stopwatch.Elapsed.TotalSeconds < targetDuration)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                // 检查是否需要跳过当前等待（手动跳转时触发）
                if (_skipCurrentWait)
                {
                    //System.Diagnostics.Debug.WriteLine(" [手动跳转] 跳过当前等待，立即进入下一帧");
                    _skipCurrentWait = false; // 重置标志
                    break; // 立即退出等待循环
                }

                // 如果暂停，等待恢复
                while (_isPaused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(50, cancellationToken);
                }
                
                // 恢复后立即退出等待循环，进入下一帧（参考Python版本：第1019-1020行）
                if (_justResumed)
                {
                    //System.Diagnostics.Debug.WriteLine(" [恢复] 检测到刚从暂停恢复，立即进入下一帧");
                    _justResumed = false; // 重置标志
                    break;
                }

                await Task.Delay(10, cancellationToken); // 10ms精度
            }

            _currentIndex++;
        }

        /// <summary>
        /// 暂停播放（参考Python版本：keytime.py 第917-956行）
        /// </summary>
        public Task PausePlaybackAsync()
        {
            if (!IsPlaying || _isPaused)
                return Task.CompletedTask;

            _isPaused = true;
            
            // 记录暂停开始时间（使用绝对时间戳）
            _pauseStartTime = DateTime.Now;

            //System.Diagnostics.Debug.WriteLine($" [暂停播放] 当前位置: {_currentIndex}/{_timingSequence.Count}, 当前关键帧ID: {_currentKeyframeId}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 继续播放（参考Python版本：keytime.py 第958-1025行）
        /// </summary>
        public Task ResumePlaybackAsync()
        {
            if (!IsPlaying || !_isPaused)
                return Task.CompletedTask;

            var currentTime = DateTime.Now;
            
            // 计算本次暂停的时长和最终时间
            if (_pauseStartTime.HasValue && _currentKeyframeId.HasValue && _currentFrameStartTime.HasValue)
            {
                // 计算本次暂停时长
                var pauseDuration = (currentTime - _pauseStartTime.Value).TotalSeconds;
                _totalPauseDuration += pauseDuration;
                
                // 计算从帧开始到暂停时的已播放时间
                var playedDuration = (_pauseStartTime.Value - _currentFrameStartTime.Value).TotalSeconds;
                
                // 正确的最终时间 = 已播放时间 + 总暂停时间
                var finalDisplayTime = playedDuration + _totalPauseDuration;
                
                // 找到当前关键帧的原始时间（用于日志显示）
                var currentTiming = _timingSequence.FirstOrDefault(t => t.KeyframeId == _currentKeyframeId.Value);
                var originalDuration = currentTiming?.Duration ?? 0;
                
                // 异步更新数据库中的时间记录，使用用户看到的最终时间
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 修复：使用ImageId和SequenceOrder精确定位，支持跳帧录制
                        if (_currentSequenceOrder.HasValue)
                        {
                            await _timingRepository.UpdateDurationAsync(_currentImageId, _currentSequenceOrder.Value, finalDisplayTime);
                            
                            // 更新内存中的时间序列
                            if (currentTiming != null)
                            {
                                currentTiming.Duration = finalDisplayTime;
                            }
                        }
                        
                        // 重新从数据库加载完整的时间序列，确保所有帧间时间间隔都是最新的
                        var newTimingSequence = await _timingRepository.GetTimingSequenceAsync(_currentImageId);
                        if (newTimingSequence != null && newTimingSequence.Any())
                        {
                            _timingSequence = newTimingSequence.ToList();
                            //System.Diagnostics.Debug.WriteLine($" [暂停] 已重新加载时间序列，共 {_timingSequence.Count} 个关键帧");
                        }
                        
                        //System.Diagnostics.Debug.WriteLine($" [暂停] 时间累加：关键帧 {_currentKeyframeId} 时间从 {originalDuration:F2}秒 调整为 {finalDisplayTime:F2}秒");
                        //System.Diagnostics.Debug.WriteLine($"  - 已播放时间: {playedDuration:F2}秒");
                        //System.Diagnostics.Debug.WriteLine($"  - 本次暂停时长: {pauseDuration:F2}秒");
                        //System.Diagnostics.Debug.WriteLine($"  - 累计暂停时间: {_totalPauseDuration:F2}秒");
                        //System.Diagnostics.Debug.WriteLine($"  - 最终时间: {finalDisplayTime:F2}秒");
                    }
                    catch (Exception)
                    {
                    }
                });
            }
            
            _isPaused = false;
            _pauseStartTime = null;
            _justResumed = true; // 设置恢复标志
            
            // 重置当前帧开始时间（参考Python版本：第1023行）
            _currentFrameStartTime = DateTime.Now;

            //System.Diagnostics.Debug.WriteLine($" [恢复播放] 继续播放，立即跳转到下一帧");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止播放
        /// </summary>
        public Task StopPlaybackAsync()
        {
            if (!IsPlaying)
                return Task.CompletedTask;

            _cancellationTokenSource?.Cancel();
            _stopwatch.Stop();
            IsPlaying = false;
            _isPaused = false;

            return Task.CompletedTask;
        }
        
        /// <summary>
        /// 手动跳转后，跳过当前等待并立即播放下一帧
        /// 参考Python版本：keyframe_navigation.py 第157-167行
        /// </summary>
        public void SkipCurrentWaitAndPlayNext()
        {
            if (!IsPlaying)
            {
                return;
            }

            //System.Diagnostics.Debug.WriteLine(" [手动跳转] 设置跳过当前等待标志");
            _skipCurrentWait = true;
        }

        /// <summary>
        /// 记录手动操作，用于实时修正播放时间
        /// 参考Python版本：keytime.py 第750-786行
        /// </summary>
        /// <param name="keyframeId">当前关键帧ID</param>
        /// <returns>是否成功记录</returns>
        public Task<bool> RecordManualOperationAsync(int keyframeId)
        {
            if (!IsPlaying || !_manualCorrectionEnabled)
            {
                //System.Diagnostics.Debug.WriteLine(" [手动修正] 播放未运行或手动修正未启用");
                return Task.FromResult(false);
            }

            var currentTime = DateTime.Now;

            // 如果有上次手动操作时间，计算实际停留时间
            if (_currentFrameStartTime.HasValue)
            {
                var actualDuration = (currentTime - _currentFrameStartTime.Value).TotalSeconds;
                
                //System.Diagnostics.Debug.WriteLine($" [手动修正] 关键帧 {keyframeId}");
                //System.Diagnostics.Debug.WriteLine($"   开始时间: {_currentFrameStartTime.Value:HH:mm:ss.fff}");
                //System.Diagnostics.Debug.WriteLine($"   结束时间: {currentTime:HH:mm:ss.fff}");
                //System.Diagnostics.Debug.WriteLine($"   实际停留: {actualDuration:F2}秒");

                // 异步更新数据库中的时间记录，避免阻塞UI（参考Python版本：keytime.py 第769-780行）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 修复：使用当前SequenceOrder精确定位，支持跳帧录制
                        if (_currentSequenceOrder.HasValue)
                        {
                            var timing = _timingSequence.FirstOrDefault(t => t.SequenceOrder == _currentSequenceOrder.Value);
                            if (timing != null)
                            {
                                var oldDuration = timing.Duration;
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($" [数据库更新前] ImageId={_currentImageId}, SequenceOrder={_currentSequenceOrder.Value}, KeyframeId={keyframeId}: 旧值={oldDuration:F2}秒 → 新值={actualDuration:F2}秒");
                                #endif
                                
                                await _timingRepository.UpdateDurationAsync(_currentImageId, _currentSequenceOrder.Value, actualDuration);
                                
                                // 更新内存中的时间序列
                                timing.Duration = actualDuration;
                                
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($" [数据库更新完成] ImageId={_currentImageId}, SequenceOrder={_currentSequenceOrder.Value} 时间修正为 {actualDuration:F2}秒");
                                #endif
                            }
                            else
                            {
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($" [手动修正] 找不到 SequenceOrder={_currentSequenceOrder.Value} 的Timing记录");
                                #endif
                            }
                        }
                        else
                        {
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($" [手动修正] 当前SequenceOrder为空，无法更新");
                            #endif
                        }
                    }
                    catch (Exception)
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($" [手动修正异常] ImageId={_currentImageId}, SequenceOrder={_currentSequenceOrder?.ToString() ?? "null"}");
                        #endif
                    }
                });
            }
            else
            {
                //System.Diagnostics.Debug.WriteLine($" [手动修正] 关键帧 {keyframeId}: 没有开始时间记录");
            }

            // 记录当前操作时间，作为下一帧的开始时间
            _currentFrameStartTime = currentTime;
            _lastManualOperationTime = currentTime;

            //System.Diagnostics.Debug.WriteLine($" [手动修正] 记录新的帧开始时间: {currentTime:HH:mm:ss.fff}");
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 跳转到关键帧事件参数
    /// </summary>
    public class JumpToKeyframeEventArgs : EventArgs
    {
        public int KeyframeId { get; set; }
        public double Position { get; set; }
        public int YPosition { get; set; }
        
        /// <summary>
        /// 是否使用直接跳转（不使用滚动动画）
        /// 参考Python版本：keytime.py 第1112行，循环回第一帧时直接跳转
        /// </summary>
        public bool UseDirectJump { get; set; }
    }
}



