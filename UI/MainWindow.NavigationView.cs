using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Services.TextEditor.Application;
using Microsoft.EntityFrameworkCore;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 导航视图与项目树加载
    /// </summary>
    public partial class MainWindow
    {
        // 添加标记，用于跟踪是否是第一次进入幻灯片模式
        private static bool _isFirstTimeEnteringProjects = true;
        private int _textProjectTreeLoadToken;

        private void ApplyUnifiedSearchResetAndTreeRefresh(TreeItemType? preferredType = null, int preferredId = 0)
        {
            bool hadSearchTerm = SearchBox != null && !string.IsNullOrWhiteSpace(SearchBox.Text);

            if (SearchBox != null && hadSearchTerm)
            {
                SearchBox.TextChanged -= SearchBox_TextChanged;
                try
                {
                    SearchBox.Clear();
                }
                finally
                {
                    SearchBox.TextChanged += SearchBox_TextChanged;
                }
            }

            UpdateSearchClearButtonVisibility(string.Empty);

            if (hadSearchTerm)
            {
                if (preferredType.HasValue && preferredId > 0)
                {
                    ReloadProjectsPreservingTreeState(preferredType, preferredId);
                }
                else
                {
                    ReloadProjectsPreservingTreeState();
                }

                return;
            }

            FilterProjectTree();
        }

        /// <summary>
        /// 从数据库加载项目树
        /// </summary>
        private void LoadProjects(bool enableDetailedIcons = true)
        {
            try
            {
                var dbManager = DatabaseManagerService;
                _projectTreeItems.Clear();

                var folders = dbManager.GetAllFolders();
                var rootFiles = BuildRootMediaFiles(dbManager);
                var folderMediaLookup = BuildFolderMediaLookup(dbManager, folders);
                var manualSortFolderIds = dbManager.GetManualSortFolderIds().ToHashSet();
                BuildOriginalMarkLookup(enableDetailedIcons, out var markedFolderIds, out var folderSequenceIds, out var markedImageIds);
#if DEBUG
                // System.Diagnostics.Trace.WriteLine(
                //     $"[LoadProjects] DbPath={dbManager.GetDatabasePath()}, folders={folders.Count}, rootFiles={rootFiles.Count}");
                // foreach (var f in folders)
                // {
                //     System.Diagnostics.Trace.WriteLine($"[LoadProjects] Folder: Id={f.Id}, Name={f.Name}, Path={f.Path}");
                // }
#endif

                foreach (var folder in folders)
                {
                    bool isManualSort = manualSortFolderIds.Contains(folder.Id);
                    if (!folderMediaLookup.TryGetValue(folder.Id, out var files))
                    {
                        files = new List<MediaFile>();
                    }

                    bool hasMediaFiles = files.Any(f => f.FileType == FileType.Video || f.FileType == FileType.Audio);
                    string folderPlayMode = folder.VideoPlayMode;
                    bool hasColorEffectMark = folder.AutoColorEffect == 1;

                    string iconKind;
                    string iconColor;
                    if (hasMediaFiles)
                    {
                        if (!string.IsNullOrEmpty(folderPlayMode))
                        {
                            (iconKind, iconColor) = GetPlayModeIcon(folderPlayMode);
                        }
                        else
                        {
                            (iconKind, iconColor) = GetPlayModeIcon("random");
                        }
                    }
                    else if (hasColorEffectMark)
                    {
                        (iconKind, iconColor) = ("FolderStar", ICON_COLOR_PALETTE);
                    }
                    else
                    {
                        // Keep folder badge-style icon variants (sync/download/cog) from the same source
                        // so highlight recolor only changes color and never downgrades icon kind to plain Folder.
                        (iconKind, iconColor) = _originalManager.GetFolderIconKind(folder.Id, isManualSort);
                    }
                    iconColor = ResolveFolderHighlightIconColor(folder.Id, folder.HighlightColor, iconColor);
                    bool hasCustomHighlightColor = !string.IsNullOrWhiteSpace(folder.HighlightColor);

                    var folderItem = new ProjectTreeItem
                    {
                        Id = folder.Id,
                        Name = folder.Name,
                        Icon = iconKind,
                        IconKind = iconKind,
                        IconColor = iconColor,
                        UseCustomIconColor = hasCustomHighlightColor,
                        Type = TreeItemType.Folder,
                        Path = folder.Path,
                        Children = new ObservableCollection<ProjectTreeItem>()
                    };

                    // [Debug][FolderHighlight] LoadProjects log disabled after verification.

                    foreach (var file in files)
                    {
                        string fileIconKind = "File";
                        string fileIconColor = "#95E1D3";
                        if (file.FileType == FileType.Image)
                        {
                            bool imageMarked = enableDetailedIcons && markedImageIds != null && markedImageIds.Contains(file.Id);
                            (fileIconKind, fileIconColor) = imageMarked ? ("Star", "#FFD700") : ("Image", "#95E1D3");
                        }

                        folderItem.Children.Add(new ProjectTreeItem
                        {
                            Id = file.Id,
                            Name = file.Name,
                            Icon = fileIconKind,
                            IconKind = fileIconKind,
                            IconColor = fileIconColor,
                            Type = TreeItemType.File,
                            Path = file.Path,
                            FileType = file.FileType
                        });
                    }

                    _projectTreeItems.Add(folderItem);
                }

                foreach (var file in rootFiles)
                {
                    string rootFileIconKind = "File";
                    string rootFileIconColor = "#95E1D3";
                    if (file.FileType == FileType.Image)
                    {
                        bool imageMarked = enableDetailedIcons && markedImageIds != null && markedImageIds.Contains(file.Id);
                        (rootFileIconKind, rootFileIconColor) = imageMarked ? ("Star", "#FFD700") : ("Image", "#95E1D3");
                    }

                    _projectTreeItems.Add(new ProjectTreeItem
                    {
                        Id = file.Id,
                        Name = file.Name,
                        Icon = rootFileIconKind,
                        IconKind = rootFileIconKind,
                        IconColor = rootFileIconColor,
                        Type = TreeItemType.File,
                        Path = file.Path,
                        FileType = file.FileType
                    });
                }

                if (IsLyricsLibraryFeatureEnabled)
                {
                    LoadLyricsLibraryToTree();
                }
                LoadTextProjectsToTree();
                FilterProjectTree();
            }
            catch (Exception)
            {
            }
        }

        private void BuildOriginalMarkLookup(
            bool enableDetailedIcons,
            out HashSet<int> markedFolderIds,
            out HashSet<int> folderSequenceIds,
            out HashSet<int> markedImageIds)
        {
            markedFolderIds = null;
            folderSequenceIds = null;
            markedImageIds = null;

            if (!enableDetailedIcons || _dbContext == null)
            {
                return;
            }

            var markRows = _dbContext.OriginalMarks
                .AsNoTracking()
                .Select(m => new { m.ItemTypeString, m.ItemId, m.MarkTypeString })
                .ToList();

            markedFolderIds = markRows
                .Where(m => string.Equals(m.ItemTypeString, "folder", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ItemId)
                .ToHashSet();

            folderSequenceIds = markRows
                .Where(m =>
                    string.Equals(m.ItemTypeString, "folder", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.MarkTypeString, "sequence", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ItemId)
                .ToHashSet();

            markedImageIds = markRows
                .Where(m => string.Equals(m.ItemTypeString, "image", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ItemId)
                .ToHashSet();
        }

        private static (string iconKind, string iconColor) ResolveFolderIcon(
            int folderId,
            bool isManualSort,
            bool enableDetailedIcons,
            HashSet<int> markedFolderIds,
            HashSet<int> folderSequenceIds)
        {
            if (!enableDetailedIcons || markedFolderIds == null || !markedFolderIds.Contains(folderId))
            {
                return (isManualSort ? "FolderCog" : "Folder", "#FDB44B");
            }

            bool isSequence = folderSequenceIds != null && folderSequenceIds.Contains(folderId);
            if (isSequence)
            {
                return ("FolderDownload", "#FF6B35");
            }

            return ("FolderSync", "#4ECDC4");
        }

        private string ResolveFolderHighlightIconColor(int folderId, string customHighlightColor, string fallbackIconColor)
        {
            if (string.IsNullOrWhiteSpace(customHighlightColor))
            {
                // [Debug][FolderHighlight] ResolveColor(null/empty) log disabled after verification.
                return fallbackIconColor;
            }

            if (_configManager != null && folderId > 0)
            {
                string resolved = _configManager.GetFolderColor(folderId, customHighlightColor);
                // [Debug][FolderHighlight] ResolveColor(resolved) log disabled after verification.
                return resolved;
            }

            // [Debug][FolderHighlight] ResolveColor(direct) log disabled after verification.
            return customHighlightColor;
        }

        private Dictionary<int, List<MediaFile>> BuildFolderMediaLookup(DatabaseManager dbManager, List<Folder> folders)
        {
            var result = new Dictionary<int, List<MediaFile>>();
            if (_dbContext == null || folders == null || folders.Count == 0)
            {
                return result;
            }

            var folderIds = folders.Select(f => f.Id).Distinct().ToList();
            var folderIdSet = folderIds.ToHashSet();

            var legacyFiles = _dbContext.MediaFiles
                .AsNoTracking()
                .Where(m => m.FolderId.HasValue && folderIdSet.Contains(m.FolderId.Value))
                .OrderBy(m => m.FolderId)
                .ThenBy(m => m.OrderIndex ?? int.MaxValue)
                .ToList();

            var legacyLookup = legacyFiles
                .GroupBy(m => m.FolderId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (!dbManager.IsFolderSystemV2Enabled())
            {
                return legacyLookup;
            }

            var mappedRows = (from fi in _dbContext.FolderImages.AsNoTracking()
                              join mf in _dbContext.MediaFiles.AsNoTracking() on fi.ImageId equals mf.Id
                              where folderIdSet.Contains(fi.FolderId)
                              orderby fi.FolderId, fi.OrderIndex, mf.OrderIndex
                              select new { fi.FolderId, MediaFile = mf })
                .ToList();

            var mappedLookup = mappedRows
                .GroupBy(r => r.FolderId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.MediaFile).ToList());

            foreach (var folderId in folderIds)
            {
                if (mappedLookup.TryGetValue(folderId, out var mappedFiles) && mappedFiles.Count > 0)
                {
                    result[folderId] = mappedFiles;
                }
                else if (legacyLookup.TryGetValue(folderId, out var fallbackFiles))
                {
                    result[folderId] = fallbackFiles;
                }
                else
                {
                    result[folderId] = new List<MediaFile>();
                }
            }

            return result;
        }

        private List<MediaFile> BuildRootMediaFiles(DatabaseManager dbManager)
        {
            if (_dbContext == null)
            {
                return dbManager.GetRootMediaFiles();
            }

            if (!dbManager.IsFolderSystemV2Enabled())
            {
                return _dbContext.MediaFiles
                    .AsNoTracking()
                    .Where(m => m.FolderId == null)
                    .OrderBy(m => m.OrderIndex ?? int.MaxValue)
                    .ToList();
            }

            return _dbContext.MediaFiles
                .AsNoTracking()
                .Where(m => m.FolderId == null &&
                            !_dbContext.FolderImages.Any(fi => fi.ImageId == m.Id))
                .OrderBy(m => m.OrderIndex ?? int.MaxValue)
                .ToList();
        }

        /// <summary>
        /// 根据当前视图模式过滤项目树
        /// </summary>
        private void FilterProjectTree()
        {
            _filteredProjectTreeItems.Clear();

            if (_currentViewMode == NavigationViewMode.Files)
            {
                foreach (var item in _projectTreeItems)
                {
                    if (item.Type == TreeItemType.Folder || item.Type == TreeItemType.File)
                    {
                        _filteredProjectTreeItems.Add(item);
                    }
                    else if (IsLyricsLibraryFeatureEnabled && (item.Type == TreeItemType.LyricsGroup || item.Type == TreeItemType.LyricsSong))
                    {
                        _filteredProjectTreeItems.Add(item);
                    }
                }
            }
            else
            {
                foreach (var item in _projectTreeItems)
                {
                    if (item.Type == TreeItemType.Project || item.Type == TreeItemType.TextProject)
                    {
                        _filteredProjectTreeItems.Add(item);
                    }
                }
            }
        }

        /// <summary>
        /// 文件按钮点击事件
        /// </summary>
        private void BtnShowFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewMode == NavigationViewMode.Files && !_isBibleMode) return;

            bool wasInBibleMode = _isBibleMode;

            _currentViewMode = NavigationViewMode.Files;
            _isBibleMode = false;

            BibleVerseScrollViewer.Visibility = Visibility.Collapsed;
            BibleNavigationPanel.Visibility = Visibility.Collapsed;
            ProjectTree.Visibility = Visibility.Visible;

            if (wasInBibleMode)
            {
                ImageScrollViewer.Visibility = Visibility.Visible;
                VideoContainer.Visibility = Visibility.Visible;
            }

            UpdateViewModeButtons();
            ApplyUnifiedSearchResetAndTreeRefresh();
        }

        /// <summary>
        /// 项目按钮点击事件
        /// </summary>
        private void BtnShowProjects_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewMode == NavigationViewMode.Projects && !_isBibleMode) return;

            bool wasInBibleMode = _isBibleMode;

            _currentViewMode = NavigationViewMode.Projects;
            _isBibleMode = false;

            BibleVerseScrollViewer.Visibility = Visibility.Collapsed;
            BibleNavigationPanel.Visibility = Visibility.Collapsed;
            ProjectTree.Visibility = Visibility.Visible;

            if (wasInBibleMode)
            {
                ClearImageDisplay();
                ImageScrollViewer.Visibility = Visibility.Visible;
                VideoContainer.Visibility = Visibility.Visible;
            }

            UpdateViewModeButtons();
            if (_currentTextProject != null)
            {
                ApplyUnifiedSearchResetAndTreeRefresh(TreeItemType.TextProject, _currentTextProject.Id);
            }
            else
            {
                ApplyUnifiedSearchResetAndTreeRefresh();
            }

            if (_isFirstTimeEnteringProjects && _currentTextProject == null)
            {
                _ = LoadFirstProjectAsync();
                _isFirstTimeEnteringProjects = false;
            }
        }

        /// <summary>
        /// 更新切换按钮的视觉状态
        /// </summary>
        private void UpdateViewModeButtons()
        {
            var inactiveBackground = new SolidColorBrush(Colors.White);
            var inactiveForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666"));
            var inactiveBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));

            var activeBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3"));
            var activeForeground = new SolidColorBrush(Colors.White);
            var activeBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1976D2"));

            BtnShowFiles.Background = _currentViewMode == NavigationViewMode.Files ? activeBackground : inactiveBackground;
            BtnShowFiles.Foreground = _currentViewMode == NavigationViewMode.Files ? activeForeground : inactiveForeground;
            BtnShowFiles.BorderBrush = _currentViewMode == NavigationViewMode.Files ? activeBorder : inactiveBorder;

            BtnShowProjects.Background = _currentViewMode == NavigationViewMode.Projects ? activeBackground : inactiveBackground;
            BtnShowProjects.Foreground = _currentViewMode == NavigationViewMode.Projects ? activeForeground : inactiveForeground;
            BtnShowProjects.BorderBrush = _currentViewMode == NavigationViewMode.Projects ? activeBorder : inactiveBorder;

            BtnShowBible.Background = _currentViewMode == NavigationViewMode.Bible ? activeBackground : inactiveBackground;
            BtnShowBible.Foreground = _currentViewMode == NavigationViewMode.Bible ? activeForeground : inactiveForeground;
            BtnShowBible.BorderBrush = _currentViewMode == NavigationViewMode.Bible ? activeBorder : inactiveBorder;

            UpdateSearchEntryModeVisual();
        }

        /// <summary>
        /// 加载文本项目到项目树
        /// </summary>
        private void LoadTextProjectsToTree()
        {
            int loadToken = ++_textProjectTreeLoadToken;
            _ = LoadTextProjectsToTreeAsync(loadToken);
        }

        private async Task LoadTextProjectsToTreeAsync(int loadToken)
        {
            try
            {
                var textProjectService = _mainWindowServices?.GetRequired<ITextProjectService>();
                if (textProjectService == null)
                {
                    return;
                }

                var textProjects = await textProjectService.GetAllProjectsAsync();

                if (loadToken != _textProjectTreeLoadToken)
                {
                    return;
                }

                foreach (var project in textProjects.OrderBy(p => p.Id))
                {
                    _projectTreeItems.Add(new ProjectTreeItem
                    {
                        Id = project.Id,
                        Name = project.Name,
                        Icon = "FileDocument",
                        IconKind = "FileDocument",
                        IconColor = "#2196F3",
                        Type = TreeItemType.TextProject,
                        Path = null
                    });
                }

                FilterProjectTree();
            }
            catch
            {
            }
        }

        private void LoadLyricsLibraryToTree()
        {
            try
            {
                if (_dbContext == null)
                {
                    return;
                }

                var groups = _dbContext.LyricsGroups
                    .AsNoTracking()
                    .Where(g => !g.IsSystem)
                    .OrderBy(g => g.SortOrder)
                    .ThenBy(g => g.Id)
                    .ToList();

                var songs = _dbContext.LyricsProjects
                    .AsNoTracking()
                    .OrderBy(p => p.SortOrder)
                    .ThenBy(p => p.Id)
                    .ToList();

                foreach (var group in groups)
                {
                    string groupHighlightColor = ResolveLyricsGroupTreeColor(group);
                    var groupSongs = songs
                        .Where(s => s.GroupId == group.Id)
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.Id)
                        .ToList();
                    var groupItem = new ProjectTreeItem
                    {
                        Id = group.Id,
                        Name = group.Name,
                        Icon = "FolderMusic",
                        IconKind = "FolderMusic",
                        IconColor = group.IsSystem ? "#9E9E9E" : groupHighlightColor,
                        UseCustomIconColor = !group.IsSystem && !string.IsNullOrWhiteSpace(group.HighlightColor),
                        Type = TreeItemType.LyricsGroup,
                        Tag = group,
                        Children = new ObservableCollection<ProjectTreeItem>()
                    };
                    if (string.IsNullOrWhiteSpace(groupItem.IconKind))
                    {
                        groupItem.IconKind = "FolderMusic";
                        groupItem.IconColor = "#4CAF50";
                    }

                    for (int i = 0; i < groupSongs.Count; i++)
                    {
                        var song = groupSongs[i];
                        groupItem.Children.Add(new ProjectTreeItem
                        {
                            Id = song.Id,
                            Name = BuildLyricsSongDisplayName(i + 1, song.Name),
                            Icon = "MusicNote",
                            IconKind = "MusicNote",
                            IconColor = "#FFC107",
                            Type = TreeItemType.LyricsSong,
                            Tag = song
                        });
                    }

                    _projectTreeItems.Add(groupItem);
                }

                // 兼容历史数据：未分组歌曲也需要在文件视图中可见（顶层歌曲节点）
                var ungroupedSongs = songs
                    .Where(s => !s.GroupId.HasValue)
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.Id)
                    .ToList();
                for (int i = 0; i < ungroupedSongs.Count; i++)
                {
                    var song = ungroupedSongs[i];
                    _projectTreeItems.Add(new ProjectTreeItem
                    {
                        Id = song.Id,
                        Name = BuildLyricsSongDisplayName(i + 1, song.Name),
                        Icon = "MusicNote",
                        IconKind = "MusicNote",
                        IconColor = "#FFC107",
                        Type = TreeItemType.LyricsSong,
                        Tag = song
                    });
                }
            }
            catch
            {
            }
        }

        private static string BuildLyricsSongDisplayName(int index, string rawName)
        {
            string name = string.IsNullOrWhiteSpace(rawName) ? "未命名" : rawName.Trim();
            return $"{index}.{name}";
        }

        private string ResolveLyricsGroupTreeColor(LyricsGroup group)
        {
            try
            {
                if (group == null)
                {
                    return "#4CAF50";
                }

                if (_configManager != null)
                {
                    return _configManager.GetFolderColor(group.Id, group.HighlightColor);
                }

                return string.IsNullOrWhiteSpace(group.HighlightColor) ? "#4CAF50" : group.HighlightColor;
            }
            catch
            {
                return "#4CAF50";
            }
        }

        private async Task LoadFirstProjectAsync()
        {
            try
            {
                var textProjectService = _mainWindowServices?.GetRequired<ITextProjectService>();
                if (textProjectService == null)
                {
                    ShowStatus("文本项目服务未初始化");
                    return;
                }

                var firstProject = (await textProjectService.GetAllProjectsAsync())
                    .OrderBy(p => p.Id)
                    .FirstOrDefault();

                if (firstProject != null)
                {
                    await LoadTextProjectAsync(firstProject.Id);
                    ShowStatus($"已打开项目: {firstProject.Name}");
                }
                else
                {
                    ShowStatus("暂无幻灯片项目，请创建新项目");
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"加载项目失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据文件类型获取图标
        /// </summary>
        private string GetFileIcon(FileType fileType)
        {
            return fileType switch
            {
                FileType.Image => "",
                FileType.Video => "",
                FileType.Audio => "",
                _ => ""
            };
        }
    }
}



