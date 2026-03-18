using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
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
        private readonly ConfigManager _configManager;
        private CancellationTokenSource _cancellationTokenSource;

        private int _currentImageId;
        private double _totalDuration; // 总时长（秒）
        private double _startPosition; // 起始Y坐标
        private double _endPosition;   // 结束Y坐标
        private System.Collections.Generic.List<PlaybackSegment> _playbackSegments; // 播放段列表
        private System.Diagnostics.Stopwatch _playbackStopwatch; // 播放计时器（用于追踪已播放时间）
        private DateTime _playbackStartTime; // 播放开始时间
        private PlaybackSegment _currentSegment; // 当前正在播放的段
        private DateTime _currentSegmentStartTime; // 当前段开始时间
        private double _currentSegmentSpeed; // 当前段开始时的速度（用于重新计算剩余时间）
        private double _currentSegmentPlayedOriginalTime; // 当前段已累计播放的原始时长
        private DateTime _pauseStartTime; // 暂停开始时间（用于恢复时剔除暂停时长）

        /// <summary>
        /// 当前播放模式
        /// </summary>
        public PlaybackMode Mode => PlaybackMode.Composite;

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// 当前轮次实际起始位置（由服务计算）
        /// </summary>
        public double CurrentStartPosition { get; private set; }

        /// <summary>
        /// 当前轮次实际终点位置（由服务计算）
        /// </summary>
        public double CurrentEndPosition { get; private set; }

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
        /// 播放速度倍率（默认1.0，表示正常速度）
        /// 1.0 = 正常速度，2.0 = 2倍速，0.5 = 0.5倍速
        /// </summary>
        public double Speed { get; private set; } = 1.0;

        /// <summary>
        /// 播放速度变化事件
        /// </summary>
        public event EventHandler<SpeedChangedEventArgs> SpeedChanged;

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
        public event EventHandler<CompositeScrollStopEventArgs> ScrollStopRequested;

        /// <summary>
        /// 当前关键帧变化事件（用于更新指示块颜色）
        /// </summary>
        public event EventHandler<CurrentKeyframeChangedEventArgs> CurrentKeyframeChanged;

        /// <summary>
        /// 请求获取可滚动高度事件（触发MainWindow返回ScrollViewer的可滚动高度）
        /// </summary>
        public event EventHandler<ScrollableHeightRequestEventArgs> ScrollableHeightRequested;
        
        /// <summary>
        /// 请求获取当前滚动位置事件（触发MainWindow返回ScrollViewer的当前滚动位置）
        /// </summary>
        public event EventHandler<CurrentScrollPositionRequestEventArgs> CurrentScrollPositionRequested;

        public CompositePlaybackService(
            ITimingRepository timingRepository,
            IKeyframeRepository keyframeRepository,
            Repositories.Interfaces.ICompositeScriptRepository compositeScriptRepository,
            ConfigManager configManager)
        {
            _timingRepository = timingRepository ?? throw new ArgumentNullException(nameof(timingRepository));
            _keyframeRepository = keyframeRepository ?? throw new ArgumentNullException(nameof(keyframeRepository));
            _compositeScriptRepository = compositeScriptRepository ?? throw new ArgumentNullException(nameof(compositeScriptRepository));
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
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
            
            //  如果脚本存在但使用的是旧的默认值（105秒），且不是自动计算的，则更新为JSON配置的默认值
            if (compositeScript != null && !compositeScript.AutoCalculate)
            {
                const double OLD_DEFAULT_DURATION = 105.0;
                if (Math.Abs(compositeScript.TotalDuration - OLD_DEFAULT_DURATION) < 0.01)
                {
                    // 使用JSON配置的默认值更新
                    compositeScript.TotalDuration = _configManager.CompositePlaybackDefaultDuration;
                    compositeScript.UpdatedAt = DateTime.Now;
                    await _compositeScriptRepository.CreateOrUpdateAsync(imageId, _configManager.CompositePlaybackDefaultDuration, autoCalculate: false);
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($" 更新脚本默认时间: {OLD_DEFAULT_DURATION}秒 -> {_configManager.CompositePlaybackDefaultDuration}秒");
                    #endif
                }
            }

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
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" 合成播放模式1：有关键帧和时间序列");
                //#endif

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
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" 合成播放模式2a：有多个关键帧但无录制数据，从第一帧滚动到最后一帧");
                    //#endif

                    // 从CompositeScript获取TOTAL时间，如果没有则使用配置的默认时间
                    _totalDuration = compositeScript?.TotalDuration ?? _configManager.CompositePlaybackDefaultDuration;
                    
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
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" 合成播放模式2b：只有一个关键帧，从该关键帧滚动到底部");
                    //#endif

                    // 从CompositeScript获取TOTAL时间，如果没有则使用配置的默认时间
                    _totalDuration = compositeScript?.TotalDuration ?? _configManager.CompositePlaybackDefaultDuration;
                    
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
                    
                    //  只滚动到75%的位置，保留底部25%内容可见
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
                // 模式3：无关键帧 - 从顶部滚动到底部，使用TOTAL时间（从配置读取默认值）
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" 合成播放模式3：无关键帧，从顶部滚动");
                //#endif

                // 从CompositeScript获取TOTAL时间，如果没有则使用配置的默认时间
                _totalDuration = compositeScript?.TotalDuration ?? _configManager.CompositePlaybackDefaultDuration;
                
                // 如果CompositeScript不存在，创建默认的
                if (compositeScript == null)
                {
                    await _compositeScriptRepository.CreateOrUpdateAsync(imageId, _totalDuration, autoCalculate: false);
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   创建默认CompositeScript: {_totalDuration}秒");
                    //#endif
                }

                // 使用整个图片的范围作为滚动区域（从0到可滚动高度）
                _startPosition = 0;
                
                // 请求获取实际的可滚动高度
                var heightArgs = new ScrollableHeightRequestEventArgs();
                ScrollableHeightRequested?.Invoke(this, heightArgs);
                
                //  只滚动到75%的位置，保留底部25%内容可见
                double fullScrollableHeight = heightArgs.ScrollableHeight > 0 ? heightArgs.ScrollableHeight : 10000;
                _endPosition = fullScrollableHeight * 0.75;
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   完整可滚动高度: {fullScrollableHeight:F0}");
                //System.Diagnostics.Debug.WriteLine($"   实际滚动到: {_endPosition:F0} (75%)");
                //System.Diagnostics.Debug.WriteLine($"   保留底部: {fullScrollableHeight - _endPosition:F0} (25%)");
                //System.Diagnostics.Debug.WriteLine($"   使用TOTAL时间: {_totalDuration:F1}秒");
                //#endif

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
                System.Diagnostics.Debug.WriteLine($" 未知的播放模式组合");
                #endif
                return;
            }

            IsPlaying = true;
            IsPaused = false;
            _pauseStartTime = default;
            CompletedPlayCount = 0;
            _cancellationTokenSource = new CancellationTokenSource();
            CurrentStartPosition = _startPosition;
            CurrentEndPosition = _endPosition;

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
                        
                        // 记录当前段信息（用于速度改变时重新计算）
                        _currentSegment = segment;
                        _currentSegmentStartTime = DateTime.Now;
                        _currentSegmentSpeed = Speed; // 保存段开始时的速度
                        _currentSegmentPlayedOriginalTime = 0;
                        
                        if (segment.Type == SegmentType.Scroll)
                        {
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [CompositePlaybackService] ========== 触发滚动段 ==========");
                            //System.Diagnostics.Debug.WriteLine($"   起始位置: {segment.StartPosition:F1}");
                            //System.Diagnostics.Debug.WriteLine($"   结束位置: {segment.EndPosition:F1}");
                            //System.Diagnostics.Debug.WriteLine($"   时长: {segment.Duration:F1}秒");
                            //System.Diagnostics.Debug.WriteLine($"   关键帧ID: {segment.KeyframeId}");
                            //System.Diagnostics.Debug.WriteLine($"   完成轮数: {CompletedPlayCount}");
                            //#endif
                            
                            // 滚动段：触发滚动请求
                            // 触发当前关键帧变化事件（滚动到目标关键帧）
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"    [CompositePlaybackService] 触发 CurrentKeyframeChanged 事件");
                            //#endif
                            CurrentKeyframeChanged?.Invoke(this, new CurrentKeyframeChangedEventArgs
                            {
                                KeyframeId = segment.KeyframeId,
                                YPosition = segment.EndPosition
                            });
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"    [CompositePlaybackService] 触发 ScrollRequested 事件");
                            //#endif
                            //  直接加速滚动动画，不改变时长
                            // 动画时长保持原始值，通过SpeedRatio加速动画播放
                            
                            #if DEBUG
            // System.Diagnostics.Debug.WriteLine($"    [滚动段] 原始时长: {segment.Duration:F2}秒, 速度: {Speed:F2}x (直接加速动画)");
                            #endif
                            
                            ScrollRequested?.Invoke(this, new CompositeScrollEventArgs
                            {
                                StartPosition = segment.StartPosition,
                                EndPosition = segment.EndPosition,
                                Duration = segment.Duration, // 使用原始时长
                                SpeedRatio = Speed // 传递速度倍率给动画
                            });
                            
                            // 触发进度更新（显示倒计时，使用与实际等待一致的时长）
                            double adjustedDuration = segment.Duration / Speed;
                            ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
                            {
                                CurrentIndex = CompletedPlayCount,
                                TotalCount = PlayCount == -1 ? 999 : PlayCount,
                                RemainingTime = adjustedDuration,
                                CurrentItemId = _currentImageId
                            });
                            
                            // 等待滚动完成（与倒计时一致）
                            await WaitWithSpeedAdjustment(segment.Duration, cancellationToken);
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
                            
                            //  停留段：直接加速等待时间
                            double adjustedDuration = segment.Duration / Speed;
                            
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"    [停留段] 原始时长: {segment.Duration:F2}秒, 速度: {Speed:F2}x, 调整后: {adjustedDuration:F2}秒");
                            #endif
                            
                            // 触发进度更新（显示倒计时，显示加速后的时间）
                            ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
                            {
                                CurrentIndex = CompletedPlayCount,
                                TotalCount = PlayCount == -1 ? 999 : PlayCount,
                                RemainingTime = adjustedDuration, // 显示加速后的剩余时间
                                CurrentItemId = _currentImageId
                            });
                            
                            // 等待停留时间（应用速度倍率）
                            await WaitWithSpeedAdjustment(segment.Duration, cancellationToken);
                        }
                    }

                    // 手动停止/取消时，直接退出循环，避免误触发“自动调整TOTAL时间”
                    if (!IsPlaying || cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // 完成一轮播放
                    CompletedPlayCount++;

                    // 业务规则：TOTAL 只允许由“下帧/PageDown 手动校准”覆盖
                    // 这里不做自动改写，避免停止/循环后意外覆盖用户设定值

                    // 如果还要继续，跳回起始位置
                    if (PlayCount == -1 || CompletedPlayCount < PlayCount)
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($" 新一轮播放开始，保持当前速度 {Speed:F2}x");
                        #endif
                        
                        // 触发跳回起始位置的事件
                        ScrollRequested?.Invoke(this, new CompositeScrollEventArgs
                        {
                            StartPosition = _startPosition,
                            EndPosition = _startPosition,
                            Duration = 0, // 时长为0表示直接跳转，不滚动
                            SpeedRatio = Speed
                        });
                        
                        await Task.Delay(100, cancellationToken); // 短暂延迟，让跳转完成
                    }
                }

                // 正常播放完成（非手动取消）才触发完成逻辑
                if (!cancellationToken.IsCancellationRequested && IsPlaying)
                {
                    await StopPlaybackAsync();
                    PlaybackCompleted?.Invoke(this, EventArgs.Empty);
                }
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
            _pauseStartTime = DateTime.Now;

            // 滚动段暂停时，立即停掉当前动画，恢复时按剩余时长重启
            if (_currentSegment != null && _currentSegment.Type == SegmentType.Scroll)
            {
                ScrollStopRequested?.Invoke(this, new CompositeScrollStopEventArgs { PreserveCountdown = true });
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 继续播放
        /// </summary>
        public Task ResumePlaybackAsync()
        {
            if (!IsPlaying || !IsPaused)
                return Task.CompletedTask;

            if (_pauseStartTime != default && _currentSegmentStartTime != default)
            {
                var pausedDuration = DateTime.Now - _pauseStartTime;
                _currentSegmentStartTime = _currentSegmentStartTime.Add(pausedDuration);
            }

            IsPaused = false;
            _pauseStartTime = default;

            // 如果当前是滚动段，恢复时从当前位置继续滚动剩余时长
            if (_currentSegment != null && _currentSegment.Type == SegmentType.Scroll)
            {
                double elapsedOriginalTime = GetCurrentSegmentElapsedOriginalTime();
                double remainingOriginalTime = Math.Max(0, _currentSegment.Duration - elapsedOriginalTime);

                if (remainingOriginalTime > 0.05)
                {
                    var positionArgs = new CurrentScrollPositionRequestEventArgs();
                    CurrentScrollPositionRequested?.Invoke(this, positionArgs);
                    double currentScrollPosition = positionArgs.CurrentScrollPosition;
                    double actualStartPosition = currentScrollPosition;

                    ScrollRequested?.Invoke(this, new CompositeScrollEventArgs
                    {
                        StartPosition = actualStartPosition,
                        EndPosition = _currentSegment.EndPosition,
                        Duration = remainingOriginalTime,
                        SpeedRatio = Speed
                    });
                }
            }
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
            ScrollStopRequested?.Invoke(this, new CompositeScrollStopEventArgs { PreserveCountdown = false });

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
            
            //  现在使用SpeedRatio直接加速动画，实际播放时间就是原始时间
            // 不需要转换，因为动画时长保持不变，只是播放速度加快
            double elapsedSecondsOriginal = elapsedSeconds;

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"⏱ 更新TOTAL时间: {elapsedSeconds:F2}秒 (调整后) -> {elapsedSecondsOriginal:F2}秒 (原始, 速度: {Speed:F2}x)");
            #endif

            // 如果时间太短（小于1秒），忽略
            if (elapsedSecondsOriginal < 1.0)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" 播放时间太短，忽略更新");
                #endif
                return;
            }

            // 停止当前播放
            await StopPlaybackAsync();

            // 更新CompositeScript的TOTAL时间（使用原始时间）
            await _compositeScriptRepository.CreateOrUpdateAsync(_currentImageId, elapsedSecondsOriginal, autoCalculate: false);

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($" 已更新TOTAL时间为 {elapsedSecondsOriginal:F2}秒 (考虑速度 {Speed:F2}x)，重新开始循环播放");
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
        /// 设置播放速度倍率
        /// </summary>
        /// <param name="speed">速度倍率（必须大于0，建议范围0.5-3.0）</param>
        public void SetSpeed(double speed)
        {
            if (speed <= 0)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" 无效的速度值: {speed}，必须大于0");
                #endif
                return;
            }

            // 限制速度范围（0.1x - 5.0x）
            speed = Math.Max(0.1, Math.Min(5.0, speed));

            if (Math.Abs(Speed - speed) < 0.01)
            {
                // 速度没有变化，不需要更新
                return;
            }

            double oldSpeed = Speed;
            Speed = speed;

            #if DEBUG
            // System.Diagnostics.Debug.WriteLine($" 播放速度已设置为: {Speed:F2}x (从 {oldSpeed:F2}x)");
            #endif

            // 如果正在播放且未暂停，当前段需要立即应用新速度
            // 暂停状态下不重算当前段，避免把暂停时长误算进已播放时间
            if (IsPlaying && !IsPaused && _currentSegment != null)
            {
                // 重新计算剩余时间并继续播放
                ApplySpeedToCurrentSegment();
            }

            // 触发速度变化事件
            SpeedChanged?.Invoke(this, new SpeedChangedEventArgs { Speed = Speed });
        }
        
        /// <summary>
        /// 将当前速度应用到正在播放的段（立即生效）
        /// </summary>
        private void ApplySpeedToCurrentSegment()
        {
            if (_currentSegment == null || !IsPlaying)
                return;
            
            try
            {
                // 计算已播放的时间（从段开始到现在）
                double elapsedAdjustedTime = (DateTime.Now - _currentSegmentStartTime).TotalSeconds;
                
                // 计算已播放的原始时间（包含历史切速累计）
                double elapsedOriginalTime = GetCurrentSegmentElapsedOriginalTime();
                
                // 计算剩余原始时间
                double remainingOriginalTime = Math.Max(0, _currentSegment.Duration - elapsedOriginalTime);
                
                // 使用新速度计算剩余调整后时间
                double remainingAdjustedTime = remainingOriginalTime / Speed;
                
                #if DEBUG
            // System.Diagnostics.Debug.WriteLine($"    [速度调整] 已播放调整: {elapsedAdjustedTime:F2}秒, 已播放原始: {elapsedOriginalTime:F2}秒");
            // System.Diagnostics.Debug.WriteLine($"      剩余原始: {remainingOriginalTime:F2}秒, 剩余调整: {remainingAdjustedTime:F2}秒 (新速度: {Speed:F2}x)");
                #endif
                
                if (_currentSegment.Type == SegmentType.Scroll && remainingAdjustedTime > 0.1)
                {
                    // 获取当前滚动位置（而不是使用段的起始位置）
                    var positionArgs = new CurrentScrollPositionRequestEventArgs();
                    CurrentScrollPositionRequested?.Invoke(this, positionArgs);
                    double currentScrollPosition = positionArgs.CurrentScrollPosition;
                    
                    // 如果获取不到当前位置，使用段的起始位置作为后备
                    if (currentScrollPosition <= 0 && _currentSegment.StartPosition > 0)
                    {
                        currentScrollPosition = _currentSegment.StartPosition;
                    }
                    
                    // 确保当前位置不超过目标位置
                    double actualStartPosition = Math.Min(currentScrollPosition, _currentSegment.EndPosition);
                    
                    #if DEBUG
            // System.Diagnostics.Debug.WriteLine($"    [速度调整] 当前滚动位置: {currentScrollPosition:F1}, 使用位置: {actualStartPosition:F1}, 目标位置: {_currentSegment.EndPosition:F1}");
                    #endif
                    
                    // 更新段开始时间和速度（重新开始计时）
                    _currentSegmentPlayedOriginalTime = elapsedOriginalTime;
                    _currentSegmentStartTime = DateTime.Now;
                    _currentSegmentSpeed = Speed;
                    
                    // 重新触发滚动（从当前位置到目标位置，使用原始剩余时间，通过SpeedRatio加速）
                    ScrollRequested?.Invoke(this, new CompositeScrollEventArgs
                    {
                        StartPosition = actualStartPosition, // 使用当前滚动位置
                        EndPosition = _currentSegment.EndPosition,
                        Duration = remainingOriginalTime, // 使用原始剩余时间
                        SpeedRatio = Speed // 传递速度倍率，直接加速动画
                    });
                    
                    // 更新进度（显示原始剩余时间）
                    ProgressUpdated?.Invoke(this, new PlaybackProgressEventArgs
                    {
                        CurrentIndex = CompletedPlayCount,
                        TotalCount = PlayCount == -1 ? 999 : PlayCount,
                        RemainingTime = remainingAdjustedTime, // 显示与实际等待一致的剩余时间
                        CurrentItemId = _currentImageId
                    });
                }
            }
            #if DEBUG
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" 应用速度到当前段失败: {ex.Message}");
            }
            #else
            catch (Exception)
            {
            }
            #endif
        }
        
        /// <summary>
        /// 带速度调整的等待（速度改变时可以立即响应）
        /// </summary>
        private async Task WaitWithSpeedAdjustment(double duration, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsPlaying)
                {
                    if (IsPaused)
                    {
                        await Task.Delay(50, cancellationToken);
                        continue;
                    }

                    if (_currentSegment != null)
                    {
                        // 计算已播放的原始时间（包含历史切速累计）
                        double elapsedOriginal = GetCurrentSegmentElapsedOriginalTime();
                        
                        // 计算剩余原始时间
                        double remainingOriginal = Math.Max(0, _currentSegment.Duration - elapsedOriginal);
                        
                        // 使用当前速度计算剩余调整后时间
                        double remainingAdjusted = remainingOriginal / Speed;
                        
                        if (remainingAdjusted <= 0.05)
                        {
                            // 剩余时间很少，直接完成
                            break;
                        }
                        
                        // 每次等待一小段时间（50ms），以便速度改变时能快速响应
                        double waitTime = Math.Min(0.05, remainingAdjusted);
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(waitTime), cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            // 等待被取消，这是正常的（播放停止或速度改变）
                            break;
                        }
                    }
                    else
                    {
                        // 如果没有当前段信息，使用简单的等待（不应该发生）
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(0.05), cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            // 等待被取消，正常退出
                            break;
                        }
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 等待被取消，这是正常的（播放停止）
            }
            #if DEBUG
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" WaitWithSpeedAdjustment 异常: {ex.Message}");
            }
            #else
            catch (Exception)
            {
            }
            #endif
        }

        // CheckAndAdjustTotalTimeAsync 已废弃：
        // 规则已改为仅允许下帧/PageDown手动覆盖TOTAL，不允许自动改写

        /// <summary>
        /// 计算当前段累计已播放的原始时长（秒）
        /// </summary>
        private double GetCurrentSegmentElapsedOriginalTime()
        {
            if (_currentSegmentStartTime == default)
            {
                return _currentSegmentPlayedOriginalTime;
            }

            double elapsedAdjusted = (DateTime.Now - _currentSegmentStartTime).TotalSeconds;
            double elapsedCurrentWindowOriginal = elapsedAdjusted * _currentSegmentSpeed;
            return _currentSegmentPlayedOriginalTime + elapsedCurrentWindowOriginal;
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
    
    /// <summary>动画速度倍率（默认1.0，用于直接加速滚动动画）</summary>
    public double SpeedRatio { get; set; } = 1.0;
    }

    /// <summary>
    /// 停止滚动请求参数
    /// </summary>
    public class CompositeScrollStopEventArgs : EventArgs
    {
        /// <summary>
        /// 是否保留倒计时（如切速时的瞬时停滚）
        /// </summary>
        public bool PreserveCountdown { get; set; }
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

    /// <summary>
    /// 播放速度变化事件参数
    /// </summary>
    public class SpeedChangedEventArgs : EventArgs
    {
        /// <summary>当前播放速度倍率</summary>
        public double Speed { get; set; }
    }
    
    /// <summary>
    /// 请求当前滚动位置事件参数
    /// </summary>
    public class CurrentScrollPositionRequestEventArgs : EventArgs
    {
        /// <summary>当前滚动位置（由MainWindow填充）</summary>
        public double CurrentScrollPosition { get; set; }
    }
}



