using System.Collections.Generic;
using System.Windows;
using WpfButton = System.Windows.Controls.Button;

namespace ImageColorChanger.UI.Modules
{
    internal readonly struct TopMenuButtonMetrics
    {
        public TopMenuButtonMetrics(double fontSize, double height, Thickness padding, Thickness margin)
        {
            FontSize = fontSize;
            Height = height;
            Padding = padding;
            Margin = margin;
        }

        public double FontSize { get; }

        public double Height { get; }

        public Thickness Padding { get; }

        public Thickness Margin { get; }
    }

    internal static class TopMenuButtonNormalizer
    {
        internal const double StandardFontSize = 21;
        internal const double StandardHeight = 32;
        internal const double StandardMinWidth = 60;
        internal static readonly Thickness StandardPadding = new Thickness(8, 4, 8, 4);
        internal static readonly Thickness StandardMargin = new Thickness(3, 0, 3, 0);

        internal static TopMenuButtonMetrics StandardMetrics => new TopMenuButtonMetrics(
            StandardFontSize,
            StandardHeight,
            StandardPadding,
            StandardMargin);

        internal static void NormalizeButtons(IEnumerable<WpfButton> buttons, double fontSize)
        {
            NormalizeButtons(
                buttons,
                new TopMenuButtonMetrics(fontSize, StandardHeight, StandardPadding, StandardMargin));
        }

        internal static void NormalizeButtons(IEnumerable<WpfButton> buttons, TopMenuButtonMetrics metrics)
        {
            if (buttons == null)
            {
                return;
            }

            foreach (WpfButton button in buttons)
            {
                NormalizeButton(button, metrics);
            }
        }

        private static void NormalizeButton(WpfButton button, TopMenuButtonMetrics metrics)
        {
            if (button == null)
            {
                return;
            }

            button.ClearValue(FrameworkElement.WidthProperty);
            button.MinWidth = StandardMinWidth;
            button.Height = metrics.Height;
            button.Padding = metrics.Padding;
            button.Margin = metrics.Margin;
            button.FontSize = metrics.FontSize;
            button.VerticalAlignment = VerticalAlignment.Center;
        }
    }
}
