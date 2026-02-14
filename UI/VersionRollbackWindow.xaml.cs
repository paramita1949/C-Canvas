#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ImageColorChanger.Services;
using MessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    public partial class VersionRollbackWindow : Window
    {
        private List<string> _versions = new();

        public VersionRollbackWindow()
        {
            InitializeComponent();
            CurrentVersionText.Text = $"当前版本：V{UpdateService.GetCurrentVersion()}";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadVersionsAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadVersionsAsync();
        }

        private async Task LoadVersionsAsync()
        {
            SetBusy(true, "正在获取可回退版本...");
            DownloadProgress.Value = 0;
            ProgressDetailText.Text = string.Empty;
            ReadmeTextBox.Text = "正在加载版本列表...";

            try
            {
                _versions = await UpdateService.GetRollbackVersionsAsync();
                VersionsListBox.ItemsSource = _versions.Select(v => $"V{v}").ToList();

                if (_versions.Count == 0)
                {
                    StatusText.Text = "未找到可回退版本（请确认网盘版本目录含 files.txt）";
                    ReadmeTextBox.Text = "暂无可选版本。";
                }
                else
                {
                    StatusText.Text = $"已加载 {_versions.Count} 个可用版本";
                    VersionsListBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"加载失败：{ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void VersionsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (VersionsListBox.SelectedIndex < 0 || VersionsListBox.SelectedIndex >= _versions.Count)
            {
                ReadmeTextBox.Text = "选择版本后显示说明...";
                return;
            }

            var selectedVersion = _versions[VersionsListBox.SelectedIndex];
            ReadmeTextBox.Text = $"正在读取 V{selectedVersion} 的更新说明...";

            try
            {
                var readme = await UpdateService.GetVersionReadmeAsync(selectedVersion);
                if (string.IsNullOrWhiteSpace(readme))
                {
                    ReadmeTextBox.Text = "修复若干BUG";
                }
                else
                {
                    ReadmeTextBox.Text = readme.Trim();
                }
            }
            catch
            {
                ReadmeTextBox.Text = "读取更新说明失败。";
            }
        }

        private async void RollbackButton_Click(object sender, RoutedEventArgs e)
        {
            if (VersionsListBox.SelectedIndex < 0 || VersionsListBox.SelectedIndex >= _versions.Count)
            {
                MessageBox.Show("请先选择一个版本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedVersion = _versions[VersionsListBox.SelectedIndex];
            var confirm = MessageBox.Show(
                $"即将安装版本 V{selectedVersion}。\n\n该操作会覆盖当前程序文件，安装后自动重启。\n\n是否继续？",
                "确认回退",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            SetBusy(true, $"正在准备版本 V{selectedVersion} ...");
            DownloadProgress.Value = 0;
            ProgressDetailText.Text = string.Empty;

            try
            {
                var versionInfo = await UpdateService.GetVersionInfoAsync(selectedVersion);
                if (versionInfo == null)
                {
                    MessageBox.Show("无法读取该版本的 files.txt 或版本不可用。", "回退失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var progress = new Progress<(long downloaded, long total)>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        var percentage = p.total > 0 ? (double)p.downloaded / p.total * 100 : 0;
                        DownloadProgress.Value = percentage;
                        ProgressDetailText.Text = $"{UpdateService.FormatFileSize(p.downloaded)} / {UpdateService.FormatFileSize(p.total)}";
                    });
                });

                StatusText.Text = $"正在下载 V{selectedVersion} ...";
                var updateDir = await UpdateService.DownloadUpdateAsync(versionInfo, progress);
                if (string.IsNullOrEmpty(updateDir))
                {
                    MessageBox.Show("下载失败，请检查网络后重试。", "回退失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusText.Text = "下载完成，正在应用更新并重启...";
                DownloadProgress.Value = 100;

                if (!UpdateService.ApplyUpdate(updateDir))
                {
                    MessageBox.Show("应用更新失败，请尝试以管理员身份运行。", "回退失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"回退过程中发生错误：{ex.Message}", "回退失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetBusy(bool busy, string? statusText = null)
        {
            RefreshButton.IsEnabled = !busy;
            RollbackButton.IsEnabled = !busy;
            CloseButton.IsEnabled = !busy;

            if (!string.IsNullOrWhiteSpace(statusText))
            {
                StatusText.Text = statusText;
            }
        }
    }
}

