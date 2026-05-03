using System;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

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
            public string WatermarkPosition { get; set; } = "RightBottom";
            public string WatermarkFontFamily { get; set; } = "Microsoft YaHei UI";
            public double WatermarkFontSize { get; set; } = 48;
            public double WatermarkOpacity { get; set; } = 43;
        }

        private readonly Func<State> _loadState;
        private readonly Action<bool> _setMaster;
        private readonly Action<bool> _setLyrics;
        private readonly Action<bool> _setCaption;
        private readonly Action<bool> _setTransparent;
        private readonly Action<string> _setWatermarkPosition;
        private readonly Action<string> _setWatermarkFontFamily;
        private readonly Action<double> _setWatermarkFontSize;
        private readonly Action<double> _setWatermarkOpacity;
        private readonly Action _pushWatermarkFrame;
        private bool _syncingUi;
        private string _selectedWatermarkPosition = "RightBottom";

        public NdiControlCenterWindow(
            Func<State> loadState,
            Action<bool> setMaster,
            Action<bool> setLyrics,
            Action<bool> setCaption,
            Action<bool> setTransparent,
            Action<string> setWatermarkPosition,
            Action<string> setWatermarkFontFamily,
            Action<double> setWatermarkFontSize,
            Action<double> setWatermarkOpacity,
            Action pushWatermarkFrame)
        {
            _loadState = loadState ?? throw new ArgumentNullException(nameof(loadState));
            _setMaster = setMaster ?? throw new ArgumentNullException(nameof(setMaster));
            _setLyrics = setLyrics ?? throw new ArgumentNullException(nameof(setLyrics));
            _setCaption = setCaption ?? throw new ArgumentNullException(nameof(setCaption));
            _setTransparent = setTransparent ?? throw new ArgumentNullException(nameof(setTransparent));
            _setWatermarkPosition = setWatermarkPosition ?? throw new ArgumentNullException(nameof(setWatermarkPosition));
            _setWatermarkFontFamily = setWatermarkFontFamily ?? throw new ArgumentNullException(nameof(setWatermarkFontFamily));
            _setWatermarkFontSize = setWatermarkFontSize ?? throw new ArgumentNullException(nameof(setWatermarkFontSize));
            _setWatermarkOpacity = setWatermarkOpacity ?? throw new ArgumentNullException(nameof(setWatermarkOpacity));
            _pushWatermarkFrame = pushWatermarkFrame ?? throw new ArgumentNullException(nameof(pushWatermarkFrame));

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

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshUi();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void PosLeftTopButton_Click(object sender, RoutedEventArgs e) => SetWatermarkPosition("LeftTop");
        private void PosRightTopButton_Click(object sender, RoutedEventArgs e) => SetWatermarkPosition("RightTop");
        private void PosLeftBottomButton_Click(object sender, RoutedEventArgs e) => SetWatermarkPosition("LeftBottom");
        private void PosRightBottomButton_Click(object sender, RoutedEventArgs e) => SetWatermarkPosition("RightBottom");
        private void PosCenterButton_Click(object sender, RoutedEventArgs e) => SetWatermarkPosition("Center");

        private void ChooseFontButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            try
            {
                using var fontDialog = new Forms.FontDialog
                {
                    FontMustExist = true,
                    ShowColor = false,
                    MinSize = 10,
                    MaxSize = 220,
                    Font = new System.Drawing.Font(
                        WatermarkFontTextBox.Text,
                        float.TryParse(WatermarkSizeTextBox.Text, out float tempSize) ? tempSize : 48f)
                };

                if (fontDialog.ShowDialog() != Forms.DialogResult.OK || fontDialog.Font == null)
                {
                    return;
                }

                WatermarkFontTextBox.Text = fontDialog.Font.FontFamily.Name;
                _setWatermarkFontFamily(WatermarkFontTextBox.Text);
                _pushWatermarkFrame();
            }
            catch
            {
            }
        }

        private void WatermarkSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _ = sender;
            _ = e;
            if (_syncingUi || !IsLoaded || WatermarkSizeTextBox == null)
            {
                return;
            }

            WatermarkSizeTextBox.Text = WatermarkSizeSlider.Value.ToString("0.#");
            _setWatermarkFontSize(WatermarkSizeSlider.Value);
            _pushWatermarkFrame();
        }

        private void WatermarkSizeTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (_syncingUi || !IsLoaded || WatermarkSizeSlider == null)
            {
                return;
            }

            if (!double.TryParse(WatermarkSizeTextBox.Text.Trim(), out double val))
            {
                return;
            }

            val = Math.Clamp(val, 10, 220);
            if (Math.Abs(WatermarkSizeSlider.Value - val) > 0.01)
            {
                WatermarkSizeSlider.Value = val;
            }
            _setWatermarkFontSize(val);
            _pushWatermarkFrame();
        }

        private void WatermarkOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _ = sender;
            _ = e;
            if (_syncingUi || !IsLoaded || WatermarkOpacityTextBox == null)
            {
                return;
            }

            WatermarkOpacityTextBox.Text = WatermarkOpacitySlider.Value.ToString("0.#");
            _setWatermarkOpacity(WatermarkOpacitySlider.Value);
            _pushWatermarkFrame();
        }

        private void WatermarkOpacityTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (_syncingUi || !IsLoaded || WatermarkOpacitySlider == null)
            {
                return;
            }

            if (!double.TryParse(WatermarkOpacityTextBox.Text.Trim(), out double val))
            {
                return;
            }

            val = Math.Clamp(val, 0, 100);
            if (Math.Abs(WatermarkOpacitySlider.Value - val) > 0.01)
            {
                WatermarkOpacitySlider.Value = val;
            }
            _setWatermarkOpacity(val);
            _pushWatermarkFrame();
        }

        private void RefreshUi()
        {
            State state = _loadState();
            _syncingUi = true;
            MasterToggle.IsChecked = state.MasterEnabled;
            LyricsToggle.IsChecked = state.LyricsEnabled;
            CaptionToggle.IsChecked = state.CaptionEnabled;
            TransparentToggle.IsChecked = state.LyricsTransparentEnabled;
            WatermarkFontTextBox.Text = state.WatermarkFontFamily;
            WatermarkSizeSlider.Value = Math.Clamp(state.WatermarkFontSize, 10, 220);
            WatermarkSizeTextBox.Text = WatermarkSizeSlider.Value.ToString("0.#");
            WatermarkOpacitySlider.Value = Math.Clamp(state.WatermarkOpacity, 0, 100);
            WatermarkOpacityTextBox.Text = WatermarkOpacitySlider.Value.ToString("0.#");
            _selectedWatermarkPosition = string.IsNullOrWhiteSpace(state.WatermarkPosition) ? "RightBottom" : state.WatermarkPosition;
            ApplyPositionButtonStyle();
            _syncingUi = false;

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

        private void SetWatermarkPosition(string position)
        {
            _selectedWatermarkPosition = position;
            ApplyPositionButtonStyle();
            _setWatermarkPosition(position);
            _pushWatermarkFrame();
        }

        private void ApplyPositionButtonStyle()
        {
            ApplyPosButton(PosLeftTopButton, _selectedWatermarkPosition == "LeftTop");
            ApplyPosButton(PosRightTopButton, _selectedWatermarkPosition == "RightTop");
            ApplyPosButton(PosLeftBottomButton, _selectedWatermarkPosition == "LeftBottom");
            ApplyPosButton(PosRightBottomButton, _selectedWatermarkPosition == "RightBottom");
            ApplyPosButton(PosCenterButton, _selectedWatermarkPosition == "Center");
        }

        private static void ApplyPosButton(System.Windows.Controls.Button button, bool active)
        {
            if (active)
            {
                button.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
                button.Foreground = System.Windows.Media.Brushes.White;
                button.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
            }
            else
            {
                button.Background = System.Windows.Media.Brushes.White;
                button.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85));
                button.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225));
            }
        }
    }
}
