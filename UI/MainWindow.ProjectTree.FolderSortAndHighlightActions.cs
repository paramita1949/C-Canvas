using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Database.Models;

using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：文件夹排序与高亮色
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 设置文件夹高亮颜色
        /// </summary>
        private void SetFolderHighlightColor(ProjectTreeItem item)
        {
            try
            {
                var dbManager = DatabaseManagerService;
                var colorDialog = new System.Windows.Forms.ColorDialog
                {
                    FullOpen = true,
                    AnyColor = true
                };

                string existingColor = dbManager.GetFolderHighlightColor(item.Id);
                if (!string.IsNullOrEmpty(existingColor))
                {
                    try
                    {
                        var wpfColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(existingColor);
                        colorDialog.Color = System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
                    }
                    catch
                    {
                    }
                }

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var selectedColor = colorDialog.Color;
                    string colorHex = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";

                    // [Debug][FolderHighlight] Set-Begin log disabled after verification.
                    dbManager.SetFolderHighlightColor(item.Id, colorHex);

                    // [Debug][FolderHighlight] Set-Saved log disabled after verification.
                    ShowStatus($"已设置文件夹 [{item.Name}] 的高亮颜色: {colorHex}");

                    ReloadProjectsPreservingTreeState();
                    RefreshSearchResultsIfNeeded();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"设置高亮颜色失败: {ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ClearFolderHighlightColor(ProjectTreeItem item)
        {
            try
            {
                var dbManager = DatabaseManagerService;
                string existingColor = dbManager.GetFolderHighlightColor(item.Id);
                // [Debug][FolderHighlight] Clear-Begin log disabled after verification.
                if (string.IsNullOrWhiteSpace(existingColor))
                {
                    ShowStatus($"文件夹 [{item.Name}] 当前没有高亮色");
                    return;
                }

                dbManager.SetFolderHighlightColor(item.Id, null);
                // [Debug][FolderHighlight] Clear-Saved log disabled after verification.
                ShowStatus($"已取消文件夹 [{item.Name}] 的高亮颜色");

                ReloadProjectsPreservingTreeState();
                RefreshSearchResultsIfNeeded();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"取消高亮颜色失败: {ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RefreshSearchResultsIfNeeded()
        {
            string searchTerm = SearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return;
            }

            string searchScope = (SearchScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";
            var searchResults = SearchManagerService.SearchProjects(searchTerm, searchScope, GetMediaSearchFilterMode());
            if (searchResults == null)
            {
                return;
            }

            _projectTreeItems.Clear();
            foreach (var result in searchResults)
            {
                _projectTreeItems.Add(result);
            }
        }

        /// <summary>
        /// 重置文件夹排序（取消手动排序，恢复自动排序）
        /// </summary>
        private void ResetFolderSort(ProjectTreeItem item)
        {
            var result = MessageBox.Show(
                $"确定要重置文件夹 '{item.Name}' 的排序吗？\n将按照文件名自动排序。",
                "确认重置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var dbManager = DatabaseManagerService;
                dbManager.UnmarkFolderAsManualSort(item.Id);

                var files = dbManager.GetMediaFilesByFolder(item.Id);
                if (files.Count > 0)
                {
                    var sortedFiles = files
                        .Select(f => new
                        {
                            File = f,
                            SortKey = SortManagerService.GetSortKey(System.IO.Path.GetFileName(f.Path))
                        })
                        .OrderBy(x => x.SortKey.prefixNumber)
                        .ThenBy(x => x.SortKey.pinyinPart)
                        .ThenBy(x => x.SortKey.suffixNumber)
                        .Select(x => x.File)
                        .ToList();

                    for (int i = 0; i < sortedFiles.Count; i++)
                    {
                        sortedFiles[i].OrderIndex = i + 1;
                    }

                    for (int i = 0; i < sortedFiles.Count; i++)
                    {
                        string originalFileName = System.IO.Path.GetFileNameWithoutExtension(sortedFiles[i].Path);
                        sortedFiles[i].Name = originalFileName;
                    }

                    dbManager.UpdateMediaFilesOrder(sortedFiles);
                }

                ReloadProjectsPreservingTreeState();
                ShowStatus($"已重置文件夹排序: {item.Name}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重置排序失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}


