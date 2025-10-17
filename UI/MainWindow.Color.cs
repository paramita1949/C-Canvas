using System;
using System.Windows;
using System.Windows.Media;
using SkiaSharp;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 颜色效果相关

        private void BtnColorEffect_Click(object sender, RoutedEventArgs e)
        {
            ToggleColorEffect();
        }

        private void ToggleColorEffect()
        {
            if (_imageProcessor.CurrentImage == null)
            {
                System.Windows.MessageBox.Show("请先打开图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 切换变色效果状态
            _imageProcessor.IsInverted = !_imageProcessor.IsInverted;
            _isColorEffectEnabled = _imageProcessor.IsInverted;
            
            // 更新按钮样式
            if (_isColorEffectEnabled)
            {
                BtnColorEffect.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(BUTTON_ACTIVE_COLOR_R, BUTTON_ACTIVE_COLOR_G, BUTTON_ACTIVE_COLOR_B)); // 浅绿色
                ShowStatus($"✨ 已启用颜色效果 (当前颜色: {_currentTargetColorName})");
            }
            else
            {
                BtnColorEffect.Background = System.Windows.Media.Brushes.Transparent; // 使用透明背景，让样式生效
                ShowStatus("✅ 已关闭颜色效果");
            }
            
            // 通过ImageProcessor的UpdateImage来更新显示（包含完整的缩放、居中逻辑）
            _imageProcessor.UpdateImage();
            
            // 更新投影
            UpdateProjection();
        }


        private void OpenColorPicker()
        {
            using (var colorDialog = new System.Windows.Forms.ColorDialog())
            {
                // 设置当前颜色
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    _currentTargetColor.Red, 
                    _currentTargetColor.Green, 
                    _currentTargetColor.Blue);
                
                colorDialog.AllowFullOpen = true;
                colorDialog.FullOpen = true;

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var selectedColor = colorDialog.Color;
                    _currentTargetColor = new SKColor(selectedColor.R, selectedColor.G, selectedColor.B);
                    
                    // 使用 ConfigManager 查找预设名称
                    var presetName = _configManager.FindPresetName(selectedColor.R, selectedColor.G, selectedColor.B);
                    _currentTargetColorName = presetName ?? "自定义";
                    
                    // 如果颜色效果已启用，清除缓存并更新显示
                    if (_isColorEffectEnabled)
                    {
                        _imageProcessor.ClearCache();
                        _imageProcessor.UpdateImage();
                    }
                    
                    // 保存颜色设置
                    SaveSettings();
                    
                    string colorInfo = presetName != null 
                        ? $"{presetName}" 
                        : $"自定义颜色: RGB({selectedColor.R}, {selectedColor.G}, {selectedColor.B})";
                    ShowStatus($"✨ 已设置{colorInfo}");
                }
            }
        }

        /// <summary>
        /// 保存当前颜色为预设
        /// </summary>
        private void SaveCurrentColorAsPreset()
        {
            try
            {
                // 创建输入对话框
                var inputDialog = new Window
                {
                    Title = "保存颜色预设",
                    Width = 380,
                    Height = 175,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize
                };

                var stackPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
                
                var label = new System.Windows.Controls.TextBlock 
                { 
                    Text = $"请输入预设名称\n当前颜色: RGB({_currentTargetColor.Red}, {_currentTargetColor.Green}, {_currentTargetColor.Blue})",
                    Margin = new Thickness(0, 0, 0, 10)
                };
                
                var textBox = new System.Windows.Controls.TextBox 
                { 
                    Margin = new Thickness(0, 0, 0, 10),
                    FontSize = 14
                };
                
                var buttonPanel = new System.Windows.Controls.StackPanel 
                { 
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };
                
                var okButton = new System.Windows.Controls.Button 
                { 
                    Content = "确定",
                    Width = 70,
                    Height = 30,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };
                
                var cancelButton = new System.Windows.Controls.Button 
                { 
                    Content = "取消",
                    Width = 70,
                    Height = 30,
                    IsCancel = true
                };

                bool? dialogResult = null;
                
                okButton.Click += (s, e) => 
                {
                    dialogResult = true;
                    inputDialog.Close();
                };
                
                cancelButton.Click += (s, e) => 
                {
                    dialogResult = false;
                    inputDialog.Close();
                };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);
                
                stackPanel.Children.Add(label);
                stackPanel.Children.Add(textBox);
                stackPanel.Children.Add(buttonPanel);
                
                inputDialog.Content = stackPanel;
                
                // 聚焦文本框
                inputDialog.Loaded += (s, e) => textBox.Focus();
                
                inputDialog.ShowDialog();

                if (dialogResult == true && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    string presetName = textBox.Text.Trim();
                    
                    // 添加到配置管理器
                    bool success = _configManager.AddCustomColorPreset(
                        presetName,
                        _currentTargetColor.Red,
                        _currentTargetColor.Green,
                        _currentTargetColor.Blue
                    );

                    if (success)
                    {
                        _currentTargetColorName = presetName;
                        SaveSettings();
                        ShowStatus($"✅ 已保存颜色预设: {presetName}");
                        System.Windows.MessageBox.Show($"颜色预设 '{presetName}' 已保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("该颜色预设已存在或颜色已被使用，请使用其他名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 保存颜色预设失败: {ex.Message}");
                System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}

