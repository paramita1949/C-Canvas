using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Database.Models;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：歌词库菜单与动作
    /// </summary>
    public partial class MainWindow
    {
        private void BuildLyricsRootContextMenu(ContextMenu contextMenu)
        {
            if (!IsLyricsLibraryFeatureEnabled)
            {
                return;
            }

            var createGroupItem = new MenuItem { Header = "新建歌词库" };
            createGroupItem.Click += (s, args) => CreateLyricsLibrary();
            contextMenu.Items.Add(createGroupItem);

            if (IsLyricsTransferFeatureEnabled)
            {
                contextMenu.Items.Add(new Separator());

                var importItem = new MenuItem { Header = "导入歌词" };
                importItem.Click += async (s, args) => await ImportLyricsPackageAsync();
                contextMenu.Items.Add(importItem);

                var exportItem = new MenuItem { Header = "导出歌词" };
                exportItem.Click += async (s, args) => await ExportLyricsLibraryPackageAsync();
                contextMenu.Items.Add(exportItem);
            }
        }

        private void BuildLyricsGroupContextMenu(ContextMenu contextMenu, ProjectTreeItem groupItem)
        {
            if (!IsLyricsLibraryFeatureEnabled)
            {
                return;
            }

            var createSongItem = new MenuItem { Header = " 新建歌词" };
            createSongItem.Click += (s, args) => CreateLyricsSong(groupItem.Id);
            contextMenu.Items.Add(createSongItem);

            contextMenu.Items.Add(new Separator());

            var renameItem = new MenuItem { Header = "重命名" };
            renameItem.Click += (s, args) => RenameLyricsGroup(groupItem);
            contextMenu.Items.Add(renameItem);

            var highlightColorItem = new MenuItem { Header = "标记高亮色" };
            highlightColorItem.Click += (s, args) => SetLyricsGroupHighlightColor(groupItem);
            contextMenu.Items.Add(highlightColorItem);

            var watermarkMenu = new MenuItem { Header = "水印" };

            var clearWatermarkItem = new MenuItem { Header = "无水印" };
            clearWatermarkItem.Click += (s, args) => ApplyGroupWatermark(groupItem.Id, string.Empty, "无水印");
            watermarkMenu.Items.Add(clearWatermarkItem);

            var watermarks = EnumerateLyricsWatermarkFiles()
                .OrderBy(path => System.IO.Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (watermarks.Count > 0)
            {
                foreach (var path in watermarks)
                {
                    string relative = GetLyricsWatermarkRelativePath(path);
                    string displayName = System.IO.Path.GetFileNameWithoutExtension(path);
                    var item = new MenuItem
                    {
                        Header = string.IsNullOrWhiteSpace(displayName) ? System.IO.Path.GetFileName(path) : displayName
                    };
                    item.Click += (s, args) => ApplyGroupWatermark(groupItem.Id, relative, item.Header?.ToString() ?? "水印");
                    watermarkMenu.Items.Add(item);
                }
            }
            else
            {
                var emptyItem = new MenuItem { Header = "(暂无已导入水印)", IsEnabled = false };
                watermarkMenu.Items.Add(emptyItem);
            }

            contextMenu.Items.Add(watermarkMenu);

            if (IsLyricsTransferFeatureEnabled)
            {
                var exportItem = new MenuItem { Header = "导出歌单" };
                exportItem.Click += async (s, args) => await ExportLyricsGroupPackageAsync(groupItem.Id);
                contextMenu.Items.Add(exportItem);
            }

            contextMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = " 删除歌词库" };
            deleteItem.Click += (s, args) => DeleteLyricsGroup(groupItem.Id);
            contextMenu.Items.Add(deleteItem);
        }

        private void ApplyGroupWatermark(int groupId, string watermarkRelativePath, string watermarkName)
        {
            if (_dbContext == null)
            {
                return;
            }

            var songs = _dbContext.LyricsProjects.Where(p => p.GroupId == groupId).ToList();
            if (songs.Count == 0)
            {
                ShowStatus("当前歌词库没有可设置的歌曲");
                return;
            }

            string target = watermarkRelativePath ?? string.Empty;
            foreach (var song in songs)
            {
                song.ProjectionWatermarkPath = target;
                song.ModifiedTime = DateTime.Now;
            }

            _dbContext.SaveChanges();
            ShowStatus($"已为 {songs.Count} 首歌曲设置水印: {watermarkName}");

            if (_currentLyricsProject != null && _currentLyricsProject.GroupId == groupId)
            {
                _currentLyricsProject.ProjectionWatermarkPath = target;
                if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
                {
                    RenderLyricsToProjection();
                }
            }
        }

        private void BuildLyricsSongContextMenu(ContextMenu contextMenu, ProjectTreeItem songItem)
        {
            if (!IsLyricsLibraryFeatureEnabled)
            {
                return;
            }

            var openItem = new MenuItem { Header = "打开歌词" };
            openItem.Click += (s, args) => _ = EnterLyricsModeFromSongAsync(songItem.Id);
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new Separator());

            var renameItem = new MenuItem { Header = "✏ 重命名" };
            renameItem.Click += (s, args) => RenameLyricsSong(songItem);
            contextMenu.Items.Add(renameItem);

            if (IsLyricsTransferFeatureEnabled)
            {
                var exportItem = new MenuItem { Header = "导出歌词" };
                exportItem.Click += async (s, args) => await ExportLyricsSongPackageAsync(songItem.Id);
                contextMenu.Items.Add(exportItem);
            }

            contextMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = " 删除歌曲" };
            deleteItem.Click += (s, args) => DeleteLyricsSong(songItem.Id);
            contextMenu.Items.Add(deleteItem);
        }

        private void CreateLyricsLibrary()
        {
            CreateLyricsGroup();
        }

        private void CreateLyricsGroup()
        {
            if (_dbContext == null)
            {
                return;
            }

            string name = PromptTextDialog("新建歌词库", "请输入歌词库名称：", $"歌词库{DateTime.Now:HHmmss}");
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            int nextOrder = _dbContext.LyricsGroups.Any() ? _dbContext.LyricsGroups.Max(g => g.SortOrder) + 1 : 0;
            try
            {
                var manager = new Managers.LyricsGroupManager(_dbContext);
                manager.CreateGroup(name.Trim(), nextOrder, isSystem: false);
                LoadProjects();
                ShowStatus($"已创建歌词库: {name.Trim()}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[歌词库] 新建失败: {ex}");
                ShowStatus($"新建歌词库失败: {ex.Message}");
            }
        }

        private void SetLyricsGroupHighlightColor(ProjectTreeItem groupItem)
        {
            if (_dbContext == null || groupItem == null)
            {
                return;
            }

            try
            {
                var manager = new Managers.LyricsGroupManager(_dbContext);
                var colorDialog = new System.Windows.Forms.ColorDialog
                {
                    FullOpen = true,
                    AnyColor = true
                };

                string existingColor = manager.GetGroupHighlightColor(groupItem.Id);
                if (!string.IsNullOrWhiteSpace(existingColor))
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

                if (colorDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }

                string colorHex = $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
                manager.SetGroupHighlightColor(groupItem.Id, colorHex);
                ShowStatus($"已设置歌词库 [{groupItem.Name}] 的高亮颜色: {colorHex}");

                LoadProjects();

                string searchTerm = SearchBox.Text?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    string searchScope = (SearchScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";
                    var searchResults = SearchManagerService.SearchProjects(searchTerm, searchScope);
                    if (searchResults != null)
                    {
                        _projectTreeItems.Clear();
                        _filteredProjectTreeItems.Clear();
                        foreach (var result in searchResults)
                        {
                            _projectTreeItems.Add(result);
                            _filteredProjectTreeItems.Add(result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"设置歌词库高亮颜色失败: {ex.Message}",
                    "错误",
                    WpfMessageBoxButton.OK,
                    WpfMessageBoxImage.Error);
            }
        }

        private void CreateLyricsSong(int groupId)
        {
            if (_dbContext == null)
            {
                return;
            }

            string rawName = PromptTextDialog("新建歌词", "请输入歌曲名称：", $"新歌_{DateTime.Now:HHmmss}");
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return;
            }

            var sortOrders = _dbContext.LyricsProjects
                .Where(p => p.GroupId == groupId)
                .Select(p => p.SortOrder)
                .ToList();
            int nextOrder = sortOrders.Count == 0 ? 0 : sortOrders.Max() + 1;

            var project = new LyricsProject
            {
                Name = rawName.Trim(),
                GroupId = groupId,
                ImageId = null,
                SourceType = 1,
                SortOrder = nextOrder,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now,
                FontSize = 60,
                TextAlign = "Center",
                Content = ""
            };
            _dbContext.LyricsProjects.Add(project);
            _dbContext.SaveChanges();

            LoadProjects();
            FocusLyricsTreeNode(groupId, project.Id);
            _ = EnterLyricsModeFromSongAsync(project.Id);
        }

        private void FocusLyricsTreeNode(int groupId, int songId)
        {
            try
            {
                foreach (var root in _projectTreeItems)
                {
                    root.IsSelected = false;
                }

                var groupNode = _projectTreeItems
                    .FirstOrDefault(x => x.Type == TreeItemType.LyricsGroup && x.Id == groupId);
                if (groupNode == null)
                {
                    return;
                }

                groupNode.IsExpanded = true;
                var songNode = groupNode.Children?
                    .FirstOrDefault(x => x.Type == TreeItemType.LyricsSong && x.Id == songId);
                if (songNode != null)
                {
                    songNode.IsSelected = true;
                }
            }
            catch
            {
                // 仅优化导航体验，失败不影响主流程
            }
        }

        private void RenameLyricsGroup(ProjectTreeItem groupItem)
        {
            if (_dbContext == null || groupItem == null)
            {
                return;
            }

            var group = _dbContext.LyricsGroups.FirstOrDefault(g => g.Id == groupItem.Id);
            if (group == null || group.IsSystem)
            {
                ShowStatus("系统分组不支持重命名");
                return;
            }

            string name = PromptTextDialog("重命名歌词库", "请输入新歌词库名称：", group.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            group.Name = name.Trim();
            group.ModifiedTime = DateTime.Now;
            _dbContext.SaveChanges();
            ReloadProjectsPreservingLyricsTreeState(TreeItemType.LyricsGroup, group.Id);
            ShowStatus($"已重命名歌词库: {group.Name}");
        }

        private void RenameLyricsSong(ProjectTreeItem songItem)
        {
            if (_dbContext == null || songItem == null)
            {
                return;
            }

            var song = _dbContext.LyricsProjects.FirstOrDefault(p => p.Id == songItem.Id);
            if (song == null)
            {
                return;
            }

            string name = PromptTextDialog("重命名歌曲", "请输入新歌曲名称：", song.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            song.Name = name.Trim();
            song.ModifiedTime = DateTime.Now;
            _dbContext.SaveChanges();
            ReloadProjectsPreservingLyricsTreeState(TreeItemType.LyricsSong, song.Id);
            ShowStatus($"已重命名歌曲: {song.Name}");
        }

        private void ReloadProjectsPreservingLyricsTreeState(TreeItemType? preferredType = null, int preferredId = 0)
        {
            var state = CaptureLyricsTreeViewState();
            LoadProjects();
            RestoreLyricsTreeViewState(state, preferredType, preferredId);
        }

        private LyricsTreeViewState CaptureLyricsTreeViewState()
        {
            var state = new LyricsTreeViewState();
            foreach (var item in _projectTreeItems)
            {
                if (item.Type == TreeItemType.LyricsGroup && item.IsExpanded)
                {
                    state.ExpandedGroupIds.Add(item.Id);
                }

                if (item.IsSelected && state.SelectedId <= 0)
                {
                    state.SelectedType = item.Type;
                    state.SelectedId = item.Id;
                }

                if (item.Type == TreeItemType.LyricsGroup && item.Children != null)
                {
                    foreach (var child in item.Children)
                    {
                        if (child.IsSelected && state.SelectedId <= 0)
                        {
                            state.SelectedType = child.Type;
                            state.SelectedId = child.Id;
                        }
                    }
                }
            }

            return state;
        }

        private void RestoreLyricsTreeViewState(LyricsTreeViewState state, TreeItemType? preferredType, int preferredId)
        {
            if (state == null)
            {
                return;
            }

            foreach (var item in _projectTreeItems.Where(x => x.Type == TreeItemType.LyricsGroup))
            {
                item.IsExpanded = state.ExpandedGroupIds.Contains(item.Id);
                item.IsSelected = false;
                if (item.Children == null)
                {
                    continue;
                }

                foreach (var child in item.Children)
                {
                    child.IsSelected = false;
                }
            }

            var targetType = preferredType ?? state.SelectedType;
            var targetId = preferredId > 0 ? preferredId : state.SelectedId;
            if (targetId <= 0)
            {
                return;
            }

            if (targetType == TreeItemType.LyricsGroup)
            {
                var groupNode = _projectTreeItems.FirstOrDefault(x => x.Type == TreeItemType.LyricsGroup && x.Id == targetId);
                if (groupNode != null)
                {
                    groupNode.IsExpanded = true;
                    groupNode.IsSelected = true;
                }
                return;
            }

            if (targetType == TreeItemType.LyricsSong)
            {
                foreach (var groupNode in _projectTreeItems.Where(x => x.Type == TreeItemType.LyricsGroup))
                {
                    var songNode = groupNode.Children?.FirstOrDefault(x => x.Type == TreeItemType.LyricsSong && x.Id == targetId);
                    if (songNode == null)
                    {
                        continue;
                    }

                    groupNode.IsExpanded = true;
                    songNode.IsSelected = true;
                    return;
                }
            }
        }

        private sealed class LyricsTreeViewState
        {
            public HashSet<int> ExpandedGroupIds { get; } = new();
            public TreeItemType SelectedType { get; set; } = TreeItemType.LyricsRoot;
            public int SelectedId { get; set; }
        }

        private void DeleteLyricsGroup(int groupId)
        {
            if (_dbContext == null)
            {
                return;
            }

            var group = _dbContext.LyricsGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null)
            {
                return;
            }

            if (group.IsSystem)
            {
                ShowStatus("系统分组不允许删除");
                return;
            }

            var result = WpfMessageBox.Show(
                $"确定删除歌词库“{group.Name}”吗？\n库内所有歌词将一并删除，且不可恢复。",
                "确认删除歌词库",
                WpfMessageBoxButton.YesNo,
                WpfMessageBoxImage.Warning);
            if (result != WpfMessageBoxResult.Yes)
            {
                return;
            }

            var songs = _dbContext.LyricsProjects.Where(p => p.GroupId == groupId).ToList();
            var removedSongIds = songs.Select(s => s.Id).ToHashSet();
            if (songs.Count > 0)
            {
                _dbContext.LyricsProjects.RemoveRange(songs);
            }

            _dbContext.LyricsGroups.Remove(group);
            _dbContext.SaveChanges();

            if (_currentLyricsProject != null && removedSongIds.Contains(_currentLyricsProject.Id))
            {
                _currentLyricsProject = null;
                _currentLyricsProjectId = 0;
                if (_isLyricsMode)
                {
                    try
                    {
                        ExitLyricsMode();
                    }
                    catch
                    {
                        // 退出失败不阻断删除主流程
                    }
                }
            }

            LoadProjects();
            ShowStatus($"已删除歌词库: {group.Name}（同时删除 {songs.Count} 首歌词）");
        }

        private void DeleteLyricsSong(int songId)
        {
            if (_dbContext == null)
            {
                return;
            }

            var song = _dbContext.LyricsProjects.FirstOrDefault(p => p.Id == songId);
            if (song == null)
            {
                return;
            }

            var result = WpfMessageBox.Show(
                $"确定删除歌曲“{song.Name}”吗？",
                "确认删除歌曲",
                WpfMessageBoxButton.YesNo,
                WpfMessageBoxImage.Warning);
            if (result != WpfMessageBoxResult.Yes)
            {
                return;
            }

            _dbContext.LyricsProjects.Remove(song);
            _dbContext.SaveChanges();
            LoadProjects();
            ShowStatus($"已删除歌曲: {song.Name}");
        }

        private string PromptTextDialog(string title, string prompt, string defaultValue)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = prompt, FontSize = 14 };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var input = new System.Windows.Controls.TextBox
            {
                Text = defaultValue ?? "",
                FontSize = 14,
                Height = 34,
                Padding = new Thickness(8, 4, 8, 4)
            };
            Grid.SetRow(input, 2);
            grid.Children.Add(input);

            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var okButton = new System.Windows.Controls.Button { Content = "确定", Width = 88, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            var cancelButton = new System.Windows.Controls.Button { Content = "取消", Width = 88, Height = 32 };
            okButton.Click += (_, __) => { dialog.DialogResult = true; dialog.Close(); };
            cancelButton.Click += (_, __) => { dialog.DialogResult = false; dialog.Close(); };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 4);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.Loaded += (_, __) =>
            {
                input.Focus();
                input.SelectAll();
            };

            return dialog.ShowDialog() == true ? input.Text : null;
        }
    }
}



