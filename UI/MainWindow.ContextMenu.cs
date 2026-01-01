using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ImageColorChanger.Core;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的右键菜单处理部分
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 右键菜单

        /// <summary>
        /// 导航栏分隔条拖动完成事件 - 保存宽度
        /// </summary>
        private void NavigationSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (NavigationPanelColumn != null)
            {
                double newWidth = NavigationPanelColumn.ActualWidth;
                _configManager.NavigationPanelWidth = newWidth;
                // System.Diagnostics.Debug.WriteLine($"✅ 导航栏宽度已保存: {newWidth}");
            }
        }

        /// <summary>
        /// 圣经导航分隔条拖动完成事件 - 保存历史记录+按钮区域高度
        /// </summary>
        private void BibleNavigationSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (BibleHistoryAndButtonRow != null)
            {
                double newHeight = BibleHistoryAndButtonRow.ActualHeight;
                if (newHeight > 0)
                {
                    _configManager.BibleHistoryRowHeight = newHeight;
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ 圣经历史记录+按钮区域高度已保存: {newHeight}");
#endif
                }
            }
        }

        /// <summary>
        /// 右键菜单打开事件 - 如果没有图片则阻止显示
        /// </summary>
        private void CanvasContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            // 如果没有加载图片，立即关闭菜单
            if (_imageProcessor.CurrentImage == null)
            {
                if (sender is ContextMenu menu)
                {
                    menu.IsOpen = false;
                }
            }
        }

        private void ImageScrollViewer_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 如果没有加载图片，阻止显示右键菜单
            if (_imageProcessor.CurrentImage == null)
            {
                e.Handled = true;
                
                // 确保菜单不会显示
                if (ImageScrollViewer.ContextMenu != null)
                {
                    ImageScrollViewer.ContextMenu.IsOpen = false;
                }
                return;
            }

            // 使用XAML中定义的ContextMenu
            var contextMenu = ImageScrollViewer.ContextMenu;
            if (contextMenu == null)
            {
                contextMenu = new ContextMenu();
                
                // 🔑 应用自定义样式
                contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");
                
                ImageScrollViewer.ContextMenu = contextMenu;
            }
            
            // 清除除了"滚动速度"和"滚动函数"之外的所有菜单项
            var scrollSpeedMenu = contextMenu.Items.Cast<object>()
                .FirstOrDefault(item => item is MenuItem mi && mi.Header.ToString() == "滚动速度");
            var scrollEasingMenu = contextMenu.Items.Cast<object>()
                .FirstOrDefault(item => item is MenuItem mi && mi.Header.ToString() == "滚动函数");
            
            contextMenu.Items.Clear();
            
            // 歌词模式切换菜单（第一位）
            var lyricsMenuItem = new MenuItem 
            { 
                Header = _isLyricsMode ? "退出歌词" : "进入歌词",
                IsCheckable = false
            };
            
            lyricsMenuItem.Click += (s, args) =>
            {
                if (_isLyricsMode)
                {
                    ExitLyricsMode();
                }
                else
                {
                    EnterLyricsMode();
                }
            };
            contextMenu.Items.Add(lyricsMenuItem);

            // 🎬 合成标记菜单（第二位）
            var compositeMarkMenuItem = new MenuItem 
            { 
                Header = "合成标记",
                IsCheckable = true,
                IsChecked = false // 默认未选中，异步加载真实状态
            };
            
            // 🔧 异步加载当前图片的合成标记状态
            _ = Task.Run(async () =>
            {
                if (_keyframeManager != null && _currentImageId > 0)
                {
                    var isEnabled = await _keyframeManager.GetCompositePlaybackEnabledAsync(_currentImageId);
                    Dispatcher.Invoke(() => compositeMarkMenuItem.IsChecked = isEnabled);
                }
            });

            compositeMarkMenuItem.Click += async (s, args) =>
            {
                if (_keyframeManager != null && _currentImageId > 0)
                {
                    // 🔧 MenuItem的IsChecked会在Click事件中自动切换，所以这里读取的是切换后的值
                    bool newState = compositeMarkMenuItem.IsChecked;
                    bool success = await _keyframeManager.SetCompositePlaybackEnabledAsync(_currentImageId, newState);
                    
                    if (success)
                    {
                        ShowStatus(newState 
                            ? "✅ 已启用合成标记：录制完成后自动播放合成" 
                            : "✅ 已关闭合成标记：录制完成后播放普通模式");
                        
                        // 🎨 立刻更新合成播放按钮颜色
                        SetCompositeButtonColor(newState);
                    }
                    else
                    {
                        // 如果保存失败，恢复原状态
                        compositeMarkMenuItem.IsChecked = !newState;
                        ShowStatus("❌ 更新合成标记失败");
                    }
                }
            };
            contextMenu.Items.Add(compositeMarkMenuItem);

            // 重新添加滚动速度菜单（无分隔线）
            if (scrollSpeedMenu != null)
            {
                contextMenu.Items.Add(scrollSpeedMenu);
                // 更新滚动速度菜单的选中状态
                if (_keyframeManager != null)
                {
                    foreach (var item in ((MenuItem)scrollSpeedMenu).Items)
                    {
                        if (item is MenuItem subMenu && subMenu.Tag != null)
                        {
                            if (double.TryParse(subMenu.Tag.ToString(), out double speed))
                            {
                                subMenu.IsChecked = Math.Abs(speed - _keyframeManager.ScrollDuration) < 0.01;
                            }
                        }
                    }
                }
            }
            
            // 重新添加滚动函数菜单（无分隔线）
            if (scrollEasingMenu != null)
            {
                contextMenu.Items.Add(scrollEasingMenu);
                // 更新滚动函数菜单的选中状态
                if (_keyframeManager != null)
                {
                    foreach (var item in ((MenuItem)scrollEasingMenu).Items)
                    {
                        if (item is MenuItem subMenu && subMenu.Tag != null)
                        {
                            string tag = subMenu.Tag.ToString();
                            if (tag == "Linear")
                            {
                                subMenu.IsChecked = _keyframeManager.IsLinearScrolling;
                            }
                            else
                            {
                                subMenu.IsChecked = !_keyframeManager.IsLinearScrolling && 
                                                    tag == _keyframeManager.ScrollEasingType;
                            }
                        }
                    }
                }
            }

            // 🎨 变色颜色菜单（无分隔线）
            var colorMenuItem = new MenuItem { Header = "变色颜色" };

            // 从 ConfigManager 获取所有颜色预设
            var allPresets = _configManager.GetAllColorPresets();
            
            foreach (var preset in allPresets)
            {
                var menuItem = new MenuItem 
                { 
                    Header = preset.Name,
                    IsCheckable = true,
                    IsChecked = _currentTargetColor.Red == preset.R && 
                               _currentTargetColor.Green == preset.G && 
                               _currentTargetColor.Blue == preset.B
                };
                
                // 捕获当前预设到局部变量
                var currentPreset = preset;
                
                menuItem.Click += (s, args) =>
                {
                    _currentTargetColor = currentPreset.ToSKColor();
                    _currentTargetColorName = currentPreset.Name; // 保存颜色名称
                    if (_isColorEffectEnabled)
                    {
                        // 如果颜色效果已启用，清除缓存并更新显示
                        _imageProcessor.ClearCache();
                        _imageProcessor.UpdateImage();
                    }
                    // 保存颜色设置
                    SaveSettings();
                    ShowStatus($"✨ 已切换颜色: {currentPreset.Name}");
                };
                colorMenuItem.Items.Add(menuItem);
            }

            // 添加分隔线
            colorMenuItem.Items.Add(new Separator());

            // 自定义颜色
            var customColorItem = new MenuItem { Header = "自定义颜色..." };
            customColorItem.Click += (s, args) => OpenColorPicker();
            colorMenuItem.Items.Add(customColorItem);
            
            // 保存当前颜色为预设
            if (_currentTargetColorName == "自定义")
            {
                var savePresetItem = new MenuItem { Header = "保存当前颜色为预设..." };
                savePresetItem.Click += (s, args) => SaveCurrentColorAsPreset();
                colorMenuItem.Items.Add(savePresetItem);
            }

            contextMenu.Items.Add(colorMenuItem);

            // 原图模式显示切换菜单(仅在原图模式下显示)
            if (_originalMode)
            {
                contextMenu.Items.Add(new Separator());

                // 🆕 创建分割图菜单项（仅在有相似图片时显示）
                if (_originalManager.HasSimilarImages())
                {
                    var similarImages = _originalManager.GetSimilarImages();
                    int imageCount = similarImages.Count;

                    // 只支持1-4张图片
                    if (imageCount >= 1 && imageCount <= 4)
                    {
                        var createSplitMenuItem = new MenuItem
                        {
                            Header = $"创建分割图 ({imageCount}张)",
                            FontSize = 14
                        };
                        createSplitMenuItem.Click += async (s, args) =>
                        {
                            await CreateSplitSlideInPraiseProject();
                        };
                        contextMenu.Items.Add(createSplitMenuItem);
                        contextMenu.Items.Add(new Separator());
                    }
                }

                var displayModeMenuItem = new MenuItem { Header = "原图模式" };
                
                // 拉伸模式
                var stretchItem = new MenuItem 
                { 
                    Header = "拉伸", 
                    IsCheckable = true,
                    IsChecked = _originalDisplayMode == OriginalDisplayMode.Stretch
                };
                stretchItem.Click += (s, args) =>
                {
                    if (_originalDisplayMode != OriginalDisplayMode.Stretch)
                    {
                        _originalDisplayMode = OriginalDisplayMode.Stretch;
                        _imageProcessor.OriginalDisplayModeValue = _originalDisplayMode;
                        _imageProcessor.UpdateImage();
                        UpdateProjection();
                        ShowStatus("✅ 原图模式: 拉伸显示");
                    }
                };
                displayModeMenuItem.Items.Add(stretchItem);
                
                // 适中模式
                var fitItem = new MenuItem 
                { 
                    Header = "适中", 
                    IsCheckable = true,
                    IsChecked = _originalDisplayMode == OriginalDisplayMode.Fit
                };
                fitItem.Click += (s, args) =>
                {
                    if (_originalDisplayMode != OriginalDisplayMode.Fit)
                    {
                        _originalDisplayMode = OriginalDisplayMode.Fit;
                        _imageProcessor.OriginalDisplayModeValue = _originalDisplayMode;
                        _imageProcessor.UpdateImage();
                        UpdateProjection();
                        ShowStatus("✅ 原图模式: 适中显示");
                    }
                };
                displayModeMenuItem.Items.Add(fitItem);
                
                contextMenu.Items.Add(displayModeMenuItem);
            }

            // 显示菜单
            contextMenu.IsOpen = true;
        }

        #endregion
    }
}
