using System;
using System.Collections.Generic;
using System.IO;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Database.Repositories;

namespace ImageColorChanger.Database
{
    /// <summary>
    /// 数据库管理器（编排层）
    /// 对外保持兼容 API，内部委托给按聚合拆分的 Repository。
    /// </summary>
    public class DatabaseManager : IDisposable
    {
        private readonly CanvasDbContext _context;
        private readonly IFolderRepository _folderRepository;
        private readonly IMediaRepository _mediaRepository;
        private readonly ISettingsRepository _settingsRepository;
        private readonly IOriginalMarkRepository _originalMarkRepository;
        private readonly IKeyframeRepository _keyframeRepository;
        private readonly IDatabaseMaintenanceRepository _databaseMaintenanceRepository;
        private bool _disposed;

        public DatabaseManager(
            CanvasDbContext context,
            IFolderRepository folderRepository,
            IMediaRepository mediaRepository,
            ISettingsRepository settingsRepository,
            IOriginalMarkRepository originalMarkRepository,
            IKeyframeRepository keyframeRepository,
            IDatabaseMaintenanceRepository databaseMaintenanceRepository)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _folderRepository = folderRepository ?? throw new ArgumentNullException(nameof(folderRepository));
            _mediaRepository = mediaRepository ?? throw new ArgumentNullException(nameof(mediaRepository));
            _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
            _originalMarkRepository = originalMarkRepository ?? throw new ArgumentNullException(nameof(originalMarkRepository));
            _keyframeRepository = keyframeRepository ?? throw new ArgumentNullException(nameof(keyframeRepository));
            _databaseMaintenanceRepository = databaseMaintenanceRepository ?? throw new ArgumentNullException(nameof(databaseMaintenanceRepository));
        }

        public DatabaseManager(string dbPath = null)
        {
            if (string.IsNullOrEmpty(dbPath))
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                dbPath = Path.Combine(appDirectory, "pyimages.db");
            }

            _context = new CanvasDbContext(dbPath);
            _context.InitializeDatabase();

            _folderRepository = new FolderRepository(_context);
            _mediaRepository = new MediaRepository(_context);
            _settingsRepository = new SettingsRepository(_context);
            _originalMarkRepository = new OriginalMarkRepository(_context);
            _keyframeRepository = new KeyframeRepository(_context);
            _databaseMaintenanceRepository = new DatabaseMaintenanceRepository(_context);
        }

        #region 文件夹操作

        public Folder ImportFolder(string folderPath, string folderName = null) => _folderRepository.ImportFolder(folderPath, folderName);
        public List<Folder> GetAllFolders() => _folderRepository.GetAllFolders();
        public void DeleteFolder(int folderId, bool forceDelete = false) => _folderRepository.DeleteFolder(folderId, forceDelete);
        public void UpdateFoldersOrder(List<Folder> folders) => _folderRepository.UpdateFoldersOrder(folders);

        #endregion

        #region 媒体文件操作

        public MediaFile AddMediaFile(string filePath, int? folderId = null) => _mediaRepository.AddMediaFile(filePath, folderId);
        public List<MediaFile> AddMediaFiles(IEnumerable<string> filePaths, int? folderId = null) => _mediaRepository.AddMediaFiles(filePaths, folderId);
        public List<MediaFile> GetMediaFilesByFolder(int folderId) => _mediaRepository.GetMediaFilesByFolder(folderId);
        public List<MediaFile> GetMediaFilesByFolder(int folderId, FileType? fileType = null) => _mediaRepository.GetMediaFilesByFolder(folderId, fileType);
        public List<MediaFile> GetRootMediaFiles() => _mediaRepository.GetRootMediaFiles();
        public void DeleteMediaFile(int mediaFileId) => _mediaRepository.DeleteMediaFile(mediaFileId);
        public void UpdateMediaFilesOrder(List<MediaFile> mediaFiles) => _mediaRepository.UpdateMediaFilesOrder(mediaFiles);

        public MediaFile GetMediaFileById(int id) => _mediaRepository.GetMediaFileById(id);
        public MediaFile GetNextMediaFile(int folderId, int? currentOrderIndex, FileType? fileType = null) => _mediaRepository.GetNextMediaFile(folderId, currentOrderIndex, fileType);
        public MediaFile GetPreviousMediaFile(int folderId, int? currentOrderIndex, FileType? fileType = null) => _mediaRepository.GetPreviousMediaFile(folderId, currentOrderIndex, fileType);
        public List<MediaFile> SearchFiles(string searchTerm, FileType? fileType = null) => _mediaRepository.SearchFiles(searchTerm, fileType);
        public List<MediaFile> SearchFilesInFolder(string searchTerm, int folderId, FileType? fileType = null) => _mediaRepository.SearchFilesInFolder(searchTerm, folderId, fileType);

        #endregion

        #region 设置操作

        public string GetSetting(string key, string defaultValue = null) => _settingsRepository.GetSetting(key, defaultValue);
        public void SaveSetting(string key, string value) => _settingsRepository.SaveSetting(key, value);
        public string GetUISetting(string key, string defaultValue = null) => _settingsRepository.GetUISetting(key, defaultValue);
        public void SaveUISetting(string key, string value) => _settingsRepository.SaveUISetting(key, value);

        #endregion

        #region 关键帧操作

        public Keyframe AddKeyframe(int imageId, double position, int yPosition) => _keyframeRepository.AddKeyframe(imageId, position, yPosition);
        public List<Keyframe> GetKeyframes(int imageId) => _keyframeRepository.GetKeyframes(imageId);
        public void DeleteKeyframe(int keyframeId) => _keyframeRepository.DeleteKeyframe(keyframeId);
        public void ClearKeyframes(int imageId) => _keyframeRepository.ClearKeyframes(imageId);

        #endregion

        #region 原图标记

        public OriginalMark MarkAsOriginal(ItemType itemType, int itemId, MarkType markType = MarkType.Loop) =>
            _originalMarkRepository.MarkAsOriginal(itemType, itemId, markType);

        public void UnmarkAsOriginal(ItemType itemType, int itemId) =>
            _originalMarkRepository.UnmarkAsOriginal(itemType, itemId);

        public bool HasOriginalMark(ItemType itemType, int itemId) =>
            _originalMarkRepository.HasOriginalMark(itemType, itemId);

        public bool AddOriginalMark(OriginalMark mark) => _originalMarkRepository.AddOriginalMark(mark);
        public bool RemoveOriginalMark(ItemType itemType, int itemId) => _originalMarkRepository.RemoveOriginalMark(itemType, itemId);
        public bool CheckOriginalMark(ItemType itemType, int itemId) => _originalMarkRepository.CheckOriginalMark(itemType, itemId);
        public MarkType? GetOriginalMarkType(ItemType itemType, int itemId) => _originalMarkRepository.GetOriginalMarkType(itemType, itemId);

        #endregion

        #region 数据库维护

        public void OptimizeDatabase() => _databaseMaintenanceRepository.OptimizeDatabase();
        public bool CheckIntegrity() => _databaseMaintenanceRepository.CheckIntegrity();
        public void CheckpointAndCloseConnections() => _databaseMaintenanceRepository.CheckpointAndCloseConnections();

        #endregion

        #region 文件夹扩展状态

        public bool IsManualSortFolder(int folderId) => _folderRepository.IsManualSortFolder(folderId);
        public void MarkFolderAsManualSort(int folderId) => _folderRepository.MarkFolderAsManualSort(folderId);
        public void UnmarkFolderAsManualSort(int folderId) => _folderRepository.UnmarkFolderAsManualSort(folderId);
        public List<int> GetManualSortFolderIds() => _folderRepository.GetManualSortFolderIds();

        public void MarkFolderAutoColorEffect(int folderId) => _folderRepository.MarkFolderAutoColorEffect(folderId);
        public void UnmarkFolderAutoColorEffect(int folderId) => _folderRepository.UnmarkFolderAutoColorEffect(folderId);
        public bool HasFolderAutoColorEffect(int folderId) => _folderRepository.HasFolderAutoColorEffect(folderId);

        public void SetFolderVideoPlayMode(int folderId, string playMode) => _folderRepository.SetFolderVideoPlayMode(folderId, playMode);
        public string GetFolderVideoPlayMode(int folderId) => _folderRepository.GetFolderVideoPlayMode(folderId);
        public void ClearFolderVideoPlayMode(int folderId) => _folderRepository.ClearFolderVideoPlayMode(folderId);

        public void SetFolderHighlightColor(int folderId, string color) => _folderRepository.SetFolderHighlightColor(folderId, color);
        public string GetFolderHighlightColor(int folderId) => _folderRepository.GetFolderHighlightColor(folderId);

        #endregion

        #region 圣经插入配置

        public string GetBibleInsertConfigValue(string key, string defaultValue = "") =>
            _settingsRepository.GetBibleInsertConfigValue(key, defaultValue);

        public void SetBibleInsertConfigValue(string key, string value) =>
            _settingsRepository.SetBibleInsertConfigValue(key, value);

        #endregion

        #region 数据库上下文

        public CanvasDbContext GetDbContext() => _context;

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _context?.Dispose();
            }

            _disposed = true;
        }

        #endregion
    }
}
