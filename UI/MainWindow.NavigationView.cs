using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Services.TextEditor.Application;
using ImageColorChanger.UI.Modules;
using ImageColorChanger.Utils;
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
        private int _textProjectTreeLoadToken;
        private const string FolderHierarchyModeSettingKey = "ProjectTree.FolderHierarchyModes";
        private bool _folderHierarchyModesLoaded;
        private readonly HashSet<int> _hierarchyFolderIds = new();

        private sealed class FolderTreeEntry
        {
            public MediaFile MediaFile { get; init; }
            public string RelativePath { get; init; }
        }

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
        private void LoadProjects(bool enableDetailedIcons = true, bool clearMediaPlaylistCache = true)
        {
            try
            {
                var loadSw = System.Diagnostics.Stopwatch.StartNew();
                var dbManager = DatabaseManagerService;
                _projectTreeItems.Clear();
                if (clearMediaPlaylistCache)
                {
                    ClearMediaPlaylistCache();
                }

                var phaseSw = System.Diagnostics.Stopwatch.StartNew();
                var folders = dbManager.GetAllFolders();
                var rootFiles = BuildRootMediaFiles(dbManager);
                var folderMediaLookup = BuildFolderMediaLookup(dbManager, folders);
                var manualSortFolderIds = dbManager.GetManualSortFolderIds().ToHashSet();
                BuildOriginalMarkLookup(enableDetailedIcons, out var markedFolderIds, out var folderSequenceIds, out var markedImageIds);
                StartupPerfLogger.Mark(
                    "MainWindow.LoadProjects.SourceData.Ready",
                    $"ElapsedMs={phaseSw.ElapsedMilliseconds}; folders={folders.Count}; rootFiles={rootFiles.Count}; detailedIcons={enableDetailedIcons}");
#if DEBUG
                // System.Diagnostics.Trace.WriteLine(
                //     $"[LoadProjects] DbPath={dbManager.GetDatabasePath()}, folders={folders.Count}, rootFiles={rootFiles.Count}");
                // foreach (var f in folders)
                // {
                //     System.Diagnostics.Trace.WriteLine($"[LoadProjects] Folder: Id={f.Id}, Name={f.Name}, Path={f.Path}");
                // }
#endif

                phaseSw.Restart();
                foreach (var folder in folders)
                {
                    bool isManualSort = manualSortFolderIds.Contains(folder.Id);
                    if (!folderMediaLookup.TryGetValue(folder.Id, out var folderEntries))
                    {
                        folderEntries = new List<FolderTreeEntry>();
                    }

                    bool hasNestedFolders = folderEntries.Any(e => HasNestedFolderPath(e.RelativePath));
                    bool hierarchyEnabled = hasNestedFolders && IsFolderHierarchyEnabled(folder.Id);
                    var files = folderEntries.Select(e => e.MediaFile).ToList();
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
                        HasNestedFolders = hasNestedFolders,
                        RootFolderId = folder.Id,
                        StateKey = $"folder:{folder.Id}",
                        Children = new ObservableCollection<ProjectTreeItem>()
                    };

                    // [Debug][FolderHighlight] LoadProjects log disabled after verification.

                    if (hierarchyEnabled)
                    {
                        BuildHierarchicalFolderChildren(folderItem, folder, folderEntries, enableDetailedIcons, markedImageIds);
                    }
                    else
                    {
                        AppendFlatFolderChildren(folderItem, files, enableDetailedIcons, markedImageIds);
                    }

                    _projectTreeItems.Add(folderItem);
                }
                StartupPerfLogger.Mark(
                    "MainWindow.LoadProjects.FoldersBuilt",
                    $"ElapsedMs={phaseSw.ElapsedMilliseconds}; folderCount={folders.Count}");

                phaseSw.Restart();
                foreach (var file in rootFiles)
                {
                    if (IsAppleDoubleSidecarPath(file.Path))
                    {
                        continue;
                    }

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
                        StateKey = $"file:{file.Id}",
                        FileType = file.FileType
                    });
                }
                StartupPerfLogger.Mark(
                    "MainWindow.LoadProjects.RootFilesBuilt",
                    $"ElapsedMs={phaseSw.ElapsedMilliseconds}; rootCount={rootFiles.Count}");

                phaseSw.Restart();
                if (IsLyricsLibraryFeatureEnabled)
                {
                    LoadLyricsLibraryToTree();
                }
                LoadTextProjectsToTree();
                FilterProjectTree();
                StartupPerfLogger.Mark(
                    "MainWindow.LoadProjects.Completed",
                    $"ElapsedMs={loadSw.ElapsedMilliseconds}; totalTreeItems={_projectTreeItems.Count}; filteredItems={_filteredProjectTreeItems.Count}");
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

        private Dictionary<int, List<FolderTreeEntry>> BuildFolderMediaLookup(DatabaseManager dbManager, List<Folder> folders)
        {
            var result = new Dictionary<int, List<FolderTreeEntry>>();
            if (_dbContext == null || folders == null || folders.Count == 0)
            {
                return result;
            }

            var folderIds = folders.Select(f => f.Id).Distinct().ToList();
            var folderIdSet = folderIds.ToHashSet();
            var folderPathById = folders.ToDictionary(f => f.Id, f => f.Path);

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
                foreach (var folder in folders)
                {
                    if (!legacyLookup.TryGetValue(folder.Id, out var legacyFolderFiles) || legacyFolderFiles.Count == 0)
                    {
                        result[folder.Id] = new List<FolderTreeEntry>();
                        continue;
                    }

                    result[folder.Id] = legacyFolderFiles
                        .Select(m => new FolderTreeEntry
                        {
                            MediaFile = m,
                            RelativePath = BuildRelativePathSafe(folder.Path, m.Path)
                        })
                        .ToList();
                }

                return result;
            }

            var mappedRows = (from fi in _dbContext.FolderImages.AsNoTracking()
                              join mf in _dbContext.MediaFiles.AsNoTracking() on fi.ImageId equals mf.Id
                              where folderIdSet.Contains(fi.FolderId)
                              orderby fi.FolderId, fi.OrderIndex, mf.OrderIndex
                              select new { fi.FolderId, MediaFile = mf, fi.RelativePath })
                .ToList();

            var mappedLookup = mappedRows
                .GroupBy(r => r.FolderId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => new FolderTreeEntry
                    {
                        MediaFile = x.MediaFile,
                        RelativePath = string.IsNullOrWhiteSpace(x.RelativePath)
                            ? BuildRelativePathSafe(folderPathById.TryGetValue(x.FolderId, out var path) ? path : null, x.MediaFile.Path)
                            : x.RelativePath
                    }).ToList());

            foreach (var folderId in folderIds)
            {
                if (mappedLookup.TryGetValue(folderId, out var mappedEntries) && mappedEntries.Count > 0)
                {
                    result[folderId] = mappedEntries;
                }
                else if (legacyLookup.TryGetValue(folderId, out var fallbackFiles))
                {
                    string folderPath = folderPathById.TryGetValue(folderId, out var path) ? path : null;
                    result[folderId] = fallbackFiles
                        .Select(m => new FolderTreeEntry
                        {
                            MediaFile = m,
                            RelativePath = BuildRelativePathSafe(folderPath, m.Path)
                        })
                        .ToList();
                }
                else
                {
                    result[folderId] = new List<FolderTreeEntry>();
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

        private void AppendFlatFolderChildren(
            ProjectTreeItem folderItem,
            IEnumerable<MediaFile> files,
            bool enableDetailedIcons,
            HashSet<int> markedImageIds)
        {
            foreach (var file in files)
            {
                if (IsAppleDoubleSidecarPath(file.Path))
                {
                    continue;
                }

                folderItem.Children.Add(CreateFileTreeItem(file, enableDetailedIcons, markedImageIds));
            }
        }

        private void BuildHierarchicalFolderChildren(
            ProjectTreeItem rootFolderItem,
            Folder folder,
            IEnumerable<FolderTreeEntry> entries,
            bool enableDetailedIcons,
            HashSet<int> markedImageIds)
        {
            var folderNodeByPath = new Dictionary<string, ProjectTreeItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var file = entry.MediaFile;
                if (file == null || IsAppleDoubleSidecarPath(file.Path))
                {
                    continue;
                }

                string relativePath = NormalizeRelativePath(entry.RelativePath);
                string directory = GetRelativeDirectory(relativePath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    rootFolderItem.Children.Add(CreateFileTreeItem(file, enableDetailedIcons, markedImageIds));
                    continue;
                }

                var parentNode = EnsureVirtualFolderPath(rootFolderItem, folder, folderNodeByPath, directory);
                parentNode.Children.Add(CreateFileTreeItem(file, enableDetailedIcons, markedImageIds));
            }
        }

        private ProjectTreeItem EnsureVirtualFolderPath(
            ProjectTreeItem rootFolderItem,
            Folder folder,
            Dictionary<string, ProjectTreeItem> folderNodeByPath,
            string directory)
        {
            var segments = directory
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var current = rootFolderItem;
            var cumulative = string.Empty;

            foreach (string segment in segments)
            {
                cumulative = string.IsNullOrEmpty(cumulative) ? segment : $"{cumulative}/{segment}";
                if (!folderNodeByPath.TryGetValue(cumulative, out var node))
                {
                    string fullPath = folder?.Path ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(fullPath))
                    {
                        fullPath = Path.Combine(fullPath, cumulative.Replace('/', Path.DirectorySeparatorChar));
                    }

                    node = new ProjectTreeItem
                    {
                        Id = BuildVirtualFolderNodeId(folder?.Id ?? 0, cumulative),
                        Name = segment,
                        Icon = "Folder",
                        IconKind = "Folder",
                        IconColor = "#FDB44B",
                        Type = TreeItemType.Folder,
                        IsVirtualFolder = true,
                        RootFolderId = folder?.Id,
                        RelativeFolderPath = cumulative,
                        Path = fullPath,
                        StateKey = $"vf:{folder?.Id ?? 0}:{cumulative.ToLowerInvariant()}",
                        Children = new ObservableCollection<ProjectTreeItem>()
                    };
                    folderNodeByPath[cumulative] = node;
                    current.Children.Add(node);
                }

                current = node;
            }

            return current;
        }

        private ProjectTreeItem CreateFileTreeItem(MediaFile file, bool enableDetailedIcons, HashSet<int> markedImageIds)
        {
            string fileIconKind = "File";
            string fileIconColor = "#95E1D3";
            if (file.FileType == FileType.Image)
            {
                bool imageMarked = enableDetailedIcons && markedImageIds != null && markedImageIds.Contains(file.Id);
                (fileIconKind, fileIconColor) = imageMarked ? ("Star", "#FFD700") : ("Image", "#95E1D3");
            }

            return new ProjectTreeItem
            {
                Id = file.Id,
                Name = file.Name,
                Icon = fileIconKind,
                IconKind = fileIconKind,
                IconColor = fileIconColor,
                Type = TreeItemType.File,
                Path = file.Path,
                StateKey = $"file:{file.Id}",
                FileType = file.FileType
            };
        }

        private static bool HasNestedFolderPath(string relativePath)
        {
            return !string.IsNullOrWhiteSpace(GetRelativeDirectory(relativePath));
        }

        private static string BuildRelativePathSafe(string rootPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(rootPath))
                {
                    string relative = Path.GetRelativePath(rootPath, fullPath);
                    return NormalizeRelativePath(relative);
                }
            }
            catch
            {
            }

            return NormalizeRelativePath(Path.GetFileName(fullPath));
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            return relativePath.Replace('\\', '/').Trim();
        }

        private static string GetRelativeDirectory(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            string normalized = NormalizeRelativePath(relativePath);
            int lastSlash = normalized.LastIndexOf('/');
            return lastSlash <= 0 ? string.Empty : normalized.Substring(0, lastSlash);
        }

        private static int BuildVirtualFolderNodeId(int folderId, string relativeFolderPath)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + folderId;
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(relativeFolderPath ?? string.Empty);
                if (hash == int.MinValue)
                {
                    hash = int.MinValue + 1;
                }

                return hash > 0 ? -hash : hash;
            }
        }

        private void EnsureFolderHierarchyModesLoaded()
        {
            if (_folderHierarchyModesLoaded)
            {
                return;
            }

            _folderHierarchyModesLoaded = true;
            _hierarchyFolderIds.Clear();
            try
            {
                string raw = DatabaseManagerService.GetUISetting(FolderHierarchyModeSettingKey, string.Empty) ?? string.Empty;
                foreach (string token in raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(token.Trim(), out int folderId) && folderId > 0)
                    {
                        _hierarchyFolderIds.Add(folderId);
                    }
                }
            }
            catch
            {
            }
        }

        private bool IsFolderHierarchyEnabled(int folderId)
        {
            EnsureFolderHierarchyModesLoaded();
            return _hierarchyFolderIds.Contains(folderId);
        }

        private void SetFolderHierarchyEnabled(int folderId, bool enabled)
        {
            if (folderId <= 0)
            {
                return;
            }

            EnsureFolderHierarchyModesLoaded();
            if (enabled)
            {
                _hierarchyFolderIds.Add(folderId);
            }
            else
            {
                _hierarchyFolderIds.Remove(folderId);
            }

            try
            {
                string serialized = string.Join(",", _hierarchyFolderIds.OrderBy(id => id));
                DatabaseManagerService.SaveUISetting(FolderHierarchyModeSettingKey, serialized);
            }
            catch
            {
            }
        }

        private int ResolveRootFolderId(ProjectTreeItem item)
        {
            if (item == null)
            {
                return 0;
            }

            if (item.RootFolderId.HasValue && item.RootFolderId.Value > 0)
            {
                return item.RootFolderId.Value;
            }

            return item.Id > 0 ? item.Id : 0;
        }

        private void SetFolderHierarchyMode(ProjectTreeItem item, bool enabled)
        {
            int rootFolderId = ResolveRootFolderId(item);
            if (rootFolderId <= 0)
            {
                return;
            }

            bool current = IsFolderHierarchyEnabled(rootFolderId);
            if (current == enabled)
            {
                return;
            }

            SetFolderHierarchyEnabled(rootFolderId, enabled);
            ReloadProjectsPreservingTreeState(TreeItemType.Folder, rootFolderId);
            ShowStatus(enabled ? "已切换为多层级展开模式" : "已切换为单级穿透模式");
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
            ApplyBibleTitleDisplayMode(false);
            SyncProjectionBibleTitle();

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
            bool isProjectionActive = _projectionManager?.IsProjectionActive == true;

            _currentViewMode = NavigationViewMode.Projects;
            _isBibleMode = false;
            if (BibleUiBehaviorResolver.ShouldClearBibleProjectionWhenSwitchingToSlides(
                    wasInBibleMode,
                    isProjectionActive))
            {
                ClearProjectedBibleContentForSlideSwitch();
            }
            else
            {
                ApplyBibleTitleDisplayMode(false);
                SyncProjectionBibleTitle();
            }

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

            if (BibleUiBehaviorResolver.ShouldAutoLoadBlankSlideOnProjectsViewEntry(
                    wasInBibleMode,
                    isProjectionActive,
                    _isProjectionLocked))
            {
                _ = EnsureBlankSlideOnBibleProjectionExitAsync();
                return;
            }

            if (BibleUiBehaviorResolver.ShouldRestoreLockedSlideOnProjectsViewEntry(
                    _isProjectionLocked,
                    _lockedProjectionProjectId.HasValue,
                    _lockedProjectionSlideId.HasValue))
            {
                _ = RestoreLockedProjectionSlideAsync();
                return;
            }

            if (BibleUiBehaviorResolver.ShouldAutoLoadProjectOnProjectsViewEntry(_currentTextProject != null))
            {
                _ = LoadFirstProjectAsync();
                return;
            }

            RefreshProjectionFromCurrentSlideIfNeeded();
        }

        private void ClearProjectedBibleContentForSlideSwitch()
        {
            BibleChapterTitle.Text = string.Empty;
            ApplyBibleTitleDisplayMode(false);

            _mergedVerses?.Clear();
            _projectionManager?.ClearProjectionDisplay();
            SyncProjectionBibleTitle();
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

        private async Task LoadFirstProjectAsync(bool preferBlankSlide = false)
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
                    if (preferBlankSlide)
                    {
                        await SelectOrCreateBlankSlideInCurrentProjectAsync();
                    }
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

        private async Task EnsureBlankSlideOnBibleProjectionExitAsync()
        {
            try
            {
                if (_currentTextProject == null)
                {
                    await LoadFirstProjectAsync(preferBlankSlide: true);
                    return;
                }

                await SelectOrCreateBlankSlideInCurrentProjectAsync();
            }
            catch (Exception ex)
            {
                ShowStatus($"加载空白幻灯片失败: {ex.Message}");
            }
        }

        private async Task SelectOrCreateBlankSlideInCurrentProjectAsync()
        {
            if (_currentTextProject == null)
            {
                return;
            }

            var textProjectService = _mainWindowServices?.GetRequired<ITextProjectService>();
            if (textProjectService == null)
            {
                return;
            }

            var slides = await textProjectService.GetSlidesByProjectWithElementsAsync(_currentTextProject.Id);
            if (slides == null || slides.Count == 0)
            {
                return;
            }

            var blankSlide = slides
                .OrderBy(s => s.SortOrder)
                .FirstOrDefault(IsBlankSlide);

            if (blankSlide == null)
            {
                int maxOrder = slides.Max(s => s.SortOrder);
                blankSlide = new Slide
                {
                    ProjectId = _currentTextProject.Id,
                    Title = "空白幻灯片",
                    SortOrder = maxOrder + 1,
                    BackgroundColor = GetCurrentSlideThemeBackgroundColorHex(),
                    SplitMode = -1,
                    SplitStretchMode = _splitImageDisplayModePreference
                };

                blankSlide = await textProjectService.AddSlideAsync(blankSlide);
                await LoadSlideList();
            }

            var selectableBlankSlide = blankSlide;
            if (SlideListBox?.ItemsSource is IEnumerable<Slide> visibleSlides)
            {
                selectableBlankSlide = visibleSlides.FirstOrDefault(s => s.Id == blankSlide.Id) ?? blankSlide;
            }

            _isRevertingSlideSelection = true;
            try
            {
                SlideListBox.SelectedItem = selectableBlankSlide;
            }
            finally
            {
                _isRevertingSlideSelection = false;
            }

            if (_currentSlide?.Id != selectableBlankSlide.Id)
            {
                await LoadSlide(selectableBlankSlide);
            }

            ShowStatus($"已切换到空白幻灯片: {selectableBlankSlide.Title}");
        }

        private static bool IsBlankSlide(Slide slide)
        {
            if (slide == null)
            {
                return false;
            }

            bool hasElements = slide.Elements?.Any() == true;
            bool hasBackgroundImage = !string.IsNullOrWhiteSpace(slide.BackgroundImagePath);
            bool hasGradientBackground = slide.BackgroundGradientEnabled;
            bool hasVideoBackground = slide.VideoBackgroundEnabled;

            return !hasElements &&
                   !hasBackgroundImage &&
                   !hasGradientBackground &&
                   !hasVideoBackground;
        }

        private void RefreshProjectionFromCurrentSlideIfNeeded()
        {
            if (_projectionManager?.IsProjectionActive != true ||
                _isProjectionLocked ||
                _currentSlide == null ||
                TextEditorPanel?.Visibility != Visibility.Visible)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                new Action(UpdateProjectionFromCanvas),
                System.Windows.Threading.DispatcherPriority.Render);
        }

        private async Task RestoreLockedProjectionSlideAsync()
        {
            if (!_lockedProjectionProjectId.HasValue || !_lockedProjectionSlideId.HasValue)
            {
                return;
            }

            try
            {
                int lockedProjectId = _lockedProjectionProjectId.Value;
                int lockedSlideId = _lockedProjectionSlideId.Value;

                await LoadTextProjectAsync(lockedProjectId);

                if (SlideListBox?.ItemsSource is IEnumerable<Slide> slides)
                {
                    var lockedSlide = slides.FirstOrDefault(s => s.Id == lockedSlideId);
                    if (lockedSlide != null)
                    {
                        _isRevertingSlideSelection = true;
                        try
                        {
                            SlideListBox.SelectedItem = lockedSlide;
                        }
                        finally
                        {
                            _isRevertingSlideSelection = false;
                        }

                        if (_currentSlide?.Id != lockedSlide.Id)
                        {
                            await LoadSlide(lockedSlide);
                        }

                        if (_projectionManager?.IsProjectionActive == true)
                        {
                            _ = Dispatcher.BeginInvoke(
                                new Action(UpdateProjectionFromCanvas),
                                System.Windows.Threading.DispatcherPriority.Render);
                        }

                        ShowStatus($"已恢复锁定幻灯片: {lockedSlide.Title}");
                        return;
                    }
                }

                ShowStatus("锁定幻灯片不存在，已打开项目");
            }
            catch (Exception ex)
            {
                ShowStatus($"恢复锁定幻灯片失败: {ex.Message}");
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



