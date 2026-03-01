using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 文本编辑器：字体/样式/对齐/侧边面板
    /// </summary>
    public partial class MainWindow
    {
        private const string SidePanelOffsetXKeyPrefix = "TextEditorSidePanelOffsetX_";
        private const string SidePanelOffsetYKeyPrefix = "TextEditorSidePanelOffsetY_";

        private bool _isSidePanelDragging;
        private System.Windows.Point _sidePanelDragStartPoint;
        private double _sidePanelDragStartHorizontalOffset;
        private double _sidePanelDragStartVerticalOffset;
        private Popup _draggingSidePanelPopup;

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
            RestoreSidePanelOffset(BorderSettingsPopup);
            BorderSettingsPanel.BindTarget(_selectedTextBox);
            BorderSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void BtnFloatingBackground_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            CloseOtherSidePanels("BackgroundSettingsPopup");
            RestoreSidePanelOffset(BackgroundSettingsPopup);
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
            RestoreSidePanelOffset(ShadowSettingsPopup);
            ShadowSettingsPanel.BindTarget(_selectedTextBox);
            ShadowSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void BtnFloatingSpacing_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            CloseOtherSidePanels("SpacingSettingsPopup");
            RestoreSidePanelOffset(SpacingSettingsPopup);
            SpacingSettingsPanel.BindTarget(_selectedTextBox);
            SpacingSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void BtnFloatingAnimation_Click(object sender, RoutedEventArgs e)
        {
            bool popupMode = _selectedTextBox == null && _isBiblePopupOverlayVisible;
            if (_selectedTextBox == null && !popupMode)
            {
                return;
            }

            CloseOtherSidePanels("AnimationSettingsPopup");
            RestoreSidePanelOffset(AnimationSettingsPopup);
            AnimationSettingsPanel.BindTarget(_selectedTextBox);
            AnimationSettingsPanel.AnimationSettingsChanged -= AnimationSettingsPanel_AnimationSettingsChanged;
            LoadAnimationSettingsToPanel();
            AnimationSettingsPanel.AnimationSettingsChanged += AnimationSettingsPanel_AnimationSettingsChanged;
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
            AnimationSettingsPanel.SetBiblePopupAnimationSettings(
                _biblePopupAnimationEnabled,
                _biblePopupAnimationOpacity,
                _biblePopupAnimationDuration,
                _biblePopupAnimationType
            );
        }

        private void AnimationSettingsPanel_AnimationSettingsChanged(object sender, EventArgs e)
        {
            var (enabled, opacity, duration) = AnimationSettingsPanel.GetAnimationSettings();
            _projectionAnimationEnabled = enabled;
            _projectionAnimationOpacity = opacity;
            _projectionAnimationDuration = duration;
            SaveProjectionAnimationSettings();

            var (popupEnabled, popupOpacity, popupDuration, popupType) = AnimationSettingsPanel.GetBiblePopupAnimationSettings();
            _biblePopupAnimationEnabled = popupEnabled;
            _biblePopupAnimationOpacity = Math.Clamp(popupOpacity, 0.0, 1.0);
            _biblePopupAnimationDuration = Math.Clamp(popupDuration, 100, 3000);
            _biblePopupAnimationType = popupType;
            SaveBiblePopupAnimationSettings();
            ApplyBiblePopupAnimationSettingsImmediately();
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

        private void SaveBiblePopupAnimationSettings()
        {
            try
            {
                _configManager.BiblePopupAnimationEnabled = _biblePopupAnimationEnabled;
                _configManager.BiblePopupAnimationOpacity = _biblePopupAnimationOpacity;
                _configManager.BiblePopupAnimationDuration = _biblePopupAnimationDuration;
                _configManager.BiblePopupAnimationType = _biblePopupAnimationType;
                _configManager.SaveConfig();
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
            HideBibleFloatingToolbar();

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
            RestoreSidePanelOffset(TextColorSettingsPopup);
            TextColorSettingsPanel.BindTarget(_selectedTextBox);
            TextColorSettingsPopup.IsOpen = true;
            e.Handled = true;
        }

        private void SidePanelDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement dragHandle || dragHandle.Tag is not string popupName)
                return;

            var popup = GetSidePanelPopupByName(popupName);
            if (popup == null || popup.PlacementTarget is not UIElement placementTarget)
                return;

            _draggingSidePanelPopup = popup;
            _isSidePanelDragging = true;
            _sidePanelDragStartPoint = e.GetPosition(placementTarget);
            _sidePanelDragStartHorizontalOffset = popup.HorizontalOffset;
            _sidePanelDragStartVerticalOffset = popup.VerticalOffset;

            dragHandle.CaptureMouse();
            e.Handled = true;
        }

        private void SidePanelDragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isSidePanelDragging || _draggingSidePanelPopup == null)
                return;

            if (_draggingSidePanelPopup.PlacementTarget is not UIElement placementTarget)
                return;

            var currentPoint = e.GetPosition(placementTarget);
            var delta = currentPoint - _sidePanelDragStartPoint;

            _draggingSidePanelPopup.HorizontalOffset = _sidePanelDragStartHorizontalOffset + delta.X;
            _draggingSidePanelPopup.VerticalOffset = _sidePanelDragStartVerticalOffset + delta.Y;
            e.Handled = true;
        }

        private void SidePanelDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement dragHandle)
            {
                dragHandle.ReleaseMouseCapture();
            }

            if (_isSidePanelDragging && _draggingSidePanelPopup != null)
            {
                SaveSidePanelOffset(_draggingSidePanelPopup);
            }

            _isSidePanelDragging = false;
            _draggingSidePanelPopup = null;
            e.Handled = true;
        }

        private void SidePanelHeaderClose_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement closeButton || closeButton.Tag is not string popupName)
                return;

            var popup = GetSidePanelPopupByName(popupName);
            if (popup != null)
            {
                popup.IsOpen = false;
            }

            e.Handled = true;
        }

        private Popup GetSidePanelPopupByName(string popupName)
        {
            return popupName switch
            {
                "BorderSettingsPopup" => BorderSettingsPopup,
                "BackgroundSettingsPopup" => BackgroundSettingsPopup,
                "TextColorSettingsPopup" => TextColorSettingsPopup,
                "ShadowSettingsPopup" => ShadowSettingsPopup,
                "SpacingSettingsPopup" => SpacingSettingsPopup,
                "AnimationSettingsPopup" => AnimationSettingsPopup,
                _ => null
            };
        }

        private void RestoreSidePanelOffset(Popup popup)
        {
            if (popup == null)
                return;

            var popupName = popup.Name;
            var dbManager = DatabaseManagerService;
            var savedX = dbManager.GetUISetting($"{SidePanelOffsetXKeyPrefix}{popupName}");
            var savedY = dbManager.GetUISetting($"{SidePanelOffsetYKeyPrefix}{popupName}");

            if (double.TryParse(savedX, NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            {
                popup.HorizontalOffset = x;
            }

            if (double.TryParse(savedY, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                popup.VerticalOffset = y;
            }
        }

        private void SaveSidePanelOffset(Popup popup)
        {
            if (popup == null)
                return;

            var popupName = popup.Name;
            var dbManager = DatabaseManagerService;
            dbManager.SaveUISetting($"{SidePanelOffsetXKeyPrefix}{popupName}", popup.HorizontalOffset.ToString(CultureInfo.InvariantCulture));
            dbManager.SaveUISetting($"{SidePanelOffsetYKeyPrefix}{popupName}", popup.VerticalOffset.ToString(CultureInfo.InvariantCulture));
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
