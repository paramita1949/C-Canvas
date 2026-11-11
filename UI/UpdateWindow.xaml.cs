#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI
{
    public partial class UpdateWindow : Window
    {
        private readonly VersionInfo _versionInfo;
        private CancellationTokenSource? _downloadCancellation;
        private bool _closed;

        public UpdateWindow(VersionInfo versionInfo)
        {
            _versionInfo = versionInfo ?? throw new ArgumentNullException(nameof(versionInfo));
            InitializeComponent();
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
            MinHeight = Math.Min(MinHeight, MaxHeight);
            MinWidth = Math.Min(MinWidth, MaxWidth);
            Height = Math.Min(Height, MaxHeight);
            Width = Math.Min(Width, MaxWidth);
            InitializeUI();
        }

        private void InitializeUI()
        {
            HeaderTitleText.Text = $"更新到 V{_versionInfo.Version}";
            VersionText.Text = $"当前 V{UpdateService.GetCurrentVersion()}";
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(_versionInfo.ReleaseNotes)
                ? "暂无更新说明。" : _versionInfo.ReleaseNotes.Trim();
            InfoPanel.Visibility = Visibility.Visible;
            ProgressPanel.Visibility = Visibility.Collapsed;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.Button) return;
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            _downloadCancellation?.Cancel();
            base.OnClosed(e);
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCancellation != null) return;
            using var cancellation = new CancellationTokenSource();
            _downloadCancellation = cancellation;
            var token = cancellation.Token;
            UpdateButton.IsEnabled = false;
            SkipButtonText.Text = "取消更新";
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressText.Text = "正在连接…";
            ProgressDetailText.Text = "";
            DownloadProgress.IsIndeterminate = true;
            DownloadProgress.Value = 0;
            var clock = Stopwatch.StartNew();
            var acceptDownloadProgress = true;
            string? downloadedDirectory = null;
            var installationStarted = false;
            try
            {
                // Progress 已回到WPF上下文；服务侧节流，无需每个数据块再同步 Dispatcher.Invoke。
                var progress = new Progress<(long downloaded, long total)>(p =>
                {
                    if (_closed || token.IsCancellationRequested || !acceptDownloadProgress) return;
                    DownloadProgress.IsIndeterminate = p.total <= 0;
                    DownloadProgress.Value = p.total > 0 ? Math.Clamp((double)p.downloaded / p.total * 100, 0, 100) : 0;
                    ProgressText.Text = "正在下载…";
                    var size = p.total > 0 ? $"{UpdateService.FormatFileSize(p.downloaded)} / {UpdateService.FormatFileSize(p.total)}"
                        : $"已下载 {UpdateService.FormatFileSize(p.downloaded)}（总大小未知）";
                    var speed = (long)(p.downloaded / Math.Max(0.1, clock.Elapsed.TotalSeconds));
                    ProgressDetailText.Text = $"{size} · 平均 {UpdateService.FormatFileSize(speed)}/秒";
                });
                downloadedDirectory = await UpdateService.DownloadUpdateAsync(_versionInfo, progress, token);
                acceptDownloadProgress = false;
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(downloadedDirectory))
                {
                    ProgressText.Text = "下载未完成";
                    ProgressDetailText.Text = string.IsNullOrWhiteSpace(UpdateService.LastDownloadError)
                        ? "请重试或使用浏览器下载。" : UpdateService.LastDownloadError;
                    return;
                }
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 100;
                for (int i = 5; i > 0; i--)
                {
                    ProgressText.Text = $"下载完成，{i}秒后重启（可取消）";
                    await Task.Delay(1000, token);
                }
                token.ThrowIfCancellationRequested();
                installationStarted = UpdateService.ApplyUpdate(downloadedDirectory);
                if (!installationStarted)
                {
                    ProgressText.Text = "应用更新失败。";
                    ProgressDetailText.Text = "请检查安装目录权限，或使用浏览器下载后手动更新。";
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                if (!_closed)
                {
                    ProgressText.Text = "更新已取消";
                    ProgressDetailText.Text = "";
                }
            }
            catch (Exception ex)
            {
                if (!_closed)
                {
                    ProgressText.Text = "更新失败";
                    ProgressDetailText.Text = ex.Message;
                }
                Trace.WriteLine($"[Updater UI] {ex}");
            }
            finally
            {
                acceptDownloadProgress = false;
                if (!installationStarted) UpdateService.CleanupIncompleteDownload(downloadedDirectory);
                _downloadCancellation = null;
                if (!_closed)
                {
                    UpdateButton.IsEnabled = true;
                    UpdateButtonText.Text = "重试更新";
                    SkipButtonText.Text = "稍后";
                    DownloadProgress.IsIndeterminate = false;
                }
            }
        }

        private void BrowserDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            _downloadCancellation?.Cancel();
            if (!Uri.TryCreate(_versionInfo.DownloadUrl, UriKind.Absolute, out var url) ||
                (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            {
                ProgressPanel.Visibility = Visibility.Visible;
                ProgressText.Text = "此更新没有可用的浏览器下载地址。";
                return;
            }
            try
            {
                // 单包直链与应用内同源；多文件清单打开该网盘，避免只下载首文件导致不完整更新。
                var address = _versionInfo.Files.Count == 1 ? url.AbsoluteUri : url.GetLeftPart(UriPartial.Authority);
                Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ProgressPanel.Visibility = Visibility.Visible;
                ProgressText.Text = "无法打开浏览器。";
                ProgressDetailText.Text = ex.Message;
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadCancellation != null) _downloadCancellation.Cancel();
            else Close();
        }
    }
}
