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
            folderName ??= System.IO.Path.GetFileName(folderPath);
            var existingFolder = _context.Folders.FirstOrDefault(f => f.Path == folderPath);
            if (existingFolder != null)
            {
                return existingFolder;
            }

            var maxOrder = _context.Folders.Max(f => (int?)f.OrderIndex) ?? -1;
            var folder = new Folder
            {
                Name = folderName,
                Path = folderPath,
                OrderIndex = maxOrder + 1,
                CreatedTime = DateTime.Now
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
            var folder = _context.Folders.Find(folderId);
            if (folder == null)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"[DeleteFolder] 文件夹不存在: FolderId={folderId}");
#endif
                return;
            }

            bool foreignKeysDisabled = false;

#if DEBUG
            System.Diagnostics.Trace.WriteLine(
                $"[DeleteFolder] 开始: FolderId={folderId}, Name={folder.Name}, forceDelete={forceDelete}");
#endif
            try
            {
                if (forceDelete)
                {
                    _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
                    foreignKeysDisabled = true;
#if DEBUG
                    System.Diagnostics.Trace.WriteLine("[DeleteFolder] forceDelete: foreign_keys=OFF");
#endif

                    _context.Database.ExecuteSqlRaw(
                        "UPDATE lyrics_projects SET image_id = NULL WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})",
                        folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM keyframe_timings WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM keyframes WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM image_display_locations WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM composite_scripts WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM original_marks WHERE item_type = 'image' AND item_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM images WHERE folder_id = {0}", folderId);
                    int folderDeleted = _context.Database.ExecuteSqlRaw("DELETE FROM folders WHERE id = {0}", folderId);
                    if (folderDeleted <= 0)
                    {
                        throw new InvalidOperationException($"删除文件夹失败：folders 表未删除到记录（FolderId={folderId}）");
                    }
#if DEBUG
                    System.Diagnostics.Trace.WriteLine("[DeleteFolder] forceDelete SQL执行完成");
                    System.Diagnostics.Trace.WriteLine($"[DeleteFolder] forceDelete 删除folders行数: {folderDeleted}");
#endif
                }
                else
                {
                    var fileIds = _context.MediaFiles
                        .Where(f => f.FolderId == folderId)
                        .Select(f => f.Id)
                        .ToList();

#if DEBUG
                    System.Diagnostics.Trace.WriteLine($"[DeleteFolder] 普通删除关联文件数: {fileIds.Count}");
#endif
                    // 避免 SaveChanges() 触发 EF 自动事务（该上下文在当前架构下可能与其他操作并发，导致 nested transaction）。
                    _context.Database.ExecuteSqlRaw(
                        "UPDATE lyrics_projects SET image_id = NULL WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})",
                        folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM keyframe_timings WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM keyframes WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM image_display_locations WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM composite_scripts WHERE image_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM original_marks WHERE item_type = 'image' AND item_id IN (SELECT id FROM images WHERE folder_id = {0})", folderId);
                    _context.Database.ExecuteSqlRaw("DELETE FROM images WHERE folder_id = {0}", folderId);
                    int folderDeleted = _context.Database.ExecuteSqlRaw("DELETE FROM folders WHERE id = {0}", folderId);
                    if (folderDeleted <= 0)
                    {
                        throw new InvalidOperationException($"删除文件夹失败：folders 表未删除到记录（FolderId={folderId}）");
                    }
#if DEBUG
                    System.Diagnostics.Trace.WriteLine("[DeleteFolder] 普通删除 SQL执行完成");
                    System.Diagnostics.Trace.WriteLine($"[DeleteFolder] 普通删除 删除folders行数: {folderDeleted}");
#endif
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"[DeleteFolder] 异常: {ex.Message}");
                System.Diagnostics.Trace.WriteLine($"[DeleteFolder] 堆栈: {ex.StackTrace}");
#else
                _ = ex;
#endif
                throw;
            }
            finally
            {
                if (foreignKeysDisabled)
                {
                    try
                    {
                        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
#if DEBUG
                        System.Diagnostics.Trace.WriteLine("[DeleteFolder] 已恢复 foreign_keys=ON");
#endif
                    }
                    catch
                    {
                    }
                }
            }
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
