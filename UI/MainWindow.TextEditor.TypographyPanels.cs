using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 文本编辑器：字体/样式/对齐/侧边面板
    /// </summary>
    public partial class MainWindow
    {
        private void FontFamilySelector_GotFocus(object sender, RoutedEventArgs e)
        {
            var comboBox = sender as System.Windows.Controls.ComboBox;
            if (comboBox != null && !comboBox.IsDropDownOpen)
            {
                comboBox.IsDropDownOpen = true;
            }
        }

        private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedTextBox == null || FontFamilySelector.SelectedItem == null)
                return;

            var selectedItem = FontFamilySelector.SelectedItem as ComboBoxItem;
            if (selectedItem != null && selectedItem.Tag is Core.FontItemData fontData)
            {
                if (_selectedTextBox.HasTextSelection())
                {
                    _selectedTextBox.ApplyStyleToSelection(fontFamilyObj: fontData.FontFamily);
                    MarkContentAsModified();
                }
            }
        }

        private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            string sizeText = null;

            if (FontSizeSelector.SelectedItem is ComboBoxItem item)
            {
                sizeText = item.Content?.ToString();
            }
            else
            {
                sizeText = FontSizeSelector.Text;
            }

            if (!string.IsNullOrEmpty(sizeText) && int.TryParse(sizeText, out int fontSize))
            {
                fontSize = Math.Max(10, Math.Min(200, fontSize));

                if (fontSize.ToString() != sizeText)
                {
                    FontSizeSelector.Text = fontSize.ToString();
                    return;
                }

                if (_selectedTextBox.HasTextSelection())
                {
                    _selectedTextBox.ApplyStyleToSelection(fontSize: fontSize);
                    MarkContentAsModified();
                }
            }
        }

        private void FontSizeInput_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        private void FontSizeInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
        }

        private void FontSizeSelector_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            if (_selectedTextBox.HasTextSelection())
            {
                var fontSizeValue = _selectedTextBox.RichTextBox.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontSizeProperty);
                int currentSize = fontSizeValue != DependencyProperty.UnsetValue
                    ? (int)Math.Round((double)fontSizeValue)
                    : (int)Math.Round(_selectedTextBox.Data.FontSize);

                int delta;
                if (currentSize >= 30)
                {
                    delta = e.Delta > 0 ? 1 : -1;
                }
                else
                {
                    delta = e.Delta > 0 ? 2 : -2;
                }
                int newSize = Math.Max(10, Math.Min(240, currentSize + delta));

                _selectedTextBox.ApplyStyleToSelection(fontSize: newSize);
                MarkContentAsModified();
                FontSizeSelector.Text = newSize.ToString();
            }

            e.Handled = true;
        }

        private void FontSizeInput_MouseWheel(object sender, MouseWheelEventArgs e)
        {
        }

        private void BtnDecreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;

            if (_selectedTextBox.HasTextSelection())
            {
                var fontSizeValue = _selectedTextBox.RichTextBox.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontSizeProperty);
                int currentSize = fontSizeValue != DependencyProperty.UnsetValue
                    ? (int)Math.Round((double)fontSizeValue)
                    : (int)Math.Round(_selectedTextBox.Data.FontSize);

                int delta = currentSize > 30 ? -1 : -2;
                int newSize = Math.Max(10, currentSize + delta);

                _selectedTextBox.ApplyStyleToSelection(fontSize: newSize);
                MarkContentAsModified();
                FontSizeSelector.Text = newSize.ToString();
            }
        }

        private void BtnIncreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;

            if (_selectedTextBox.HasTextSelection())
            {
                var fontSizeValue = _selectedTextBox.RichTextBox.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontSizeProperty);
                int currentSize = fontSizeValue != DependencyProperty.UnsetValue
                    ? (int)Math.Round((double)fontSizeValue)
                    : (int)Math.Round(_selectedTextBox.Data.FontSize);

                int delta = currentSize >= 30 ? 1 : 2;
                int newSize = Math.Min(240, currentSize + delta);

                _selectedTextBox.ApplyStyleToSelection(fontSize: newSize);
                MarkContentAsModified();
                FontSizeSelector.Text = newSize.ToString();
            }
        }

        private void BtnBold_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            if (_selectedTextBox.HasTextSelection())
            {
                bool currentIsBold = _selectedTextBox.IsSelectionBold();
                bool newIsBold = !currentIsBold;

                _selectedTextBox.ApplyStyleToSelection(isBold: newIsBold);
                UpdateBoldButtonState(newIsBold);
                MarkContentAsModified();
            }
        }

        private void BtnUnderline_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            if (_selectedTextBox.HasTextSelection())
            {
                bool currentIsUnderline = _selectedTextBox.IsSelectionUnderline();
                bool newIsUnderline = !currentIsUnderline;

                _selectedTextBox.ApplyStyleToSelection(isUnderline: newIsUnderline);
                UpdateUnderlineButtonState(newIsUnderline);
                MarkContentAsModified();
            }
        }

        private void BtnFloatingBorder_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            CloseOtherSidePanels("BorderSettingsPopup");
            BorderSettingsPanel.BindTarget(_selectedTextBox);
            BorderSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void BtnFloatingBackground_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            CloseOtherSidePanels("BackgroundSettingsPopup");
            BackgroundSettingsPanel.BindTarget(_selectedTextBox);
            BackgroundSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void BtnFloatingShadow_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
            {
                return;
            }

            CloseOtherSidePanels("ShadowSettingsPopup");
            ShadowSettingsPanel.BindTarget(_selectedTextBox);
            ShadowSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void BtnFloatingSpacing_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            CloseOtherSidePanels("SpacingSettingsPopup");
            SpacingSettingsPanel.BindTarget(_selectedTextBox);
            SpacingSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void BtnFloatingAnimation_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            CloseOtherSidePanels("AnimationSettingsPopup");
            AnimationSettingsPanel.BindTarget(_selectedTextBox);
            AnimationSettingsPanel.AnimationSettingsChanged -= AnimationSettingsPanel_AnimationSettingsChanged;
            AnimationSettingsPanel.AnimationSettingsChanged += AnimationSettingsPanel_AnimationSettingsChanged;
            LoadAnimationSettingsToPanel();
            AnimationSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void LoadAnimationSettingsToPanel()
        {
            AnimationSettingsPanel.SetAnimationSettings(
                _projectionAnimationEnabled,
                _projectionAnimationOpacity,
                _projectionAnimationDuration
            );
        }

        private void AnimationSettingsPanel_AnimationSettingsChanged(object sender, EventArgs e)
        {
            var (enabled, opacity, duration) = AnimationSettingsPanel.GetAnimationSettings();
            _projectionAnimationEnabled = enabled;
            _projectionAnimationOpacity = opacity;
            _projectionAnimationDuration = duration;
            SaveProjectionAnimationSettings();
        }

        private void SaveProjectionAnimationSettings()
        {
            try
            {
                _configManager.ProjectionAnimationEnabled = _projectionAnimationEnabled;
                _configManager.ProjectionAnimationOpacity = _projectionAnimationOpacity;
                _configManager.ProjectionAnimationDuration = _projectionAnimationDuration;
            }
            catch (Exception)
            {
            }
        }

        private void BtnFloatingItalic_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            if (_selectedTextBox.HasTextSelection())
            {
                bool currentIsItalic = _selectedTextBox.IsSelectionItalic();
                bool newIsItalic = !currentIsItalic;

                _selectedTextBox.ApplyStyleToSelection(isItalic: newIsItalic);
                UpdateItalicButtonState(newIsItalic);
                MarkContentAsModified();
            }
        }

        private void CloseAllSidePanels()
        {
            BorderSettingsPopup.IsOpen = false;
            BackgroundSettingsPopup.IsOpen = false;
            TextColorSettingsPopup.IsOpen = false;
            ShadowSettingsPopup.IsOpen = false;
            SpacingSettingsPopup.IsOpen = false;
            AnimationSettingsPopup.IsOpen = false;
        }

        private void CloseOtherSidePanels(string keepPopupName)
        {
            if (keepPopupName != "BorderSettingsPopup")
            {
                BorderSettingsPopup.IsOpen = false;
            }
            if (keepPopupName != "BackgroundSettingsPopup")
            {
                BackgroundSettingsPopup.IsOpen = false;
            }
            if (keepPopupName != "TextColorSettingsPopup")
            {
                TextColorSettingsPopup.IsOpen = false;
            }
            if (keepPopupName != "ShadowSettingsPopup")
            {
                ShadowSettingsPopup.IsOpen = false;
            }
            if (keepPopupName != "SpacingSettingsPopup")
            {
                SpacingSettingsPopup.IsOpen = false;
            }
            if (keepPopupName != "AnimationSettingsPopup")
            {
                AnimationSettingsPopup.IsOpen = false;
            }
        }

        public void DeselectAllTextBoxes()
        {
            DeselectAllTextBoxes(false);
        }

        public void DeselectAllTextBoxes(bool closePanels)
        {
            foreach (var textBox in _textBoxes)
            {
                if (textBox.IsInEditMode)
                {
                    textBox.ExitEditMode();
                }
                textBox.SetSelected(false);
            }
            _selectedTextBox = null;

            if (closePanels)
            {
                CloseAllSidePanels();
            }

            Keyboard.ClearFocus();
            EditorCanvas.Focus();
        }

        public bool HasSelectedTextBox()
        {
            return _selectedTextBox != null;
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
            }

            base.OnKeyDown(e);
        }

        private void SidePanel_Opened(object sender, EventArgs e)
        {
        }

        private void SidePanel_Closed(object sender, EventArgs e)
        {
        }

        private void BtnTextColor_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            CloseOtherSidePanels("TextColorSettingsPopup");
            TextColorSettingsPanel.BindTarget(_selectedTextBox);
            TextColorSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void BtnAlignLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;
            _selectedTextBox.ApplyStyle(textAlign: "Left");
            MarkContentAsModified();
        }

        private void BtnAlignCenter_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;
            _selectedTextBox.ApplyStyle(textAlign: "Center");
            MarkContentAsModified();
        }

        private void BtnAlignRight_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;
            _selectedTextBox.ApplyStyle(textAlign: "Right");
            MarkContentAsModified();
        }
    }
}
