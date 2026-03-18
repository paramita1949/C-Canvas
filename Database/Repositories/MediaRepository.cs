using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFCore.BulkExtensions;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Database.Repositories
{
    public sealed class MediaRepository : IMediaRepository
    {
        private readonly CanvasDbContext _context;

        public MediaRepository(CanvasDbContext context)
        {
            _context = context;
        }

        public MediaFile AddMediaFile(string filePath, int? folderId = null)
        {
            var existing = _context.MediaFiles.FirstOrDefault(m => m.Path == filePath);
            if (existing != null)
            {
                return existing;
            }

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath).ToLower();
            var fileType = GetFileType(extension);

            var maxOrder = _context.MediaFiles
                .Where(m => m.FolderId == folderId)
                .Max(m => (int?)m.OrderIndex) ?? -1;

            var mediaFile = new MediaFile
            {
                Name = fileName,
                Path = filePath,
                FolderId = folderId,
                OrderIndex = maxOrder + 1,
                FileType = fileType,
                LastModified = File.Exists(filePath)
                    ? File.GetLastWriteTime(filePath)
                    : DateTime.Now
            };

            _context.MediaFiles.Add(mediaFile);
            _context.SaveChanges();
            return mediaFile;
        }

        public List<MediaFile> AddMediaFiles(IEnumerable<string> filePaths, int? folderId = null)
        {
            var normalizedInputPaths = (filePaths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedInputPaths.Count == 0)
            {
                return new List<MediaFile>();
            }

            // 全局去重（images.path 全库唯一）
            var existingPaths = new HashSet<string>(
                _context.MediaFiles
                    .Where(m => normalizedInputPaths.Contains(m.Path))
                    .Select(m => m.Path)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

            var candidatePaths = normalizedInputPaths
                .Where(path => !existingPaths.Contains(path))
                .ToList();

            var mediaFiles = new List<MediaFile>();
            int orderIndex = folderId.HasValue
                ? _context.MediaFiles.Where(m => m.FolderId == folderId.Value).Max(m => (int?)m.OrderIndex) ?? 0
                : _context.MediaFiles.Where(m => m.FolderId == null).Max(m => (int?)m.OrderIndex) ?? 0;

            foreach (var filePath in candidatePaths)
            {
                var extension = Path.GetExtension(filePath).ToLower();
                var fileName = Path.GetFileNameWithoutExtension(filePath);

                var mediaFile = new MediaFile
                {
                    FolderId = folderId,
                    Path = filePath,
                    Name = fileName,
                    FileType = GetFileType(extension),
                    OrderIndex = ++orderIndex,
                    LastModified = File.Exists(filePath)
                        ? File.GetLastWriteTime(filePath)
                        : DateTime.Now
                };

                mediaFiles.Add(mediaFile);
            }

            if (mediaFiles.Count > 0)
            {
                _context.BulkInsert(mediaFiles, new BulkConfig
                {
                    SetOutputIdentity = true,
                    BatchSize = 5000
                });
            }

            return mediaFiles;
        }

        public List<MediaFile> GetMediaFilesByFolder(int folderId)
        {
            return GetMediaFilesByFolderCore(folderId, null);
        }

        public List<MediaFile> GetMediaFilesByFolder(int folderId, FileType? fileType = null)
        {
            return GetMediaFilesByFolderCore(folderId, fileType);
        }

        public List<MediaFile> GetRootMediaFiles()
        {
            return _context.MediaFiles
                .Where(m => m.FolderId == null &&
                            !_context.FolderImages.Any(fi => fi.ImageId == m.Id))
                .OrderBy(m => m.OrderIndex)
                .ToList();
        }

        public List<string> GetAllMediaPaths()
        {
            return _context.MediaFiles
                .Select(m => m.Path)
                .ToList();
        }

        public void DeleteMediaFile(int mediaFileId)
        {
            var mediaFile = _context.MediaFiles.Find(mediaFileId);
            if (mediaFile != null)
            {
                using var transaction = _context.Database.BeginTransaction();

                var originalMarks = _context.OriginalMarks
                    .Where(m => m.ItemId == mediaFileId && m.ItemTypeString != null && m.ItemTypeString.ToLower() == "image")
                    .ToList();
                if (originalMarks.Count > 0)
                {
                    _context.OriginalMarks.RemoveRange(originalMarks);
                }

                var originalModeTimings = _context.OriginalModeTimings
                    .Where(t => t.BaseImageId == mediaFileId || t.FromImageId == mediaFileId || t.ToImageId == mediaFileId)
                    .ToList();
                if (originalModeTimings.Count > 0)
                {
                    _context.OriginalModeTimings.RemoveRange(originalModeTimings);
                }

                _context.MediaFiles.Remove(mediaFile);
                _context.SaveChanges();
                transaction.Commit();
            }
        }

        public void UpdateMediaFilesOrder(List<MediaFile> mediaFiles)
        {
            if (mediaFiles == null || mediaFiles.Count == 0)
            {
                return;
            }

            _context.SaveChanges();
        }

        public MediaFile GetMediaFileById(int id)
        {
            return _context.MediaFiles.FirstOrDefault(m => m.Id == id);
        }

        public MediaFile GetNextMediaFile(int folderId, int? currentOrderIndex, FileType? fileType = null)
        {
            var allFiles = GetMediaFilesByFolderCore(folderId, fileType);
            if (allFiles.Count == 0)
            {
                return null;
            }

            var idx = allFiles.FindIndex(f => f.OrderIndex == currentOrderIndex);
            return idx >= 0 && idx + 1 < allFiles.Count ? allFiles[idx + 1] : allFiles.FirstOrDefault();
        }

        public MediaFile GetPreviousMediaFile(int folderId, int? currentOrderIndex, FileType? fileType = null)
        {
            var allFiles = GetMediaFilesByFolderCore(folderId, fileType);
            if (allFiles.Count == 0)
            {
                return null;
            }

            var idx = allFiles.FindIndex(f => f.OrderIndex == currentOrderIndex);
            return idx > 0 ? allFiles[idx - 1] : allFiles.LastOrDefault();
        }

        public List<MediaFile> SearchFiles(string searchTerm, FileType? fileType = null)
        {
            var allFiles = _context.MediaFiles
                .Include(f => f.Folder)
                .Where(f => f.Name.ToLower().Contains(searchTerm.ToLower()))
                .OrderBy(f => f.OrderIndex)
                .ToList();

            if (fileType.HasValue)
            {
                allFiles = allFiles.Where(f => f.FileType == fileType.Value).ToList();
            }

            return allFiles;
        }

        public List<MediaFile> SearchFilesInFolder(string searchTerm, int folderId, FileType? fileType = null)
        {
            var folderFiles = GetMediaFilesByFolderCore(folderId, fileType);
            return folderFiles
                .Where(f => f.Name.ToLower().Contains(searchTerm.ToLower()))
                .ToList();
        }

        private List<MediaFile> GetMediaFilesByFolderCore(int folderId, FileType? fileType)
        {
            if (!IsFolderSystemV2Enabled())
            {
                var legacy = _context.MediaFiles
                    .Where(m => m.FolderId == folderId)
                    .OrderBy(m => m.OrderIndex)
                    .ToList();
                if (fileType.HasValue)
                {
                    legacy = legacy.Where(m => m.FileType == fileType.Value).ToList();
                }

                return legacy;
            }

            // 优先采用 folder_images 映射，兼容旧数据时回退 folder_id。
            var mapped = _context.FolderImages
                .Where(fi => fi.FolderId == folderId)
                .Join(_context.MediaFiles, fi => fi.ImageId, mf => mf.Id, (fi, mf) => new { fi, mf })
                .OrderBy(x => x.fi.OrderIndex ?? int.MaxValue)
                .ThenBy(x => x.mf.OrderIndex ?? int.MaxValue)
                .Select(x => x.mf)
                .ToList();

            if (mapped.Count == 0)
            {
                mapped = _context.MediaFiles
                    .Where(m => m.FolderId == folderId)
                    .OrderBy(m => m.OrderIndex)
                    .ToList();
            }

            if (fileType.HasValue)
            {
                mapped = mapped.Where(m => m.FileType == fileType.Value).ToList();
            }

            return mapped;
        }

        private bool IsFolderSystemV2Enabled()
        {
            var setting = _context.Settings.FirstOrDefault(s => s.Key == "feature.folder_system_v2")?.Value;
            return !string.Equals(setting, "0", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(setting, "false", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(setting, "off", StringComparison.OrdinalIgnoreCase);
        }

        private static FileType GetFileType(string extension)
        {
            var imageExts = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif" };
            var videoExts = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".f4v", ".rm", ".rmvb" };
            var audioExts = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac" };

            extension = extension.ToLower();
            if (imageExts.Contains(extension))
            {
                return FileType.Image;
            }

            if (videoExts.Contains(extension))
            {
                return FileType.Video;
            }

            if (audioExts.Contains(extension))
            {
                return FileType.Audio;
            }

            return FileType.Image;
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
    }
}
