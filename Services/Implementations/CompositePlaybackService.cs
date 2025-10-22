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
    /// 根据关键帧总时长，从第一帧平滑滚动到最后一帧，然后循环
    /// </summary>
    public class CompositePlaybackService : IPlaybackService
    {
        private readonly ITimingRepository _timingRepository;
        private readonly IKeyframeRepository _keyframeRepository;
        private CancellationTokenSource _cancellationTokenSource;

        private int _currentImageId;
        private double _totalDuration; // 总时长（秒）
        private double _startPosition; // 起始Y坐标
        private double _endPosition;   // 结束Y坐标
        private System.Collections.Generic.List<PlaybackSegment> _playbackSegments; // 播放段列表

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

        public CompositePlaybackService(
            ITimingRepository timingRepository,
            IKeyframeRepository keyframeRepository)
        {
            _timingRepository = timingRepository ?? throw new ArgumentNullException(nameof(timingRepository));
            _keyframeRepository = keyframeRepository ?? throw new ArgumentNullException(nameof(keyframeRepository));
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

            // 1. 获取所有关键帧（用于获取起始和结束位置）
            var keyframes = await _keyframeRepository.GetKeyframesByImageIdAsync(imageId);
            if (keyframes == null || keyframes.Count < 2)
            {
                return;
            }

            // 2. 获取时间序列（用于计算总时长）
            var timingSequence = await _timingRepository.GetTimingSequenceAsync(imageId);
            if (timingSequence == null || !timingSequence.Any())
            {
                return;
            }

            // 3. 计算总时长
            _totalDuration = timingSequence.Sum(t => t.Duration);

            // 4. 获取起始和结束位置
            var orderedKeyframes = keyframes.OrderBy(k => k.OrderIndex).ToList();
            _startPosition = orderedKeyframes.First().YPosition;
            _endPosition = orderedKeyframes.Last().YPosition;

            // 5. 构建播放段（考虑循环标记）
            _playbackSegments = BuildPlaybackSegments(orderedKeyframes, timingSequence.ToList());

            IsPlaying = true;
            IsPaused = false;
            CompletedPlayCount = 0;
            _cancellationTokenSource = new CancellationTokenSource();

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

            // 触发停止滚动事件，立即停止当前正在进行的滚动动画
            ScrollStopRequested?.Invoke(this, EventArgs.Empty);

            return Task.CompletedTask;
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
}

