using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private string _currentGlobalIconColorHex = DEFAULT_GLOBAL_ICON_COLOR_HEX;

        private static readonly IReadOnlyList<(string Name, string Hex)> IconColorPresets = new (string Name, string Hex)[]
        {
            ("默认豆沙", "#917878"),
            ("石墨灰", "#4B5563"),
            ("深海蓝", "#1E3A8A"),
            ("松针绿", "#166534"),
            ("暖棕", "#92400E"),
            ("酒红", "#7F1D1D")
        };

        private void InitializeThemeSettings()
        {
            string savedHex = _uiSettingsStore?.GetValue(UI_SETTING_GLOBAL_ICON_COLOR);
            ApplyGlobalIconColor(string.IsNullOrWhiteSpace(savedHex) ? DEFAULT_GLOBAL_ICON_COLOR_HEX : savedHex, saveSetting: false, showStatus: false);
        }

        private MenuItem BuildThemeMenuItem()
        {
            var themeMenuItem = new MenuItem { Header = "主题" };
            string current = NormalizeHex(_currentGlobalIconColorHex);

            foreach (var preset in IconColorPresets)
            {
                string hex = NormalizeHex(preset.Hex);
                var item = new MenuItem
                {
                    Header = $"{preset.Name} ({hex})",
                    IsCheckable = true,
                    IsChecked = string.Equals(current, hex, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += (_, __) => ApplyGlobalIconColor(hex, saveSetting: true, showStatus: true);
                themeMenuItem.Items.Add(item);
            }

            themeMenuItem.Items.Add(new Separator());

            var customItem = new MenuItem { Header = "自定义配色..." };
            customItem.Click += (_, __) => ShowCustomIconColorPicker();
            themeMenuItem.Items.Add(customItem);

            var resetItem = new MenuItem { Header = "恢复默认" };
            resetItem.Click += (_, __) => ApplyGlobalIconColor(DEFAULT_GLOBAL_ICON_COLOR_HEX, saveSetting: true, showStatus: true);
            themeMenuItem.Items.Add(resetItem);

            return themeMenuItem;
        }

        private void ShowCustomIconColorPicker()
        {
            try
            {
                if (!TryParseColorHex(_currentGlobalIconColorHex, out var currentColor))
                {
                    currentColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(DEFAULT_GLOBAL_ICON_COLOR_HEX);
                }

                using var dialog = new Forms.ColorDialog
                {
                    AllowFullOpen = true,
                    FullOpen = true,
                    Color = System.Drawing.Color.FromArgb(currentColor.R, currentColor.G, currentColor.B)
                };

                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                {
                    string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                    ApplyGlobalIconColor(hex, saveSetting: true, showStatus: true);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"自定义图标配色失败: {ex.Message}");
            }
        }

        private void ApplyGlobalIconColor(string hex, bool saveSetting, bool showStatus)
        {
            if (!TryParseColorHex(hex, out var color))
            {
                if (showStatus)
                {
                    ShowStatus($"无效颜色: {hex}");
                }
                return;
            }

            string normalizedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            _currentGlobalIconColorHex = normalizedHex;

            var appResources = System.Windows.Application.Current?.Resources;
            if (appResources != null)
            {
                appResources["ColorGlobalIcon"] = color;
                appResources["BrushGlobalIcon"] = new SolidColorBrush(color);
                appResources["BrushIconDefault"] = new SolidColorBrush(color);
                appResources["BrushMenuHover"] = new SolidColorBrush(color) { Opacity = 0.16 };
                appResources["ColorScrollBarThumb"] = WpfColor.FromArgb(0xCC, color.R, color.G, color.B);
                appResources["ColorScrollBarThumbHover"] = WpfColor.FromArgb(
                    0xE6,
                    (byte)Math.Max(0, Math.Min(255, color.R * 0.82)),
                    (byte)Math.Max(0, Math.Min(255, color.G * 0.82)),
                    (byte)Math.Max(0, Math.Min(255, color.B * 0.82)));
                ApplyScrollBarTrackThemeResources(appResources);
            }

            if (saveSetting)
            {
                _uiSettingsStore?.SaveValue(UI_SETTING_GLOBAL_ICON_COLOR, normalizedHex);
            }

            if (showStatus)
            {
                ShowStatus($"图标配色已切换: {normalizedHex}");
            }
        }

        private void ApplyScrollBarTrackThemeResources(System.Windows.ResourceDictionary appResources)
        {
            bool useDarkTrack = IsCurrentWindowBackgroundDark();

            if (useDarkTrack)
            {
                appResources["ColorScrollBarTrackLight"] = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString("#4A4A4A");
                appResources["ColorScrollBarTrackDark"] = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString("#2C2C2C");
            }
            else
            {
                appResources["ColorScrollBarTrackLight"] = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString("#E0E0E0");
                appResources["ColorScrollBarTrackDark"] = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString("#C8C8C8");
            }
        }

        private bool IsCurrentWindowBackgroundDark()
        {
            if (Background is SolidColorBrush brush)
            {
                var c = brush.Color;
                double luminance = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
                return luminance < 128.0;
            }

            return false;
        }

        private static bool TryParseColorHex(string hex, out WpfColor color)
        {
            color = default;
            string normalized = NormalizeHex(hex);
            if (normalized.Length != 7 || !normalized.StartsWith("#", StringComparison.Ordinal))
            {
                return false;
            }

            if (!byte.TryParse(normalized.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
                !byte.TryParse(normalized.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
                !byte.TryParse(normalized.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                return false;
            }

            color = WpfColor.FromRgb(r, g, b);
            return true;
        }

        private static string NormalizeHex(string hex)
        {
            string value = (hex ?? string.Empty).Trim().ToUpperInvariant();
            if (!value.StartsWith("#", StringComparison.Ordinal))
            {
                value = $"#{value}";
            }
            return value;
        }
    }
}
