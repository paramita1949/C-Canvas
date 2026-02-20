using System;
using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Database.Repositories
{
    public sealed class FolderRepository : IFolderRepository
    {
        private readonly CanvasDbContext _context;

        public FolderRepository(CanvasDbContext context)
        {
            _context = context;
        }

        public Folder ImportFolder(string folderPath, string folderName = null)
        {
            folderPath = NormalizePath(folderPath);
            folderName ??= System.IO.Path.GetFileName(folderPath);
            var normalizedPath = NormalizeNormalizedPath(folderPath);
            var existingFolder = _context.Folders.FirstOrDefault(f =>
                f.Path == folderPath ||
                (!string.IsNullOrEmpty(f.NormalizedPath) && f.NormalizedPath == normalizedPath));
            if (existingFolder != null)
            {
                return existingFolder;
            }

            var maxOrder = _context.Folders.Max(f => (int?)f.OrderIndex) ?? -1;
            var folder = new Folder
            {
                Name = folderName,
                Path = folderPath,
                NormalizedPath = normalizedPath,
                OrderIndex = maxOrder + 1,
                CreatedTime = DateTime.Now,
                ScanPolicy = "full",
                LastScanTime = DateTime.Now,
                LastScanStatus = "success"
            };

            _context.Folders.Add(folder);
            _context.SaveChanges();
            return folder;
        }

        public List<Folder> GetAllFolders()
        {
            return _context.Folders
                .OrderBy(f => f.OrderIndex)
                .Include(f => f.MediaFiles)
                .ToList();
        }

        public void DeleteFolder(int folderId, bool forceDelete = false)
        {
            var operationId = Guid.NewGuid().ToString("N");
            var folder = _context.Folders.Find(folderId);
            if (folder == null)
            {
#if DEBUG
                // System.Diagnostics.Trace.WriteLine($"[DeleteFolder] op={operationId} 文件夹不存在: FolderId={folderId}");
#endif
                return;
            }

#if DEBUG
            // System.Diagnostics.Trace.WriteLine(
            //     $"[DeleteFolder] op={operationId} 开始: FolderId={folderId}, Name={folder.Name}, forceDelete={forceDelete}");
#endif
            try
            {
                // 先记录该目录当前映射素材，用于后续判断孤儿素材是否可清理。
                var mappedImageIds = _context.FolderImages
                    .Where(x => x.FolderId == folderId)
                    .Select(x => x.ImageId)
                    .Distinct()
                    .ToList();

                if (mappedImageIds.Count == 0)
                {
                    // 兼容旧数据：无映射时回退到旧 folder_id 关系。
                    mappedImageIds = _context.MediaFiles
                        .Where(f => f.FolderId == folderId)
                        .Select(f => f.Id)
                        .ToList();
                }

#if DEBUG
                // System.Diagnostics.Trace.WriteLine($"[DeleteFolder] op={operationId} 关联素材数: {mappedImageIds.Count}");
#endif

                // 删该目录映射关系
                var links = _context.FolderImages.Where(x => x.FolderId == folderId).ToList();
                if (links.Count > 0)
                {
                    _context.FolderImages.RemoveRange(links);
                }

                // 删除目录（images.folder_id 设置为 null 由 FK 策略处理）
                _context.Folders.Remove(folder);
                _context.SaveChanges();

                // 按需清理“仅属于该目录且无业务引用”的孤儿素材。
                if (mappedImageIds.Count > 0)
                {
                    DeleteOrphanImages(mappedImageIds, forceDelete, operationId);
                }

#if DEBUG
                // System.Diagnostics.Trace.WriteLine($"[DeleteFolder] op={operationId} v2删除完成");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                // System.Diagnostics.Trace.WriteLine($"[DeleteFolder] op={operationId} 异常: {ex.Message}");
                // System.Diagnostics.Trace.WriteLine($"[DeleteFolder] op={operationId} 堆栈: {ex.StackTrace}");
#else
                _ = ex;
#endif
                throw;
            }
        }

        private void DeleteOrphanImages(List<int> candidateImageIds, bool forceDelete, string operationId)
        {
            var linkedByOtherFolders = _context.FolderImages
                .Where(x => candidateImageIds.Contains(x.ImageId))
                .Select(x => x.ImageId)
                .Distinct()
                .ToHashSet();

            var orphanIds = candidateImageIds
                .Where(id => !linkedByOtherFolders.Contains(id))
                .Distinct()
                .ToList();

            if (orphanIds.Count == 0)
            {
                return;
            }

            var hasReferences = _context.Keyframes.Where(k => orphanIds.Contains(k.ImageId)).Select(k => k.ImageId).Distinct()
                .Concat(_context.KeyframeTimings.Where(t => orphanIds.Contains(t.ImageId)).Select(t => t.ImageId).Distinct())
                .Concat(_context.ImageDisplayLocations.Where(l => orphanIds.Contains(l.ImageId)).Select(l => l.ImageId).Distinct())
                .Concat(_context.CompositeScripts.Where(s => orphanIds.Contains(s.ImageId)).Select(s => s.ImageId).Distinct())
                .Concat(_context.OriginalMarks.Where(m => m.ItemTypeString == "image" && orphanIds.Contains(m.ItemId)).Select(m => m.ItemId).Distinct())
                .Concat(_context.LyricsProjects.Where(lp => lp.ImageId.HasValue && orphanIds.Contains(lp.ImageId.Value)).Select(lp => lp.ImageId.Value).Distinct())
                .Distinct()
                .ToHashSet();

            var safeToDelete = forceDelete
                ? orphanIds
                : orphanIds.Where(id => !hasReferences.Contains(id)).ToList();

            if (safeToDelete.Count == 0)
            {
                return;
            }

#if DEBUG
            // System.Diagnostics.Trace.WriteLine($"[DeleteFolder] op={operationId} 清理孤儿素材: {safeToDelete.Count}");
#endif
            var images = _context.MediaFiles.Where(f => safeToDelete.Contains(f.Id)).ToList();
            _context.MediaFiles.RemoveRange(images);
            _context.SaveChanges();
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return System.IO.Path.GetFullPath(path);
            }
            catch
            {
                return path ?? string.Empty;
            }
        }

        private static string NormalizeNormalizedPath(string path)
        {
            var full = NormalizePath(path);
            return full.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }
        

        public void UpdateFoldersOrder(List<Folder> folders)
        {
            if (folders == null || folders.Count == 0)
            {
                return;
            }

            _context.SaveChanges();
        }

        public bool IsManualSortFolder(int folderId)
        {
            var manualSort = _context.ManualSortFolders.FirstOrDefault(m => m.FolderId == folderId);
            return manualSort != null && manualSort.IsManualSort;
        }

        public void MarkFolderAsManualSort(int folderId)
        {
            var manualSort = _context.ManualSortFolders.FirstOrDefault(m => m.FolderId == folderId);
            if (manualSort == null)
            {
                manualSort = new ManualSortFolder
                {
                    FolderId = folderId,
                    IsManualSort = true,
                    LastManualSortTime = DateTime.Now
                };
                _context.ManualSortFolders.Add(manualSort);
            }
            else
            {
                manualSort.IsManualSort = true;
                manualSort.LastManualSortTime = DateTime.Now;
            }

            _context.SaveChanges();
        }

        public void UnmarkFolderAsManualSort(int folderId)
        {
            var manualSort = _context.ManualSortFolders.FirstOrDefault(m => m.FolderId == folderId);
            if (manualSort != null)
            {
                _context.ManualSortFolders.Remove(manualSort);
                _context.SaveChanges();
            }
        }

        public List<int> GetManualSortFolderIds()
        {
            return _context.ManualSortFolders
                .Where(m => m.IsManualSort)
                .Select(m => m.FolderId)
                .ToList();
        }

        public void MarkFolderAutoColorEffect(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            if (folder != null)
            {
                folder.AutoColorEffect = 1;
                _context.SaveChanges();
            }
        }

        public void UnmarkFolderAutoColorEffect(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            if (folder != null)
            {
                folder.AutoColorEffect = null;
                _context.SaveChanges();
            }
        }

        public bool HasFolderAutoColorEffect(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            return folder?.AutoColorEffect == 1;
        }

        public void SetFolderVideoPlayMode(int folderId, string playMode)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            if (folder != null)
            {
                folder.VideoPlayMode = playMode;
                _context.SaveChanges();
            }
        }

        public string GetFolderVideoPlayMode(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            return folder?.VideoPlayMode;
        }

        public void ClearFolderVideoPlayMode(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            if (folder != null)
            {
                folder.VideoPlayMode = null;
                _context.SaveChanges();
            }
        }

        public void SetFolderHighlightColor(int folderId, string color)
        {
            var folder = _context.Folders.Find(folderId);
            if (folder != null)
            {
                folder.HighlightColor = color;
                _context.SaveChanges();
            }
        }

        public string GetFolderHighlightColor(int folderId)
        {
            return _context.Folders.Find(folderId)?.HighlightColor;
        }
    }
}
