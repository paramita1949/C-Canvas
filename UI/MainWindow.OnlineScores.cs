using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Services.OnlineScores;
using Microsoft.EntityFrameworkCore;
using Brushes = System.Windows.Media.Brushes;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private OnlineScoreSearchMode _onlineScoreMode;
        private string _onlineScoreLocalSearchText;
        private ShigebenClient _onlineScoreClient;
        private CancellationTokenSource _onlineScoreSearchCts;
        private CancellationTokenSource _onlineScoreSelectionCts;
        private CancellationTokenSource _onlineScoreLyricsCts;
        private long _onlineScoreSearchVersion, _onlineScoreSelectionVersion;
        private bool _onlineScoreClosed;
        private OnlineScoreResult _pressedOnlineScore;
        private OnlineScoreLibrary _onlineScoreLibrary;
        private readonly HashSet<OnlineScoreResult> _onlineScoreLyricsProbes = new();
        private readonly HashSet<OnlineScoreResult> _onlineScoreKindProbes = new();
        private readonly HashSet<string> _onlineScoreKindFailures = new(StringComparer.Ordinal);
        private long _onlineScoreKindEnrichmentVersion = -1;
        private OnlineScoreKindFilter _onlineScoreKind = OnlineScoreKindFilter.All;
        private ICollectionView _onlineScoreResultView;
        private IReadOnlyList<OnlineScoreResult> _onlineScoreResults = Array.Empty<OnlineScoreResult>();
        private Dictionary<int, string> _onlineScoreLocalPaths = new();
        private OnlineScoreStorageSettings _onlineScoreStorageSettings;
        private OnlineScoreStorageSettings GetOnlineScoreStorageSettings() =>
            _onlineScoreStorageSettings ??= new OnlineScoreStorageSettings(DatabaseManagerService);
        private OnlineScoreLibrary GetOnlineScoreLibrary(OnlineScoreResult result)
        {
            var settings = GetOnlineScoreStorageSettings();
            string path = settings.GetBookDirectory(result.BookName);
            // 每次明确点击取最新目录映射，避免新歌本加入后旧实例漏掉可复用缓存。
            _onlineScoreLibrary = new OnlineScoreLibrary(DatabaseManagerService, path, settings.CacheDirectories);
            return _onlineScoreLibrary;
        }
        private bool IsOnlineScoreMode => !_isBibleMode && _currentViewMode == NavigationViewMode.Files && _onlineScoreMode != OnlineScoreSearchMode.Local;

        private void ScoreNameSearchText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => EnterOnlineScoreMode(OnlineScoreSearchMode.Name);
        private void ScoreLyricsSearchText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => EnterOnlineScoreMode(OnlineScoreSearchMode.Lyrics);
        private void EnterOnlineScoreMode(OnlineScoreSearchMode mode)
        {
            if (_isBibleMode || _onlineScoreClosed) return;
            if (_currentViewMode != NavigationViewMode.Files) BtnShowFiles_Click(null, null);
            if (IsOnlineScoreMode && _onlineScoreMode == mode)
            {
                // 再次点击当前联网方式即取消，恢复进入联网前的本地查询和筛选。
                LeaveOnlineScoreMode();
                UpdateSearchEntryModeVisual();
                string localText = _onlineScoreLocalSearchText ?? "";
                if (SearchBox.Text != localText) SearchBox.Text = localText;
                else SearchBox_TextChanged(SearchBox, null);
                SearchBox.Focus();
                return;
            }
            if (!IsOnlineScoreMode) _onlineScoreLocalSearchText = SearchBox.Text;
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
            CancelOnlineScoreLyrics();
            StopOnlineScoreDownloadFeedback();
            SetOnlineScoreSearchBusy(false);
            _pressedOnlineScore = null;
            _onlineScoreLyricsProbes.Clear();
            _onlineScoreKindProbes.Clear();
            _onlineScoreKindFailures.Clear();
            _onlineScoreResults = Array.Empty<OnlineScoreResult>();
            _onlineScoreLocalPaths = new();
            _onlineScoreResultView = null;
            ++_onlineScoreSearchVersion; ++_onlineScoreSelectionVersion;
            _onlineScoreSearchCts?.Cancel(); _onlineScoreSearchCts?.Dispose(); _onlineScoreSearchCts = null;
            _onlineScoreSelectionCts?.Cancel(); _onlineScoreSelectionCts?.Dispose(); _onlineScoreSelectionCts = null;
        }
        private void UpdateOnlineScoreEntryVisual()
        {
            if (_isBibleMode || _currentViewMode != NavigationViewMode.Files) LeaveOnlineScoreMode();
            UpdateOnlineLibraryPane();
            RefreshOnlineScoreNetworkAnimation();
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
            _onlineScoreKind = OnlineScoreKindFilter.All;
            UpdateOnlineScoreKindVisual();
            OnlineScoreStatus.Text = string.IsNullOrWhiteSpace(text) ? "使用上方搜索框输入歌名或歌词。" : "正在搜索诗歌本…";
            UpdateOnlineScoreEntryVisual();
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                if (!immediate) await Task.Delay(350, ct);
                if (!IsCurrentOnlineScoreSearch(version)) return;
                SetOnlineScoreSearchBusy(true);
                var results = await GetOnlineScoreClient().SearchAsync(text, _onlineScoreMode, ct);
                if (!IsCurrentOnlineScoreSearch(version)) return;
                var settings = GetOnlineScoreStorageSettings();
                var local = await new OnlineScoreLibrary(DatabaseManagerService, settings.DirectoryPath, settings.CacheDirectories)
                    .GetLocalScorePathsAsync(results.Select(score => score.Id), ct);
                if (!IsCurrentOnlineScoreSearch(version)) return;
                _onlineScoreLocalPaths = local;
                PublishOnlineScoreResults(results);
                UpdateOnlineScoreResultCount();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { if (IsCurrentOnlineScoreSearch(version)) OnlineScoreStatus.Text = OnlineScoreError(ex); }
            finally
            {
                // 旧请求结束不能停止新请求的动画；切模式/关闭由统一取消入口停止。
                if (IsCurrentOnlineScoreSearch(version)) SetOnlineScoreSearchBusy(false);
            }
        }
        private bool IsCurrentOnlineScoreSearch(long version) => !_onlineScoreClosed && IsOnlineScoreMode && version == _onlineScoreSearchVersion;

        private static IReadOnlyList<OnlineScoreResult> OrderOnlineScoreResults(IEnumerable<OnlineScoreResult> results, IReadOnlyDictionary<int, string> localPaths) =>
            results.OrderByDescending(score => localPaths.ContainsKey(score.Id)).ToArray(); // Stable sort retains upstream relevance within each group.

        private void PublishOnlineScoreResults(IEnumerable<OnlineScoreResult> results)
        {
            var selected = OnlineScoreList.SelectedItem;
            _onlineScoreResults = OrderOnlineScoreResults(results, _onlineScoreLocalPaths);
            _onlineScoreResultView = new ListCollectionView(_onlineScoreResults.ToList());
            _onlineScoreResultView.Filter = item => item is OnlineScoreResult score && score.MatchesKind(_onlineScoreKind);
            OnlineScoreList.ItemsSource = _onlineScoreResultView;
            if (selected != null && _onlineScoreResultView.Contains(selected)) OnlineScoreList.SelectedItem = selected;
        }

        private void ScoreKindAllText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ApplyOnlineScoreKind(OnlineScoreKindFilter.All);
        private void ScoreKindStructuredText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ApplyOnlineScoreKind(OnlineScoreKindFilter.Structured);
        private void ScoreKindImageText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ApplyOnlineScoreKind(OnlineScoreKindFilter.OriginalImage);
        private async void ApplyOnlineScoreKind(OnlineScoreKindFilter kind)
        {
            if (!IsOnlineScoreMode || _onlineScoreClosed) return;
            StopOnlineScoreDownloadFeedback();
            _onlineScoreKind = kind;
            _pressedOnlineScore = null; // 筛选不是结果点击，不能沿用按下的旧行触发下载。
            // 用户切换结果范围后，旧点击不能在稍后完成并打开另一范围的图片。
            ++_onlineScoreSelectionVersion;
            _onlineScoreSelectionCts?.Cancel(); _onlineScoreSelectionCts?.Dispose();
            _onlineScoreSelectionCts = null;
            _onlineScoreResultView?.Refresh();
            UpdateOnlineScoreKindVisual();
            UpdateOnlineScoreResultCount();
            // 默认仅识别可见行。用户明确筛选时才补全本次结果的类型，不扫描所有歌本。
            if (kind == OnlineScoreKindFilter.All || _onlineScoreSearchCts == null || _onlineScoreResults.Count == 0 ||
                _onlineScoreKindEnrichmentVersion == _onlineScoreSearchVersion) return;
            _onlineScoreKindEnrichmentVersion = _onlineScoreSearchVersion;
            var results = _onlineScoreResults;
            var ct = _onlineScoreSearchCts.Token;
            long version = _onlineScoreSearchVersion;
            try
            {
                foreach (var score in results)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!IsCurrentOnlineScoreSearch(version)) return;
                    await UpdateOnlineScoreKindHintAsync(score);
                    await Task.Delay(75, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }
        private void UpdateOnlineScoreKindVisual()
        {
            ApplyMediaSearchFilterTextStyle(ScoreKindAllText, _onlineScoreKind == OnlineScoreKindFilter.All);
            ApplyMediaSearchFilterTextStyle(ScoreKindStructuredText, _onlineScoreKind == OnlineScoreKindFilter.Structured);
            ApplyMediaSearchFilterTextStyle(ScoreKindImageText, _onlineScoreKind == OnlineScoreKindFilter.OriginalImage);
        }
        private void UpdateOnlineScoreResultCount()
        {
            // 元数据识别不能覆盖用户点击后的保存进度、错误或成功消息。
            if (_onlineScoreSelectionCts == null && !string.IsNullOrWhiteSpace(SearchBox.Text))
                OnlineScoreStatus.Text = $"找到 {_onlineScoreResults.Count(score => score.MatchesKind(_onlineScoreKind))} 首";
        }
        private async Task UpdateOnlineScoreKindHintAsync(OnlineScoreResult score)
        {
            if (!IsOnlineScoreMode || _onlineScoreClosed || _onlineScoreSearchCts == null ||
                score == null || !_onlineScoreKindProbes.Add(score)) return;
            var ct = _onlineScoreSearchCts.Token;
            long version = _onlineScoreSearchVersion;
            string book = score.BookId.HasValue ? "id:" + score.BookId.Value : "name:" + score.BookName;
            if (_onlineScoreKindFailures.Contains(book)) return;
            try
            {
                var capabilities = await GetOnlineScoreClient().GetCapabilitiesAsync(score, ct);
                if (ct.IsCancellationRequested || !IsCurrentOnlineScoreSearch(version)) return;
                bool wasVisible = score.MatchesKind(_onlineScoreKind);
                score.SetCapabilities(capabilities);
                // “全部”及仍可见的选中行不刷新，避免元数据提示取消正在下载的歌曲。
                if (wasVisible != score.MatchesKind(_onlineScoreKind)) _onlineScoreResultView?.Refresh();
                UpdateOnlineScoreResultCount();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception) { if (IsCurrentOnlineScoreSearch(version)) _onlineScoreKindFailures.Add(book); }
        }

        private async void OnlineScoreResult_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement row)
            {
                await UpdateOnlineScoreLyricsHintAsync(row);
                if (row.IsLoaded && row.DataContext is OnlineScoreResult score) await UpdateOnlineScoreKindHintAsync(score);
            }
        }
        private async void OnlineScoreResult_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 回收虚拟行可能只换 DataContext，不再触发 Loaded。
            if (sender is FrameworkElement row && row.IsLoaded)
            {
                await UpdateOnlineScoreLyricsHintAsync(row);
                if (row.IsLoaded && row.DataContext is OnlineScoreResult score) await UpdateOnlineScoreKindHintAsync(score);
            }
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
            StopOnlineScoreDownloadFeedback();
            // 选中只更新高亮/取消过时动作。键盘导航、数据绑定、程序选中都不能触发下载。
            ++_onlineScoreSelectionVersion;
            _onlineScoreSelectionCts?.Cancel(); _onlineScoreSelectionCts?.Dispose();
            _onlineScoreSelectionCts = null;
            UpdateOnlineScoreResultCount();
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
            CancelOnlineScoreLyrics();
            _onlineScoreSelectionCts?.Cancel(); _onlineScoreSelectionCts?.Dispose();
            var cts = new CancellationTokenSource(); _onlineScoreSelectionCts = cts; CancellationToken ct = cts.Token;
            long version = ++_onlineScoreSelectionVersion;
            long searchVersion = _onlineScoreSearchVersion;
            bool IsCurrent() => !ct.IsCancellationRequested && IsCurrentOnlineScoreSearch(searchVersion) && version == _onlineScoreSelectionVersion;
            OnlineScoreLyricsPreview.Inlines.Clear();
            var progress = BeginOnlineScoreDownloadFeedback(result, IsCurrent);
            OnlineScoreStatus.Text = $"找到 {_onlineScoreResults.Count(score => score.MatchesKind(_onlineScoreKind))} 首";
            try
            {
                // 点击优先复用本地文件或直接取谱码；可选类型索引不再挡住打开，也不排在后台类型队列后面。
                var library = GetOnlineScoreLibrary(result);
                string path;
                if (_onlineScoreLocalPaths.TryGetValue(result.Id, out string localPath) && File.Exists(localPath) &&
                    (result.HasStructuredNotation != true || StructuredScoreDocument.ReadFromFile(localPath) != null)) path = localPath;
                else path = await library.GetOrDownloadAsync(result,
                        (directory, token) => GetOnlineScoreClient().DownloadScoreWithProgressAsync(result.Id, directory, token, progress), ct);
                if (!IsCurrent()) return;
                var image = DatabaseManagerService.GetDbContext().MediaFiles.AsNoTracking().SingleOrDefault(m => m.Path == path)
                    ?? library.RegisterImage(path);
                // 实际文件可证明已取得谱码；原图能力保持原有未知/已确认状态，不伪造另一类能力。
                if (StructuredScoreDocument.IsScoreFile(path))
                    result.SetCapabilities(new ScoreCapabilities(true, result.HasOriginalImage));
                // 使用真实本地 ID 和文件夹归属，不再维护在线预览/缩放/播放/投影分支。
                ReloadProjectsPreservingTreeState(TreeItemType.File, image.Id);
                LoadSearchScopes();
                _imageProcessor?.ClearImageCache();
                var item = new ProjectTreeItem { Id = image.Id, Name = image.Name, Path = image.Path,
                    Type = TreeItemType.File, FileType = image.FileType, RootFolderId = image.FolderId };
                SetOnlineScoreDownloadPhase("歌谱已保存，正在打开…");
                await HandleFileNodeClickAsync(item);
                if (IsCurrent())
                {
                    _onlineScoreLocalPaths[result.Id] = path;
                    PublishOnlineScoreResults(_onlineScoreResults);
                    OnlineScoreStatus.Text = $"找到 {_onlineScoreResults.Count(score => score.MatchesKind(_onlineScoreKind))} 首";
                    if (_imagePath == path && _currentImageId == image.Id)
                        _ = CompleteOnlineScoreLyricsAsync(result, library, image.Id, path, version, searchVersion);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { if (IsCurrent()) OnlineScoreStatus.Text = OnlineScoreError(ex); }
            finally
            {
                if (version == _onlineScoreSelectionVersion)
                {
                    StopOnlineScoreDownloadFeedback();
                    _onlineScoreSelectionCts = null;
                }
                cts.Dispose();
            }
        }

        private void CancelOnlineScoreLyrics() => _onlineScoreLyricsCts?.Cancel();

        private async Task CompleteOnlineScoreLyricsAsync(OnlineScoreResult result, OnlineScoreLibrary library,
            int imageId, string path, long selectionVersion, long searchVersion)
        {
            CancelOnlineScoreLyrics();
            var cancellation = new CancellationTokenSource(); _onlineScoreLyricsCts = cancellation;
            bool IsCurrent() => !cancellation.IsCancellationRequested && !_onlineScoreClosed &&
                IsCurrentOnlineScoreSearch(searchVersion) && selectionVersion == _onlineScoreSelectionVersion &&
                _currentImageId == imageId && _imagePath == path;
            bool IsEditing() => _isLyricsMode && _lyricsEntryKind == LyricsEntryKind.ImageBound && _lyricsEntryId == imageId;
            try
            {
                if (!IsCurrent() || IsEditing()) return;
                if (library.HasLyrics(imageId))
                {
                    new Services.Lyrics.ImageLyricsService(_dbContext).Load(imageId); // Backfill legacy database-only lyrics into the actual score file.
                    result.SetLyricsAvailability(true); return;
                }
                var score = StructuredScoreDocument.ReadFromFile(path);
                if (score?.Lyrics != null && !score.LyricsFromNotation)
                { result.SetLyricsAvailability(!string.IsNullOrWhiteSpace(score.Lyrics.Content)); return; }
                // 已取得的全文直接命中会话缓存；缺失时后台补取，不延长点击的下载/打开动画。
                string lyrics = await GetOnlineScoreClient().GetFullLyricsAsync(result.Id, cancellation.Token);
                if (!IsCurrent()) return;
                result.SetLyricsAvailability(!string.IsNullOrWhiteSpace(lyrics));
                // 等待期间用户可能打开编辑器或创建正文，必须重新检查；已有空正文同样不覆盖。
                if (!IsEditing())
                {
                    if (!string.IsNullOrWhiteSpace(lyrics) && score != null)
                        score.SaveLyrics(path, new Services.Lyrics.LyricsDocument { Content = lyrics });
                    library.StoreLyricsIfMissing(imageId, lyrics);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("在线歌谱后台歌词未补齐：" + ex.Message);
                if (IsCurrent()) OnlineScoreStatus.Text = "歌谱已打开；歌词暂未获取，下次点击可补取。";
            }
            finally
            {
                if (_onlineScoreLyricsCts == cancellation) _onlineScoreLyricsCts = null;
                cancellation.Dispose();
            }
        }

        private void DisposeOnlineScores()
        {
            _onlineScoreClosed = true;
            ++_structuredOpenVersion; ++_structuredSwitchVersion; ++_structuredScoreVersion;
            _structuredOpenCancellation?.Cancel(); _structuredWarmCancellation?.Cancel();
            _structuredViewCancellation?.Cancel(); CancelStructuredScoreImageRequest();
            CancelOnlineScoreRequests();
            _onlineScoreClient?.Dispose(); _onlineScoreClient = null;
            // YM 属于用户的本地歌谱库，关闭窗口不删除文件或图片歌词。
        }
    }
}
