using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ImageColorChanger.Database;
using ImageColorChanger.Managers;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private readonly object _autoDeleteSyncGate = new();
        private readonly List<FileSystemWatcher> _autoDeleteWatchers = new();
        private CancellationTokenSource _autoDeleteSyncCts = new();
        private bool _autoDeleteSyncQueued;
        private bool _autoDeleteSyncRunning;
        private bool _autoDeleteWatcherStarted;
        private Dictionary<int, string> _autoDeleteWatcherFolderSnapshot = new();
        private static readonly TimeSpan AutoDeleteSyncDebounce = TimeSpan.FromMilliseconds(450);

        private void EnsureAutoDeleteSyncStarted()
        {
            if (_autoDeleteWatcherStarted)
            {
                return;
            }

            _autoDeleteWatcherStarted = true;
            RebuildAutoDeleteWatchers(force: true);
        }

        private void StopAutoDeleteSync()
        {
            try
            {
                _autoDeleteSyncCts.Cancel();
                _autoDeleteSyncCts.Dispose();
            }
            catch
            {
            }
            finally
            {
                _autoDeleteSyncCts = new CancellationTokenSource();
            }

            DisposeAutoDeleteWatchers();
            _autoDeleteWatcherStarted = false;
            _autoDeleteWatcherFolderSnapshot = new Dictionary<int, string>();

            lock (_autoDeleteSyncGate)
            {
                _autoDeleteSyncQueued = false;
                _autoDeleteSyncRunning = false;
            }
        }

        private void NotifyProjectFoldersMayChanged()
        {
            if (!_autoDeleteWatcherStarted)
            {
                return;
            }

            RebuildAutoDeleteWatchers(force: false);
        }

        private void RebuildAutoDeleteWatchers(bool force)
        {
            Dictionary<int, string> currentMap = CaptureFolderWatchMap();
            if (!force && AreFolderWatchMapsEqual(currentMap, _autoDeleteWatcherFolderSnapshot))
            {
                return;
            }

            DisposeAutoDeleteWatchers();
            _autoDeleteWatcherFolderSnapshot = currentMap;

            foreach (var pair in currentMap)
            {
                try
                {
                    var watcher = new FileSystemWatcher(pair.Value)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    };
                    watcher.Deleted += AutoDeleteWatcher_Deleted;
                    watcher.Renamed += AutoDeleteWatcher_Renamed;
                    watcher.Error += AutoDeleteWatcher_Error;
                    watcher.EnableRaisingEvents = true;
                    _autoDeleteWatchers.Add(watcher);
                }
                catch
                {
                    // 某个目录无法监听时忽略，不影响其他目录。
                }
            }
        }

        private Dictionary<int, string> CaptureFolderWatchMap()
        {
            var result = new Dictionary<int, string>();
            try
            {
                var folders = DatabaseManagerService.GetAllFolders();
                foreach (var folder in folders)
                {
                    string path = NormalizeExistingDirectoryPath(folder?.Path);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    result[folder.Id] = path;
                }
            }
            catch
            {
            }

            return result;
        }

        private static string NormalizeExistingDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!Directory.Exists(fullPath))
                {
                    return string.Empty;
                }

                return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool AreFolderWatchMapsEqual(Dictionary<int, string> left, Dictionary<int, string> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out string rightPath))
                {
                    return false;
                }

                if (!string.Equals(pair.Value, rightPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void DisposeAutoDeleteWatchers()
        {
            foreach (var watcher in _autoDeleteWatchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Deleted -= AutoDeleteWatcher_Deleted;
                    watcher.Renamed -= AutoDeleteWatcher_Renamed;
                    watcher.Error -= AutoDeleteWatcher_Error;
                    watcher.Dispose();
                }
                catch
                {
                }
            }

            _autoDeleteWatchers.Clear();
        }

        private void AutoDeleteWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            if (!ShouldTriggerAutoDeleteSync(e?.FullPath))
            {
                return;
            }

            RequestAutoDeleteSync();
        }

        private void AutoDeleteWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            // oldPath 被重命名离开后，等价于原路径删除。
            if (!ShouldTriggerAutoDeleteSync(e?.OldFullPath))
            {
                return;
            }

            RequestAutoDeleteSync();
        }

        private void AutoDeleteWatcher_Error(object sender, ErrorEventArgs e)
        {
            // 监听器异常时兜底触发一次删除同步，再重建监听器。
            RequestAutoDeleteSync();
            _ = Dispatcher.BeginInvoke(new Action(() => RebuildAutoDeleteWatchers(force: true)), DispatcherPriority.Background);
        }

        private static bool ShouldTriggerAutoDeleteSync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(extension))
            {
                // 无扩展名通常是目录删除事件。
                return true;
            }

            return ImportManager.AllExtensions.Any(ext =>
                string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase));
        }

        private void RequestAutoDeleteSync()
        {
            lock (_autoDeleteSyncGate)
            {
                _autoDeleteSyncQueued = true;
                if (_autoDeleteSyncRunning)
                {
                    return;
                }

                _autoDeleteSyncRunning = true;
            }

            _ = RunAutoDeleteSyncLoopAsync(_autoDeleteSyncCts.Token);
        }

        private async Task RunAutoDeleteSyncLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(AutoDeleteSyncDebounce, token);

                    lock (_autoDeleteSyncGate)
                    {
                        if (!_autoDeleteSyncQueued)
                        {
                            _autoDeleteSyncRunning = false;
                            return;
                        }

                        _autoDeleteSyncQueued = false;
                    }

                    var result = await RunAutoDeleteSyncOnceAsync(token);
                    if (result.removed <= 0)
                    {
                        continue;
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        ReloadProjectsPreservingTreeState();
                        LoadSearchScopes();
                        ShowStatus($"自动删除同步完成: 删除 {result.removed}");
                        RebuildAutoDeleteWatchers(force: true);
                    }, DispatcherPriority.Background, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_autoDeleteSyncGate)
                {
                    _autoDeleteSyncRunning = false;
                }
            }
        }

        private (int added, int removed, int updated) RunAutoDeleteSyncOnceAsyncSafe(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string dbPath = DatabaseManagerService.GetDatabasePath();
            using var isolatedDbManager = new DatabaseManager(dbPath);
            var isolatedImportManager = new ImportManager(isolatedDbManager, new SortManager());
            return isolatedImportManager.SyncAllFoldersRemovalsOnly();
        }

        private Task<(int added, int removed, int updated)> RunAutoDeleteSyncOnceAsync(CancellationToken token)
        {
            return Task.Run(() => RunAutoDeleteSyncOnceAsyncSafe(token), token);
        }
    }
}

