using System;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace ImageColorChanger.UI
{
    /// <summary>移动/重命名窗口共用整体主题色；不改写其他控制面板的资源。</summary>
    public static class MediaFileDialogTheme
    {
        public static void EnsureResources()
        {
            var resources = System.Windows.Application.Current?.Resources;
            if (resources == null || resources.Contains("BrushMediaDialogBackground")) return;
            if (resources["ColorGlobalIcon"] is Color color) Apply(resources, color);
        }

        public static void Apply(ResourceDictionary resources, Color themeColor)
        {
            if (resources == null) return;
            Color accent = Color.FromRgb(themeColor.R, themeColor.G, themeColor.B);
            Color background = Blend(accent, Colors.White, 0.95);
            Color selected = Blend(accent, Colors.White, 0.88);
            Color emphasis = accent;
            // 自定义极浅主题仍需清晰显示选中标记和细边框。
            while (Contrast(emphasis, selected) < 4.5) emphasis = Blend(emphasis, Colors.Black, 0.15);
            Set(resources, "Background", background);
            Set(resources, "Surface", Blend(accent, Colors.White, 0.985));
            Set(resources, "Input", Blend(accent, Colors.White, 0.995));
            Set(resources, "Selected", selected);
            Set(resources, "Border", Blend(accent, Colors.White, 0.73));
            Set(resources, "Text", Blend(accent, Colors.Black, 0.80));
            Set(resources, "Muted", Blend(accent, Colors.Black, 0.56));
            Set(resources, "Accent", accent);
            Set(resources, "Emphasis", emphasis);
            Set(resources, "Outline", emphasis);
            Set(resources, "OnAccent", Contrast(Colors.White, accent) >= Contrast(Colors.Black, accent) ? Colors.White : Colors.Black);
        }

        private static void Set(ResourceDictionary resources, string role, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources["BrushMediaDialog" + role] = brush;
        }
        private static Color Blend(Color a, Color b, double factor) => Color.FromRgb(
            (byte)Math.Round(a.R * (1 - factor) + b.R * factor),
            (byte)Math.Round(a.G * (1 - factor) + b.G * factor),
            (byte)Math.Round(a.B * (1 - factor) + b.B * factor));
        private static double Luminance(Color color)
        {
            static double Linear(byte channel)
            {
                double value = channel / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        private static double Contrast(Color a, Color b)
        {
            double x = Luminance(a), y = Luminance(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }
    }
}
