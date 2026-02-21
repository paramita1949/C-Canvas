using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImageColorChanger.Database.Models;
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

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                string searchTerm = SearchBox.Text?.Trim() ?? "";

                if (_isBibleMode)
                {
                    await HandleBibleSearchInputChangedAsync(searchTerm);
                    return;
                }

                HideBibleSearchResults();

                var searchManager = SearchManagerService;
                if (searchManager == null) return;

                string searchScope = (SearchScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";

                // System.Diagnostics.Debug.WriteLine($"🔍 搜索: 关键词='{searchTerm}', 范围='{searchScope}'");

                // 如果搜索词为空，重新加载所有项目
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    LoadProjects();
                    return;
                }

                // 执行搜索
                var searchResults = searchManager.SearchProjects(searchTerm, searchScope);
                
                // System.Diagnostics.Debug.WriteLine($"📊 搜索结果: {searchResults?.Count ?? 0} 项");

                if (searchResults == null)
                {
                    LoadProjects();
                    return;
                }

                // 🔧 修复：搜索结果需要同时更新 _projectTreeItems 和 _filteredProjectTreeItems
                _projectTreeItems.Clear();
                _filteredProjectTreeItems.Clear();
                
                foreach (var item in searchResults)
                {
                    _projectTreeItems.Add(item);
                    _filteredProjectTreeItems.Add(item); // 🔑 关键：搜索结果直接显示，不需要过滤
                }

                // 不需要重新设置ItemsSource，ObservableCollection会自动通知UI更新
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 搜索失败: {ex}");
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

            if (_isBibleMode)
            {
                HideBibleSearchResults();
                ShowStatus("✅ 已清除经文搜索");
                return;
            }

            CollapseAllFolders();
            ShowStatus("✅ 已清除搜索并折叠所有文件夹");
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
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"加载搜索范围失败: {ex}");
            }
        }

        #endregion
    }
}
