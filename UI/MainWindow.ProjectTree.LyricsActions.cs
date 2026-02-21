using System;
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

            var createGroupItem = new MenuItem { Header = "📁 新建歌词库" };
            createGroupItem.Click += (s, args) => CreateLyricsLibrary();
            contextMenu.Items.Add(createGroupItem);

            if (IsLyricsTransferFeatureEnabled)
            {
                contextMenu.Items.Add(new Separator());

                var importItem = new MenuItem { Header = "📥 导入 .lyr" };
                importItem.Click += async (s, args) => await ImportLyricsPackageAsync();
                contextMenu.Items.Add(importItem);

                var exportItem = new MenuItem { Header = "📤 导出全部 .lyr" };
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

            var createSongItem = new MenuItem { Header = "🎵 新建歌词" };
            createSongItem.Click += (s, args) => CreateLyricsSong(groupItem.Id);
            contextMenu.Items.Add(createSongItem);

            contextMenu.Items.Add(new Separator());

            var renameItem = new MenuItem { Header = "✏️ 重命名歌词库" };
            renameItem.Click += (s, args) => RenameLyricsGroup(groupItem);
            contextMenu.Items.Add(renameItem);

            if (IsLyricsTransferFeatureEnabled)
            {
                var exportItem = new MenuItem { Header = "📤 导出歌词库 .lyr" };
                exportItem.Click += async (s, args) => await ExportLyricsGroupPackageAsync(groupItem.Id);
                contextMenu.Items.Add(exportItem);
            }

            contextMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑️ 删除歌词库" };
            deleteItem.Click += (s, args) => DeleteLyricsGroup(groupItem.Id);
            contextMenu.Items.Add(deleteItem);
        }

        private void BuildLyricsSongContextMenu(ContextMenu contextMenu, ProjectTreeItem songItem)
        {
            if (!IsLyricsLibraryFeatureEnabled)
            {
                return;
            }

            var openItem = new MenuItem { Header = "🎤 打开歌词" };
            openItem.Click += (s, args) => EnterLyricsModeFromSong(songItem.Id);
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new Separator());

            var renameItem = new MenuItem { Header = "✏️ 重命名" };
            renameItem.Click += (s, args) => RenameLyricsSong(songItem);
            contextMenu.Items.Add(renameItem);

            if (IsLyricsTransferFeatureEnabled)
            {
                var exportItem = new MenuItem { Header = "📤 导出 .lyr" };
                exportItem.Click += async (s, args) => await ExportLyricsSongPackageAsync(songItem.Id);
                contextMenu.Items.Add(exportItem);
            }

            contextMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑️ 删除歌曲" };
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
            var manager = new Managers.LyricsGroupManager(_dbContext);
            manager.CreateGroup(name.Trim(), nextOrder, isSystem: false);
            LoadProjects();
            ShowStatus($"✅ 已创建歌词库: {name.Trim()}");
        }

        private void CreateLyricsSong(int groupId)
        {
            if (_dbContext == null)
            {
                return;
            }

            string name = PromptTextDialog("新建歌词", "请输入歌曲名称：", $"新歌_{DateTime.Now:HHmmss}");
            if (string.IsNullOrWhiteSpace(name))
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
                Name = name.Trim(),
                GroupId = groupId,
                ImageId = null,
                SourceType = 1,
                SortOrder = nextOrder,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now,
                FontSize = 88,
                TextAlign = "Center",
                Content = ""
            };
            _dbContext.LyricsProjects.Add(project);
            _dbContext.SaveChanges();

            LoadProjects();
            EnterLyricsModeFromSong(project.Id);
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
                ShowStatus("⚠️ 系统分组不支持重命名");
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
            LoadProjects();
            ShowStatus($"✅ 已重命名歌词库: {group.Name}");
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
            LoadProjects();
            ShowStatus($"✅ 已重命名歌曲: {song.Name}");
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
                ShowStatus("⚠️ 系统分组不允许删除");
                return;
            }

            var result = WpfMessageBox.Show(
                $"确定删除歌词库“{group.Name}”吗？\n库内歌曲会被保留为未归档（不展示）。",
                "确认删除歌词库",
                WpfMessageBoxButton.YesNo,
                WpfMessageBoxImage.Warning);
            if (result != WpfMessageBoxResult.Yes)
            {
                return;
            }

            var songs = _dbContext.LyricsProjects.Where(p => p.GroupId == groupId).ToList();
            foreach (var song in songs)
            {
                song.GroupId = null;
                song.ModifiedTime = DateTime.Now;
            }

            _dbContext.LyricsGroups.Remove(group);
            _dbContext.SaveChanges();
            LoadProjects();
            ShowStatus($"✅ 已删除歌词库: {group.Name}");
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
            ShowStatus($"✅ 已删除歌曲: {song.Name}");
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
