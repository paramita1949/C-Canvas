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
    /// 文字颜色设置侧边面板
    /// </summary>
    public partial class TextColorSettingsPanel : System.Windows.Controls.UserControl
    {
        /// <summary>
        /// 由宿主显式注入的设置存储。
        /// </summary>
        public Services.Interfaces.IUiSettingsStore SettingsStore { get; set; }

        private DraggableTextBox _targetTextBox;
        private string _currentColor = "#000000";
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
            "#B0BEC5", "#757575", "#424242", "#212121", "#FFFFFF", "#000000"
        };

        public TextColorSettingsPanel()
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

            // 从文本框读取当前文字颜色
            if (_targetTextBox != null && _targetTextBox.Data != null)
            {
                _currentColor = _targetTextBox.Data.FontColor;
                if (string.IsNullOrEmpty(_currentColor))
                {
                    _currentColor = "#000000";
                }

                // 更新UI控件
                TxtRgbInput.Text = _currentColor;
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
                        ApplyTextColor(color);
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
                ApplyTextColor(colorHex);
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
                ApplyTextColor(colorHex);
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
                    //                    System.Diagnostics.Debug.WriteLine($"⚠️ [文本颜色面板] 无法访问数据库，使用空列表");
                    //#endif
                    _recentColors = new List<string>();
                    UpdateRecentColorsGrid();
                    return;
                }

                var jsonValue = settingsStore.GetValue("TextColorRecentColors");
                if (string.IsNullOrEmpty(jsonValue))
                {
                    //#if DEBUG
                    //                    System.Diagnostics.Debug.WriteLine($"📥 [文本颜色面板] 数据库中没有最近颜色");
                    //#endif
                    _recentColors = new List<string>();
                }
                else
                {
                    var savedColors = System.Text.Json.JsonSerializer.Deserialize<List<string>>(jsonValue) ?? new List<string>();
                    //#if DEBUG
                    //                    System.Diagnostics.Debug.WriteLine($"📥 [文本颜色面板] 从数据库加载: {string.Join(", ", savedColors)} (数量={savedColors.Count})");
                    //#endif
                    _recentColors = savedColors.Take(6).ToList();
                }
            }
            catch
            {
                //#if DEBUG
                //                System.Diagnostics.Debug.WriteLine($"⚠️ [文本颜色面板] 加载失败: {ex.Message}");
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
                    //                    System.Diagnostics.Debug.WriteLine($"⚠️ [文本颜色面板] 无法访问数据库，保存失败");
                    //#endif
                    return;
                }

                var colorsToSave = _recentColors.Take(6).ToList();
                var jsonValue = System.Text.Json.JsonSerializer.Serialize(colorsToSave);
                settingsStore.SaveValue("TextColorRecentColors", jsonValue);
                //#if DEBUG
                //                System.Diagnostics.Debug.WriteLine($"💾 [文本颜色面板] 保存到数据库: {string.Join(", ", colorsToSave)}");
                //#endif
            }
            catch
            {
                //#if DEBUG
                //                System.Diagnostics.Debug.WriteLine($"⚠️ [文本颜色面板] 保存失败: {ex.Message}");
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
                        ApplyTextColor(color);
                    }
                };
                RecentColorsGrid.Children.Add(border);
            }
        }

        /// <summary>
        /// 无颜色按钮点击（透明）
        /// </summary>
        private void BtnNoColor_Click(object sender, RoutedEventArgs e)
        {
            _currentColor = "Transparent";
            TxtRgbInput.Text = "透明";
            ApplyTextColor("Transparent");
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
                ApplyTextColor(colorHex);
            }
        }

        /// <summary>
        /// 应用文字颜色
        /// </summary>
        private void ApplyTextColor(string colorHex)
        {
            if (_targetTextBox == null)
                return;

            // ✅ 只允许选中文字后修改颜色
            if (_targetTextBox.HasTextSelection())
            {
                if (colorHex == "Transparent")
                {
                    // 透明颜色需要特殊处理
                    _targetTextBox.ApplyStyleToSelection(color: "#00000000");
                }
                else
                {
                    _targetTextBox.ApplyStyleToSelection(color: colorHex);
                }
                
                // 主窗口会通过监听文本框的变化来标记内容已修改
            }
        }

        /// <summary>
        /// 关闭按钮点击
        /// </summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // 关闭面板（通过查找父窗口的 Popup）
            var popup = FindParentPopup(this);
            if (popup != null)
            {
                popup.IsOpen = false;
            }
        }

        /// <summary>
        /// 查找父 Popup
        /// </summary>
        private System.Windows.Controls.Primitives.Popup FindParentPopup(DependencyObject element)
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is System.Windows.Controls.Primitives.Popup popup)
                {
                    return popup;
                }
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}



