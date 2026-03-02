using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 边框设置面板（纯色）
    /// </summary>
    public partial class BorderSettingsPanel : System.Windows.Controls.UserControl
    {
        public Services.Interfaces.IUiSettingsStore SettingsStore { get; set; }

        private DraggableTextBox _targetTextBox;
        private int _borderWidth = 2;
        private int _cornerRadius;
        private int _opacity;
        private bool _isBindingTarget;
        private string _currentColor = "#FFFFFF";
        private DraggableTextBox.BorderLineStyle _lineStyle = DraggableTextBox.BorderLineStyle.Solid;
        private readonly List<string> _recentColors = new();
        private readonly List<Border> _colorChips = new();

        private readonly string[] _quickColors =
        {
            "#000000", "#FFFFFF", "#5F6368", "#D4D4D4", "#4A86E8", "#202124", "#78909C", "#F4A43A", "#0E9BA8", "#A8B81C"
        };

        private readonly string[] _paletteColors =
        {
            "#000000","#444444","#666666","#999999","#AAAAAA","#BEBEBE","#C8C8C8","#D8D8D8","#E2E2E2","#EFEFEF",
            "#B30000","#F8160D","#FF9800","#FFF100","#00EE00","#1CD7DE","#4D7FD8","#1A1AE9","#9013FE","#E90BE9",
            "#E6B8AF","#EBC1C1","#EEDDC5","#EEE0B5","#C2D9B7","#C5D8DD","#B8CAE8","#BACCE2","#C4BFE0","#D3BECC",
            "#D97C66","#DE8A8A","#EDC38F","#F0D57F","#9BC48D","#98BBC2","#8EAFDF","#8CB1DA","#A59CD1","#C792B0",
            "#D84A2A","#DD6565","#EDAE64","#F2CC57","#83BB6E","#73A8B6","#6493D8","#689FD1","#8678C2","#B6719D",
            "#B73411","#D90000","#E7922E","#E9BB2E","#68AA4A","#4A8A9B","#467AD0","#3F82C2","#6D5AB6","#AA4E82",
            "#98210B","#B40000","#B86D00","#C09300","#3E8621","#165F70","#2154C1","#17588E","#3E2A8C","#7A1A55",
            "#701400","#980000","#8D5200","#8C6B00","#2B6618","#0E4352","#244F97","#0E426C","#2C1D76","#5F1643"
        };

        public BorderSettingsPanel()
        {
            InitializeComponent();
            BuildColorPalette();
            UpdateRecentColorPreviews();
            UpdateLineStyleButtonStates();
        }

        public void BindTarget(DraggableTextBox textBox)
        {
            _targetTextBox = textBox;
            if (_recentColors.Count == 0)
            {
                LoadRecentColors();
            }

            if (_targetTextBox?.Data == null)
            {
                return;
            }

            _currentColor = NormalizeColorHex(_targetTextBox.Data.BorderColor) ?? "#FFFFFF";
            _borderWidth = (int)Math.Round(_targetTextBox.Data.BorderWidth);
            _cornerRadius = (int)Math.Round(_targetTextBox.Data.BorderRadius);
            _opacity = Math.Clamp(_targetTextBox.Data.BorderOpacity, 0, 100);
            if (_opacity >= 100)
            {
                // 与填充一致：默认进入可见态，点颜色即可生效。
                _opacity = 0;
            }

            _isBindingTarget = true;
            WidthSlider.Value = _borderWidth;
            CornerRadiusSlider.Value = _cornerRadius;
            OpacitySlider.Value = _opacity;
            UpdateOpacityLabel();
            UpdateColorSelectionVisual();
            UpdateLineStyleButtonStates();
            Dispatcher.BeginInvoke(new Action(() => _isBindingTarget = false), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BuildColorPalette()
        {
            QuickColorPanel.Children.Clear();
            PaletteGrid.Children.Clear();
            _colorChips.Clear();

            foreach (var hex in _quickColors)
            {
                var chip = CreateColorChip(hex, 21);
                _colorChips.Add(chip);
                QuickColorPanel.Children.Add(chip);
            }

            foreach (var hex in _paletteColors)
            {
                var chip = CreateColorChip(hex, 21);
                _colorChips.Add(chip);
                PaletteGrid.Children.Add(chip);
            }
        }

        private Border CreateColorChip(string colorHex, double size)
        {
            var normalized = NormalizeColorHex(colorHex) ?? "#FFFFFF";
            var chip = new Border
            {
                Width = size,
                Height = size,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(size / 2),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(198, 198, 198)),
                Background = new SolidColorBrush(ParseColor(normalized)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = normalized
            };

            chip.MouseLeftButtonDown += (_, _) =>
            {
                _currentColor = normalized;
                AddToRecentColors(_currentColor);
                UpdateColorSelectionVisual();
                ApplyBorderStyle();
            };

            return chip;
        }

        private void BtnOpenCustomColor_Click(object sender, RoutedEventArgs e)
        {
            using var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            var c = colorDialog.Color;
            _currentColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            AddToRecentColors(_currentColor);
            UpdateColorSelectionVisual();
            ApplyBorderStyle();
        }

        private void BtnNoBorder_Click(object sender, RoutedEventArgs e)
        {
            _currentColor = "Transparent";
            UpdateColorSelectionVisual();
            ApplyBorderStyle();
        }

        private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _borderWidth = (int)Math.Round(e.NewValue);
            if (TxtWidthLabel != null)
            {
                TxtWidthLabel.Text = $"边框 {_borderWidth} px";
            }

            if (_isBindingTarget)
            {
                return;
            }

            ApplyBorderStyle();
        }

        private void CornerRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _cornerRadius = (int)Math.Round(e.NewValue);
            if (TxtCornerRadiusLabel != null)
            {
                TxtCornerRadiusLabel.Text = $"圆角 {_cornerRadius} px";
            }

            if (_isBindingTarget)
            {
                return;
            }

            ApplyBorderStyle();
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _opacity = (int)Math.Round(e.NewValue);
            UpdateOpacityLabel();

            if (_isBindingTarget)
            {
                return;
            }

            ApplyBorderStyle();
        }

        private void UpdateOpacityLabel()
        {
            if (TxtOpacityLabel == null)
            {
                return;
            }

            TxtOpacityLabel.Text = _opacity >= 100 ? "透明度 100% （无边框）" : $"透明度 {_opacity}%";
        }

        private void BtnBorderStyleSolid_Click(object sender, RoutedEventArgs e)
        {
            _lineStyle = DraggableTextBox.BorderLineStyle.Solid;
            UpdateLineStyleButtonStates();
            ApplyBorderStyle();
        }

        private void BtnBorderStyleDashed_Click(object sender, RoutedEventArgs e)
        {
            _lineStyle = DraggableTextBox.BorderLineStyle.Dashed;
            UpdateLineStyleButtonStates();
            ApplyBorderStyle();
        }

        private void BtnBorderStyleDotted_Click(object sender, RoutedEventArgs e)
        {
            _lineStyle = DraggableTextBox.BorderLineStyle.Dotted;
            UpdateLineStyleButtonStates();
            ApplyBorderStyle();
        }

        private void BtnBorderStyleDashDot_Click(object sender, RoutedEventArgs e)
        {
            _lineStyle = DraggableTextBox.BorderLineStyle.DashDot;
            UpdateLineStyleButtonStates();
            ApplyBorderStyle();
        }

        private void BtnBorderStyleLongDash_Click(object sender, RoutedEventArgs e)
        {
            _lineStyle = DraggableTextBox.BorderLineStyle.LongDash;
            UpdateLineStyleButtonStates();
            ApplyBorderStyle();
        }

        private void UpdateLineStyleButtonStates()
        {
            SetStyleButtonSelected(BtnBorderStyleSolid, _lineStyle == DraggableTextBox.BorderLineStyle.Solid);
            SetStyleButtonSelected(BtnBorderStyleDashed, _lineStyle == DraggableTextBox.BorderLineStyle.Dashed);
            SetStyleButtonSelected(BtnBorderStyleDotted, _lineStyle == DraggableTextBox.BorderLineStyle.Dotted);
            SetStyleButtonSelected(BtnBorderStyleDashDot, _lineStyle == DraggableTextBox.BorderLineStyle.DashDot);
            SetStyleButtonSelected(BtnBorderStyleLongDash, _lineStyle == DraggableTextBox.BorderLineStyle.LongDash);
        }

        private static void SetStyleButtonSelected(System.Windows.Controls.Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            button.Background = selected
                ? new SolidColorBrush(WpfColor.FromArgb(36, 33, 150, 243))
                : new SolidColorBrush(WpfColor.FromArgb(0, 0, 0, 0));
            button.BorderBrush = selected
                ? new SolidColorBrush(WpfColor.FromRgb(33, 150, 243))
                : new SolidColorBrush(WpfColor.FromRgb(210, 210, 210));
            button.BorderThickness = new Thickness(1);
        }

        private void UpdateColorSelectionVisual()
        {
            var selected = NormalizeColorHex(_currentColor);
            foreach (var chip in _colorChips)
            {
                bool isSelected = !string.Equals(_currentColor, "Transparent", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(chip.Tag as string, selected, StringComparison.OrdinalIgnoreCase);
                chip.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                chip.BorderBrush = isSelected
                    ? new SolidColorBrush(WpfColor.FromRgb(60, 60, 60))
                    : new SolidColorBrush(WpfColor.FromRgb(198, 198, 198));
                chip.Child = isSelected ? BuildCheckmarkVisual(chip.Background as SolidColorBrush) : null;
            }
        }

        private static UIElement BuildCheckmarkVisual(SolidColorBrush chipBrush)
        {
            var color = chipBrush?.Color ?? WpfColor.FromRgb(0, 0, 0);
            double luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
            var markBrush = luminance > 160
                ? new SolidColorBrush(WpfColor.FromRgb(20, 20, 20))
                : new SolidColorBrush(WpfColor.FromRgb(255, 255, 255));

            return new Viewbox
            {
                Width = 12,
                Height = 12,
                Child = new Path
                {
                    Data = Geometry.Parse("M4 10L8 14L16 6"),
                    Stroke = markBrush,
                    StrokeThickness = 2.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Fill = System.Windows.Media.Brushes.Transparent
                }
            };
        }

        private void ApplyBorderStyle()
        {
            if (_targetTextBox == null)
            {
                return;
            }

            _targetTextBox.ApplyStyle(
                borderColor: _currentColor,
                borderWidth: _borderWidth,
                borderRadius: _cornerRadius,
                borderOpacity: _opacity);
            _targetTextBox.ApplyBorderLineStyle(_lineStyle);
        }

        private void AddToRecentColors(string colorHex)
        {
            var normalized = NormalizeColorHex(colorHex);
            if (normalized == null || string.Equals(normalized, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _recentColors.RemoveAll(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
            _recentColors.Insert(0, normalized);
            if (_recentColors.Count > 6)
            {
                _recentColors.RemoveRange(6, _recentColors.Count - 6);
            }

            UpdateRecentColorPreviews();
            SaveRecentColors();
        }

        private void UpdateRecentColorPreviews()
        {
            if (RecentColorPreview1 == null)
            {
                return;
            }

            RecentColorPreview1.Background = new SolidColorBrush(ParseColor(_recentColors.ElementAtOrDefault(0) ?? "#EBEFF3"));
            RecentColorPreview2.Background = new SolidColorBrush(ParseColor(_recentColors.ElementAtOrDefault(1) ?? "#8B4747"));
        }

        private void LoadRecentColors()
        {
            try
            {
                var jsonValue = SettingsStore?.GetValue("BorderRecentColors");
                if (string.IsNullOrWhiteSpace(jsonValue))
                {
                    _recentColors.Clear();
                    return;
                }

                var savedColors = JsonSerializer.Deserialize<List<string>>(jsonValue) ?? new List<string>();
                _recentColors.Clear();
                _recentColors.AddRange(savedColors.Select(NormalizeColorHex).Where(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("Transparent", StringComparison.OrdinalIgnoreCase)).Take(6)!);
            }
            catch
            {
                _recentColors.Clear();
            }
            finally
            {
                UpdateRecentColorPreviews();
            }
        }

        private void SaveRecentColors()
        {
            try
            {
                var jsonValue = JsonSerializer.Serialize(_recentColors.Take(6).ToList());
                SettingsStore?.SaveValue("BorderRecentColors", jsonValue);
            }
            catch
            {
            }
        }

        private static string NormalizeColorHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (string.Equals(trimmed, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                return "Transparent";
            }

            if (!trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                trimmed = "#" + trimmed;
            }

            return (trimmed.Length == 7 || trimmed.Length == 9) ? trimmed.ToUpperInvariant() : null;
        }

        private static WpfColor ParseColor(string colorHex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(colorHex) || string.Equals(colorHex, "Transparent", StringComparison.OrdinalIgnoreCase))
                {
                    return WpfColor.FromArgb(0, 0, 0, 0);
                }

                return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
            }
            catch
            {
                return WpfColor.FromRgb(255, 255, 255);
            }
        }
    }
}
