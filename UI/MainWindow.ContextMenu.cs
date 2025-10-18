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

        private void ImageScrollViewer_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (_imageProcessor.CurrentImage == null)
                return;

            // 使用XAML中定义的ContextMenu
            var contextMenu = ImageScrollViewer.ContextMenu;
            if (contextMenu == null)
            {
                contextMenu = new ContextMenu();
                ImageScrollViewer.ContextMenu = contextMenu;
            }
            
            contextMenu.FontSize = 14;
            
            // 清除除了"滚动速度"和"滚动函数"之外的所有菜单项
            var scrollSpeedMenu = contextMenu.Items.Cast<object>()
                .FirstOrDefault(item => item is MenuItem mi && mi.Header.ToString() == "滚动速度");
            var scrollEasingMenu = contextMenu.Items.Cast<object>()
                .FirstOrDefault(item => item is MenuItem mi && mi.Header.ToString() == "滚动函数");
            
            contextMenu.Items.Clear();
            
            // 重新添加滚动速度和滚动函数菜单
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
            if (scrollSpeedMenu != null || scrollEasingMenu != null)
            {
                contextMenu.Items.Add(new Separator());
            }

            // 变色颜色子菜单
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
