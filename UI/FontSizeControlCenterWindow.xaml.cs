using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Core;
using WpfInput = System.Windows.Input;

namespace ImageColorChanger.UI
{
    public partial class FontSizeControlCenterWindow : Window
    {
        public sealed class State
        {
            public double FolderFontSize { get; set; }
            public double FileFontSize { get; set; }
            public double FolderTagFontSize { get; set; }
            public double TopMenuFontSize { get; set; }
        }

        private readonly Func<State> _loadState;
        private readonly Action<double> _setFolderFontSize;
        private readonly Action<double> _setFileFontSize;
        private readonly Action<double> _setFolderTagFontSize;
        private readonly Action<double> _setTopMenuFontSize;
        private bool _syncingUi;

        public FontSizeControlCenterWindow(
            Func<State> loadState,
            Action<double> setFolderFontSize,
            Action<double> setFileFontSize,
            Action<double> setFolderTagFontSize,
            Action<double> setTopMenuFontSize)
        {
            _loadState = loadState ?? throw new ArgumentNullException(nameof(loadState));
            _setFolderFontSize = setFolderFontSize ?? throw new ArgumentNullException(nameof(setFolderFontSize));
            _setFileFontSize = setFileFontSize ?? throw new ArgumentNullException(nameof(setFileFontSize));
            _setFolderTagFontSize = setFolderTagFontSize ?? throw new ArgumentNullException(nameof(setFolderTagFontSize));
            _setTopMenuFontSize = setTopMenuFontSize ?? throw new ArgumentNullException(nameof(setTopMenuFontSize));

            InitializeComponent();
            Loaded += (_, _) => RefreshUi();
        }

        private void RefreshUi()
        {
            State state = _loadState();
            _syncingUi = true;
            SetControlValue(FolderFontSizeSlider, FolderFontSizeTextBox, state.FolderFontSize);
            SetControlValue(FileFontSizeSlider, FileFontSizeTextBox, state.FileFontSize);
            SetControlValue(FolderTagFontSizeSlider, FolderTagFontSizeTextBox, state.FolderTagFontSize);
            SetControlValue(TopMenuFontSizeSlider, TopMenuFontSizeTextBox, state.TopMenuFontSize);
            _syncingUi = false;
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _ = e;
            if (_syncingUi || !IsLoaded || sender is not Slider slider)
            {
                return;
            }

            ApplyValue(slider, slider.Value);
        }

        private void FontSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _ = sender;
            _ = e;
        }

        private void FontSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _ = e;
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                ApplyTextBoxValue(textBox);
            }
        }

        private void FontSizeTextBox_KeyDown(object sender, WpfInput.KeyEventArgs e)
        {
            if (e.Key != WpfInput.Key.Enter || sender is not System.Windows.Controls.TextBox textBox)
            {
                return;
            }

            ApplyTextBoxValue(textBox);
            e.Handled = true;
        }

        private void FontSizeStepButton_Click(object sender, RoutedEventArgs e)
        {
            _ = e;
            if (sender is not System.Windows.Controls.Button button)
            {
                return;
            }

            if (button == FolderFontSizeMinusButton)
            {
                ApplyValue(FolderFontSizeSlider, FolderFontSizeSlider.Value - FontSizeControlValue.Step);
            }
            else if (button == FolderFontSizePlusButton)
            {
                ApplyValue(FolderFontSizeSlider, FolderFontSizeSlider.Value + FontSizeControlValue.Step);
            }
            else if (button == FileFontSizeMinusButton)
            {
                ApplyValue(FileFontSizeSlider, FileFontSizeSlider.Value - FontSizeControlValue.Step);
            }
            else if (button == FileFontSizePlusButton)
            {
                ApplyValue(FileFontSizeSlider, FileFontSizeSlider.Value + FontSizeControlValue.Step);
            }
            else if (button == FolderTagFontSizeMinusButton)
            {
                ApplyValue(FolderTagFontSizeSlider, FolderTagFontSizeSlider.Value - FontSizeControlValue.Step);
            }
            else if (button == FolderTagFontSizePlusButton)
            {
                ApplyValue(FolderTagFontSizeSlider, FolderTagFontSizeSlider.Value + FontSizeControlValue.Step);
            }
            else if (button == TopMenuFontSizeMinusButton)
            {
                ApplyValue(TopMenuFontSizeSlider, TopMenuFontSizeSlider.Value - FontSizeControlValue.Step);
            }
            else if (button == TopMenuFontSizePlusButton)
            {
                ApplyValue(TopMenuFontSizeSlider, TopMenuFontSizeSlider.Value + FontSizeControlValue.Step);
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            ApplyValue(FolderFontSizeSlider, 26);
            ApplyValue(FileFontSizeSlider, 26);
            ApplyValue(FolderTagFontSizeSlider, 18);
            ApplyValue(TopMenuFontSizeSlider, 22);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            Close();
        }

        private void ApplyTextBoxValue(System.Windows.Controls.TextBox textBox)
        {
            if (!double.TryParse(textBox.Text.Trim(), out double value))
            {
                RefreshUi();
                return;
            }

            if (textBox == FolderFontSizeTextBox)
            {
                ApplyValue(FolderFontSizeSlider, value);
            }
            else if (textBox == FileFontSizeTextBox)
            {
                ApplyValue(FileFontSizeSlider, value);
            }
            else if (textBox == FolderTagFontSizeTextBox)
            {
                ApplyValue(FolderTagFontSizeSlider, value);
            }
            else if (textBox == TopMenuFontSizeTextBox)
            {
                ApplyValue(TopMenuFontSizeSlider, value);
            }
        }

        private void ApplyValue(Slider slider, double value)
        {
            double snapped = FontSizeControlValue.SnapToStep(value, slider.Minimum, slider.Maximum);
            System.Windows.Controls.TextBox textBox = GetTextBox(slider);
            Action<double> setter = GetSetter(slider);

            _syncingUi = true;
            SetControlValue(slider, textBox, snapped);
            _syncingUi = false;

            setter(snapped);
        }

        private static void SetControlValue(Slider slider, System.Windows.Controls.TextBox textBox, double value)
        {
            double snapped = FontSizeControlValue.SnapToStep(value, slider.Minimum, slider.Maximum);
            slider.Value = snapped;
            textBox.Text = snapped.ToString("0.#");
        }

        private System.Windows.Controls.TextBox GetTextBox(Slider slider)
        {
            if (slider == FolderFontSizeSlider)
            {
                return FolderFontSizeTextBox;
            }

            if (slider == FileFontSizeSlider)
            {
                return FileFontSizeTextBox;
            }

            if (slider == FolderTagFontSizeSlider)
            {
                return FolderTagFontSizeTextBox;
            }

            return TopMenuFontSizeTextBox;
        }

        private Action<double> GetSetter(Slider slider)
        {
            if (slider == FolderFontSizeSlider)
            {
                return _setFolderFontSize;
            }

            if (slider == FileFontSizeSlider)
            {
                return _setFileFontSize;
            }

            if (slider == FolderTagFontSizeSlider)
            {
                return _setFolderTagFontSize;
            }

            return _setTopMenuFontSize;
        }
    }
}
