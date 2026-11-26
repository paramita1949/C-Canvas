using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Services.OnlineScores;
using Brushes = System.Windows.Media.Brushes;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private OnlineScoreSearchMode _onlineScoreMode;
        private ShigebenClient _onlineScoreClient;
        private CancellationTokenSource _onlineScoreSearchCts;
        private CancellationTokenSource _onlineScoreSelectionCts;
        private long _onlineScoreSearchVersion, _onlineScoreSelectionVersion;
        private bool _onlineScoreClosed;
        private OnlineScoreResult _pressedOnlineScore;
        private OnlineScoreLibrary _onlineScoreLibrary;
        private readonly HashSet<OnlineScoreResult> _onlineScoreLyricsProbes = new();
        private OnlineScoreLibrary GetOnlineScoreLibrary() => _onlineScoreLibrary ??= new OnlineScoreLibrary(DatabaseManagerService);
        private bool IsOnlineScoreMode => !_isBibleMode && _currentViewMode == NavigationViewMode.Files && _onlineScoreMode != OnlineScoreSearchMode.Local;

        private void ScoreNameSearchText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => EnterOnlineScoreMode(OnlineScoreSearchMode.Name);
        private void ScoreLyricsSearchText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => EnterOnlineScoreMode(OnlineScoreSearchMode.Lyrics);
        private void EnterOnlineScoreMode(OnlineScoreSearchMode mode)
        {
            if (_isBibleMode || _onlineScoreClosed) return;
            if (_currentViewMode != NavigationViewMode.Files) BtnShowFiles_Click(null, null);
            _onlineScoreMode = mode;
            UpdateSearchEntryModeVisual();
            SearchBox_TextChanged(SearchBox, null);
            SearchBox.Focus();
        }
        private void LeaveOnlineScoreMode()
        {
            _onlineScoreMode = OnlineScoreSearchMode.Local;
            CancelOnlineScoreRequests();
            if (OnlineScorePanel != null) OnlineScorePanel.Visibility = Visibility.Collapsed;
            if (OnlineScoreList != null) OnlineScoreList.ItemsSource = null;
            if (ProjectTree != null && !_isBibleMode && _currentViewMode == NavigationViewMode.Files) ProjectTree.Visibility = Visibility.Visible;
            UpdateIncludeMediaSearchToggleVisual();
        }
        private void CancelOnlineScoreRequests()
        {
            _pressedOnlineScore = null;
            _onlineScoreLyricsProbes.Clear();
            ++_onlineScoreSearchVersion; ++_onlineScoreSelectionVersion;
            _onlineScoreSearchCts?.Cancel(); _onlineScoreSearchCts?.Dispose(); _onlineScoreSearchCts = null;
            _onlineScoreSelectionCts?.Cancel(); _onlineScoreSelectionCts?.Dispose(); _onlineScoreSelectionCts = null;
        }
        private void UpdateOnlineScoreEntryVisual()
        {
            if (_isBibleMode || _currentViewMode != NavigationViewMode.Files) LeaveOnlineScoreMode();
            ApplyMediaSearchFilterTextStyle(ScoreNameSearchText, IsOnlineScoreMode && _onlineScoreMode == OnlineScoreSearchMode.Name);
            ApplyMediaSearchFilterTextStyle(ScoreLyricsSearchText, IsOnlineScoreMode && _onlineScoreMode == OnlineScoreSearchMode.Lyrics);
            if (OnlineScorePanel != null) OnlineScorePanel.Visibility = IsOnlineScoreMode ? Visibility.Visible : Visibility.Collapsed;
            if (IsOnlineScoreMode)
            {
                ProjectTree.Visibility = Visibility.Collapsed;
                SearchScope.Visibility = Visibility.Collapsed;
                MaterialDesignThemes.Wpf.HintAssist.SetHint(SearchBox, _onlineScoreMode == OnlineScoreSearchMode.Name ? "输入歌名，回车立即搜索" : "输入歌词，空格分隔关键词");
            }
        }
        private ShigebenClient GetOnlineScoreClient() => _onlineScoreClient ??= ShigebenClient.FromApprovedConfiguration();

        private async Task SearchOnlineScoresAsync(string text, bool immediate = false)
        {
            if (!IsOnlineScoreMode || _onlineScoreClosed) return;
            CancelOnlineScoreRequests();
            var cts = new CancellationTokenSource(); _onlineScoreSearchCts = cts;
            CancellationToken ct = cts.Token;
            long version = _onlineScoreSearchVersion;
            OnlineScoreList.ItemsSource = null; OnlineScoreLyricsPreview.Inlines.Clear();
            OnlineScoreStatus.Text = string.IsNullOrWhiteSpace(text) ? "使用上方搜索框输入歌名或歌词。" : "正在搜索诗歌本…";
            UpdateOnlineScoreEntryVisual();
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                if (!immediate) await Task.Delay(350, ct);
                var results = await GetOnlineScoreClient().SearchAsync(text, _onlineScoreMode, ct);
                if (!IsCurrentOnlineScoreSearch(version)) return;
                OnlineScoreList.ItemsSource = results;
                OnlineScoreStatus.Text = $"找到 {results.Count} 首";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { if (IsCurrentOnlineScoreSearch(version)) OnlineScoreStatus.Text = OnlineScoreError(ex); }
        }
        private bool IsCurrentOnlineScoreSearch(long version) => !_onlineScoreClosed && IsOnlineScoreMode && version == _onlineScoreSearchVersion;

        private async void OnlineScoreResult_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement row) await UpdateOnlineScoreLyricsHintAsync(row);
        }
        private async void OnlineScoreResult_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 回收虚拟行可能只换 DataContext，不再触发 Loaded。
            if (sender is FrameworkElement row && row.IsLoaded) await UpdateOnlineScoreLyricsHintAsync(row);
        }
        private async Task UpdateOnlineScoreLyricsHintAsync(FrameworkElement row)
        {
            if (!IsOnlineScoreMode || _onlineScoreClosed || _onlineScoreSearchCts == null ||
                row.DataContext is not OnlineScoreResult result || !_onlineScoreLyricsProbes.Add(result)) return;
            long version = _onlineScoreSearchVersion;
            CancellationToken ct = _onlineScoreSearchCts.Token;
            try
            {
                bool available = await GetOnlineScoreClient().GetLyricsAvailabilityAsync(result.Id, ct);
                if (!ct.IsCancellationRequested && IsCurrentOnlineScoreSearch(version)) result.SetLyricsAvailability(available);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception) { /* 可选提示失败不影响搜索/选中/画面，也不误标成无歌词。 */ }
        }
        private static string OnlineScoreError(Exception ex) => ex is ScoreIntegrationException ? ex.Message :
            ex is HttpRequestException || ex is OperationCanceledException ? "网络连接或请求超时；主屏幕保持当前画面，请稍后重试。" :
            "歌谱加载失败；主屏幕保持当前画面，请检查接入配置或图片格式。";

        private void OnlineScoreList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 选中只更新高亮/取消过时动作。键盘导航、数据绑定、程序选中都不能触发下载。
            ++_onlineScoreSelectionVersion;
            _onlineScoreSelectionCts?.Cancel(); _onlineScoreSelectionCts?.Dispose();
            _onlineScoreSelectionCts = null;
        }

        private ListBoxItem GetOnlineScoreItemAt(object source)
        {
            if (source is not DependencyObject element) return null;
            var item = ItemsControl.ContainerFromElement(OnlineScoreList, element) as ListBoxItem;
            return item != null && ItemsControl.ItemsControlFromItemContainer(item) == OnlineScoreList ? item : null;
        }

        private void OnlineScoreList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 必须在同一条结果上按下并松开，不将滚动条、空白区或拖动误当下载。
            _pressedOnlineScore = GetOnlineScoreItemAt(e.OriginalSource)?.Content as OnlineScoreResult;
        }

        private async void OnlineScoreList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var item = GetOnlineScoreItemAt(e.OriginalSource);
            var result = item?.Content as OnlineScoreResult;
            bool clicked = result != null && ReferenceEquals(result, _pressedOnlineScore);
            _pressedOnlineScore = null;
            if (!clicked || !IsOnlineScoreMode || _onlineScoreClosed) return;
            // 与本地文件点击一致：单选整行，焦点离开仍保留非激活黄色高亮。
            OnlineScoreList.SelectedItem = result;
            item.Focus();
            await OpenClickedOnlineScoreAsync(result);
        }

        private async Task OpenClickedOnlineScoreAsync(OnlineScoreResult result)
        {
            _onlineScoreSelectionCts?.Cancel(); _onlineScoreSelectionCts?.Dispose();
            var cts = new CancellationTokenSource(); _onlineScoreSelectionCts = cts; CancellationToken ct = cts.Token;
            long version = ++_onlineScoreSelectionVersion;
            long searchVersion = _onlineScoreSearchVersion;
            bool IsCurrent() => !ct.IsCancellationRequested && IsCurrentOnlineScoreSearch(searchVersion) && version == _onlineScoreSelectionVersion;
            OnlineScoreStatus.Text = $"正在保存到 YM：{result.Name}…";
            OnlineScoreLyricsPreview.Inlines.Clear();
            try
            {
                var library = GetOnlineScoreLibrary();
                string path = await library.GetOrDownloadAsync(result,
                    (directory, token) => GetOnlineScoreClient().DownloadScoreAsync(result.Id, directory, token), ct);
                if (!IsCurrent()) return;
                var image = library.RegisterImage(path);
                string lyricsStatus = "";
                if (!library.HasLyrics(image.Id))
                {
                    try
                    {
                        string lyrics = await GetOnlineScoreClient().GetFullLyricsAsync(result.Id, ct);
                        if (!IsCurrent()) return;
                        result.SetLyricsAvailability(!string.IsNullOrWhiteSpace(lyrics));
                        if (library.StoreLyricsIfMissing(image.Id, lyrics)) lyricsStatus = "；完整歌词已关联，可使用图片－歌词功能";
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    { lyricsStatus = "；歌词暂未获取，下次选择可补取"; }
                }
                else result.SetLyricsAvailability(true);
                if (!IsCurrent()) return;
                // 使用真实本地 ID 和文件夹归属，不再维护在线预览/缩放/播放/投影分支。
                ReloadProjectsPreservingTreeState(TreeItemType.File, image.Id);
                LoadSearchScopes();
                _imageProcessor?.ClearImageCache();
                var item = new ProjectTreeItem { Id = image.Id, Name = image.Name, Path = image.Path,
                    Type = TreeItemType.File, FileType = image.FileType, RootFolderId = image.FolderId };
                await HandleFileNodeClickAsync(item);
                if (IsCurrent()) OnlineScoreStatus.Text = $"已保存到 YM：{result.Name}；已交给本地图片入口打开" + lyricsStatus;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { if (IsCurrent()) OnlineScoreStatus.Text = OnlineScoreError(ex); }
        }

        private void DisposeOnlineScores()
        {
            _onlineScoreClosed = true;
            CancelOnlineScoreRequests();
            _onlineScoreClient?.Dispose(); _onlineScoreClient = null;
            // YM 属于用户的本地歌谱库，关闭窗口不删除文件或图片歌词。
        }
    }
}
