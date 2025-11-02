using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Repositories.Interfaces;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services.Implementations
{
    /// <summary>
    /// 合成播放服务
    /// 根据合成脚本的总时长，从第一帧平滑滚动到最后一帧，然后循环
    /// - 当有关键帧时，总时长为关键帧时间的累计值（自动计算）
    /// - 当没有关键帧时，使用CompositeScript中手动设定的总时长
    /// </summary>
    public class CompositePlaybackService : IPlaybackService
    {
        private readonly ITimingRepository _timingRepository;
        private readonly IKeyframeRepository _keyframeRepository;
        private readonly Repositories.Interfaces.ICompositeScriptRepository _compositeScriptRepository;
        private CancellationTokenSource _cancellationTokenSource;

        private int _currentImageId;
        private double _totalDuration; // 总时长（秒）
        private double _startPosition; // 起始Y坐标
        private double _endPosition;   // 结束Y坐标
        private System.Collections.Generic.List<PlaybackSegment> _playbackSegments; // 播放段列表
        private System.Diagnostics.Stopwatch _playbackStopwatch; // 播放计时器（用于追踪已播放时间）
        private DateTime _playbackStartTime; // 播放开始时间

        /// <summary>
        /// 当前播放模式
        /// </summary>
        public PlaybackMode Mode => PlaybackMode.Composite;

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// 是否已暂停
        /// </summary>
        public bool IsPaused { get; private set; }

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
        /// 请求滚动事件（触发MainWindow执行滚动动画）
        /// </summary>
        public event EventHandler<CompositeScrollEventArgs> ScrollRequested;

        /// <summary>
        /// 请求停止滚动事件（触发MainWindow停止当前滚动动画）
        /// </summary>
        public event EventHandler ScrollStopRequested;

        /// <summary>
        /// 当前关键帧变化事件（用于更新指示块颜色）
        /// </summary>
        public event EventHandler<CurrentKeyframeChangedEventArgs> CurrentKeyframeChanged;

        /// <summary>
        /// 请求获取可滚动高度事件（触发MainWindow返回ScrollViewer的可滚动高度）
        /// </summary>
        public event EventHandler<ScrollableHeightRequestEventArgs> ScrollableHeightRequested;

        public CompositePlaybackService(
            ITimingRepository timingRepository,
            IKeyframeRepository keyframeRepository,
            Repositories.Interfaces.ICompositeScriptRepository compositeScriptRepository)
        {
            _timingRepository = timingRepository ?? throw new ArgumentNullException(nameof(timingRepository));
            _keyframeRepository = keyframeRepository ?? throw new ArgumentNullException(nameof(keyframeRepository));
            _compositeScriptRepository = compositeScriptRepository ?? throw new ArgumentNullException(nameof(compositeScriptRepository));
            _playbackStopwatch = new System.Diagnostics.Stopwatch();
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

            _currentImageId = imageId;

            // 1. 获取合成脚本（优先从CompositeScript读取时间配置）
            var compositeScript = await _compositeScriptRepository.GetByImageIdAsync(imageId);

            // 2. 获取所有关键帧
            var keyframes = await _keyframeRepository.GetKeyframesByImageIdAsync(imageId);
            
            // 3. 获取时间序列
            var timingSequence = await _timingRepository.GetTimingSequenceAsync(imageId);

            // 4. 判断播放模式
            bool hasKeyframes = keyframes != null && keyframes.Count >= 1;
            bool hasMultipleKeyframes = keyframes != null && keyframes.Count >= 2;
            bool hasTimings = timingSequence != null && timingSequence.Any();

            if (hasKeyframes && hasTimings)
            {
                // 模式1：有关键帧和时间序列 - 使用关键帧模式播放
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"📊 合成播放模式1：有关键帧和时间序列");
                #endif

                // 计算总时长（从时间序列累计）
                _totalDuration = timingSequence.Sum(t => t.Duration);

                // 更新或创建合成脚本（自动计算模式）
                await _compositeScriptRepository.CreateOrUpdateAsync(imageId, _totalDuration, autoCalculate: true);

                // 获取起始和结束位置
                var orderedKeyframes = keyframes.OrderBy(k => k.OrderIndex).ToList();
                _startPosition = orderedKeyframes.First().YPosition;
                _endPosition = orderedKeyframes.Last().YPosition;

                // 构建播放段（考虑循环标记）
                _playbackSegments = BuildPlaybackSegments(orderedKeyframes, timingSequence.ToList());
            }
            else if (hasKeyframes && !hasTimings)
            {
                // 模式2：有关键帧但无录制数据
                
                if (hasMultipleKeyframes)
                {
                    // 模式2a：有多个关键帧 - 从第一帧滚动到最后一帧
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"📊 合成播放模式2a：有多个关键帧但无录制数据，从第一帧滚动到最后一帧");
                    #endif

                    // 从CompositeScript获取TOTAL时间，如果没有则使用默认100秒
                    _totalDuration = compositeScript?.TotalDuration ?? 100.0;
                    
                    // 如果CompositeScript不存在，创建默认的
                    if (compositeScript == null)
                    {
                        await _compositeScriptRepository.CreateOrUpdateAsync(imageId, _totalDuration, autoCalculate: false);
                    }

                    // 获取起始和结束位置
                    var orderedKeyframes = keyframes.OrderBy(k => k.OrderIndex).ToList();
                    _startPosition = orderedKeyframes.First().YPosition;
                    _endPosition = orderedKeyframes.Last().YPosition;

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"   从关键帧 {_startPosition} 滚动到 {_endPosition}");
                    System.Diagnostics.Debug.WriteLine($"   使用TOTAL时间: {_totalDuration:F1}秒");
                    #endif

                    // 构建一个简单的滚动段：从第一帧直接滚动到最后一帧
                    _playbackSegments = new System.Collections.Generic.List<PlaybackSegment>
                    {
                        new PlaybackSegment
                        {
                            Type = SegmentType.Scroll,
                            StartPosition = _startPosition,
                            EndPosition = _endPosition,
                            Duration = _totalDuration,
                            KeyframeId = orderedKeyframes.Last().Id
                        }
                    };
                }
                else
                {
                    // 模式2b：只有一个关键帧 - 从该关键帧滚动到底部
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"📊 合成播放模式2b：只有一个关键帧，从该关键帧滚动到底部");
                    #endif

                    // 从CompositeScript获取TOTAL时间，如果没有则使用默认100秒
                    _totalDuration = compositeScript?.TotalDuration ?? 100.0;
                    
                    // 如果CompositeScript不存在，创建默认的
                    if (compositeScript == null)
                    {
                        await _compositeScriptRepository.CreateOrUpdateAsync(imageId, _totalDuration, autoCalculate: false);
                    }

                    // 起始位置是唯一的关键帧
                    _startPosition = keyframes.First().YPosition;
                    
                    // 请求获取实际的可滚动高度
                    var heightArgs = new ScrollableHeightRequestEventArgs();
                    ScrollableHeightRequested?.Invoke(this, heightArgs);
                    
                    // 📏 只滚动到75%的位置，保留底部25%内容可见
                    double fullScrollableHeight = heightArgs.ScrollableHeight > 0 ? heightArgs.ScrollableHeight : 10000;
                    _endPosition = _startPosition + (fullScrollableHeight - _startPosition) * 0.75;

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"   从关键帧: {_startPosition:F0}");
                    System.Diagnostics.Debug.WriteLine($"   完整可滚动高度: {fullScrollableHeight:F0}");
                    System.Diagnostics.Debug.WriteLine($"   实际滚动到: {_endPosition:F0} (剩余距离的75%)");
                    System.Diagnostics.Debug.WriteLine($"   保留底部: {fullScrollableHeight - _endPosition:F0} (25%)");
                    System.Diagnostics.Debug.WriteLine($"   使用TOTAL时间: {_totalDuration:F1}秒");
                    #endif

                    // 构建一个简单的滚动段：从关键帧滚动到底部
                    _playbackSegments = new System.Collections.Generic.List<PlaybackSegment>
                    {
                        new PlaybackSegment
                        {
                            Type = SegmentType.Scroll,
                            StartPosition = _startPosition,
                            EndPosition = _endPosition,
                            Duration = _totalDuration,
                            KeyframeId = keyframes.First().Id
                        }
                    };
                }
            }
            else if (!hasKeyframes)
            {
                // 模式3：无关键帧 - 从顶部滚动到底部，使用TOTAL时间（默认100秒）
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"📊 合成播放模式3：无关键帧，从顶部滚动");
                #endif

                // 从CompositeScript获取TOTAL时间，如果没有则使用默认100秒
                _totalDuration = compositeScript?.TotalDuration ?? 100.0;
                
                // 如果CompositeScript不存在，创建默认的
                if (compositeScript == null)
                {
                    await _compositeScriptRepository.CreateOrUpdateAsync(imageId, _totalDuration, autoCalculate: false);
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"   创建默认CompositeScript: {_totalDuration}秒");
                    #endif
                }

                // 使用整个图片的范围作为滚动区域（从0到可滚动高度）
                _startPosition = 0;
                
                // 请求获取实际的可滚动高度
                var heightArgs = new ScrollableHeightRequestEventArgs();
                ScrollableHeightRequested?.Invoke(this, heightArgs);
                
                // 📏 只滚动到75%的位置，保留底部25%内容可见
                double fullScrollableHeight = heightArgs.ScrollableHeight > 0 ? heightArgs.ScrollableHeight : 10000;
                _endPosition = fullScrollableHeight * 0.75;
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   完整可滚动高度: {fullScrollableHeight:F0}");
                System.Diagnostics.Debug.WriteLine($"   实际滚动到: {_endPosition:F0} (75%)");
                System.Diagnostics.Debug.WriteLine($"   保留底部: {fullScrollableHeight - _endPosition:F0} (25%)");
                System.Diagnostics.Debug.WriteLine($"   使用TOTAL时间: {_totalDuration:F1}秒");
                #endif

                // 构建一个简单的滚动段：从顶部滚动到底部
                // 滚动函数（线性/贝塞尔等）会在TOTAL时间内自然完成滚动
                _playbackSegments = new System.Collections.Generic.List<PlaybackSegment>
                {
                    new PlaybackSegment
                    {
                        Type = SegmentType.Scroll,
                        StartPosition = _startPosition,
                        EndPosition = _endPosition,
                        Duration = _totalDuration,
                        KeyframeId = 0
                    }
                };
            }
            else
            {
                // 理论上不应该到这里
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ 未知的播放模式组合");
                #endif
                return;
            }

            IsPlaying = true;
            IsPaused = false;
            CompletedPlayCount = 0;
            _cancellationTokenSource = new CancellationTokenSource();

            // 启动计时器
            _playbackStopwatch.Restart();
            _playbackStartTime = DateTime.Now;

            // 启动播放循环
            _ = Task.Run(() => PlaybackLoopAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 构建播放段（考虑循环标记）
        /// </summary>
        private System.Collections.Generic.List<PlaybackSegment> BuildPlaybackSegments(
            System.Collections.Generic.List<Database.Models.Keyframe> keyframes,
            System.Collections.Generic.List<Database.Models.DTOs.TimingSequenceDto> timings)
        {
            var segments = new System.Collections.Generic.List<PlaybackSegment>();
            
            int currentStartIndex = 0; // 当前滚动段的起始关键帧索引
            double accumulatedDuration = 0; // 累积的滚动时长
            
            for (int i = 0; i < keyframes.Count; i++)
            {
                var keyframe = keyframes[i];
                var timing = timings[i];
                
                // 如果当前帧有循环标记
                if (keyframe.LoopCount.HasValue && keyframe.LoopCount.Value > 1)
                {
                    // 先添加滚动段：从currentStartIndex滚动到当前i（如果有滚动距离）
                    if (currentStartIndex < i) // 只有当有距离时才添加滚动段
                    {
                        segments.Add(new PlaybackSegment
                        {
                            Type = SegmentType.Scroll,
                            StartPosition = keyframes[currentStartIndex].YPosition,
                            EndPosition = keyframes[i].YPosition,
                        Duration = accumulatedDuration,
                        KeyframeId = keyframes[i].Id // 滚动到目标关键帧
                    });
                    }
                    
                    // 添加停留段
                    segments.Add(new PlaybackSegment
                    {
                        Type = SegmentType.Pause,
                        StartPosition = keyframes[i].YPosition,
                        EndPosition = keyframes[i].YPosition,
                        Duration = timing.Duration,
                        KeyframeId = keyframes[i].Id // 停留在当前关键帧
                    });
                    
                    // 重置累积时长，准备下一个滚动段
                    currentStartIndex = i;
                    accumulatedDuration = 0;
                }
                else
                {
                    // 累积时长
                    accumulatedDuration += timing.Duration;
                }
            }
            
            // 处理最后的滚动段（从最后一个循环点或起点到结尾）
            if (currentStartIndex < keyframes.Count - 1)
            {
                segments.Add(new PlaybackSegment
                {
                    Type = SegmentType.Scroll,
                    StartPosition = keyframes[currentStartIndex].YPosition,
                    EndPosition = keyframes[keyframes.Count - 1].YPosition,
                    Duration = accumulatedDuration,
                    KeyframeId = keyframes[keyframes.Count - 1].Id // 滚动到最后一帧
                });
            }
            
            return segments;
        }

        /// <summary>
        /// 播放循环
        /// </summary>
        private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (IsPlaying && !cancellationToken.IsCancellationRequested)
                {
                    // 判断是否应该继续播放
                    if (PlayCount != -1 && CompletedPlayCount >= PlayCount)
                    {
                        break;
                    }
                    
                    // 依次播放每个段
                    foreach (var segment in _playbackSegments)
                    {
                        if (!IsPlaying || cancellationToken.IsCancellationRequested)
                            break;
                        
                        if (segment.Type == SegmentType.Scroll)
                        {
                            // 滚动段：触发滚动请求
                            // 触发当前关键帧变化事件（滚动到目标关键帧）
                            CurrentKeyframeChanged?.Invoke(this, new CurrentKeyframeChangedEventArgs
                            {
                                KeyframeId = segment.KeyframeId,
                                YPosition = segment.EndPosition
                            });
                            
                            ScrollRequested?.Invoke(this, new CompositeScrollEventArgs
                            {
                                StartPosition = segment.StartPosition,
                                EndPosition = segment.EndPosition,
                                Duration = segment.Duration
                            });
                            
                            // 触发进度更新（显示倒计时）
                            ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
                            {
                                CurrentIndex = CompletedPlayCount,
                                TotalCount = PlayCount == -1 ? 999 : PlayCount,
                                RemainingTime = segment.Duration,
                                CurrentItemId = _currentImageId
                            });
                            
                            // 等待滚动完成
                            await Task.Delay(TimeSpan.FromSeconds(segment.Duration), cancellationToken);
                        }
                        else if (segment.Type == SegmentType.Pause)
                        {
                            // 停留段：画面不动，只等待
                            // 触发当前关键帧变化事件（停留在当前关键帧）
                            CurrentKeyframeChanged?.Invoke(this, new CurrentKeyframeChangedEventArgs
                            {
                                KeyframeId = segment.KeyframeId,
                                YPosition = segment.StartPosition
                            });
                            
                            // 触发进度更新（显示倒计时）
                            ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
                            {
                                CurrentIndex = CompletedPlayCount,
                                TotalCount = PlayCount == -1 ? 999 : PlayCount,
                                RemainingTime = segment.Duration,
                                CurrentItemId = _currentImageId
                            });
                            
                            // 等待停留时间
                            await Task.Delay(TimeSpan.FromSeconds(segment.Duration), cancellationToken);
                        }
                    }

                    // 完成一轮播放
                    CompletedPlayCount++;

                    // 📊 检查是否需要自动调整TOTAL时间（仅在无关键帧或单关键帧模式）
                    await CheckAndAdjustTotalTimeAsync();

                    // 如果还要继续，跳回起始位置
                    if (PlayCount == -1 || CompletedPlayCount < PlayCount)
                    {
                        // 触发跳回起始位置的事件
                        ScrollRequested?.Invoke(this, new CompositeScrollEventArgs
                        {
                            StartPosition = _startPosition,
                            EndPosition = _startPosition,
                            Duration = 0 // 时长为0表示直接跳转，不滚动
                        });
                        
                        await Task.Delay(100, cancellationToken); // 短暂延迟，让跳转完成
                    }
                }

                // 播放结束
                await StopPlaybackAsync();
                PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                // 播放被取消
            }
            catch (Exception)
            {
                // 忽略异常
            }
        }

        /// <summary>
        /// 暂停播放
        /// </summary>
        public Task PausePlaybackAsync()
        {
            if (!IsPlaying || IsPaused)
                return Task.CompletedTask;

            IsPaused = true;
            // TODO: 实现暂停逻辑（暂停滚动动画）
            return Task.CompletedTask;
        }

        /// <summary>
        /// 继续播放
        /// </summary>
        public Task ResumePlaybackAsync()
        {
            if (!IsPlaying || !IsPaused)
                return Task.CompletedTask;

            IsPaused = false;
            // TODO: 实现继续逻辑（恢复滚动动画）
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
            IsPlaying = false;
            IsPaused = false;
            _playbackStopwatch.Stop();

            // 触发停止滚动事件，立即停止当前正在进行的滚动动画
            ScrollStopRequested?.Invoke(this, EventArgs.Empty);

            return Task.CompletedTask;
        }

        /// <summary>
        /// 更新TOTAL时间为已播放时间，并立即重新开始循环播放
        /// 用于手动调整：当播放到合适位置时，点击下一帧快捷键可快速设定正确的TOTAL时间
        /// </summary>
        public async Task UpdateTotalAndRestartAsync()
        {
            if (!IsPlaying)
                return;

            // 获取已播放的时间（秒）
            double elapsedSeconds = _playbackStopwatch.Elapsed.TotalSeconds;

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"⏱️ 更新TOTAL时间: {elapsedSeconds:F2}秒");
            #endif

            // 如果时间太短（小于1秒），忽略
            if (elapsedSeconds < 1.0)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ 播放时间太短，忽略更新");
                #endif
                return;
            }

            // 停止当前播放
            await StopPlaybackAsync();

            // 更新CompositeScript的TOTAL时间
            await _compositeScriptRepository.CreateOrUpdateAsync(_currentImageId, elapsedSeconds, autoCalculate: false);

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ 已更新TOTAL时间为 {elapsedSeconds:F2}秒，重新开始循环播放");
            #endif

            // 短暂延迟，确保停止完成
            await Task.Delay(100);

            // 重新开始播放（会读取新的TOTAL时间）
            await StartPlaybackAsync(_currentImageId);
        }

        /// <summary>
        /// 获取当前已播放的时间（秒）
        /// </summary>
        public double GetElapsedSeconds()
        {
            return _playbackStopwatch.Elapsed.TotalSeconds;
        }

        /// <summary>
        /// 检查并自动调整TOTAL时间
        /// 仅在Mode 2b（单关键帧）和Mode 3（无关键帧）时执行
        /// 如果实际播放时间远小于设定的TOTAL时间，自动更新为实际时间
        /// </summary>
        private async Task CheckAndAdjustTotalTimeAsync()
        {
            // 只检查单段滚动模式（Mode 2b和Mode 3）
            if (_playbackSegments.Count != 1 || _playbackSegments[0].Type != SegmentType.Scroll)
                return;

            var segment = _playbackSegments[0];
            
            // Mode 2b: 单关键帧（KeyframeId > 0）
            // Mode 3: 无关键帧（KeyframeId == 0）
            if (segment.KeyframeId != 0 && segment.KeyframeId != _playbackSegments[0].KeyframeId)
                return;

            double actualElapsed = _playbackStopwatch.Elapsed.TotalSeconds;
            double expectedDuration = _totalDuration;

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"");
            System.Diagnostics.Debug.WriteLine($"🕐 [TOTAL时间检测]");
            System.Diagnostics.Debug.WriteLine($"   实际播放时间: {actualElapsed:F2}秒");
            System.Diagnostics.Debug.WriteLine($"   设定TOTAL时间: {expectedDuration:F2}秒");
            System.Diagnostics.Debug.WriteLine($"   差异百分比: {(expectedDuration - actualElapsed) / expectedDuration * 100:F1}%");
            #endif

            // 如果实际时间明显小于预期时间（提前20%以上完成）
            // 说明TOTAL时间设置得太长了，需要调整
            if (actualElapsed < expectedDuration * 0.8 && actualElapsed >= 5.0)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 检测到TOTAL时间过长，自动调整:");
                System.Diagnostics.Debug.WriteLine($"   {expectedDuration:F1}秒 -> {actualElapsed:F1}秒");
                #endif

                // 更新数据库的TOTAL时间
                await _compositeScriptRepository.CreateOrUpdateAsync(_currentImageId, actualElapsed, autoCalculate: false);
                
                // 更新内存中的值
                _totalDuration = actualElapsed;
                _playbackSegments[0].Duration = actualElapsed;

                // 重置计时器，下一轮使用新时间
                _playbackStopwatch.Restart();

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ TOTAL时间已自动更新为 {actualElapsed:F1}秒");
                #endif
            }
            else
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✓ TOTAL时间合理，无需调整");
                #endif
                
                // 重置计时器，准备下一轮
                _playbackStopwatch.Restart();
            }
        }
    }

    /// <summary>
    /// 合成播放滚动事件参数
    /// </summary>
    public class CompositeScrollEventArgs : EventArgs
    {
        /// <summary>起始Y坐标</summary>
        public double StartPosition { get; set; }

        /// <summary>结束Y坐标</summary>
        public double EndPosition { get; set; }

        /// <summary>滚动时长（秒）</summary>
        public double Duration { get; set; }
    }

    /// <summary>
    /// 播放段类型
    /// </summary>
    internal enum SegmentType
    {
        /// <summary>滚动段：从起始位置滚动到结束位置</summary>
        Scroll,
        
        /// <summary>停留段：画面停在该位置不动</summary>
        Pause
    }

    /// <summary>
    /// 播放段
    /// </summary>
    internal class PlaybackSegment
    {
        /// <summary>段类型</summary>
        public SegmentType Type { get; set; }
        
        /// <summary>起始Y坐标</summary>
        public double StartPosition { get; set; }
        
        /// <summary>结束Y坐标</summary>
        public double EndPosition { get; set; }
        
        /// <summary>持续时长（秒）</summary>
        public double Duration { get; set; }
        
        /// <summary>关键帧ID（用于标识当前播放的关键帧）</summary>
        public int KeyframeId { get; set; }
    }

    /// <summary>
    /// 当前关键帧变化事件参数
    /// </summary>
    public class CurrentKeyframeChangedEventArgs : EventArgs
    {
        /// <summary>当前关键帧ID</summary>
        public int KeyframeId { get; set; }
        
        /// <summary>当前关键帧Y坐标</summary>
        public double YPosition { get; set; }
    }

    /// <summary>
    /// 请求可滚动高度事件参数
    /// </summary>
    public class ScrollableHeightRequestEventArgs : EventArgs
    {
        /// <summary>可滚动高度（由MainWindow填充）</summary>
        public double ScrollableHeight { get; set; }
    }
}

