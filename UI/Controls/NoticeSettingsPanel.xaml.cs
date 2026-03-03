using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageColorChanger.Services.TextEditor.Components.Notice;

namespace ImageColorChanger.UI.Controls
{
    public partial class NoticeSettingsPanel : System.Windows.Controls.UserControl
    {
        public event Action<NoticeComponentConfig> ConfigChanged;

        private bool _isBinding;
        private string _selectedDefaultColorHex = NoticeComponentConfig.DefaultNoticeColorHex;
        private bool _debugEnabled;

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
                SpeedSlider.Value = normalized.Speed;
                SpeedValueText.Text = normalized.Speed.ToString();
                SelectDuration(normalized.DurationMinutes);
                SelectBarHeightLevel(normalized.BarHeight);
                AutoCloseCheckBox.IsChecked = normalized.AutoClose;
                DurationComboBox.IsEnabled = normalized.AutoClose;
                SelectDefaultColor(normalized.DefaultColorHex);
                _debugEnabled = normalized.DebugEnabled;
            }
            finally
            {
                _isBinding = false;
            }
        }

        private void DirectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _ = sender;
            if (SpeedValueText != null)
            {
                SpeedValueText.Text = ((int)Math.Round(e.NewValue)).ToString();
            }

            RaiseConfigChanged();
        }

        private void DurationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            RaiseConfigChanged();
        }

        private void BarHeightLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
            }
            _ = e;
            RaiseConfigChanged();
        }

        private void RaiseConfigChanged()
        {
            if (_isBinding || !IsLoaded)
            {
                return;
            }

            ConfigChanged?.Invoke(GetCurrentConfig());
        }

        private NoticeComponentConfig GetCurrentConfig()
        {
            var cfg = new NoticeComponentConfig
            {
                PositionFlags = GetSelectedPositionFlags(),
                Direction = GetSelectedDirection(),
                Speed = (int)Math.Round(SpeedSlider.Value),
                DurationMinutes = GetSelectedDuration(),
                BarHeight = GetSelectedBarHeight(),
                DefaultColorHex = GetSelectedDefaultColor(),
                AutoClose = AutoCloseCheckBox.IsChecked == true,
                DebugEnabled = _debugEnabled
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
            if (DirectionComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                if (string.Equals(tag, "LeftToRight", StringComparison.OrdinalIgnoreCase))
                {
                    return NoticeDirection.LeftToRight;
                }

                if (string.Equals(tag, "RightToLeft", StringComparison.OrdinalIgnoreCase))
                {
                    return NoticeDirection.RightToLeft;
                }

                if (string.Equals(tag, "PingPong", StringComparison.OrdinalIgnoreCase))
                {
                    return NoticeDirection.PingPong;
                }
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

        private int GetSelectedBarHeightLevel()
        {
            if (BarHeightLevelComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                int.TryParse(tag, out int level))
            {
                return Math.Clamp(level, NoticeComponentConfig.MinBarHeightLevel, NoticeComponentConfig.MaxBarHeightLevel);
            }

            return 1;
        }

        private double GetSelectedBarHeight()
        {
            return NoticeComponentConfig.GetBarHeightByLevel(GetSelectedBarHeightLevel());
        }

        private void SelectDirection(NoticeDirection direction)
        {
            string targetTag = direction switch
            {
                NoticeDirection.LeftToRight => "LeftToRight",
                NoticeDirection.PingPong => "PingPong",
                _ => "RightToLeft"
            };
            var item = DirectionComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(x => string.Equals(x.Tag as string, targetTag, StringComparison.OrdinalIgnoreCase));
            DirectionComboBox.SelectedItem = item ?? DirectionComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
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

        private void SelectBarHeightLevel(double height)
        {
            string targetTag = NoticeComponentConfig.GetBarHeightLevel(height).ToString();
            var item = BarHeightLevelComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(x => string.Equals(x.Tag as string, targetTag, StringComparison.OrdinalIgnoreCase));
            BarHeightLevelComboBox.SelectedItem = item ?? BarHeightLevelComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
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
    }
}
