using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Utils;
using ImageColorChanger.Services.OnlineScores;
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
        public static readonly string[] ImageExtensions = ImageFileSupport.ImageExtensions;

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
        public static readonly string[] AllExtensions = ImageExtensions.Concat(VideoExtensions).Concat(AudioExtensions).Append(StructuredScoreDocument.Extension).ToArray();
        private static readonly HashSet<string> AllExtensionsSet = new HashSet<string>(AllExtensions, StringComparer.OrdinalIgnoreCase);

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
            => ImportSingleFileCore(filePath, null);

        public MediaFile ImportSingleFileIntoFolder(string filePath, int folderId)
            => ImportSingleFileCore(filePath, folderId);

        private MediaFile ImportSingleFileCore(string filePath, int? folderId)
        {
            LastError = null;
            WriteLock.Wait();
            var operationId = Guid.NewGuid().ToString("N");
#if DEBUG
            // System.Diagnostics.Trace.WriteLine($"[ImportManager] op={operationId} ImportSingleFile 开始: {filePath}");
#endif
            try
            {
                if (string.Equals(Path.GetExtension(filePath), StructuredScoreDocument.Extension, StringComparison.OrdinalIgnoreCase))
                    StructuredScoreDocument.Read(filePath); // 只校验容器，保留原件路径；打开时才排版。
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

                // 单文件导入保持根目录；文件夹内新建使用现有文件夹索引。
                var mediaFile = _dbManager.AddMediaFile(filePath, folderId);

                if (mediaFile != null)
                {
                    //System.Diagnostics.Debug.WriteLine($" 成功导入文件: {mediaFile.Name}");
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
                // System.Diagnostics.Debug.WriteLine($" 开始导入文件夹: {folderName}");

                // 递归扫描所有支持的媒体文件
                var mediaFiles = ScanMediaFilesRecursively(folderPath, sortForImport: true);
                
                if (mediaFiles.Count == 0)
                {
                    LastError = "所选文件夹中没有支持的媒体文件";
                    return (null, new List<MediaFile>(), new List<string>());
                }

                // System.Diagnostics.Debug.WriteLine($" 找到 {mediaFiles.Count} 个媒体文件");

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

                // System.Diagnostics.Debug.WriteLine($" 导入完成: 新增 {newFiles.Count} 个文件，已存在 {existingFiles.Count} 个文件");
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
        private List<string> ScanMediaFilesRecursively(string folderPath, bool sortForImport)
        {
            var mediaFiles = new List<string>();
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(folderPath);

            try
            {
                while (pendingDirectories.Count > 0)
                {
                    string currentDirectory = pendingDirectories.Pop();

                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(currentDirectory, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            var extension = Path.GetExtension(file);
                            if (!string.IsNullOrWhiteSpace(extension) && AllExtensionsSet.Contains(extension))
                            {
                                if (extension.Equals(StructuredScoreDocument.Extension, StringComparison.OrdinalIgnoreCase))
                                {
                                    try { StructuredScoreDocument.Read(file); mediaFiles.Add(file); }
                                    catch (Exception ex) { LastError = $"结构谱未导入：{Path.GetFileName(file)}；{ex.Message}"; }
                                }
                                else mediaFiles.Add(file);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }
                    catch (PathTooLongException)
                    {
                        continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    try
                    {
                        foreach (var subDir in Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                var attributes = File.GetAttributes(subDir);
                                if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                                {
                                    // 跳过符号链接/挂载点，防止循环扫描。
                                    continue;
                                }
                            }
                            catch
                            {
                                // 无法读取属性时继续尝试扫描该目录。
                            }

                            pendingDirectories.Push(subDir);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }
                    catch (PathTooLongException)
                    {
                        continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                }

                mediaFiles = mediaFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (sortForImport)
                {
                    // 导入阶段需要稳定排序；同步阶段禁用排序可显著降低冷启动扫描成本。
                    mediaFiles = mediaFiles
                        .Select(f => new { File = f, SortKey = _sortManager.GetSortKey(f) })
                        .OrderBy(x => x.SortKey.prefixNumber)
                        .ThenBy(x => x.SortKey.pinyinPart)
                        .ThenBy(x => x.SortKey.suffixNumber)
                        .ThenBy(x => x.SortKey.stableKey)
                        .Select(x => x.File)
                        .ToList();
                }
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
                var globalExistingPathSet = new HashSet<string>(
                    _dbManager.GetAllMediaPaths().Select(NormalizePath),
                    StringComparer.OrdinalIgnoreCase);

                var result = SyncFolderCore(folder, globalExistingPathSet);
#if DEBUG
                // System.Diagnostics.Trace.WriteLine(
                //     $"[ImportManager] op={operationId} SyncFolder 完成: FolderId={folderId}, added={result.added}, removed={result.removed}, updated={result.updated}");
#endif
                return result;
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
        private bool ReapplySortRuleForFolder(int folderId)
        {
            try
            {
                //  关键：检查文件夹是否为手动排序，如果是则跳过自动排序
                if (_dbManager.IsManualSortFolder(folderId))
                {
                    //System.Diagnostics.Debug.WriteLine($" 跳过手动排序文件夹 {folderId} 的自动排序");
                    return false;
                }

                // 获取文件夹中的所有文件
                var files = _dbManager.GetMediaFilesByFolder(folderId);
                if (files.Count == 0) return false;

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
                    .ThenBy(x => x.SortKey.stableKey)
                    .Select(x => x.File)
                    .ToList();

                return _dbManager.UpdateFolderMediaFilesOrder(folderId, sortedFiles);
            }
            catch (Exception ex)
            {
                LastError = $"更新文件夹排序失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 同步所有文件夹
        /// </summary>
        public (int added, int removed, int updated) SyncAllFolders()
        {
            return SyncAllFolders(includeAdditions: true);
        }

        /// <summary>
        /// 仅同步删除项（不自动导入新增文件）
        /// </summary>
        public (int added, int removed, int updated) SyncAllFoldersRemovalsOnly()
        {
            return SyncAllFolders(includeAdditions: false);
        }

        private (int added, int removed, int updated) SyncAllFolders(bool includeAdditions)
        {
            WriteLock.Wait();
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
                var globalExistingPathSet = new HashSet<string>(
                    _dbManager.GetAllMediaPaths().Select(NormalizePath),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var folder in folders)
                {
                    var (added, removed, updated) = SyncFolderCore(folder, globalExistingPathSet, includeAdditions);
                    totalAdded += added;
                    totalRemoved += removed;
                    totalUpdated += updated;
                }

                //System.Diagnostics.Debug.WriteLine($" 全部同步完成: 新增 {totalAdded}, 删除 {totalRemoved}");
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
            finally
            {
                WriteLock.Release();
            }

            return (totalAdded, totalRemoved, totalUpdated);
        }

        private (int added, int removed, int updated) SyncFolderCore(
            Folder folder,
            HashSet<string> globalExistingPathSet,
            bool includeAdditions = true)
        {
            if (folder == null)
            {
                return (0, 0, 0);
            }

            var context = _dbManager.GetDbContext();
            if (!Directory.Exists(folder.Path))
            {
                var staleFiles = _dbManager.GetMediaFilesByFolder(folder.Id);
                int removedWhenFolderMissing = CleanupDeletedEntriesForFolder(folder, staleFiles, context);
                if (removedWhenFolderMissing > 0)
                {
                    ReapplySortRuleForFolder(folder.Id);
                }

                return (0, removedWhenFolderMissing, RemoveEmptyOnlineFolder(folder, context));
            }

            // 同步阶段优先速度，不做拼音排序。
            var currentFiles = ScanMediaFilesRecursively(folder.Path, sortForImport: false)
                .Select(NormalizePath)
                .ToList();
            var currentFileSet = new HashSet<string>(currentFiles, StringComparer.OrdinalIgnoreCase);

            var dbFiles = _dbManager.GetMediaFilesByFolder(folder.Id);

            var newFiles = includeAdditions
                ? currentFiles.Where(f => !globalExistingPathSet.Contains(f)).ToList()
                : new List<string>();

            var deletedFiles = dbFiles
                .Where(f => !currentFileSet.Contains(NormalizePath(f.Path)))
                .ToList();

            if (includeAdditions && newFiles.Count > 0)
            {
                _dbManager.AddMediaFiles(newFiles, folder.Id);
                foreach (var file in newFiles)
                {
                    globalExistingPathSet.Add(file);
                }
            }

            bool folderV2Enabled = _dbManager.IsFolderSystemV2Enabled();
            var mappedPathSet = new HashSet<string>(dbFiles.Select(f => NormalizePath(f.Path)), StringComparer.OrdinalIgnoreCase);
            var pathsNeedingLink = includeAdditions
                ? currentFiles.Where(p => !mappedPathSet.Contains(p)).ToList()
                : new List<string>();
            if (includeAdditions && folderV2Enabled && pathsNeedingLink.Count > 0)
            {
                var imageMap = context.MediaFiles
                    .Where(m => pathsNeedingLink.Contains(m.Path))
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

                var linksToAdd = new List<FolderImage>();
                foreach (var path in pathsNeedingLink)
                {
                    if (!imageMap.TryGetValue(path, out var imageId) || existingLinks.Contains(imageId))
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

            int removedCount = CleanupDeletedEntriesForFolder(folder, deletedFiles, context);

            // 旧码谱目录可能保存过文字顺序。正常同步即使无增删也修复它，
            // 删除专用后台扫描保持轻量，不额外排序；手动目录仍由统一入口跳过。
            bool sortUpdated = false;
            if (newFiles.Count > 0 || removedCount > 0 ||
                includeAdditions && dbFiles.Any(file => StructuredScoreDocument.IsScoreFile(file.Path)))
            {
                sortUpdated = ReapplySortRuleForFolder(folder.Id);
            }

            return (newFiles.Count, removedCount, RemoveEmptyOnlineFolder(folder, context) + (sortUpdated ? 1 : 0));
        }

        // 顶部同步、目录右键同步与自动删除同步共用此边界。
        // 只清理已配置在线歌本的空导航记录；物理目录及歌本路径设置保留，后续下载可重新登记。
        private int RemoveEmptyOnlineFolder(Folder folder, CanvasDbContext context)
        {
            if (!new OnlineScoreStorageSettings(_dbManager).IsLibraryDirectory(folder.Path) ||
                context.MediaFiles.Any(m => m.FolderId == folder.Id) ||
                context.FolderImages.Any(link => link.FolderId == folder.Id)) return 0;

            var stored = context.Folders.SingleOrDefault(f => f.Id == folder.Id);
            if (stored == null) return 0;
            context.Folders.Remove(stored);
            context.SaveChanges();
            return 1; // 即使图片早已删除，也通知自动同步刷新目录和搜索范围。
        }

        private int CleanupDeletedEntriesForFolder(Folder folder, List<MediaFile> deletedFiles, CanvasDbContext context)
        {
            if (folder == null || deletedFiles == null || deletedFiles.Count == 0 || context == null)
            {
                return 0;
            }

            bool folderV2Enabled = _dbManager.IsFolderSystemV2Enabled();
            var deletedIds = deletedFiles.Select(f => f.Id).Distinct().ToList();

            if (folderV2Enabled && deletedIds.Count > 0)
            {
                var deleteLinks = context.FolderImages
                    .Where(fi => fi.FolderId == folder.Id && deletedIds.Contains(fi.ImageId))
                    .ToList();
                if (deleteLinks.Count > 0)
                {
                    context.FolderImages.RemoveRange(deleteLinks);
                    context.SaveChanges();
                }
            }

            foreach (var deletedFile in deletedFiles)
            {
                var normalizedPath = NormalizePath(deletedFile.Path);
                bool existsOnDisk = File.Exists(normalizedPath);
                if (!existsOnDisk)
                {
                    // 文件物理不存在时，直接删除媒体主记录及关联数据（单次同步即可生效）。
                    _dbManager.DeleteMediaFile(deletedFile.Id);
                    continue;
                }

                if (folderV2Enabled)
                {
                    bool hasAnyFolderLink = context.FolderImages.Any(fi => fi.ImageId == deletedFile.Id);
                    if (!hasAnyFolderLink)
                    {
                        var media = context.MediaFiles.FirstOrDefault(m => m.Id == deletedFile.Id);
                        if (media != null && media.FolderId == folder.Id)
                        {
                            media.FolderId = null;
                            context.SaveChanges();
                        }
                    }
                }
            }

            return deletedFiles.Count;
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
            return $"所有媒体|{ImageFileSupport.ImageDialogPattern};*.cscore;*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.f4v;*.rm;*.rmvb;*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac|" +
                   "结构化歌谱|*.cscore|" +
                   $"{ImageFileSupport.BuildImageDialogFilter()}|" +
                   "视频文件|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.f4v;*.rm;*.rmvb|" +
                   "音频文件|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac|" +
                   "所有文件|*.*";
        }
    }
}



