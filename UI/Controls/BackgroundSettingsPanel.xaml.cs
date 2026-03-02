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
    /// 背景设置侧边面板（纯色 / 渐变 + 圆角 / 透明度）。
    /// </summary>
    public partial class BackgroundSettingsPanel : System.Windows.Controls.UserControl
    {
        private enum FillMode
        {
            Solid = 0,
            Gradient = 1
        }

        public Services.Interfaces.IUiSettingsStore SettingsStore { get; set; }

        private DraggableTextBox _targetTextBox;
        private FillMode _fillMode = FillMode.Solid;
        private int _cornerRadius;
        private int _opacity = 100;
        private string _currentColor = "#FFFFFF";
        private string _selectedGradientKey = string.Empty;
        private string _gradientStartColor;
        private string _gradientEndColor;
        private DraggableTextBox.BackgroundGradientDirection _gradientDirection = DraggableTextBox.BackgroundGradientDirection.TopToBottom;
        private bool _isBindingTarget;
        private readonly List<string> _recentColors = new();
        private readonly List<Border> _solidChips = new();
        private readonly List<Border> _gradientChips = new();

        private readonly string[] _solidQuickColors =
        {
            "#000000", "#FFFFFF", "#5F6368", "#D4D4D4", "#4A86E8", "#202124", "#78909C", "#F4A43A", "#0E9BA8", "#A8B81C"
        };

        private readonly string[] _solidPaletteColors =
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

        private readonly (string Start, string End)[] _gradientQuickPresets =
        {
            ("#212121", "#5C5C5C"), ("#1A1A1A", "#7E8F9A"), ("#F4A43A", "#F7CA7A"), ("#0E9BA8", "#25C7D3"), ("#8AA322", "#B9CC4D"),
            ("#9E0B0F", "#E44147"), ("#9F5E00", "#E79B34"), ("#2F7B1F", "#72BF54"), ("#165AA9", "#4E8DE0"), ("#3D247D", "#7A5AC1")
        };

        private readonly (string Start, string End)[] _gradientPalettePresets =
        {
            ("#000000", "#444444"), ("#2A2A2A", "#666666"), ("#4A4A4A", "#8C8C8C"), ("#7A7A7A", "#B0B0B0"), ("#A0A0A0", "#CECECE"), ("#B9B9B9", "#E0E0E0"), ("#D0D0D0", "#F0F0F0"),
            ("#D90000", "#FF6A6A"), ("#A85A00", "#F2B46B"), ("#B88E00", "#F1D56A"), ("#2D7A1E", "#82C66D"), ("#1B5FB0", "#78A9E8"), ("#244596", "#6687D8"), ("#5B3DA1", "#9E86D8"),
            ("#8A1E1E", "#E18A8A"), ("#A0671E", "#E8C08F"), ("#A08B22", "#E5D08C"), ("#3F7F36", "#9EC89A"), ("#3A7C8D", "#9CC8D2"), ("#436AA0", "#9FB9E3"), ("#6B4A9D", "#B9A4DE"),
            ("#B00000", "#F20000"), ("#C06F00", "#F39A23"), ("#C9A000", "#F4CB27"), ("#2F8F18", "#5FBE3F"), ("#1A5FAF", "#3F86DA"), ("#124D8D", "#2E76C6"), ("#4D2D9E", "#7C56CE")
        };

        public BackgroundSettingsPanel()
        {
            InitializeComponent();
            BuildSolidChips();
            BuildGradientChips();
            SetFillMode(FillMode.Solid);
            UpdateRecentColorPreviews();
            UpdateGradientDirectionButtons();
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

            _isBindingTarget = true;
            _currentColor = NormalizeColorHex(_targetTextBox.Data.BackgroundColor) ?? "#FFFFFF";
            _cornerRadius = (int)Math.Round(_targetTextBox.Data.BackgroundRadius);
            _opacity = Math.Clamp(_targetTextBox.Data.BackgroundOpacity, 0, 100);
            if (_opacity >= 100)
            {
                // 默认进入可见态：点击颜色立即填充。
                _opacity = 0;
            }

            CornerRadiusSlider.Value = _cornerRadius;
            OpacitySlider.Value = _opacity;
            UpdateOpacityLabel();
            UpdateSelectionVisuals();
            UpdateGradientDirectionButtons();

            // 某些控件在赋值后会异步触发 ValueChanged，延迟一拍再解锁可避免“打开即应用样式”。
            Dispatcher.BeginInvoke(new Action(() => _isBindingTarget = false), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BtnSolidTab_Click(object sender, RoutedEventArgs e) => SetFillMode(FillMode.Solid);
        private void BtnGradientTab_Click(object sender, RoutedEventArgs e) => SetFillMode(FillMode.Gradient);

        private void SetFillMode(FillMode mode)
        {
            _fillMode = mode;
            SolidView.Visibility = mode == FillMode.Solid ? Visibility.Visible : Visibility.Collapsed;
            GradientView.Visibility = mode == FillMode.Gradient ? Visibility.Visible : Visibility.Collapsed;

            bool isSolid = mode == FillMode.Solid;
            TxtSolidTab.Foreground = isSolid ? new SolidColorBrush(WpfColor.FromRgb(34, 34, 34)) : new SolidColorBrush(WpfColor.FromRgb(102, 102, 102));
            TxtGradientTab.Foreground = isSolid ? new SolidColorBrush(WpfColor.FromRgb(102, 102, 102)) : new SolidColorBrush(WpfColor.FromRgb(34, 34, 34));
            SolidTabUnderline.Visibility = isSolid ? Visibility.Visible : Visibility.Collapsed;
            GradientTabUnderline.Visibility = isSolid ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BuildSolidChips()
        {
            SolidQuickColorPanel.Children.Clear();
            SolidPaletteGrid.Children.Clear();
            _solidChips.Clear();

            foreach (var hex in _solidQuickColors)
            {
                var chip = CreateSolidChip(hex, 21);
                _solidChips.Add(chip);
                SolidQuickColorPanel.Children.Add(chip);
            }

            foreach (var hex in _solidPaletteColors)
            {
                var chip = CreateSolidChip(hex, 21);
                _solidChips.Add(chip);
                SolidPaletteGrid.Children.Add(chip);
            }
        }

        private void BuildGradientChips()
        {
            GradientQuickPanel.Children.Clear();
            GradientPaletteGrid.Children.Clear();
            _gradientChips.Clear();

            foreach (var preset in _gradientQuickPresets)
            {
                var chip = CreateGradientChip(preset.Start, preset.End, 21);
                _gradientChips.Add(chip);
                GradientQuickPanel.Children.Add(chip);
            }

            foreach (var preset in _gradientPalettePresets)
            {
                var chip = CreateGradientChip(preset.Start, preset.End, 21);
                _gradientChips.Add(chip);
                GradientPaletteGrid.Children.Add(chip);
            }
        }

        private Border CreateSolidChip(string colorHex, double size)
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
                _selectedGradientKey = string.Empty;
                _gradientStartColor = null;
                _gradientEndColor = null;
                _currentColor = normalized;
                AddToRecentColors(_currentColor);
                UpdateSelectionVisuals();
                ApplyBackgroundStyle();
            };

            return chip;
        }

        private Border CreateGradientChip(string startHex, string endHex, double size)
        {
            string key = $"{startHex}|{endHex}";
            var chip = new Border
            {
                Width = size,
                Height = size,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(size / 2),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(198, 198, 198)),
                Background = new LinearGradientBrush(ParseColor(startHex), ParseColor(endHex), new System.Windows.Point(0, 0), new System.Windows.Point(1, 1)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = key
            };

            chip.MouseLeftButtonDown += (_, _) =>
            {
                _selectedGradientKey = key;
                _gradientStartColor = startHex;
                _gradientEndColor = endHex;
                _currentColor = MixColors(startHex, endHex, 0.5);
                AddToRecentColors(_currentColor);
                UpdateSelectionVisuals();
                ApplyBackgroundStyle();
            };

            return chip;
        }

        private static string MixColors(string startHex, string endHex, double ratio)
        {
            ratio = Math.Clamp(ratio, 0, 1);
            var s = ParseColor(startHex);
            var e = ParseColor(endHex);
            byte r = (byte)Math.Round((s.R * (1 - ratio)) + (e.R * ratio));
            byte g = (byte)Math.Round((s.G * (1 - ratio)) + (e.G * ratio));
            byte b = (byte)Math.Round((s.B * (1 - ratio)) + (e.B * ratio));
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private void BtnOpenSolidCustomColor_Click(object sender, RoutedEventArgs e)
        {
            var selected = PickColor();
            if (selected == null)
            {
                return;
            }

            _selectedGradientKey = string.Empty;
            _gradientStartColor = null;
            _gradientEndColor = null;
            _currentColor = selected;
            AddToRecentColors(_currentColor);
            UpdateSelectionVisuals();
            ApplyBackgroundStyle();
        }

        private void BtnOpenGradientCustomColor_Click(object sender, RoutedEventArgs e)
        {
            var selected = PickColor();
            if (selected == null)
            {
                return;
            }

            var end = ShiftBrightness(selected, 0.35);
            _selectedGradientKey = $"custom|{selected}|{end}";
            _gradientStartColor = selected;
            _gradientEndColor = end;
            _currentColor = MixColors(selected, end, 0.5);
            AddToRecentColors(_currentColor);
            UpdateSelectionVisuals();
            ApplyBackgroundStyle();
        }

        private static string PickColor()
        {
            using var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return null;
            }

            var c = colorDialog.Color;
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private static string ShiftBrightness(string hex, double delta)
        {
            var c = ParseColor(hex);
            byte Shift(byte v) => (byte)Math.Clamp((int)Math.Round(v + (255 - v) * delta), 0, 255);
            return $"#{Shift(c.R):X2}{Shift(c.G):X2}{Shift(c.B):X2}";
        }

        private void BtnGradientDirection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tag)
            {
                return;
            }

            _gradientDirection = tag switch
            {
                "TopToBottom" => DraggableTextBox.BackgroundGradientDirection.TopToBottom,
                "BottomToTop" => DraggableTextBox.BackgroundGradientDirection.BottomToTop,
                "LeftToRight" => DraggableTextBox.BackgroundGradientDirection.LeftToRight,
                "RadialCenter" => DraggableTextBox.BackgroundGradientDirection.RadialCenter,
                _ => _gradientDirection
            };

            UpdateGradientDirectionButtons();
            ApplyBackgroundStyle();
        }

        private void UpdateGradientDirectionButtons()
        {
            SetDirButtonState(BtnGradientDirTopToBottom, _gradientDirection == DraggableTextBox.BackgroundGradientDirection.TopToBottom);
            SetDirButtonState(BtnGradientDirBottomToTop, _gradientDirection == DraggableTextBox.BackgroundGradientDirection.BottomToTop);
            SetDirButtonState(BtnGradientDirLeftToRight, _gradientDirection == DraggableTextBox.BackgroundGradientDirection.LeftToRight);
            SetDirButtonState(BtnGradientDirRadialCenter, _gradientDirection == DraggableTextBox.BackgroundGradientDirection.RadialCenter);
        }

        private static void SetDirButtonState(System.Windows.Controls.Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            button.Background = selected
                ? new SolidColorBrush(WpfColor.FromArgb(36, 33, 150, 243))
                : System.Windows.Media.Brushes.Transparent;
            button.BorderBrush = selected
                ? new SolidColorBrush(WpfColor.FromRgb(33, 150, 243))
                : System.Windows.Media.Brushes.Transparent;
            button.BorderThickness = selected ? new Thickness(1) : new Thickness(0);
        }

        private void BtnNoFill_Click(object sender, RoutedEventArgs e)
        {
            _selectedGradientKey = string.Empty;
            _gradientStartColor = null;
            _gradientEndColor = null;
            _currentColor = "Transparent";
            UpdateSelectionVisuals();
            ApplyBackgroundStyle();
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

            ApplyBackgroundStyle();
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _opacity = (int)Math.Round(e.NewValue);
            UpdateOpacityLabel();

            if (_isBindingTarget)
            {
                return;
            }

            ApplyBackgroundStyle();
        }

        private void UpdateOpacityLabel()
        {
            if (TxtOpacityLabel == null)
            {
                return;
            }

            TxtOpacityLabel.Text = _opacity >= 100 ? "透明度 100% （无背景）" : $"透明度 {_opacity}%";
        }

        private void UpdateSelectionVisuals()
        {
            var selectedSolid = NormalizeColorHex(_currentColor);
            foreach (var chip in _solidChips)
            {
                bool selected = !_currentColor.Equals("Transparent", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(chip.Tag as string, selectedSolid, StringComparison.OrdinalIgnoreCase);
                chip.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
                chip.BorderBrush = selected ? new SolidColorBrush(WpfColor.FromRgb(60, 60, 60)) : new SolidColorBrush(WpfColor.FromRgb(198, 198, 198));
                chip.Child = selected ? BuildCheckmarkVisual(chip.Background as SolidColorBrush) : null;
            }

            foreach (var chip in _gradientChips)
            {
                bool selected = string.Equals(chip.Tag as string, _selectedGradientKey, StringComparison.OrdinalIgnoreCase);
                chip.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
                chip.BorderBrush = selected ? new SolidColorBrush(WpfColor.FromRgb(60, 60, 60)) : new SolidColorBrush(WpfColor.FromRgb(198, 198, 198));
                chip.Child = selected ? BuildGradientCheckmarkVisual() : null;
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
                Width = 14,
                Height = 14,
                Child = new Path
                {
                    Data = Geometry.Parse("M4 10L8 14L16 6"),
                    Stroke = markBrush,
                    StrokeThickness = 2.2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Fill = System.Windows.Media.Brushes.Transparent
                }
            };
        }

        private static UIElement BuildGradientCheckmarkVisual()
        {
            return new Viewbox
            {
                Width = 14,
                Height = 14,
                Child = new Path
                {
                    Data = Geometry.Parse("M4 10L8 14L16 6"),
                    Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 255, 255)),
                    StrokeThickness = 2.2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Fill = System.Windows.Media.Brushes.Transparent
                }
            };
        }

        private void ApplyBackgroundStyle()
        {
            if (_isBindingTarget)
            {
                return;
            }

            if (_targetTextBox == null)
            {
                return;
            }

            if (_fillMode == FillMode.Gradient &&
                !string.IsNullOrWhiteSpace(_gradientStartColor) &&
                !string.IsNullOrWhiteSpace(_gradientEndColor) &&
                !string.Equals(_currentColor, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                _targetTextBox.ApplyBackgroundGradient(
                    _gradientStartColor,
                    _gradientEndColor,
                    direction: _gradientDirection,
                    backgroundRadius: _cornerRadius,
                    backgroundOpacity: _opacity);
                return;
            }

            _targetTextBox.ClearBackgroundGradient();
            _targetTextBox.ApplyStyle(
                backgroundColor: _currentColor,
                backgroundRadius: _cornerRadius,
                backgroundOpacity: _opacity);
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
            if (SolidRecentColorPreview1 == null)
            {
                return;
            }

            SolidRecentColorPreview1.Background = new SolidColorBrush(ParseColor(_recentColors.ElementAtOrDefault(0) ?? "#EBEFF3"));
            SolidRecentColorPreview2.Background = new SolidColorBrush(ParseColor(_recentColors.ElementAtOrDefault(1) ?? "#8B4747"));
            GradientRecentColorPreview1.Background = new SolidColorBrush(ParseColor(_recentColors.ElementAtOrDefault(0) ?? "#EBEFF3"));
        }

        private void LoadRecentColors()
        {
            try
            {
                var jsonValue = SettingsStore?.GetValue("BackgroundRecentColors");
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
                SettingsStore?.SaveValue("BackgroundRecentColors", jsonValue);
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
