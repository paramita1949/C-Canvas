using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的项目树搜索功能
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 搜索功能
        private const string ImageSearchFilterSelectedSettingKey = "ProjectSearchImageFilterSelected";
        private const string MediaSearchFilterSelectedSettingKey = "ProjectSearchMediaFilterSelected";
        private bool _imageSearchFilterSelected = true;
        private bool _mediaSearchFilterSelected;
        private bool _mediaSearchFilterSelectionLoaded;
        private bool _projectSearchNumericActive;
        private string _projectSearchTerm;
        private int? _projectSearchFolderId;

        private void PublishProjectSearchResults(IEnumerable<ProjectTreeItem> results, string term, bool preserveFolder = false)
        {
            bool keepFolder = preserveFolder && _projectSearchTerm == term;
            _projectSearchTerm = term;
            _projectSearchNumericActive = SearchManager.IsNumericFolderSearch(term);
            if (!keepFolder || !_projectSearchNumericActive) _projectSearchFolderId = null;
            _projectTreeItems.Clear();
            foreach (var item in results) _projectTreeItems.Add(item);
            BuildProjectSearchFolderTags();
            ApplyProjectSearchFolderFilter();
        }

        private void ApplyProjectSearchFolderFilter()
        {
            // ObservableCollection 的 Reset/Add 会访问视图当前位置，不能放入 DeferRefresh。
            _filteredProjectTreeItems.Clear();
            foreach (var item in _projectTreeItems)
                if (!_projectSearchNumericActive || !_projectSearchFolderId.HasValue ||
                    item.Type == TreeItemType.File && !item.IsOnlineScoreSearchResult &&
                    (item.RootFolderId ?? 0) == _projectSearchFolderId.Value)
                    _filteredProjectTreeItems.Add(item);
            RefreshOnlineLibraryRoots();
        }

        private void SelectProjectSearchFolder(int? folderId)
        {
            _projectSearchFolderId = folderId;
            foreach (ToggleButton chip in ProjectSearchFolderTagsWrap.Children)
                chip.IsChecked = Equals(chip.Tag, folderId);
            // 只过滤已经取得的结果，不重新查询数据库、联网或打开歌谱。
            ApplyProjectSearchFolderFilter();
        }

        private void BuildProjectSearchFolderTags()
        {
            if (ProjectSearchFolderTagsWrap == null) return;
            ProjectSearchFolderTagsWrap.Children.Clear();
            if (!_projectSearchNumericActive) { UpdateProjectSearchFolderTagsVisibility(); return; }
            var groups = _projectTreeItems.Where(i => i.Type == TreeItemType.File && !i.IsOnlineScoreSearchResult)
                .GroupBy(i => i.RootFolderId ?? 0).ToArray();
            if (_projectSearchFolderId.HasValue && !groups.Any(g => g.Key == _projectSearchFolderId.Value))
                _projectSearchFolderId = null;
            var style = (Style)ProjectSearchFolderTagsPanel.FindResource("ProjectSearchFolderChip");
            void AddChip(int? id, string label, int count, string tooltip)
            {
                var chip = new ToggleButton { Style = style, Tag = id, IsChecked = id == _projectSearchFolderId,
                    Content = new TextBlock { Text = $"{label} {count}", TextTrimming = TextTrimming.CharacterEllipsis },
                    ToolTip = tooltip };
                System.Windows.Automation.AutomationProperties.SetName(chip, $"{label}，{count}项");
                chip.Click += (_, _) => SelectProjectSearchFolder(id);
                ProjectSearchFolderTagsWrap.Children.Add(chip);
            }
            if (groups.Length > 0)
            {
                AddChip(null, "全部", _projectTreeItems.Count, "全部本地搜索结果");
                foreach (var group in groups)
                {
                    var first = group.First();
                    string label = string.IsNullOrWhiteSpace(first.SearchFolderName) ? "根目录" : first.SearchFolderName;
                    string path = Path.GetDirectoryName(first.Path ?? "");
                    AddChip(group.Key, label, group.Count(), string.IsNullOrEmpty(path) ? label : label + "\n" + path);
                }
            }
            ProjectSearchFolderTagsScroll.MaxHeight = 64;
            ProjectSearchFolderTagsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ProjectSearchFolderTagsMore.IsChecked = false;
            ProjectSearchFolderTagsMore.Content = "更多";
            ProjectSearchFolderTagsMore.Click -= ProjectSearchFolderTagsMore_Click;
            ProjectSearchFolderTagsMore.Click += ProjectSearchFolderTagsMore_Click;
            ProjectSearchFolderTagsScroll.SizeChanged -= ProjectSearchFolderTags_SizeChanged;
            ProjectSearchFolderTagsScroll.SizeChanged += ProjectSearchFolderTags_SizeChanged;
            UpdateProjectSearchFolderTagsVisibility();
        }

        private void ProjectSearchFolderTagsMore_Click(object sender, RoutedEventArgs e)
        {
            bool expanded = ProjectSearchFolderTagsMore.IsChecked == true;
            ProjectSearchFolderTagsScroll.MaxHeight = expanded ? 180 : 64;
            ProjectSearchFolderTagsScroll.VerticalScrollBarVisibility = expanded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            ProjectSearchFolderTagsMore.Content = expanded ? "收起" : "更多";
        }

        private void ProjectSearchFolderTags_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double width = ProjectSearchFolderTagsScroll.ActualWidth;
            if (width <= 0) return;
            ProjectSearchFolderTagsWrap.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            ProjectSearchFolderTagsMore.Visibility = ProjectSearchFolderTagsWrap.DesiredSize.Height > 64
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateProjectSearchFolderTagsVisibility()
        {
            if (ProjectSearchFolderTagsPanel == null) return;
            ProjectSearchFolderTagsPanel.Visibility = _projectSearchNumericActive && !_isBibleMode &&
                _currentViewMode == NavigationViewMode.Files && !IsOnlineScoreMode &&
                SearchBox?.Text?.Trim() == _projectSearchTerm && ProjectSearchFolderTagsWrap.Children.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResetProjectSearchFolderTags()
        {
            _projectSearchNumericActive = false; _projectSearchTerm = null; _projectSearchFolderId = null;
            ProjectSearchFolderTagsWrap?.Children.Clear();
            if (ProjectSearchFolderTagsPanel != null) ProjectSearchFolderTagsPanel.Visibility = Visibility.Collapsed;
        }

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string searchTerm = SearchBox.Text?.Trim() ?? "";
                UpdateSearchClearButtonVisibility(searchTerm);

                if (_isBibleMode)
                {
                    ResetProjectSearchFolderTags();
                    await HandleBibleSearchInputChangedAsync(searchTerm);
                    return;
                }

                HideBibleSearchResults();
                if (IsOnlineScoreMode)
                {
                    ResetProjectSearchFolderTags();
                    await SearchOnlineScoresAsync(searchTerm);
                    return;
                }

                var searchManager = SearchManagerService;
                if (searchManager == null) return;

                string searchScope = (SearchScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";

                // System.Diagnostics.Debug.WriteLine($"搜索: 关键词='{searchTerm}', 范围='{searchScope}'");

                // 如果搜索词为空，重新加载所有项目
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    LoadProjects();
                    return;
                }

                // 执行搜索
                var searchResults = searchManager.SearchProjects(searchTerm, searchScope, GetMediaSearchFilterMode());
                
                // System.Diagnostics.Debug.WriteLine($"搜索结果: {searchResults?.Count ?? 0} 项");

                if (searchResults == null)
                {
                    LoadProjects();
                    return;
                }

                PublishProjectSearchResults(searchResults, searchTerm);

                // 不需要重新设置ItemsSource，ObservableCollection会自动通知UI更新
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($" 搜索失败: {ex}");
                MessageBox.Show($"搜索失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 双击搜索框清空内容
        /// </summary>
        private void SearchBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
            UpdateSearchClearButtonVisibility(string.Empty);

            if (_isBibleMode)
            {
                HideBibleSearchResults();
                ShowStatus("已清除经文搜索");
                return;
            }

            CollapseAllFolders();
            ShowStatus("已清除搜索并折叠所有文件夹");
        }

        private void SearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
            UpdateSearchClearButtonVisibility(string.Empty);

            if (_isBibleMode)
            {
                HideBibleSearchResults();
                ShowStatus("已清除经文搜索");
                return;
            }

            CollapseAllFolders();
            ShowStatus("已清除搜索并折叠所有文件夹");
        }

        private void UpdateSearchClearButtonVisibility(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) ResetProjectSearchFolderTags();
            if (SearchClearButton == null)
            {
                return;
            }

            SearchClearButton.Visibility = string.IsNullOrWhiteSpace(searchTerm)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        internal MediaSearchFilterMode GetMediaSearchFilterMode()
        {
            EnsureMediaSearchFilterSelectionLoaded();

            if (_imageSearchFilterSelected && !_mediaSearchFilterSelected)
            {
                return MediaSearchFilterMode.ImageOnly;
            }

            if (!_imageSearchFilterSelected && _mediaSearchFilterSelected)
            {
                return MediaSearchFilterMode.MediaOnly;
            }

            // 两者都选中 or 两者都未选中 => 全部
            return MediaSearchFilterMode.All;
        }

        private void ImageSearchFilterText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isBibleMode)
            {
                return;
            }

            if (IsOnlineScoreMode)
            {
                LeaveOnlineScoreMode();
                ApplyMediaSearchFilterSelection(true, _mediaSearchFilterSelected, persist: true);
                UpdateSearchEntryModeVisual();
                SearchBox_TextChanged(SearchBox, null);
                return;
            }
            ApplyMediaSearchFilterSelection(!_imageSearchFilterSelected, _mediaSearchFilterSelected, persist: true);
        }

        private void MediaSearchFilterText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isBibleMode)
            {
                return;
            }

            if (IsOnlineScoreMode)
            {
                LeaveOnlineScoreMode();
                ApplyMediaSearchFilterSelection(_imageSearchFilterSelected, true, persist: true);
                UpdateSearchEntryModeVisual();
                SearchBox_TextChanged(SearchBox, null);
                return;
            }
            ApplyMediaSearchFilterSelection(_imageSearchFilterSelected, !_mediaSearchFilterSelected, persist: true);
        }

        private void UpdateIncludeMediaSearchToggleVisual()
        {
            if (ImageSearchFilterText == null || MediaSearchFilterText == null)
            {
                return;
            }

            EnsureMediaSearchFilterSelectionLoaded();
            // 本地筛选与联网搜索方式是两层状态；联网时仍显示已保存的本地组合。
            ApplyMediaSearchFilterTextStyle(ImageSearchFilterText, _imageSearchFilterSelected);
            ApplyMediaSearchFilterTextStyle(MediaSearchFilterText, _mediaSearchFilterSelected);
        }

        private static void ApplyMediaSearchFilterTextStyle(TextBlock textBlock, bool isActive)
        {
            if (textBlock == null)
            {
                return;
            }

            textBlock.Foreground = isActive
                ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1976D2"))
                : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#757575"));
            textBlock.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            textBlock.Background = System.Windows.Media.Brushes.Transparent;
            textBlock.TextDecorations = null;
        }

        private void EnsureMediaSearchFilterSelectionLoaded()
        {
            if (_mediaSearchFilterSelectionLoaded)
            {
                return;
            }

            _mediaSearchFilterSelectionLoaded = true;
            try
            {
                string savedImageValue = DatabaseManagerService.GetUISetting(ImageSearchFilterSelectedSettingKey, bool.TrueString);
                string savedMediaValue = DatabaseManagerService.GetUISetting(MediaSearchFilterSelectedSettingKey, bool.FalseString);

                _imageSearchFilterSelected = ParseBoolOrDefault(savedImageValue, defaultValue: true);
                _mediaSearchFilterSelected = ParseBoolOrDefault(savedMediaValue, defaultValue: false);
            }
            catch
            {
                _imageSearchFilterSelected = true;
                _mediaSearchFilterSelected = false;
            }
        }

        private void ApplyMediaSearchFilterSelection(bool imageSelected, bool mediaSelected, bool persist)
        {
            EnsureMediaSearchFilterSelectionLoaded();

            bool changed = _imageSearchFilterSelected != imageSelected ||
                           _mediaSearchFilterSelected != mediaSelected;
            _imageSearchFilterSelected = imageSelected;
            _mediaSearchFilterSelected = mediaSelected;
            UpdateIncludeMediaSearchToggleVisual();

            if (persist)
            {
                try
                {
                    DatabaseManagerService.SaveUISetting(ImageSearchFilterSelectedSettingKey, _imageSearchFilterSelected.ToString());
                    DatabaseManagerService.SaveUISetting(MediaSearchFilterSelectedSettingKey, _mediaSearchFilterSelected.ToString());
                }
                catch
                {
                }
            }

            if (changed)
            {
                // 复用现有搜索流程：触发当前关键词重新搜索
                SearchBox_TextChanged(SearchBox, null);
            }
        }

        private static bool ParseBoolOrDefault(string value, bool defaultValue)
        {
            return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
        }

        /// <summary>
        /// 加载搜索范围选项
        /// </summary>
        private void LoadSearchScopes()
        {
            try
            {
                var searchManager = SearchManagerService;
                if (searchManager == null) return;

                var scopes = searchManager.GetSearchScopes();
                SearchScope.Items.Clear();
                
                foreach (var scope in scopes)
                {
                    var item = new ComboBoxItem { Content = scope };
                    SearchScope.Items.Add(item);
                }

                // 默认选中"全部"
                if (SearchScope.Items.Count > 0)
                {
                    SearchScope.SelectedIndex = 0;
                }

                EnsureMediaSearchFilterSelectionLoaded();
                UpdateSearchEntryModeVisual();
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"加载搜索范围失败: {ex}");
            }
        }

        #endregion
    }
}



