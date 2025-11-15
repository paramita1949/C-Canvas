using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageColorChanger.Core;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 阴影设置侧边面板 - 重新设计版本
    /// </summary>
    public partial class ShadowSettingsPanel : System.Windows.Controls.UserControl
    {
        private DraggableTextBox _targetTextBox;
        private ShadowType _currentShadowType = ShadowType.DropShadow;
        private ShadowPreset _currentPreset = ShadowPreset.DropStandard;
        private double _distance = 8;
        private double _blur = 12;
        private int _opacity = 80;  // 默认80%不透明度
        private string _currentColor = "#000000";

        // 常用颜色 (24色 - 6x4网格)
        private readonly string[] _commonColors = new string[]
        {
            // 第1行：黑白灰系
            "#000000", "#FFFFFF", "#E0E0E0", "#BDBDBD", "#9E9E9E", "#757575",
            // 第2行：红橙黄系
            "#F44336", "#FF5722", "#FF9800", "#FFC107", "#FFEB3B", "#CDDC39",
            // 第3行：绿青蓝系
            "#4CAF50", "#009688", "#00BCD4", "#03A9F4", "#2196F3", "#3F51B5",
            // 第4行：紫粉棕系
            "#9C27B0", "#E91E63", "#F06292", "#BA68C8", "#8D6E63", "#607D8B"
        };

        public ShadowSettingsPanel()
        {
            InitializeComponent();
            InitializeUI();
        }

        /// <summary>
        /// 初始化UI
        /// </summary>
        private void InitializeUI()
        {
            InitializeColorGrid();
            UpdatePresetButtons();
            UpdateShadowTypeButtons();
            UpdateParametersVisibility();
        }

        /// <summary>
        /// 绑定目标文本框
        /// </summary>
        public void BindTarget(DraggableTextBox textBox)
        {
            _targetTextBox = textBox;

            // 从文本框读取当前阴影设置
            if (_targetTextBox != null && _targetTextBox.Data != null)
            {
                _currentColor = _targetTextBox.Data.ShadowColor;
                _opacity = _targetTextBox.Data.ShadowOpacity;

                // ✅ 如果阴影透明度为 0%（完全透明/无阴影），打开面板时设置为 80%（默认可见）
                if (_opacity <= 0)
                {
                    _opacity = 80;
                }

                // 计算距离（从偏移量）
                var offsetX = _targetTextBox.Data.ShadowOffsetX;
                var offsetY = _targetTextBox.Data.ShadowOffsetY;
                _distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                _blur = _targetTextBox.Data.ShadowBlur;

                // 更新UI控件
                DistanceSlider.Value = Math.Min(_distance, 30);
                BlurSlider.Value = Math.Min(_blur, 40);
                OpacitySlider.Value = _opacity;

                UpdateDistanceLabel();
                UpdateBlurLabel();
                UpdateOpacityLabel();
            }
        }

        /// <summary>
        /// 初始化常用颜色网格
        /// </summary>
        private void InitializeColorGrid()
        {
            foreach (var color in _commonColors)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = color
                };
                btn.Click += ColorButton_Click;
                ColorGrid.Children.Add(btn);
            }
        }

        /// <summary>
        /// 更新参数面板可见性
        /// </summary>
        private void UpdateParametersVisibility()
        {
            bool hasActiveShadow = _currentShadowType != ShadowType.None;
            ParametersPanel.Visibility = hasActiveShadow ? Visibility.Visible : Visibility.Collapsed;
            PresetTitle.Visibility = hasActiveShadow ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 更新阴影类型按钮状态
        /// </summary>
        private void UpdateShadowTypeButtons()
        {
            var activeColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
            var normalColor = new SolidColorBrush(System.Windows.Media.Colors.White);

            BtnDropShadow.Background = _currentShadowType == ShadowType.DropShadow ? activeColor : normalColor;
            BtnDropShadow.Foreground = _currentShadowType == ShadowType.DropShadow ? new SolidColorBrush(System.Windows.Media.Colors.White) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51));

            BtnInnerShadow.Background = _currentShadowType == ShadowType.InnerShadow ? activeColor : normalColor;
            BtnInnerShadow.Foreground = _currentShadowType == ShadowType.InnerShadow ? new SolidColorBrush(System.Windows.Media.Colors.White) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51));

            BtnPerspectiveShadow.Background = _currentShadowType == ShadowType.PerspectiveShadow ? activeColor : normalColor;
            BtnPerspectiveShadow.Foreground = _currentShadowType == ShadowType.PerspectiveShadow ? new SolidColorBrush(System.Windows.Media.Colors.White) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51));
        }

        /// <summary>
        /// 更新预设按钮
        /// </summary>
        private void UpdatePresetButtons()
        {
            PresetPanel.Children.Clear();

            var presets = _currentShadowType switch
            {
                ShadowType.DropShadow => new[] { ShadowPreset.DropSoft, ShadowPreset.DropStandard, ShadowPreset.DropStrong },
                ShadowType.InnerShadow => new[] { ShadowPreset.InnerSubtle, ShadowPreset.InnerStandard, ShadowPreset.InnerDeep },
                ShadowType.PerspectiveShadow => new[] { ShadowPreset.PerspectiveNear, ShadowPreset.PerspectiveMedium, ShadowPreset.PerspectiveFar },
                _ => new ShadowPreset[0]
            };

            foreach (var preset in presets)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Content = ShadowPresetConfig.GetPresetName(preset),
                    Height = 32,
                    Margin = new Thickness(0, 0, 0, 8),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 63, 70)),
                    Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85)),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = preset
                };
                btn.Click += PresetButton_Click;
                PresetPanel.Children.Add(btn);
            }
        }

        // ========== 事件处理 ==========

        /// <summary>
        /// 阴影类型按钮点击
        /// </summary>
        private void BtnDropShadow_Click(object sender, RoutedEventArgs e)
        {
            _currentShadowType = ShadowType.DropShadow;
            _currentPreset = ShadowPreset.DropStandard;
            UpdateShadowTypeButtons();
            UpdatePresetButtons();
            UpdateParametersVisibility();
            ApplyPreset(_currentPreset);
        }

        private void BtnInnerShadow_Click(object sender, RoutedEventArgs e)
        {
            _currentShadowType = ShadowType.InnerShadow;
            _currentPreset = ShadowPreset.InnerStandard;
            UpdateShadowTypeButtons();
            UpdatePresetButtons();
            UpdateParametersVisibility();
            ApplyPreset(_currentPreset);
        }

        private void BtnPerspectiveShadow_Click(object sender, RoutedEventArgs e)
        {
            _currentShadowType = ShadowType.PerspectiveShadow;
            _currentPreset = ShadowPreset.PerspectiveMedium;
            UpdateShadowTypeButtons();
            UpdatePresetButtons();
            UpdateParametersVisibility();
            ApplyPreset(_currentPreset);
        }

        /// <summary>
        /// 无颜色按钮点击 - 移除阴影
        /// </summary>
        private void BtnNoColor_Click(object sender, RoutedEventArgs e)
        {
            if (_targetTextBox == null)
                return;

            // 重置为无阴影状态
            _currentShadowType = ShadowType.None;
            _targetTextBox.ApplyStyleToSelection(
                shadowColor: "#00000000",
                shadowOffsetX: 0,
                shadowOffsetY: 0,
                shadowBlur: 0,
                shadowOpacity: 0
            );

            UpdateShadowTypeButtons();
            UpdateParametersVisibility();
        }

        /// <summary>
        /// 更多颜色按钮点击
        /// </summary>
        private void BtnMoreColors_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = colorDialog.Color;
                _currentColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                ApplyShadow();
            }
        }

        /// <summary>
        /// 预设按钮点击
        /// </summary>
        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is ShadowPreset preset)
            {
                _currentPreset = preset;
                ApplyPreset(preset);
            }
        }

        /// <summary>
        /// 应用预设方案
        /// </summary>
        private void ApplyPreset(ShadowPreset preset)
        {
            var (offsetX, offsetY, blur) = ShadowPresetConfig.GetPresetParams(preset);

            _distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
            _blur = blur;

            DistanceSlider.Value = Math.Min(_distance, 30);
            BlurSlider.Value = Math.Min(_blur, 40);

            UpdateDistanceLabel();
            UpdateBlurLabel();

            ApplyShadow();
        }

        /// <summary>
        /// 颜色按钮点击
        /// </summary>
        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string colorHex)
            {
                _currentColor = colorHex;
                ApplyShadow();
            }
        }

        /// <summary>
        /// 距离滑块事件
        /// </summary>
        private void DistanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _distance = e.NewValue;
            UpdateDistanceLabel();
            ApplyShadow();
        }

        private void BtnDecreaseDistance_Click(object sender, RoutedEventArgs e)
        {
            DistanceSlider.Value = Math.Max(0, DistanceSlider.Value - 1);
        }

        private void BtnIncreaseDistance_Click(object sender, RoutedEventArgs e)
        {
            DistanceSlider.Value = Math.Min(30, DistanceSlider.Value + 1);
        }

        /// <summary>
        /// 模糊滑块事件
        /// </summary>
        private void BlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _blur = e.NewValue;
            UpdateBlurLabel();
            ApplyShadow();
        }

        private void BtnDecreaseBlur_Click(object sender, RoutedEventArgs e)
        {
            BlurSlider.Value = Math.Max(0, BlurSlider.Value - 1);
        }

        private void BtnIncreaseBlur_Click(object sender, RoutedEventArgs e)
        {
            BlurSlider.Value = Math.Min(40, BlurSlider.Value + 1);
        }

        /// <summary>
        /// 透明度滑块事件
        /// </summary>
        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _opacity = (int)e.NewValue;
            UpdateOpacityLabel();
            ApplyShadow();
        }

        private void BtnDecreaseOpacity_Click(object sender, RoutedEventArgs e)
        {
            OpacitySlider.Value = Math.Max(0, OpacitySlider.Value - 5);
        }

        private void BtnIncreaseOpacity_Click(object sender, RoutedEventArgs e)
        {
            OpacitySlider.Value = Math.Min(100, OpacitySlider.Value + 5);
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 更新距离标签
        /// </summary>
        private void UpdateDistanceLabel()
        {
            if (TxtDistanceValue != null)
                TxtDistanceValue.Text = $"{_distance:F0} px";
        }

        /// <summary>
        /// 更新模糊标签
        /// </summary>
        private void UpdateBlurLabel()
        {
            if (TxtBlurValue != null)
                TxtBlurValue.Text = $"{_blur:F0} px";
        }

        /// <summary>
        /// 更新透明度标签
        /// </summary>
        private void UpdateOpacityLabel()
        {
            if (TxtOpacityValue != null)
                TxtOpacityValue.Text = $"{_opacity}%";
        }

        /// <summary>
        /// 应用阴影到文本框
        /// </summary>
        private void ApplyShadow()
        {
            if (_targetTextBox == null)
            {
// #if DEBUG
//                 System.Diagnostics.Debug.WriteLine("⚠️ ApplyShadow: _targetTextBox 为 null");
// #endif
                return;
            }

            // 根据阴影类型计算偏移量
            double offsetX, offsetY;

            if (_currentShadowType == ShadowType.InnerShadow)
            {
                // 内阴影使用负偏移
                offsetX = -_distance / Math.Sqrt(2);
                offsetY = -_distance / Math.Sqrt(2);
            }
            else if (_currentShadowType == ShadowType.PerspectiveShadow)
            {
                // 透视阴影：X偏移较大，Y偏移较小
                offsetX = _distance * 0.8;
                offsetY = _distance * 0.3;
            }
            else
            {
                // 外部阴影：均匀偏移
                offsetX = _distance / Math.Sqrt(2);
                offsetY = _distance / Math.Sqrt(2);
            }

// #if DEBUG
//             System.Diagnostics.Debug.WriteLine($"✅ ApplyShadow 调用:");
//             System.Diagnostics.Debug.WriteLine($"   - 阴影类型: {_currentShadowType}");
//             System.Diagnostics.Debug.WriteLine($"   - 颜色: {_currentColor}");
//             System.Diagnostics.Debug.WriteLine($"   - 距离: {_distance} → OffsetX={offsetX:F2}, OffsetY={offsetY:F2}");
//             System.Diagnostics.Debug.WriteLine($"   - 模糊: {_blur}");
//             System.Diagnostics.Debug.WriteLine($"   - 透明度: {_opacity}%");
// #endif

            // 应用阴影样式
            _targetTextBox.ApplyStyleToSelection(
                shadowColor: _currentColor,
                shadowOffsetX: offsetX,
                shadowOffsetY: offsetY,
                shadowBlur: _blur,
                shadowOpacity: _opacity
            );

            // ✅ 强制刷新文本框渲染（触发投影更新）
            _targetTextBox.InvalidateVisual();
        }
    }
}

