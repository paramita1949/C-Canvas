using System.Collections.Generic;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Database.Repositories
{
    public interface IFolderRepository
    {
        Folder ImportFolder(string folderPath, string folderName = null);
        List<Folder> GetAllFolders();
        void DeleteFolder(int folderId, bool forceDelete = false);
        void UpdateFoldersOrder(List<Folder> folders);

        bool IsManualSortFolder(int folderId);
        void MarkFolderAsManualSort(int folderId);
        void UnmarkFolderAsManualSort(int folderId);
        List<int> GetManualSortFolderIds();

        void MarkFolderAutoColorEffect(int folderId);
        void UnmarkFolderAutoColorEffect(int folderId);
        bool HasFolderAutoColorEffect(int folderId);

        void SetFolderVideoPlayMode(int folderId, string playMode);
        string GetFolderVideoPlayMode(int folderId);
        int NormalizeFolderVideoPlayModes(string defaultMode = "random");

        void SetFolderHighlightColor(int folderId, string color);
        string GetFolderHighlightColor(int folderId);
    }
}
