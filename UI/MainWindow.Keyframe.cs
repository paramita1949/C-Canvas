using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers.Keyframes;
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
        private KeyframeRepository _keyframeRepository; // 仅供 KeyframeManager 使用
        
        // 滚动速度设置（默认8秒，与Python版本一致）
        private double _scrollDuration = 8.0;
        
        // 滚动缓动类型（默认贝塞尔曲线，与Python一致）
        private string _scrollEasingType = "Bezier";
        
        // 是否使用线性滚动（无缓动）
        private bool _isLinearScrolling = false;

        #endregion

        #region 关键帧状态管理

        /// <summary>
        /// 重置关键帧索引到-1（图片加载时调用）
        /// 参考Python版本：image_processor.py 第341行
        /// </summary>
        public void ResetKeyframeIndex()
        {
            if (_keyframeManager != null)
            {
                _keyframeManager.CurrentKeyframeIndex = -1;
                // System.Diagnostics.Debug.WriteLine("🔄 [图片加载] 重置关键帧索引为-1");
                
                // 更新关键帧预览线和指示块
                _keyframeManager?.UpdatePreviewLines();
            }
        }

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
                    // Console.WriteLine("❌ 数据库上下文未就绪");
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

                // System.Diagnostics.Debug.WriteLine("✅ 关键帧和播放系统初始化完成");
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 关键帧系统初始化异常: {ex.Message}");
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
                    UpdatePreviewLines();
                    
                    // 如果正在录制，获取最新的关键帧ID并记录时间（使用新架构）
                    if (_playbackViewModel?.IsRecording == true)
                    {
                        var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                        if (keyframes != null && keyframes.Count > 0)
                        {
                            var lastKeyframe = keyframes.OrderByDescending(k => k.Id).FirstOrDefault();
                            if (lastKeyframe != null)
                            {
                                await _playbackViewModel.RecordKeyframeTimeAsync(lastKeyframe.Id);
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
                // System.Diagnostics.Debug.WriteLine($"添加关键帧异常: {ex}");
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
                    UpdatePreviewLines();
                }
                catch (Exception ex)
                {
                    ShowStatus($"❌ 清除关键帧出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 上一个关键帧/上一张图/上一个媒体按钮点击事件（三模式支持）
        /// </summary>
        private async void BtnPrevKeyframe_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            // 🎯 模式1：媒体播放模式（视频/音频）
            if (IsMediaPlaybackMode())
            {
                await SwitchToPreviousMediaFile();
                return;
            }

            // 🎯 模式2：原图标记模式（切换相似图片）
            if (IsOriginalMarkMode())
            {
                SwitchToPreviousSimilarImage();
                return;
            }

            // 🎯 模式3：关键帧模式（默认）
            if (_keyframeManager == null)
            {
                ShowStatus("关键帧系统未初始化");
                return;
            }

            // 如果正在录制，先记录当前帧的时间（跳转前）
            if (_playbackViewModel?.IsRecording == true && _keyframeManager.CurrentKeyframeIndex >= 0)
            {
                var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                {
                    var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                    await _playbackViewModel.RecordKeyframeTimeAsync(currentKeyframe.Id);
                    System.Diagnostics.Debug.WriteLine($"📝 [录制] 离开关键帧 #{_keyframeManager.CurrentKeyframeIndex + 1}，记录停留时间");
                }
            }
            
            // 如果正在播放，记录手动操作用于实时修正（参考Python版本：keytime.py 第750-786行）
            if (_playbackViewModel?.IsPlaying == true && _keyframeManager.CurrentKeyframeIndex >= 0)
            {
                var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                {
                    var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                    // 调用播放服务的手动修正方法
                    var playbackService = App.GetRequiredService<Services.PlaybackServiceFactory>()
                        .GetPlaybackService(Database.Models.Enums.PlaybackMode.Keyframe);
                    if (playbackService is Services.Implementations.KeyframePlaybackService kfService)
                    {
                        await kfService.RecordManualOperationAsync(currentKeyframe.Id);
                        System.Diagnostics.Debug.WriteLine($"🕐 [播放修正] 记录手动跳转: 帧#{_keyframeManager.CurrentKeyframeIndex + 1}");
                        
                        // 跳过当前等待，立即播放下一帧（参考Python版本：keyframe_navigation.py 第157-167行）
                        kfService.SkipCurrentWaitAndPlayNext();
                    }
                }
            }
            
            // 然后执行跳转
            _keyframeManager.Navigator.StepToPrevKeyframe();
        }

        /// <summary>
        /// 下一个关键帧/下一张图/下一个媒体按钮点击事件（三模式支持）
        /// </summary>
        private async void BtnNextKeyframe_Click(object sender, RoutedEventArgs e)
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            // 🎯 模式1：媒体播放模式（视频/音频）
            if (IsMediaPlaybackMode())
            {
                await SwitchToNextMediaFile();
                return;
            }

            // 🎯 模式2：原图标记模式（切换相似图片）
            if (IsOriginalMarkMode())
            {
                SwitchToNextSimilarImage();
                return;
            }

            // 🎯 模式3：关键帧模式（默认）
            if (_keyframeManager == null)
            {
                ShowStatus("关键帧系统未初始化");
                return;
            }

            // 如果正在录制，先记录当前帧的时间（跳转前）
            if (_playbackViewModel?.IsRecording == true && _keyframeManager.CurrentKeyframeIndex >= 0)
            {
                var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                {
                    var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                    await _playbackViewModel.RecordKeyframeTimeAsync(currentKeyframe.Id);
                    System.Diagnostics.Debug.WriteLine($"📝 [录制] 离开关键帧 #{_keyframeManager.CurrentKeyframeIndex + 1}，记录停留时间");
                }
            }
            
            // 如果正在播放，记录手动操作用于实时修正（参考Python版本：keytime.py 第750-786行）
            if (_playbackViewModel?.IsPlaying == true && _keyframeManager.CurrentKeyframeIndex >= 0)
            {
                var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                {
                    var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                    // 调用播放服务的手动修正方法
                    var playbackService = App.GetRequiredService<Services.PlaybackServiceFactory>()
                        .GetPlaybackService(Database.Models.Enums.PlaybackMode.Keyframe);
                    if (playbackService is Services.Implementations.KeyframePlaybackService kfService)
                    {
                        await kfService.RecordManualOperationAsync(currentKeyframe.Id);
                        System.Diagnostics.Debug.WriteLine($"🕐 [播放修正] 记录手动跳转: 帧#{_keyframeManager.CurrentKeyframeIndex + 1}");
                        
                        // 跳过当前等待，立即播放下一帧（参考Python版本：keyframe_navigation.py 第157-167行）
                        kfService.SkipCurrentWaitAndPlayNext();
                    }
                }
            }
            
            // 然后执行跳转
            bool shouldRecordTime = await _keyframeManager.Navigator.StepToNextKeyframe();
            
            // shouldRecordTime 用于控制循环停止录制后是否继续记录（通常是false）
        }

        #endregion

        #region 播放按钮事件

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

            if (_playbackViewModel == null)
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
                _playbackViewModel.CurrentImageId = currentImageId;
                await _playbackViewModel.ClearTimingDataCommand.ExecuteAsync(null);
                ShowStatus("✅ 已清除时间数据");
            }
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
                    if (currentImageId <= 0) return;

                    // 获取当前图片的所有关键帧
                    var keyframes = _keyframeManager.GetKeyframesAsync(currentImageId).Result;
                    if (keyframes == null || !keyframes.Any()) return;

                    // 获取尺寸信息
                    double imageCanvasWidth = KeyframePreviewLinesCanvas.ActualWidth;
                    double imageCanvasHeight = KeyframePreviewLinesCanvas.ActualHeight;
                    double scrollbarCanvasHeight = ScrollbarIndicatorsCanvas.ActualHeight;
                    double viewportHeight = ImageScrollViewer.ViewportHeight;
                    
                    if (imageCanvasHeight <= 0 || scrollbarCanvasHeight <= 0) return;

                    // 计算滚动范围
                    double scrollableHeight = Math.Max(0, imageCanvasHeight - viewportHeight);

                    // 绘制每个关键帧
                    for (int i = 0; i < keyframes.Count; i++)
                    {
                        var keyframe = keyframes[i];

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

                            // 创建容器来放置方块和数字
                            var indicatorContainer = new Grid();
                            
                            // 方块（放大到 16x16）
                            var indicator = new System.Windows.Shapes.Rectangle
                            {
                                Width = 16,
                                Height = 16,  // 正方形，放大一倍
                                Fill = new System.Windows.Media.SolidColorBrush(
                                    System.Windows.Media.Color.FromRgb(255, 0, 0)), // 红色
                                Stroke = new System.Windows.Media.SolidColorBrush(
                                    System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色边框
                                StrokeThickness = 1.5,
                                RadiusX = 2,
                                RadiusY = 2,
                                Opacity = 0.95,
                                Cursor = System.Windows.Input.Cursors.Hand,
                                Tag = keyframe.Id  // 保存关键帧ID
                            };

                            indicatorContainer.Children.Add(indicator);

                            // 如果有循环次数提示，显示数字
                            if (keyframe.LoopCount.HasValue && keyframe.LoopCount.Value > 0)
                            {
                                var loopText = new TextBlock
                                {
                                    Text = keyframe.LoopCount.Value.ToString(),
                                    FontSize = 11,  // 放大字体
                                    FontWeight = FontWeights.Bold,
                                    Foreground = new System.Windows.Media.SolidColorBrush(
                                        System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色文字
                                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                                    IsHitTestVisible = false  // 不阻挡鼠标事件
                                };
                                indicatorContainer.Children.Add(loopText);
                            }

                            Canvas.SetTop(indicatorContainer, indicatorY - 8); // 居中（调整偏移）
                            Canvas.SetLeft(indicatorContainer, -3);  // 稍微向左，确保在滚动条区域内
                            ScrollbarIndicatorsCanvas.Children.Add(indicatorContainer);
                        }
                    }

                    // 绘制当前关键帧的绿色高亮指示器
                    DrawCurrentKeyframeIndicator(scrollbarCanvasHeight, imageCanvasHeight);
                });
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 更新预览线失败: {ex.Message}");
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
                if (currentImageId <= 0 || _keyframeManager.CurrentKeyframeIndex < 0) return;

                var keyframes = _keyframeManager.GetKeyframesAsync(currentImageId).Result;
                if (keyframes == null || _keyframeManager.CurrentKeyframeIndex >= keyframes.Count) return;

                // 获取当前关键帧
                var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                
                // 计算在滚动条Canvas上的位置
                double relativePosition = currentKeyframe.YPosition / imageCanvasHeight;
                double indicatorY = relativePosition * scrollbarCanvasHeight;

                // 创建容器
                var currentContainer = new Grid();

                // 创建绿色高亮指示块（比红色稍大一点，18x18）
                var currentIndicator = new System.Windows.Shapes.Rectangle
                {
                    Width = 18,
                    Height = 18,  // 正方形，比红色稍大
                    Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0, 255, 0)), // 鲜绿色
                    Stroke = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色边框
                    StrokeThickness = 2,
                    RadiusX = 2,
                    RadiusY = 2,
                    Opacity = 1.0,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = currentKeyframe.Id
                };

                currentContainer.Children.Add(currentIndicator);

                // 如果有循环次数提示，显示数字
                if (currentKeyframe.LoopCount.HasValue && currentKeyframe.LoopCount.Value > 0)
                {
                    var loopText = new TextBlock
                    {
                        Text = currentKeyframe.LoopCount.Value.ToString(),
                        FontSize = 12,  // 放大字体
                        FontWeight = FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色文字
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        IsHitTestVisible = false
                    };
                    currentContainer.Children.Add(loopText);
                }

                Canvas.SetTop(currentContainer, indicatorY - 9); // 居中（调整偏移）
                Canvas.SetLeft(currentContainer, -4);  // 稍微向左
                ScrollbarIndicatorsCanvas.Children.Add(currentContainer);
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 绘制当前关键帧指示块失败: {ex.Message}");
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
                Keyframe closestKeyframe = null;
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
                        closestKeyframe = keyframe;
                    }
                }

                if (closestIndex >= 0 && closestKeyframe != null)
                {
                    // 右键：设置循环次数
                    if (e.ChangedButton == System.Windows.Input.MouseButton.Right)
                    {
                        ShowKeyframeLoopCountMenu(closestKeyframe, e.GetPosition(this));
                        e.Handled = true;
                        return;
                    }

                    // 左键：跳转
                    // 更新当前关键帧索引
                    _keyframeManager.UpdateKeyframeIndex(closestIndex);

                    // 滚动到目标关键帧
                    double scrollableHeight = Math.Max(0, imageCanvasHeight - ImageScrollViewer.ViewportHeight);
                    
                    if (scrollableHeight > 0)
                    {
                        double targetOffset = closestKeyframe.YPosition;
                        ImageScrollViewer.ScrollToVerticalOffset(targetOffset);
                        
                        ShowStatus($"🎯 跳转到关键帧 #{closestIndex + 1}/{keyframes.Count}");
                        
                        // 刷新预览线显示
                        UpdatePreviewLines();
                    }
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 点击跳转失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示关键帧循环次数设置菜单
        /// </summary>
        private void ShowKeyframeLoopCountMenu(Keyframe keyframe, System.Windows.Point position)
        {
            var menu = new ContextMenu();
            
            // 菜单标题
            var titleItem = new MenuItem
            {
                Header = $"关键帧 #{keyframe.OrderIndex + 1}",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(titleItem);
            menu.Items.Add(new Separator());
            
            // 设置数字选项（只保留 2、3、4）
            foreach (int i in new[] { 2, 3, 4 })
            {
                int loopCount = i;
                var menuItem = new MenuItem
                {
                    Header = $"{i}",
                    IsChecked = keyframe.LoopCount == i,
                    FontSize = 14
                };
                menuItem.Click += async (s, e) => await SetKeyframeLoopCount(keyframe.Id, loopCount);
                menu.Items.Add(menuItem);
            }
            
            menu.Items.Add(new Separator());
            
            // 清除设置
            var clearItem = new MenuItem
            {
                Header = "清除",
                FontWeight = FontWeights.Bold
            };
            clearItem.Click += async (s, e) => await SetKeyframeLoopCount(keyframe.Id, null);
            menu.Items.Add(clearItem);
            
            // 显示菜单
            menu.IsOpen = true;
            menu.PlacementTarget = this;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
            menu.HorizontalOffset = position.X;
            menu.VerticalOffset = position.Y;
        }

        /// <summary>
        /// 设置关键帧循环次数
        /// </summary>
        private async Task SetKeyframeLoopCount(int keyframeId, int? loopCount)
        {
            try
            {
                if (_keyframeRepository == null)
                {
                    ShowStatus("❌ 关键帧系统未初始化");
                    return;
                }

                bool success = await _keyframeRepository.UpdateLoopCountAsync(keyframeId, loopCount);
                
                if (success)
                {
                    // 清除缓存
                    int currentImageId = GetCurrentImageId();
                    if (_keyframeManager != null && currentImageId > 0)
                    {
                        // 强制刷新缓存
                        await _keyframeManager.GetKeyframesAsync(currentImageId);
                    }
                    
                    // 刷新UI显示
                    UpdatePreviewLines();
                    
                    if (loopCount.HasValue)
                    {
                        ShowStatus($"✅ 已设置循环提示: {loopCount}遍");
                    }
                    else
                    {
                        ShowStatus($"✅ 已清除循环提示");
                    }
                }
                else
                {
                    ShowStatus("❌ 设置失败");
                }
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 设置循环次数失败: {ex.Message}");
                ShowStatus($"❌ 设置失败: {ex.Message}");
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
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 加载滚动速度失败: {ex.Message}");
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
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 保存滚动速度失败: {ex.Message}");
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
                    }
                }
            }
            catch
            {
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
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"⚠️ 更新菜单选中状态失败: {ex.Message}");
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
                    SaveScrollEasingSettings();
                }
            }
            catch
            {
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
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"⚠️ 更新菜单选中状态失败: {ex.Message}");
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
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 保存滚动函数失败: {ex.Message}");
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
                    }
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 加载滚动函数失败: {ex.Message}");
            }
        }
        
        #endregion

        #region 三模式切换支持方法

        /// <summary>
        /// 判断是否处于媒体播放模式（视频/音频正在播放）
        /// </summary>
        private bool IsMediaPlaybackMode()
        {
            // 检查是否有媒体播放器且正在播放
            return videoPlayerManager?.IsPlaying == true;
        }

        /// <summary>
        /// 判断是否处于原图标记模式
        /// </summary>
        private bool IsOriginalMarkMode()
        {
            try
            {
                // 检查当前图片是否被标记为原图模式
                if (currentImageId == 0 || dbManager == null)
                    return false;

                var dbContext = dbManager.GetDbContext();
                if (dbContext == null)
                    return false;

                // 查询当前图片或其所属文件夹是否有原图标记
                var currentFile = dbContext.MediaFiles.FirstOrDefault(m => m.Id == currentImageId);
                if (currentFile == null)
                    return false;

                // 检查图片本身是否有原图标记
                var imageMark = dbContext.OriginalMarks.FirstOrDefault(
                    m => m.ItemTypeString == "image" && m.ItemId == currentImageId);
                
                if (imageMark != null)
                    return true;

                // 检查所属文件夹是否有原图标记
                if (currentFile.FolderId.HasValue)
                {
                    var folderMark = dbContext.OriginalMarks.FirstOrDefault(
                        m => m.ItemTypeString == "folder" && m.ItemId == currentFile.FolderId.Value);
                    
                    if (folderMark != null)
                        return true;
                }

                return false;
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 检查原图模式失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切换到上一个媒体文件
        /// </summary>
        private Task SwitchToPreviousMediaFile()
        {
            try
            {
                if (videoPlayerManager == null)
                {
                    ShowStatus("媒体播放器未初始化");
                    return Task.CompletedTask;
                }

                // 调用媒体播放器的上一曲功能
                videoPlayerManager.PlayPrevious();
                ShowStatus("⏮️ 切换到上一个媒体文件");
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 切换上一个媒体失败: {ex.Message}");
                ShowStatus($"❌ 切换失败: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 切换到下一个媒体文件
        /// </summary>
        private Task SwitchToNextMediaFile()
        {
            try
            {
                if (videoPlayerManager == null)
                {
                    ShowStatus("媒体播放器未初始化");
                    return Task.CompletedTask;
                }

                // 调用媒体播放器的下一曲功能
                videoPlayerManager.PlayNext();
                ShowStatus("⏭️ 切换到下一个媒体文件");
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 切换下一个媒体失败: {ex.Message}");
                ShowStatus($"❌ 切换失败: {ex.Message}");
            }
            return Task.CompletedTask;
        }


        #endregion
    }
}

