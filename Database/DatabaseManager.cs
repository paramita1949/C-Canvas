using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;
using EFCore.BulkExtensions;

namespace ImageColorChanger.Database
{
    /// <summary>
    /// 数据库管理器
    /// 提供高级数据库操作接口
    /// </summary>
    public class DatabaseManager : IDisposable
    {
        private readonly CanvasDbContext _context;
        private bool _disposed = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dbPath">数据库文件路径，默认为 pyimages.db</param>
        public DatabaseManager(string dbPath = "pyimages.db")
        {
            _context = new CanvasDbContext(dbPath);
            _context.InitializeDatabase();
        }

        #region 文件夹操作

        /// <summary>
        /// 导入文件夹
        /// </summary>
        public Folder ImportFolder(string folderPath, string folderName = null)
        {
            folderName ??= System.IO.Path.GetFileName(folderPath);

            // 检查文件夹是否已存在
            var existingFolder = _context.Folders.FirstOrDefault(f => f.Path == folderPath);
            if (existingFolder != null)
                return existingFolder;

            // 获取最大排序索引
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

        /// <summary>
        /// 获取所有文件夹
        /// </summary>
        public List<Folder> GetAllFolders()
        {
            return _context.Folders
                .OrderBy(f => f.OrderIndex)
                .Include(f => f.MediaFiles)
                .ToList();
        }

        /// <summary>
        /// 删除文件夹（级联删除所有文件）
        /// </summary>
        public void DeleteFolder(int folderId)
        {
            var folder = _context.Folders.Find(folderId);
            if (folder != null)
            {
                // 先删除文件夹下的所有文件
                var files = _context.MediaFiles.Where(f => f.FolderId == folderId).ToList();
                _context.MediaFiles.RemoveRange(files);
                
                // 再删除文件夹
                _context.Folders.Remove(folder);
                _context.SaveChanges();
            }
        }

        #endregion

        #region 媒体文件操作

        /// <summary>
        /// 添加媒体文件
        /// </summary>
        public MediaFile AddMediaFile(string filePath, int? folderId = null)
        {
            // 检查文件是否已存在
            var existing = _context.MediaFiles.FirstOrDefault(m => m.Path == filePath);
            if (existing != null)
                return existing;

            var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var extension = System.IO.Path.GetExtension(filePath).ToLower();

            // 确定文件类型
            var fileType = GetFileType(extension);

            // 获取最大排序索引
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
                LastModified = System.IO.File.Exists(filePath) 
                    ? System.IO.File.GetLastWriteTime(filePath) 
                    : DateTime.Now
            };

            _context.MediaFiles.Add(mediaFile);
            _context.SaveChanges();

            return mediaFile;
        }

        /// <summary>
        /// 批量添加媒体文件（高性能版本，使用BulkInsert）
        /// </summary>
        public List<MediaFile> AddMediaFiles(IEnumerable<string> filePaths, int? folderId = null)
        {
            var mediaFiles = new List<MediaFile>();
            int orderIndex = 0;

            // 如果有 folderId，获取该文件夹的当前最大 OrderIndex
            if (folderId.HasValue)
            {
                orderIndex = _context.MediaFiles
                    .Where(m => m.FolderId == folderId.Value)
                    .Max(m => (int?)m.OrderIndex) ?? 0;
            }
            else
            {
                orderIndex = _context.MediaFiles
                    .Where(m => m.FolderId == null)
                    .Max(m => (int?)m.OrderIndex) ?? 0;
            }

            // 批量创建 MediaFile 对象
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
                    LastModified = System.IO.File.Exists(filePath) 
                        ? System.IO.File.GetLastWriteTime(filePath) 
                        : DateTime.Now
                };
                
                mediaFiles.Add(mediaFile);
            }

            // 使用 BulkInsert 批量插入（性能提升 10-100 倍）
            if (mediaFiles.Count > 0)
            {
                _context.BulkInsert(mediaFiles, new EFCore.BulkExtensions.BulkConfig 
                { 
                    SetOutputIdentity = true,  // 获取自动生成的 ID
                    BatchSize = 5000           // 每批处理 5000 条
                });
            }

            return mediaFiles;
        }

        /// <summary>
        /// 获取文件夹中的媒体文件
        /// </summary>
        public List<MediaFile> GetMediaFilesByFolder(int folderId)
        {
            return _context.MediaFiles
                .Where(m => m.FolderId == folderId)
                .OrderBy(m => m.OrderIndex)
                .ToList();
        }

        /// <summary>
        /// 获取根目录的媒体文件
        /// </summary>
        public List<MediaFile> GetRootMediaFiles()
        {
            return _context.MediaFiles
                .Where(m => m.FolderId == null)
                .OrderBy(m => m.OrderIndex)
                .ToList();
        }

        /// <summary>
        /// 删除媒体文件
        /// </summary>
        public void DeleteMediaFile(int mediaFileId)
        {
            var mediaFile = _context.MediaFiles.Find(mediaFileId);
            if (mediaFile != null)
            {
                _context.MediaFiles.Remove(mediaFile);
                _context.SaveChanges();
            }
        }

        /// <summary>
        /// 根据文件扩展名判断文件类型
        /// </summary>
        private FileType GetFileType(string extension)
        {
            var imageExts = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif" };
            var videoExts = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".f4v", ".rm", ".rmvb" };
            var audioExts = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac" };

            extension = extension.ToLower();

            if (imageExts.Contains(extension))
                return FileType.Image;
            else if (videoExts.Contains(extension))
                return FileType.Video;
            else if (audioExts.Contains(extension))
                return FileType.Audio;
            else
                return FileType.Image; // 默认为图片
        }

        #endregion

        #region 设置操作

        /// <summary>
        /// 获取设置值
        /// </summary>
        public string GetSetting(string key, string defaultValue = null)
        {
            var setting = _context.Settings.Find(key);
            return setting?.Value ?? defaultValue;
        }

        /// <summary>
        /// 保存设置
        /// </summary>
        public void SaveSetting(string key, string value)
        {
            var setting = _context.Settings.Find(key);
            if (setting == null)
            {
                setting = new Setting { Key = key, Value = value };
                _context.Settings.Add(setting);
            }
            else
            {
                setting.Value = value;
            }
            _context.SaveChanges();
        }

        /// <summary>
        /// 获取UI设置值
        /// </summary>
        public string GetUISetting(string key, string defaultValue = null)
        {
            var setting = _context.UISettings.Find(key);
            return setting?.Value ?? defaultValue;
        }

        /// <summary>
        /// 保存UI设置
        /// </summary>
        public void SaveUISetting(string key, string value)
        {
            var setting = _context.UISettings.Find(key);
            if (setting == null)
            {
                setting = new UISetting { Key = key, Value = value };
                _context.UISettings.Add(setting);
            }
            else
            {
                setting.Value = value;
            }
            _context.SaveChanges();
        }

        #endregion

        #region 关键帧操作

        /// <summary>
        /// 添加关键帧
        /// </summary>
        public Keyframe AddKeyframe(int imageId, double position, int yPosition)
        {
            var maxOrder = _context.Keyframes
                .Where(k => k.ImageId == imageId)
                .Max(k => (int?)k.OrderIndex) ?? -1;

            var keyframe = new Keyframe
            {
                ImageId = imageId,
                Position = position,
                YPosition = yPosition,
                OrderIndex = maxOrder + 1
            };

            _context.Keyframes.Add(keyframe);
            _context.SaveChanges();

            return keyframe;
        }

        /// <summary>
        /// 获取图片的所有关键帧
        /// </summary>
        public List<Keyframe> GetKeyframes(int imageId)
        {
            return _context.Keyframes
                .Where(k => k.ImageId == imageId)
                .OrderBy(k => k.OrderIndex)
                .ToList();
        }

        /// <summary>
        /// 删除关键帧
        /// </summary>
        public void DeleteKeyframe(int keyframeId)
        {
            var keyframe = _context.Keyframes.Find(keyframeId);
            if (keyframe != null)
            {
                _context.Keyframes.Remove(keyframe);
                _context.SaveChanges();
            }
        }

        /// <summary>
        /// 清除图片的所有关键帧
        /// </summary>
        public void ClearKeyframes(int imageId)
        {
            var keyframes = _context.Keyframes.Where(k => k.ImageId == imageId);
            _context.Keyframes.RemoveRange(keyframes);
            _context.SaveChanges();
        }

        #endregion

        #region 原图标记操作

        /// <summary>
        /// 标记为原图模式
        /// </summary>
        public OriginalMark MarkAsOriginal(ItemType itemType, int itemId, MarkType markType = MarkType.Loop)
        {
            // 检查是否已存在标记
            var itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
            var existing = _context.OriginalMarks
                .FirstOrDefault(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);

            if (existing != null)
            {
                // 更新标记类型
                existing.MarkType = markType;
                _context.SaveChanges();
                return existing;
            }

            // 创建新标记
            var mark = new OriginalMark
            {
                ItemType = itemType,
                ItemId = itemId,
                MarkType = markType,
                CreatedTime = DateTime.Now
            };

            _context.OriginalMarks.Add(mark);
            _context.SaveChanges();

            return mark;
        }

        /// <summary>
        /// 取消原图标记
        /// </summary>
        public void UnmarkAsOriginal(ItemType itemType, int itemId)
        {
            var itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
            var mark = _context.OriginalMarks
                .FirstOrDefault(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);

            if (mark != null)
            {
                _context.OriginalMarks.Remove(mark);
                _context.SaveChanges();
            }
        }

        /// <summary>
        /// 检查是否有原图标记
        /// </summary>
        public bool HasOriginalMark(ItemType itemType, int itemId)
        {
            var itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
            return _context.OriginalMarks
                .Any(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);
        }

        #endregion

        #region 数据库维护

        /// <summary>
        /// 优化数据库
        /// </summary>
        public void OptimizeDatabase()
        {
            _context.Database.ExecuteSqlRaw("VACUUM;");
            _context.Database.ExecuteSqlRaw("ANALYZE;");
        }

        /// <summary>
        /// 检查数据库完整性
        /// </summary>
        public bool CheckIntegrity()
        {
            try
            {
                var result = _context.Database.ExecuteSqlRaw("PRAGMA integrity_check;");
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 搜索操作

        /// <summary>
        /// 搜索文件
        /// </summary>
        public List<MediaFile> SearchFiles(string searchTerm, FileType? fileType = null)
        {
            // 先执行数据库查询（不包含枚举过滤）
            var allFiles = _context.MediaFiles
                .Include(f => f.Folder)
                .Where(f => f.Name.ToLower().Contains(searchTerm.ToLower()))
                .OrderBy(f => f.OrderIndex)
                .ToList();

            // 在内存中进行枚举过滤
            if (fileType.HasValue)
            {
                allFiles = allFiles.Where(f => f.FileType == fileType.Value).ToList();
            }

            return allFiles;
        }

        /// <summary>
        /// 在指定文件夹中搜索文件
        /// </summary>
        public List<MediaFile> SearchFilesInFolder(string searchTerm, int folderId, FileType? fileType = null)
        {
            // 先执行数据库查询（不包含枚举过滤）
            var allFiles = _context.MediaFiles
                .Include(f => f.Folder)
                .Where(f => f.FolderId == folderId && f.Name.ToLower().Contains(searchTerm.ToLower()))
                .OrderBy(f => f.OrderIndex)
                .ToList();

            // 在内存中进行枚举过滤
            if (fileType.HasValue)
            {
                allFiles = allFiles.Where(f => f.FileType == fileType.Value).ToList();
            }

            return allFiles;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _context?.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion

        #region 原图标记管理

        /// <summary>
        /// 添加原图标记
        /// </summary>
        public bool AddOriginalMark(OriginalMark mark)
        {
            try
            {
                // 检查是否已存在,如果存在则更新
                var existing = _context.OriginalMarks
                    .FirstOrDefault(m => m.ItemTypeString == mark.ItemTypeString && m.ItemId == mark.ItemId);

                if (existing != null)
                {
                    existing.MarkTypeString = mark.MarkTypeString;
                    existing.CreatedTime = DateTime.Now;
                }
                else
                {
                    _context.OriginalMarks.Add(mark);
                }

                _context.SaveChanges();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"添加原图标记失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 移除原图标记
        /// </summary>
        public bool RemoveOriginalMark(ItemType itemType, int itemId)
        {
            try
            {
                string itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
                var mark = _context.OriginalMarks
                    .FirstOrDefault(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);

                if (mark != null)
                {
                    _context.OriginalMarks.Remove(mark);
                    _context.SaveChanges();
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"移除原图标记失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查是否有原图标记
        /// </summary>
        public bool CheckOriginalMark(ItemType itemType, int itemId)
        {
            try
            {
                string itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
                return _context.OriginalMarks
                    .Any(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检查原图标记失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取原图标记类型
        /// </summary>
        public MarkType? GetOriginalMarkType(ItemType itemType, int itemId)
        {
            try
            {
                string itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
                var mark = _context.OriginalMarks
                    .FirstOrDefault(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);

                return mark?.MarkType;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取原图标记类型失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 根据ID获取媒体文件
        /// </summary>
        public MediaFile GetMediaFileById(int id)
        {
            return _context.MediaFiles.FirstOrDefault(m => m.Id == id);
        }

        /// <summary>
        /// 获取文件夹下的所有指定类型的媒体文件
        /// </summary>
        public List<MediaFile> GetMediaFilesByFolder(int folderId, FileType? fileType = null)
        {
            // 先获取所有数据,避免EF Core枚举比较翻译问题
            var allFiles = _context.MediaFiles
                .Where(m => m.FolderId == folderId)
                .OrderBy(m => m.OrderIndex)
                .ToList();
            
            // 在内存中过滤文件类型
            if (fileType != null)
            {
                allFiles = allFiles.Where(m => m.FileType == fileType.Value).ToList();
            }

            return allFiles;
        }

        /// <summary>
        /// 获取下一个媒体文件
        /// </summary>
        public MediaFile GetNextMediaFile(int folderId, int? currentOrderIndex, FileType? fileType = null)
        {
            // 先获取所有数据,避免EF Core枚举比较翻译问题
            var allFiles = _context.MediaFiles
                .Where(m => m.FolderId == folderId && m.OrderIndex > currentOrderIndex)
                .OrderBy(m => m.OrderIndex)
                .ToList();

            // 在内存中过滤文件类型
            if (fileType != null)
            {
                return allFiles.FirstOrDefault(m => m.FileType == fileType.Value);
            }

            return allFiles.FirstOrDefault();
        }

        /// <summary>
        /// 获取上一个媒体文件
        /// </summary>
        public MediaFile GetPreviousMediaFile(int folderId, int? currentOrderIndex, FileType? fileType = null)
        {
            // 先获取所有数据,避免EF Core枚举比较翻译问题
            var allFiles = _context.MediaFiles
                .Where(m => m.FolderId == folderId && m.OrderIndex < currentOrderIndex)
                .OrderByDescending(m => m.OrderIndex)
                .ToList();

            // 在内存中过滤文件类型
            if (fileType != null)
            {
                return allFiles.FirstOrDefault(m => m.FileType == fileType.Value);
            }

            return allFiles.FirstOrDefault();
        }

        #endregion
    }
}

