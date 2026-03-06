using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string searchTerm = SearchBox.Text?.Trim() ?? "";
                UpdateSearchClearButtonVisibility(searchTerm);

                if (_isBibleMode)
                {
                    await HandleBibleSearchInputChangedAsync(searchTerm);
                    return;
                }

                HideBibleSearchResults();

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

                // 修复：搜索结果需要同时更新 _projectTreeItems 和 _filteredProjectTreeItems
                _projectTreeItems.Clear();
                _filteredProjectTreeItems.Clear();
                
                foreach (var item in searchResults)
                {
                    _projectTreeItems.Add(item);
                    _filteredProjectTreeItems.Add(item); // 关键：搜索结果直接显示，不需要过滤
                }

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

            ApplyMediaSearchFilterSelection(!_imageSearchFilterSelected, _mediaSearchFilterSelected, persist: true);
        }

        private void MediaSearchFilterText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isBibleMode)
            {
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



