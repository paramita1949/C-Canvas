using System;
using System.Windows;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        public WpfBrush ImageBackgroundBrush { get; private set; } = WpfBrushes.Black;

        private void InitializeImageBackgroundColor()
        {
            ApplyImageBackgroundColor(_configManager?.ImageBackgroundColor, persist: false);
        }

        private void ApplyImageBackgroundColor(string colorHex, bool persist = true)
        {
            string normalized = Core.ImageBackgroundColorCatalog.Normalize(colorHex);
            if (persist && _configManager != null)
            {
                _configManager.ImageBackgroundColor = normalized;
            }

            var brush = CreateImageBackgroundBrush(normalized);
            ImageBackgroundBrush = brush;
            ImageScrollViewer.Background = brush;
            ImageContainer.Background = brush;
            OnPropertyChanged(nameof(ImageBackgroundBrush));

            _projectionManager?.SetImageBackgroundColor(normalized);
        }

        private void OpenImageBackgroundColorPicker()
        {
            using (var colorDialog = new System.Windows.Forms.ColorDialog())
            {
                System.Windows.Media.Color current = ((SolidColorBrush)ImageBackgroundBrush).Color;
                colorDialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
                colorDialog.AllowFullOpen = true;
                colorDialog.FullOpen = true;

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string selected = $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
                    ApplyImageBackgroundColor(selected);
                    ShowStatus($"已设置图片背景色: {selected}");
                }
            }
        }

        private static SolidColorBrush CreateImageBackgroundBrush(string colorHex)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    Core.ImageBackgroundColorCatalog.Normalize(colorHex));
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return WpfBrushes.Black;
            }
        }
    }
}
