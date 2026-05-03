using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace ImageColorChanger.UI
{
    public partial class NdiWatermarkSettingsWindow : Window
    {
        public sealed class Input
        {
            public string Text { get; set; } = string.Empty;
            public string Position { get; set; } = "RightBottom";
            public double FontSize { get; set; } = 48;
            public string FontFamily { get; set; } = "Microsoft YaHei UI";
            public double Opacity { get; set; } = 43;
        }

        public sealed class Output
        {
            public string Text { get; set; } = string.Empty;
            public string Position { get; set; } = "RightBottom";
            public double FontSize { get; set; }
            public string FontFamily { get; set; } = "Microsoft YaHei UI";
            public double Opacity { get; set; }
        }

        private readonly Dictionary<string, System.Windows.Controls.Button> _positionButtons = new();
        private string _selectedPosition = "RightBottom";

        public Output Result { get; private set; }

        public NdiWatermarkSettingsWindow(Input input)
        {
            InitializeComponent();
            BuildPositionButtons();

            WatermarkTextBox.Text = input.Text ?? string.Empty;
            _selectedPosition = string.IsNullOrWhiteSpace(input.Position) ? "RightBottom" : input.Position;
            FontSizeSlider.Value = Math.Clamp(input.FontSize, 10, 220);
            FontSizeTextBox.Text = FontSizeSlider.Value.ToString("0.#");
            OpacitySlider.Value = Math.Clamp(input.Opacity, 0, 100);
            OpacityTextBox.Text = OpacitySlider.Value.ToString("0.#");
            FontFamilyTextBox.Text = string.IsNullOrWhiteSpace(input.FontFamily) ? "Microsoft YaHei UI" : input.FontFamily;

            UpdatePositionButtons();
        }

        private void BuildPositionButtons()
        {
            var items = new[]
            {
                ("LeftTop", "左上"),
                ("RightTop", "右上"),
                ("LeftBottom", "左下"),
                ("RightBottom", "右下"),
                ("Center", "居中")
            };

            foreach ((string key, string label) in items)
            {
                var button = new System.Windows.Controls.Button
                {
                    Content = label,
                    Height = 34,
                    Margin = new Thickness(0, 0, 8, 0),
                    BorderThickness = new Thickness(1),
                    FontSize = 13
                };
                button.Click += (_, _) =>
                {
                    _selectedPosition = key;
                    UpdatePositionButtons();
                };
                _positionButtons[key] = button;
                PositionGrid.Children.Add(button);
            }
        }

        private void UpdatePositionButtons()
        {
            foreach ((string key, System.Windows.Controls.Button button) in _positionButtons)
            {
                bool active = string.Equals(key, _selectedPosition, StringComparison.OrdinalIgnoreCase);
                if (active)
                {
                    button.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
                    button.Foreground = System.Windows.Media.Brushes.White;
                    button.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
                }
                else
                {
                    button.Background = System.Windows.Media.Brushes.White;
                    button.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85));
                    button.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225));
                }
            }
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _ = sender;
            _ = e;
            if (FontSizeTextBox != null)
            {
                FontSizeTextBox.Text = FontSizeSlider.Value.ToString("0.#");
            }
        }

        private void FontSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (!double.TryParse(FontSizeTextBox.Text.Trim(), out double val))
            {
                return;
            }

            val = Math.Clamp(val, 10, 220);
            if (Math.Abs(FontSizeSlider.Value - val) > 0.01)
            {
                FontSizeSlider.Value = val;
            }
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _ = sender;
            _ = e;
            if (OpacityTextBox != null)
            {
                OpacityTextBox.Text = OpacitySlider.Value.ToString("0.#");
            }
        }

        private void OpacityTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (!double.TryParse(OpacityTextBox.Text.Trim(), out double val))
            {
                return;
            }

            val = Math.Clamp(val, 0, 100);
            if (Math.Abs(OpacitySlider.Value - val) > 0.01)
            {
                OpacitySlider.Value = val;
            }
        }

        private void ChooseFontButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            try
            {
                using var fontDialog = new Forms.FontDialog
                {
                    FontMustExist = true,
                    ShowColor = false,
                    MinSize = 10,
                    MaxSize = 220,
                    Font = new System.Drawing.Font(
                        FontFamilyTextBox.Text,
                        float.TryParse(FontSizeTextBox.Text, out float tempSize) ? tempSize : 48f)
                };

                if (fontDialog.ShowDialog() != Forms.DialogResult.OK || fontDialog.Font == null)
                {
                    return;
                }

                FontFamilyTextBox.Text = fontDialog.Font.FontFamily.Name;
                FontSizeTextBox.Text = fontDialog.Font.Size.ToString("0.#");
            }
            catch
            {
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (!double.TryParse(FontSizeTextBox.Text.Trim(), out double parsedSize) || parsedSize < 10 || parsedSize > 220)
            {
                System.Windows.MessageBox.Show(this, "帧水印字号格式无效", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(OpacityTextBox.Text.Trim(), out double parsedOpacity) || parsedOpacity < 0 || parsedOpacity > 100)
            {
                System.Windows.MessageBox.Show(this, "帧水印透明度格式无效", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new Output
            {
                Text = WatermarkTextBox.Text ?? string.Empty,
                Position = string.IsNullOrWhiteSpace(_selectedPosition) ? "RightBottom" : _selectedPosition,
                FontSize = parsedSize,
                FontFamily = string.IsNullOrWhiteSpace(FontFamilyTextBox.Text) ? "Microsoft YaHei UI" : FontFamilyTextBox.Text.Trim(),
                Opacity = parsedOpacity
            };

            DialogResult = true;
            Close();
        }
    }
}
