using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 视频播放列表辅助
    /// </summary>
    public partial class MainWindow
    {
        private MediaFile FindCurrentMediaFileByPath(DatabaseManager dbManager, List<MediaFile> rootFiles, string currentVideoPath)
        {
            var currentMediaFile = rootFiles.FirstOrDefault(f => f.Path == currentVideoPath);
            if (currentMediaFile != null)
            {
                return currentMediaFile;
            }

            var folders = dbManager.GetAllFolders();
            foreach (var folder in folders)
            {
                var folderFiles = dbManager.GetMediaFilesByFolder(folder.Id);
                currentMediaFile = folderFiles.FirstOrDefault(f => f.Path == currentVideoPath);
                if (currentMediaFile != null)
                {
                    return currentMediaFile;
                }
            }

            return null;
        }

        private static List<string> BuildVideoPlaylistEntries(DatabaseManager dbManager, List<MediaFile> rootFiles, MediaFile currentMediaFile)
        {
            if (currentMediaFile.FolderId.HasValue)
            {
                var folderFiles = dbManager.GetMediaFilesByFolder(currentMediaFile.FolderId.Value);
                return folderFiles
                    .Where(f => f.FileType == FileType.Video)
                    .OrderBy(f => f.OrderIndex ?? 0)
                    .ThenBy(f => f.Name)
                    .Select(f => f.Path)
                    .ToList();
            }

            return rootFiles
                .Where(f => f.FileType == FileType.Video)
                .OrderBy(f => f.OrderIndex ?? 0)
                .ThenBy(f => f.Name)
                .Select(f => f.Path)
                .ToList();
        }

        private void ApplyFolderPlayModeIfNeeded(DatabaseManager dbManager, MediaFile currentMediaFile)
        {
            if (!currentMediaFile.FolderId.HasValue)
            {
                return;
            }

            string folderPlayMode = dbManager.GetFolderVideoPlayMode(currentMediaFile.FolderId.Value);
            if (string.IsNullOrEmpty(folderPlayMode))
            {
                folderPlayMode = "random";
                dbManager.SetFolderVideoPlayMode(currentMediaFile.FolderId.Value, folderPlayMode);
            }

            PlayMode mode = folderPlayMode switch
            {
                "sequential" => PlayMode.Sequential,
                "random" => PlayMode.Random,
                "loop_all" => PlayMode.LoopAll,
                "loop_one" => PlayMode.LoopOne,
                _ => PlayMode.Sequential
            };

            _videoPlayerManager.SetPlayMode(mode);
            string[] modeNames = { "顺序", "随机", "单曲", "列表" };
            ShowStatus($"播放模式: {modeNames[(int)mode]}");
        }
    }
}


