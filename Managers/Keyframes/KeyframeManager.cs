using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ImageColorChanger.Database.Models;
using ImageColorChanger.UI;

namespace ImageColorChanger.Managers.Keyframes
{
    /// <summary>
    /// 关键帧管理器
    /// 负责关键帧的增删改查、状态管理、缓存优化
    /// </summary>
    public class KeyframeManager
    {
        private readonly KeyframeRepository _repository;
        private readonly MainWindow _mainWindow;
        private KeyframeNavigator _navigator;
        private readonly Repositories.Interfaces.IMediaFileRepository _mediaFileRepository;

        #region 状态管理

        /// <summary>
        /// 当前关键帧索引（-1表示未选中）
        /// </summary>
        public int CurrentKeyframeIndex { get; set; } = -1;

        /// <summary>
        /// 上一个关键帧索引
        /// </summary>
        public int PreviousKeyframeIndex { get; private set; } = -1;

        /// <summary>
        /// 是否启用循环模式
        /// </summary>
        public bool IsLoopEnabled { get; set; } = true;

        /// <summary>
        /// 滚动动画时间（秒）
        /// </summary>
        public double ScrollDuration { get; set; } = 9.0;
        
        /// <summary>
        /// 滚动缓动类型（字符串，匹配Python版本）
        /// </summary>
        public string ScrollEasingType { get; set; } = "Bezier";
        
        /// <summary>
        /// 是否使用线性滚动（无缓动）
        /// </summary>
        public bool IsLinearScrolling { get; set; } = false;

        #endregion

        #region 缓存机制

        // 关键帧缓存（线程安全）
        private readonly ConcurrentDictionary<int, List<Keyframe>> _cache = new();
        // 缓存时间戳
        private readonly ConcurrentDictionary<int, DateTime> _cacheTimestamp = new();
        // 缓存生存时间（5秒）
        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(5);

        #endregion

        #region UI防抖

        private DateTime _lastUiUpdate = DateTime.MinValue;
        private readonly TimeSpan _uiUpdateDelay = TimeSpan.FromMilliseconds(20);
        private DispatcherTimer _uiUpdateTimer;
        private string _pendingUpdateType;

        #endregion

        #region 导航器

        /// <summary>
        /// 关键帧导航器
        /// </summary>
        public KeyframeNavigator Navigator => _navigator;

        #endregion

        #region 滚动动画状态

        /// <summary>
        /// 当前正在运行的滚动动画
        /// </summary>
        private System.Windows.Media.Animation.Storyboard _currentScrollAnimation;

        /// <summary>
        /// 是否正在滚动
        /// </summary>
        public bool IsScrolling => _currentScrollAnimation != null;

        /// <summary>
        /// 停止当前的滚动动画
        /// </summary>
        public void StopScrollAnimation()
        {
            if (_currentScrollAnimation != null)
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    var scrollViewer = _mainWindow.ImageScrollViewer;
                    if (scrollViewer != null)
                    {
                        // 获取当前滚动位置（动画进行中的位置）
                        var currentOffset = scrollViewer.VerticalOffset;
                        
                        // 停止动画并清除动画属性（关键！）
                        _currentScrollAnimation.Stop();
                        // 清除附加到ScrollViewer的动画属性，否则会影响后续的ScrollToVerticalOffset
                        scrollViewer.BeginAnimation(Utils.AnimationHelper.GetAnimatedVerticalOffsetProperty(), null);
                        _currentScrollAnimation = null;
                        
                        // 保持在当前位置（防止回退到初始位置）
                        scrollViewer.ScrollToVerticalOffset(currentOffset);
                        
                        // System.Diagnostics.Debug.WriteLine($"🛑 已停止滚动动画，保持在位置: {currentOffset:F0}");
                    }
                });
            }
        }

        #endregion

        /// <summary>
        /// 构造函数
        /// </summary>
        public KeyframeManager(
            KeyframeRepository repository, 
            MainWindow mainWindow,
            Repositories.Interfaces.IMediaFileRepository mediaFileRepository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));

            // 初始化导航器
            _navigator = new KeyframeNavigator(this, mainWindow, repository);

            // 初始化UI更新定时器
            InitializeUiUpdateTimer();
        }

        #region 关键帧操作

        /// <summary>
        /// 添加关键帧
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <param name="position">滚动位置（0.0-1.0）</param>
        /// <param name="yPosition">Y坐标位置（像素）</param>
        /// <returns>是否成功添加</returns>
        public async Task<bool> AddKeyframeAsync(int imageId, double position, int yPosition)
        {
            var result = await _repository.AddKeyframeAsync(imageId, position, yPosition);

            if (result > 0)
            {
                // 清除缓存
                ClearCache(imageId);

                // 更新预览线
                ScheduleUiUpdate("preview_lines");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取关键帧（带缓存）
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>关键帧列表</returns>
        /// <summary>
        /// 同步获取关键帧（仅从缓存），用于性能敏感的操作
        /// </summary>
        public List<Keyframe> GetKeyframesFromCache(int imageId)
        {
            if (_cache.TryGetValue(imageId, out var cachedKeyframes))
            {
                return cachedKeyframes;
            }
            return null;
        }

        /// <summary>
        /// 同步获取关键帧（带数据库加载和缓存）
        /// </summary>
        public List<Keyframe> GetKeyframes(int imageId)
        {
            // 检查缓存
            if (_cache.TryGetValue(imageId, out var cachedKeyframes) &&
                _cacheTimestamp.TryGetValue(imageId, out var timestamp))
            {
                if (DateTime.Now - timestamp < _cacheTtl)
                {
                    // 缓存命中
                    return cachedKeyframes;
                }
            }

            // 从数据库加载（同步）
            var keyframes = _repository.GetKeyframesByImageId(imageId);

            // 更新缓存
            _cache[imageId] = keyframes;
            _cacheTimestamp[imageId] = DateTime.Now;

            return keyframes;
        }

        public async Task<List<Keyframe>> GetKeyframesAsync(int imageId)
        {
            // 检查缓存
            if (_cache.TryGetValue(imageId, out var cachedKeyframes) &&
                _cacheTimestamp.TryGetValue(imageId, out var timestamp))
            {
                if (DateTime.Now - timestamp < _cacheTtl)
                {
                    // 缓存命中
                    return cachedKeyframes;
                }
            }

            // 从数据库加载
            var keyframes = await _repository.GetKeyframesAsync(imageId);

            // 更新缓存
            _cache[imageId] = keyframes;
            _cacheTimestamp[imageId] = DateTime.Now;

            return keyframes;
        }

        /// <summary>
        /// 删除关键帧
        /// </summary>
        /// <param name="keyframeId">关键帧ID</param>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否成功删除</returns>
        public async Task<bool> DeleteKeyframeAsync(int keyframeId, int imageId)
        {
            var success = await _repository.DeleteKeyframeAsync(keyframeId);

            if (success)
            {
                ClearCache(imageId);
                ScheduleUiUpdate("both");
            }

            return success;
        }

        /// <summary>
        /// 清除关键帧
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否成功清除</returns>
        public async Task<bool> ClearKeyframesAsync(int imageId)
        {
            var success = await _repository.ClearKeyframesAsync(imageId);

            if (success)
            {
                ClearCache(imageId);
                ScheduleUiUpdate("both");
            }

            return success;
        }

        #endregion

        #region 索引管理

        /// <summary>
        /// 更新关键帧索引
        /// </summary>
        /// <param name="newIndex">新的索引</param>
        public void UpdateKeyframeIndex(int newIndex)
        {
            PreviousKeyframeIndex = CurrentKeyframeIndex;
            CurrentKeyframeIndex = newIndex;
        }

        /// <summary>
        /// 检测是否是回跳（从较大索引跳到较小索引）
        /// </summary>
        /// <param name="targetIndex">目标索引</param>
        /// <returns>是否是回跳</returns>
        public bool IsBackwardJump(int targetIndex)
        {
            return CurrentKeyframeIndex >= 0 &&
                   targetIndex >= 0 &&
                   targetIndex < CurrentKeyframeIndex;
        }

        #endregion

        #region 平滑滚动

        /// <summary>
        /// 平滑滚动到目标位置
        /// </summary>
        /// <param name="targetPosition">目标位置（0.0-1.0）</param>
        public void SmoothScrollTo(double targetPosition)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                try
                {
                    var scrollViewer = _mainWindow.ImageScrollViewer;
                    if (scrollViewer == null) return;

                    var currentPosition = scrollViewer.VerticalOffset;
                    var scrollableHeight = scrollViewer.ScrollableHeight;
                    if (scrollableHeight == 0) return;

                    var targetOffset = targetPosition * scrollableHeight;

                    // 如果当前位置和目标位置相差很小，直接跳转
                    if (Math.Abs(currentPosition - targetOffset) < 1.0)
                    {
                        scrollViewer.ScrollToVerticalOffset(targetOffset);
                        return;
                    }

                    // 开始FPS监控
                    _mainWindow.StartFpsMonitoring();
                    
                    // 执行平滑滚动动画
                    _currentScrollAnimation = Utils.AnimationHelper.AnimateScroll(
                        scrollViewer,
                        currentPosition,
                        targetOffset,
                        TimeSpan.FromSeconds(ScrollDuration),
                        () =>
                        {
                            // 动画完成后清除引用
                            _currentScrollAnimation = null;
                            
                            // 更新投影
                            if (_mainWindow.IsProjectionEnabled)
                            {
                                _mainWindow.UpdateProjection();
                            }
                            
                            // 停止FPS监控
                            _mainWindow.StopFpsMonitoring();
                        },
                        ScrollEasingType,  // 使用配置的缓动类型
                        IsLinearScrolling   // 是否线性滚动
                    );
                }
                catch (Exception)
                {
                    // System.Diagnostics.Debug.WriteLine($"❌ 平滑滚动异常: {ex.Message}");
                }
            });
        }

        #endregion

        #region 缓存管理

        /// <summary>
        /// 清除缓存
        /// </summary>
        /// <param name="imageId">图片ID，null表示清除所有缓存</param>
        private void ClearCache(int? imageId = null)
        {
            if (imageId.HasValue)
            {
                _cache.TryRemove(imageId.Value, out _);
                _cacheTimestamp.TryRemove(imageId.Value, out _);
            }
            else
            {
                _cache.Clear();
                _cacheTimestamp.Clear();
            }
        }

        #endregion

        #region UI更新防抖

        /// <summary>
        /// 初始化UI更新定时器
        /// </summary>
        private void InitializeUiUpdateTimer()
        {
            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = _uiUpdateDelay
            };
            _uiUpdateTimer.Tick += (s, e) =>
            {
                _uiUpdateTimer.Stop();
                ExecuteUiUpdate(_pendingUpdateType);
            };
        }

        /// <summary>
        /// 安排UI更新
        /// </summary>
        /// <param name="updateType">更新类型：indicators/preview_lines/both</param>
        private void ScheduleUiUpdate(string updateType)
        {
            var now = DateTime.Now;

            if (now - _lastUiUpdate < _uiUpdateDelay)
            {
                _pendingUpdateType = updateType;

                if (!_uiUpdateTimer.IsEnabled)
                {
                    _uiUpdateTimer.Start();
                }
            }
            else
            {
                ExecuteUiUpdate(updateType);
            }
        }

        /// <summary>
        /// 执行UI更新
        /// </summary>
        /// <param name="updateType">更新类型</param>
        private void ExecuteUiUpdate(string updateType)
        {
            _lastUiUpdate = DateTime.Now;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                try
                {
                    switch (updateType)
                    {
                        case "indicators":
                            _mainWindow.UpdateKeyframeIndicators();
                            break;
                        case "preview_lines":
                            _mainWindow.UpdatePreviewLines();
                            break;
                        case "both":
                            _mainWindow.UpdateKeyframeIndicators();
                            _mainWindow.UpdatePreviewLines();
                            break;
                    }
                }
                catch (Exception)
                {
                    // Console.WriteLine($"❌ UI更新异常: {ex.Message}");
                }
            });
        }

        #endregion

        #region UI回调方法

        /// <summary>
        /// 更新关键帧指示器
        /// </summary>
        public async Task UpdateKeyframeIndicatorsAsync()
        {
            ScheduleUiUpdate("indicators");
            await Task.CompletedTask;
        }

        /// <summary>
        /// 更新预览线
        /// </summary>
        public void UpdatePreviewLines()
        {
            ScheduleUiUpdate("preview_lines");
        }

        #endregion

        #region 合成播放标记

        /// <summary>
        /// 获取图片的合成播放启用状态
        /// </summary>
        public async Task<bool> GetCompositePlaybackEnabledAsync(int imageId)
        {
            if (imageId <= 0) return false;

            try
            {
                var mediaFile = await _mediaFileRepository.GetByIdAsync(imageId);
                return mediaFile?.CompositePlaybackEnabled ?? false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 设置图片的合成播放启用状态
        /// </summary>
        public async Task<bool> SetCompositePlaybackEnabledAsync(int imageId, bool enabled)
        {
            if (imageId <= 0) return false;

            try
            {
                var mediaFile = await _mediaFileRepository.GetByIdAsync(imageId);
                if (mediaFile == null) return false;

                mediaFile.CompositePlaybackEnabled = enabled;
                await _mediaFileRepository.UpdateAsync(mediaFile);
                
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion
    }
}

