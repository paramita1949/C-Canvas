using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.UI;
using ImageColorChanger.Core;

namespace ImageColorChanger.Managers
{
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
        /// <returns>搜索结果的项目树项集合</returns>
        public ObservableCollection<ProjectTreeItem> SearchProjects(string searchTerm, string searchScope)
        {
            var results = new ObservableCollection<ProjectTreeItem>();

            // 如果搜索词为空，返回所有项目
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return null; // 返回null表示重新加载所有项目
            }

            // 解析搜索范围
            int? searchFolderId = null;
            if (searchScope != "全部")
            {
                // 从范围中提取文件夹ID (格式: "文件夹名 (ID:123)")
                var match = Regex.Match(searchScope, @"\(ID:(\d+)\)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int folderId))
                {
                    searchFolderId = folderId;
                }
            }

            // 搜索数据库 - 只搜索图片文件
            var files = searchFolderId == null
                ? _dbManager.SearchFiles(searchTerm, FileType.Image)
                : _dbManager.SearchFilesInFolder(searchTerm, searchFolderId.Value, FileType.Image);

            // System.Diagnostics.Debug.WriteLine($"📁 数据库返回 {files.Count} 个文件");

            // 将搜索结果转换为树项
            foreach (var file in files)
            {
                var folderName = file.Folder?.Name ?? "根目录";
                var folderId = file.FolderId ?? 0;
                
                // 获取文件夹颜色（优先使用自定义颜色）
                string folderColor = "#666666"; // 默认颜色
                if (_configManager != null && folderId > 0)
                {
                    // 从数据库获取自定义颜色
                    string customColor = _dbManager.GetFolderHighlightColor(folderId);
                    folderColor = _configManager.GetFolderColor(folderId, customColor);
                }

                results.Add(new ProjectTreeItem
                {
                    Name = file.Name,  // 只显示文件名，不包含文件夹信息
                    Icon = "", // 搜索结果不显示图标
                    IconKind = "Image", // 设置为图片图标
                    IconColor = "#95E1D3", // 图片图标颜色
                    Type = TreeItemType.File,
                    Id = file.Id,
                    Path = file.Path,
                    FileType = file.FileType,
                    
                    // 文件夹标签信息
                    FolderName = folderName,
                    FolderColor = folderColor,
                    ShowFolderTag = true  // 在搜索结果中始终显示文件夹标签
                });
            }

            return results;
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

            return scopes;
        }
    }
}

