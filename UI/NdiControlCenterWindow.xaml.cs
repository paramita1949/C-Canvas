using System;
using System.Windows;
using System.Windows.Media;

namespace ImageColorChanger.UI
{
    public partial class NdiControlCenterWindow : Window
    {
        public sealed class State
        {
            public bool MasterEnabled { get; set; }
            public bool LyricsEnabled { get; set; }
            public bool CaptionEnabled { get; set; }
            public bool LyricsTransparentEnabled { get; set; }
            public int ConnectionCount { get; set; }
        }

        private readonly Func<State> _loadState;
        private readonly Action<bool> _setMaster;
        private readonly Action<bool> _setLyrics;
        private readonly Action<bool> _setCaption;
        private readonly Action<bool> _setTransparent;
        private readonly Action _openWatermarkSettings;

        public NdiControlCenterWindow(
            Func<State> loadState,
            Action<bool> setMaster,
            Action<bool> setLyrics,
            Action<bool> setCaption,
            Action<bool> setTransparent,
            Action openWatermarkSettings)
        {
            _loadState = loadState ?? throw new ArgumentNullException(nameof(loadState));
            _setMaster = setMaster ?? throw new ArgumentNullException(nameof(setMaster));
            _setLyrics = setLyrics ?? throw new ArgumentNullException(nameof(setLyrics));
            _setCaption = setCaption ?? throw new ArgumentNullException(nameof(setCaption));
            _setTransparent = setTransparent ?? throw new ArgumentNullException(nameof(setTransparent));
            _openWatermarkSettings = openWatermarkSettings ?? throw new ArgumentNullException(nameof(openWatermarkSettings));

            InitializeComponent();
            Loaded += (_, _) => RefreshUi();
        }

        private void MasterToggle_Click(object sender, RoutedEventArgs e)
        {
            _setMaster(MasterToggle.IsChecked == true);
            RefreshUi();
        }

        private void LyricsToggle_Click(object sender, RoutedEventArgs e)
        {
            _setLyrics(LyricsToggle.IsChecked == true);
            RefreshUi();
        }

        private void CaptionToggle_Click(object sender, RoutedEventArgs e)
        {
            _setCaption(CaptionToggle.IsChecked == true);
            RefreshUi();
        }

        private void TransparentToggle_Click(object sender, RoutedEventArgs e)
        {
            _setTransparent(TransparentToggle.IsChecked == true);
            RefreshUi();
        }

        private void WatermarkButton_Click(object sender, RoutedEventArgs e)
        {
            _openWatermarkSettings();
            RefreshUi();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshUi();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void RefreshUi()
        {
            State state = _loadState();
            MasterToggle.IsChecked = state.MasterEnabled;
            LyricsToggle.IsChecked = state.LyricsEnabled;
            CaptionToggle.IsChecked = state.CaptionEnabled;
            TransparentToggle.IsChecked = state.LyricsTransparentEnabled;

            bool connected = state.MasterEnabled && state.ConnectionCount > 0;
            RuntimeBadgeText.Text = connected ? "已连接" : "未连接";
            RuntimeBadge.Background = connected ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 252, 231)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246));
            RuntimeBadge.BorderBrush = connected ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(134, 239, 172)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 213, 219));
            RuntimeBadgeText.Foreground = connected ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105));

            MasterSummaryText.Text = state.MasterEnabled ? "已开启" : "已关闭";
            ConnectionSummaryText.Text = state.ConnectionCount.ToString();
            ChannelSummaryText.Text = $"{(state.LyricsEnabled ? "歌词开" : "歌词关")} / {(state.CaptionEnabled ? "字幕开" : "字幕关")}";

            if (state.LyricsTransparentEnabled && state.LyricsEnabled)
            {
                LyricsTransparentStateText.Text = "已生效";
                LyricsTransparentStateText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74));
            }
            else if (state.LyricsTransparentEnabled && !state.LyricsEnabled)
            {
                LyricsTransparentStateText.Text = "待生效（需开启歌词 NDI 输出）";
                LyricsTransparentStateText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(14, 165, 233));
            }
            else
            {
                LyricsTransparentStateText.Text = "未启用";
                LyricsTransparentStateText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
            }

            StatusText.Text = connected
                ? $"当前有 {state.ConnectionCount} 台客户端在线。\n你可以同时输出歌词与字幕，建议在接收端分别选择通道源。"
                : "当前没有客户端连接。\n可先配置通道与透明选项，开启 NDI 后会立即按当前配置生效。";
        }
    }
}
