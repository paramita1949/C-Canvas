using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageColorChanger.UI.Controls.Common;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 文字颜色面板（纯色 + 自定义）
    /// </summary>
    public partial class TextColorSettingsPanel : System.Windows.Controls.UserControl
    {
        private enum FillMode
        {
            Solid = 0,
            Gradient = 1
        }

        public enum ApplyMode
        {
            FontColor = 0,
            TextHighlight = 1
        }

        public Services.Interfaces.IUiSettingsStore SettingsStore { get; set; }
        public event Action<string, ApplyMode> ColorApplied;

        private DraggableTextBox _targetTextBox;
        private readonly List<string> _recentColors = new();
        private readonly List<Border> _colorChips = new();
        private bool _isUpdatingInputs;
        private bool _isDraggingSv;
        private ApplyMode _applyMode = ApplyMode.FontColor;
        private FillMode _fillMode = FillMode.Solid;
        private bool _isTransparentHighlightSelected;
        private int _opacity;
        private string _gradientStartColor = "#212121";
        private string _gradientEndColor = "#5C5C5C";
        private string _gradientDirection = "LeftToRight";
        private string _selectedGradientChipTag = string.Empty;

        private double _hue;
        private double _saturation;
        private double _value;

        public TextColorSettingsPanel()
        {
            InitializeComponent();
            UpdateRecentColorPreviews();
            SetFillMode(FillMode.Solid, rebuildPalette: true);
            SetCurrentColorHex("#000000", updateInputs: true);
        }

        public void SetApplyMode(ApplyMode mode)
        {
            _applyMode = mode;
            UpdateTransparentButtonVisibility();
        }

        public void BindTarget(DraggableTextBox textBox)
        {
            _targetTextBox = textBox;

            if (_recentColors.Count == 0)
            {
                LoadRecentColors();
            }

            var colorHex = _targetTextBox?.Data?.FontColor;
            if (_applyMode == ApplyMode.TextHighlight)
            {
                colorHex = GetSelectionHighlightColorHex();
                _isTransparentHighlightSelected = string.IsNullOrWhiteSpace(colorHex);
            }
            else
            {
                _isTransparentHighlightSelected = false;
            }

            if (string.IsNullOrWhiteSpace(colorHex) || string.Equals(colorHex, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                colorHex = "#000000";
            }

            if (SharedColorModule.TryParseGradientSpec(colorHex, out var gradientSpec))
            {
                var start = ParseColor(gradientSpec.StartColor);
                _opacity = OpacityFromAlpha(start.A);
                _gradientStartColor = ToRgbHex(start);
                _gradientEndColor = ToRgbHex(ParseColor(gradientSpec.EndColor));
                _gradientDirection = gradientSpec.Direction;
                _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(_gradientStartColor, _gradientEndColor);
                SetFillMode(FillMode.Gradient, rebuildPalette: true);
                UpdateGradientPreview(updateInputs: true);
            }
            else
            {
                var initial = ParseColor(colorHex);
                _opacity = OpacityFromAlpha(initial.A);
                SetFillMode(FillMode.Solid, rebuildPalette: true);
                SetCurrentColor(WpfColor.FromRgb(initial.R, initial.G, initial.B), updateInputs: true);
            }

            _isUpdatingInputs = true;
            if (OpacitySlider != null)
            {
                OpacitySlider.Value = _opacity;
            }
            _isUpdatingInputs = false;
            UpdateOpacityLabel();
            UpdateColorChipSelectionVisual();
            UpdateTransparentButtonVisual();
            UpdateTransparentButtonVisibility();
            UpdateGradientDirectionButtons();
            PaletteView.Visibility = Visibility.Visible;
            CustomEditorView.Visibility = Visibility.Collapsed;
        }

        private void RebuildColorBoard()
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
                    _isTransparentHighlightSelected = false;
                    if (selection.IsGradient)
                    {
                        _gradientStartColor = selection.GradientStartHex;
                        _gradientEndColor = selection.GradientEndHex;
                        _selectedGradientChipTag = selection.ChipTag;
                        UpdateGradientPreview(updateInputs: true);
                    }
                    else
                    {
                        SetCurrentColorHex(selection.SolidColorHex, updateInputs: true);
                    }

                    UpdateColorChipSelectionVisual();
                    UpdateTransparentButtonVisual();
                    ApplyCurrentColor();
                },
                size: 21,
                margin: 1.5);
        }

        private void BtnOpenCustomEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_fillMode == FillMode.Gradient)
            {
                var picked = SharedColorModule.PickSystemColor(_gradientStartColor);
                if (string.IsNullOrWhiteSpace(picked))
                {
                    return;
                }

                _gradientStartColor = ToRgbHex(ParseColor(picked));
                _gradientEndColor = SharedColorModule.ShiftBrightness(_gradientStartColor, 0.35);
                _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(_gradientStartColor, _gradientEndColor);
                AddToRecentColors(_gradientStartColor);
                UpdateGradientPreview(updateInputs: true);
                ApplyCurrentColor();
                UpdateColorChipSelectionVisual();
                return;
            }

            PaletteView.Visibility = Visibility.Collapsed;
            CustomEditorView.Visibility = Visibility.Visible;
        }

        private void BtnCustomCancel_Click(object sender, RoutedEventArgs e)
        {
            PaletteView.Visibility = Visibility.Visible;
            CustomEditorView.Visibility = Visibility.Collapsed;
        }

        private void BtnCustomConfirm_Click(object sender, RoutedEventArgs e)
        {
            _isTransparentHighlightSelected = false;
            ApplyCurrentColor();
            UpdateColorChipSelectionVisual();
            UpdateTransparentButtonVisual();
            PaletteView.Visibility = Visibility.Visible;
            CustomEditorView.Visibility = Visibility.Collapsed;
        }

        private void BtnSolidTab_Click(object sender, RoutedEventArgs e)
        {
            if (_fillMode == FillMode.Gradient)
            {
                var baseColor = ParseColor(_gradientStartColor);
                SetCurrentColor(WpfColor.FromRgb(baseColor.R, baseColor.G, baseColor.B), updateInputs: true);
            }
            SetFillMode(FillMode.Solid, rebuildPalette: true);
            UpdateColorChipSelectionVisual();
        }

        private void BtnGradientTab_Click(object sender, RoutedEventArgs e)
        {
            if (_fillMode == FillMode.Solid && CurrentColorPreview.Fill is SolidColorBrush solid)
            {
                _gradientStartColor = ToRgbHex(solid.Color);
                _gradientEndColor = SharedColorModule.ShiftBrightness(_gradientStartColor, 0.35);
                _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(_gradientStartColor, _gradientEndColor);
            }
            SetFillMode(FillMode.Gradient, rebuildPalette: true);
            UpdateGradientPreview(updateInputs: true);
            UpdateColorChipSelectionVisual();
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

            RebuildColorBoard();
            UpdateGradientDirectionButtons();

            if (_fillMode != FillMode.Gradient)
            {
                return;
            }

            UpdateGradientPreview(updateInputs: true);
            UpdateColorChipSelectionVisual();
            ApplyCurrentColor();
        }

        private void BtnHighlightTransparent_Click(object sender, RoutedEventArgs e)
        {
            if (_applyMode != ApplyMode.TextHighlight || _targetTextBox == null)
            {
                return;
            }

            _targetTextBox.ApplyHighlightToSelection("Transparent");
            _isTransparentHighlightSelected = true;
            UpdateColorChipSelectionVisual();
            UpdateTransparentButtonVisual();
            ColorApplied?.Invoke("Transparent", _applyMode);
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

            if (!rebuildPalette)
            {
                return;
            }

            RebuildColorBoard();
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

        private void UpdateGradientPreview(bool updateInputs)
        {
            var gradientSpec = BuildGradientSpec(_gradientStartColor, _gradientEndColor, _gradientDirection, _opacity);
            if (!SharedColorModule.TryCreateBrush(gradientSpec, out var previewBrush))
            {
                return;
            }

            CurrentColorPreview.Fill = previewBrush;
            _isTransparentHighlightSelected = false;
            UpdateRecentColorPreviews();
            UpdateTransparentButtonVisual();
            UpdateOpacityLabel();

            if (!updateInputs)
            {
                return;
            }

            var start = ParseColor(_gradientStartColor);
            _isUpdatingInputs = true;
            TxtHex.Text = gradientSpec ?? _gradientStartColor;
            TxtR.Text = start.R.ToString(CultureInfo.InvariantCulture);
            TxtG.Text = start.G.ToString(CultureInfo.InvariantCulture);
            TxtB.Text = start.B.ToString(CultureInfo.InvariantCulture);
            HueSlider.Value = _hue;
            _isUpdatingInputs = false;
        }

        private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingInputs)
            {
                return;
            }

            _hue = e.NewValue;
            UpdateSvHueBase();
            RefreshCurrentColorFromHsv(updateInputs: true);
        }

        private void SvPickerHost_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDraggingSv = true;
            SvPickerHost.CaptureMouse();
            UpdateSvFromPoint(e.GetPosition(SvPickerHost));
        }

        private void SvPickerHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDraggingSv)
            {
                return;
            }

            UpdateSvFromPoint(e.GetPosition(SvPickerHost));
        }

        private void SvPickerHost_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDraggingSv = false;
            SvPickerHost.ReleaseMouseCapture();
        }

        private void SvPickerHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSvSelectorVisual();
        }

        private void TxtHex_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInputs)
            {
                return;
            }

            if (_fillMode == FillMode.Gradient && SharedColorModule.TryParseGradientSpec(TxtHex.Text, out var gradientSpec))
            {
                _gradientStartColor = ToRgbHex(ParseColor(gradientSpec.StartColor));
                _gradientEndColor = ToRgbHex(ParseColor(gradientSpec.EndColor));
                _gradientDirection = gradientSpec.Direction;
                _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(_gradientStartColor, _gradientEndColor);
                UpdateGradientPreview(updateInputs: false);
                UpdateGradientDirectionButtons();
                UpdateColorChipSelectionVisual();
                ApplyCurrentColor();
                return;
            }

            var normalized = NormalizeColorHex(TxtHex.Text);
            if (normalized == null)
            {
                return;
            }

            if (_fillMode == FillMode.Gradient)
            {
                _gradientStartColor = ToRgbHex(ParseColor(normalized));
                _gradientEndColor = SharedColorModule.ShiftBrightness(_gradientStartColor, 0.35);
                _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(_gradientStartColor, _gradientEndColor);
                UpdateGradientPreview(updateInputs: false);
                UpdateColorChipSelectionVisual();
                ApplyCurrentColor();
                return;
            }

            SetCurrentColorHex(normalized, updateInputs: true);
        }

        private void TxtRgb_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInputs)
            {
                return;
            }

            if (!byte.TryParse(TxtR.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ||
                !byte.TryParse(TxtG.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ||
                !byte.TryParse(TxtB.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                return;
            }

            var color = WpfColor.FromRgb(r, g, b);
            if (_fillMode == FillMode.Gradient)
            {
                _gradientStartColor = ToRgbHex(color);
                _gradientEndColor = SharedColorModule.ShiftBrightness(_gradientStartColor, 0.35);
                _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(_gradientStartColor, _gradientEndColor);
                UpdateGradientPreview(updateInputs: true);
                UpdateColorChipSelectionVisual();
                ApplyCurrentColor();
                return;
            }

            SetCurrentColor(color, updateInputs: true);
        }

        private void SetCurrentColorHex(string colorHex, bool updateInputs)
        {
            var color = ParseColor(colorHex);
            _opacity = OpacityFromAlpha(color.A);
            _isUpdatingInputs = true;
            if (OpacitySlider != null)
            {
                OpacitySlider.Value = _opacity;
            }
            _isUpdatingInputs = false;
            UpdateOpacityLabel();
            SetCurrentColor(color, updateInputs);
        }

        private void SetCurrentColor(WpfColor color, bool updateInputs)
        {
            RgbToHsv(color, out _hue, out _saturation, out _value);
            UpdateSvHueBase();
            UpdateSvSelectorVisual();
            var effectiveColor = ComposeColorWithOpacity(color);
            CurrentColorPreview.Fill = new SolidColorBrush(effectiveColor);
            _isTransparentHighlightSelected = false;
            UpdateRecentColorPreviews();
            UpdateColorChipSelectionVisual();
            UpdateTransparentButtonVisual();
            UpdateOpacityLabel();

            if (!updateInputs)
            {
                return;
            }

            _isUpdatingInputs = true;
            var hex = ToHex(effectiveColor);
            TxtHex.Text = hex;
            TxtR.Text = color.R.ToString(CultureInfo.InvariantCulture);
            TxtG.Text = color.G.ToString(CultureInfo.InvariantCulture);
            TxtB.Text = color.B.ToString(CultureInfo.InvariantCulture);
            HueSlider.Value = _hue;
            _isUpdatingInputs = false;
        }

        private void RefreshCurrentColorFromHsv(bool updateInputs)
        {
            var color = HsvToRgb(_hue, _saturation, _value);
            var effectiveColor = ComposeColorWithOpacity(color);
            CurrentColorPreview.Fill = new SolidColorBrush(effectiveColor);
            UpdateSvSelectorVisual();
            UpdateOpacityLabel();

            if (updateInputs)
            {
                _isUpdatingInputs = true;
                TxtHex.Text = ToHex(effectiveColor);
                TxtR.Text = color.R.ToString(CultureInfo.InvariantCulture);
                TxtG.Text = color.G.ToString(CultureInfo.InvariantCulture);
                TxtB.Text = color.B.ToString(CultureInfo.InvariantCulture);
                _isUpdatingInputs = false;
            }
        }

        private void UpdateSvFromPoint(WpfPoint point)
        {
            if (SvPickerHost.ActualWidth <= 1 || SvPickerHost.ActualHeight <= 1)
            {
                return;
            }

            var x = Math.Max(0, Math.Min(SvPickerHost.ActualWidth, point.X));
            var y = Math.Max(0, Math.Min(SvPickerHost.ActualHeight, point.Y));
            _saturation = x / SvPickerHost.ActualWidth;
            _value = 1 - (y / SvPickerHost.ActualHeight);
            RefreshCurrentColorFromHsv(updateInputs: true);
        }

        private void UpdateSvHueBase()
        {
            var hueColor = HsvToRgb(_hue, 1, 1);
            SvHueBaseRect.Fill = new SolidColorBrush(hueColor);
        }

        private void UpdateSvSelectorVisual()
        {
            if (SvPickerHost.ActualWidth <= 1 || SvPickerHost.ActualHeight <= 1)
            {
                return;
            }

            var x = _saturation * SvPickerHost.ActualWidth;
            var y = (1 - _value) * SvPickerHost.ActualHeight;
            Canvas.SetLeft(SvSelector, x - (SvSelector.Width / 2));
            Canvas.SetTop(SvSelector, y - (SvSelector.Height / 2));
        }

        private void ApplyCurrentColor()
        {
            string colorSpec;
            if (_fillMode == FillMode.Gradient)
            {
                colorSpec = BuildGradientSpec(_gradientStartColor, _gradientEndColor, _gradientDirection, _opacity);
                AddToRecentColors(_gradientStartColor);
            }
            else
            {
                if (CurrentColorPreview.Fill is not SolidColorBrush brush)
                {
                    return;
                }

                colorSpec = ToHex(brush.Color);
                AddToRecentColors(ToRgbHex(brush.Color));
            }

            if (_targetTextBox != null)
            {
                if (_applyMode == ApplyMode.TextHighlight)
                {
                    _targetTextBox.ApplyHighlightToSelection(colorSpec);
                }
                else
                {
                    _targetTextBox.Data.FontColor = colorSpec;
                    if (_targetTextBox.HasTextSelection())
                    {
                        _targetTextBox.ApplyStyleToSelection(color: colorSpec);
                    }
                }
            }

            ColorApplied?.Invoke(colorSpec, _applyMode);
        }

        private void UpdateColorChipSelectionVisual()
        {
            if (_fillMode == FillMode.Gradient)
            {
                foreach (var chip in _colorChips)
                {
                    if (chip == null)
                    {
                        continue;
                    }

                    bool selected = !_isTransparentHighlightSelected &&
                                    string.Equals(chip.Tag as string, _selectedGradientChipTag, StringComparison.OrdinalIgnoreCase);

                    chip.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
                    chip.BorderBrush = selected
                        ? new SolidColorBrush(WpfColor.FromRgb(60, 60, 60))
                        : new SolidColorBrush(WpfColor.FromRgb(198, 198, 198));
                    chip.Child = selected ? BuildCheckmarkVisual(chip.Background as System.Windows.Media.Brush) : null;
                }
                return;
            }

            var selectedHex = CurrentColorPreview.Fill is SolidColorBrush brush
                ? ToRgbHex(brush.Color)
                : null;

            foreach (var chip in _colorChips)
            {
                if (chip == null)
                {
                    continue;
                }

                bool selected = !_isTransparentHighlightSelected &&
                                string.Equals(chip.Tag as string, selectedHex, StringComparison.OrdinalIgnoreCase);

                chip.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
                chip.BorderBrush = selected
                    ? new SolidColorBrush(WpfColor.FromRgb(60, 60, 60))
                    : new SolidColorBrush(WpfColor.FromRgb(198, 198, 198));

                chip.Child = selected ? BuildCheckmarkVisual(chip.Background as System.Windows.Media.Brush) : null;
            }
        }

        private UIElement BuildCheckmarkVisual(System.Windows.Media.Brush chipBrush)
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

        private void UpdateTransparentButtonVisibility()
        {
            if (BtnHighlightTransparent == null)
            {
                return;
            }

            BtnHighlightTransparent.Visibility = _applyMode == ApplyMode.TextHighlight
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateTransparentButtonVisual()
        {
            if (BtnHighlightTransparent == null)
            {
                return;
            }

            BtnHighlightTransparent.BorderBrush = _isTransparentHighlightSelected
                ? new SolidColorBrush(WpfColor.FromRgb(66, 133, 244))
                : new SolidColorBrush(WpfColor.FromRgb(207, 207, 207));
            BtnHighlightTransparent.BorderThickness = _isTransparentHighlightSelected ? new Thickness(2) : new Thickness(1);
        }

        private string GetSelectionHighlightColorHex()
        {
            if (_targetTextBox?.RichTextBox?.Selection == null || !_targetTextBox.HasTextSelection())
            {
                return null;
            }

            var value = _targetTextBox.RichTextBox.Selection
                .GetPropertyValue(System.Windows.Documents.TextElement.BackgroundProperty);

            if (value == DependencyProperty.UnsetValue || value == null)
            {
                return null;
            }

            if (value is System.Windows.Media.Brush brush)
            {
                if (brush is SolidColorBrush solid && solid.Color.A == 0)
                {
                    return null;
                }

                return SharedColorModule.EncodeBrush(brush);
            }

            return null;
        }

        private void AddToRecentColors(string colorHex)
        {
            var normalized = NormalizeColorHex(colorHex);
            if (normalized == null)
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
            RecentColorPreview1.Background = new SolidColorBrush(ParseColor(_recentColors.ElementAtOrDefault(0) ?? "#EBEFF3"));
            RecentColorPreview2.Background = new SolidColorBrush(ParseColor(_recentColors.ElementAtOrDefault(1) ?? "#8B4747"));
        }

        private void LoadRecentColors()
        {
            try
            {
                var jsonValue = SettingsStore?.GetValue("TextColorRecentColors");
                if (string.IsNullOrWhiteSpace(jsonValue))
                {
                    _recentColors.Clear();
                    UpdateRecentColorPreviews();
                    return;
                }

                var saved = JsonSerializer.Deserialize<List<string>>(jsonValue) ?? new List<string>();
                _recentColors.Clear();
                _recentColors.AddRange(saved
                    .Select(NormalizeColorHex)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(6));
            }
            catch
            {
                _recentColors.Clear();
            }

            UpdateRecentColorPreviews();
        }

        private void SaveRecentColors()
        {
            try
            {
                if (SettingsStore == null)
                {
                    return;
                }

                var jsonValue = JsonSerializer.Serialize(_recentColors.Take(6).ToList());
                SettingsStore.SaveValue("TextColorRecentColors", jsonValue);
            }
            catch
            {
                // ignore persistence failures
            }
        }

        private static string NormalizeColorHex(string colorHex)
        {
            return SharedColorModule.NormalizeColorHex(colorHex);
        }

        private static WpfColor ParseColor(string colorHex)
        {
            if (SharedColorModule.TryCreateBrush(colorHex, out var brush) &&
                SharedColorModule.TryGetRepresentativeColor(brush, out var representative))
            {
                return representative;
            }

            return SharedColorModule.ParseColor(colorHex, Colors.Black);
        }

        private static string ToHex(WpfColor color) => SharedColorModule.ToHex(color);

        private static string ToRgbHex(WpfColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        private WpfColor ComposeColorWithOpacity(WpfColor baseColor)
        {
            byte alpha = (byte)Math.Clamp((int)Math.Round(255 * (100 - Math.Clamp(_opacity, 0, 100)) / 100.0), 0, 255);
            return WpfColor.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
        }

        private static int OpacityFromAlpha(byte alpha)
        {
            return Math.Clamp(100 - (int)Math.Round((alpha / 255.0) * 100.0), 0, 100);
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _opacity = (int)Math.Round(e.NewValue);
            UpdateOpacityLabel();

            if (_isUpdatingInputs)
            {
                return;
            }

            if (_fillMode == FillMode.Gradient)
            {
                UpdateGradientPreview(updateInputs: true);
                RebuildColorBoard();
                ApplyCurrentColor();
                UpdateColorChipSelectionVisual();
                UpdateTransparentButtonVisual();
                return;
            }

            var preview = CurrentColorPreview.Fill as SolidColorBrush;
            var baseColor = preview?.Color ?? Colors.Black;
            SetCurrentColor(WpfColor.FromRgb(baseColor.R, baseColor.G, baseColor.B), updateInputs: true);
            ApplyCurrentColor();
            UpdateColorChipSelectionVisual();
            UpdateTransparentButtonVisual();
        }

        private void UpdateOpacityLabel()
        {
            if (TxtOpacityLabel == null)
            {
                return;
            }

            TxtOpacityLabel.Text = _opacity >= 100 ? "透明度 100% （完全透明）" : $"透明度 {_opacity}%";
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

        private static WpfColor HsvToRgb(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360;
            s = Math.Max(0, Math.Min(1, s));
            v = Math.Max(0, Math.Min(1, v));

            var c = v * s;
            var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            var m = v - c;

            double r1, g1, b1;
            if (h < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            return WpfColor.FromRgb(
                (byte)Math.Round((r1 + m) * 255),
                (byte)Math.Round((g1 + m) * 255),
                (byte)Math.Round((b1 + m) * 255));
        }

        private static void RgbToHsv(WpfColor color, out double h, out double s, out double v)
        {
            var r = color.R / 255.0;
            var g = color.G / 255.0;
            var b = color.B / 255.0;

            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;

            h = 0;
            if (delta > 0)
            {
                if (Math.Abs(max - r) < 0.0001) h = 60 * (((g - b) / delta) % 6);
                else if (Math.Abs(max - g) < 0.0001) h = 60 * (((b - r) / delta) + 2);
                else h = 60 * (((r - g) / delta) + 4);
            }

            if (h < 0) h += 360;
            s = max <= 0 ? 0 : delta / max;
            v = max;
        }
    }
}
