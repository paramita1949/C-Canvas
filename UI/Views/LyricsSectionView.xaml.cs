namespace ImageColorChanger.UI.Views
{
    public partial class LyricsSectionView : System.Windows.Controls.UserControl
    {
        public LyricsSectionView()
        {
            InitializeComponent();
        }

        public System.Windows.Controls.Grid LyricsEditorPanelRoot => LyricsEditorPanel;
        public System.Windows.Controls.TextBlock LyricsFontSizeDisplayLabel => LyricsFontSizeDisplay;
        public System.Windows.Controls.Button BtnCloseLyricsEditorButton => BtnCloseLyricsEditor;
        public System.Windows.Controls.Button BtnLyricsTextColorButton => BtnLyricsTextColor;
        public System.Windows.Controls.Button BtnLyricsWatermarkButton => BtnLyricsWatermark;
        public System.Windows.Controls.Button BtnLyricsAlignLeftButton => BtnLyricsAlignLeft;
        public System.Windows.Controls.Button BtnLyricsAlignCenterButton => BtnLyricsAlignCenter;
        public System.Windows.Controls.Button BtnLyricsAlignRightButton => BtnLyricsAlignRight;
        public System.Windows.Controls.Button BtnLyricsClearButton => BtnLyricsClear;
        public System.Windows.Controls.Button BtnLyricsSliceRuleDefaultButton => BtnLyricsSliceRuleDefault;
        public System.Windows.Controls.Button BtnLyricsSliceRule1Button => BtnLyricsSliceRule1;
        public System.Windows.Controls.Button BtnLyricsSliceRule2Button => BtnLyricsSliceRule2;
        public System.Windows.Controls.Button BtnLyricsSliceRule3Button => BtnLyricsSliceRule3;
        public System.Windows.Controls.Button BtnLyricsSliceRule4Button => BtnLyricsSliceRule4;
        public System.Windows.Controls.StackPanel LyricsSliceToolbarContainer => LyricsSliceToolbar;
        public System.Windows.Controls.Border LyricsSlicePanelHost => LyricsSlicePanel;
        public System.Windows.Controls.ListBox LyricsSliceListHost => LyricsSliceList;
        public System.Windows.Controls.Button BtnLyricsSlicePrevButton => BtnLyricsSlicePrev;
        public System.Windows.Controls.Button BtnLyricsSliceNextButton => BtnLyricsSliceNext;
        public System.Windows.Controls.StackPanel LyricsPageNavPanelContainer => LyricsPageNavPanel;
        public System.Windows.Controls.Button BtnLyricsPagingToggleButton => BtnLyricsPagingToggle;
        public System.Windows.Controls.Button BtnLyricsPageUpButton => BtnLyricsPageUp;
        public System.Windows.Controls.TextBlock LyricsPagingStateTextLabel => LyricsPagingStateText;
        public System.Windows.Controls.Button BtnLyricsPageDownButton => BtnLyricsPageDown;
        public System.Windows.Controls.ScrollViewer LyricsScrollViewerHost => LyricsScrollViewer;
        public System.Windows.Controls.TextBox LyricsTextBoxEditor => LyricsTextBox;
        public System.Windows.Controls.Grid LyricsSplitGridHost => LyricsSplitGrid;
        public System.Windows.Controls.Border LyricsSplitRegion1Border => LyricsSplitRegion1;
        public System.Windows.Controls.Border LyricsSplitRegion2Border => LyricsSplitRegion2;
        public System.Windows.Controls.Border LyricsSplitRegion3Border => LyricsSplitRegion3;
        public System.Windows.Controls.Border LyricsSplitRegion4Border => LyricsSplitRegion4;
        public System.Windows.Controls.TextBox LyricsSplitTextBox1Editor => LyricsSplitTextBox1;
        public System.Windows.Controls.TextBox LyricsSplitTextBox2Editor => LyricsSplitTextBox2;
        public System.Windows.Controls.TextBox LyricsSplitTextBox3Editor => LyricsSplitTextBox3;
        public System.Windows.Controls.TextBox LyricsSplitTextBox4Editor => LyricsSplitTextBox4;
    }
}
