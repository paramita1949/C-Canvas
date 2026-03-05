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
    /// 背景设置侧边面板（纯色 / 渐变 + 圆角 / 透明度）。
    /// </summary>
    public partial class BackgroundSettingsPanel : System.Windows.Controls.UserControl
    {
        public sealed class BackgroundStyleSelection
        {
            public bool UseGradient { get; init; }
            public string BackgroundColor { get; init; }
            public string GradientStartColor { get; init; }
            public string GradientEndColor { get; init; }
            public DraggableTextBox.BackgroundGradientDirection GradientDirection { get; init; }
            public int CornerRadius { get; init; }
            public int Opacity { get; init; }
        }

        private enum FillMode
        {
            Solid = 0,
            Gradient = 1
        }

        public Services.Interfaces.IUiSettingsStore SettingsStore { get; set; }

        private DraggableTextBox _targetTextBox;
        private Action<BackgroundStyleSelection> _backgroundChangedCallback;
        private bool _isCanvasBackgroundMode;
        private FillMode _fillMode = FillMode.Solid;
        private int _cornerRadius;
        private int _opacity = 100;
        private string _currentColor = "#FFFFFF";
        private string _selectedGradientChipTag = string.Empty;
        private string _gradientStartColor;
        private string _gradientEndColor;
        private DraggableTextBox.BackgroundGradientDirection _gradientDirection = DraggableTextBox.BackgroundGradientDirection.TopToBottom;
        private bool _isBindingTarget;
        private readonly List<string> _recentColors = new();
        private readonly List<Border> _solidChips = new();
        private readonly List<Border> _gradientChips = new();

        public BackgroundSettingsPanel()
        {
            InitializeComponent();
            RebuildSolidColorBoard();
            RebuildGradientColorBoard();
            SetFillMode(FillMode.Solid);
            UpdateRecentColorPreviews();
            UpdateGradientDirectionButtons();
        }

        public void BindTarget(DraggableTextBox textBox)
        {
            _targetTextBox = textBox;
            _backgroundChangedCallback = null;
            _isCanvasBackgroundMode = false;
            if (CornerRadiusSection != null)
            {
                CornerRadiusSection.Visibility = Visibility.Visible;
            }
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
            RebuildSolidColorBoard();
            RebuildGradientColorBoard();

            CornerRadiusSlider.Value = _cornerRadius;
            OpacitySlider.Value = _opacity;
            UpdateOpacityLabel();
            UpdateSelectionVisuals();
            UpdateGradientDirectionButtons();

            // 某些控件在赋值后会异步触发 ValueChanged，延迟一拍再解锁可避免“打开即应用样式”。
            Dispatcher.BeginInvoke(new Action(() => _isBindingTarget = false), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void BindCanvasBackground(
            string solidColorHex,
            bool useGradient,
            string gradientStartColor,
            string gradientEndColor,
            DraggableTextBox.BackgroundGradientDirection gradientDirection,
            int opacity,
            Action<BackgroundStyleSelection> onApply)
        {
            _targetTextBox = null;
            _backgroundChangedCallback = onApply;
            _isCanvasBackgroundMode = true;
            if (CornerRadiusSection != null)
            {
                CornerRadiusSection.Visibility = Visibility.Collapsed;
            }

            if (_recentColors.Count == 0)
            {
                LoadRecentColors();
            }

            _isBindingTarget = true;

            _cornerRadius = 0;
            _opacity = Math.Clamp(opacity, 0, 100);
            _gradientDirection = gradientDirection;
            RebuildSolidColorBoard();
            RebuildGradientColorBoard();

            if (useGradient &&
                !string.IsNullOrWhiteSpace(gradientStartColor) &&
                !string.IsNullOrWhiteSpace(gradientEndColor))
            {
                _gradientStartColor = NormalizeColorHex(gradientStartColor);
                _gradientEndColor = NormalizeColorHex(gradientEndColor);
                _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(_gradientStartColor, _gradientEndColor);
                _currentColor = MixColors(_gradientStartColor, _gradientEndColor, 0.5);
                SetFillMode(FillMode.Gradient);
            }
            else
            {
                _selectedGradientChipTag = string.Empty;
                _gradientStartColor = null;
                _gradientEndColor = null;
                _currentColor = NormalizeColorHex(solidColorHex) ?? "#000000";
                SetFillMode(FillMode.Solid);
            }

            CornerRadiusSlider.Value = _cornerRadius;
            OpacitySlider.Value = _opacity;
            UpdateOpacityLabel();
            UpdateSelectionVisuals();
            UpdateGradientDirectionButtons();

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

        private void RebuildSolidColorBoard()
        {
            SharedColorBoardBuilder.BuildChips(
                SolidQuickColorPanel,
                SolidPaletteGrid,
                _solidChips,
                gradientMode: false,
                opacity: _opacity,
                gradientDirection: "LeftToRight",
                onChipSelected: selection =>
                {
                    _selectedGradientChipTag = string.Empty;
                    _gradientStartColor = null;
                    _gradientEndColor = null;
                    _currentColor = selection.SolidColorHex;
                    AddToRecentColors(_currentColor);
                    UpdateSelectionVisuals();
                    ApplyBackgroundStyle();
                });
        }

        private void RebuildGradientColorBoard()
        {
            SharedColorBoardBuilder.BuildChips(
                GradientQuickPanel,
                GradientPaletteGrid,
                _gradientChips,
                gradientMode: true,
                opacity: _opacity,
                gradientDirection: ToSharedGradientDirection(_gradientDirection),
                onChipSelected: selection =>
                {
                    _selectedGradientChipTag = selection.ChipTag;
                    _gradientStartColor = selection.GradientStartHex;
                    _gradientEndColor = selection.GradientEndHex;
                    _currentColor = MixColors(_gradientStartColor, _gradientEndColor, 0.5);
                    AddToRecentColors(_currentColor);
                    UpdateSelectionVisuals();
                    ApplyBackgroundStyle();
                });
        }

        private static string MixColors(string startHex, string endHex, double ratio)
        {
            return SharedColorModule.MixColors(startHex, endHex, ratio);
        }

        private void BtnOpenSolidCustomColor_Click(object sender, RoutedEventArgs e)
        {
            var selected = PickColor();
            if (selected == null)
            {
                return;
            }

            _selectedGradientChipTag = string.Empty;
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
            _selectedGradientChipTag = SharedColorBoardBuilder.BuildGradientChipTag(selected, end);
            _gradientStartColor = selected;
            _gradientEndColor = end;
            _currentColor = MixColors(selected, end, 0.5);
            AddToRecentColors(_currentColor);
            UpdateSelectionVisuals();
            ApplyBackgroundStyle();
        }

        private static string PickColor()
        {
            return SharedColorModule.PickSystemColor();
        }

        private static string ShiftBrightness(string hex, double delta)
        {
            return SharedColorModule.ShiftBrightness(hex, delta);
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
                "RightToLeft" => DraggableTextBox.BackgroundGradientDirection.RightToLeft,
                "RadialCenter" => DraggableTextBox.BackgroundGradientDirection.RadialCenter,
                _ => _gradientDirection
            };

            RebuildGradientColorBoard();
            UpdateGradientDirectionButtons();
            ApplyBackgroundStyle();
        }

        private void UpdateGradientDirectionButtons()
        {
            SetDirButtonState(BtnGradientDirTopToBottom, _gradientDirection == DraggableTextBox.BackgroundGradientDirection.TopToBottom);
            SetDirButtonState(BtnGradientDirBottomToTop, _gradientDirection == DraggableTextBox.BackgroundGradientDirection.BottomToTop);
            SetDirButtonState(BtnGradientDirLeftToRight, _gradientDirection == DraggableTextBox.BackgroundGradientDirection.LeftToRight);
            SetDirButtonState(BtnGradientDirRightToLeft, _gradientDirection == DraggableTextBox.BackgroundGradientDirection.RightToLeft);
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
            _selectedGradientChipTag = string.Empty;
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

            if (_isCanvasBackgroundMode)
            {
                return;
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

            RebuildGradientColorBoard();
            UpdateSelectionVisuals();
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
                chip.Child = selected ? BuildCheckmarkVisual(chip.Background as System.Windows.Media.Brush) : null;
            }

            foreach (var chip in _gradientChips)
            {
                bool selected = string.Equals(chip.Tag as string, _selectedGradientChipTag, StringComparison.OrdinalIgnoreCase);
                chip.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
                chip.BorderBrush = selected ? new SolidColorBrush(WpfColor.FromRgb(60, 60, 60)) : new SolidColorBrush(WpfColor.FromRgb(198, 198, 198));
                chip.Child = selected ? BuildCheckmarkVisual(chip.Background as System.Windows.Media.Brush) : null;
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

        private void ApplyBackgroundStyle()
        {
            if (_isBindingTarget)
            {
                return;
            }

            if (_targetTextBox == null && _backgroundChangedCallback == null)
            {
                return;
            }

            bool useGradient = _fillMode == FillMode.Gradient &&
                               !string.IsNullOrWhiteSpace(_gradientStartColor) &&
                               !string.IsNullOrWhiteSpace(_gradientEndColor) &&
                               !string.Equals(_currentColor, "Transparent", StringComparison.OrdinalIgnoreCase);

            if (_backgroundChangedCallback != null)
            {
                _backgroundChangedCallback.Invoke(new BackgroundStyleSelection
                {
                    UseGradient = useGradient,
                    BackgroundColor = _currentColor,
                    GradientStartColor = _gradientStartColor,
                    GradientEndColor = _gradientEndColor,
                    GradientDirection = _gradientDirection,
                    CornerRadius = _cornerRadius,
                    Opacity = _opacity
                });
                return;
            }

            if (useGradient)
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
            return SharedColorModule.NormalizeColorHex(value);
        }

        private static WpfColor ParseColor(string colorHex)
        {
            return SharedColorModule.ParseColor(colorHex, WpfColor.FromRgb(255, 255, 255));
        }

        private static string ToSharedGradientDirection(DraggableTextBox.BackgroundGradientDirection direction)
        {
            return direction switch
            {
                DraggableTextBox.BackgroundGradientDirection.TopToBottom => "TopToBottom",
                DraggableTextBox.BackgroundGradientDirection.BottomToTop => "BottomToTop",
                DraggableTextBox.BackgroundGradientDirection.RightToLeft => "RightToLeft",
                DraggableTextBox.BackgroundGradientDirection.RadialCenter => "RadialCenter",
                _ => "LeftToRight"
            };
        }
    }
}
