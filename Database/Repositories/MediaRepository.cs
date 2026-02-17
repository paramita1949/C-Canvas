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
            var mediaFiles = new List<MediaFile>();
            int orderIndex = folderId.HasValue
                ? _context.MediaFiles.Where(m => m.FolderId == folderId.Value).Max(m => (int?)m.OrderIndex) ?? 0
                : _context.MediaFiles.Where(m => m.FolderId == null).Max(m => (int?)m.OrderIndex) ?? 0;

            foreach (var filePath in filePaths)
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
            return _context.MediaFiles
                .Where(m => m.FolderId == folderId)
                .OrderBy(m => m.OrderIndex)
                .ToList();
        }

        public List<MediaFile> GetMediaFilesByFolder(int folderId, FileType? fileType = null)
        {
            var allFiles = _context.MediaFiles
                .Where(m => m.FolderId == folderId)
                .OrderBy(m => m.OrderIndex)
                .ToList();

            if (fileType != null)
            {
                allFiles = allFiles.Where(m => m.FileType == fileType.Value).ToList();
            }

            return allFiles;
        }

        public List<MediaFile> GetRootMediaFiles()
        {
            return _context.MediaFiles
                .Where(m => m.FolderId == null)
                .OrderBy(m => m.OrderIndex)
                .ToList();
        }

        public void DeleteMediaFile(int mediaFileId)
        {
            var mediaFile = _context.MediaFiles.Find(mediaFileId);
            if (mediaFile != null)
            {
                _context.MediaFiles.Remove(mediaFile);
                _context.SaveChanges();
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
            var allFiles = _context.MediaFiles
                .Where(m => m.FolderId == folderId && m.OrderIndex > currentOrderIndex)
                .OrderBy(m => m.OrderIndex)
                .ToList();

            return fileType != null ? allFiles.FirstOrDefault(m => m.FileType == fileType.Value) : allFiles.FirstOrDefault();
        }

        public MediaFile GetPreviousMediaFile(int folderId, int? currentOrderIndex, FileType? fileType = null)
        {
            var allFiles = _context.MediaFiles
                .Where(m => m.FolderId == folderId && m.OrderIndex < currentOrderIndex)
                .OrderByDescending(m => m.OrderIndex)
                .ToList();

            return fileType != null ? allFiles.FirstOrDefault(m => m.FileType == fileType.Value) : allFiles.FirstOrDefault();
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
            var allFiles = _context.MediaFiles
                .Include(f => f.Folder)
                .Where(f => f.FolderId == folderId && f.Name.ToLower().Contains(searchTerm.ToLower()))
                .OrderBy(f => f.OrderIndex)
                .ToList();

            if (fileType.HasValue)
            {
                allFiles = allFiles.Where(f => f.FileType == fileType.Value).ToList();
            }

            return allFiles;
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
    }
}
