#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ImageColorChanger.Services;
using MessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    public partial class VersionRollbackWindow : Window
    {
        private List<string> _versions = new();
        private readonly string _currentVersion;
        private bool _isBusy;
        private bool _closed;
        private int _readmeRequestId;

        public VersionRollbackWindow()
        {
            InitializeComponent();
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
            MinHeight = Math.Min(MinHeight, MaxHeight);
            MinWidth = Math.Min(MinWidth, MaxWidth);
            Height = Math.Min(Height, MaxHeight);
            Width = Math.Min(Width, MaxWidth);
            _currentVersion = UpdateService.GetCurrentVersion();
            CurrentVersionText.Text = $"当前版本 V{_currentVersion}";
            UpdateSelectionState();
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
            if (_isBusy || _closed) return;
            SetBusy(true, "正在获取可回退版本...");
            ProgressPanel.Visibility = Visibility.Collapsed;
            DownloadProgress.Value = 0;
            ProgressDetailText.Text = string.Empty;
            // 刷新先清旧目标，失效说明请求，失败时不允许安装上次的选择。
            _readmeRequestId++;
            VersionsListBox.ItemsSource = null;
            _versions.Clear();
            EmptyVersionsText.Visibility = Visibility.Visible;
            EmptyVersionsText.Text = "正在加载…";
            UpdateSelectionState();
            ReadmeTextBox.Text = "正在加载版本列表...";

            try
            {
                var versions = await UpdateService.GetRollbackVersionsAsync();
                if (_closed) return;
                _versions = versions;
                VersionsListBox.ItemsSource = _versions.Select(v => $"V{v}").ToList();

                if (_versions.Count == 0)
                {
                    StatusText.Text = "暂无可回退的历史版本，可稍后刷新重试。";
                    EmptyVersionsText.Text = "暂无可回退版本";
                    ReadmeTextBox.Text = "暂无可选版本。";
                }
                else
                {
                    StatusText.Text = $"已加载 {_versions.Count} 个可用版本";
                    EmptyVersionsText.Visibility = Visibility.Collapsed;
                    VersionsListBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                if (_closed) return;
                StatusText.Text = $"加载失败：{ex.Message}";
                EmptyVersionsText.Text = "加载失败，请刷新重试";
                ReadmeTextBox.Text = "未能获取历史版本，请检查网络后刷新。";
            }
            finally
            {
                if (!_closed) SetBusy(false);
            }
        }

        private async void VersionsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var requestId = ++_readmeRequestId;
            UpdateSelectionState();
            if (VersionsListBox.SelectedIndex < 0 || VersionsListBox.SelectedIndex >= _versions.Count)
            {
                ReadmeTextBox.Text = "选择左侧版本，查看对应说明。";
                return;
            }

            var selectedVersion = _versions[VersionsListBox.SelectedIndex];
            ReadmeTextBox.Text = $"正在读取 V{selectedVersion} 的更新说明...";

            try
            {
                var readme = await UpdateService.GetVersionReadmeAsync(selectedVersion);
                if (_closed || requestId != _readmeRequestId) return;
                if (string.IsNullOrWhiteSpace(readme))
                {
                    ReadmeTextBox.Text = "该版本暂未提供更新说明。";
                }
                else
                {
                    ReadmeTextBox.Text = readme.Trim();
                }
            }
            catch
            {
                if (!_closed && requestId == _readmeRequestId)
                    ReadmeTextBox.Text = "读取更新说明失败，请重新选择版本后重试。";
            }
        }

        private async void RollbackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy || _closed) return;
            if (VersionsListBox.SelectedIndex < 0 || VersionsListBox.SelectedIndex >= _versions.Count)
            {
                MessageBox.Show("请先选择一个版本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedVersion = _versions[VersionsListBox.SelectedIndex];
            var confirm = MessageBox.Show(
                $"当前版本 V{_currentVersion} → 目标版本 V{selectedVersion}。\n\n该操作会覆盖当前程序文件，安装后自动重启。请先保存工作，旧版的数据兼容性请自行确认。\n\n是否继续？",
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
            if (!_isBusy) Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                while (source != null)
                {
                    if (source is System.Windows.Controls.Button) return;
                    source = source is System.Windows.Media.Visual || source is System.Windows.Media.Media3D.Visual3D
                        ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                        : (source as FrameworkContentElement)?.Parent ?? LogicalTreeHelper.GetParent(source);
                }
            }
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // 下载/应用更新时保持窗口，避免遗留无人确认的安装操作。
            e.Cancel = _isBusy;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _closed = true;
            _readmeRequestId++;
        }

        private void UpdateSelectionState()
        {
            var index = VersionsListBox.SelectedIndex;
            var selectedVersion = index >= 0 && index < _versions.Count ? _versions[index] : null;
            RollbackButton.IsEnabled = !_isBusy && selectedVersion != null;
            RollbackButton.Content = selectedVersion == null ? "选择回退版本" : $"回退到 V{selectedVersion}";
            TargetVersionText.Text = selectedVersion == null
                ? "尚未选择目标版本" : $"V{_currentVersion} → V{selectedVersion}";
            ReadmeTitleText.Text = selectedVersion == null ? "版本说明" : $"V{selectedVersion} 版本说明";
        }

        private void SetBusy(bool busy, string? statusText = null)
        {
            _isBusy = busy;
            RefreshButton.IsEnabled = !busy;
            VersionsListBox.IsEnabled = !busy;
            CloseButton.IsEnabled = !busy;
            CancelButton.IsEnabled = !busy;
            if (busy) ProgressPanel.Visibility = Visibility.Visible;
            UpdateSelectionState();

            if (!string.IsNullOrWhiteSpace(statusText))
            {
                StatusText.Text = statusText;
            }
        }
    }
}

