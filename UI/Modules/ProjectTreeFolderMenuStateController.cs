using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;

namespace ImageColorChanger.UI.Modules
{
    public sealed class ProjectTreeFolderMenuStateController
    {
        private readonly DatabaseManager _databaseManager;
        private readonly OriginalManager _originalManager;

        public ProjectTreeFolderMenuStateController(DatabaseManager databaseManager, OriginalManager originalManager)
        {
            _databaseManager = databaseManager;
            _originalManager = originalManager;
        }

        public ProjectTreeFolderMenuState GetState(int folderId)
        {
            var folderFiles = _databaseManager.GetMediaFilesByFolder(folderId);

            return new ProjectTreeFolderMenuState
            {
                HasVideoOrAudio = folderFiles.Any(f => f.FileType == FileType.Video || f.FileType == FileType.Audio),
                HasImages = folderFiles.Any(f => f.FileType == FileType.Image),
                HasFolderOriginalMark = _originalManager.CheckOriginalMark(ItemType.Folder, folderId),
                HasColorEffectMark = _databaseManager.HasFolderAutoColorEffect(folderId),
                CurrentPlayMode = _databaseManager.GetFolderVideoPlayMode(folderId),
                IsManualSort = _databaseManager.IsManualSortFolder(folderId)
            };
        }
    }

    public sealed class ProjectTreeFolderMenuState
    {
        public bool HasVideoOrAudio { get; set; }
        public bool HasImages { get; set; }
        public bool HasFolderOriginalMark { get; set; }
        public bool HasColorEffectMark { get; set; }
        public string CurrentPlayMode { get; set; }
        public bool IsManualSort { get; set; }
    }
}
