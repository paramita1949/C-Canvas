using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers.Keyframes;
using ImageColorChanger.Managers.Playback;
using MessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 关键帧功能扩展
    /// </summary>
    public partial class MainWindow
    {
        #region 关键帧和播放字段

        private KeyframeManager _keyframeManager;
        private KeyframeRepository _keyframeRepository;
        private TimeRecorder _timeRecorder;
        private AutoPlayer _autoPlayer;
        private PlaybackController _playbackController;
        
        // 滚动速度设置（默认8秒，与Python版本一致）
        private double _scrollDuration = 8.0;
        
        // 滚动缓动类型（默认贝塞尔曲线，与Python一致）
        private string _scrollEasingType = "Bezier";
        
        // 是否使用线性滚动（无缓动）
        private bool _isLinearScrolling = false;

        #endregion

        #region 关键帧初始化

        /// <summary>
        /// 初始化关键帧和播放系统
        /// </summary>
        private void InitializeKeyframeSystem()
        {
            try
            {
                // 获取数据库上下文
                var dbContext = dbManager?.GetDbContext();
                if (dbContext == null)
                {
                    Console.WriteLine("❌ 数据库上下文未就绪");
                    return;
                }

                // 创建关键帧仓库
                _keyframeRepository = new KeyframeRepository(dbContext);

                // 创建关键帧管理器
                _keyframeManager = new KeyframeManager(_keyframeRepository, this);
                
                // 从数据库加载滚动速度和缓动函数设置
                LoadScrollSpeedSettings();
                LoadScrollEasingSettings();
                
                // 初始化菜单选中状态
                Dispatcher.InvokeAsync(() =>
                {
                    UpdateScrollSpeedMenuCheck(_scrollDuration);
                    string easingName = _isLinearScrolling ? "Linear" : _scrollEasingType;
                    UpdateScrollEasingMenuCheck(easingName);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // 创建时间录制器
                _timeRecorder = new TimeRecorder(_keyframeRepository);

                // 创建自动播放器
                _autoPlayer = new AutoPlayer(this, _timeRecorder, _keyframeManager);

                // 创建播放控制器
                _playbackController = new PlaybackController(
                    this, _timeRecorder, _autoPlayer, _keyframeManager);

                // 订阅播放控制器事件
                _playbackController.RecordingStateChanged += OnRecordingStateChanged;
                _playbackController.PlayingStateChanged += OnPlayingStateChanged;
                _playbackController.PlayFinished += OnPlayFinished;

                System.Diagnostics.Debug.WriteLine("✅ 关键帧和播放系统初始化完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 关键帧系统初始化异常: {ex.Message}");
                MessageBox.Show($"关键帧系统初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 关键帧按钮事件

        /// <summary>
        /// 添加关键帧按钮点击事件
        /// </summary>
        private async void BtnAddKeyframe_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_keyframeManager == null)
            {
                ShowStatus("关键帧系统未初始化");
                return;
            }

            try
            {
                // 计算滚动位置
                double position = ImageScrollViewer.ScrollableHeight > 0
                    ? ImageScrollViewer.VerticalOffset / ImageScrollViewer.ScrollableHeight
                    : 0;
                int yPosition = (int)ImageScrollViewer.VerticalOffset;

                // 添加关键帧
                bool success = await _keyframeManager.AddKeyframeAsync(
                    currentImageId, position, yPosition);

                if (success)
                {
                    ShowStatus($"✅ 已添加关键帧");
                    
                    // 🔥 立即强制更新预览线（不等待防抖）
                    System.Diagnostics.Debug.WriteLine("🔥 BtnAddKeyframe: 强制更新预览线");
                    UpdatePreviewLines();
                    
                    // 如果正在录制，获取最新的关键帧ID并记录时间
                    if (_playbackController?.IsRecording == true)
                    {
                        var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                        if (keyframes != null && keyframes.Count > 0)
                        {
                            var lastKeyframe = keyframes.OrderByDescending(k => k.Id).FirstOrDefault();
                            if (lastKeyframe != null)
                            {
                                _playbackController.RecordKeyframeTime(lastKeyframe.Id);
                            }
                        }
                    }
                }
                else
                {
                    ShowStatus("❌ 添加关键帧失败（该位置附近已存在关键帧）");
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 添加关键帧出错: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"添加关键帧异常: {ex}");
            }
        }

        /// <summary>
        /// 清除关键帧按钮点击事件
        /// </summary>
        private async void BtnClearKeyframes_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_keyframeManager == null)
            {
                ShowStatus("关键帧系统未初始化");
                return;
            }

            var result = MessageBox.Show(
                "确定要清除当前图片的所有关键帧吗？\n此操作不可撤销。",
                "确认清除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _keyframeManager.ClearKeyframesAsync(currentImageId);
                    ShowStatus("✅ 已清除所有关键帧");
                    
                    // 🔥 立即强制更新预览线（清除显示）
                    System.Diagnostics.Debug.WriteLine("🔥 BtnClearKeyframes: 强制更新预览线");
                    UpdatePreviewLines();
                }
                catch (Exception ex)
                {
                    ShowStatus($"❌ 清除关键帧出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 上一个关键帧按钮点击事件
        /// </summary>
        private async void BtnPrevKeyframe_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_keyframeManager == null)
            {
                ShowStatus("关键帧系统未初始化");
                return;
            }

            _keyframeManager.Navigator.StepToPrevKeyframe();
            
            // 如果正在录制，记录时间
            if (_playbackController?.IsRecording == true && _keyframeManager.CurrentKeyframeIndex >= 0)
            {
                var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                {
                    var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                    _playbackController.RecordKeyframeTime(currentKeyframe.Id);
                }
            }
        }

        /// <summary>
        /// 下一个关键帧按钮点击事件
        /// </summary>
        private async void BtnNextKeyframe_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_keyframeManager == null)
            {
                ShowStatus("关键帧系统未初始化");
                return;
            }

            _keyframeManager.Navigator.StepToNextKeyframe();
            
            // 如果正在录制，记录时间
            if (_playbackController?.IsRecording == true && _keyframeManager.CurrentKeyframeIndex >= 0)
            {
                var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                {
                    var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                    _playbackController.RecordKeyframeTime(currentKeyframe.Id);
                }
            }
        }

        #endregion

        #region 播放按钮事件

        /// <summary>
        /// 录制时间按钮点击事件
        /// </summary>
        private async void BtnRecordTiming_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_playbackController == null)
            {
                ShowStatus("播放系统未初始化");
                return;
            }

            await _playbackController.ToggleTimingRecordingAsync(currentImageId);
        }

        /// <summary>
        /// 自动播放按钮点击事件
        /// </summary>
        private async void BtnAutoPlay_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_playbackController == null)
            {
                ShowStatus("播放系统未初始化");
                return;
            }

            await _playbackController.ToggleAutoPlayAsync(currentImageId);
        }

        /// <summary>
        /// 暂停/继续按钮点击事件
        /// </summary>
        private async void BtnPausePlay_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackController == null)
            {
                ShowStatus("播放系统未初始化");
                return;
            }

            await _playbackController.ToggleCountdownPauseAsync();
        }

        /// <summary>
        /// 清除时间数据按钮点击事件
        /// </summary>
        private async void BtnClearTiming_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_playbackController == null)
            {
                ShowStatus("播放系统未初始化");
                return;
            }

            var result = MessageBox.Show(
                "确定要清除当前图片的时间数据吗？\n此操作不可撤销。",
                "确认清除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await _playbackController.ClearTimingDataAsync(currentImageId);
            }
        }

        /// <summary>
        /// 显示脚本信息按钮点击事件
        /// </summary>
        private async void BtnShowScript_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_playbackController == null)
            {
                ShowStatus("播放系统未初始化");
                return;
            }

            try
            {
                var scriptInfo = await _playbackController.GetFormattedScriptInfoAsync(currentImageId);
                MessageBox.Show(scriptInfo, "脚本信息", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 获取脚本信息出错: {ex.Message}");
            }
        }

        #endregion

        #region 播放事件处理

        /// <summary>
        /// 录制状态改变事件处理
        /// </summary>
        private void OnRecordingStateChanged(object sender, bool isRecording)
        {
            Dispatcher.Invoke(() =>
            {
                // TODO: 更新录制按钮状态
                // BtnRecordTiming.Content = isRecording ? "⏹️ 停止录制" : "🔴 开始录制";
                // BtnRecordTiming.Background = isRecording ? Brushes.Red : Brushes.Gray;
                
                ShowStatus(isRecording ? "🔴 正在录制时间..." : "⏹️ 录制已停止");
            });
        }

        /// <summary>
        /// 播放状态改变事件处理
        /// </summary>
        private void OnPlayingStateChanged(object sender, bool isPlaying)
        {
            Dispatcher.Invoke(() =>
            {
                // TODO: 更新播放按钮状态
                // BtnAutoPlay.Content = isPlaying ? "⏹️ 停止播放" : "▶️ 自动播放";
                // BtnPausePlay.IsEnabled = isPlaying;
                
                ShowStatus(isPlaying ? "▶️ 正在播放..." : "⏹️ 播放已停止");
            });
        }

        /// <summary>
        /// 播放完成事件处理
        /// </summary>
        private void OnPlayFinished(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ShowStatus($"✅ 播放完成 - 共播放 {_playbackController?.CompletedPlayCount ?? 0} 次");
            });
        }

        #endregion

        #region 关键帧辅助方法

        /// <summary>
        /// 获取当前图片ID
        /// </summary>
        public int GetCurrentImageId()
        {
            return currentImageId;
        }

        /// <summary>
        /// 投影是否启用
        /// </summary>
        public bool IsProjectionEnabled => projectionManager?.IsProjectionActive ?? false;

        /// <summary>
        /// 更新关键帧指示器
        /// </summary>
        public void UpdateKeyframeIndicators()
        {
            // TODO: 实现关键帧指示器UI更新
            // 在图片上显示关键帧标记点
        }

        /// <summary>
        /// 更新预览线（图片上的横线 + 滚动条旁的方块）
        /// </summary>
        public void UpdatePreviewLines()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // 清空之前的预览线和指示块
                    KeyframePreviewLinesCanvas.Children.Clear();
                    ScrollbarIndicatorsCanvas.Children.Clear();
                    
                    // 获取当前图片ID
                    int currentImageId = GetCurrentImageId();
                    System.Diagnostics.Debug.WriteLine($"🎨 UpdatePreviewLines: currentImageId={currentImageId}");
                    
                    if (currentImageId <= 0) 
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ 无效的图片ID");
                        return;
                    }

                    // 获取当前图片的所有关键帧
                    var keyframes = _keyframeManager.GetKeyframesAsync(currentImageId).Result;
                    System.Diagnostics.Debug.WriteLine($"🎨 获取到 {keyframes?.Count ?? 0} 个关键帧");
                    
                    if (keyframes == null || !keyframes.Any()) 
                    {
                        System.Diagnostics.Debug.WriteLine("ℹ️ 没有关键帧，清空预览线");
                        return;
                    }

                    // 获取尺寸信息
                    double imageCanvasWidth = KeyframePreviewLinesCanvas.ActualWidth;
                    double imageCanvasHeight = KeyframePreviewLinesCanvas.ActualHeight;
                    double scrollbarCanvasHeight = ScrollbarIndicatorsCanvas.ActualHeight;
                    double viewportHeight = ImageScrollViewer.ViewportHeight;
                    
                    System.Diagnostics.Debug.WriteLine($"📐 图片Canvas尺寸: {imageCanvasWidth}x{imageCanvasHeight}");
                    System.Diagnostics.Debug.WriteLine($"📐 滚动条Canvas高度: {scrollbarCanvasHeight}");
                    System.Diagnostics.Debug.WriteLine($"📐 视口高度: {viewportHeight}");
                    
                    if (imageCanvasHeight <= 0 || scrollbarCanvasHeight <= 0) 
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ 尺寸无效，跳过绘制");
                        return;
                    }

                    // 计算滚动范围
                    double scrollableHeight = Math.Max(0, imageCanvasHeight - viewportHeight);
                    System.Diagnostics.Debug.WriteLine($"📐 可滚动高度: {scrollableHeight}");

                    // 绘制每个关键帧
                    for (int i = 0; i < keyframes.Count; i++)
                    {
                        var keyframe = keyframes[i];
                        
                        System.Diagnostics.Debug.WriteLine($"  🖍️ 关键帧{i+1}: YPos={keyframe.YPosition}");

                        // 1. 在图片上绘制横线（红色虚线）
                        var previewLine = new System.Windows.Shapes.Line
                        {
                            X1 = 0,
                            X2 = imageCanvasWidth,
                            Y1 = keyframe.YPosition,
                            Y2 = keyframe.YPosition,
                            Stroke = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromArgb(200, 255, 0, 0)), // 半透明红色
                            StrokeThickness = 2,
                            StrokeDashArray = new System.Windows.Media.DoubleCollection { 10, 5 }, // 虚线
                            Opacity = 0.8
                        };
                        KeyframePreviewLinesCanvas.Children.Add(previewLine);

                        // 2. 在滚动条旁边绘制位置提示方块
                        if (scrollableHeight > 0)
                        {
                            double relativePosition = keyframe.YPosition / imageCanvasHeight;
                            double indicatorY = relativePosition * scrollbarCanvasHeight;

                            var indicator = new System.Windows.Shapes.Rectangle
                            {
                                Width = 8,
                                Height = 8,  // 正方形
                                Fill = new System.Windows.Media.SolidColorBrush(
                                    System.Windows.Media.Color.FromRgb(255, 0, 0)), // 红色
                                Stroke = new System.Windows.Media.SolidColorBrush(
                                    System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色边框
                                StrokeThickness = 1,
                                RadiusX = 1,
                                RadiusY = 1,
                                Opacity = 0.9,
                                Cursor = System.Windows.Input.Cursors.Hand
                            };

                            Canvas.SetTop(indicator, indicatorY - 4); // 居中
                            Canvas.SetLeft(indicator, 1);
                            ScrollbarIndicatorsCanvas.Children.Add(indicator);
                            
                            System.Diagnostics.Debug.WriteLine($"    - 图片横线Y={keyframe.YPosition}, 滚动条方块Y={indicatorY:F0}");
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ 已绘制 {keyframes.Count} 条预览线和 {ScrollbarIndicatorsCanvas.Children.Count} 个滚动条指示块");

                    // 绘制当前关键帧的绿色高亮指示器
                    DrawCurrentKeyframeIndicator(scrollbarCanvasHeight, imageCanvasHeight);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 更新预览线失败: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 绘制当前关键帧的绿色高亮指示器（在滚动条旁边）
        /// </summary>
        private void DrawCurrentKeyframeIndicator(double scrollbarCanvasHeight, double imageCanvasHeight)
        {
            try
            {
                int currentImageId = GetCurrentImageId();
                if (currentImageId <= 0 || _keyframeManager.CurrentKeyframeIndex < 0)
                {
                    return;
                }

                var keyframes = _keyframeManager.GetKeyframesAsync(currentImageId).Result;
                if (keyframes == null || _keyframeManager.CurrentKeyframeIndex >= keyframes.Count)
                {
                    return;
                }

                // 获取当前关键帧
                var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                
                // 计算在滚动条Canvas上的位置
                double relativePosition = currentKeyframe.YPosition / imageCanvasHeight;
                double indicatorY = relativePosition * scrollbarCanvasHeight;

                // 创建绿色高亮指示块（稍大的正方形）
                var currentIndicator = new System.Windows.Shapes.Rectangle
                {
                    Width = 9,
                    Height = 9,  // 正方形，稍大
                    Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0, 255, 0)), // 鲜绿色
                    Stroke = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色边框
                    StrokeThickness = 1.5,
                    RadiusX = 1,
                    RadiusY = 1,
                    Opacity = 1.0,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                Canvas.SetTop(currentIndicator, indicatorY - 4.5); // 居中
                Canvas.SetLeft(currentIndicator, 0.5);
                ScrollbarIndicatorsCanvas.Children.Add(currentIndicator);
                
                System.Diagnostics.Debug.WriteLine($"🟢 当前关键帧绿色指示块: 位于Y={indicatorY:F0}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 绘制当前关键帧指示块失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新倒计时显示
        /// </summary>
        /// <param name="remainingSeconds">剩余秒数，-1表示隐藏</param>
        public void UpdateCountdownDisplay(double remainingSeconds)
        {
            Dispatcher.Invoke(() =>
            {
                // TODO: 实现倒计时显示更新
                // 例如更新某个TextBlock的Text属性
                // CountdownText.Text = remainingSeconds >= 0 ? $"倒: {remainingSeconds:F1}秒" : "倒: --";
            });
        }

        /// <summary>
        /// 更新暂停倒计时显示
        /// </summary>
        /// <param name="pauseTime">暂停时间（秒）</param>
        /// <param name="remainingTime">剩余时间（秒）</param>
        /// <param name="playedTime">已播放时间（秒）</param>
        public void UpdatePauseCountdownDisplay(double pauseTime, double remainingTime, double playedTime)
        {
            Dispatcher.Invoke(() =>
            {
                // TODO: 实现暂停倒计时显示更新
                // 例如：CountdownText.Text = $"暂停{pauseTime:F1}s | 剩余{remainingTime:F1}s | 已播{playedTime:F1}s";
            });
        }

        #endregion

        #region 关键帧指示块点击跳转

        /// <summary>
        /// 点击关键帧指示块跳转
        /// </summary>
        private async void ScrollbarIndicatorsCanvas_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                int currentImageId = GetCurrentImageId();
                if (currentImageId <= 0 || _keyframeManager == null)
                {
                    return;
                }

                // 获取点击位置
                var clickPoint = e.GetPosition(ScrollbarIndicatorsCanvas);
                double clickY = clickPoint.Y;
                double canvasHeight = ScrollbarIndicatorsCanvas.ActualHeight;

                System.Diagnostics.Debug.WriteLine($"🖱️ 点击指示块区域: Y={clickY:F0}, Canvas高度={canvasHeight:F0}");

                // 获取所有关键帧
                var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                if (keyframes == null || !keyframes.Any())
                {
                    return;
                }

                // 获取图片Canvas高度
                double imageCanvasHeight = KeyframePreviewLinesCanvas.ActualHeight;
                if (imageCanvasHeight <= 0)
                {
                    return;
                }

                // 查找最接近点击位置的关键帧
                int closestIndex = -1;
                double minDistance = double.MaxValue;

                for (int i = 0; i < keyframes.Count; i++)
                {
                    var keyframe = keyframes[i];
                    double relativePosition = keyframe.YPosition / imageCanvasHeight;
                    double indicatorY = relativePosition * canvasHeight;
                    double distance = Math.Abs(indicatorY - clickY);

                    if (distance < minDistance && distance < 20) // 20px容差
                    {
                        minDistance = distance;
                        closestIndex = i;
                    }
                }

                if (closestIndex >= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ 点击跳转到关键帧 #{closestIndex + 1}");

                    // 更新当前关键帧索引
                    _keyframeManager.UpdateKeyframeIndex(closestIndex);

                    // 滚动到目标关键帧
                    var targetKeyframe = keyframes[closestIndex];
                    double scrollableHeight = Math.Max(0, imageCanvasHeight - ImageScrollViewer.ViewportHeight);
                    
                    if (scrollableHeight > 0)
                    {
                        double targetOffset = targetKeyframe.YPosition;
                        ImageScrollViewer.ScrollToVerticalOffset(targetOffset);
                        
                        ShowStatus($"🎯 跳转到关键帧 #{closestIndex + 1}/{keyframes.Count}");
                        
                        // 刷新预览线显示
                        UpdatePreviewLines();
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 点击位置没有关键帧");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 点击跳转失败: {ex.Message}");
            }
        }

        #endregion
        
        #region 滚动速度设置
        
        /// <summary>
        /// 从数据库加载滚动速度设置
        /// </summary>
        private void LoadScrollSpeedSettings()
        {
            try
            {
                var dbContext = dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "scroll_speed");
                if (setting != null && double.TryParse(setting.Value, out double speed))
                {
                    _scrollDuration = speed;
                    if (_keyframeManager != null)
                    {
                        _keyframeManager.ScrollDuration = speed;
                        _keyframeManager.ScrollEasingType = _scrollEasingType;
                        _keyframeManager.IsLinearScrolling = _isLinearScrolling;
                    }
                    System.Diagnostics.Debug.WriteLine($"✅ 已加载滚动速度: {_scrollDuration}秒");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ 使用默认滚动速度: 8.0秒");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 加载滚动速度失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 保存滚动速度设置到数据库
        /// </summary>
        private void SaveScrollSpeedSettings()
        {
            try
            {
                var dbContext = dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "scroll_speed");
                if (setting != null)
                {
                    setting.Value = _scrollDuration.ToString();
                }
                else
                {
                    dbContext.Settings.Add(new Setting
                    {
                        Key = "scroll_speed",
                        Value = _scrollDuration.ToString()
                    });
                }
                
                dbContext.SaveChanges();
                System.Diagnostics.Debug.WriteLine($"✅ 已保存滚动速度: {_scrollDuration}秒");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 保存滚动速度失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 设置滚动速度（右键菜单点击事件）
        /// </summary>
        private void SetScrollSpeed_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Tag != null)
                {
                    if (double.TryParse(menuItem.Tag.ToString(), out double speed))
                    {
                        _scrollDuration = speed;
                        
                        // 更新KeyframeManager的滚动时长
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.ScrollDuration = speed;
                            _keyframeManager.ScrollEasingType = _scrollEasingType;
                            _keyframeManager.IsLinearScrolling = _isLinearScrolling;
                        }
                        
                        // 更新菜单选中状态
                        UpdateScrollSpeedMenuCheck(speed);
                        
                        // 保存到数据库
                        SaveScrollSpeedSettings();
                        
                        ShowStatus($"✅ 滚动速度已设置为 {speed}秒");
                        System.Diagnostics.Debug.WriteLine($"📊 滚动速度更新: {speed}秒");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 设置滚动速度失败: {ex.Message}");
                ShowStatus($"❌ 设置滚动速度失败");
            }
        }
        
        /// <summary>
        /// 更新滚动速度菜单的选中状态
        /// </summary>
        private void UpdateScrollSpeedMenuCheck(double currentSpeed)
        {
            try
            {
                var contextMenu = ImageScrollViewer.ContextMenu;
                if (contextMenu == null) return;
                
                foreach (var item in contextMenu.Items)
                {
                    if (item is MenuItem parentMenu && parentMenu.Header.ToString() == "滚动速度")
                    {
                        foreach (var subItem in parentMenu.Items)
                        {
                            if (subItem is MenuItem subMenu)
                            {
                                if (subMenu.Tag != null && double.TryParse(subMenu.Tag.ToString(), out double speed))
                                {
                                    subMenu.IsChecked = Math.Abs(speed - currentSpeed) < 0.01;
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 更新菜单选中状态失败: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 滚动函数设置
        
        /// <summary>
        /// 设置滚动缓动函数（右键菜单点击事件）
        /// </summary>
        private void SetScrollEasing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Tag != null)
                {
                    string easingName = menuItem.Tag.ToString();
                    
                    // 处理线性滚动（无缓动）
                    if (easingName == "Linear")
                    {
                        _isLinearScrolling = true;
                        
                        // 更新KeyframeManager
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.IsLinearScrolling = true;
                        }
                        
                        ShowStatus($"✅ 滚动函数已设置为 线性滚动");
                    }
                    else
                    {
                        // 其他缓动类型
                        _isLinearScrolling = false;
                        _scrollEasingType = easingName;
                        
                        // 更新KeyframeManager
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.IsLinearScrolling = false;
                            _keyframeManager.ScrollEasingType = easingName;
                        }
                        
                        ShowStatus($"✅ 滚动函数已设置为 {GetEasingDisplayName(easingName)}");
                    }
                    
                    // 更新菜单选中状态
                    UpdateScrollEasingMenuCheck(easingName);
                    
                    // 保存到数据库
                    SaveScrollEasingSettings();
                    
                    System.Diagnostics.Debug.WriteLine($"📊 滚动函数更新: {easingName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 设置滚动函数失败: {ex.Message}");
                ShowStatus($"❌ 设置滚动函数失败");
            }
        }
        
        /// <summary>
        /// 获取缓动函数显示名称
        /// </summary>
        private string GetEasingDisplayName(string easingName)
        {
            return easingName switch
            {
                "Linear" => "线性滚动",
                "OptimizedCubic" => "优化三次",
                "EaseOutExpo" => "快速启动",
                "Bezier" => "贝塞尔曲线",
                "CssEaseInOut" => "CSS缓入缓出",
                _ => easingName
            };
        }
        
        /// <summary>
        /// 更新滚动函数菜单的选中状态
        /// </summary>
        private void UpdateScrollEasingMenuCheck(string currentEasing)
        {
            try
            {
                var contextMenu = ImageScrollViewer.ContextMenu;
                if (contextMenu == null) return;
                
                foreach (var item in contextMenu.Items)
                {
                    if (item is MenuItem parentMenu && parentMenu.Header.ToString() == "滚动函数")
                    {
                        foreach (var subItem in parentMenu.Items)
                        {
                            if (subItem is MenuItem subMenu && subMenu.Tag != null)
                            {
                                subMenu.IsChecked = subMenu.Tag.ToString() == currentEasing;
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 更新菜单选中状态失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 保存滚动函数设置到数据库
        /// </summary>
        private void SaveScrollEasingSettings()
        {
            try
            {
                var dbContext = dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                // 保存缓动类型或"Linear"
                string easingValue = _isLinearScrolling ? "Linear" : _scrollEasingType;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "scroll_easing");
                if (setting != null)
                {
                    setting.Value = easingValue;
                }
                else
                {
                    dbContext.Settings.Add(new Setting
                    {
                        Key = "scroll_easing",
                        Value = easingValue
                    });
                }
                
                dbContext.SaveChanges();
                System.Diagnostics.Debug.WriteLine($"✅ 已保存滚动函数: {easingValue}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 保存滚动函数失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 从数据库加载滚动函数设置
        /// </summary>
        private void LoadScrollEasingSettings()
        {
            try
            {
                var dbContext = dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "scroll_easing");
                if (setting != null)
                {
                    // 检查是否是线性滚动
                    if (setting.Value == "Linear")
                    {
                        _isLinearScrolling = true;
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.IsLinearScrolling = true;
                        }
                        System.Diagnostics.Debug.WriteLine($"✅ 已加载滚动函数: Linear");
                    }
                    else
                    {
                        _isLinearScrolling = false;
                        _scrollEasingType = setting.Value;
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.IsLinearScrolling = false;
                            _keyframeManager.ScrollEasingType = setting.Value;
                        }
                        System.Diagnostics.Debug.WriteLine($"✅ 已加载滚动函数: {_scrollEasingType}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ 使用默认滚动函数: Bezier");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 加载滚动函数失败: {ex.Message}");
            }
        }
        
        #endregion
    }
}

