using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.UI;
using ImageColorChanger.Core;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Managers
{
    public enum MediaSearchFilterMode
    {
        All = 0,
        ImageOnly = 1,
        MediaOnly = 2
    }

    /// <summary>
    /// 搜索管理器 - 处理项目树的搜索功能
    /// </summary>
    public class SearchManager
    {
        private readonly DatabaseManager _dbManager;
        private readonly ConfigManager _configManager;

        public SearchManager(DatabaseManager dbManager, ConfigManager configManager = null)
        {
            _dbManager = dbManager;
            _configManager = configManager;
        }

        /// <summary>
        /// 搜索项目
        /// </summary>
        /// <param name="searchTerm">搜索关键词</param>
        /// <param name="searchScope">搜索范围 ("全部" 或文件夹名)</param>
        /// <param name="mediaFilterMode">媒体筛选模式（默认全部）</param>
        /// <returns>搜索结果的项目树项集合</returns>
        public ObservableCollection<ProjectTreeItem> SearchProjects(
            string searchTerm,
            string searchScope,
            MediaSearchFilterMode mediaFilterMode = MediaSearchFilterMode.All)
        {
            var results = new ObservableCollection<ProjectTreeItem>();

            // 如果搜索词为空，返回所有项目
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return null; // 返回null表示重新加载所有项目
            }

            ParseSearchScope(searchScope, out int? searchFolderId, out int? searchLyricsGroupId);

            // 图片/媒体文件搜索（选择歌词库范围时，不参与文件搜索）
            if (!searchLyricsGroupId.HasValue)
            {
                if (mediaFilterMode != MediaSearchFilterMode.MediaOnly)
                {
                    var imageFiles = searchFolderId == null
                        ? _dbManager.SearchFiles(searchTerm, FileType.Image)
                        : _dbManager.SearchFilesInFolder(searchTerm, searchFolderId.Value, FileType.Image);

                    foreach (var file in imageFiles)
                    {
                        var folderName = file.Folder?.Name ?? "根目录";
                        var folderId = file.FolderId ?? 0;

                        // 获取文件夹颜色（优先使用自定义颜色）
                        string folderColor = "#666666";
                        if (_configManager != null && folderId > 0)
                        {
                            string customColor = _dbManager.GetFolderHighlightColor(folderId);
                            folderColor = _configManager.GetFolderColor(folderId, customColor);
                        }

                        results.Add(new ProjectTreeItem
                        {
                            Name = file.Name,
                            Icon = "",
                            IconKind = "Image",
                            IconColor = "#95E1D3",
                            Type = TreeItemType.File,
                            Id = file.Id,
                            Path = file.Path,
                            FileType = file.FileType,
                            FolderName = BuildFileSearchFolderTag(folderName, file.FileType),
                            FolderColor = folderColor,
                            ShowFolderTag = true
                        });
                    }
                }

                if (mediaFilterMode != MediaSearchFilterMode.ImageOnly)
                {
                    AppendMediaSearchResults(results, searchTerm, searchFolderId);
                }
            }

            // 歌词搜索策略：
            // 1) 全部范围：同时搜索文件与歌词库
            // 2) 文件夹范围(ID): 仅搜索该文件夹文件
            // 3) 歌词库范围(LID): 仅搜索该歌词库
            bool includeLyrics = mediaFilterMode != MediaSearchFilterMode.MediaOnly;
            if (includeLyrics)
            {
                if (searchLyricsGroupId.HasValue)
                {
                    AppendLyricsSearchResults(results, searchTerm, searchLyricsGroupId);
                }
                else if (!searchFolderId.HasValue)
                {
                    AppendLyricsSearchResults(results, searchTerm, null);
                }
            }

            return results;
        }

        private void AppendMediaSearchResults(ObservableCollection<ProjectTreeItem> results, string searchTerm, int? searchFolderId)
        {
            IEnumerable<MediaFile> mediaFiles;
            if (searchFolderId.HasValue)
            {
                mediaFiles = _dbManager.SearchFilesInFolder(searchTerm, searchFolderId.Value, FileType.Video)
                    .Concat(_dbManager.SearchFilesInFolder(searchTerm, searchFolderId.Value, FileType.Audio));
            }
            else
            {
                mediaFiles = _dbManager.SearchFiles(searchTerm, FileType.Video)
                    .Concat(_dbManager.SearchFiles(searchTerm, FileType.Audio));
            }

            foreach (var file in mediaFiles.Where(f => !IsAppleDoubleSidecarFileName(f?.Name)).GroupBy(f => f.Id).Select(g => g.First()))
            {
                var folderName = file.Folder?.Name ?? "根目录";
                var folderId = file.FolderId ?? 0;

                string folderColor = "#666666";
                if (_configManager != null && folderId > 0)
                {
                    string customColor = _dbManager.GetFolderHighlightColor(folderId);
                    folderColor = _configManager.GetFolderColor(folderId, customColor);
                }

                results.Add(new ProjectTreeItem
                {
                    Name = file.Name,
                    Icon = "",
                    IconKind = "File",
                    IconColor = "#90CAF9",
                    Type = TreeItemType.File,
                    Id = file.Id,
                    Path = file.Path,
                    FileType = file.FileType,
                    FolderName = BuildFileSearchFolderTag(folderName, file.FileType),
                    FolderColor = folderColor,
                    ShowFolderTag = true
                });
            }
        }

        private static bool IsAppleDoubleSidecarFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) &&
                   fileName.StartsWith("._", StringComparison.Ordinal);
        }

        /// <summary>
        /// 获取所有文件夹列表用于搜索范围下拉框
        /// </summary>
        /// <returns>文件夹列表（包含"全部"选项）</returns>
        public ObservableCollection<string> GetSearchScopes()
        {
            var scopes = new ObservableCollection<string> { "全部" };

            var folders = _dbManager.GetAllFolders();
            foreach (var folder in folders)
            {
                scopes.Add($"{folder.Name} (ID:{folder.Id})");
            }

            try
            {
                var groups = _dbManager.GetDbContext().LyricsGroups
                    .AsNoTracking()
                    .Where(g => !g.IsSystem)
                    .OrderBy(g => g.SortOrder)
                    .ThenBy(g => g.Id)
                    .Select(g => new { g.Id, g.Name })
                    .ToList();

                foreach (var group in groups)
                {
                    scopes.Add($"{group.Name} (LID:{group.Id})");
                }
            }
            catch
            {
            }

            return scopes;
        }

        private void ParseSearchScope(string searchScope, out int? folderId, out int? lyricsGroupId)
        {
            folderId = null;
            lyricsGroupId = null;

            if (string.IsNullOrWhiteSpace(searchScope) || searchScope == "全部")
            {
                return;
            }

            var folderMatch = Regex.Match(searchScope, @"\(ID:(\d+)\)$", RegexOptions.IgnoreCase);
            if (folderMatch.Success && int.TryParse(folderMatch.Groups[1].Value, out int fId))
            {
                folderId = fId;
                return;
            }

            var lyricsMatch = Regex.Match(searchScope, @"\(LID:(\d+)\)$", RegexOptions.IgnoreCase);
            if (lyricsMatch.Success && int.TryParse(lyricsMatch.Groups[1].Value, out int lId))
            {
                lyricsGroupId = lId;
            }
        }

        private void AppendLyricsSearchResults(ObservableCollection<ProjectTreeItem> results, string searchTerm, int? lyricsGroupId)
        {
            try
            {
                var db = _dbManager.GetDbContext();
                if (db == null)
                {
                    return;
                }

                var groupQuery = db.LyricsGroups
                    .AsNoTracking()
                    .Where(g => !g.IsSystem);

                if (lyricsGroupId.HasValue)
                {
                    groupQuery = groupQuery.Where(g => g.Id == lyricsGroupId.Value);
                }

                var groups = groupQuery
                    .Select(g => new { g.Id, g.Name, g.HighlightColor })
                    .ToList();
                if (groups.Count == 0)
                {
                    return;
                }

                var groupMap = groups.ToDictionary(g => g.Id, g => g);
                var matchedGroupIds = groups
                    .Where(g => ContainsIgnoreCase(g.Name, searchTerm))
                    .Select(g => g.Id)
                    .ToHashSet();

                var songQuery = db.LyricsProjects.AsNoTracking();
                if (lyricsGroupId.HasValue)
                {
                    songQuery = songQuery.Where(s => s.GroupId == lyricsGroupId.Value);
                }
                else
                {
                    songQuery = songQuery.Where(s => s.SourceType == 1 || s.GroupId != null);
                }

                var songs = songQuery
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.Id)
                    .ToList();

                var hitGroupIds = new HashSet<int>();
                foreach (var song in songs)
                {
                    int gid = song.GroupId ?? 0;
                    string groupName = gid > 0 && groupMap.TryGetValue(gid, out var groupInfo) ? groupInfo.Name : "未分组";
                    string lyricsGroupLabel = BuildLyricsSearchTag(groupName);
                    bool nameMatch = ContainsIgnoreCase(song.Name, searchTerm);
                    bool contentMatch = ContainsIgnoreCase(song.Content, searchTerm);
                    bool groupMatch = gid > 0 && matchedGroupIds.Contains(gid);

                    if (!nameMatch && !contentMatch && !groupMatch)
                    {
                        continue;
                    }

                    if (gid > 0)
                    {
                        hitGroupIds.Add(gid);
                    }

                    results.Add(new ProjectTreeItem
                    {
                        Id = song.Id,
                        Name = song.Name,
                        Icon = "",
                        IconKind = "MusicNote",
                        IconColor = "#FFC107",
                        Type = TreeItemType.LyricsSong,
                        FolderName = lyricsGroupLabel,
                        FolderColor = ResolveLyricsGroupColor(gid, gid > 0 && groupMap.TryGetValue(gid, out var c) ? c.HighlightColor : null),
                        ShowFolderTag = true
                    });
                }

                // 当命中歌词库名称但库里歌曲为空时，仍显示该歌词库节点，确保“歌词库可被搜索定位”。
                foreach (int gid in matchedGroupIds)
                {
                    if (hitGroupIds.Contains(gid) || !groupMap.TryGetValue(gid, out var group))
                    {
                        continue;
                    }

                    results.Add(new ProjectTreeItem
                    {
                        Id = group.Id,
                        Name = BuildLyricsSearchTag(group.Name),
                        Icon = "",
                        IconKind = "FolderMusic",
                        IconColor = ResolveLyricsGroupColor(group.Id, group.HighlightColor),
                        Type = TreeItemType.LyricsGroup,
                        FolderName = "歌词库",
                        FolderColor = ResolveLyricsGroupColor(group.Id, group.HighlightColor),
                        ShowFolderTag = true
                    });
                }
            }
            catch
            {
            }
        }

        private string ResolveLyricsGroupColor(int groupId, string customColor)
        {
            if (_configManager != null && groupId > 0)
            {
                return _configManager.GetFolderColor(groupId, customColor);
            }

            return string.IsNullOrWhiteSpace(customColor) ? "#4CAF50" : customColor;
        }

        private static bool ContainsIgnoreCase(string text, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            return (text ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildLyricsSearchTag(string name)
        {
            string safeName = string.IsNullOrWhiteSpace(name) ? "未分组" : name.Trim();
            if (safeName.EndsWith("[歌词]", StringComparison.Ordinal) || safeName.EndsWith("（歌词）", StringComparison.Ordinal))
            {
                return safeName;
            }

            return $"{safeName} [歌词]";
        }

        private static string BuildMediaSearchTag(string name, FileType fileType)
        {
            string safeName = string.IsNullOrWhiteSpace(name) ? "未命名媒体" : name.Trim();
            string tag = fileType == FileType.Video ? "[视频]" : "[音频]";

            if (safeName.EndsWith(tag, StringComparison.Ordinal))
            {
                return safeName;
            }

            return $"{safeName} {tag}";
        }

        private static string BuildFileSearchFolderTag(string folderName, FileType fileType)
        {
            string safeFolderName = string.IsNullOrWhiteSpace(folderName) ? "根目录" : folderName.Trim();
            string tag = fileType switch
            {
                FileType.Image => "[图片]",
                FileType.Video => "[视频]",
                FileType.Audio => "[音频]",
                _ => "[文件]"
            };

            if (safeFolderName.EndsWith(tag, StringComparison.Ordinal))
            {
                return safeFolderName;
            }

            return $"{safeFolderName} {tag}";
        }
    }
}

