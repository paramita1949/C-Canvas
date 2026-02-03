using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers.Keyframes;
using MessageBox = System.Windows.MessageBox;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Keyframe Helpers (Utils, Navigation, Scroll, Mode Switch)
    /// </summary>
    public partial class MainWindow
    {
        #region 关键帧辅助方法

        /// <summary>
        /// 获取当前图片ID
        /// </summary>
        public int GetCurrentImageId()
        {
            return _currentImageId;
        }

        /// <summary>
        /// 投影是否启用
        /// </summary>
        public bool IsProjectionEnabled => _projectionManager?.IsProjectionActive ?? false;

        /// <summary>
        /// 开始FPS监控
        /// </summary>
        public void StartFpsMonitoring()
        {
            try
            {
                _fpsMonitor?.StartMonitoring();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 启动FPS监控失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 停止FPS监控
        /// </summary>
        public void StopFpsMonitoring()
        {
            try
            {
                _fpsMonitor?.StopMonitoring();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 停止FPS监控失败: {ex.Message}");
            }
        }

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
                    int _currentImageId = GetCurrentImageId();
                    if (_currentImageId <= 0) return;

                    // 获取当前图片的所有关键帧
                    var keyframes = _keyframeManager.GetKeyframesAsync(_currentImageId).Result;
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
                        
                        // 判断是否是当前关键帧
                        bool isCurrentKeyframe = (i == _keyframeManager.CurrentKeyframeIndex);

                        // 1. 在图片上绘制横线
                        var previewLine = new System.Windows.Shapes.Line
                        {
                            X1 = 0,
                            X2 = imageCanvasWidth,
                            Y1 = keyframe.YPosition,
                            Y2 = keyframe.YPosition,
                            StrokeThickness = 4,
                            Opacity = 0.8
                        };
                        
                        // 当前关键帧：绿色实线
                        if (isCurrentKeyframe)
                        {
                            previewLine.Stroke = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0, 255, 0)); // 鲜绿色
                            // 不设置 StrokeDashArray，默认就是实线
                        }
                        else
                        {
                            // 其他关键帧：红色虚线
                            previewLine.Stroke = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromArgb(200, 255, 0, 0)); // 半透明红色
                            previewLine.StrokeDashArray = new System.Windows.Media.DoubleCollection { 10, 5 }; // 虚线
                        }
                        
                        KeyframePreviewLinesCanvas.Children.Add(previewLine);

                        // 2. 在滚动条旁边绘制位置提示方块
                        if (scrollableHeight > 0)
                        {
                            double relativePosition = keyframe.YPosition / imageCanvasHeight;
                            double indicatorY = relativePosition * scrollbarCanvasHeight;

                            // 创建容器来放置方块和数字
                            var indicatorContainer = new Grid();
                            
                            // 方块（放大到 20x20）
                            var indicator = new System.Windows.Shapes.Rectangle
                            {
                                Width = 20,
                                Height = 20,  // 正方形
                                Fill = new System.Windows.Media.SolidColorBrush(
                                    System.Windows.Media.Color.FromRgb(255, 32, 32)), // 鲜艳红色
                                RadiusX = 3,
                                RadiusY = 3,
                                Opacity = 0.45,
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
                                    FontSize = 13,  // 放大字体
                                    FontWeight = FontWeights.Bold,
                                    Foreground = new System.Windows.Media.SolidColorBrush(
                                        System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色文字
                                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                                    IsHitTestVisible = false  // 不阻挡鼠标事件
                                };
                                indicatorContainer.Children.Add(loopText);
                            }

                            Canvas.SetTop(indicatorContainer, indicatorY - 10); // 居中（调整偏移）
                            Canvas.SetLeft(indicatorContainer, -2);  // 稍微向左，确保在滚动条区域内
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
                int _currentImageId = GetCurrentImageId();
                if (_currentImageId <= 0 || _keyframeManager.CurrentKeyframeIndex < 0) return;

                var keyframes = _keyframeManager.GetKeyframesAsync(_currentImageId).Result;
                if (keyframes == null || _keyframeManager.CurrentKeyframeIndex >= keyframes.Count) return;

                // 获取当前关键帧
                var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                
                // 计算在滚动条Canvas上的位置
                double relativePosition = currentKeyframe.YPosition / imageCanvasHeight;
                double indicatorY = relativePosition * scrollbarCanvasHeight;

                // 创建容器
                var currentContainer = new Grid();

                // 创建绿色高亮指示块（比红色稍大一点，22x22）
                var currentIndicator = new System.Windows.Shapes.Rectangle
                {
                    Width = 22,
                    Height = 22,  // 正方形，比红色稍大
                    Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0, 255, 0)), // 鲜绿色
                    RadiusX = 3,
                    RadiusY = 3,
                    Opacity = 0.5,
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
                        FontSize = 14,  // 放大字体
                        FontWeight = FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色文字
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        IsHitTestVisible = false
                    };
                    currentContainer.Children.Add(loopText);
                }

                Canvas.SetTop(currentContainer, indicatorY - 11); // 居中（调整偏移）
                Canvas.SetLeft(currentContainer, -3);  // 稍微向左
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
        private void ScrollbarIndicatorsCanvas_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                int _currentImageId = GetCurrentImageId();
                if (_currentImageId <= 0 || _keyframeManager == null)
                {
                    return;
                }

                // 获取点击位置
                var clickPoint = e.GetPosition(ScrollbarIndicatorsCanvas);
                double clickY = clickPoint.Y;
                double canvasHeight = ScrollbarIndicatorsCanvas.ActualHeight;

                // 获取所有关键帧（从缓存，性能优化）
                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
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
                    // 🔧 修复：在跳转前保存当前索引，避免更新索引后导致记录错误
                    int currentIndexBeforeJump = _keyframeManager.CurrentKeyframeIndex;
                    
                    // 🔧 修复：如果正在录制，先记录当前帧的时间（跳转前）
                    if (_playbackViewModel?.IsRecording == true && currentIndexBeforeJump >= 0 && currentIndexBeforeJump < keyframes.Count)
                    {
                        var currentKeyframe = keyframes[currentIndexBeforeJump];
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"📹 [点击跳转-录制] 从关键帧 #{currentIndexBeforeJump + 1} (ID={currentKeyframe.Id}) 跳转到 #{closestIndex + 1}，先记录当前帧时间");
                        #endif
                        _ = _playbackViewModel.RecordKeyframeTimeAsync(currentKeyframe.Id); // 异步执行不等待
                    }
                    
                    // 如果正在播放，记录手动操作用于实时修正
                    if (_playbackViewModel?.IsPlaying == true && currentIndexBeforeJump >= 0 && currentIndexBeforeJump < keyframes.Count)
                    {
                        var currentKeyframe = keyframes[currentIndexBeforeJump];
                        var playbackService = App.GetRequiredService<Services.PlaybackServiceFactory>()
                            .GetPlaybackService(Database.Models.Enums.PlaybackMode.Keyframe);
                        if (playbackService is Services.Implementations.KeyframePlaybackService kfService)
                        {
                            _ = kfService.RecordManualOperationAsync(currentKeyframe.Id); // 异步执行不等待
                            kfService.SkipCurrentWaitAndPlayNext();
                        }
                    }

                    // 更新当前关键帧索引
                    _keyframeManager.UpdateKeyframeIndex(closestIndex);

                    // 🔧 修复：点击跳转时，先停止任何正在进行的滚动动画，确保直接跳转
                    if (_keyframeManager.IsScrolling)
                    {
                        _keyframeManager.StopScrollAnimation();
                    }
                    
                    // 清除可能存在的动画属性，确保直接跳转
                    ImageScrollViewer.BeginAnimation(Utils.AnimationHelper.GetAnimatedVerticalOffsetProperty(), null);

                    // 滚动到目标关键帧（直接跳转，无动画）
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
            
            // 🔑 应用自定义样式
            menu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");
            
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
                    int _currentImageId = GetCurrentImageId();
                    if (_keyframeManager != null && _currentImageId > 0)
                    {
                        // 强制刷新缓存（异步不等待）
                        _ = _keyframeManager.GetKeyframesAsync(_currentImageId);
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
                var dbContext = _dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "scroll_speed");
                if (setting != null && double.TryParse(setting.Value, out double speed))
                {
                    _scrollDuration = speed;
                    if (_keyframeManager != null)
                    {
                        _keyframeManager.ScrollDuration = speed;
                        // 注意：不在这里设置 ScrollEasingType 和 IsLinearScrolling
                        // 这些应该在 LoadScrollEasingSettings() 中单独加载
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
                var dbContext = _dbManager?.GetDbContext();
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
                        
                        // 更新KeyframeManager的滚动时长（仅更新速度，不改变缓动函数设置）
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.ScrollDuration = speed;
                            // 注意：不在这里修改 ScrollEasingType 和 IsLinearScrolling
                            // 这两个属性应该只在用户明确修改缓动函数时才改变
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
                var dbContext = _dbManager?.GetDbContext();
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
                var dbContext = _dbManager?.GetDbContext();
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
                else
                {
                    // 如果数据库中没有设置，默认使用线性滚动
                    _isLinearScrolling = true;
                    if (_keyframeManager != null)
                    {
                        _keyframeManager.IsLinearScrolling = true;
                    }
                    
                    // 保存默认设置到数据库
                    dbContext.Settings.Add(new Database.Models.Setting
                    {
                        Key = "scroll_easing",
                        Value = "Linear"
                    });
                    dbContext.SaveChanges();
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("✅ 已设置默认滚动函数为线性");
                    #endif
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
        internal bool IsMediaPlaybackMode()
        {
            // 检查是否有媒体播放器且正在播放
            return _videoPlayerManager?.IsPlaying == true;
        }

        /// <summary>
        /// 判断是否处于原图标记模式
        /// </summary>
        private bool IsOriginalMarkMode()
        {
            try
            {
                // 检查当前图片是否被标记为原图模式
                if (_currentImageId == 0 || _dbManager == null)
                    return false;

                var dbContext = _dbManager.GetDbContext();
                if (dbContext == null)
                    return false;

                // 查询当前图片或其所属文件夹是否有原图标记
                var currentFile = dbContext.MediaFiles.FirstOrDefault(m => m.Id == _currentImageId);
                if (currentFile == null)
                    return false;

                // 检查图片本身是否有原图标记
                var imageMark = dbContext.OriginalMarks.FirstOrDefault(
                    m => m.ItemTypeString == "image" && m.ItemId == _currentImageId);
                
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
        internal Task SwitchToPreviousMediaFile()
        {
            try
            {
                if (_videoPlayerManager == null)
                {
                    ShowStatus("媒体播放器未初始化");
                    return Task.CompletedTask;
                }

                // 调用媒体播放器的上一曲功能
                _videoPlayerManager.PlayPrevious();
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
        internal Task SwitchToNextMediaFile()
        {
            try
            {
                if (_videoPlayerManager == null)
                {
                    ShowStatus("媒体播放器未初始化");
                    return Task.CompletedTask;
                }

                // 调用媒体播放器的下一曲功能
                _videoPlayerManager.PlayNext();
                ShowStatus("⏭️ 切换到下一个媒体文件");
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 切换下一个媒体失败: {ex.Message}");
                ShowStatus($"❌ 切换失败: {ex.Message}");
            }
            return Task.CompletedTask;
        }


        /// <summary>
        /// 显示Toast悬浮提示（自动消失）
        /// </summary>
        /// <param name="message">提示消息</param>
        /// <param name="durationMs">显示时长（毫秒），默认2000ms</param>
        private async void ShowToast(string message, int durationMs = 2000)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // 设置消息
                    ToastMessage.Text = message;
                    
                    // 显示Toast
                    ToastNotification.Visibility = Visibility.Visible;
                    
                    // 淡入动画
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    ToastNotification.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    
                    // 等待指定时长
                    await Task.Delay(durationMs);
                    
                    // 淡出动画
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 1,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    fadeOut.Completed += (s, e) =>
                    {
                        ToastNotification.Visibility = Visibility.Collapsed;
                    };
                    ToastNotification.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Toast显示异常: {ex.Message}");
                }
            });
        }

        #endregion

    }
}