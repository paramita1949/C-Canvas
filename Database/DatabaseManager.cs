using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
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
        /// <param name="dbPath">数据库文件路径，默认为主程序目录下的 pyimages.db</param>
        public DatabaseManager(string dbPath = null)
        {
            // 如果没有指定路径，则使用主程序所在目录
            if (string.IsNullOrEmpty(dbPath))
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                dbPath = System.IO.Path.Combine(appDirectory, "pyimages.db");
            }
            
            // System.Diagnostics.Debug.WriteLine($"📁 数据库文件路径: {dbPath}");
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
        /// 删除文件夹（级联删除所有文件及关联数据）
        /// </summary>
        public void DeleteFolder(int folderId, bool forceDelete = false)
        {
            var folder = _context.Folders.Find(folderId);
            if (folder == null) return;

            if (forceDelete)
            {
                // 🔥 强制删除模式：禁用外键约束，使用原生SQL
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[删除文件夹] 使用强制删除模式");
                #endif

                try
                {
                    // 获取文件夹下的所有文件ID
                    var fileIds = _context.MediaFiles
                        .Where(f => f.FolderId == folderId)
                        .Select(f => f.Id)
                        .ToList();

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[删除文件夹] 找到 {fileIds.Count} 个文件");
                    #endif

                    // 先禁用外键约束（必须在任何操作之前）
                    _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

                    using var transaction = _context.Database.BeginTransaction();
                    try
                    {
                        // 使用原生SQL删除所有关联数据（按任意顺序）
                        if (fileIds.Count > 0)
                        {
                            string fileIdList = string.Join(",", fileIds);

                            // 使用 FormattableString 避免 SQL 注入警告（fileIdList 是安全的整数列表）
                            _context.Database.ExecuteSqlRaw("DELETE FROM keyframe_timings WHERE image_id IN ({0})", fileIdList);
                            _context.Database.ExecuteSqlRaw("DELETE FROM keyframes WHERE image_id IN ({0})", fileIdList);
                            _context.Database.ExecuteSqlRaw("DELETE FROM image_display_locations WHERE image_id IN ({0})", fileIdList);
                            _context.Database.ExecuteSqlRaw("DELETE FROM composite_scripts WHERE image_id IN ({0})", fileIdList);
                            _context.Database.ExecuteSqlRaw("DELETE FROM original_marks WHERE item_type = 'image' AND item_id IN ({0})", fileIdList);

                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[删除文件夹] 已删除所有关联数据");
                            #endif
                        }

                        // 删除媒体文件
                        _context.Database.ExecuteSqlRaw("DELETE FROM images WHERE folder_id = {0}", folderId);

                        // 删除文件夹
                        _context.Database.ExecuteSqlRaw("DELETE FROM folders WHERE id = {0}", folderId);

                        transaction.Commit();

                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[删除文件夹] 强制删除成功");
                        #endif
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                    finally
                    {
                        // 恢复外键约束
                        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
                    }
                }
                catch (Exception ex)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[删除文件夹] 强制删除失败: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[删除文件夹] 堆栈: {ex.StackTrace}");
                    #else
                    _ = ex;
                    #endif
                    
                    // 确保恢复外键约束
                    try
                    {
                        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
                    }
                    catch
                    {
                        // 忽略恢复失败
                    }
                    
                    throw;
                }
            }
            else
            {
                // 常规删除模式：使用事务和EF Core
                using var transaction = _context.Database.BeginTransaction();
                try
                {
                    // 获取文件夹下的所有文件ID
                    var fileIds = _context.MediaFiles
                        .Where(f => f.FolderId == folderId)
                        .Select(f => f.Id)
                        .ToList();

                    if (fileIds.Count > 0)
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[删除文件夹] 找到 {fileIds.Count} 个文件");
                        #endif

                        // 按依赖顺序删除
                        // 1. 删除关键帧时间记录
                        var timings = _context.KeyframeTimings
                            .Where(t => fileIds.Contains(t.ImageId))
                            .ToList();
                        if (timings.Count > 0)
                        {
                            _context.KeyframeTimings.RemoveRange(timings);
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[删除文件夹] 删除 {timings.Count} 条关键帧时间记录");
                            #endif
                        }

                        // 2. 删除关键帧
                        var keyframes = _context.Keyframes
                            .Where(k => fileIds.Contains(k.ImageId))
                            .ToList();
                        if (keyframes.Count > 0)
                        {
                            _context.Keyframes.RemoveRange(keyframes);
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[删除文件夹] 删除 {keyframes.Count} 个关键帧");
                            #endif
                        }

                        // 3. 删除显示位置记录
                        var displayLocations = _context.ImageDisplayLocations
                            .Where(l => fileIds.Contains(l.ImageId))
                            .ToList();
                        if (displayLocations.Count > 0)
                        {
                            _context.ImageDisplayLocations.RemoveRange(displayLocations);
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[删除文件夹] 删除 {displayLocations.Count} 条显示位置记录");
                            #endif
                        }

                        // 4. 删除合成脚本
                        var compositeScripts = _context.CompositeScripts
                            .Where(s => fileIds.Contains(s.ImageId))
                            .ToList();
                        if (compositeScripts.Count > 0)
                        {
                            _context.CompositeScripts.RemoveRange(compositeScripts);
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[删除文件夹] 删除 {compositeScripts.Count} 个合成脚本");
                            #endif
                        }

                        // 5. 删除原图标记
                        var originalMarks = _context.OriginalMarks
                            .Where(m => m.ItemTypeString == "image" && fileIds.Contains(m.ItemId))
                            .ToList();
                        if (originalMarks.Count > 0)
                        {
                            _context.OriginalMarks.RemoveRange(originalMarks);
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"[删除文件夹] 删除 {originalMarks.Count} 个原图标记");
                            #endif
                        }

                        // 6. 删除媒体文件
                        var files = _context.MediaFiles
                            .Where(f => fileIds.Contains(f.Id))
                            .ToList();
                        _context.MediaFiles.RemoveRange(files);
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[删除文件夹] 删除 {files.Count} 个媒体文件");
                        #endif
                    }

                    // 7. 删除文件夹本身
                    _context.Folders.Remove(folder);

                    // 提交所有更改
                    _context.SaveChanges();
                    transaction.Commit();

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[删除文件夹] 成功删除文件夹 ID={folderId}");
                    #endif
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[删除文件夹] 失败: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[删除文件夹] 堆栈: {ex.StackTrace}");
                    #else
                    _ = ex;
                    #endif
                    throw;
                }
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
        /// 批量更新媒体文件的排序顺序
        /// </summary>
        public void UpdateMediaFilesOrder(List<MediaFile> mediaFiles)
        {
            if (mediaFiles == null || mediaFiles.Count == 0)
                return;

            try
            {
                // EF Core会自动跟踪这些对象的变化
                _context.SaveChanges();
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"更新媒体文件排序失败: {ex}");
                throw;
            }
        }

        /// <summary>
        /// 批量更新文件夹的排序顺序
        /// </summary>
        public void UpdateFoldersOrder(List<Folder> folders)
        {
            if (folders == null || folders.Count == 0)
                return;

            try
            {
                // EF Core会自动跟踪这些对象的变化
                _context.SaveChanges();
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"更新文件夹排序失败: {ex}");
                throw;
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
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"添加原图标记失败: {ex.Message}");
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
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"移除原图标记失败: {ex.Message}");
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
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"检查原图标记失败: {ex.Message}");
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
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"获取原图标记类型失败: {ex.Message}");
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

        #region 手动排序管理

        /// <summary>
        /// 检查文件夹是否为手动排序
        /// </summary>
        public bool IsManualSortFolder(int folderId)
        {
            var manualSort = _context.ManualSortFolders
                .FirstOrDefault(m => m.FolderId == folderId);
            
            return manualSort != null && manualSort.IsManualSort;
        }

        /// <summary>
        /// 标记文件夹为手动排序
        /// </summary>
        public void MarkFolderAsManualSort(int folderId)
        {
            var manualSort = _context.ManualSortFolders
                .FirstOrDefault(m => m.FolderId == folderId);

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

        /// <summary>
        /// 取消文件夹的手动排序标记
        /// </summary>
        public void UnmarkFolderAsManualSort(int folderId)
        {
            var manualSort = _context.ManualSortFolders
                .FirstOrDefault(m => m.FolderId == folderId);

            if (manualSort != null)
            {
                _context.ManualSortFolders.Remove(manualSort);
                _context.SaveChanges();
            }
        }

        /// <summary>
        /// 获取所有手动排序的文件夹ID
        /// </summary>
        public List<int> GetManualSortFolderIds()
        {
            return _context.ManualSortFolders
                .Where(m => m.IsManualSort)
                .Select(m => m.FolderId)
                .ToList();
        }

        #endregion
        
        #region 变色效果标记管理
        
        /// <summary>
        /// 标记文件夹自动开启变色效果
        /// </summary>
        public void MarkFolderAutoColorEffect(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            if (folder != null)
            {
                folder.AutoColorEffect = 1;
                _context.SaveChanges();
                // System.Diagnostics.Debug.WriteLine($"✅ 已标记文件夹 [{folder.Name}] 自动开启变色");
            }
        }
        
        /// <summary>
        /// 取消文件夹变色标记
        /// </summary>
        public void UnmarkFolderAutoColorEffect(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            if (folder != null)
            {
                folder.AutoColorEffect = null;
                _context.SaveChanges();
                // System.Diagnostics.Debug.WriteLine($"✅ 已取消文件夹 [{folder.Name}] 的变色标记");
            }
        }
        
        /// <summary>
        /// 检查文件夹是否标记了自动变色
        /// </summary>
        public bool HasFolderAutoColorEffect(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            return folder?.AutoColorEffect == 1;
        }
        
        #endregion
        
        #region 视频播放模式管理
        
        /// <summary>
        /// 设置文件夹的视频播放模式
        /// </summary>
        public void SetFolderVideoPlayMode(int folderId, string playMode)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            if (folder != null)
            {
                folder.VideoPlayMode = playMode;
                _context.SaveChanges();
                // System.Diagnostics.Debug.WriteLine($"✅ 已设置文件夹 [{folder.Name}] 的播放模式: {playMode}");
            }
        }
        
        /// <summary>
        /// 获取文件夹的视频播放模式
        /// </summary>
        public string GetFolderVideoPlayMode(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            return folder?.VideoPlayMode;
        }
        
        /// <summary>
        /// 清除文件夹的视频播放模式
        /// </summary>
        public void ClearFolderVideoPlayMode(int folderId)
        {
            var folder = _context.Folders.FirstOrDefault(f => f.Id == folderId);
            if (folder != null)
            {
                folder.VideoPlayMode = null;
                _context.SaveChanges();
                // System.Diagnostics.Debug.WriteLine($"✅ 已清除文件夹 [{folder.Name}] 的播放模式");
            }
        }

        #endregion

        #region 数据库上下文

        /// <summary>
        /// 获取数据库上下文
        /// </summary>
        /// <returns>数据库上下文</returns>
        public CanvasDbContext GetDbContext()
        {
            return _context;
        }

        #endregion

        #region 数据库迁移

        /// <summary>
        /// 执行数据库迁移 - 添加 loop_count 列
        /// </summary>
        public void MigrateAddLoopCount()
        {
            try
            {
                // 检查列是否已存在
                var checkSql = "SELECT COUNT(*) FROM pragma_table_info('keyframes') WHERE name='loop_count'";
                var connection = _context.Database.GetDbConnection();
                connection.Open();
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    
                    if (count == 0)
                    {
                        // 列不存在，执行添加
                        _context.Database.ExecuteSqlRaw("ALTER TABLE keyframes ADD COLUMN loop_count INTEGER NULL");
                        // System.Diagnostics.Debug.WriteLine("✅ 数据库迁移成功：已添加 loop_count 列");
                    }
                    else
                    {
                        // System.Diagnostics.Debug.WriteLine("ℹ️ loop_count 列已存在，跳过迁移");
                    }
                }
                
                connection.Close();
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 数据库迁移失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行数据库迁移 - 添加 highlight_color 列
        /// </summary>
        public void MigrateAddHighlightColor()
        {
            try
            {
                // 检查列是否已存在
                var checkSql = "SELECT COUNT(*) FROM pragma_table_info('folders') WHERE name='highlight_color'";
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    
                    if (count == 0)
                    {
                        // 列不存在，执行添加
                        _context.Database.ExecuteSqlRaw("ALTER TABLE folders ADD COLUMN highlight_color TEXT NULL");
                        //System.Diagnostics.Debug.WriteLine("✅ 数据库迁移成功：已添加 highlight_color 列");
                    }
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 数据库迁移失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行数据库迁移 - 创建圣经历史记录表
        /// </summary>
        public void MigrateAddBibleHistoryTable()
        {
            try
            {
                // 检查表是否已存在
                var checkSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='bible_history'";
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    
                    if (count == 0)
                    {
                        // 表不存在，创建表
                        var createTableSql = @"
                            CREATE TABLE bible_history (
                                slot_index INTEGER PRIMARY KEY,
                                display_text TEXT,
                                book_id INTEGER NOT NULL DEFAULT 0,
                                chapter INTEGER NOT NULL DEFAULT 0,
                                start_verse INTEGER NOT NULL DEFAULT 0,
                                end_verse INTEGER NOT NULL DEFAULT 0,
                                is_checked INTEGER NOT NULL DEFAULT 0,
                                is_locked INTEGER NOT NULL DEFAULT 0,
                                updated_time TEXT NOT NULL
                            )";
                        _context.Database.ExecuteSqlRaw(createTableSql);
                        
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("✅ 数据库迁移成功：已创建 bible_history 表");
                        #endif
                    }
                    else
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("ℹ️ bible_history 表已存在，跳过迁移");
                        #endif
                    }
                }
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 数据库迁移失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
                #endif
            }
        }
        
        /// <summary>
        /// 设置文件夹高亮颜色
        /// </summary>
        public void SetFolderHighlightColor(int folderId, string color)
        {
            var folder = _context.Folders.Find(folderId);
            if (folder != null)
            {
                folder.HighlightColor = color;
                _context.SaveChanges();
            }
        }
        
        /// <summary>
        /// 获取文件夹高亮颜色
        /// </summary>
        public string GetFolderHighlightColor(int folderId)
        {
            return _context.Folders.Find(folderId)?.HighlightColor;
        }
        
        /// <summary>
        /// 执行数据库迁移 - 添加 is_underline 列到 text_elements 表
        /// </summary>
        public void MigrateAddUnderlineSupport()
        {
            try
            {
                // 检查列是否已存在
                var checkSql = "SELECT COUNT(*) FROM pragma_table_info('text_elements') WHERE name='is_underline'";
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());

                    if (count == 0)
                    {
                        // 列不存在，执行添加
                        _context.Database.ExecuteSqlRaw("ALTER TABLE text_elements ADD COLUMN is_underline INTEGER NOT NULL DEFAULT 0");

                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("✅ 数据库迁移成功：已添加 is_underline 列到 text_elements 表");
                        #endif
                    }
                    else
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("ℹ️ is_underline 列已存在，跳过迁移");
                        #endif
                    }
                }
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 数据库迁移失败: {ex.Message}");
                #endif
            }
        }

        /// <summary>
        /// 执行数据库迁移 - 添加 RichText 支持（斜体、边框、背景、阴影、间距）
        /// </summary>
        public void MigrateAddRichTextSupport()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                // 定义所有需要添加的列
                var columnsToAdd = new[]
                {
                    // 斜体
                    ("is_italic", "INTEGER NOT NULL DEFAULT 0"),

                    // 边框样式
                    ("border_color", "TEXT NOT NULL DEFAULT '#000000'"),
                    ("border_width", "REAL NOT NULL DEFAULT 0"),
                    ("border_radius", "REAL NOT NULL DEFAULT 0"),
                    ("border_opacity", "INTEGER NOT NULL DEFAULT 0"),

                    // 背景样式
                    ("background_color", "TEXT NOT NULL DEFAULT '#FFFFFF'"),
                    ("background_radius", "REAL NOT NULL DEFAULT 0"),
                    ("background_opacity", "INTEGER NOT NULL DEFAULT 0"),

                    // 阴影样式
                    ("shadow_color", "TEXT NOT NULL DEFAULT '#000000'"),
                    ("shadow_offset_x", "REAL NOT NULL DEFAULT 0"),
                    ("shadow_offset_y", "REAL NOT NULL DEFAULT 0"),
                    ("shadow_blur", "REAL NOT NULL DEFAULT 0"),
                    ("shadow_opacity", "INTEGER NOT NULL DEFAULT 0"),

                    // 间距样式
                    ("line_spacing", "REAL NOT NULL DEFAULT 1.2"),
                    ("letter_spacing", "REAL NOT NULL DEFAULT 0.0")
                };

                int addedCount = 0;
                foreach (var (columnName, columnDef) in columnsToAdd)
                {
                    // 检查列是否已存在
                    var checkSql = $"SELECT COUNT(*) FROM pragma_table_info('text_elements') WHERE name='{columnName}'";
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = checkSql;
                        var count = Convert.ToInt32(command.ExecuteScalar());

                        if (count == 0)
                        {
                            // 列不存在，执行添加
                            // 注意：columnName 和 columnDef 来自代码中的硬编码元组，不是用户输入，因此是安全的
#pragma warning disable EF1002
                            _context.Database.ExecuteSqlRaw($"ALTER TABLE text_elements ADD COLUMN {columnName} {columnDef}");
#pragma warning restore EF1002
                            addedCount++;
                        }
                    }
                }

                #if DEBUG
                if (addedCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ 数据库迁移成功：已添加 {addedCount} 个 RichText 列到 text_elements 表");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ RichText 列已存在，跳过迁移");
                }
                #endif
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ RichText 数据库迁移失败: {ex.Message}");
                #endif
            }
        }

        /// <summary>
        /// 执行数据库迁移 - 添加阴影类型和预设字段
        /// </summary>
        public void MigrateAddShadowTypeAndPreset()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                // 定义需要添加的列
                var columnsToAdd = new[]
                {
                    ("shadow_type", "INTEGER NOT NULL DEFAULT 0"),
                    ("shadow_preset", "INTEGER NOT NULL DEFAULT 0")
                };

                foreach (var (columnName, columnDef) in columnsToAdd)
                {
                    // 检查列是否已存在
                    var checkSql = $"SELECT COUNT(*) FROM pragma_table_info('text_elements') WHERE name='{columnName}'";
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = checkSql;
                        var count = Convert.ToInt32(command.ExecuteScalar());

                        if (count == 0)
                        {
                            // 列不存在，执行添加
                            var alterSql = $"ALTER TABLE text_elements ADD COLUMN {columnName} {columnDef}";
                            _context.Database.ExecuteSqlRaw(alterSql);
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"✅ 数据库迁移成功：已添加 {columnName} 列");
                            #endif
                        }
                        else
                        {
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"ℹ️ {columnName} 列已存在，跳过迁移");
                            #endif
                        }
                    }
                }

                if (connection.State == System.Data.ConnectionState.Open)
                {
                    connection.Close();
                }
            }
            catch (Exception)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 数据库迁移失败 (shadow_type/shadow_preset)");
                #endif
                throw;
            }
        }

        /// <summary>
        /// 执行数据库迁移 - 创建富文本片段表（完全 RichText 支持）
        /// </summary>
        public void MigrateCreateRichTextSpansTable()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                // 检查表是否已存在
                var checkSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='rich_text_spans'";
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());

                    if (count == 0)
                    {
                        // 创建富文本片段表
                        var createTableSql = @"
                            CREATE TABLE rich_text_spans (
                                id INTEGER PRIMARY KEY AUTOINCREMENT,
                                text_element_id INTEGER NOT NULL,
                                span_order INTEGER NOT NULL,
                                text TEXT NOT NULL DEFAULT '',

                                -- 字体样式（可继承）
                                font_family TEXT NULL,
                                font_size REAL NULL,
                                font_color TEXT NULL,
                                is_bold INTEGER NOT NULL DEFAULT 0,
                                is_italic INTEGER NOT NULL DEFAULT 0,
                                is_underline INTEGER NOT NULL DEFAULT 0,

                                -- 边框样式（可继承）
                                border_color TEXT NULL,
                                border_width REAL NULL,
                                border_radius REAL NULL,
                                border_opacity INTEGER NULL,

                                -- 背景样式（可继承）
                                background_color TEXT NULL,
                                background_radius REAL NULL,
                                background_opacity INTEGER NULL,

                                -- 阴影样式（可继承）
                                shadow_color TEXT NULL,
                                shadow_offset_x REAL NULL,
                                shadow_offset_y REAL NULL,
                                shadow_blur REAL NULL,
                                shadow_opacity INTEGER NULL,

                                FOREIGN KEY (text_element_id) REFERENCES text_elements(id) ON DELETE CASCADE
                            )";

                        _context.Database.ExecuteSqlRaw(createTableSql);

                        // 创建索引
                        _context.Database.ExecuteSqlRaw("CREATE INDEX idx_rich_text_spans_element ON rich_text_spans(text_element_id)");
                        _context.Database.ExecuteSqlRaw("CREATE INDEX idx_rich_text_spans_order ON rich_text_spans(text_element_id, span_order)");

                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("✅ rich_text_spans 表创建成功");
                        #endif
                    }
                    else
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("ℹ️ rich_text_spans 表已存在，跳过创建");
                        #endif
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 创建 rich_text_spans 表失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
        }

        /// <summary>
        /// 执行数据库迁移 - 添加幻灯片视频背景支持
        /// </summary>
        public void MigrateAddVideoBackgroundSupport()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                // 定义需要添加的列
                var columnsToAdd = new[]
                {
                    ("video_background_enabled", "INTEGER NOT NULL DEFAULT 0"),
                    ("video_loop_enabled", "INTEGER NOT NULL DEFAULT 1"),
                    ("video_volume", "REAL NOT NULL DEFAULT 0.5")
                };

                int addedCount = 0;
                foreach (var (columnName, columnDef) in columnsToAdd)
                {
                    // 检查列是否已存在
                    var checkSql = $"SELECT COUNT(*) FROM pragma_table_info('slides') WHERE name='{columnName}'";
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = checkSql;
                        var count = Convert.ToInt32(command.ExecuteScalar());

                        if (count == 0)
                        {
                            // 列不存在，执行添加
                            var alterSql = $"ALTER TABLE slides ADD COLUMN {columnName} {columnDef}";
                            _context.Database.ExecuteSqlRaw(alterSql);
                            addedCount++;
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"✅ 数据库迁移成功：已添加 {columnName} 列到 slides 表");
                            #endif
                        }
                    }
                }

                #if DEBUG
                if (addedCount == 0)
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ 视频背景列已存在，跳过迁移");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✅ 数据库迁移完成：已添加 {addedCount} 个视频背景列");
                }
                #endif
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 视频背景数据库迁移失败: {ex.Message}");
                #endif
            }
        }

        /// <summary>
        /// 执行数据库迁移 - 创建圣经插入配置表
        /// </summary>
        public void MigrateAddBibleInsertConfigTable()
        {
            try
            {
                // 检查表是否已存在
                var checkSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='bible_insert_config'";
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());

                    if (count == 0)
                    {
                        // 表不存在，创建表（键值对存储）
                        var createTableSql = @"
                            CREATE TABLE bible_insert_config (
                                key TEXT PRIMARY KEY,
                                value TEXT NOT NULL
                            )";
                        _context.Database.ExecuteSqlRaw(createTableSql);

                        // 插入默认配置
                        var insertDefaultSql = @"
                            INSERT INTO bible_insert_config (key, value) VALUES
                            ('style', '0'),
                            ('font_family', 'DengXian'),
                            ('title_color', '#FF0000'),
                            ('title_size', '50'),
                            ('title_bold', '1'),
                            ('verse_color', '#FF9A35'),
                            ('verse_size', '40'),
                            ('verse_bold', '0'),
                            ('verse_spacing', '1.2'),
                            ('verse_number_color', '#FFFF00'),
                            ('verse_number_size', '40'),
                            ('verse_number_bold', '1'),
                            ('auto_hide_navigation', '1')";
                        _context.Database.ExecuteSqlRaw(insertDefaultSql);

                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("✅ 数据库迁移成功：已创建 bible_insert_config 表");
                        #endif
                    }
                    else
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("ℹ️ bible_insert_config 表已存在，跳过迁移");
                        #endif
                    }
                }
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 数据库迁移失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
                #endif
            }
        }
        
        /// <summary>
        /// 获取圣经插入配置值
        /// </summary>
        public string GetBibleInsertConfigValue(string key, string defaultValue = "")
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT value FROM bible_insert_config WHERE key = @key";
                    var param = command.CreateParameter();
                    param.ParameterName = "@key";
                    param.Value = key;
                    command.Parameters.Add(param);
                    
                    var result = command.ExecuteScalar();
                    return result?.ToString() ?? defaultValue;
                }
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 读取配置失败 [{key}]: {ex.Message}");
                #endif
                return defaultValue;
            }
        }
        
        /// <summary>
        /// 设置圣经插入配置值
        /// </summary>
        public void SetBibleInsertConfigValue(string key, string value)
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT OR REPLACE INTO bible_insert_config (key, value) 
                        VALUES (@key, @value)";
                    
                    var keyParam = command.CreateParameter();
                    keyParam.ParameterName = "@key";
                    keyParam.Value = key;
                    command.Parameters.Add(keyParam);
                    
                    var valueParam = command.CreateParameter();
                    valueParam.ParameterName = "@value";
                    valueParam.Value = value;
                    command.Parameters.Add(valueParam);
                    
                    command.ExecuteNonQuery();
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"💾 [DB] 配置已保存: {key} = {value}");
                //#endif
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 保存配置失败 [{key}]: {ex.Message}");
                #endif
            }
        }

        #endregion
    }
}

