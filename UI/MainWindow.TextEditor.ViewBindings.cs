using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using WpfControls = System.Windows.Controls;
using WpfPrimitives = System.Windows.Controls.Primitives;
using WpfShapes = System.Windows.Shapes;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private bool _isTextEditorSectionEventsWired;
        private Dictionary<string, Action<object, EventArgs>> _textEditorEventMap;

        private WpfControls.Grid TextEditorPanel => TextEditorSectionView?.TextEditorPanelRoot;
        private WpfControls.Button BtnToolbarMenu => TextEditorSectionView?.BtnToolbarMenuButton;
        private WpfControls.Button BtnAddText => TextEditorSectionView?.BtnAddTextButton;
        private WpfControls.Button BtnBackgroundImage => TextEditorSectionView?.BtnBackgroundImageButton;
        private WpfControls.Button BtnBackgroundColor => TextEditorSectionView?.BtnBackgroundColorButton;
        private WpfControls.Button BtnSplitView => TextEditorSectionView?.BtnSplitViewButton;
        private WpfControls.Button BtnSplitStretchMode => TextEditorSectionView?.BtnSplitStretchModeButton;
        private WpfControls.Button BtnSlideOutputMode => TextEditorSectionView?.BtnSlideOutputModeButton;
        private WpfControls.Button BtnComponent => TextEditorSectionView?.BtnComponentButton;
        private WpfControls.ComboBox FontFamilySelector => TextEditorSectionView?.FontFamilySelectorControl;
        private WpfControls.ComboBox FontSizeSelector => TextEditorSectionView?.FontSizeSelectorControl;
        private WpfControls.Button BtnIncreaseFontSize => TextEditorSectionView?.BtnIncreaseFontSizeButton;
        private WpfControls.Button BtnDecreaseFontSize => TextEditorSectionView?.BtnDecreaseFontSizeButton;
        private WpfControls.Button BtnBold => TextEditorSectionView?.BtnBoldButton;
        private WpfControls.Button BtnTextColor => TextEditorSectionView?.BtnTextColorButton;
        private WpfControls.Button BtnSaveTextProject => TextEditorSectionView?.BtnSaveTextProjectButton;
        private WpfControls.Button BtnLockProjection => TextEditorSectionView?.BtnLockProjectionButton;
        private WpfControls.Button BtnUpdateProjection => TextEditorSectionView?.BtnUpdateProjectionButton;
        private WpfControls.Button BtnCloseTextEditor => TextEditorSectionView?.BtnCloseTextEditorInPanelButton;
        private WpfControls.Button BtnCloseTextEditorInPanel => TextEditorSectionView?.BtnCloseTextEditorInPanelButton;
        private WpfControls.Button BtnCanvasAspectRatio => TextEditorSectionView?.BtnCanvasAspectRatioInPanelButton;
        private WpfControls.Button BtnCanvasAspectRatioInPanel => TextEditorSectionView?.BtnCanvasAspectRatioInPanelButton;
        private WpfControls.Border TextEditorMiniToolbar => TextEditorSectionView?.TextEditorMiniToolbarControl;
        private WpfControls.Button BtnSecondLayerSelect => TextEditorSectionView?.BtnSecondLayerSelectButton;
        private WpfControls.Button BtnSecondLayerAddText => TextEditorSectionView?.BtnSecondLayerAddTextButton;
        private WpfControls.StackPanel SecondLayerCanvasActions => TextEditorSectionView?.SecondLayerCanvasActionsControl;
        private WpfControls.Button BtnSecondLayerBold => TextEditorSectionView?.BtnSecondLayerBoldButton;
        private WpfControls.Button BtnSecondLayerTextHighlightColor => TextEditorSectionView?.BtnSecondLayerTextHighlightColorButton;
        private WpfControls.StackPanel SecondLayerSelectedActions => TextEditorSectionView?.SecondLayerSelectedActionsControl;
        private WpfShapes.Rectangle SecondLayerTextColorBar => TextEditorSectionView?.SecondLayerTextColorBarControl;
        private WpfShapes.Rectangle SecondLayerTextHighlightBar => TextEditorSectionView?.SecondLayerTextHighlightBarControl;
        private WpfControls.Border SecondLayerNoticeSeparator => TextEditorSectionView?.SecondLayerNoticeSeparatorControl;
        private WpfControls.Button BtnSecondLayerNoticeSettings => TextEditorSectionView?.BtnSecondLayerNoticeSettingsButton;
        private WpfControls.Button BtnSecondLayerNoticeProjectionToggle => TextEditorSectionView?.BtnSecondLayerNoticeProjectionToggleButton;
        private WpfControls.TextBlock SecondLayerNoticeProjectionToggleText => TextEditorSectionView?.SecondLayerNoticeProjectionToggleTextBlock;
        private WpfControls.Button BtnSecondLayerNoticeToggle => TextEditorSectionView?.BtnSecondLayerNoticeToggleButton;
        private WpfControls.Button BtnSecondLayerNoticeDelete => TextEditorSectionView?.BtnSecondLayerNoticeDeleteButton;
        private WpfShapes.Path SecondLayerNoticeToggleIcon => TextEditorSectionView?.SecondLayerNoticeToggleIconPath;
        private WpfControls.Border SecondLayerNoticeStickySeparator => TextEditorSectionView?.SecondLayerNoticeStickySeparatorControl;
        private WpfControls.Button BtnSecondLayerNoticeStickyToggle => TextEditorSectionView?.BtnSecondLayerNoticeStickyToggleButton;
        private WpfShapes.Path SecondLayerNoticeStickyToggleIcon => TextEditorSectionView?.SecondLayerNoticeStickyToggleIconPath;
        private WpfControls.Border SlidePanelBorder => TextEditorSectionView?.SlidePanelBorderControl;
        private WpfControls.ScrollViewer SlideScrollViewer => TextEditorSectionView?.SlideScrollViewerControl;
        private WpfControls.ListBox SlideListBox => TextEditorSectionView?.SlideListBoxControl;
        private WpfPrimitives.Popup BibleToolbar => TextEditorSectionView?.BibleToolbarPopup;
        private WpfControls.Canvas EditorCanvas => TextEditorSectionView?.EditorCanvasControl;
        private WpfControls.Grid EditorCanvasContainer => TextEditorSectionView?.EditorCanvasContainerControl;
        private WpfControls.Border MainBiblePopupBorder => TextEditorSectionView?.MainBiblePopupBorderControl;
        private WpfControls.TextBlock MainBiblePopupReferenceText => TextEditorSectionView?.MainBiblePopupReferenceTextControl;
        private WpfControls.ScrollViewer MainBiblePopupContentScrollViewer => TextEditorSectionView?.MainBiblePopupContentScrollViewerControl;
        private WpfControls.TextBlock MainBiblePopupContentText => TextEditorSectionView?.MainBiblePopupContentTextControl;
        private WpfControls.Button MainBiblePopupCloseButton => TextEditorSectionView?.MainBiblePopupCloseButtonControl;
        private WpfControls.Image MainBiblePopupOverlayImage => TextEditorSectionView?.MainBiblePopupOverlayImageControl;
        private WpfControls.Button MainBiblePopupOverlayCloseButton => TextEditorSectionView?.MainBiblePopupOverlayCloseButtonControl;
        private WpfControls.Button BtnFloatingItalic => TextEditorSectionView?.BtnSecondLayerItalicButton;
        private WpfControls.Button BtnFloatingUnderline => TextEditorSectionView?.BtnSecondLayerUnderlineButton;
        private WpfControls.Button BtnUnderline => TextEditorSectionView?.BtnSecondLayerUnderlineButton;
        private WpfControls.Button BtnSecondLayerAlignLeft => TextEditorSectionView?.BtnSecondLayerAlignLeftButton;
        private WpfControls.Button BtnSecondLayerAlignCenter => TextEditorSectionView?.BtnSecondLayerAlignCenterButton;
        private WpfControls.Button BtnSecondLayerAlignRight => TextEditorSectionView?.BtnSecondLayerAlignRightButton;
        private WpfControls.Button BtnSecondLayerAlignJustify => TextEditorSectionView?.BtnSecondLayerAlignJustifyButton;
        private WpfControls.Button BtnSecondLayerAlignTop => TextEditorSectionView?.BtnSecondLayerAlignTopButton;
        private WpfControls.Button BtnSecondLayerAlignMiddle => TextEditorSectionView?.BtnSecondLayerAlignMiddleButton;
        private WpfControls.Button BtnSecondLayerAlignBottom => TextEditorSectionView?.BtnSecondLayerAlignBottomButton;
        private WpfPrimitives.Popup BorderSettingsPopup => TextEditorSectionView?.BorderSettingsPopupControl;
        private WpfPrimitives.Popup AlignmentSettingsPopup => TextEditorSectionView?.AlignmentSettingsPopupControl;
        private BorderSettingsPanel BorderSettingsPanel => TextEditorSectionView?.BorderSettingsPanelControl;
        private WpfPrimitives.Popup BackgroundSettingsPopup => TextEditorSectionView?.BackgroundSettingsPopupControl;
        private BackgroundSettingsPanel BackgroundSettingsPanel => TextEditorSectionView?.BackgroundSettingsPanelControl;
        private WpfPrimitives.Popup TextColorSettingsPopup => TextEditorSectionView?.TextColorSettingsPopupControl;
        private TextColorSettingsPanel TextColorSettingsPanel => TextEditorSectionView?.TextColorSettingsPanelControl;
        private WpfPrimitives.Popup NoticeSettingsPopup => TextEditorSectionView?.NoticeSettingsPopupControl;
        private NoticeSettingsPanel NoticeSettingsPanel => TextEditorSectionView?.NoticeSettingsPanelControl;
        private WpfPrimitives.Popup ShadowSettingsPopup => TextEditorSectionView?.ShadowSettingsPopupControl;
        private ShadowSettingsPanel ShadowSettingsPanel => TextEditorSectionView?.ShadowSettingsPanelControl;
        private WpfPrimitives.Popup SpacingSettingsPopup => TextEditorSectionView?.SpacingSettingsPopupControl;
        private SpacingSettingsPanel SpacingSettingsPanel => TextEditorSectionView?.SpacingSettingsPanelControl;
        private WpfPrimitives.Popup AnimationSettingsPopup => TextEditorSectionView?.AnimationSettingsPopupControl;
        private AnimationSettingsPanel AnimationSettingsPanel => TextEditorSectionView?.AnimationSettingsPanelControl;
        private WpfControls.Canvas AlignmentGuidesCanvas => TextEditorSectionView?.AlignmentGuidesCanvasControl;
        private WpfShapes.Line VerticalCenterLine => TextEditorSectionView?.VerticalCenterLineControl;
        private WpfShapes.Line HorizontalCenterLine => TextEditorSectionView?.HorizontalCenterLineControl;
        private WpfShapes.Line VerticalAlignLine => TextEditorSectionView?.VerticalAlignLineControl;
        private WpfShapes.Line HorizontalAlignLine => TextEditorSectionView?.HorizontalAlignLineControl;

        private void InitializeTextEditorSectionBindings()
        {
            if (_isTextEditorSectionEventsWired || TextEditorSectionView == null)
            {
                return;
            }

            _textEditorEventMap = new Dictionary<string, Action<object, EventArgs>>();

            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnAddText_Click), BtnAddText_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnBackgroundImage_Click), BtnBackgroundImage_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnBackgroundColor_Click), BtnBackgroundColor_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSplitView_Click), BtnSplitView_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSplitStretchMode_Click), BtnSplitStretchMode_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSlideOutputMode_Click), BtnSlideOutputMode_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnComponent_Click), BtnComponent_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnComponentClock_Click), BtnComponentClock_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnComponentCountdown_Click), BtnComponentCountdown_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnComponentNotice_Click), BtnComponentNotice_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(MenuSplit_SubmenuOpened), MenuSplit_SubmenuOpened);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuImportSingle_Click), BtnMenuImportSingle_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuImportMulti_Click), BtnMenuImportMulti_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuImportVideo_Click), BtnMenuImportVideo_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuSplitSingle_Click), BtnMenuSplitSingle_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuSplitHorizontal_Click), BtnMenuSplitHorizontal_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuSplitVertical_Click), BtnMenuSplitVertical_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuSplitTriple_Click), BtnMenuSplitTriple_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuSplitQuad_Click), BtnMenuSplitQuad_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuLayoutGallery_Click), BtnMenuLayoutGallery_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuLayoutTitleSubtitle_Click), BtnMenuLayoutTitleSubtitle_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuLayoutSectionTitleCentered_Click), BtnMenuLayoutSectionTitleCentered_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuLayoutTitleBody_Click), BtnMenuLayoutTitleBody_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuLayoutTitleTopOnly_Click), BtnMenuLayoutTitleTopOnly_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuLayoutBodyKeyPoints_Click), BtnMenuLayoutBodyKeyPoints_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(MenuNdiOutput_SubmenuOpened), MenuNdiOutput_SubmenuOpened);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuNdiComplete_Click), BtnMenuNdiComplete_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuNdiTransparent_Click), BtnMenuNdiTransparent_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(MenuSlideTheme_SubmenuOpened), MenuSlideTheme_SubmenuOpened);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuSlideThemeDark_Click), BtnMenuSlideThemeDark_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnMenuSlideThemeLight_Click), BtnMenuSlideThemeLight_Click);
            BindTextEditorEvent<WpfControls.SelectionChangedEventArgs>(nameof(FontFamily_Changed), FontFamily_Changed);
            BindTextEditorEvent<RoutedEventArgs>(nameof(FontFamilySelector_GotFocus), FontFamilySelector_GotFocus);
            BindTextEditorEvent<WpfControls.SelectionChangedEventArgs>(nameof(FontSize_Changed), FontSize_Changed);
            BindTextEditorEvent<System.Windows.Input.MouseWheelEventArgs>(nameof(FontSizeSelector_MouseWheel), FontSizeSelector_MouseWheel);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnIncreaseFontSize_Click), BtnIncreaseFontSize_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnDecreaseFontSize_Click), BtnDecreaseFontSize_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnBold_Click), BtnBold_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnTextColor_Click), BtnTextColor_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSaveTextProject_Click), BtnSaveTextProject_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnLockProjection_Click), BtnLockProjection_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnUpdateProjection_Click), BtnUpdateProjection_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnCloseTextEditor_Click), BtnCloseTextEditor_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnCanvasAspectRatio_Click), BtnCanvasAspectRatio_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerSelect_Click), BtnSecondLayerSelect_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerAddText_Click), BtnSecondLayerAddText_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerCanvasBackground_Click), BtnSecondLayerCanvasBackground_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerCanvasLayout_Click), BtnSecondLayerCanvasLayout_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerFillColor_Click), BtnSecondLayerFillColor_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerNoticeSettings_Click), BtnSecondLayerNoticeSettings_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerNoticeProjectionToggle_Click), BtnSecondLayerNoticeProjectionToggle_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerNoticeToggle_Click), BtnSecondLayerNoticeToggle_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerNoticeDelete_Click), BtnSecondLayerNoticeDelete_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerAlignmentMenu_Click), BtnSecondLayerAlignmentMenu_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnSecondLayerBorder_Click), BtnSecondLayerBorder_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnTextHighlightColor_Click), BtnTextHighlightColor_Click);
            BindTextEditorEvent<System.Windows.Input.MouseWheelEventArgs>(nameof(SlideScrollViewer_PreviewMouseWheel), SlideScrollViewer_PreviewMouseWheel);
            BindTextEditorEvent<WpfControls.SelectionChangedEventArgs>(nameof(SlideListBox_SelectionChanged), SlideListBox_SelectionChanged);
            BindTextEditorEvent<System.Windows.Input.MouseButtonEventArgs>(nameof(SlideListBox_RightClick), SlideListBox_RightClick);
            BindTextEditorEvent<System.Windows.Input.MouseButtonEventArgs>(nameof(SlideListBox_PreviewMouseLeftButtonDown), SlideListBox_PreviewMouseLeftButtonDown);
            BindTextEditorEvent<System.Windows.Input.MouseEventArgs>(nameof(SlideListBox_PreviewMouseMove), SlideListBox_PreviewMouseMove);
            BindTextEditorEvent<System.Windows.DragEventArgs>(nameof(SlideListBox_Drop), SlideListBox_Drop);
            BindTextEditorEvent<System.Windows.DragEventArgs>(nameof(SlideListBox_DragOver), SlideListBox_DragOver);
            BindTextEditorEvent<System.Windows.DragEventArgs>(nameof(SlideListBox_DragLeave), SlideListBox_DragLeave);
            BindTextEditorEvent<System.Windows.Input.KeyEventArgs>(nameof(SlideListBox_KeyDown), SlideListBox_KeyDown);
            BindTextEditorEvent<System.Windows.Input.MouseButtonEventArgs>(nameof(EditorCanvas_MouseDown), EditorCanvas_MouseDown);
            BindTextEditorEvent<System.Windows.Input.KeyEventArgs>(nameof(EditorCanvas_KeyDown), EditorCanvas_KeyDown);
            BindTextEditorEvent<RoutedEventArgs>(nameof(MainBiblePopupClose_Click), MainBiblePopupClose_Click);
            BindTextEditorEvent<System.Windows.Input.MouseButtonEventArgs>(nameof(MainBiblePopupOverlayImage_PreviewMouseLeftButtonDown), MainBiblePopupOverlayImage_PreviewMouseLeftButtonDown);
            BindTextEditorEvent<System.Windows.Input.MouseWheelEventArgs>(nameof(MainBiblePopupOverlayImage_PreviewMouseWheel), MainBiblePopupOverlayImage_PreviewMouseWheel);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnBibleInsertStyleSettings_Click), BtnBibleInsertStyleSettings_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnFloatingBorder_Click), BtnFloatingBorder_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnFloatingBackground_Click), BtnFloatingBackground_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnFloatingShadow_Click), BtnFloatingShadow_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnFloatingSpacing_Click), BtnFloatingSpacing_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnFloatingAnimation_Click), BtnFloatingAnimation_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnFloatingItalic_Click), BtnFloatingItalic_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnUnderline_Click), BtnUnderline_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnAlignLeft_Click), BtnAlignLeft_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnAlignCenter_Click), BtnAlignCenter_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnAlignRight_Click), BtnAlignRight_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnAlignJustify_Click), BtnAlignJustify_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnAlignTop_Click), BtnAlignTop_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnAlignMiddle_Click), BtnAlignMiddle_Click);
            BindTextEditorEvent<RoutedEventArgs>(nameof(BtnAlignBottom_Click), BtnAlignBottom_Click);
            BindTextEditorEvent<System.Windows.Input.MouseButtonEventArgs>(nameof(SidePanelDragHandle_MouseLeftButtonDown), SidePanelDragHandle_MouseLeftButtonDown);
            BindTextEditorEvent<System.Windows.Input.MouseEventArgs>(nameof(SidePanelDragHandle_MouseMove), SidePanelDragHandle_MouseMove);
            BindTextEditorEvent<System.Windows.Input.MouseButtonEventArgs>(nameof(SidePanelDragHandle_MouseLeftButtonUp), SidePanelDragHandle_MouseLeftButtonUp);
            BindTextEditorEvent<RoutedEventArgs>(nameof(SidePanelHeaderClose_Click), SidePanelHeaderClose_Click);
            BindTextEditorEvent<System.Windows.Input.KeyEventArgs>(nameof(TextEditorPanel_KeyDown), TextEditorPanel_KeyDown);
            BindTextEditorEvent<System.Windows.Input.MouseWheelEventArgs>(nameof(TextEditorPanel_PreviewMouseWheel), TextEditorPanel_PreviewMouseWheel);
            BindTextEditorEvent<System.Windows.Input.MouseButtonEventArgs>(nameof(TextEditorPanel_PreviewMouseDown), TextEditorPanel_PreviewMouseDown);

            TextEditorSectionView.EventForwarder = DispatchTextEditorSectionEvent;
            _isTextEditorSectionEventsWired = true;
        }

        private void BindTextEditorEvent<TEventArgs>(string eventName, Action<object, TEventArgs> handler)
            where TEventArgs : EventArgs
        {
            _textEditorEventMap[eventName] = (sender, args) =>
            {
                if (args is TEventArgs typedArgs)
                {
                    handler(sender, typedArgs);
                    return;
                }

                LogTextEditorDispatchTypeMismatch(eventName, typeof(TEventArgs), args?.GetType());
            };
        }

        private void DispatchTextEditorSectionEvent(string eventName, object sender, EventArgs args)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                LogTextEditorDispatchWarning("empty_event_name", "收到空事件名，已忽略。");
                return;
            }

            if (_textEditorEventMap == null)
            {
                LogTextEditorDispatchWarning(eventName, "事件分发表未初始化。");
                return;
            }

            if (_textEditorEventMap.TryGetValue(eventName, out var handler))
            {
                handler(sender, args);
                return;
            }

            LogTextEditorDispatchWarning(eventName, "未找到对应处理器。");
        }

        private void LogTextEditorDispatchTypeMismatch(string eventName, Type expectedType, Type actualType)
        {
            string message =
                $"[TextEditorDispatch] 参数类型不匹配: event={eventName}, expected={expectedType?.Name}, actual={actualType?.Name ?? "null"}";

            Debug.WriteLine(message);
            Trace.WriteLine(message);
#if DEBUG
            Debug.Fail(message);
#endif
        }

        private void LogTextEditorDispatchWarning(string eventName, string detail)
        {
            string message = $"[TextEditorDispatch] event={eventName}, detail={detail}";
            Debug.WriteLine(message);
            Trace.WriteLine(message);
#if DEBUG
            Debug.Fail(message);
#endif
        }
    }
}
