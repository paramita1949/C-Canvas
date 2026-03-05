using System;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI.Controls.Common
{
    internal static class SharedColorModule
    {
        private const string GradientPrefix = "GRAD|";

        internal readonly struct GradientSpec
        {
            public string StartColor { get; init; }
            public string EndColor { get; init; }
            public string Direction { get; init; }
        }

        public static readonly string[] SolidQuickColors =
        {
            "#000000", "#FFFFFF", "#5F6368", "#D4D4D4", "#4A86E8", "#202124", "#78909C", "#F4A43A", "#0E9BA8", "#A8B81C"
        };

        public static readonly string[] SolidPaletteColors =
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

        public static readonly (string Start, string End)[] GradientQuickPresets =
        {
            ("#212121", "#5C5C5C"), ("#1A1A1A", "#7E8F9A"), ("#F4A43A", "#F7CA7A"), ("#0E9BA8", "#25C7D3"), ("#8AA322", "#B9CC4D"),
            ("#9E0B0F", "#E44147"), ("#9F5E00", "#E79B34"), ("#2F7B1F", "#72BF54"), ("#165AA9", "#4E8DE0"), ("#3D247D", "#7A5AC1")
        };

        public static readonly (string Start, string End)[] GradientPalettePresets =
        {
            ("#000000", "#444444"), ("#2A2A2A", "#666666"), ("#4A4A4A", "#8C8C8C"), ("#7A7A7A", "#B0B0B0"), ("#A0A0A0", "#CECECE"), ("#B9B9B9", "#E0E0E0"), ("#D0D0D0", "#F0F0F0"),
            ("#D90000", "#FF6A6A"), ("#A85A00", "#F2B46B"), ("#B88E00", "#F1D56A"), ("#2D7A1E", "#82C66D"), ("#1B5FB0", "#78A9E8"), ("#244596", "#6687D8"), ("#5B3DA1", "#9E86D8"),
            ("#8A1E1E", "#E18A8A"), ("#A0671E", "#E8C08F"), ("#A08B22", "#E5D08C"), ("#3F7F36", "#9EC89A"), ("#3A7C8D", "#9CC8D2"), ("#436AA0", "#9FB9E3"), ("#6B4A9D", "#B9A4DE"),
            ("#B00000", "#F20000"), ("#C06F00", "#F39A23"), ("#C9A000", "#F4CB27"), ("#2F8F18", "#5FBE3F"), ("#1A5FAF", "#3F86DA"), ("#124D8D", "#2E76C6"), ("#4D2D9E", "#7C56CE")
        };

        public static string PickSystemColor(string initialHex = null)
        {
            using var dialog = new Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                SolidColorOnly = false
            };

            if (TryParseColor(initialHex, out var initial))
            {
                dialog.Color = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B);
            }

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
            {
                return null;
            }

            var c = dialog.Color;
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        public static string NormalizeColorHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith(GradientPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (string.Equals(trimmed, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                return "Transparent";
            }

            if (!trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                trimmed = "#" + trimmed;
            }

            if (trimmed.Length != 7 && trimmed.Length != 9)
            {
                return null;
            }

            for (var i = 1; i < trimmed.Length; i++)
            {
                if (!Uri.IsHexDigit(trimmed[i]))
                {
                    return null;
                }
            }

            return trimmed.ToUpperInvariant();
        }

        public static string BuildGradientSpec(string startHex, string endHex, string direction = "LeftToRight")
        {
            var start = NormalizeColorHex(startHex);
            var end = NormalizeColorHex(endHex);
            if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end) ||
                string.Equals(start, "Transparent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(end, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var dir = NormalizeGradientDirection(direction);
            return $"{GradientPrefix}{start}|{end}|{dir}";
        }

        public static bool IsGradientSpec(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Trim().StartsWith(GradientPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryParseGradientSpec(string value, out GradientSpec spec)
        {
            spec = default;
            if (!IsGradientSpec(value))
            {
                return false;
            }

            var payload = value.Trim().Substring(GradientPrefix.Length);
            var parts = payload.Split('|');
            if (parts.Length < 2)
            {
                return false;
            }

            var start = NormalizeColorHex(parts[0]);
            var end = NormalizeColorHex(parts[1]);
            if (string.IsNullOrWhiteSpace(start) || string.IsNullOrWhiteSpace(end))
            {
                return false;
            }

            var dir = parts.Length >= 3 ? NormalizeGradientDirection(parts[2]) : "LeftToRight";
            spec = new GradientSpec
            {
                StartColor = start,
                EndColor = end,
                Direction = dir
            };
            return true;
        }

        public static bool TryCreateBrush(string value, out System.Windows.Media.Brush brush, int? opacityPercent = null)
        {
            brush = null;
            if (TryParseGradientSpec(value, out var spec))
            {
                var start = ParseColor(spec.StartColor, Colors.Black);
                var end = ParseColor(spec.EndColor, Colors.Black);
                if (opacityPercent.HasValue)
                {
                    start = ApplyOpacity(start, opacityPercent.Value);
                    end = ApplyOpacity(end, opacityPercent.Value);
                }

                brush = BuildGradientBrush(start, end, spec.Direction);
                return brush != null;
            }

            var normalized = NormalizeColorHex(value);
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var color = ParseColor(normalized, Colors.Black);
            if (opacityPercent.HasValue)
            {
                color = ApplyOpacity(color, opacityPercent.Value);
            }

            brush = new SolidColorBrush(color);
            return true;
        }

        public static string EncodeBrush(System.Windows.Media.Brush brush)
        {
            switch (brush)
            {
                case SolidColorBrush solid:
                    return ToHex(solid.Color, includeAlpha: solid.Color.A < 255);
                case LinearGradientBrush linear when linear.GradientStops?.Count >= 2:
                {
                    var start = linear.GradientStops[0].Color;
                    var end = linear.GradientStops[^1].Color;
                    var dir = DirectionFromPoints(linear.StartPoint, linear.EndPoint);
                    return BuildGradientSpec(ToHex(start, includeAlpha: start.A < 255), ToHex(end, includeAlpha: end.A < 255), dir);
                }
                case RadialGradientBrush radial when radial.GradientStops?.Count >= 2:
                {
                    var start = radial.GradientStops[0].Color;
                    var end = radial.GradientStops[^1].Color;
                    return BuildGradientSpec(ToHex(start, includeAlpha: start.A < 255), ToHex(end, includeAlpha: end.A < 255), "RadialCenter");
                }
                default:
                    return null;
            }
        }

        public static bool TryGetRepresentativeColor(System.Windows.Media.Brush brush, out WpfColor color)
        {
            color = Colors.Black;
            switch (brush)
            {
                case SolidColorBrush solid:
                    color = solid.Color;
                    return true;
                case GradientBrush gradient when gradient.GradientStops?.Count > 0:
                    color = gradient.GradientStops[0].Color;
                    return true;
                default:
                    return false;
            }
        }

        public static WpfColor ParseColor(string colorHex, WpfColor fallback)
        {
            try
            {
                var normalized = NormalizeColorHex(colorHex) ?? "#000000";
                if (string.Equals(normalized, "Transparent", StringComparison.OrdinalIgnoreCase))
                {
                    return WpfColor.FromArgb(0, 0, 0, 0);
                }

                if (System.Windows.Media.ColorConverter.ConvertFromString(normalized) is WpfColor color)
                {
                    return color;
                }
            }
            catch
            {
            }

            return fallback;
        }

        public static bool TryParseColor(string colorHex, out WpfColor color)
        {
            color = Colors.White;
            try
            {
                var normalized = NormalizeColorHex(colorHex);
                if (normalized == null || string.Equals(normalized, "Transparent", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (System.Windows.Media.ColorConverter.ConvertFromString(normalized) is WpfColor parsed)
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

        public static string MixColors(string startHex, string endHex, double ratio)
        {
            ratio = Math.Clamp(ratio, 0, 1);
            var start = ParseColor(startHex, Colors.Black);
            var end = ParseColor(endHex, Colors.Black);
            byte r = (byte)Math.Round((start.R * (1 - ratio)) + (end.R * ratio));
            byte g = (byte)Math.Round((start.G * (1 - ratio)) + (end.G * ratio));
            byte b = (byte)Math.Round((start.B * (1 - ratio)) + (end.B * ratio));
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        public static string ShiftBrightness(string hex, double delta)
        {
            var c = ParseColor(hex, Colors.Black);
            byte Shift(byte v) => (byte)Math.Clamp((int)Math.Round(v + (255 - v) * delta), 0, 255);
            return $"#{Shift(c.R):X2}{Shift(c.G):X2}{Shift(c.B):X2}";
        }

        public static string ToHex(WpfColor color, bool includeAlpha = false)
        {
            if (includeAlpha || color.A < 255)
            {
                return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            }

            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static string ApplyOpacityToHex(string colorHex, int opacityPercent)
        {
            var c = ParseColor(colorHex, Colors.Black);
            byte alpha = (byte)Math.Clamp((int)Math.Round(255 * (100 - Math.Clamp(opacityPercent, 0, 100)) / 100.0), 0, 255);
            return ToHex(WpfColor.FromArgb(alpha, c.R, c.G, c.B), includeAlpha: true);
        }

        public static WpfColor ApplyOpacity(WpfColor color, int opacityPercent)
        {
            byte alpha = (byte)Math.Clamp((int)Math.Round(255 * (100 - Math.Clamp(opacityPercent, 0, 100)) / 100.0), 0, 255);
            return WpfColor.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static string NormalizeGradientDirection(string direction)
        {
            return (direction ?? string.Empty).Trim() switch
            {
                "TopToBottom" => "TopToBottom",
                "BottomToTop" => "BottomToTop",
                "RightToLeft" => "RightToLeft",
                "RadialCenter" => "RadialCenter",
                _ => "LeftToRight"
            };
        }

        private static System.Windows.Media.Brush BuildGradientBrush(WpfColor start, WpfColor end, string direction)
        {
            return NormalizeGradientDirection(direction) switch
            {
                "TopToBottom" => new LinearGradientBrush(start, end, new System.Windows.Point(0, 0), new System.Windows.Point(0, 1)),
                "BottomToTop" => new LinearGradientBrush(start, end, new System.Windows.Point(0, 1), new System.Windows.Point(0, 0)),
                "RightToLeft" => new LinearGradientBrush(start, end, new System.Windows.Point(1, 0), new System.Windows.Point(0, 0)),
                "RadialCenter" => new RadialGradientBrush
                {
                    GradientOrigin = new System.Windows.Point(0.5, 0.5),
                    Center = new System.Windows.Point(0.5, 0.5),
                    RadiusX = 0.68,
                    RadiusY = 0.68,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(start, 0),
                        new GradientStop(end, 1)
                    }
                },
                _ => new LinearGradientBrush(start, end, new System.Windows.Point(0, 0), new System.Windows.Point(1, 0))
            };
        }

        private static string DirectionFromPoints(System.Windows.Point start, System.Windows.Point end)
        {
            if (Math.Abs(start.X - 0) < 0.01 && Math.Abs(start.Y - 0) < 0.01 &&
                Math.Abs(end.X - 0) < 0.01 && Math.Abs(end.Y - 1) < 0.01)
            {
                return "TopToBottom";
            }

            if (Math.Abs(start.X - 0) < 0.01 && Math.Abs(start.Y - 1) < 0.01 &&
                Math.Abs(end.X - 0) < 0.01 && Math.Abs(end.Y - 0) < 0.01)
            {
                return "BottomToTop";
            }

            if (Math.Abs(start.X - 1) < 0.01 && Math.Abs(start.Y - 0) < 0.01 &&
                Math.Abs(end.X - 0) < 0.01 && Math.Abs(end.Y - 0) < 0.01)
            {
                return "RightToLeft";
            }

            return "LeftToRight";
        }
    }
}
