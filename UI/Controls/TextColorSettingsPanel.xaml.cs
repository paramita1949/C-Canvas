using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 文字颜色面板（纯色 + 自定义）
    /// </summary>
    public partial class TextColorSettingsPanel : System.Windows.Controls.UserControl
    {
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
        private bool _isTransparentHighlightSelected;

        private double _hue;
        private double _saturation;
        private double _value;

        private readonly string[] _quickColors =
        {
            "#000000", "#FFFFFF", "#5F6368", "#D4D4D4", "#4A86E8", "#202124", "#78909C", "#F4A43A", "#0E9BA8", "#E0E72A"
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

        public TextColorSettingsPanel()
        {
            InitializeComponent();
            BuildQuickColors();
            BuildPaletteColors();
            UpdateRecentColorPreviews();
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

            SetCurrentColorHex(NormalizeColorHex(colorHex), updateInputs: true);
            UpdateColorChipSelectionVisual();
            UpdateTransparentButtonVisual();
            UpdateTransparentButtonVisibility();
            PaletteView.Visibility = Visibility.Visible;
            CustomEditorView.Visibility = Visibility.Collapsed;
        }

        private void BuildQuickColors()
        {
            QuickColorPanel.Children.Clear();
            _colorChips.Clear();
            foreach (var hex in _quickColors)
            {
                QuickColorPanel.Children.Add(CreateColorChip(hex, 21, 3, applyImmediately: true));
            }
            UpdateColorChipSelectionVisual();
        }

        private void BuildPaletteColors()
        {
            PaletteGrid.Children.Clear();
            foreach (var hex in _paletteColors)
            {
                PaletteGrid.Children.Add(CreateColorChip(hex, 21, 3, applyImmediately: true));
            }
            UpdateColorChipSelectionVisual();
        }

        private Border CreateColorChip(string colorHex, double size, double margin, bool applyImmediately)
        {
            var color = ParseColor(colorHex);
            var chip = new Border
            {
                Width = size,
                Height = size,
                Margin = new Thickness(margin / 2),
                CornerRadius = new CornerRadius(size / 2),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(198, 198, 198)),
                Background = new SolidColorBrush(color),
                Tag = NormalizeColorHex(colorHex),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            _colorChips.Add(chip);

            chip.MouseLeftButtonDown += (_, _) =>
            {
                var hex = chip.Tag as string;
                if (string.IsNullOrWhiteSpace(hex))
                {
                    return;
                }

                _isTransparentHighlightSelected = false;
                SetCurrentColorHex(hex, updateInputs: true);
                UpdateColorChipSelectionVisual();
                UpdateTransparentButtonVisual();
                if (applyImmediately)
                {
                    ApplyCurrentColor();
                }
            };

            return chip;
        }

        private void BtnOpenCustomEditor_Click(object sender, RoutedEventArgs e)
        {
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

            var normalized = NormalizeColorHex(TxtHex.Text);
            if (normalized == null)
            {
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
            SetCurrentColor(color, updateInputs: true);
        }

        private void SetCurrentColorHex(string colorHex, bool updateInputs)
        {
            var color = ParseColor(colorHex);
            SetCurrentColor(color, updateInputs);
        }

        private void SetCurrentColor(WpfColor color, bool updateInputs)
        {
            RgbToHsv(color, out _hue, out _saturation, out _value);
            UpdateSvHueBase();
            UpdateSvSelectorVisual();
            CurrentColorPreview.Fill = new SolidColorBrush(color);
            _isTransparentHighlightSelected = false;
            UpdateRecentColorPreviews();
            UpdateColorChipSelectionVisual();
            UpdateTransparentButtonVisual();

            if (!updateInputs)
            {
                return;
            }

            _isUpdatingInputs = true;
            var hex = ToHex(color);
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
            CurrentColorPreview.Fill = new SolidColorBrush(color);
            UpdateSvSelectorVisual();

            if (updateInputs)
            {
                _isUpdatingInputs = true;
                TxtHex.Text = ToHex(color);
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
            if (CurrentColorPreview.Fill is not SolidColorBrush brush)
            {
                return;
            }

            var hex = ToHex(brush.Color);
            AddToRecentColors(hex);

            if (_targetTextBox != null)
            {
                if (_applyMode == ApplyMode.TextHighlight)
                {
                    _targetTextBox.ApplyHighlightToSelection(hex);
                }
                else
                {
                    _targetTextBox.Data.FontColor = hex;
                    if (_targetTextBox.HasTextSelection())
                    {
                        _targetTextBox.ApplyStyleToSelection(color: hex);
                    }
                }
            }

            ColorApplied?.Invoke(hex, _applyMode);
        }

        private void UpdateColorChipSelectionVisual()
        {
            var selectedHex = CurrentColorPreview.Fill is SolidColorBrush brush
                ? ToHex(brush.Color)
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

                chip.Child = selected ? BuildCheckmarkVisual(chip.Background as SolidColorBrush) : null;
            }
        }

        private UIElement BuildCheckmarkVisual(SolidColorBrush chipBrush)
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

            if (value is SolidColorBrush brush)
            {
                if (brush.Color.A == 0)
                {
                    return null;
                }

                return ToHex(brush.Color);
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
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return null;
            }

            var value = colorHex.Trim();
            if (!value.StartsWith("#", StringComparison.Ordinal))
            {
                value = "#" + value;
            }

            if (value.Length == 7 || value.Length == 9)
            {
                for (var i = 1; i < value.Length; i++)
                {
                    if (!Uri.IsHexDigit(value[i]))
                    {
                        return null;
                    }
                }

                return value.ToUpperInvariant();
            }

            return null;
        }

        private static WpfColor ParseColor(string colorHex)
        {
            try
            {
                var converted = System.Windows.Media.ColorConverter.ConvertFromString(NormalizeColorHex(colorHex) ?? "#000000");
                if (converted is WpfColor color)
                {
                    return color;
                }
            }
            catch
            {
            }

            return Colors.Black;
        }

        private static string ToHex(WpfColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

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
