namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private bool _isMediaSectionEventsWired;

        private System.Windows.Controls.Border MediaPlayerPanel => MediaPlayerSectionView?.MediaPlayerPanelHost;
        private System.Windows.Controls.Button BtnMediaPrev => MediaPlayerSectionView?.BtnMediaPrevButton;
        private System.Windows.Controls.Button BtnMediaPlayPause => MediaPlayerSectionView?.BtnMediaPlayPauseButton;
        private System.Windows.Controls.Button BtnMediaNext => MediaPlayerSectionView?.BtnMediaNextButton;
        private System.Windows.Controls.Button BtnMediaStop => MediaPlayerSectionView?.BtnMediaStopButton;
        private System.Windows.Controls.Slider MediaProgressSlider => MediaPlayerSectionView?.MediaProgressSliderControl;
        private System.Windows.Controls.TextBlock MediaCurrentTime => MediaPlayerSectionView?.MediaCurrentTimeLabel;
        private System.Windows.Controls.TextBlock MediaTotalTime => MediaPlayerSectionView?.MediaTotalTimeLabel;
        private System.Windows.Controls.Button BtnPlayMode => MediaPlayerSectionView?.BtnPlayModeButton;
        private System.Windows.Controls.Slider VolumeSlider => MediaPlayerSectionView?.VolumeSliderControl;

        private void InitializeMediaSectionBindings()
        {
            if (_isMediaSectionEventsWired || MediaPlayerSectionView == null)
            {
                return;
            }

            if (BtnMediaPrev != null) BtnMediaPrev.Click += BtnMediaPrev_Click;
            if (BtnMediaPlayPause != null) BtnMediaPlayPause.Click += BtnMediaPlayPause_Click;
            if (BtnMediaNext != null) BtnMediaNext.Click += BtnMediaNext_Click;
            if (BtnMediaStop != null) BtnMediaStop.Click += BtnMediaStop_Click;
            if (BtnPlayMode != null) BtnPlayMode.Click += BtnPlayMode_Click;

            if (MediaProgressSlider != null) MediaProgressSlider.ValueChanged += MediaProgressSlider_ValueChanged;
            if (VolumeSlider != null) VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;

            _isMediaSectionEventsWired = true;
        }
    }
}
