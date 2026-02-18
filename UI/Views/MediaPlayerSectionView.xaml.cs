namespace ImageColorChanger.UI.Views
{
    public partial class MediaPlayerSectionView : System.Windows.Controls.UserControl
    {
        public MediaPlayerSectionView()
        {
            InitializeComponent();
        }

        public System.Windows.Controls.Border MediaPlayerPanelHost => MediaPlayerPanel;
        public System.Windows.Controls.Button BtnMediaPrevButton => BtnMediaPrev;
        public System.Windows.Controls.Button BtnMediaPlayPauseButton => BtnMediaPlayPause;
        public System.Windows.Controls.Button BtnMediaNextButton => BtnMediaNext;
        public System.Windows.Controls.Button BtnMediaStopButton => BtnMediaStop;
        public System.Windows.Controls.Slider MediaProgressSliderControl => MediaProgressSlider;
        public System.Windows.Controls.TextBlock MediaCurrentTimeLabel => MediaCurrentTime;
        public System.Windows.Controls.TextBlock MediaTotalTimeLabel => MediaTotalTime;
        public System.Windows.Controls.Button BtnPlayModeButton => BtnPlayMode;
        public System.Windows.Controls.Slider VolumeSliderControl => VolumeSlider;
    }
}
