using System;
using System.Windows;

namespace ImageColorChanger.UI.Views
{
    public partial class TextEditorSectionView : System.Windows.Controls.UserControl
    {
        public Action<string, object, EventArgs> EventForwarder { get; set; }

        public TextEditorSectionView()
        {
            InitializeComponent();
        }

        public System.Windows.Controls.Grid TextEditorPanelRoot => TextEditorPanel;
        public System.Windows.Controls.Button BtnToolbarMenuButton => BtnToolbarMenu;
        public System.Windows.Controls.Button BtnAddTextButton => BtnAddText;
        public System.Windows.Controls.Button BtnBackgroundImageButton => BtnBackgroundImage;
        public System.Windows.Controls.Button BtnBackgroundColorButton => BtnBackgroundColor;
        public System.Windows.Controls.Button BtnSplitViewButton => BtnSplitView;
        public System.Windows.Controls.Button BtnSplitStretchModeButton => BtnSplitStretchMode;
        public System.Windows.Controls.Button BtnSlideOutputModeButton => BtnSlideOutputMode;
        public System.Windows.Controls.Button BtnComponentButton => BtnComponent;
        public System.Windows.Controls.ComboBox FontFamilySelectorControl => FontFamilySelector;
        public System.Windows.Controls.ComboBox FontSizeSelectorControl => FontSizeSelector;
        public System.Windows.Controls.Button BtnIncreaseFontSizeButton => BtnIncreaseFontSize;
        public System.Windows.Controls.Button BtnDecreaseFontSizeButton => BtnDecreaseFontSize;
        public System.Windows.Controls.Button BtnBoldButton => BtnBold;
        public System.Windows.Controls.Button BtnTextColorButton => BtnTextColor;
        public System.Windows.Controls.Button BtnSaveTextProjectButton => BtnSaveTextProject;
        public System.Windows.Controls.Button BtnLockProjectionButton => BtnLockProjection;
        public System.Windows.Controls.Button BtnUpdateProjectionButton => BtnUpdateProjection;
        public System.Windows.Controls.Button BtnCloseTextEditorInPanelButton => BtnCloseTextEditorInPanel;
        public System.Windows.Controls.Button BtnCanvasAspectRatioInPanelButton => BtnCanvasAspectRatioInPanel;
        public System.Windows.Controls.Border TextEditorMiniToolbarControl => TextEditorMiniToolbar;
        public System.Windows.Controls.Button BtnSecondLayerSelectButton => BtnSecondLayerSelect;
        public System.Windows.Controls.Button BtnSecondLayerAddTextButton => BtnSecondLayerAddText;
        public System.Windows.Controls.StackPanel SecondLayerCanvasActionsControl => SecondLayerCanvasActions;
        public System.Windows.Controls.Button BtnSecondLayerBoldButton => BtnSecondLayerBold;
        public System.Windows.Controls.StackPanel SecondLayerSelectedActionsControl => SecondLayerSelectedActions;
        public System.Windows.Shapes.Rectangle SecondLayerTextColorBarControl => SecondLayerTextColorBar;
        public System.Windows.Controls.Button BtnSecondLayerTextHighlightColorButton => BtnSecondLayerTextHighlightColor;
        public System.Windows.Shapes.Rectangle SecondLayerTextHighlightBarControl => SecondLayerTextHighlightBar;
        public System.Windows.Controls.Border SecondLayerNoticeSeparatorControl => SecondLayerNoticeSeparator;
        public System.Windows.Controls.Button BtnSecondLayerNoticeSettingsButton => BtnSecondLayerNoticeSettings;
        public System.Windows.Controls.Button BtnSecondLayerNoticeProjectionToggleButton => BtnSecondLayerNoticeProjectionToggle;
        public System.Windows.Controls.TextBlock SecondLayerNoticeProjectionToggleTextBlock => SecondLayerNoticeProjectionToggleText;
        public System.Windows.Controls.Button BtnSecondLayerNoticeToggleButton => BtnSecondLayerNoticeToggle;
        public System.Windows.Controls.Button BtnSecondLayerNoticeDeleteButton => BtnSecondLayerNoticeDelete;
        public System.Windows.Shapes.Path SecondLayerNoticeToggleIconPath => SecondLayerNoticeToggleIcon;
        public System.Windows.Controls.Border SecondLayerNoticeStickySeparatorControl => SecondLayerNoticeStickySeparator;
        public System.Windows.Controls.Button BtnSecondLayerNoticeStickyToggleButton => BtnSecondLayerNoticeStickyToggle;
        public System.Windows.Shapes.Path SecondLayerNoticeStickyToggleIconPath => SecondLayerNoticeStickyToggleIcon;
        public System.Windows.Controls.Border SlidePanelBorderControl => SlidePanelBorder;
        public System.Windows.Controls.ScrollViewer SlideScrollViewerControl => SlideScrollViewer;
        public System.Windows.Controls.ListBox SlideListBoxControl => SlideListBox;
        public System.Windows.Controls.Primitives.Popup BibleToolbarPopup => BibleToolbar;
        public System.Windows.Controls.Canvas EditorCanvasControl => EditorCanvas;
        public System.Windows.Controls.Grid EditorCanvasContainerControl => EditorCanvasContainer;
        public System.Windows.Controls.Border MainBiblePopupBorderControl => MainBiblePopupBorder;
        public System.Windows.Controls.TextBlock MainBiblePopupReferenceTextControl => MainBiblePopupReferenceText;
        public System.Windows.Controls.ScrollViewer MainBiblePopupContentScrollViewerControl => MainBiblePopupContentScrollViewer;
        public System.Windows.Controls.TextBlock MainBiblePopupContentTextControl => MainBiblePopupContentText;
        public System.Windows.Controls.Button MainBiblePopupCloseButtonControl => MainBiblePopupCloseButton;
        public System.Windows.Controls.Image MainBiblePopupOverlayImageControl => MainBiblePopupOverlayImage;
        public System.Windows.Controls.Button MainBiblePopupOverlayCloseButtonControl => MainBiblePopupOverlayCloseButton;
        public System.Windows.Controls.Button BtnSecondLayerItalicButton => BtnSecondLayerItalic;
        public System.Windows.Controls.Button BtnSecondLayerUnderlineButton => BtnSecondLayerUnderline;
        public System.Windows.Controls.Button BtnSecondLayerAlignLeftButton => BtnSecondLayerAlignLeft;
        public System.Windows.Controls.Button BtnSecondLayerAlignCenterButton => BtnSecondLayerAlignCenter;
        public System.Windows.Controls.Button BtnSecondLayerAlignRightButton => BtnSecondLayerAlignRight;
        public System.Windows.Controls.Button BtnSecondLayerAlignJustifyButton => BtnSecondLayerAlignJustify;
        public System.Windows.Controls.Button BtnSecondLayerAlignTopButton => BtnSecondLayerAlignTop;
        public System.Windows.Controls.Button BtnSecondLayerAlignMiddleButton => BtnSecondLayerAlignMiddle;
        public System.Windows.Controls.Button BtnSecondLayerAlignBottomButton => BtnSecondLayerAlignBottom;
        public System.Windows.Controls.Primitives.Popup BorderSettingsPopupControl => BorderSettingsPopup;
        public System.Windows.Controls.Primitives.Popup AlignmentSettingsPopupControl => AlignmentSettingsPopup;
        public UI.Controls.BorderSettingsPanel BorderSettingsPanelControl => BorderSettingsPanel;
        public System.Windows.Controls.Primitives.Popup BackgroundSettingsPopupControl => BackgroundSettingsPopup;
        public UI.Controls.BackgroundSettingsPanel BackgroundSettingsPanelControl => BackgroundSettingsPanel;
        public System.Windows.Controls.Primitives.Popup TextColorSettingsPopupControl => TextColorSettingsPopup;
        public UI.Controls.TextColorSettingsPanel TextColorSettingsPanelControl => TextColorSettingsPanel;
        public System.Windows.Controls.Primitives.Popup NoticeSettingsPopupControl => NoticeSettingsPopup;
        public UI.Controls.NoticeSettingsPanel NoticeSettingsPanelControl => NoticeSettingsPanel;
        public System.Windows.Controls.Primitives.Popup ShadowSettingsPopupControl => ShadowSettingsPopup;
        public UI.Controls.ShadowSettingsPanel ShadowSettingsPanelControl => ShadowSettingsPanel;
        public System.Windows.Controls.Primitives.Popup SpacingSettingsPopupControl => SpacingSettingsPopup;
        public UI.Controls.SpacingSettingsPanel SpacingSettingsPanelControl => SpacingSettingsPanel;
        public System.Windows.Controls.Primitives.Popup AnimationSettingsPopupControl => AnimationSettingsPopup;
        public UI.Controls.AnimationSettingsPanel AnimationSettingsPanelControl => AnimationSettingsPanel;
        public System.Windows.Controls.Canvas AlignmentGuidesCanvasControl => AlignmentGuidesCanvas;
        public System.Windows.Shapes.Line VerticalCenterLineControl => VerticalCenterLine;
        public System.Windows.Shapes.Line HorizontalCenterLineControl => HorizontalCenterLine;
        public System.Windows.Shapes.Line VerticalAlignLineControl => VerticalAlignLine;
        public System.Windows.Shapes.Line HorizontalAlignLineControl => HorizontalAlignLine;

        private void ForwardToMainWindow(string methodName, object sender, EventArgs args)
        {
            EventForwarder?.Invoke(methodName, sender, args);
        }

        private void BtnToolbarMenu_Click(object sender, RoutedEventArgs e)
        {
            if (BtnToolbarMenu?.ContextMenu == null)
            {
                return;
            }

            BtnToolbarMenu.ContextMenu.PlacementTarget = BtnToolbarMenu;
            BtnToolbarMenu.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            BtnToolbarMenu.ContextMenu.IsOpen = true;
        }

        private void BtnAddText_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnAddText_Click), sender, e);
        private void BtnBackgroundImage_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnBackgroundImage_Click), sender, e);
        private void BtnBackgroundColor_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnBackgroundColor_Click), sender, e);
        private void BtnSplitView_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSplitView_Click), sender, e);
        private void BtnSplitStretchMode_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSplitStretchMode_Click), sender, e);
        private void BtnSlideOutputMode_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSlideOutputMode_Click), sender, e);
        private void BtnComponent_Click(object sender, RoutedEventArgs e)
        {
            if (BtnComponent?.ContextMenu == null)
            {
                return;
            }

            BtnComponent.ContextMenu.PlacementTarget = BtnComponent;
            BtnComponent.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            BtnComponent.ContextMenu.IsOpen = true;
        }
        private void BtnComponentClock_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnComponentClock_Click), sender, e);
        private void BtnComponentCountdown_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnComponentCountdown_Click), sender, e);
        private void BtnComponentNotice_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnComponentNotice_Click), sender, e);
        private void MenuSplit_SubmenuOpened(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(MenuSplit_SubmenuOpened), sender, e);
        private void BtnMenuImportSingle_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuImportSingle_Click), sender, e);
        private void BtnMenuImportMulti_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuImportMulti_Click), sender, e);
        private void BtnMenuImportVideo_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuImportVideo_Click), sender, e);
        private void BtnMenuSplitSingle_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuSplitSingle_Click), sender, e);
        private void BtnMenuSplitHorizontal_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuSplitHorizontal_Click), sender, e);
        private void BtnMenuSplitVertical_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuSplitVertical_Click), sender, e);
        private void BtnMenuSplitTriple_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuSplitTriple_Click), sender, e);
        private void BtnMenuSplitQuad_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuSplitQuad_Click), sender, e);
        private void BtnMenuLayoutGallery_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuLayoutGallery_Click), sender, e);
        private void BtnMenuLayoutTitleSubtitle_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuLayoutTitleSubtitle_Click), sender, e);
        private void BtnMenuLayoutSectionTitleCentered_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuLayoutSectionTitleCentered_Click), sender, e);
        private void BtnMenuLayoutTitleBody_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuLayoutTitleBody_Click), sender, e);
        private void BtnMenuLayoutTitleTopOnly_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuLayoutTitleTopOnly_Click), sender, e);
        private void BtnMenuLayoutBodyKeyPoints_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuLayoutBodyKeyPoints_Click), sender, e);
        private void MenuNdiOutput_SubmenuOpened(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(MenuNdiOutput_SubmenuOpened), sender, e);
        private void BtnMenuNdiComplete_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuNdiComplete_Click), sender, e);
        private void BtnMenuNdiTransparent_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuNdiTransparent_Click), sender, e);
        private void MenuSlideTheme_SubmenuOpened(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(MenuSlideTheme_SubmenuOpened), sender, e);
        private void BtnMenuSlideThemeDark_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuSlideThemeDark_Click), sender, e);
        private void BtnMenuSlideThemeLight_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnMenuSlideThemeLight_Click), sender, e);
        private void FontFamily_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ForwardToMainWindow(nameof(FontFamily_Changed), sender, e);
        private void FontFamilySelector_GotFocus(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(FontFamilySelector_GotFocus), sender, e);
        private void FontSize_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ForwardToMainWindow(nameof(FontSize_Changed), sender, e);
        private void FontSizeSelector_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => ForwardToMainWindow(nameof(FontSizeSelector_MouseWheel), sender, e);
        private void BtnIncreaseFontSize_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnIncreaseFontSize_Click), sender, e);
        private void BtnDecreaseFontSize_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnDecreaseFontSize_Click), sender, e);
        private void BtnBold_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnBold_Click), sender, e);
        private void BtnTextColor_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnTextColor_Click), sender, e);
        private void BtnSaveTextProject_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSaveTextProject_Click), sender, e);
        private void BtnLockProjection_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnLockProjection_Click), sender, e);
        private void BtnUpdateProjection_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnUpdateProjection_Click), sender, e);
        private void BtnCloseTextEditor_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnCloseTextEditor_Click), sender, e);
        private void BtnCanvasAspectRatio_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnCanvasAspectRatio_Click), sender, e);
        private void BtnSecondLayerSelect_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerSelect_Click), sender, e);
        private void BtnSecondLayerAddText_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerAddText_Click), sender, e);
        private void BtnSecondLayerCanvasBackground_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerCanvasBackground_Click), sender, e);
        private void BtnSecondLayerCanvasLayout_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerCanvasLayout_Click), sender, e);
        private void BtnSecondLayerFillColor_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerFillColor_Click), sender, e);
        private void BtnSecondLayerNoticeSettings_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerNoticeSettings_Click), sender, e);
        private void BtnSecondLayerNoticeProjectionToggle_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerNoticeProjectionToggle_Click), sender, e);
        private void BtnSecondLayerNoticeToggle_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerNoticeToggle_Click), sender, e);
        private void BtnSecondLayerNoticeDelete_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerNoticeDelete_Click), sender, e);
        private void BtnSecondLayerAlignmentMenu_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerAlignmentMenu_Click), sender, e);
        private void BtnSecondLayerBorder_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnSecondLayerBorder_Click), sender, e);
        private void BtnTextHighlightColor_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnTextHighlightColor_Click), sender, e);
        private void SlideScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => ForwardToMainWindow(nameof(SlideScrollViewer_PreviewMouseWheel), sender, e);
        private void SlideListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ForwardToMainWindow(nameof(SlideListBox_SelectionChanged), sender, e);
        private void SlideListBox_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => ForwardToMainWindow(nameof(SlideListBox_RightClick), sender, e);
        private void SlideListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => ForwardToMainWindow(nameof(SlideListBox_PreviewMouseLeftButtonDown), sender, e);
        private void SlideListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e) => ForwardToMainWindow(nameof(SlideListBox_PreviewMouseMove), sender, e);
        private void SlideListBox_Drop(object sender, System.Windows.DragEventArgs e) => ForwardToMainWindow(nameof(SlideListBox_Drop), sender, e);
        private void SlideListBox_DragOver(object sender, System.Windows.DragEventArgs e) => ForwardToMainWindow(nameof(SlideListBox_DragOver), sender, e);
        private void SlideListBox_DragLeave(object sender, System.Windows.DragEventArgs e) => ForwardToMainWindow(nameof(SlideListBox_DragLeave), sender, e);
        private void SlideListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) => ForwardToMainWindow(nameof(SlideListBox_KeyDown), sender, e);
        private void EditorCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => ForwardToMainWindow(nameof(EditorCanvas_MouseDown), sender, e);
        private void EditorCanvas_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) => ForwardToMainWindow(nameof(EditorCanvas_KeyDown), sender, e);
        private void MainBiblePopupClose_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(MainBiblePopupClose_Click), sender, e);
        private void MainBiblePopupOverlayImage_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => ForwardToMainWindow(nameof(MainBiblePopupOverlayImage_PreviewMouseLeftButtonDown), sender, e);
        private void MainBiblePopupOverlayImage_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => ForwardToMainWindow(nameof(MainBiblePopupOverlayImage_PreviewMouseWheel), sender, e);
        private void BtnBibleInsertStyleSettings_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnBibleInsertStyleSettings_Click), sender, e);
        private void BtnFloatingBorder_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnFloatingBorder_Click), sender, e);
        private void BtnFloatingBackground_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnFloatingBackground_Click), sender, e);
        private void BtnFloatingShadow_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnFloatingShadow_Click), sender, e);
        private void BtnFloatingSpacing_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnFloatingSpacing_Click), sender, e);
        private void BtnFloatingAnimation_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnFloatingAnimation_Click), sender, e);
        private void BtnFloatingItalic_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnFloatingItalic_Click), sender, e);
        private void BtnUnderline_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnUnderline_Click), sender, e);
        private void BtnAlignLeft_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnAlignLeft_Click), sender, e);
        private void BtnAlignCenter_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnAlignCenter_Click), sender, e);
        private void BtnAlignRight_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnAlignRight_Click), sender, e);
        private void BtnAlignJustify_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnAlignJustify_Click), sender, e);
        private void BtnAlignTop_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnAlignTop_Click), sender, e);
        private void BtnAlignMiddle_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnAlignMiddle_Click), sender, e);
        private void BtnAlignBottom_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(BtnAlignBottom_Click), sender, e);
        private void SidePanelDragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => ForwardToMainWindow(nameof(SidePanelDragHandle_MouseLeftButtonDown), sender, e);
        private void SidePanelDragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) => ForwardToMainWindow(nameof(SidePanelDragHandle_MouseMove), sender, e);
        private void SidePanelDragHandle_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => ForwardToMainWindow(nameof(SidePanelDragHandle_MouseLeftButtonUp), sender, e);
        private void SidePanelHeaderClose_Click(object sender, RoutedEventArgs e) => ForwardToMainWindow(nameof(SidePanelHeaderClose_Click), sender, e);
        private void TextEditorPanel_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) => ForwardToMainWindow(nameof(TextEditorPanel_KeyDown), sender, e);
        private void TextEditorPanel_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) => ForwardToMainWindow(nameof(TextEditorPanel_PreviewMouseWheel), sender, e);
        private void TextEditorPanel_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => ForwardToMainWindow(nameof(TextEditorPanel_PreviewMouseDown), sender, e);
    }
}
