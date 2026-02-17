using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 边框设置侧边面板
    /// </summary>
    public partial class BorderSettingsPanel : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// 由宿主显式注入的设置存储。
        /// </summary>
        public Services.Interfaces.IUiSettingsStore SettingsStore { get; set; }

        private DraggableTextBox _targetTextBox;
        private int _borderWidth = 2;
        private int _cornerRadius = 0;
        private int _opacity = 0;
        private string _currentColor = "#FFFFFF";
        private List<string> _recentColors = new List<string>();

        // 常用颜色色板 (6x4 = 24色) - 参考用户设计图
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

        public BorderSettingsPanel()
        {
            InitializeComponent();
            InitializeColorPalette();
            // ✅ 延迟加载：在 BindTarget 时再加载（确保数据库已初始化）
            // LoadRecentColors();
        }

        /// <summary>
        /// 绑定目标文本框
        /// </summary>
        public void BindTarget(DraggableTextBox textBox)
        {
            _targetTextBox = textBox;

            // ✅ 如果最近颜色还没有加载，现在加载（延迟加载，确保数据库已初始化）
            if (_recentColors.Count == 0)
            {
                LoadRecentColors();
            }

            // 从文本框读取当前边框设置
            if (_targetTextBox != null && _targetTextBox.Data != null)
            {
                _currentColor = _targetTextBox.Data.BorderColor;
                _borderWidth = (int)_targetTextBox.Data.BorderWidth;
                _cornerRadius = (int)_targetTextBox.Data.BorderRadius;
                _opacity = _targetTextBox.Data.BorderOpacity;

                // ✅ 如果边框透明度为 100%（完全透明），打开面板时设置为 0%（完全不透明）
                if (_opacity >= 100)
                {
                    _opacity = 0;
                    _targetTextBox.Data.BorderOpacity = 0;
                }

                // 更新UI控件
                TxtRgbInput.Text = _currentColor;
                WidthSlider.Value = _borderWidth;
                CornerRadiusSlider.Value = _cornerRadius;
                OpacitySlider.Value = _opacity;
            }
        }

        /// <summary>
        /// 初始化色板
        /// </summary>
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
                        ApplyBorderColor(color);
                    }
                };
                ColorGrid.Children.Add(border);
            }
        }

        /// <summary>
        /// 颜色按钮点击
        /// </summary>
        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string colorHex)
            {
                _currentColor = colorHex;
                TxtRgbInput.Text = colorHex;
                AddToRecentColors(colorHex);
                ApplyBorderColor(colorHex);
            }
        }

        /// <summary>
        /// 使用RGB按钮点击
        /// </summary>
        private void BtnUseRgb_Click(object sender, RoutedEventArgs e)
        {
            var colorHex = TxtRgbInput.Text.Trim();
            if (colorHex.StartsWith("#") && (colorHex.Length == 7 || colorHex.Length == 9))
            {
                _currentColor = colorHex;
                AddToRecentColors(colorHex);
                ApplyBorderColor(colorHex);
            }
        }

        /// <summary>
        /// 添加到最近使用颜色
        /// </summary>
        private void AddToRecentColors(string colorHex)
        {
            if (_recentColors.Contains(colorHex))
                _recentColors.Remove(colorHex);

            _recentColors.Insert(0, colorHex);
            if (_recentColors.Count > 6)
                _recentColors.RemoveAt(6);

            UpdateRecentColorsGrid();
            SaveRecentColors();
        }

        /// <summary>
        /// 加载最近使用颜色（从数据库）
        /// </summary>
        private void LoadRecentColors()
        {
            try
            {
                var settingsStore = SettingsStore;
                if (settingsStore == null)
                {
                    //#if DEBUG
                    //                    System.Diagnostics.Debug.WriteLine($"⚠️ [边框面板] 无法访问数据库，使用空列表");
                    //#endif
                    _recentColors = new List<string>();
                    UpdateRecentColorsGrid();
                    return;
                }

                var jsonValue = settingsStore.GetValue("BorderRecentColors");
                //#if DEBUG
                //                System.Diagnostics.Debug.WriteLine($"🔍 [边框面板] 从数据库读取值: {(string.IsNullOrEmpty(jsonValue) ? "空" : jsonValue)}");
                //#endif
                if (string.IsNullOrEmpty(jsonValue))
                {
                    //#if DEBUG
                    //                    System.Diagnostics.Debug.WriteLine($"📥 [边框面板] 数据库中没有最近颜色");
                    //#endif
                    _recentColors = new List<string>();
                }
                else
                {
                    var savedColors = System.Text.Json.JsonSerializer.Deserialize<List<string>>(jsonValue) ?? new List<string>();
                    //#if DEBUG
                    //                    System.Diagnostics.Debug.WriteLine($"📥 [边框面板] 从数据库加载: {string.Join(", ", savedColors)} (数量={savedColors.Count})");
                    //#endif
                    _recentColors = savedColors.Take(6).ToList();
                }
            }
            catch
            {
                //#if DEBUG
                //                System.Diagnostics.Debug.WriteLine($"⚠️ [边框面板] 加载失败: {ex.Message}");
                //#endif
                _recentColors = new List<string>();
            }
            UpdateRecentColorsGrid();
        }

        /// <summary>
        /// 保存最近使用颜色（到数据库）
        /// </summary>
        private void SaveRecentColors()
        {
            try
            {
                var settingsStore = SettingsStore;
                if (settingsStore == null)
                {
                    //#if DEBUG
                    //                    System.Diagnostics.Debug.WriteLine($"⚠️ [边框面板] 无法访问数据库，保存失败");
                    //#endif
                    return;
                }

                var colorsToSave = _recentColors.Take(6).ToList();
                var jsonValue = System.Text.Json.JsonSerializer.Serialize(colorsToSave);
                settingsStore.SaveValue("BorderRecentColors", jsonValue);
                //#if DEBUG
                //                System.Diagnostics.Debug.WriteLine($"💾 [边框面板] 保存到数据库: {string.Join(", ", colorsToSave)}");
                //#endif
            }
            catch
            {
                //#if DEBUG
                //                System.Diagnostics.Debug.WriteLine($"⚠️ [边框面板] 保存失败: {ex.Message}");
                //#endif
            }
        }

        /// <summary>
        /// 更新最近颜色网格
        /// </summary>
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
                        ApplyBorderColor(color);
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
            ApplyBorderColor("Transparent");
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
                ApplyBorderColor(colorHex);
            }
        }

        /// <summary>
        /// 宽度滑块值变化
        /// </summary>
        private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _borderWidth = (int)e.NewValue;
            if (TxtWidthLabel != null)
                TxtWidthLabel.Text = $"宽度 {_borderWidth} px";

            ApplyBorderStyle();
        }

        /// <summary>
        /// 圆角滑块值变化
        /// </summary>
        private void CornerRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _cornerRadius = (int)e.NewValue;
            if (TxtCornerRadiusLabel != null)
                TxtCornerRadiusLabel.Text = $"圆角 {_cornerRadius} px";

            ApplyBorderStyle();
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
                    TxtOpacityLabel.Text = "透明度 100% （无边框）";
                else
                    TxtOpacityLabel.Text = $"透明度 {_opacity}%";
            }

            ApplyBorderStyle();
        }

        /// <summary>
        /// 应用边框颜色到文本框
        /// </summary>
        private void ApplyBorderColor(string colorHex)
        {
            _currentColor = colorHex;
            ApplyBorderStyle();
        }

        /// <summary>
        /// 应用完整边框样式到文本框（支持选中文本）
        /// </summary>
        private void ApplyBorderStyle()
        {
            if (_targetTextBox == null)
                return;

            // 边框样式始终应用到整个文本框，不需要选中文字
            _targetTextBox.ApplyStyle(
                borderColor: _currentColor,
                borderWidth: _borderWidth,
                borderRadius: _cornerRadius,
                borderOpacity: _opacity
            );
        }
    }
}



