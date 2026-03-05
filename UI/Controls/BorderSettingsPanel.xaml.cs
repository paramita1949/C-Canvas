using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageColorChanger.UI.Controls.Common;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 边框设置面板（纯色）
    /// </summary>
    public partial class BorderSettingsPanel : System.Windows.Controls.UserControl
    {
        private enum FillMode
        {
            Solid = 0,
            Gradient = 1
        }

        public Services.Interfaces.IUiSettingsStore SettingsStore { get; set; }

        private DraggableTextBox _targetTextBox;
        private int _borderWidth = 2;
        private int _cornerRadius;
        private int _opacity;
        private bool _isBindingTarget;
        private string _currentColor = "#FFFFFF";
        private FillMode _fillMode = FillMode.Solid;
        private string _gradientStartColor = "#212121";
        private string _gradientEndColor = "#5C5C5C";
        private string _gradientDirection = "LeftToRight";
        private string _selectedGradientChipTag = string.Empty;
        private DraggableTextBox.BorderLineStyle _lineStyle = DraggableTextBox.BorderLineStyle.Solid;
        private readonly List<string> _recentColors = new();
        private readonly List<Border> _colorChips = new();

        public BorderSettingsPanel()
        {
            InitializeComponent();
            UpdateRecentColorPreviews();
            UpdateLineStyleButtonStates();
            SetFillMode(FillMode.Solid, rebuildPalette: true);
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

            var rawBorder = NormalizeColorHex(_targetTextBox.Data.BorderColor) ?? "#FFFFFF";
            if (SharedColorModule.TryParseGradientSpec(rawBorder, out var gradientSpec))
            {
                _fillMode = FillMode.Gradient;
                _gradientStartColor = ToRgbHex(ParseColor(gradientSpec.StartColor));
                _gradientEndColor = ToRgbHex(ParseColor(gradientSpec.EndColor));
                _gradientDirection = gradientSpec.Direction;
                _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(_gradientStartColor, _gradientEndColor);
                _currentColor = rawBorder;
            }
            else
            {
                _fillMode = FillMode.Solid;
                _currentColor = rawBorder;
            }
            _borderWidth = (int)Math.Round(_targetTextBox.Data.BorderWidth);
            _cornerRadius = (int)Math.Round(_targetTextBox.Data.BorderRadius);
            _opacity = Math.Clamp(_targetTextBox.Data.BorderOpacity, 0, 100);
            if (_opacity >= 100)
            {
                // 与填充一致：默认进入可见态，点颜色即可生效。
                _opacity = 0;
            }
            if (_fillMode == FillMode.Gradient)
            {
                _currentColor = BuildGradientSpec(_gradientStartColor, _gradientEndColor, _gradientDirection, _opacity);
            }

            _isBindingTarget = true;
            WidthSlider.Value = _borderWidth;
            CornerRadiusSlider.Value = _cornerRadius;
            OpacitySlider.Value = _opacity;
            UpdateOpacityLabel();
            SetFillMode(_fillMode, rebuildPalette: true);
            UpdateColorSelectionVisual();
            UpdateGradientDirectionButtons();
            UpdateLineStyleButtonStates();
            Dispatcher.BeginInvoke(new Action(() => _isBindingTarget = false), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BuildColorPalette()
        {
            SharedColorBoardBuilder.BuildChips(
                QuickColorPanel,
                PaletteGrid,
                _colorChips,
                gradientMode: _fillMode == FillMode.Gradient,
                opacity: _opacity,
                gradientDirection: _gradientDirection,
                onChipSelected: selection =>
                {
                    if (selection.IsGradient)
                    {
                        _gradientStartColor = selection.GradientStartHex;
                        _gradientEndColor = selection.GradientEndHex;
                        _selectedGradientChipTag = selection.ChipTag;
                        _currentColor = BuildGradientSpec(_gradientStartColor, _gradientEndColor, _gradientDirection, _opacity);
                        AddToRecentColors(_gradientStartColor);
                    }
                    else
                    {
                        _currentColor = selection.SolidColorHex;
                        AddToRecentColors(_currentColor);
                    }

                    UpdateColorSelectionVisual();
                    ApplyBorderStyle();
                });
        }

        private void BtnOpenCustomColor_Click(object sender, RoutedEventArgs e)
        {
            var baseColor = _fillMode == FillMode.Gradient ? _gradientStartColor : _currentColor;
            var picked = SharedColorModule.PickSystemColor(baseColor);
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            if (_fillMode == FillMode.Gradient)
            {
                _gradientStartColor = ToRgbHex(ParseColor(picked));
                _gradientEndColor = SharedColorModule.ShiftBrightness(_gradientStartColor, 0.35);
                _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(_gradientStartColor, _gradientEndColor);
                _currentColor = BuildGradientSpec(_gradientStartColor, _gradientEndColor, _gradientDirection, _opacity);
                AddToRecentColors(_gradientStartColor);
                BuildColorPalette();
            }
            else
            {
                _currentColor = picked;
                AddToRecentColors(_currentColor);
            }

            UpdateColorSelectionVisual();
            ApplyBorderStyle();
        }

        private void BtnSolidTab_Click(object sender, RoutedEventArgs e)
        {
            SetFillMode(FillMode.Solid, rebuildPalette: true);
            if (SharedColorModule.TryParseGradientSpec(_currentColor, out var spec))
            {
                _currentColor = ToRgbHex(ParseColor(spec.StartColor));
            }
            UpdateColorSelectionVisual();
            ApplyBorderStyle();
        }

        private void BtnGradientTab_Click(object sender, RoutedEventArgs e)
        {
            SetFillMode(FillMode.Gradient, rebuildPalette: true);
            _currentColor = BuildGradientSpec(_gradientStartColor, _gradientEndColor, _gradientDirection, _opacity);
            UpdateColorSelectionVisual();
            ApplyBorderStyle();
        }

        private void BtnGradientDirection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tag)
            {
                return;
            }

            _gradientDirection = tag switch
            {
                "TopToBottom" => "TopToBottom",
                "BottomToTop" => "BottomToTop",
                "LeftToRight" => "LeftToRight",
                "RightToLeft" => "RightToLeft",
                "RadialCenter" => "RadialCenter",
                _ => "LeftToRight"
            };

            BuildColorPalette();
            UpdateGradientDirectionButtons();

            if (_fillMode != FillMode.Gradient)
            {
                return;
            }

            _currentColor = BuildGradientSpec(_gradientStartColor, _gradientEndColor, _gradientDirection, _opacity);
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

            if (_fillMode == FillMode.Gradient)
            {
                _currentColor = BuildGradientSpec(_gradientStartColor, _gradientEndColor, _gradientDirection, _opacity);
                BuildColorPalette();
                UpdateColorSelectionVisual();
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

            if (_fillMode == FillMode.Gradient)
            {
                BuildColorPalette();
                UpdateColorSelectionVisual();
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
            if (_fillMode == FillMode.Gradient)
            {
                foreach (var chip in _colorChips)
                {
                    bool isSelected = !string.Equals(_currentColor, "Transparent", StringComparison.OrdinalIgnoreCase) &&
                                      string.Equals(chip.Tag as string, _selectedGradientChipTag, StringComparison.OrdinalIgnoreCase);
                    chip.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                    chip.BorderBrush = isSelected
                        ? new SolidColorBrush(WpfColor.FromRgb(60, 60, 60))
                        : new SolidColorBrush(WpfColor.FromRgb(198, 198, 198));
                    chip.Child = isSelected ? BuildCheckmarkVisual(chip.Background as System.Windows.Media.Brush) : null;
                }
                return;
            }

            var selected = NormalizeColorHex(_currentColor);
            foreach (var chip in _colorChips)
            {
                bool isSelected = !string.Equals(_currentColor, "Transparent", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(chip.Tag as string, selected, StringComparison.OrdinalIgnoreCase);
                chip.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                chip.BorderBrush = isSelected
                    ? new SolidColorBrush(WpfColor.FromRgb(60, 60, 60))
                    : new SolidColorBrush(WpfColor.FromRgb(198, 198, 198));
                chip.Child = isSelected ? BuildCheckmarkVisual(chip.Background as System.Windows.Media.Brush) : null;
            }
        }

        private static UIElement BuildCheckmarkVisual(System.Windows.Media.Brush chipBrush)
        {
            WpfColor color = WpfColor.FromRgb(0, 0, 0);
            if (chipBrush is SolidColorBrush solid)
            {
                color = solid.Color;
            }
            else if (chipBrush is GradientBrush gradient && gradient.GradientStops?.Count > 0)
            {
                color = gradient.GradientStops[0].Color;
            }
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

            if (_fillMode == FillMode.Gradient && !string.Equals(_currentColor, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                _currentColor = BuildGradientSpec(_gradientStartColor, _gradientEndColor, _gradientDirection, _opacity);
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

        private void SetFillMode(FillMode mode, bool rebuildPalette)
        {
            _fillMode = mode;
            if (BtnSolidTab == null || BtnGradientTab == null)
            {
                return;
            }

            bool isSolid = mode == FillMode.Solid;
            if (TxtSolidTab != null && TxtGradientTab != null && SolidTabUnderline != null && GradientTabUnderline != null)
            {
                TxtSolidTab.Foreground = isSolid ? new SolidColorBrush(WpfColor.FromRgb(34, 34, 34)) : new SolidColorBrush(WpfColor.FromRgb(102, 102, 102));
                TxtGradientTab.Foreground = isSolid ? new SolidColorBrush(WpfColor.FromRgb(102, 102, 102)) : new SolidColorBrush(WpfColor.FromRgb(34, 34, 34));
                SolidTabUnderline.Visibility = isSolid ? Visibility.Visible : Visibility.Collapsed;
                GradientTabUnderline.Visibility = isSolid ? Visibility.Collapsed : Visibility.Visible;
            }

            if (TxtPaletteTitle != null)
            {
                TxtPaletteTitle.Text = isSolid ? "纯浅色" : "渐变";
            }

            if (GradientDirectionPanel != null)
            {
                GradientDirectionPanel.Visibility = isSolid ? Visibility.Collapsed : Visibility.Visible;
            }
            UpdateGradientDirectionButtons();

            if (rebuildPalette)
            {
                BuildColorPalette();
            }
        }

        private void UpdateGradientDirectionButtons()
        {
            SetDirButtonState(BtnGradientDirTopToBottom, _gradientDirection == "TopToBottom");
            SetDirButtonState(BtnGradientDirBottomToTop, _gradientDirection == "BottomToTop");
            SetDirButtonState(BtnGradientDirLeftToRight, _gradientDirection == "LeftToRight");
            SetDirButtonState(BtnGradientDirRightToLeft, _gradientDirection == "RightToLeft");
            SetDirButtonState(BtnGradientDirRadialCenter, _gradientDirection == "RadialCenter");
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

        private static string BuildGradientSpec(string startHex, string endHex, string direction, int opacity)
        {
            var start = ToRgbHex(ParseColor(startHex));
            var end = ToRgbHex(ParseColor(endHex));
            if (opacity <= 0)
            {
                return SharedColorModule.BuildGradientSpec(start, end, direction);
            }

            return SharedColorModule.BuildGradientSpec(
                SharedColorModule.ApplyOpacityToHex(start, opacity),
                SharedColorModule.ApplyOpacityToHex(end, opacity),
                direction);
        }

        private static string NormalizeColorHex(string value)
        {
            return SharedColorModule.NormalizeColorHex(value);
        }

        private static WpfColor ParseColor(string colorHex)
        {
            if (SharedColorModule.TryCreateBrush(colorHex, out var brush) &&
                SharedColorModule.TryGetRepresentativeColor(brush, out var representative))
            {
                return representative;
            }

            return SharedColorModule.ParseColor(colorHex, WpfColor.FromRgb(255, 255, 255));
        }

        private static string ToRgbHex(WpfColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
