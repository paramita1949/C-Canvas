using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 背景设置侧边面板
    /// </summary>
    public partial class BackgroundSettingsPanel : System.Windows.Controls.UserControl
    {
        private DraggableTextBox _targetTextBox;
        private int _cornerRadius = 0;
        private int _opacity = 100;
        private string _currentColor = "#FFFFFF";
        private List<string> _recentColors = new List<string>();

        // 常用颜色色板 (6x4 = 24色) - 与边框样式一致
        private readonly string[] _colorPalette = new string[]
        {
            // 第1行：红橙黄系
            "#EF9A9A", "#EF5350", "#FF7043", "#FF9800", "#FFEB3B", "#FFF59D",
            // 第2行：绿青系
            "#A5D6A7", "#66BB6A", "#26A69A", "#4FC3F7", "#42A5F5", "#2196F3",
            // 第3行：蓝紫粉系
            "#1976D2", "#5C6BC0", "#9C27B0", "#BA68C8", "#F48FB1", "#CE93D8",
            // 第4行：灰度系
            "#B0BEC5", "#757575", "#424242", "#212121", "#FFFFFF", "#FFFFFF"
        };

        public BackgroundSettingsPanel()
        {
            InitializeComponent();
            InitializeColorPalette();
        }

        public void BindTarget(DraggableTextBox textBox)
        {
            _targetTextBox = textBox;

            // 从文本框读取当前背景设置
            if (_targetTextBox != null && _targetTextBox.Data != null)
            {
                _currentColor = _targetTextBox.Data.BackgroundColor;
                _cornerRadius = (int)_targetTextBox.Data.BackgroundRadius;
                _opacity = _targetTextBox.Data.BackgroundOpacity;

                // ✅ 如果背景透明度为 100%（完全透明），打开面板时设置为 0%（完全不透明）
                if (_opacity >= 100)
                {
                    _opacity = 0;
                    _targetTextBox.Data.BackgroundOpacity = 0;
                }

                // 更新UI控件
                TxtRgbInput.Text = _currentColor;
                CornerRadiusSlider.Value = _cornerRadius;
                OpacitySlider.Value = _opacity;
            }
        }

        private void InitializeColorPalette()
        {
            ColorGrid.Children.Clear();

            foreach (var colorHex in _colorPalette)
            {
                var border = new Border
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)),
                    CornerRadius = new CornerRadius(6),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = colorHex
                };
                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (s is Border b && b.Tag is string color)
                    {
                        _currentColor = color;
                        TxtRgbInput.Text = color;
                        AddToRecentColors(color);
                        ApplyBackgroundColor(color);
                    }
                };
                ColorGrid.Children.Add(border);
            }
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string colorHex)
            {
                _currentColor = colorHex;
                TxtRgbInput.Text = colorHex;
                AddToRecentColors(colorHex);
                ApplyBackgroundColor(colorHex);
            }
        }

        private void BtnUseRgb_Click(object sender, RoutedEventArgs e)
        {
            var colorHex = TxtRgbInput.Text.Trim();
            if (colorHex.StartsWith("#") && (colorHex.Length == 7 || colorHex.Length == 9))
            {
                _currentColor = colorHex;
                AddToRecentColors(colorHex);
                ApplyBackgroundColor(colorHex);
            }
        }

        private void AddToRecentColors(string colorHex)
        {
            if (_recentColors.Contains(colorHex))
                _recentColors.Remove(colorHex);

            _recentColors.Insert(0, colorHex);
            if (_recentColors.Count > 6)
                _recentColors.RemoveAt(6);

            UpdateRecentColorsGrid();
        }

        private void UpdateRecentColorsGrid()
        {
            RecentColorsGrid.Children.Clear();

            foreach (var colorHex in _recentColors)
            {
                var border = new Border
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204)),
                    CornerRadius = new CornerRadius(6),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = colorHex
                };
                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (s is Border b && b.Tag is string color)
                    {
                        _currentColor = color;
                        TxtRgbInput.Text = color;
                        AddToRecentColors(color);
                        ApplyBackgroundColor(color);
                    }
                };
                RecentColorsGrid.Children.Add(border);
            }
        }

        /// <summary>
        /// 无颜色按钮点击
        /// </summary>
        private void BtnNoColor_Click(object sender, RoutedEventArgs e)
        {
            _currentColor = "Transparent";
            TxtRgbInput.Text = "无颜色";
            _opacity = 100;  // ✅ 透明度 100% = 完全透明
            OpacitySlider.Value = 100;
            ApplyBackgroundColor("Transparent");
        }

        /// <summary>
        /// 自定义色盘按钮点击
        /// </summary>
        private void BtnCustomColor_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = colorDialog.Color;
                var colorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                _currentColor = colorHex;
                TxtRgbInput.Text = colorHex;
                AddToRecentColors(colorHex);
                ApplyBackgroundColor(colorHex);
            }
        }

        /// <summary>
        /// 圆角滑块值变化
        /// </summary>
        private void CornerRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _cornerRadius = (int)e.NewValue;
            if (TxtCornerRadiusLabel != null)
                TxtCornerRadiusLabel.Text = $"圆角 {_cornerRadius} px";

            ApplyBackgroundStyle();
        }

        /// <summary>
        /// 透明度滑块值变化
        /// </summary>
        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _opacity = (int)e.NewValue;
            if (TxtOpacityLabel != null)
            {
                if (_opacity == 100)
                    TxtOpacityLabel.Text = "透明度 100% （无背景）";
                else
                    TxtOpacityLabel.Text = $"透明度 {_opacity}%";
            }

            ApplyBackgroundStyle();
        }

        private void ApplyBackgroundColor(string colorHex)
        {
// #if DEBUG
//             System.Diagnostics.Debug.WriteLine($"🎨 [ApplyBackgroundColor] 颜色={colorHex}");
// #endif
            _currentColor = colorHex;
            ApplyBackgroundStyle();
        }

        /// <summary>
        /// 应用完整背景样式到文本框（支持选中文本）
        /// </summary>
        private void ApplyBackgroundStyle()
        {
// #if DEBUG
//             System.Diagnostics.Debug.WriteLine($"🎨 [ApplyBackgroundStyle] 目标文本框={_targetTextBox != null}, 颜色={_currentColor}, 圆角={_cornerRadius}, 透明度={_opacity}");
// #endif
            if (_targetTextBox == null)
            {
// #if DEBUG
//                 System.Diagnostics.Debug.WriteLine($"⚠️ [ApplyBackgroundStyle] 目标文本框为空");
// #endif
                return;
            }

            // 🆕 优先应用到选中文本，无选择时应用到整个文本框
            _targetTextBox.ApplyStyleToSelection(
                backgroundColor: _currentColor,
                backgroundRadius: _cornerRadius,
                backgroundOpacity: _opacity
            );
        }
    }
}

