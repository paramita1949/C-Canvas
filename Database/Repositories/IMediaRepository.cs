using System.Collections.Generic;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.Database.Repositories
{
    public interface IMediaRepository
    {
        MediaFile AddMediaFile(string filePath, int? folderId = null);
        List<MediaFile> AddMediaFiles(IEnumerable<string> filePaths, int? folderId = null);
        List<MediaFile> GetMediaFilesByFolder(int folderId);
        List<MediaFile> GetMediaFilesByFolder(int folderId, FileType? fileType = null);
        List<MediaFile> GetRootMediaFiles();
        List<string> GetAllMediaPaths();
        void DeleteMediaFile(int mediaFileId);
        void UpdateMediaFilesOrder(List<MediaFile> mediaFiles);
        MediaFile GetMediaFileById(int id);
        MediaFile GetNextMediaFile(int folderId, int? currentOrderIndex, FileType? fileType = null);
        MediaFile GetPreviousMediaFile(int folderId, int? currentOrderIndex, FileType? fileType = null);
        List<MediaFile> SearchFiles(string searchTerm, FileType? fileType = null);
        List<MediaFile> SearchFilesInFolder(string searchTerm, int folderId, FileType? fileType = null);
    }
}
