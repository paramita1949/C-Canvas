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
            if (IsPlaying)
            {
                Logger.Warning("已在播放中");
                return;
            }

            // 加载时间序列
            _timingSequence = await _originalModeRepository.GetOriginalTimingSequenceAsync(imageId);
            if (_timingSequence == null || !_timingSequence.Any())
            {
                Logger.Warning("图片{ImageId}没有原图时间数据", imageId);
                return;
            }

            _currentBaseImageId = imageId;
            _currentIndex = 0;
            CompletedPlayCount = 0;
            _isPaused = false;

            IsPlaying = true;
            _cancellationTokenSource = new CancellationTokenSource();

            Logger.Info("开始原图播放: BaseImageId={ImageId}, 时间点数量={Count}, 播放次数={PlayCount}", 
                imageId, _timingSequence.Count, PlayCount);
            
            // 🔍 调试：输出时间序列详细信息
            Logger.Debug("📋 时间序列详情:");
            for (int i = 0; i < _timingSequence.Count; i++)
            {
                var timing = _timingSequence[i];
                Logger.Debug("  [{Index}] From={FromId} -> To={ToId} (Similar={SimilarId}), Duration={Duration}s", 
                    i, timing.FromImageId, timing.ToImageId, timing.SimilarImageId, timing.Duration);
            }

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
                Logger.Debug("🔁 进入播放循环");
                int loopIteration = 0;
                
                while (IsPlaying && !cancellationToken.IsCancellationRequested)
                {
                    loopIteration++;
                    Logger.Debug("🔄 循环迭代 #{Iteration}: IsPlaying={IsPlaying}, Index={Index}, Completed={Completed}", 
                        loopIteration, IsPlaying, _currentIndex, CompletedPlayCount);
                        
                    // 判断是否应该继续播放
                    if (!PlayCountJudge.ShouldContinue(PlayCount, CompletedPlayCount))
                    {
                        Logger.Info("播放次数已达到，结束播放: PlayCount={PlayCount}, CompletedPlayCount={Completed}", 
                            PlayCount, CompletedPlayCount);
                        break;
                    }

                    Logger.Debug("✅ 继续播放判断通过，开始播放下一帧");
                    
                    // 播放下一帧
                    await PlayNextFrameAsync(cancellationToken);
                    
                    // 添加短暂延迟，避免死循环占用CPU
                    await Task.Delay(10, cancellationToken);
                }

                Logger.Debug("🏁 退出播放循环: IsPlaying={IsPlaying}, Index={Index}, Completed={Completed}", 
                    IsPlaying, _currentIndex, CompletedPlayCount);
                    
                // 播放结束
                await StopPlaybackAsync();
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("原图播放被取消");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "原图播放循环异常");
            }
        }

        /// <summary>
        /// 播放下一帧
        /// 参考Python版本：keytime.py 行1708-1828
        /// 🎯 修正：每次只处理一帧，返回到主循环
        /// </summary>
        private async Task PlayNextFrameAsync(CancellationToken cancellationToken)
        {
            Logger.Debug("🎬 PlayNextFrameAsync: Index={Index}, Count={Count}, CompletedPlayCount={Completed}", 
                _currentIndex, _timingSequence.Count, CompletedPlayCount);
                
            if (_currentIndex >= _timingSequence.Count)
            {
                Logger.Debug("⚠️ 索引超出范围，退出播放");
                return;
            }

            var currentTiming = _timingSequence[_currentIndex];
            var fromImageId = currentTiming.FromImageId;
            var toImageId = currentTiming.SimilarImageId;
            var duration = currentTiming.Duration;
            
            Logger.Debug("📊 当前帧数据: Index={Index}, From={FromId}, To={ToId}, Duration={Duration}s", 
                _currentIndex, fromImageId, toImageId, duration);

            // 记录当前帧信息
            _currentSimilarImageId = toImageId;
            _totalPauseDuration = 0.0;
            _currentFrameStartTime = DateTime.Now;

            // 🎯 第一帧特殊处理（参考Python: 行1750-1763）
            if (_currentIndex == 0 && CompletedPlayCount == 0)
            {
                Logger.Debug("🎯 第一帧处理: 切到From={FromId}, 等待{Duration}s, 再切到To={ToId}", 
                    fromImageId, duration, toImageId);
                    
                // 首次播放：切到FromImageId，等待Duration，然后切到ToImageId
                SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
                {
                    ImageId = fromImageId,
                    ImagePath = null  // UI端会根据ImageId查询路径
                });
                
                ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
                {
                    CurrentIndex = _currentIndex,
                    TotalCount = _timingSequence.Count,
                    RemainingTime = duration,
                    CurrentItemId = toImageId
                });
                
                Logger.Debug("⏱️ 开始等待 {Duration}s...", duration);
                await WaitForDurationAsync(duration, cancellationToken);
                Logger.Debug("✅ 等待完成，切换到 To={ToId}", toImageId);
                
                SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
                {
                    ImageId = toImageId,
                    ImagePath = currentTiming.SimilarImagePath
                });
                
                _currentIndex++;
                Logger.Debug("📍 第一帧完成，Index递增至 {Index}", _currentIndex);
                return;
            }

            // 🎯 最后一帧特殊处理（参考Python: 行1766-1817）
            if (_currentIndex == _timingSequence.Count - 1)
            {
                Logger.Debug("🏁 最后一帧处理: CompletedPlayCount={Completed}, PlayCount={PlayCount}", 
                    CompletedPlayCount, PlayCount);
                    
                var firstImageId = _timingSequence[0].FromImageId;
                
                // 检查是否应该继续循环
                bool shouldContinue = PlayCountJudge.ShouldContinue(PlayCount, CompletedPlayCount + 1);
                Logger.Debug("🔍 循环判断: shouldContinue={ShouldContinue}, CompletedPlayCount+1={Next}", 
                    shouldContinue, CompletedPlayCount + 1);
                
                if (shouldContinue)
                {
                    // 🎯 优化：如果最后一帧的ToImageId就是第一张图，跳过切换
                    if (toImageId == firstImageId)
                    {
                        Logger.Debug("🔄 循环优化：跳过重复切换到第一张图 (ID:{ImageId})", toImageId);
                        CompletedPlayCount++;
                        _currentIndex = 0;
                        Logger.Debug("📍 重置索引: Index=0, CompletedPlayCount={Completed}", CompletedPlayCount);
                        return; // 返回主循环，继续下一轮
                    }
                    else
                    {
                        Logger.Debug("🔄 正常循环：切换到图{ToImageId}", toImageId);
                        // 正常切换到ToImageId，然后开始新一轮
                        SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
                        {
                            ImageId = toImageId,
                            ImagePath = currentTiming.SimilarImagePath
                        });
                        
                        CompletedPlayCount++;
                        _currentIndex = 0;
                        Logger.Debug("📍 开始第{Count}轮播放, Index=0, CompletedPlayCount={Completed}", 
                            CompletedPlayCount + 1, CompletedPlayCount);
                        return; // 返回主循环，继续下一轮
                    }
                }
                else
                {
                    Logger.Debug("🛑 播放结束：不需要循环");
                    // 不需要循环，显示最后一帧然后结束
                    SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
                    {
                        ImageId = toImageId,
                        ImagePath = currentTiming.SimilarImagePath
                    });
                    CompletedPlayCount++;
                    _currentIndex = _timingSequence.Count; // 标记结束
                    Logger.Debug("📍 设置结束标志: Index={Index}", _currentIndex);
                    return;
                }
            }

            // 🎯 普通帧处理（参考Python: 行1819-1828）
            // 当前已经在FromImageId上（上一帧切换过来的）
            // 显示进度，等待Duration，然后切到ToImageId
            
            Logger.Debug("▶️ 普通帧处理: 显示进度并等待 {Duration}s", duration);
            
            ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
            {
                CurrentIndex = _currentIndex,
                TotalCount = _timingSequence.Count,
                RemainingTime = duration,
                CurrentItemId = toImageId
            });
            
            // 等待Duration
            Logger.Debug("⏱️ 开始等待 {Duration}s...", duration);
            await WaitForDurationAsync(duration, cancellationToken);
            Logger.Debug("✅ 等待完成，切换到 To={ToId}", toImageId);
            
            // 切换到ToImageId
            SwitchImageRequested?.Invoke(this, new SwitchImageEventArgs
            {
                ImageId = toImageId,
                ImagePath = currentTiming.SimilarImagePath
            });
            
            _currentIndex++;
            Logger.Debug("📍 普通帧完成，Index递增至 {Index}", _currentIndex);
        }

        /// <summary>
        /// 等待指定时长
        /// </summary>
        private async Task WaitForDurationAsync(double duration, CancellationToken cancellationToken)
        {
            _stopwatch.Restart();

            while (_stopwatch.Elapsed.TotalSeconds < duration)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                if (_skipToNextFrame)
                {
                    _skipToNextFrame = false;
                    Logger.Info("立即跳到下一帧，跳过剩余等待时间");
                    break;
                }

                while (_isPaused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(50, cancellationToken);
                }

                await Task.Delay(10, cancellationToken);
            }
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
            _stopwatch.Stop();

            Logger.Info("暂停原图播放");
            return Task.CompletedTask;
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

                Logger.Debug("原图播放继续 - 暂停时长={PauseDuration}s, 已播放={PlayedDuration}s, 最终时间={FinalTime}s",
                    pauseDuration, playedDuration, finalDisplayTime);

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
                        _timingSequence = await _originalModeRepository.GetOriginalTimingSequenceAsync(_currentBaseImageId);

                        Logger.Info("暂停时间累加完成: BaseImageId={BaseId}, SimilarImageId={SimId}, 最终时间={FinalTime}s",
                            _currentBaseImageId, _currentSimilarImageId, finalDisplayTime);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "更新暂停时间失败");
                    }
                });
            }

            _isPaused = false;
            
            // 🎯 修复BUG：暂停增加时间后，应该立即跳到下一张图，而不是继续倒计时
            // 设置标志让播放循环立即跳到下一帧（参考Python版本：keytime.py 行1617-1629）
            _skipToNextFrame = true;
            Logger.Info("继续原图播放：设置立即跳转标志");
            
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
            _isPaused = false;

            Logger.Info("停止原图播放");
            return Task.CompletedTask;
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
            {
                Logger.Debug("跳过手动修正: IsPlaying={IsPlaying}, Enabled={Enabled}", IsPlaying, _manualCorrectionEnabled);
                return Task.FromResult(false);
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

                Logger.Info("🔧 原图播放手动修正: {FromId} -> {ToId}, 实际停留时间: {Duration}s", 
                    correctFromId, correctToId, actualDuration);
                Logger.Debug("   当前显示图片ID={CurrentId}, 序列索引={Index}", toImageId, _currentIndex);

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
                            UpdateTimingSequenceInMemory(correctFromId, correctToId, actualDuration);
                            
                            Logger.Info("✅ 原图播放时间修正成功: {FromId} -> {ToId} = {Duration}s",
                                correctFromId, correctToId, actualDuration);
                        }
                        else
                        {
                            Logger.Warning("❌ 原图播放时间修正失败：数据库更新失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "原图播放时间修正异常");
                    }
                });
            }

            // 记录当前操作时间，作为下一帧的开始时间
            _currentFrameStartTime = currentTime;
            _lastManualOperationTime = currentTime;

            // 🎯 手动跳转后需要重新启动倒计时，立即播放下一帧
            // 设置标志让播放循环立即跳到下一帧，这样会触发ProgressUpdated事件，重新启动倒计时
            _skipToNextFrame = true;
            _totalPauseDuration = 0.0;  // 重置暂停时长（新的一帧）
            Logger.Debug("🔄 手动跳转已记录，设置立即跳转标志");

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
                    timing.Duration = newDuration;
                    
                    Logger.Debug("✅ 已更新内存时间序列: 索引{Index}, {FromId}->{ToId}, 新时长{Duration}s", 
                        i, fromImageId, toImageId, newDuration);
                    break;
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

