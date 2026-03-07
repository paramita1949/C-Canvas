using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using ImageColorChanger.Utils;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 视频播放列表辅助
    /// </summary>
    public partial class MainWindow
    {
        private readonly Dictionary<string, List<string>> _mediaPlaylistCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _mediaPathToPlaylistKeyCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _playlistBuildInFlight = new(StringComparer.OrdinalIgnoreCase);

        private sealed class PlaylistSnapshot
        {
            public string TargetPath { get; set; }
            public string CacheKey { get; set; }
            public List<string> Playlist { get; set; }
            public int CurrentIndex { get; set; }
            public PlayMode? PlayMode { get; set; }
            public int? FolderId { get; set; }
        }

        private void ClearMediaPlaylistCache()
        {
            _mediaPlaylistCache.Clear();
            _mediaPathToPlaylistKeyCache.Clear();
        }

        private static bool IsAudioOrVideo(FileType fileType)
        {
            return fileType == FileType.Video || fileType == FileType.Audio;
        }

        private static string NormalizePathSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return System.IO.Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static string BuildPlaylistCacheKey(int? folderId)
        {
            return folderId.HasValue ? $"folder:{folderId.Value}" : "folder:root";
        }

        private static int FindPathIndex(List<string> playlist, string targetPath)
        {
            if (playlist == null || playlist.Count == 0 || string.IsNullOrWhiteSpace(targetPath))
            {
                return -1;
            }

            int index = playlist.FindIndex(p => string.Equals(p, targetPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                return index;
            }

            string normalizedTarget = NormalizePathSafe(targetPath);
            return playlist.FindIndex(p =>
                string.Equals(NormalizePathSafe(p), normalizedTarget, StringComparison.OrdinalIgnoreCase));
        }

        private void CachePlaylist(string cacheKey, List<string> playlist)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || playlist == null || playlist.Count == 0)
            {
                return;
            }

            _mediaPlaylistCache[cacheKey] = playlist;
            foreach (var path in playlist)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                _mediaPathToPlaylistKeyCache[path] = cacheKey;
                string normalizedPath = NormalizePathSafe(path);
                if (!string.IsNullOrWhiteSpace(normalizedPath))
                {
                    _mediaPathToPlaylistKeyCache[normalizedPath] = cacheKey;
                }
            }
        }

        private bool TryGetCachedPlaylistForPath(string mediaPath, out PlaylistSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(mediaPath))
            {
                return false;
            }

            string normalizedPath = NormalizePathSafe(mediaPath);
            if (!_mediaPathToPlaylistKeyCache.TryGetValue(mediaPath, out var cacheKey))
            {
                _mediaPathToPlaylistKeyCache.TryGetValue(normalizedPath, out cacheKey);
            }

            if (string.IsNullOrWhiteSpace(cacheKey) ||
                !_mediaPlaylistCache.TryGetValue(cacheKey, out var playlist) ||
                playlist == null ||
                playlist.Count == 0)
            {
                return false;
            }

            int index = FindPathIndex(playlist, mediaPath);
            if (index < 0)
            {
                return false;
            }

            snapshot = new PlaylistSnapshot
            {
                TargetPath = normalizedPath,
                CacheKey = cacheKey,
                Playlist = playlist,
                CurrentIndex = index,
                PlayMode = null,
                FolderId = cacheKey.StartsWith("folder:", StringComparison.Ordinal) &&
                           int.TryParse(cacheKey.Substring("folder:".Length), out var folderId)
                    ? folderId
                    : null
            };
            return true;
        }

        private MediaFile ResolveCurrentMediaFile(DatabaseManager dbManager, string mediaPath)
        {
            if (string.IsNullOrWhiteSpace(mediaPath))
            {
                return null;
            }

            var context = dbManager.GetDbContext();
            if (context != null)
            {
                var byPath = context.MediaFiles
                    .AsNoTracking()
                    .FirstOrDefault(m => m.Path == mediaPath);
                if (byPath != null)
                {
                    return byPath;
                }
            }

            var rootFiles = dbManager.GetRootMediaFiles();
            var rootHit = rootFiles.FirstOrDefault(f => string.Equals(f.Path, mediaPath, StringComparison.OrdinalIgnoreCase));
            if (rootHit != null)
            {
                return rootHit;
            }

            foreach (var folder in dbManager.GetAllFolders())
            {
                var folderHit = dbManager.GetMediaFilesByFolder(folder.Id)
                    .FirstOrDefault(f => IsAudioOrVideo(f.FileType) &&
                                         string.Equals(f.Path, mediaPath, StringComparison.OrdinalIgnoreCase));
                if (folderHit != null)
                {
                    return folderHit;
                }
            }

            return null;
        }

        private static PlayMode MapFolderPlayMode(string folderPlayMode)
        {
            return folderPlayMode switch
            {
                "sequential" => PlayMode.Sequential,
                "random" => PlayMode.Random,
                "loop_all" => PlayMode.LoopAll,
                "loop_one" => PlayMode.LoopOne,
                _ => PlayMode.Sequential
            };
        }

        private PlaylistSnapshot BuildPlaylistSnapshot(DatabaseManager dbManager, string mediaPath)
        {
            var currentMediaFile = ResolveCurrentMediaFile(dbManager, mediaPath);
            if (currentMediaFile == null)
            {
                return null;
            }

            string cacheKey = BuildPlaylistCacheKey(currentMediaFile.FolderId);
            if (_mediaPlaylistCache.TryGetValue(cacheKey, out var cachedPlaylist) && cachedPlaylist?.Count > 0)
            {
                int cachedIndex = FindPathIndex(cachedPlaylist, mediaPath);
                if (cachedIndex >= 0)
                {
                    return new PlaylistSnapshot
                    {
                        TargetPath = mediaPath,
                        CacheKey = cacheKey,
                        Playlist = cachedPlaylist,
                        CurrentIndex = cachedIndex,
                        PlayMode = currentMediaFile.FolderId.HasValue
                            ? MapFolderPlayMode(dbManager.GetFolderVideoPlayMode(currentMediaFile.FolderId.Value))
                            : null,
                        FolderId = currentMediaFile.FolderId
                    };
                }
            }

            List<string> playlist;
            if (currentMediaFile.FolderId.HasValue)
            {
                int folderId = currentMediaFile.FolderId.Value;
                playlist = dbManager.GetMediaFilesByFolder(folderId)
                    .Where(f => IsAudioOrVideo(f.FileType) && !IsAppleDoubleSidecarPath(f.Path))
                    .OrderBy(f => f.OrderIndex ?? 0)
                    .ThenBy(f => f.Name)
                    .Select(f => f.Path)
                    .ToList();
            }
            else
            {
                playlist = dbManager.GetRootMediaFiles()
                    .Where(f => IsAudioOrVideo(f.FileType) && !IsAppleDoubleSidecarPath(f.Path))
                    .OrderBy(f => f.OrderIndex ?? 0)
                    .ThenBy(f => f.Name)
                    .Select(f => f.Path)
                    .ToList();
            }

            int index = FindPathIndex(playlist, mediaPath);
            if (index < 0)
            {
                index = 0;
                if (!playlist.Contains(mediaPath, StringComparer.OrdinalIgnoreCase))
                {
                    playlist.Insert(0, mediaPath);
                }
            }

            return new PlaylistSnapshot
            {
                TargetPath = mediaPath,
                CacheKey = cacheKey,
                Playlist = playlist,
                CurrentIndex = index,
                PlayMode = currentMediaFile.FolderId.HasValue
                    ? MapFolderPlayMode(dbManager.GetFolderVideoPlayMode(currentMediaFile.FolderId.Value))
                    : null,
                FolderId = currentMediaFile.FolderId
            };
        }

        private async Task BuildAndApplyPlaylistAsync(string mediaPath, string reason, bool applyToPlayer)
        {
            string normalizedPath = NormalizePathSafe(mediaPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            lock (_playlistBuildInFlight)
            {
                if (_playlistBuildInFlight.Contains(normalizedPath))
                {
                    return;
                }

                _playlistBuildInFlight.Add(normalizedPath);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string dbPath = DatabaseManagerService.GetDatabasePath();
                var snapshot = await Task.Run(() =>
                {
                    using var isolatedDbManager = new DatabaseManager(dbPath);
                    return BuildPlaylistSnapshot(isolatedDbManager, normalizedPath);
                });

                await Dispatcher.InvokeAsync(() =>
                {
                    if (snapshot == null || snapshot.Playlist == null || snapshot.Playlist.Count == 0)
                    {
                        StartupPerfLogger.Mark(
                            "MainWindow.VideoPlaylist.Async.Empty",
                            $"ElapsedMs={sw.ElapsedMilliseconds}; Reason={reason}; Path={normalizedPath}");
                        return;
                    }

                    CachePlaylist(snapshot.CacheKey, snapshot.Playlist);

                    if (!applyToPlayer || _videoPlayerManager == null)
                    {
                        StartupPerfLogger.Mark(
                            "MainWindow.VideoPlaylist.Async.Cached",
                            $"ElapsedMs={sw.ElapsedMilliseconds}; Reason={reason}; Count={snapshot.Playlist.Count}; FolderId={snapshot.FolderId?.ToString() ?? "<root>"}");
                        return;
                    }

                    string activePath = _videoPlayerManager.CurrentMediaPath;
                    if (string.IsNullOrWhiteSpace(activePath))
                    {
                        return;
                    }

                    int activeIndex = FindPathIndex(snapshot.Playlist, activePath);
                    if (activeIndex < 0)
                    {
                        return;
                    }

                    _videoPlayerManager.SetPlaylistAndCurrent(snapshot.Playlist, activePath);
                    if (snapshot.PlayMode.HasValue)
                    {
                        _videoPlayerManager.SetPlayMode(snapshot.PlayMode.Value);
                    }

                    StartupPerfLogger.Mark(
                        "MainWindow.VideoPlaylist.Async.Applied",
                        $"ElapsedMs={sw.ElapsedMilliseconds}; Reason={reason}; Count={snapshot.Playlist.Count}; CurrentIndex={activeIndex}");
                });
            }
            catch (Exception ex)
            {
                StartupPerfLogger.Error("MainWindow.VideoPlaylist.Async.Failed", ex);
            }
            finally
            {
                lock (_playlistBuildInFlight)
                {
                    _playlistBuildInFlight.Remove(normalizedPath);
                }
            }
        }

        private async void StartDeferredMediaPlaylistPrewarm()
        {
            if (_startupDeferredWorkCts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                string dbPath = DatabaseManagerService.GetDatabasePath();
                string prewarmPath = await Task.Run(() =>
                {
                    using var isolatedDbManager = new DatabaseManager(dbPath);
                    var context = isolatedDbManager.GetDbContext();
                    if (context == null)
                    {
                        return null;
                    }

                    var topFolder = context.MediaFiles
                        .AsNoTracking()
                        .Where(m => m.FolderId.HasValue && (m.FileTypeString == "video" || m.FileTypeString == "audio"))
                        .GroupBy(m => m.FolderId!.Value)
                        .Select(g => new { FolderId = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .FirstOrDefault();

                    if (topFolder == null)
                    {
                        return null;
                    }

                    return context.MediaFiles
                        .AsNoTracking()
                        .Where(m => m.FolderId == topFolder.FolderId && (m.FileTypeString == "video" || m.FileTypeString == "audio"))
                        .OrderBy(m => m.OrderIndex ?? int.MaxValue)
                        .ThenBy(m => m.Id)
                        .Select(m => m.Path)
                        .FirstOrDefault();
                });

                if (string.IsNullOrWhiteSpace(prewarmPath))
                {
                    return;
                }

                await BuildAndApplyPlaylistAsync(prewarmPath, "StartupPrewarmLargestFolder", applyToPlayer: false);
            }
            catch (Exception ex)
            {
                StartupPerfLogger.Error("MainWindow.VideoPlaylist.Prewarm.Failed", ex);
            }
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

            PlayMode mode = MapFolderPlayMode(folderPlayMode);
            _videoPlayerManager.SetPlayMode(mode);
            string[] modeNames = { "顺序", "随机", "单曲", "列表" };
            ShowStatus($"播放模式: {modeNames[(int)mode]}");
        }
    }
}
