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

namespace ImageColorChanger.Services.Implementations
{
    /// <summary>
    /// 原图模式播放服务
    /// 参考Python版本：LOGIC_ANALYSIS_04 行325-609
    /// </summary>
    public class OriginalPlaybackService : IPlaybackService
    {
        private readonly IOriginalModeRepository _originalModeRepository;
        private readonly Stopwatch _stopwatch;
        private CancellationTokenSource _cancellationTokenSource;

        private int _currentBaseImageId;
        private System.Collections.Generic.List<OriginalTimingSequenceDto> _timingSequence;
        private int _currentIndex;
        private double _pauseStartTime;
        private DateTime _pauseStartRealTime;  // 暂停开始的真实时间（用于计算暂停时长）
        private bool _isPaused;
        private bool _skipToNextFrame;  // 是否立即跳到下一帧（用于暂停后继续播放）
        
        // 暂停时间累加相关字段
        private int _currentSimilarImageId;  // 当前播放的相似图片ID
        private double _totalPauseDuration;  // 总暂停时长
        private DateTime _currentFrameStartTime;  // 当前帧开始时间
        
        // 手动跳转时间修正相关字段
        private bool _manualCorrectionEnabled = true;  // 是否启用手动修正
        private DateTime? _lastManualOperationTime;  // 上次手动操作时间

        /// <summary>
        /// 当前播放模式
        /// </summary>
        public PlaybackMode Mode => PlaybackMode.Original;

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
        /// 请求切换图片事件
        /// </summary>
        public event EventHandler<SwitchImageEventArgs> SwitchImageRequested;

        public OriginalPlaybackService(IOriginalModeRepository originalModeRepository)
        {
            _originalModeRepository = originalModeRepository ?? throw new ArgumentNullException(nameof(originalModeRepository));
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// 开始播放
        /// </summary>
        public async Task StartPlaybackAsync(int imageId, CancellationToken cancellationToken = default)
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"\n🎬 [原图播放] ========== StartPlaybackAsync 被调用 ==========");
            System.Diagnostics.Debug.WriteLine($"   ImageId: {imageId}");
            System.Diagnostics.Debug.WriteLine($"   当前 IsPlaying: {IsPlaying}");
            #endif

            if (IsPlaying)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [原图播放] 已在播放中，忽略启动请求");
                #endif
                return;
            }

            // 加载时间序列
            _timingSequence = await _originalModeRepository.GetOriginalTimingSequenceAsync(imageId);
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"📊 [原图播放] 时间序列加载完成");
            System.Diagnostics.Debug.WriteLine($"   序列数量: {_timingSequence?.Count ?? 0}");
            if (_timingSequence != null && _timingSequence.Any())
            {
                for (int i = 0; i < _timingSequence.Count; i++)
                {
                    var seq = _timingSequence[i];
                    System.Diagnostics.Debug.WriteLine($"   [{i}] FromId={seq.FromImageId}, ToId={seq.SimilarImageId}, Duration={seq.Duration:F2}s");
                }
            }
            #endif

            if (_timingSequence == null || !_timingSequence.Any())
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [原图播放] 时间序列为空，无法播放");
                #endif
                return;
            }

            _currentBaseImageId = imageId;
            _currentIndex = 0;
            CompletedPlayCount = 0;
            _isPaused = false;

            IsPlaying = true;
            _cancellationTokenSource = new CancellationTokenSource();

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [原图播放] 播放状态初始化完成");
            System.Diagnostics.Debug.WriteLine($"   _currentBaseImageId: {_currentBaseImageId}");
            System.Diagnostics.Debug.WriteLine($"   _currentIndex: {_currentIndex}");
            System.Diagnostics.Debug.WriteLine($"   PlayCount: {PlayCount}");
            System.Diagnostics.Debug.WriteLine($"   CompletedPlayCount: {CompletedPlayCount}");
            System.Diagnostics.Debug.WriteLine($"🚀 [原图播放] 启动播放循环任务...");
            #endif

            // 启动播放循环
            _ = Task.Run(() => PlaybackLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 播放循环（核心逻辑）
        /// 参考Python版本：keytime.py 行1708-1828
        /// </summary>
        private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"\n🔄 [原图播放] ========== PlaybackLoopAsync 开始 ==========");
                #endif

                int loopIteration = 0;
                
                while (IsPlaying && !cancellationToken.IsCancellationRequested)
                {
                    loopIteration++;
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"\n⏩ [原图播放] 循环迭代 #{loopIteration}");
                    System.Diagnostics.Debug.WriteLine($"   _currentIndex: {_currentIndex}/{_timingSequence.Count}");
                    System.Diagnostics.Debug.WriteLine($"   CompletedPlayCount: {CompletedPlayCount}");
                    System.Diagnostics.Debug.WriteLine($"   PlayCount: {PlayCount}");
                    System.Diagnostics.Debug.WriteLine($"   IsPlaying: {IsPlaying}");
                    System.Diagnostics.Debug.WriteLine($"   IsPaused: {_isPaused}");
                    #endif
                        
                    // 判断是否应该继续播放
                    bool shouldContinue = PlayCountJudge.ShouldContinue(PlayCount, CompletedPlayCount);
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"   ShouldContinue: {shouldContinue}");
                    #endif

                    if (!shouldContinue)
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🛑 [原图播放] 播放次数已达上限，退出循环");
                        #endif
                        break;
                    }                    
                    
                    // 播放下一帧
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"▶️ [原图播放] 调用 PlayNextFrameAsync...");
                    #endif
                    await PlayNextFrameAsync(cancellationToken);
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ [原图播放] PlayNextFrameAsync 返回");
                    System.Diagnostics.Debug.WriteLine($"   返回后 _currentIndex: {_currentIndex}");
                    #endif
                    
                    // 添加短暂延迟，避免死循环占用CPU
                    await Task.Delay(10, cancellationToken);
                }
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"\n🏁 [原图播放] 播放循环结束");
                System.Diagnostics.Debug.WriteLine($"   总迭代次数: {loopIteration}");
                System.Diagnostics.Debug.WriteLine($"   最终 _currentIndex: {_currentIndex}");
                System.Diagnostics.Debug.WriteLine($"   最终 CompletedPlayCount: {CompletedPlayCount}");
                #endif
                    
                // 播放结束
                await StopPlaybackAsync();
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [原图播放] 播放被取消");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [原图播放] 播放循环异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   StackTrace: {ex.StackTrace}");
                #endif
            }
        }

        /// <summary>
        /// 播放下一帧
        /// 参考Python版本：keytime.py 行1708-1828
        /// 🎯 修正：每次只处理一帧，返回到主循环
        /// </summary>
        private async Task PlayNextFrameAsync(CancellationToken cancellationToken)
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"\n🎞️ [原图播放] ========== PlayNextFrameAsync 开始 ==========");
            System.Diagnostics.Debug.WriteLine($"   进入时 _currentIndex: {_currentIndex}");
            System.Diagnostics.Debug.WriteLine($"   进入时 CompletedPlayCount: {CompletedPlayCount}");
            System.Diagnostics.Debug.WriteLine($"   序列总数: {_timingSequence.Count}");
            #endif
                
            if (_currentIndex >= _timingSequence.Count)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [原图播放] _currentIndex >= Count，返回");
                #endif
                return;
            }

            var currentTiming = _timingSequence[_currentIndex];
            var fromImageId = currentTiming.FromImageId;
            var toImageId = currentTiming.SimilarImageId;
            var duration = currentTiming.Duration;

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"📋 [原图播放] 当前帧信息");
            System.Diagnostics.Debug.WriteLine($"   索引: {_currentIndex}");
            System.Diagnostics.Debug.WriteLine($"   FromImageId: {fromImageId}");
            System.Diagnostics.Debug.WriteLine($"   ToImageId: {toImageId}");
            System.Diagnostics.Debug.WriteLine($"   Duration: {duration:F2}s");
            #endif
            // 记录当前帧信息
            _currentSimilarImageId = toImageId;
            _totalPauseDuration = 0.0;
            _currentFrameStartTime = DateTime.Now;

            // 🎯 第一帧特殊处理（参考Python: 行1750-1763）
            if (_currentIndex == 0 && CompletedPlayCount == 0)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🎬 [原图播放] 第一帧特殊处理 (首次播放)");
                System.Diagnostics.Debug.WriteLine($"   1️⃣ 切换到 FromImageId: {fromImageId}");
                #endif
                    
                // 首次播放：切到FromImageId，等待Duration，然后切到ToImageId
                SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
                {
                    ImageId = fromImageId,
                    ImagePath = null  // UI端会根据ImageId查询路径
                });
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   2️⃣ 触发 ProgressUpdated 事件");
                #endif

                ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
                {
                    CurrentIndex = _currentIndex,
                    TotalCount = _timingSequence.Count,
                    RemainingTime = duration,
                    CurrentItemId = toImageId
                });
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   3️⃣ 等待 Duration: {duration:F2}s");
                #endif
                await WaitForDurationAsync(duration, cancellationToken);
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   4️⃣ 切换到 ToImageId: {toImageId}");
                #endif
                
                SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
                {
                    ImageId = toImageId,
                    ImagePath = currentTiming.SimilarImagePath
                });
                
                _currentIndex++;
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ [原图播放] 第一帧处理完成");
                System.Diagnostics.Debug.WriteLine($"   _currentIndex 已递增为: {_currentIndex}");
                System.Diagnostics.Debug.WriteLine($"========== PlayNextFrameAsync 结束 (第一帧) ==========\n");
                #endif
                
                return;
            }

            // 🎯 最后一帧特殊处理（参考Python: 行1766-1817）
            if (_currentIndex == _timingSequence.Count - 1)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🏁 [原图播放] 最后一帧特殊处理");
                #endif
                    
                var firstImageId = _timingSequence[0].FromImageId;
                
                // 检查是否应该继续循环
                bool shouldContinue = PlayCountJudge.ShouldContinue(PlayCount, CompletedPlayCount + 1);
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   FirstImageId: {firstImageId}");
                System.Diagnostics.Debug.WriteLine($"   ToImageId: {toImageId}");
                System.Diagnostics.Debug.WriteLine($"   ShouldContinue: {shouldContinue} (PlayCount={PlayCount}, CompletedPlayCount+1={CompletedPlayCount + 1})");
                #endif
                
                if (shouldContinue)
                {
                    // 🎯 优化：如果最后一帧的ToImageId就是第一张图，跳过切换
                    if (toImageId == firstImageId)
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"   ♻️ 最后一帧ToImageId == FirstImageId，直接循环");
                        #endif

                        CompletedPlayCount++;
                        _currentIndex = 0;
                        
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"   CompletedPlayCount: {CompletedPlayCount}");
                        System.Diagnostics.Debug.WriteLine($"   _currentIndex 重置为: {_currentIndex}");
                        System.Diagnostics.Debug.WriteLine($"========== PlayNextFrameAsync 结束 (最后帧-直接循环) ==========\n");
                        #endif
                        
                        return; // 返回主循环，继续下一轮
                    }
                    else
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"   ♻️ 切换到 ToImageId 后循环");
                        #endif
                        
                        // 正常切换到ToImageId，然后开始新一轮
                        SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
                        {
                            ImageId = toImageId,
                            ImagePath = currentTiming.SimilarImagePath
                        });
                        
                        CompletedPlayCount++;
                        _currentIndex = 0;
                        
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"   CompletedPlayCount: {CompletedPlayCount}");
                        System.Diagnostics.Debug.WriteLine($"   _currentIndex 重置为: {_currentIndex}");
                        System.Diagnostics.Debug.WriteLine($"========== PlayNextFrameAsync 结束 (最后帧-切换后循环) ==========\n");
                        #endif
                        
                        return; // 返回主循环，继续下一轮
                    }
                }
                else
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"   ⏹️ 不需要循环，显示最后一帧然后结束");
                    #endif
                    
                    // 不需要循环，显示最后一帧然后结束
                    SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
                    {
                        ImageId = toImageId,
                        ImagePath = currentTiming.SimilarImagePath
                    });
                    CompletedPlayCount++;
                    _currentIndex = _timingSequence.Count; // 标记结束
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"   CompletedPlayCount: {CompletedPlayCount}");
                    System.Diagnostics.Debug.WriteLine($"   _currentIndex 设为 Count: {_currentIndex}");
                    System.Diagnostics.Debug.WriteLine($"========== PlayNextFrameAsync 结束 (最后帧-结束) ==========\n");
                    #endif
                    
                    return;
                }
            }

            // 🎯 普通帧处理（参考Python: 行1819-1828）
            // 当前已经在FromImageId上（上一帧切换过来的）
            // 显示进度，等待Duration，然后切到ToImageId
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"▶️ [原图播放] 普通帧处理");
            System.Diagnostics.Debug.WriteLine($"   1️⃣ 触发 ProgressUpdated 事件");
            #endif
            
            ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
            {
                CurrentIndex = _currentIndex,
                TotalCount = _timingSequence.Count,
                RemainingTime = duration,
                CurrentItemId = toImageId
            });
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   2️⃣ 等待 Duration: {duration:F2}s");
            #endif
            
            // 等待Duration
            await WaitForDurationAsync(duration, cancellationToken);
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   3️⃣ 切换到 ToImageId: {toImageId}");
            #endif
            
            // 切换到ToImageId
            SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
            {
                ImageId = toImageId,
                ImagePath = currentTiming.SimilarImagePath
            });
            
            _currentIndex++;
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [原图播放] 普通帧处理完成");
            System.Diagnostics.Debug.WriteLine($"   _currentIndex 已递增为: {_currentIndex}");
            System.Diagnostics.Debug.WriteLine($"========== PlayNextFrameAsync 结束 (普通帧) ==========\n");
            #endif
        }

        /// <summary>
        /// 等待指定时长
        /// </summary>
        private async Task WaitForDurationAsync(double duration, CancellationToken cancellationToken)
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"⏱️ [原图播放] WaitForDurationAsync 开始，目标时长: {duration:F2}s");
            #endif

            _stopwatch.Restart();

            while (_stopwatch.Elapsed.TotalSeconds < duration)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [原图播放] WaitForDurationAsync 被取消");
                    #endif
                    return;
                }

                if (_skipToNextFrame)
                {
                    _skipToNextFrame = false;
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⏭️ [原图播放] WaitForDurationAsync 跳过 (skipToNextFrame=true)");
                    #endif
                    break;
                }

                while (_isPaused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(50, cancellationToken);
                }

                await Task.Delay(10, cancellationToken);
            }

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [原图播放] WaitForDurationAsync 完成，实际等待: {_stopwatch.Elapsed.TotalSeconds:F2}s");
            #endif
        }

        /// <summary>
        /// 暂停播放
        /// </summary>
        public Task PausePlaybackAsync()
        {
            if (!IsPlaying || _isPaused)
                return Task.CompletedTask;

            _isPaused = true;
            _pauseStartTime = _stopwatch.Elapsed.TotalSeconds;
            _pauseStartRealTime = DateTime.Now;  // 记录暂停开始的真实时间
            _stopwatch.Stop();            return Task.CompletedTask;
        }

        /// <summary>
        /// 继续播放（包含暂停时间累加逻辑）
        /// 参考Python版本：keytime.py 行1546-1634
        /// </summary>
        public Task ResumePlaybackAsync()
        {
            if (!IsPlaying || !_isPaused)
                return Task.CompletedTask;

            var currentTime = DateTime.Now;
            
            // 🎯 计算暂停时长并累加
            if (_pauseStartTime > 0 && _currentIndex < _timingSequence.Count && _currentSimilarImageId > 0)
            {
                // 计算本次暂停时长（使用真实时间差，参考Python版本：keytime.py 行1566）
                var pauseDuration = (currentTime - _pauseStartRealTime).TotalSeconds;
                _totalPauseDuration += pauseDuration;

                // 计算已播放时间（暂停开始时间 - 当前帧开始时间）
                var playedDuration = _pauseStartTime;

                // 最终时间 = 已播放时间 + 总暂停时间
                var finalDisplayTime = playedDuration + _totalPauseDuration;
                // 🎯 异步更新数据库中的时间记录（Fire-and-forget模式）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _originalModeRepository.UpdateOriginalDurationAsync(
                            _currentBaseImageId,
                            _currentSimilarImageId,
                            finalDisplayTime);

                        // 重新加载时间序列（更新内存中的数据）
                        _timingSequence = await _originalModeRepository.GetOriginalTimingSequenceAsync(_currentBaseImageId);                    }
                    catch (Exception)
                    {                    }
                });
            }

            _isPaused = false;
            
            // 🎯 修复BUG：暂停增加时间后，应该立即跳到下一张图，而不是继续倒计时
            // 设置标志让播放循环立即跳到下一帧（参考Python版本：keytime.py 行1617-1629）
            _skipToNextFrame = true;            
            // 重置当前帧开始时间
            _currentFrameStartTime = DateTime.Now;
            
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
            _isPaused = false;            return Task.CompletedTask;
        }

        /// <summary>
        /// 记录原图模式手动操作，用于实时修正播放时间
        /// 参考Python版本：keytime.py 行834-875
        /// </summary>
        /// <param name="fromImageId">源图片ID</param>
        /// <param name="toImageId">目标图片ID</param>
        /// <returns>是否成功记录</returns>
        public Task<bool> RecordManualSwitchAsync(int fromImageId, int toImageId)
        {
            if (!IsPlaying || !_manualCorrectionEnabled)
            {                return Task.FromResult(false);
            }

            var currentTime = DateTime.Now;

            // 如果有当前帧开始时间，计算实际停留时间
            if (_currentFrameStartTime != DateTime.MinValue && _currentIndex >= 0 && _currentIndex < _timingSequence.Count)
            {
                var actualDuration = (currentTime - _currentFrameStartTime).TotalSeconds;

                // 🎯 获取当前播放序列中的正确 FromImageId 和 ToImageId
                // 注意：在等待期间_currentIndex还没有递增，所以直接使用_currentIndex（不是_currentIndex-1）
                var currentTiming = _timingSequence[_currentIndex];
                var correctFromId = currentTiming.FromImageId;
                var correctToId = currentTiming.ToImageId;
                // 异步更新数据库中的时间记录
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 使用正确的 FromImageId 和 ToImageId 更新数据库
                        var updateResult = await _originalModeRepository.UpdateTimingDurationAsync(
                            _currentBaseImageId, correctFromId, correctToId, actualDuration);

                        if (updateResult)
                        {
                            // 同时更新内存中的时间序列
                            UpdateTimingSequenceInMemory(correctFromId, correctToId, actualDuration);                        }
                        else
                        {                        }
                    }
                    catch (Exception)
                    {                    }
                });
            }

            // 记录当前操作时间，作为下一帧的开始时间
            _currentFrameStartTime = currentTime;
            _lastManualOperationTime = currentTime;

            // 🎯 手动跳转后需要重新启动倒计时，立即播放下一帧
            // 设置标志让播放循环立即跳到下一帧，这样会触发ProgressUpdated事件，重新启动倒计时
            _skipToNextFrame = true;
            _totalPauseDuration = 0.0;  // 重置暂停时长（新的一帧）
            return Task.FromResult(true);
        }

        /// <summary>
        /// 更新内存中的原图模式时间序列
        /// </summary>
        /// <param name="fromImageId">源图片ID</param>
        /// <param name="toImageId">目标图片ID</param>
        /// <param name="newDuration">新的停留时间</param>
        private void UpdateTimingSequenceInMemory(int fromImageId, int toImageId, double newDuration)
        {
            if (_timingSequence == null)
                return;

            // 直接通过 FromImageId 和 ToImageId 来查找匹配的记录
            for (int i = 0; i < _timingSequence.Count; i++)
            {
                var timing = _timingSequence[i];
                
                if (timing.FromImageId == fromImageId && timing.ToImageId == toImageId)
                {
                    // 更新时间（直接修改对象属性，而不是替换整个对象）
                    timing.Duration = newDuration;                    break;
                }
            }
        }
    }

    /// <summary>
    /// 切换图片事件参数
    /// </summary>
    public class SwitchImageEventArgs : EventArgs
    {
        public int ImageId { get; set; }
        public string ImagePath { get; set; }
    }
}

