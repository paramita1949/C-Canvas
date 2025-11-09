using System;
using System.Windows;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// WPF 颜色选择器窗口（替代 Windows Forms ColorDialog）
    /// </summary>
    public partial class WpfColorPickerWindow : Window
    {
        public WpfColor SelectedColor { get; private set; }

        public WpfColorPickerWindow()
        {
            InitializeComponent();
            SelectedColor = Colors.White;
        }

        public WpfColorPickerWindow(WpfColor initialColor) : this()
        {
            SelectedColor = initialColor;
            SetColor(initialColor);
        }

        private void SetColor(WpfColor color)
        {
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            UpdatePreview();
        }

        private void ColorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (PreviewBrush == null || RedSlider == null || GreenSlider == null || BlueSlider == null)
                return;

            byte r = (byte)RedSlider.Value;
            byte g = (byte)GreenSlider.Value;
            byte b = (byte)BlueSlider.Value;

            PreviewBrush.Color = WpfColor.FromRgb(r, g, b);
            RgbText.Text = $"RGB: {r}, {g}, {b}";
            SelectedColor = PreviewBrush.Color;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

