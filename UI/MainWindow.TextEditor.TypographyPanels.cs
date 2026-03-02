using System;
using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 文本编辑器：字体/样式/对齐/侧边面板
    /// </summary>
    public partial class MainWindow
    {
        private const bool EnableTextDriftTrace = false;
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
            // 第二层字体下拉不做强制展开，避免选择后再次被自动拉开。
            // 仅保留给旧顶部字体框（可见时）使用。
            if (comboBox != null &&
                comboBox.Name == "FontFamilySelector" &&
                comboBox.Visibility == Visibility.Visible &&
                !comboBox.IsDropDownOpen)
            {
                comboBox.IsDropDownOpen = true;
            }
        }

        private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            var sourceCombo = sender as System.Windows.Controls.ComboBox ?? FontFamilySelector;
            if (sourceCombo?.SelectedItem == null)
                return;

            var selectedItem = sourceCombo.SelectedItem as ComboBoxItem;
            if (selectedItem != null && selectedItem.Tag is Core.FontItemData fontData)
            {
                // 同步文本框默认字体，避免取消选中后回退为旧值（如微软雅黑）
                _selectedTextBox.Data.FontFamily = fontData.FontFamily.Source;

                if (_selectedTextBox.HasTextSelection())
                {
                    _selectedTextBox.ApplyStyleToSelection(fontFamilyObj: fontData.FontFamily);
                    MarkContentAsModified();
                }
            }

            // 第二层下拉选择后立即收起，不需要点击空白区
            if (sourceCombo.Name == "SecondLayerFontFamilySelector" && sourceCombo.IsDropDownOpen)
            {
                sourceCombo.IsDropDownOpen = false;
            }
        }

        private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            var sourceCombo = sender as System.Windows.Controls.ComboBox ?? FontSizeSelector;
            string sizeText = null;

            if (sourceCombo.SelectedItem is ComboBoxItem item)
            {
                sizeText = item.Content?.ToString();
            }
            else
            {
                sizeText = sourceCombo.Text;
            }

            if (!string.IsNullOrEmpty(sizeText) && int.TryParse(sizeText, out int fontSize))
            {
                fontSize = Math.Max(10, Math.Min(200, fontSize));

                if (fontSize.ToString() != sizeText)
                {
                    sourceCombo.Text = fontSize.ToString();
                    FontSizeSelector.Text = fontSize.ToString();
                    return;
                }

                if (_selectedTextBox.HasTextSelection())
                {
                    _selectedTextBox.ApplyStyleToSelection(fontSize: fontSize);
                    _selectedTextBox.Data.FontSize = fontSize;
                    MarkContentAsModified();
                }
            }

            if (sourceCombo.Name == "SecondLayerFontSizeSelector" && sourceCombo.IsDropDownOpen)
            {
                sourceCombo.IsDropDownOpen = false;
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
                if (sender is System.Windows.Controls.ComboBox sourceCombo)
                {
                    sourceCombo.Text = newSize.ToString();
                }
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

            OpenBorderSettingsPopup(sender as UIElement);
            e.Handled = true;
        }

        private void BtnSecondLayerBorder_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            OpenBorderSettingsPopup(sender as UIElement);
            e.Handled = true;
        }

        private void OpenBorderSettingsPopup(UIElement placementTarget)
        {
            CloseOtherSidePanels("BorderSettingsPopup");
            BorderSettingsPanel.BindTarget(_selectedTextBox);

            if (placementTarget != null)
            {
                BorderSettingsPopup.PlacementTarget = placementTarget;
                BorderSettingsPopup.Placement = PlacementMode.Bottom;
                BorderSettingsPopup.HorizontalOffset = 0;
                BorderSettingsPopup.VerticalOffset = 6;
            }
            else
            {
                RestoreSidePanelOffset(BorderSettingsPopup);
            }

            BorderSettingsPopup.IsOpen = true;
        }

        private void BtnFloatingBackground_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            TraceSelectedTextLayout("BeforeOpen-Fill");
            OpenBackgroundSettingsPopup(sender as UIElement);
            ScheduleTraceSelectedTextLayout("AfterOpen-Fill");
            e.Handled = true;
        }

        private void BtnSecondLayerFillColor_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
            {
                return;
            }

            TraceSelectedTextLayout("BeforeOpen-Fill2");
            OpenBackgroundSettingsPopup(sender as UIElement);
            ScheduleTraceSelectedTextLayout("AfterOpen-Fill2");
            e.Handled = true;
        }

        private void BtnSecondLayerAlignmentMenu_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
            {
                return;
            }

            CloseOtherSidePanels("AlignmentSettingsPopup");
            UpdateAlignmentPopupButtonStates();

            if (AlignmentSettingsPopup != null)
            {
                AlignmentSettingsPopup.PlacementTarget = sender as UIElement;
                AlignmentSettingsPopup.Placement = PlacementMode.Bottom;
                AlignmentSettingsPopup.HorizontalOffset = 0;
                AlignmentSettingsPopup.VerticalOffset = 6;
                AlignmentSettingsPopup.IsOpen = !AlignmentSettingsPopup.IsOpen;
            }

            e.Handled = true;
        }

        private void OpenBackgroundSettingsPopup(UIElement placementTarget)
        {
            CloseOtherSidePanels("BackgroundSettingsPopup");
            BackgroundSettingsPanel.BindTarget(_selectedTextBox);

            if (placementTarget != null)
            {
                BackgroundSettingsPopup.PlacementTarget = placementTarget;
                BackgroundSettingsPopup.Placement = PlacementMode.Bottom;
                BackgroundSettingsPopup.HorizontalOffset = 0;
                BackgroundSettingsPopup.VerticalOffset = 6;
            }
            else
            {
                RestoreSidePanelOffset(BackgroundSettingsPopup);
            }

            BackgroundSettingsPopup.IsOpen = true;
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
            if (AlignmentSettingsPopup != null)
            {
                AlignmentSettingsPopup.IsOpen = false;
            }
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
            if (keepPopupName != "AlignmentSettingsPopup" && AlignmentSettingsPopup != null)
            {
                AlignmentSettingsPopup.IsOpen = false;
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

            TraceSelectedTextLayout("BeforeOpen-TextColor");
            CloseOtherSidePanels("TextColorSettingsPopup");
            TextColorSettingsPanel.SetApplyMode(TextColorSettingsPanel.ApplyMode.FontColor);
            TextColorSettingsPanel.BindTarget(_selectedTextBox);

            if (sender is UIElement target)
            {
                TextColorSettingsPopup.PlacementTarget = target;
                TextColorSettingsPopup.Placement = PlacementMode.Bottom;
                TextColorSettingsPopup.HorizontalOffset = 0;
                TextColorSettingsPopup.VerticalOffset = 6;
            }

            TextColorSettingsPopup.IsOpen = true;
            ScheduleTraceSelectedTextLayout("AfterOpen-TextColor");
            e.Handled = true;
        }

        private void BtnTextHighlightColor_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            TraceSelectedTextLayout("BeforeOpen-Highlight");
            CloseOtherSidePanels("TextColorSettingsPopup");
            TextColorSettingsPanel.SetApplyMode(TextColorSettingsPanel.ApplyMode.TextHighlight);
            TextColorSettingsPanel.BindTarget(_selectedTextBox);

            if (sender is UIElement target)
            {
                TextColorSettingsPopup.PlacementTarget = target;
                TextColorSettingsPopup.Placement = PlacementMode.Bottom;
                TextColorSettingsPopup.HorizontalOffset = 0;
                TextColorSettingsPopup.VerticalOffset = 6;
            }

            TextColorSettingsPopup.IsOpen = true;
            ScheduleTraceSelectedTextLayout("AfterOpen-Highlight");
            e.Handled = true;
        }

        private void ScheduleTraceSelectedTextLayout(string tag)
        {
            if (!EnableTextDriftTrace || _selectedTextBox == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => TraceSelectedTextLayout($"{tag}-Layout")),
                System.Windows.Threading.DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(() => TraceSelectedTextLayout($"{tag}-Render")),
                System.Windows.Threading.DispatcherPriority.Render);
        }

        private void TraceSelectedTextLayout(string tag)
        {
            if (!EnableTextDriftTrace || _selectedTextBox == null)
            {
                return;
            }

            try
            {
                var tb = _selectedTextBox;
                var rtb = tb.RichTextBox;
                if (rtb == null)
                {
                    Debug.WriteLine($"[DriftTrace] {tag} RichTextBox=null");
                    return;
                }

                double left = Canvas.GetLeft(tb);
                double top = Canvas.GetTop(tb);
                var padding = rtb.Padding;
                var pagePadding = rtb.Document?.PagePadding ?? new Thickness(0);
                int blockCount = rtb.Document?.Blocks?.Count ?? 0;
                bool selEmpty = rtb.Selection?.IsEmpty ?? true;

                double firstParaLineHeight = -1;
                Thickness firstParaMargin = new Thickness(-1);
                if (rtb.Document?.Blocks != null)
                {
                    foreach (var block in rtb.Document.Blocks)
                    {
                        if (block is System.Windows.Documents.Paragraph p)
                        {
                            firstParaLineHeight = p.LineHeight;
                            firstParaMargin = p.Margin;
                            break;
                        }
                    }
                }

                Debug.WriteLine(
                    $"[DriftTrace] {tag} " +
                    $"XY=({left:F2},{top:F2}) Size=({tb.ActualWidth:F2},{tb.ActualHeight:F2}) " +
                    $"RtbSize=({rtb.ActualWidth:F2},{rtb.ActualHeight:F2}) " +
                    $"Pad=({padding.Left:F1},{padding.Top:F1},{padding.Right:F1},{padding.Bottom:F1}) " +
                    $"DocPad=({pagePadding.Left:F1},{pagePadding.Top:F1},{pagePadding.Right:F1},{pagePadding.Bottom:F1}) " +
                    $"Blocks={blockCount} SelEmpty={selEmpty} " +
                    $"ParaLineHeight={firstParaLineHeight:F2} ParaMargin=({firstParaMargin.Left:F1},{firstParaMargin.Top:F1},{firstParaMargin.Right:F1},{firstParaMargin.Bottom:F1}) " +
                    $"FontSize={tb.Data?.FontSize:F2} LineSpacing={tb.Data?.LineSpacing:F2}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DriftTrace] {tag} ERROR: {ex.Message}");
            }
        }

        private void OnTextColorAppliedFromPanel(string colorHex, TextColorSettingsPanel.ApplyMode mode)
        {
            if (mode == TextColorSettingsPanel.ApplyMode.TextHighlight)
            {
                UpdateSecondLayerTextHighlightIndicator(colorHex);
            }
            else
            {
                UpdateSecondLayerTextColorIndicator(colorHex);
            }

            MarkContentAsModified();
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
                "AlignmentSettingsPopup" => AlignmentSettingsPopup,
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
            UpdateAlignmentPopupButtonStates();
            MarkContentAsModified();
        }

        private void BtnAlignCenter_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;
            _selectedTextBox.ApplyStyle(textAlign: "Center");
            UpdateAlignmentPopupButtonStates();
            MarkContentAsModified();
        }

        private void BtnAlignRight_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;
            _selectedTextBox.ApplyStyle(textAlign: "Right");
            UpdateAlignmentPopupButtonStates();
            MarkContentAsModified();
        }

        private void BtnAlignJustify_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;
            _selectedTextBox.ApplyStyle(textAlign: "Justify");
            UpdateAlignmentPopupButtonStates();
            MarkContentAsModified();
        }

        private void BtnAlignTop_Click(object sender, RoutedEventArgs e)
        {
            ApplyTextVerticalAlign("Top");
        }

        private void BtnAlignMiddle_Click(object sender, RoutedEventArgs e)
        {
            ApplyTextVerticalAlign("Middle");
        }

        private void BtnAlignBottom_Click(object sender, RoutedEventArgs e)
        {
            ApplyTextVerticalAlign("Bottom");
        }

        private void ApplyTextVerticalAlign(string verticalAlign)
        {
            if (_selectedTextBox == null)
            {
                return;
            }

            _selectedTextBox.SetTextVerticalAlign(verticalAlign);
            UpdateAlignmentPopupButtonStates();
            MarkContentAsModified();
        }

        private void UpdateAlignmentPopupButtonStates()
        {
            if (_selectedTextBox?.Data == null)
            {
                ResetAlignmentPopupButtonStates();
                return;
            }

            string hAlign = _selectedTextBox.Data.TextAlign ?? "Left";
            string vAlign = _selectedTextBox.Data.TextVerticalAlign ?? "Top";

            SetAlignmentButtonActive(BtnSecondLayerAlignLeft, string.Equals(hAlign, "Left", StringComparison.OrdinalIgnoreCase));
            SetAlignmentButtonActive(BtnSecondLayerAlignCenter, string.Equals(hAlign, "Center", StringComparison.OrdinalIgnoreCase));
            SetAlignmentButtonActive(BtnSecondLayerAlignRight, string.Equals(hAlign, "Right", StringComparison.OrdinalIgnoreCase));
            SetAlignmentButtonActive(BtnSecondLayerAlignJustify, string.Equals(hAlign, "Justify", StringComparison.OrdinalIgnoreCase));

            SetAlignmentButtonActive(BtnSecondLayerAlignTop, string.Equals(vAlign, "Top", StringComparison.OrdinalIgnoreCase));
            SetAlignmentButtonActive(BtnSecondLayerAlignMiddle, string.Equals(vAlign, "Middle", StringComparison.OrdinalIgnoreCase));
            SetAlignmentButtonActive(BtnSecondLayerAlignBottom, string.Equals(vAlign, "Bottom", StringComparison.OrdinalIgnoreCase));
        }

        private void ResetAlignmentPopupButtonStates()
        {
            SetAlignmentButtonActive(BtnSecondLayerAlignLeft, false);
            SetAlignmentButtonActive(BtnSecondLayerAlignCenter, false);
            SetAlignmentButtonActive(BtnSecondLayerAlignRight, false);
            SetAlignmentButtonActive(BtnSecondLayerAlignJustify, false);
            SetAlignmentButtonActive(BtnSecondLayerAlignTop, false);
            SetAlignmentButtonActive(BtnSecondLayerAlignMiddle, false);
            SetAlignmentButtonActive(BtnSecondLayerAlignBottom, false);
        }

        private static void SetAlignmentButtonActive(System.Windows.Controls.Button button, bool active)
        {
            if (button == null)
            {
                return;
            }

            if (active)
            {
                button.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DCE8FF"));
                button.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7AA3F0"));
                button.BorderThickness = new Thickness(1);
            }
            else
            {
                button.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                button.ClearValue(System.Windows.Controls.Button.BorderBrushProperty);
                button.ClearValue(System.Windows.Controls.Button.BorderThicknessProperty);
            }
        }
    }
}
