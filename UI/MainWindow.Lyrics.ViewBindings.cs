using System.Windows.Input;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private bool _isLyricsSectionEventsWired;

        private System.Windows.Controls.Grid LyricsEditorPanel => LyricsSectionView?.LyricsEditorPanelRoot;
        private System.Windows.Controls.TextBlock LyricsFontSizeDisplay => LyricsSectionView?.LyricsFontSizeDisplayLabel;
        private System.Windows.Controls.Button BtnCloseLyricsEditor => LyricsSectionView?.BtnCloseLyricsEditorButton;
        private System.Windows.Controls.Button BtnLyricsTextColor => LyricsSectionView?.BtnLyricsTextColorButton;
        private System.Windows.Controls.Button BtnLyricsAlignLeft => LyricsSectionView?.BtnLyricsAlignLeftButton;
        private System.Windows.Controls.Button BtnLyricsAlignCenter => LyricsSectionView?.BtnLyricsAlignCenterButton;
        private System.Windows.Controls.Button BtnLyricsAlignRight => LyricsSectionView?.BtnLyricsAlignRightButton;
        private System.Windows.Controls.Button BtnLyricsClear => LyricsSectionView?.BtnLyricsClearButton;
        private System.Windows.Controls.Button BtnLyricsSliceToggle => LyricsSectionView?.BtnLyricsSliceToggleButton;
        private System.Windows.Controls.Button BtnLyricsSliceRule1 => LyricsSectionView?.BtnLyricsSliceRule1Button;
        private System.Windows.Controls.Button BtnLyricsSliceRule2 => LyricsSectionView?.BtnLyricsSliceRule2Button;
        private System.Windows.Controls.Button BtnLyricsSliceRule3 => LyricsSectionView?.BtnLyricsSliceRule3Button;
        private System.Windows.Controls.TextBlock LyricsSliceStateText => null;
        private System.Windows.Controls.Border LyricsSlicePanel => LyricsSectionView?.LyricsSlicePanelHost;
        private System.Windows.Controls.ListBox LyricsSliceList => LyricsSectionView?.LyricsSliceListHost;
        private System.Windows.Controls.Button BtnLyricsSlicePrev => LyricsSectionView?.BtnLyricsSlicePrevButton;
        private System.Windows.Controls.Button BtnLyricsSliceNext => LyricsSectionView?.BtnLyricsSliceNextButton;
        private System.Windows.Controls.StackPanel LyricsPageNavPanel => LyricsSectionView?.LyricsPageNavPanelContainer;
        private System.Windows.Controls.Button BtnLyricsPagingToggle => LyricsSectionView?.BtnLyricsPagingToggleButton;
        private System.Windows.Controls.Button BtnLyricsPageUp => LyricsSectionView?.BtnLyricsPageUpButton;
        private System.Windows.Controls.TextBlock LyricsPagingStateText => LyricsSectionView?.LyricsPagingStateTextLabel;
        private System.Windows.Controls.Button BtnLyricsPageDown => LyricsSectionView?.BtnLyricsPageDownButton;
        private System.Windows.Controls.ScrollViewer LyricsScrollViewer => LyricsSectionView?.LyricsScrollViewerHost;
        private System.Windows.Controls.TextBox LyricsTextBox => LyricsSectionView?.LyricsTextBoxEditor;
        private System.Windows.Controls.Grid LyricsSplitGrid => LyricsSectionView?.LyricsSplitGridHost;
        private System.Windows.Controls.Border LyricsSplitRegion1 => LyricsSectionView?.LyricsSplitRegion1Border;
        private System.Windows.Controls.Border LyricsSplitRegion2 => LyricsSectionView?.LyricsSplitRegion2Border;
        private System.Windows.Controls.Border LyricsSplitRegion3 => LyricsSectionView?.LyricsSplitRegion3Border;
        private System.Windows.Controls.Border LyricsSplitRegion4 => LyricsSectionView?.LyricsSplitRegion4Border;
        private System.Windows.Controls.TextBox LyricsSplitTextBox1 => LyricsSectionView?.LyricsSplitTextBox1Editor;
        private System.Windows.Controls.TextBox LyricsSplitTextBox2 => LyricsSectionView?.LyricsSplitTextBox2Editor;
        private System.Windows.Controls.TextBox LyricsSplitTextBox3 => LyricsSectionView?.LyricsSplitTextBox3Editor;
        private System.Windows.Controls.TextBox LyricsSplitTextBox4 => LyricsSectionView?.LyricsSplitTextBox4Editor;

        private void InitializeLyricsSectionBindings()
        {
            if (_isLyricsSectionEventsWired || LyricsSectionView == null)
            {
                return;
            }

            if (BtnCloseLyricsEditor != null) BtnCloseLyricsEditor.Click += BtnCloseLyricsEditor_Click;
            if (LyricsFontSizeDisplay != null) LyricsFontSizeDisplay.MouseWheel += LyricsFontSizeDisplay_MouseWheel;
            if (BtnLyricsTextColor != null) BtnLyricsTextColor.Click += BtnLyricsTextColor_Click;
            if (BtnLyricsAlignLeft != null) BtnLyricsAlignLeft.Click += BtnLyricsAlignLeft_Click;
            if (BtnLyricsAlignCenter != null) BtnLyricsAlignCenter.Click += BtnLyricsAlignCenter_Click;
            if (BtnLyricsAlignRight != null) BtnLyricsAlignRight.Click += BtnLyricsAlignRight_Click;
            if (BtnLyricsClear != null) BtnLyricsClear.Click += BtnLyricsClear_Click;
            if (BtnLyricsSliceToggle != null) BtnLyricsSliceToggle.Click += BtnLyricsSliceToggle_Click;
            if (BtnLyricsSliceRule1 != null) BtnLyricsSliceRule1.Click += BtnLyricsSliceRule1_Click;
            if (BtnLyricsSliceRule2 != null) BtnLyricsSliceRule2.Click += BtnLyricsSliceRule2_Click;
            if (BtnLyricsSliceRule3 != null) BtnLyricsSliceRule3.Click += BtnLyricsSliceRule3_Click;
            if (BtnLyricsSlicePrev != null) BtnLyricsSlicePrev.Click += BtnLyricsSlicePrev_Click;
            if (BtnLyricsSliceNext != null) BtnLyricsSliceNext.Click += BtnLyricsSliceNext_Click;
            if (BtnLyricsPagingToggle != null) BtnLyricsPagingToggle.Click += BtnLyricsPagingToggle_Click;
            if (BtnLyricsPageUp != null) BtnLyricsPageUp.Click += BtnLyricsPageUp_Click;
            if (BtnLyricsPageDown != null) BtnLyricsPageDown.Click += BtnLyricsPageDown_Click;
            if (LyricsSliceList != null) LyricsSliceList.SelectionChanged += LyricsSliceList_SelectionChanged;
            if (LyricsSliceList != null) LyricsSliceList.PreviewMouseWheel += LyricsSliceList_PreviewMouseWheel;

            if (LyricsScrollViewer != null)
            {
                LyricsScrollViewer.ScrollChanged += LyricsScrollViewer_ScrollChanged;
                LyricsScrollViewer.MouseRightButtonUp += LyricsScrollViewer_RightClick;
            }

            WireLyricsTextBoxEvents(LyricsTextBox);
            WireLyricsTextBoxEvents(LyricsSplitTextBox1);
            WireLyricsTextBoxEvents(LyricsSplitTextBox2);
            WireLyricsTextBoxEvents(LyricsSplitTextBox3);
            WireLyricsTextBoxEvents(LyricsSplitTextBox4);

            _isLyricsSectionEventsWired = true;
        }

        private void WireLyricsTextBoxEvents(System.Windows.Controls.TextBox textBox)
        {
            if (textBox == null)
            {
                return;
            }

            textBox.TextChanged += LyricsTextBox_TextChanged;
            textBox.SelectionChanged += LyricsTextBox_SelectionChanged;
            textBox.PreviewKeyDown += LyricsTextBox_PreviewKeyDown;
            textBox.PreviewMouseWheel += LyricsTextBox_PreviewMouseWheel;
            textBox.GotFocus += LyricsEditor_GotFocus;
        }
    }
}
