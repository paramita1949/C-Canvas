using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 导入管理器
    /// 负责处理文件夹导入、单个文件导入和同步功能
    /// </summary>
    public class ImportManager
    {
        private static readonly System.Threading.SemaphoreSlim WriteLock = new(1, 1);
        private readonly DatabaseManager _dbManager;
        private readonly SortManager _sortManager;
        public string LastError { get; private set; }

        /// <summary>
        /// 支持的图片扩展名
        /// </summary>
        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif" };

        /// <summary>
        /// 支持的视频扩展名
        /// </summary>
        public static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".f4v", ".rm", ".rmvb" };

        /// <summary>
        /// 支持的音频扩展名
        /// </summary>
        public static readonly string[] AudioExtensions = { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac" };

        /// <summary>
        /// 所有支持的扩展名
        /// </summary>
        public static readonly string[] AllExtensions = ImageExtensions.Concat(VideoExtensions).Concat(AudioExtensions).ToArray();

        /// <summary>
        /// 构造函数
        /// </summary>
        public ImportManager(DatabaseManager dbManager, SortManager sortManager)
        {
            _dbManager = dbManager ?? throw new ArgumentNullException(nameof(dbManager));
            _sortManager = sortManager ?? throw new ArgumentNullException(nameof(sortManager));
        }

        /// <summary>
        /// 导入单个媒体文件（图片/视频/音频）
        /// </summary>
        public MediaFile ImportSingleFile(string filePath)
        {
            LastError = null;
            WriteLock.Wait();
            var operationId = Guid.NewGuid().ToString("N");
#if DEBUG
            // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} ImportSingleFile 开始: {filePath}");
#endif
            try
            {
                if (!File.Exists(filePath))
                {
                    LastError = "文件不存在";
                    return null;
                }

                // 检查文件扩展名是否支持
                var extension = Path.GetExtension(filePath).ToLower();
                if (!AllExtensions.Contains(extension))
                {
                    LastError = "不支持的文件格式";
                    return null;
                }

                // 添加到数据库（作为根目录文件，folderId为null）
                var mediaFile = _dbManager.AddMediaFile(filePath, null);

                if (mediaFile != null)
                {
                    //System.Diagnostics.Debug.WriteLine($"✅ 成功导入文件: {mediaFile.Name}");
                }

#if DEBUG
                // System.Diagnostics.Trace.WriteLine(
                //     $"[ImportManager] op={operationId} ImportSingleFile 完成: {(mediaFile != null ? mediaFile.Name : "<null>")}");
#endif
                return mediaFile;
            }
            catch (Exception ex)
            {
                LastError = $"导入文件失败: {ex.Message}";
#if DEBUG
                // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} ImportSingleFile 异常: {ex.Message}");
                // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} ImportSingleFile 堆栈: {ex.StackTrace}");
#endif
                return null;
            }
            finally
            {
                WriteLock.Release();
            }
        }

        /// <summary>
        /// 导入文件夹（递归扫描所有子目录）
        /// </summary>
        /// <param name="folderPath">文件夹路径</param>
        /// <returns>导入结果：(文件夹, 新文件列表, 已存在文件列表)</returns>
        public (Folder folder, List<MediaFile> newFiles, List<string> existingFiles) ImportFolder(string folderPath)
        {
            LastError = null;
            folderPath = NormalizePath(folderPath);
            WriteLock.Wait();
            var operationId = Guid.NewGuid().ToString("N");
#if DEBUG
            // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} ImportFolder 开始: {folderPath}");
#endif
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    LastError = "文件夹不存在";
                    return (null, null, null);
                }

                var folderName = Path.GetFileName(folderPath);
                // System.Diagnostics.Debug.WriteLine($"📁 开始导入文件夹: {folderName}");

                // 递归扫描所有支持的媒体文件
                var mediaFiles = ScanMediaFilesRecursively(folderPath);
                
                if (mediaFiles.Count == 0)
                {
                    LastError = "所选文件夹中没有支持的媒体文件";
                    return (null, new List<MediaFile>(), new List<string>());
                }

                // System.Diagnostics.Debug.WriteLine($"📊 找到 {mediaFiles.Count} 个媒体文件");

                // 导入文件夹到数据库
                var folder = _dbManager.ImportFolder(folderPath, folderName);
                var context = _dbManager.GetDbContext();
                bool folderV2Enabled = _dbManager.IsFolderSystemV2Enabled();

                // 全局已存在路径（images.path 全库唯一）
                var existingPathSet = new HashSet<string>(
                    _dbManager.GetAllMediaPaths().Select(NormalizePath),
                    StringComparer.OrdinalIgnoreCase);

                // 过滤出新文件
                var normalizedMediaFiles = mediaFiles.Select(NormalizePath).ToList();
                var newFilePaths = normalizedMediaFiles.Where(f => !existingPathSet.Contains(f)).ToList();
                var existingFiles = normalizedMediaFiles.Where(f => existingPathSet.Contains(f)).ToList();
                var allFilesExisting = newFilePaths.Count == 0 && existingFiles.Count > 0;
#if DEBUG
                // if (allFilesExisting)
                // {
                //     System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} ImportFolder: 所有媒体文件都已存在，继续建立目录映射");
                // }
#endif

                // 批量添加新文件
                var newFiles = _dbManager.AddMediaFiles(newFilePaths, folder.Id);

                // 建立目录映射：新文件 + 已存在文件都映射到当前目录，支持父子目录重叠导入。
                var linksToAdd = new List<FolderImage>();
                if (folderV2Enabled)
                {
                    var linkPaths = normalizedMediaFiles;
                    var imageIdMap = context.MediaFiles
                        .Where(m => linkPaths.Contains(m.Path))
                        .Select(m => new { m.Id, m.Path })
                        .ToList()
                        .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

                    var existingLinks = context.FolderImages
                        .Where(fi => fi.FolderId == folder.Id)
                        .Select(fi => fi.ImageId)
                        .ToHashSet();

                    int orderIndex = context.FolderImages
                        .Where(fi => fi.FolderId == folder.Id)
                        .Max(fi => (int?)fi.OrderIndex) ?? 0;

                    foreach (var path in normalizedMediaFiles)
                    {
                        if (!imageIdMap.TryGetValue(path, out var imageId))
                        {
                            continue;
                        }

                        if (existingLinks.Contains(imageId))
                        {
                            continue;
                        }

                        linksToAdd.Add(new FolderImage
                        {
                            FolderId = folder.Id,
                            ImageId = imageId,
                            OrderIndex = ++orderIndex,
                            RelativePath = GetRelativePathSafe(folder.Path, path),
                            DiscoveredAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            IsHidden = false
                        });
                        existingLinks.Add(imageId);
                    }

                    if (linksToAdd.Count > 0)
                    {
                        context.FolderImages.AddRange(linksToAdd);
                        context.SaveChanges();
                    }
                }

                // System.Diagnostics.Debug.WriteLine($"✅ 导入完成: 新增 {newFiles.Count} 个文件，已存在 {existingFiles.Count} 个文件");
#if DEBUG
                // System.Diagnostics.Trace.WriteLine(
                //     $"[ImportManager] op={operationId} ImportFolder 完成: FolderId={folder.Id}, NewFiles={newFiles.Count}, ExistingFiles={existingFiles.Count}, NewLinks={linksToAdd.Count}, AllExisting={allFilesExisting}");
#endif

                return (folder, newFiles, existingFiles);
            }
            catch (Exception ex)
            {
                LastError = $"导入文件夹失败: {ex.Message}";
#if DEBUG
                // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} ImportFolder 异常: {ex.Message}");
                // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} ImportFolder 堆栈: {ex.StackTrace}");
#endif
                return (null, null, null);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        /// <summary>
        /// 递归扫描文件夹中的所有媒体文件
        /// </summary>
        /// <param name="folderPath">文件夹路径</param>
        /// <returns>媒体文件路径列表</returns>
        private List<string> ScanMediaFilesRecursively(string folderPath)
        {
            var mediaFiles = new List<string>();

            try
            {
                // 使用Directory.EnumerateFiles递归搜索
                // SearchOption.AllDirectories 等同于 Python 的 rglob("*")
                foreach (var extension in AllExtensions)
                {
                    var pattern = $"*{extension}";
                    var files = Directory.EnumerateFiles(folderPath, pattern, SearchOption.AllDirectories);
                    mediaFiles.AddRange(files);
                }

                // 使用智能排序（支持中文拼音和数字混合排序）
                // 优化：只计算一次排序键，避免重复计算
                var filesWithKeys = mediaFiles
                    .Select(f => new { File = f, SortKey = _sortManager.GetSortKey(f) })
                    .OrderBy(x => x.SortKey.prefixNumber)
                    .ThenBy(x => x.SortKey.pinyinPart)
                    .ThenBy(x => x.SortKey.suffixNumber)
                    .Select(x => x.File)
                    .ToList();
                
                mediaFiles = filesWithKeys;
            }
            catch (UnauthorizedAccessException)
            {
                //System.Diagnostics.Debug.WriteLine($"⚠️ 无权访问某些子目录");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"扫描文件失败");
            }

            return mediaFiles;
        }

        /// <summary>
        /// 同步文件夹（检查新增和删除的文件）
        /// </summary>
        public (int added, int removed, int updated) SyncFolder(int folderId)
        {
            WriteLock.Wait();
            var operationId = Guid.NewGuid().ToString("N");
#if DEBUG
            // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} SyncFolder 开始: FolderId={folderId}");
#endif
            try
            {
                var folders = _dbManager.GetAllFolders();
                var folder = folders.FirstOrDefault(f => f.Id == folderId);
                
                if (folder == null || !Directory.Exists(folder.Path))
                {
                    //System.Diagnostics.Debug.WriteLine($"⚠️ 文件夹不存在: ID={folderId}");
                    return (0, 0, 0);
                }

                // 扫描当前文件系统中的文件
                var currentFiles = ScanMediaFilesRecursively(folder.Path)
                    .Select(NormalizePath)
                    .ToList();
                var currentFileSet = new HashSet<string>(currentFiles, StringComparer.OrdinalIgnoreCase);

                // 获取数据库中的文件
                var dbFiles = _dbManager.GetMediaFilesByFolder(folderId);
                var globalExistingPathSet = new HashSet<string>(
                    _dbManager.GetAllMediaPaths().Select(NormalizePath),
                    StringComparer.OrdinalIgnoreCase);

                // 计算新增的文件
                var newFiles = currentFiles
                    .Where(f => !globalExistingPathSet.Contains(f))
                    .ToList();
                
                // 计算已删除的文件
                var deletedFiles = dbFiles.Where(f => !currentFileSet.Contains(NormalizePath(f.Path))).ToList();

                // 添加新文件
                var context = _dbManager.GetDbContext();
                if (newFiles.Count > 0)
                {
                    _dbManager.AddMediaFiles(newFiles, folderId);
                }

                bool folderV2Enabled = _dbManager.IsFolderSystemV2Enabled();
                // 映射新增：包括“原库已有但未映射到该目录”的路径
                var mappedPathSet = new HashSet<string>(dbFiles.Select(f => NormalizePath(f.Path)), StringComparer.OrdinalIgnoreCase);
                var pathsNeedingLink = currentFiles.Where(p => !mappedPathSet.Contains(p)).ToList();
                if (folderV2Enabled && pathsNeedingLink.Count > 0)
                {
                    var imageMap = context.MediaFiles
                        .Where(m => pathsNeedingLink.Contains(m.Path))
                        .Select(m => new { m.Id, m.Path })
                        .ToList()
                        .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

                    var existingLinks = context.FolderImages
                        .Where(fi => fi.FolderId == folderId)
                        .Select(fi => fi.ImageId)
                        .ToHashSet();

                    int orderIndex = context.FolderImages
                        .Where(fi => fi.FolderId == folderId)
                        .Max(fi => (int?)fi.OrderIndex) ?? 0;

                    var linksToAdd = new List<FolderImage>();
                    foreach (var path in pathsNeedingLink)
                    {
                        if (!imageMap.TryGetValue(path, out var imageId) || existingLinks.Contains(imageId))
                        {
                            continue;
                        }

                        linksToAdd.Add(new FolderImage
                        {
                            FolderId = folderId,
                            ImageId = imageId,
                            OrderIndex = ++orderIndex,
                            RelativePath = GetRelativePathSafe(folder.Path, path),
                            DiscoveredAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            IsHidden = false
                        });
                        existingLinks.Add(imageId);
                    }

                    if (linksToAdd.Count > 0)
                    {
                        context.FolderImages.AddRange(linksToAdd);
                        context.SaveChanges();
                    }
                }

                // 删除不存在的仅为“该目录映射”，素材主数据由引用关系决定
                if (folderV2Enabled && deletedFiles.Count > 0)
                {
                    var deletedIds = deletedFiles.Select(f => f.Id).Distinct().ToList();
                    var deleteLinks = context.FolderImages
                        .Where(fi => fi.FolderId == folderId && deletedIds.Contains(fi.ImageId))
                        .ToList();
                    if (deleteLinks.Count > 0)
                    {
                        context.FolderImages.RemoveRange(deleteLinks);
                        context.SaveChanges();
                    }
                }

                // 🔑 关键修复：同步后重新应用排序规则
                if (newFiles.Count > 0 || deletedFiles.Count > 0)
                {
                    ReapplySortRuleForFolder(folderId);
                }

                //System.Diagnostics.Debug.WriteLine($"🔄 同步完成: 新增 {newFiles.Count}, 删除 {deletedFiles.Count}");
#if DEBUG
                // System.Diagnostics.Trace.WriteLine(
                //     $"[ImportManager] op={operationId} SyncFolder 完成: FolderId={folderId}, added={newFiles.Count}, removed={deletedFiles.Count}");
#endif
                
                return (newFiles.Count, deletedFiles.Count, 0);
            }
            catch (Exception)
            {
#if DEBUG
                // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} SyncFolder 异常: {ex.Message}");
                // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} SyncFolder 堆栈: {ex.StackTrace}");
#endif
                return (0, 0, 0);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        /// <summary>
        /// 为指定文件夹重新应用排序规则
        /// </summary>
        private void ReapplySortRuleForFolder(int folderId)
        {
            try
            {
                // 🔑 关键：检查文件夹是否为手动排序，如果是则跳过自动排序
                if (_dbManager.IsManualSortFolder(folderId))
                {
                    //System.Diagnostics.Debug.WriteLine($"⏭️ 跳过手动排序文件夹 {folderId} 的自动排序");
                    return;
                }

                // 获取文件夹中的所有文件
                var files = _dbManager.GetMediaFilesByFolder(folderId);
                if (files.Count == 0) return;

                // 使用SortManager的排序键对文件进行排序
                var sortedFiles = files
                    .Select(f => new
                    {
                        File = f,
                        SortKey = _sortManager.GetSortKey(f.Name + Path.GetExtension(f.Path))
                    })
                    .OrderBy(x => x.SortKey.prefixNumber)
                    .ThenBy(x => x.SortKey.pinyinPart)
                    .ThenBy(x => x.SortKey.suffixNumber)
                    .Select(x => x.File)
                    .ToList();

                // 更新OrderIndex
                for (int i = 0; i < sortedFiles.Count; i++)
                {
                    sortedFiles[i].OrderIndex = i + 1;
                }

                // 使用DatabaseManager的UpdateMediaFilesOrder方法保存更改
                _dbManager.UpdateMediaFilesOrder(sortedFiles);

                //System.Diagnostics.Debug.WriteLine($"✅ 已为文件夹 {folderId} 重新应用排序规则，共 {sortedFiles.Count} 个文件");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"重新应用排序规则失败: {ex}");
            }
        }

        /// <summary>
        /// 同步所有文件夹
        /// </summary>
        public (int added, int removed, int updated) SyncAllFolders()
        {
            int totalAdded = 0;
            int totalRemoved = 0;
            int totalUpdated = 0;
            var operationId = Guid.NewGuid().ToString("N");

#if DEBUG
            // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} SyncAllFolders 开始");
#endif
            try
            {
                var folders = _dbManager.GetAllFolders();

                foreach (var folder in folders)
                {
                    var (added, removed, updated) = SyncFolder(folder.Id);
                    totalAdded += added;
                    totalRemoved += removed;
                    totalUpdated += updated;
                }

                //System.Diagnostics.Debug.WriteLine($"🔄 全部同步完成: 新增 {totalAdded}, 删除 {totalRemoved}");
#if DEBUG
                // System.Diagnostics.Trace.WriteLine(
                //     $"[ImportManager] op={operationId} SyncAllFolders 完成: added={totalAdded}, removed={totalRemoved}, updated={totalUpdated}");
#endif
            }
            catch (Exception)
            {
#if DEBUG
                // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} SyncAllFolders 异常: {ex.Message}");
                // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} SyncAllFolders 堆栈: {ex.StackTrace}");
#endif
            }

            return (totalAdded, totalRemoved, totalUpdated);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static string GetRelativePathSafe(string rootPath, string fullPath)
        {
            try
            {
                return Path.GetRelativePath(rootPath, fullPath);
            }
            catch
            {
                return fullPath;
            }
        }

        /// <summary>
        /// 获取文件对话框过滤器（用于打开文件对话框）
        /// </summary>
        public static string GetFileDialogFilter()
        {
            return "所有媒体|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.f4v;*.rm;*.rmvb;*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac|" +
                   "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif|" +
                   "视频文件|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.f4v;*.rm;*.rmvb|" +
                   "音频文件|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac|" +
                   "所有文件|*.*";
        }
    }
}

