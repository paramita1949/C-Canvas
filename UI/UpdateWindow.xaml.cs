#nullable enable
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI
{
    public partial class UpdateWindow : Window
    {
        private VersionInfo _versionInfo;
        private string? _downloadedFilePath;
        private readonly bool _isRequired;
        private bool _allowRequiredClose;

        public UpdateWindow(VersionInfo versionInfo)
            : this(versionInfo, false)
        {
        }

        public UpdateWindow(VersionInfo versionInfo, bool isRequired)
        {
            InitializeComponent();
            _versionInfo = versionInfo;
            _isRequired = isRequired;
            InitializeUI();
        }

        private void InitializeUI()
        {
            // 设置版本信息
            VersionText.Text = $"V{_versionInfo.Version}";
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(_versionInfo.ReleaseNotes)
                ? "修复若干BUG"
                : _versionInfo.ReleaseNotes.Trim();
            
            // 默认显示提示信息
            InfoPanel.Visibility = Visibility.Visible;
            ProgressPanel.Visibility = Visibility.Collapsed;

            if (_isRequired)
            {
                Title = "必须升级";
                HeaderTitleText.Text = "必须升级";
                InfoTitleText.Text = "当前版本需要升级后继续使用";
                InfoSubtitleText.Text = "请立即更新，避免重要修复无法生效";
                UpdateButtonText.Text = "立即升级";
                SkipButtonText.Text = "退出软件";
                CloseButton.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            // 禁用按钮
            UpdateButton.IsEnabled = false;
            SkipButton.IsEnabled = false;

            // 隐藏提示，显示进度
            InfoPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressText.Text = "正在下载更新...";

            try
            {
                // 创建进度报告器
                var progress = new Progress<(long downloaded, long total)>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        var percentage = p.total > 0 ? (double)p.downloaded / p.total * 100 : 0;
                        DownloadProgress.Value = percentage;
                        ProgressDetailText.Text = $"{UpdateService.FormatFileSize(p.downloaded)} / {UpdateService.FormatFileSize(p.total)}";
                    });
                });

                // 下载更新
                _downloadedFilePath = await UpdateService.DownloadUpdateAsync(_versionInfo, progress);

                if (string.IsNullOrEmpty(_downloadedFilePath))
                {
                    System.Windows.MessageBox.Show(
                        "下载更新失败，请检查网络连接后重试。",
                        "下载失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    
                    UpdateButton.IsEnabled = true;
                    SkipButton.IsEnabled = true;
                    InfoPanel.Visibility = Visibility.Visible;
                    ProgressPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                ProgressText.Text = "下载完成！准备安装...";
                DownloadProgress.Value = 100;
                await Task.Delay(800);

                // 倒计时5秒后自动安装
                for (int i = 5; i > 0; i--)
                {
                    ProgressText.Text = $"下载完成！{i}秒后自动安装...";
                    await Task.Delay(1000);
                }

                // 应用更新（自动重启）
                if (_isRequired)
                {
                    _allowRequiredClose = true;
                }

                if (UpdateService.ApplyUpdate(_downloadedFilePath))
                {
                    // 程序将自动退出并重启
                }
                else
                {
                    _allowRequiredClose = false;
                    System.Windows.MessageBox.Show(
                        "应用更新失败，请尝试以管理员身份运行程序。",
                        "更新失败",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    
                    UpdateButton.IsEnabled = true;
                    SkipButton.IsEnabled = true;
                    InfoPanel.Visibility = Visibility.Visible;
                    ProgressPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                if (_isRequired)
                {
                    _allowRequiredClose = false;
                }

                System.Windows.MessageBox.Show(
                    $"更新过程中发生错误：\n\n{ex.Message}",
                    "错误",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                
                UpdateButton.IsEnabled = true;
                SkipButton.IsEnabled = true;
                InfoPanel.Visibility = Visibility.Visible;
                ProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRequired)
            {
                _allowRequiredClose = true;
                System.Windows.Application.Current.Shutdown();
                return;
            }

            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isRequired && !_allowRequiredClose)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }
    }
}



