using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 阴影设置侧边面板
    /// </summary>
    public partial class ShadowSettingsPanel : System.Windows.Controls.UserControl
    {
        private DraggableTextBox _targetTextBox;
        private int _offsetX = 0;
        private int _offsetY = 0;
        private int _blurRadius = 0;
        private int _opacity = 50;
        private string _currentColor = "#000000";
        private List<string> _recentColors = new List<string>();

        // 常用颜色色板 (9x6 = 54色)
        private readonly string[] _colorPalette = new string[]
        {
            "#000000", "#404040", "#808080", "#BFBFBF", "#FFFFFF", "#FF0000", "#00FF00", "#0000FF", "#FFFF00",
            "#C00000", "#E74C3C", "#FF6B6B", "#FFA07A", "#FFB6C1", "#FF1493", "#DC143C", "#B22222", "#8B0000",
            "#FF8C00", "#FFA500", "#FFD700", "#FFFF00", "#F0E68C", "#EEE8AA", "#BDB76B", "#DAA520", "#B8860B",
            "#006400", "#228B22", "#32CD32", "#00FF00", "#7FFF00", "#ADFF2F", "#90EE90", "#98FB98", "#00FA9A",
            "#008B8B", "#00CED1", "#00FFFF", "#87CEEB", "#4682B4", "#1E90FF", "#0000FF", "#0000CD", "#00008B",
            "#8B008B", "#9370DB", "#BA55D3", "#DA70D6", "#EE82EE", "#FF00FF", "#FF69B4", "#FFB6C1", "#FFC0CB"
        };

        public ShadowSettingsPanel()
        {
            InitializeComponent();
            InitializeColorPalette();
        }

        public void BindTarget(DraggableTextBox textBox)
        {
            _targetTextBox = textBox;

            // 从文本框读取当前阴影设置
            if (_targetTextBox != null && _targetTextBox.Data != null)
            {
                _currentColor = _targetTextBox.Data.ShadowColor;
                _offsetX = (int)_targetTextBox.Data.ShadowOffsetX;
                _offsetY = (int)_targetTextBox.Data.ShadowOffsetY;
                _blurRadius = (int)_targetTextBox.Data.ShadowBlur;
                _opacity = _targetTextBox.Data.ShadowOpacity;

                // 更新UI控件
                TxtRgbInput.Text = _currentColor;
                TxtOffsetX.Text = $"{_offsetX} px";
                TxtOffsetY.Text = $"{_offsetY} px";
                TxtBlur.Text = $"{_blurRadius} px";
                OpacitySlider.Value = _opacity;
            }
        }

        private void InitializeColorPalette()
        {
            ColorGrid.Children.Clear();

            foreach (var colorHex in _colorPalette)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = colorHex
                };
                btn.Click += ColorButton_Click;
                ColorGrid.Children.Add(btn);
            }
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string colorHex)
            {
                _currentColor = colorHex;
                TxtRgbInput.Text = colorHex;
                AddToRecentColors(colorHex);
                ApplyShadow();
            }
        }

        private void BtnUseRgb_Click(object sender, RoutedEventArgs e)
        {
            var colorHex = TxtRgbInput.Text.Trim();
            if (colorHex.StartsWith("#") && (colorHex.Length == 7 || colorHex.Length == 9))
            {
                _currentColor = colorHex;
                AddToRecentColors(colorHex);
                ApplyShadow();
            }
        }

        private void AddToRecentColors(string colorHex)
        {
            if (_recentColors.Contains(colorHex))
                _recentColors.Remove(colorHex);
            
            _recentColors.Insert(0, colorHex);
            if (_recentColors.Count > 9)
                _recentColors.RemoveAt(9);
            
            UpdateRecentColorsGrid();
        }

        private void UpdateRecentColorsGrid()
        {
            RecentColorsGrid.Children.Clear();

            foreach (var colorHex in _recentColors)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Width = 20,
                    Height = 20,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = colorHex
                };
                btn.Click += ColorButton_Click;
                RecentColorsGrid.Children.Add(btn);
            }
        }

        private void BtnDecreaseOffsetX_Click(object sender, RoutedEventArgs e)
        {
            _offsetX = Math.Max(0, _offsetX - 1);
            TxtOffsetX.Text = $"{_offsetX} px";
            ApplyShadow();
        }

        private void BtnIncreaseOffsetX_Click(object sender, RoutedEventArgs e)
        {
            _offsetX = Math.Min(20, _offsetX + 1);
            TxtOffsetX.Text = $"{_offsetX} px";
            ApplyShadow();
        }

        private void BtnDecreaseOffsetY_Click(object sender, RoutedEventArgs e)
        {
            _offsetY = Math.Max(0, _offsetY - 1);
            TxtOffsetY.Text = $"{_offsetY} px";
            ApplyShadow();
        }

        private void BtnIncreaseOffsetY_Click(object sender, RoutedEventArgs e)
        {
            _offsetY = Math.Min(20, _offsetY + 1);
            TxtOffsetY.Text = $"{_offsetY} px";
            ApplyShadow();
        }

        private void BtnDecreaseBlur_Click(object sender, RoutedEventArgs e)
        {
            _blurRadius = Math.Max(0, _blurRadius - 1);
            TxtBlur.Text = $"{_blurRadius} px";
            ApplyShadow();
        }

        private void BtnIncreaseBlur_Click(object sender, RoutedEventArgs e)
        {
            _blurRadius = Math.Min(20, _blurRadius + 1);
            TxtBlur.Text = $"{_blurRadius} px";
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

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _opacity = (int)e.NewValue;
            if (TxtOpacityLabel != null)
                TxtOpacityLabel.Text = $"透明度  {_opacity}%";
            ApplyShadow();
        }

        private void ApplyShadow()
        {
            if (_targetTextBox == null)
                return;

            // 🆕 优先应用到选中文本，无选择时应用到整个文本框
            _targetTextBox.ApplyStyleToSelection(
                shadowColor: _currentColor,
                shadowOffsetX: _offsetX,
                shadowOffsetY: _offsetY,
                shadowBlur: _blurRadius,
                shadowOpacity: _opacity
            );
        }
    }
}

