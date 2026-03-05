using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ImageColorChanger.Services.TextEditor.Components.Notice;
using Forms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI.Controls
{
    public partial class NoticeSettingsPanel : System.Windows.Controls.UserControl
    {
        private const bool EnableNoticeColorTrace = true;
        public event Action<NoticeComponentConfig> ConfigChanged;

        private bool _isBinding;
        private string _selectedDefaultColorHex = NoticeComponentConfig.DefaultNoticeColorHex;
        private static readonly string[] PresetColorHexes =
        {
            "#FF8A00",
            "#FF3B30",
            "#0EA5E9",
            "#22C55E",
            "#111111",
            "#FFFFFF"
        };

        public NoticeSettingsPanel()
        {
            InitializeComponent();
        }

        public void BindConfig(NoticeComponentConfig config)
        {
            var normalized = NoticeComponentConfigCodec.Normalize(config);
            _isBinding = true;
            try
            {
                SelectPositionFlags(normalized.PositionFlags);
                SelectDirection(normalized.Direction);
                SelectSpeedLevel(normalized.Speed);
                SelectDuration(normalized.DurationMinutes);
                SelectBarHeightLevel(normalized.BarHeight);
                AutoCloseCheckBox.IsChecked = normalized.AutoClose;
                DurationComboBox.IsEnabled = normalized.AutoClose;
                SelectDefaultColor(normalized.DefaultColorHex);
            }
            finally
            {
                _isBinding = false;
            }
        }

        private void DirectionOption_Checked(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            RaiseConfigChanged();
        }

        private void PositionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;

            if (!_isBinding &&
                PositionTopCheckBox.IsChecked != true &&
                PositionCenterCheckBox.IsChecked != true &&
                PositionBottomCheckBox.IsChecked != true)
            {
                _isBinding = true;
                PositionTopCheckBox.IsChecked = true;
                _isBinding = false;
            }

            RaiseConfigChanged();
        }

        private void SpeedLevelOption_Checked(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            RaiseConfigChanged();
        }

        private void DurationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            RaiseConfigChanged();
        }

        private void BarHeightOption_Checked(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            RaiseConfigChanged();
        }

        private void AutoCloseCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (DurationComboBox != null && AutoCloseCheckBox != null)
            {
                DurationComboBox.IsEnabled = AutoCloseCheckBox.IsChecked == true;
            }

            RaiseConfigChanged();
        }

        private void DefaultColorSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string color && !string.IsNullOrWhiteSpace(color))
            {
                _selectedDefaultColorHex = color.Trim().ToUpperInvariant();
                UpdateColorSwatchSelectionVisual();
                if (EnableNoticeColorTrace)
                {
                    Debug.WriteLine($"[NoticeColorTrace][Panel] 预设色点击: {_selectedDefaultColorHex}");
                }
            }
            _ = e;
            RaiseConfigChanged();
        }

        private void CustomColorButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;

            var initialColor = TryParseColor(_selectedDefaultColorHex, out var parsed)
                ? parsed
                : Colors.White;
            using var colorDialog = new Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                SolidColorOnly = false,
                Color = System.Drawing.Color.FromArgb(initialColor.R, initialColor.G, initialColor.B)
            };

            var owner = Window.GetWindow(this);
            Forms.DialogResult result;
            if (owner != null)
            {
                var nativeOwner = new Forms.NativeWindow();
                try
                {
                    nativeOwner.AssignHandle(new WindowInteropHelper(owner).Handle);
                    result = colorDialog.ShowDialog(nativeOwner);
                }
                finally
                {
                    nativeOwner.ReleaseHandle();
                }
            }
            else
            {
                result = colorDialog.ShowDialog();
            }

            if (result == Forms.DialogResult.OK)
            {
                var selected = colorDialog.Color;
                _selectedDefaultColorHex = $"#{selected.R:X2}{selected.G:X2}{selected.B:X2}";
                UpdateColorSwatchSelectionVisual();
                if (EnableNoticeColorTrace)
                {
                    Debug.WriteLine($"[NoticeColorTrace][Panel] 系统色盘选择: {_selectedDefaultColorHex}");
                }
                RaiseConfigChanged(forceWhenUnloaded: true);
            }
            else if (EnableNoticeColorTrace)
            {
                Debug.WriteLine("[NoticeColorTrace][Panel] 系统色盘取消");
            }
        }

        private void RaiseConfigChanged(bool forceWhenUnloaded = false)
        {
            if (_isBinding || (!IsLoaded && !forceWhenUnloaded))
            {
                if (EnableNoticeColorTrace)
                {
                    Debug.WriteLine(
                        $"[NoticeColorTrace][Panel] 跳过回调: isBinding={_isBinding}, isLoaded={IsLoaded}, " +
                        $"forceWhenUnloaded={forceWhenUnloaded}, color={_selectedDefaultColorHex}");
                }
                return;
            }

            var current = GetCurrentConfig();
            if (EnableNoticeColorTrace)
            {
                Debug.WriteLine(
                    $"[NoticeColorTrace][Panel] 触发回调: color={current.DefaultColorHex}, speed={current.Speed}, " +
                    $"height={current.BarHeight:F0}, isLoaded={IsLoaded}, forceWhenUnloaded={forceWhenUnloaded}");
            }
            ConfigChanged?.Invoke(current);
        }

        private NoticeComponentConfig GetCurrentConfig()
        {
            var cfg = new NoticeComponentConfig
            {
                PositionFlags = GetSelectedPositionFlags(),
                Direction = GetSelectedDirection(),
                Speed = GetSelectedSpeed(),
                DurationMinutes = GetSelectedDuration(),
                BarHeight = GetSelectedBarHeight(),
                DefaultColorHex = GetSelectedDefaultColor(),
                AutoClose = AutoCloseCheckBox.IsChecked == true
            };

            return NoticeComponentConfigCodec.Normalize(cfg);
        }

        private NoticePositionFlags GetSelectedPositionFlags()
        {
            NoticePositionFlags flags = NoticePositionFlags.None;
            if (PositionTopCheckBox.IsChecked == true)
            {
                flags |= NoticePositionFlags.Top;
            }

            if (PositionCenterCheckBox.IsChecked == true)
            {
                flags |= NoticePositionFlags.Center;
            }

            if (PositionBottomCheckBox.IsChecked == true)
            {
                flags |= NoticePositionFlags.Bottom;
            }

            return NoticeComponentConfig.NormalizePositionFlags(flags);
        }

        private NoticeDirection GetSelectedDirection()
        {
            if (DirectionRightToLeftOption.IsChecked == true)
            {
                return NoticeDirection.RightToLeft;
            }

            if (DirectionPingPongOption.IsChecked == true)
            {
                return NoticeDirection.PingPong;
            }

            return NoticeDirection.LeftToRight;
        }

        private int GetSelectedDuration()
        {
            if (DurationComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                int.TryParse(tag, out int minutes))
            {
                return minutes;
            }

            return 3;
        }

        private int GetSelectedSpeedLevel()
        {
            if (SpeedLevel1Option.IsChecked == true)
            {
                return 1;
            }

            if (SpeedLevel2Option.IsChecked == true)
            {
                return 2;
            }

            if (SpeedLevel4Option.IsChecked == true)
            {
                return 4;
            }

            if (SpeedLevel5Option.IsChecked == true)
            {
                return 5;
            }

            return 3;
        }

        private int GetSelectedSpeed()
        {
            return NoticeComponentConfig.GetSpeedByLevel(GetSelectedSpeedLevel());
        }

        private int GetSelectedBarHeightLevel()
        {
            if (BarHeightLevel1Option.IsChecked == true)
            {
                return 1;
            }

            if (BarHeightLevel2Option.IsChecked == true)
            {
                return 2;
            }

            if (BarHeightLevel4Option.IsChecked == true)
            {
                return 4;
            }

            return 3;
        }

        private double GetSelectedBarHeight()
        {
            return NoticeComponentConfig.GetBarHeightByLevel(GetSelectedBarHeightLevel());
        }

        private void SelectDirection(NoticeDirection direction)
        {
            DirectionLeftToRightOption.IsChecked = direction == NoticeDirection.LeftToRight;
            DirectionRightToLeftOption.IsChecked = direction == NoticeDirection.RightToLeft;
            DirectionPingPongOption.IsChecked = direction == NoticeDirection.PingPong;
        }

        private void SelectPositionFlags(NoticePositionFlags flags)
        {
            var normalized = NoticeComponentConfig.NormalizePositionFlags(flags);
            PositionTopCheckBox.IsChecked = (normalized & NoticePositionFlags.Top) == NoticePositionFlags.Top;
            PositionCenterCheckBox.IsChecked = (normalized & NoticePositionFlags.Center) == NoticePositionFlags.Center;
            PositionBottomCheckBox.IsChecked = (normalized & NoticePositionFlags.Bottom) == NoticePositionFlags.Bottom;
        }

        private void SelectDuration(int duration)
        {
            string targetTag = duration.ToString();
            var item = DurationComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(x => string.Equals(x.Tag as string, targetTag, StringComparison.OrdinalIgnoreCase));
            DurationComboBox.SelectedItem = item ?? DurationComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }

        private void SelectSpeedLevel(int speed)
        {
            int level = NoticeComponentConfig.GetSpeedLevel(speed);
            SpeedLevel1Option.IsChecked = level == 1;
            SpeedLevel2Option.IsChecked = level == 2;
            SpeedLevel3Option.IsChecked = level == 3;
            SpeedLevel4Option.IsChecked = level == 4;
            SpeedLevel5Option.IsChecked = level == 5;
        }

        private void SelectBarHeightLevel(double height)
        {
            int level = NoticeComponentConfig.GetBarHeightLevel(height);
            BarHeightLevel1Option.IsChecked = level == 1;
            BarHeightLevel2Option.IsChecked = level == 2;
            BarHeightLevel3Option.IsChecked = level == 3;
            BarHeightLevel4Option.IsChecked = level == 4;
        }

        private string GetSelectedDefaultColor()
        {
            if (!string.IsNullOrWhiteSpace(_selectedDefaultColorHex))
            {
                return _selectedDefaultColorHex;
            }

            return NoticeComponentConfig.DefaultNoticeColorHex;
        }

        private void SelectDefaultColor(string colorHex)
        {
            _selectedDefaultColorHex = string.IsNullOrWhiteSpace(colorHex)
                ? NoticeComponentConfig.DefaultNoticeColorHex
                : colorHex.Trim().ToUpperInvariant();
            UpdateColorSwatchSelectionVisual();
        }

        private void UpdateColorSwatchSelectionVisual()
        {
            UpdateSwatchButtonBorder(ColorSwatchOrange, _selectedDefaultColorHex);
            UpdateSwatchButtonBorder(ColorSwatchRed, _selectedDefaultColorHex);
            UpdateSwatchButtonBorder(ColorSwatchBlue, _selectedDefaultColorHex);
            UpdateSwatchButtonBorder(ColorSwatchGreen, _selectedDefaultColorHex);
            UpdateSwatchButtonBorder(ColorSwatchBlack, _selectedDefaultColorHex);
            UpdateSwatchButtonBorder(ColorSwatchWhite, _selectedDefaultColorHex);
            UpdateCustomSwatchButtonVisual();
        }

        private static void UpdateSwatchButtonBorder(System.Windows.Controls.Button swatch, string selectedHex)
        {
            if (swatch == null || swatch.Tag is not string tag)
            {
                return;
            }

            bool selected = string.Equals(tag.Trim().ToUpperInvariant(), selectedHex, StringComparison.Ordinal);
            swatch.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            swatch.BorderBrush = selected
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
        }

        private void UpdateCustomSwatchButtonVisual()
        {
            if (ColorSwatchCustom == null)
            {
                return;
            }

            bool isPreset = PresetColorHexes.Any(x => string.Equals(x, _selectedDefaultColorHex, StringComparison.OrdinalIgnoreCase));
            if (TryParseColor(_selectedDefaultColorHex, out var color))
            {
                ColorSwatchCustom.Background = new SolidColorBrush(color);
            }
            else
            {
                ColorSwatchCustom.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246));
            }

            ColorSwatchCustom.BorderThickness = isPreset ? new Thickness(1) : new Thickness(2);
            ColorSwatchCustom.BorderBrush = isPreset
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
        }

        private static bool TryParseColor(string colorHex, out WpfColor color)
        {
            color = Colors.White;
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return false;
            }

            try
            {
                if (System.Windows.Media.ColorConverter.ConvertFromString(colorHex) is WpfColor parsed)
                {
                    color = parsed;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
