using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Core;
using WpfInput = System.Windows.Input;

namespace ImageColorChanger.UI
{
    public partial class LiveCaptionStyleControlCenterWindow : Window
    {
        public sealed class State
        {
            public string FontFamily { get; set; } = "Microsoft YaHei UI";
            public double FontSize { get; set; }
            public double LetterSpacing { get; set; }
            public double LineGapLevel { get; set; }
            public string TextColor { get; set; } = "#FFFFFF";
            public string LatestColor { get; set; } = "#FFFF00";
            public bool ShowNdiAdvanced { get; set; }
            public double NdiLineCharLimit { get; set; } = 30;
            public string NdiAlignment { get; set; } = "center";
            public bool ShowProjectionLayout { get; set; }
            public string ProjectionOrientation { get; set; } = "horizontal";
            public string ProjectionHorizontalAnchor { get; set; } = "center";
            public string ProjectionVerticalAnchor { get; set; } = "bottom";
        }

        private readonly Func<State> _loadState;
        private readonly Action<string> _setFontFamily;
        private readonly Action<double> _setFontSize;
        private readonly Action<double> _setLetterSpacing;
        private readonly Action<double> _setLineGapLevel;
        private readonly Action<string> _setTextColor;
        private readonly Action<string> _setLatestColor;
        private readonly Action<double> _setNdiLineCharLimit;
        private readonly Action<string> _setNdiAlignment;
        private readonly Action<string> _setProjectionOrientation;
        private readonly Action<string> _setProjectionHorizontalAnchor;
        private readonly Action<string> _setProjectionVerticalAnchor;
        private bool _syncingUi;
        private string _ndiAlignment = "center";
        private string _textColor = "#FFFFFF";
        private string _latestColor = "#FFFF00";
        private string _projectionOrientation = "horizontal";
        private string _projectionHorizontalAnchor = "center";
        private string _projectionVerticalAnchor = "bottom";
        private static readonly string[] TextPresetColors = { "#FFFFFF", "#000000" };
        private static readonly string[] LatestPresetColors = { "#FFFF00", "#2D7DFF" };

        public LiveCaptionStyleControlCenterWindow(
            string title,
            string subtitle,
            Func<State> loadState,
            Action<string> setFontFamily,
            Action<double> setFontSize,
            Action<double> setLetterSpacing,
            Action<double> setLineGapLevel,
            Action<string> setTextColor,
            Action<string> setLatestColor,
            Action<double> setNdiLineCharLimit = null,
            Action<string> setNdiAlignment = null,
            Action<string> setProjectionOrientation = null,
            Action<string> setProjectionHorizontalAnchor = null,
            Action<string> setProjectionVerticalAnchor = null)
        {
            _loadState = loadState ?? throw new ArgumentNullException(nameof(loadState));
            _setFontFamily = setFontFamily ?? throw new ArgumentNullException(nameof(setFontFamily));
            _setFontSize = setFontSize ?? throw new ArgumentNullException(nameof(setFontSize));
            _setLetterSpacing = setLetterSpacing ?? throw new ArgumentNullException(nameof(setLetterSpacing));
            _setLineGapLevel = setLineGapLevel ?? throw new ArgumentNullException(nameof(setLineGapLevel));
            _setTextColor = setTextColor ?? throw new ArgumentNullException(nameof(setTextColor));
            _setLatestColor = setLatestColor ?? throw new ArgumentNullException(nameof(setLatestColor));
            _setNdiLineCharLimit = setNdiLineCharLimit;
            _setNdiAlignment = setNdiAlignment;
            _setProjectionOrientation = setProjectionOrientation;
            _setProjectionHorizontalAnchor = setProjectionHorizontalAnchor;
            _setProjectionVerticalAnchor = setProjectionVerticalAnchor;

            InitializeComponent();
            TitleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "字幕样式" : title.Trim();
            SubtitleTextBlock.Text = string.IsNullOrWhiteSpace(subtitle) ? "拖动滑条或输入数值，步长 0.5。" : subtitle.Trim();

            FontFamilyComboBox.ItemsSource = new[]
            {
                new FontOption("微软雅黑", "Microsoft YaHei UI"),
                new FontOption("等线", "DengXian"),
                new FontOption("黑体", "SimHei"),
                new FontOption("宋体", "SimSun")
            };
            Loaded += (_, _) => RefreshUi();
        }

        private void RefreshUi()
        {
            State state = _loadState();
            _syncingUi = true;

            FontFamilyComboBox.SelectedValue = string.IsNullOrWhiteSpace(state.FontFamily) ? "Microsoft YaHei UI" : state.FontFamily.Trim();
            SetControlValue(FontSizeSlider, FontSizeTextBox, state.FontSize);
            SetControlValue(LetterSpacingSlider, LetterSpacingTextBox, state.LetterSpacing);
            SetControlValue(LineGapSlider, LineGapTextBox, state.LineGapLevel);
            _textColor = NormalizeHex(state.TextColor, "#FFFFFF");
            _latestColor = NormalizeHex(state.LatestColor, "#FFFF00");

            bool showNdiAdvanced = state.ShowNdiAdvanced && _setNdiLineCharLimit != null && _setNdiAlignment != null;
            NdiLineCharRow.Visibility = showNdiAdvanced ? Visibility.Visible : Visibility.Collapsed;
            NdiAlignInlinePanel.Visibility = showNdiAdvanced ? Visibility.Visible : Visibility.Collapsed;
            if (showNdiAdvanced)
            {
                SetControlValue(NdiLineCharSlider, NdiLineCharTextBox, state.NdiLineCharLimit);
                _ndiAlignment = NormalizeAlignment(state.NdiAlignment);
                UpdateAlignmentButtons();
            }

            bool showProjectionLayout = state.ShowProjectionLayout
                && _setProjectionOrientation != null
                && _setProjectionHorizontalAnchor != null
                && _setProjectionVerticalAnchor != null;
            ProjectionLayoutRow.Visibility = showProjectionLayout ? Visibility.Visible : Visibility.Collapsed;
            if (showProjectionLayout)
            {
                _projectionOrientation = NormalizeProjectionOrientation(state.ProjectionOrientation);
                _projectionHorizontalAnchor = NormalizeHorizontalAnchor(state.ProjectionHorizontalAnchor);
                _projectionVerticalAnchor = NormalizeVerticalAnchor(state.ProjectionVerticalAnchor);
                UpdateProjectionLayoutButtons();
            }
            RenderColorSwatches();
            ApplyWindowHeight(showNdiAdvanced, showProjectionLayout);

            _syncingUi = false;
        }

        private void ApplyWindowHeight(bool showNdiAdvanced, bool showProjectionLayout)
        {
            double targetHeight = showNdiAdvanced
                ? 535
                : showProjectionLayout ? 510 : 475;

            MinHeight = Math.Min(MinHeight, targetHeight);
            Height = targetHeight;
        }

        private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (_syncingUi || !IsLoaded || FontFamilyComboBox.SelectedItem is not FontOption selected)
            {
                return;
            }

            _setFontFamily(selected.Value);
        }

        private void AlignButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (_syncingUi || !IsLoaded || sender is not System.Windows.Controls.Button button)
            {
                return;
            }

            _ndiAlignment = button == AlignLeftButton ? "left" : button == AlignRightButton ? "right" : "center";
            UpdateAlignmentButtons();
            _setNdiAlignment?.Invoke(_ndiAlignment);
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _ = e;
            if (_syncingUi || !IsLoaded || sender is not Slider slider)
            {
                return;
            }

            ApplySliderValue(slider, slider.Value);
        }

        private void StepButton_Click(object sender, RoutedEventArgs e)
        {
            _ = e;
            if (sender is not System.Windows.Controls.Button button)
            {
                return;
            }

            if (button == FontSizeMinusButton) ApplySliderValue(FontSizeSlider, FontSizeSlider.Value - FontSizeControlValue.Step);
            else if (button == FontSizePlusButton) ApplySliderValue(FontSizeSlider, FontSizeSlider.Value + FontSizeControlValue.Step);
            else if (button == LetterSpacingMinusButton) ApplySliderValue(LetterSpacingSlider, LetterSpacingSlider.Value - FontSizeControlValue.Step);
            else if (button == LetterSpacingPlusButton) ApplySliderValue(LetterSpacingSlider, LetterSpacingSlider.Value + FontSizeControlValue.Step);
            else if (button == LineGapMinusButton) ApplySliderValue(LineGapSlider, LineGapSlider.Value - FontSizeControlValue.Step);
            else if (button == LineGapPlusButton) ApplySliderValue(LineGapSlider, LineGapSlider.Value + FontSizeControlValue.Step);
            else if (button == NdiLineCharMinusButton) ApplySliderValue(NdiLineCharSlider, NdiLineCharSlider.Value - 1);
            else if (button == NdiLineCharPlusButton) ApplySliderValue(NdiLineCharSlider, NdiLineCharSlider.Value + 1);
        }

        private void ValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _ = e;
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                ApplyTextBoxValue(textBox);
            }
        }

        private void ValueTextBox_KeyDown(object sender, WpfInput.KeyEventArgs e)
        {
            if (e.Key != WpfInput.Key.Enter || sender is not System.Windows.Controls.TextBox textBox)
            {
                return;
            }

            ApplyTextBoxValue(textBox);
            e.Handled = true;
        }

        private void PickColorButton_Click(object sender, RoutedEventArgs e)
        {
            _ = e;
            if (sender is not System.Windows.Controls.Button button)
            {
                return;
            }

            bool isLatest = button == LatestColorMoreButton;
            string initial = isLatest ? _latestColor : _textColor;
            string picked = PickColorHex(initial);
            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            if (isLatest)
            {
                _latestColor = NormalizeHex(picked, "#FFFF00");
                _setLatestColor(_latestColor);
            }
            else
            {
                _textColor = NormalizeHex(picked, "#FFFFFF");
                _setTextColor(_textColor);
            }

            RenderColorSwatches();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            Close();
        }

        private void ApplyTextBoxValue(System.Windows.Controls.TextBox textBox)
        {
            if (!double.TryParse(textBox.Text.Trim(), out double value))
            {
                RefreshUi();
                return;
            }

            if (textBox == FontSizeTextBox) ApplySliderValue(FontSizeSlider, value);
            else if (textBox == LetterSpacingTextBox) ApplySliderValue(LetterSpacingSlider, value);
            else if (textBox == LineGapTextBox) ApplySliderValue(LineGapSlider, value);
            else if (textBox == NdiLineCharTextBox) ApplySliderValue(NdiLineCharSlider, value);
        }

        private void ApplySliderValue(Slider slider, double value)
        {
            double step = slider == NdiLineCharSlider ? 1 : FontSizeControlValue.Step;
            double snapped = FontSizeControlValue.SnapToStep(value, slider.Minimum, slider.Maximum, step);
            System.Windows.Controls.TextBox textBox = GetTextBox(slider);

            _syncingUi = true;
            slider.Value = snapped;
            textBox.Text = slider == NdiLineCharSlider ? snapped.ToString("0") : snapped.ToString("0.#");
            _syncingUi = false;

            if (slider == FontSizeSlider) _setFontSize(snapped);
            else if (slider == LetterSpacingSlider) _setLetterSpacing(snapped);
            else if (slider == LineGapSlider) _setLineGapLevel(snapped);
            else if (slider == NdiLineCharSlider) _setNdiLineCharLimit?.Invoke(snapped);
        }

        private static void SetControlValue(Slider slider, System.Windows.Controls.TextBox textBox, double value)
        {
            double step = slider == null ? FontSizeControlValue.Step : (slider.Minimum == 8 && slider.Maximum == 80 ? 1 : FontSizeControlValue.Step);
            double snapped = FontSizeControlValue.SnapToStep(value, slider.Minimum, slider.Maximum, step);
            slider.Value = snapped;
            textBox.Text = step >= 1 ? snapped.ToString("0") : snapped.ToString("0.#");
        }

        private System.Windows.Controls.TextBox GetTextBox(Slider slider)
        {
            if (slider == FontSizeSlider) return FontSizeTextBox;
            if (slider == LetterSpacingSlider) return LetterSpacingTextBox;
            if (slider == LineGapSlider) return LineGapTextBox;
            return NdiLineCharTextBox;
        }

        private void ColorSwatchButton_Click(object sender, RoutedEventArgs e)
        {
            _ = e;
            if (sender is not System.Windows.Controls.Button button || button.Tag is not string hex)
            {
                return;
            }

            bool isLatest = string.Equals(button.Uid, "latest", StringComparison.OrdinalIgnoreCase);
            if (isLatest)
            {
                _latestColor = NormalizeHex(hex, "#FFFF00");
                _setLatestColor(_latestColor);
            }
            else
            {
                _textColor = NormalizeHex(hex, "#FFFFFF");
                _setTextColor(_textColor);
            }

            RenderColorSwatches();
        }

        private void ProjectionLayoutButton_Click(object sender, RoutedEventArgs e)
        {
            _ = e;
            if (_syncingUi || !IsLoaded || sender is not System.Windows.Controls.Button button)
            {
                return;
            }

            if (button == OrientationHorizontalButton)
            {
                _projectionOrientation = "horizontal";
                _setProjectionOrientation?.Invoke("horizontal");
            }
            else if (button == OrientationVerticalButton)
            {
                _projectionOrientation = "vertical";
                _setProjectionOrientation?.Invoke("vertical");
            }
            else if (button == ProjectionPosAButton || button == ProjectionPosBButton || button == ProjectionPosCButton)
            {
                if (_projectionOrientation == "vertical")
                {
                    string h = button == ProjectionPosAButton ? "left" : button == ProjectionPosCButton ? "right" : "center";
                    _projectionHorizontalAnchor = h;
                    _setProjectionHorizontalAnchor?.Invoke(h);
                }
                else
                {
                    string v = button == ProjectionPosAButton ? "top" : button == ProjectionPosCButton ? "bottom" : "center";
                    _projectionVerticalAnchor = v;
                    _setProjectionVerticalAnchor?.Invoke(v);
                }
            }

            UpdateProjectionLayoutButtons();
        }

        private static string NormalizeAlignment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "center";
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "left" => "left",
                "right" => "right",
                _ => "center"
            };
        }

        private static string NormalizeProjectionOrientation(string value)
        {
            return string.Equals(value?.Trim(), "vertical", StringComparison.OrdinalIgnoreCase) ? "vertical" : "horizontal";
        }

        private static string NormalizeHorizontalAnchor(string value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "left" => "left",
                "right" => "right",
                _ => "center"
            };
        }

        private static string NormalizeVerticalAnchor(string value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "top" => "top",
                "bottom" => "bottom",
                _ => "center"
            };
        }

        private void UpdateAlignmentButtons()
        {
            SetAlignButtonState(AlignLeftButton, _ndiAlignment == "left");
            SetAlignButtonState(AlignCenterButton, _ndiAlignment == "center");
            SetAlignButtonState(AlignRightButton, _ndiAlignment == "right");
        }

        private static void SetAlignButtonState(System.Windows.Controls.Button button, bool active)
        {
            if (button == null)
            {
                return;
            }

            button.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources[active ? "BrushControlPanelAccent" : "BrushControlPanelStepBg"];
            button.Foreground = active
                ? System.Windows.Media.Brushes.White
                : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushControlPanelSecondaryText"];
            button.BorderBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources[active ? "BrushControlPanelAccent" : "BrushControlPanelInputBorder"];
        }

        private void UpdateProjectionLayoutButtons()
        {
            SetAlignButtonState(OrientationHorizontalButton, _projectionOrientation == "horizontal");
            SetAlignButtonState(OrientationVerticalButton, _projectionOrientation == "vertical");
            if (_projectionOrientation == "vertical")
            {
                ProjectionPositionLabel.Text = "左右";
                ProjectionPosAButton.Content = "左";
                ProjectionPosBButton.Content = "中";
                ProjectionPosCButton.Content = "右";
                SetAlignButtonState(ProjectionPosAButton, _projectionHorizontalAnchor == "left");
                SetAlignButtonState(ProjectionPosBButton, _projectionHorizontalAnchor == "center");
                SetAlignButtonState(ProjectionPosCButton, _projectionHorizontalAnchor == "right");
            }
            else
            {
                ProjectionPositionLabel.Text = "上下";
                ProjectionPosAButton.Content = "上";
                ProjectionPosBButton.Content = "中";
                ProjectionPosCButton.Content = "下";
                SetAlignButtonState(ProjectionPosAButton, _projectionVerticalAnchor == "top");
                SetAlignButtonState(ProjectionPosBButton, _projectionVerticalAnchor == "center");
                SetAlignButtonState(ProjectionPosCButton, _projectionVerticalAnchor == "bottom");
            }
        }

        private static string NormalizeHex(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string hex = value.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex[1..];
            }

            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out _))
            {
                return $"#{hex.ToUpperInvariant()}";
            }

            return fallback;
        }

        private static bool TryHexToDrawingColor(string hex, out System.Drawing.Color color)
        {
            color = System.Drawing.Color.White;
            string normalized = NormalizeHex(hex, "#FFFFFF");
            string raw = normalized[1..];
            if (raw.Length != 6)
            {
                return false;
            }

            if (!int.TryParse(raw.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int r) ||
                !int.TryParse(raw.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int g) ||
                !int.TryParse(raw.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int b))
            {
                return false;
            }

            color = System.Drawing.Color.FromArgb(r, g, b);
            return true;
        }

        private static string PickColorHex(string initialHex)
        {
            try
            {
                using var dialog = new System.Windows.Forms.ColorDialog
                {
                    FullOpen = true,
                    AnyColor = true
                };
                if (TryHexToDrawingColor(initialHex, out var initial))
                {
                    dialog.Color = initial;
                }

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return string.Empty;
                }

                var c = dialog.Color;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private void RenderColorSwatches()
        {
            RenderColorSwatchesForPanel(TextColorSwatchPanel, _textColor, isLatest: false, TextPresetColors);
            RenderColorSwatchesForPanel(LatestColorSwatchPanel, _latestColor, isLatest: true, LatestPresetColors);
        }

        private void RenderColorSwatchesForPanel(WrapPanel panel, string selectedColor, bool isLatest, string[] presets)
        {
            panel.Children.Clear();
            foreach (string color in presets)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Style = (Style)FindResource("ColorSwatchButtonStyle"),
                    Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(color),
                    BorderThickness = string.Equals(selectedColor, color, StringComparison.OrdinalIgnoreCase) ? new Thickness(2) : new Thickness(1),
                    BorderBrush = string.Equals(selectedColor, color, StringComparison.OrdinalIgnoreCase)
                        ? (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushControlPanelAccent"]
                        : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["BrushControlPanelInputBorder"],
                    Tag = color,
                    Uid = isLatest ? "latest" : "text"
                };
                btn.Click += ColorSwatchButton_Click;
                panel.Children.Add(btn);
            }
        }

        public sealed class FontOption
        {
            public FontOption(string displayName, string value)
            {
                DisplayName = displayName;
                Value = value;
            }

            public string DisplayName { get; }
            public string Value { get; }
            public override string ToString() => DisplayName;
        }
    }
}
