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
                return;
            }

            if (forceDelete)
            {
                try
                {
                    var fileIds = _context.MediaFiles
                        .Where(f => f.FolderId == folderId)
                        .Select(f => f.Id)
                        .ToList();

                    _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

                    using var transaction = _context.Database.BeginTransaction();
                    try
                    {
                        if (fileIds.Count > 0)
                        {
                            string fileIdList = string.Join(",", fileIds);
                            _context.Database.ExecuteSqlRaw("DELETE FROM keyframe_timings WHERE image_id IN ({0})", fileIdList);
                            _context.Database.ExecuteSqlRaw("DELETE FROM keyframes WHERE image_id IN ({0})", fileIdList);
                            _context.Database.ExecuteSqlRaw("DELETE FROM image_display_locations WHERE image_id IN ({0})", fileIdList);
                            _context.Database.ExecuteSqlRaw("DELETE FROM composite_scripts WHERE image_id IN ({0})", fileIdList);
                            _context.Database.ExecuteSqlRaw("DELETE FROM original_marks WHERE item_type = 'image' AND item_id IN ({0})", fileIdList);
                        }

                        _context.Database.ExecuteSqlRaw("DELETE FROM images WHERE folder_id = {0}", folderId);
                        _context.Database.ExecuteSqlRaw("DELETE FROM folders WHERE id = {0}", folderId);
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                    finally
                    {
                        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
                    }
                }
                catch
                {
                    try
                    {
                        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
                    }
                    catch
                    {
                    }

                    throw;
                }

                return;
            }

            using var tx = _context.Database.BeginTransaction();
            try
            {
                var fileIds = _context.MediaFiles
                    .Where(f => f.FolderId == folderId)
                    .Select(f => f.Id)
                    .ToList();

                if (fileIds.Count > 0)
                {
                    var timings = _context.KeyframeTimings.Where(t => fileIds.Contains(t.ImageId)).ToList();
                    if (timings.Count > 0) _context.KeyframeTimings.RemoveRange(timings);

                    var keyframes = _context.Keyframes.Where(k => fileIds.Contains(k.ImageId)).ToList();
                    if (keyframes.Count > 0) _context.Keyframes.RemoveRange(keyframes);

                    var displayLocations = _context.ImageDisplayLocations.Where(l => fileIds.Contains(l.ImageId)).ToList();
                    if (displayLocations.Count > 0) _context.ImageDisplayLocations.RemoveRange(displayLocations);

                    var compositeScripts = _context.CompositeScripts.Where(s => fileIds.Contains(s.ImageId)).ToList();
                    if (compositeScripts.Count > 0) _context.CompositeScripts.RemoveRange(compositeScripts);

                    var originalMarks = _context.OriginalMarks
                        .Where(m => m.ItemTypeString == "image" && fileIds.Contains(m.ItemId))
                        .ToList();
                    if (originalMarks.Count > 0) _context.OriginalMarks.RemoveRange(originalMarks);

                    var files = _context.MediaFiles.Where(f => fileIds.Contains(f.Id)).ToList();
                    _context.MediaFiles.RemoveRange(files);
                }

                _context.Folders.Remove(folder);
                _context.SaveChanges();
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
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
