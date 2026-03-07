using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using ImageColorChanger.Services;
using ImageColorChanger.Utils;
using ImageColorChanger.Database;
using ImageColorChanger.Managers;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private const string BaseWindowTitle = "咏慕投影";
        private VersionInfo _pendingTitleUpdateVersionInfo;
        private bool _startupFolderSyncDeferredQueued;
        #region 窗口生命周期事件

        /// <summary>
        /// 窗口加载完成后执行初始化任务
        /// </summary>
        private async void Window_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            StartupPerfLogger.Mark("MainWindow.WindowLoaded.Begin");
            await EnsureAuthInitializedAsync();
            StartupPerfLogger.Mark(
                "MainWindow.AuthInitialized",
                $"IsAuthenticated={_authService.IsAuthenticated}; Username={_authService.Username ?? "<null>"}; RemainingDays={_authService.RemainingDays}");
            // 调试：输出幻灯片按钮的所有间距相关属性（已完成调试，注释掉）
            /*
            System.Diagnostics.Debug.WriteLine("========== 幻灯片按钮间距调试信息 ==========");

            var buttons = new[]
            {
                ("A+", BtnIncreaseFontSize),
                ("A-", BtnDecreaseFontSize),
                ("B", BtnBold),
                ("A", BtnTextColor)
            };

            foreach (var (name, btn) in buttons)
            {
                if (btn != null)
                {
                    System.Diagnostics.Debug.WriteLine($"\n【{name}】按钮:");
                    System.Diagnostics.Debug.WriteLine($"  Margin: {btn.Margin}");
                    System.Diagnostics.Debug.WriteLine($"  Padding: {btn.Padding}");
                    System.Diagnostics.Debug.WriteLine($"  BorderThickness: {btn.BorderThickness}");
                    System.Diagnostics.Debug.WriteLine($"  Width: {btn.Width}, Height: {btn.Height}");
                    System.Diagnostics.Debug.WriteLine($"  ActualWidth: {btn.ActualWidth}, ActualHeight: {btn.ActualHeight}");
                }
            }

            System.Diagnostics.Debug.WriteLine("\n==============================================");
            */

            // 恢复圣经历史记录+按钮区域高度
            if (BibleHistoryAndButtonRow != null && _configManager.BibleHistoryRowHeight > 0)
            {
                // 如果保存了高度值，使用固定高度
                BibleHistoryAndButtonRow.Height = new GridLength(_configManager.BibleHistoryRowHeight);
                // 经文选择表格使用剩余空间
                if (BibleSelectionTableRow != null)
                {
                    BibleSelectionTableRow.Height = new GridLength(1, GridUnitType.Star);
                }
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" 已恢复圣经历史记录+按钮区域高度: {_configManager.BibleHistoryRowHeight}");
#endif
            }
            
            // 初始化圣经服务
            var bibleInitSw = System.Diagnostics.Stopwatch.StartNew();
            InitializeBibleService();
            StartupPerfLogger.Mark("MainWindow.InitializeBibleService.Completed", $"ElapsedMs={bibleInitSw.ElapsedMilliseconds}");

            // 启动关键路径：只做轻量对账，不做全量磁盘扫描。
            var startupSyncSw = System.Diagnostics.Stopwatch.StartNew();
            StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Begin");
            try
            {
                // 清理旧版“图片绑定歌词”残留，歌词模块仅保留独立歌曲数据。
                var purgeSw = System.Diagnostics.Stopwatch.StartNew();
                PurgeLegacyImageLinkedLyricsProjects();
                StartupPerfLogger.Mark("MainWindow.StartupFolderSync.PurgeLegacyLyrics.Completed", $"ElapsedMs={purgeSw.ElapsedMilliseconds}");

                // 启动时先做一次映射对账，确保历史数据迁移后读路径稳定。
                var reconcileSw = System.Diagnostics.Stopwatch.StartNew();
                var reconcileResult = DatabaseManagerService.ReconcileFolderImageLinks();
                StartupPerfLogger.Mark(
                    "MainWindow.StartupFolderSync.Reconcile.Completed",
                    $"ElapsedMs={reconcileSw.ElapsedMilliseconds}; MissingLinks={reconcileResult.missingLinks}; StaleLinks={reconcileResult.staleLinks}; AddedLinks={reconcileResult.addedLinks}; RemovedLinks={reconcileResult.removedLinks}");
#if DEBUG
                // System.Diagnostics.Trace.WriteLine(
                //     $"[MainWindow] 启动对账: missingLinks={reconcileResult.missingLinks}, staleLinks={reconcileResult.staleLinks}, addedLinks={reconcileResult.addedLinks}, removedLinks={reconcileResult.removedLinks}");
#endif

            }
            catch (Exception)
            {
                // 静默失败，不影响用户使用
#if DEBUG
                // System.Diagnostics.Trace.WriteLine($"[MainWindow] 启动时同步失败: {ex.Message}");
                // System.Diagnostics.Trace.WriteLine($"[MainWindow] 启动时同步失败堆栈: {ex.StackTrace}");
#endif
            }

            // 同步后刷新项目树和搜索范围
            var refreshTreeSw = System.Diagnostics.Stopwatch.StartNew();
            LoadProjects(enableDetailedIcons: false);
            StartupPerfLogger.Mark("MainWindow.StartupFolderSync.LoadProjects.Completed", $"ElapsedMs={refreshTreeSw.ElapsedMilliseconds}");

            refreshTreeSw.Restart();
            LoadSearchScopes();
            StartupPerfLogger.Mark("MainWindow.StartupFolderSync.LoadSearchScopes.Completed", $"ElapsedMs={refreshTreeSw.ElapsedMilliseconds}");
            StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Completed", $"ElapsedMs={startupSyncSw.ElapsedMilliseconds}");
            StartupPerfLogger.Mark("MainWindow.StartupCoreReady");
            StartDeferredVideoPlayerInitialization();
            StartupPerfLogger.Mark("MainWindow.VideoPlayer.DeferredInit.Queued", "Reason=StartupCoreReady");
            StartDeferredMediaPlaylistPrewarm();
            StartupPerfLogger.Mark("MainWindow.VideoPlaylist.Prewarm.Queued", "Reason=StartupCoreReady");
            QueueDeferredStartupFolderSync();

            // 延迟5秒后检查更新，避免影响启动速度
            await Task.Delay(5000);
            StartupPerfLogger.Mark("MainWindow.UpdateCheck.DelayElapsed");
            await CheckForUpdatesAsync();
            StartupPerfLogger.Mark("MainWindow.UpdateCheck.Completed");
        }

        private void QueueDeferredStartupFolderSync()
        {
            if (_startupFolderSyncDeferredQueued)
            {
                return;
            }

            _startupFolderSyncDeferredQueued = true;
            StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.Queued", "Reason=AfterStartupCoreReady");
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(RunDeferredStartupFolderSync));
        }

        private async void RunDeferredStartupFolderSync()
        {
            if (_startupDeferredWorkCts.IsCancellationRequested)
            {
                StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.Cancelled");
                return;
            }

            try
            {
                var importManager = ImportManagerService;
                if (importManager == null)
                {
                    StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.Skipped", "Reason=ImportManagerUnavailable");
                    return;
                }

                var totalSw = System.Diagnostics.Stopwatch.StartNew();
                var syncSw = System.Diagnostics.Stopwatch.StartNew();
                var dbPath = DatabaseManagerService.GetDatabasePath();
                var syncResult = await Task.Run(() =>
                {
                    // 使用独立数据库上下文，避免 UI 线程共享 DbContext 导致竞争与卡顿。
                    using var isolatedDbManager = new DatabaseManager(dbPath);
                    var isolatedImportManager = new ImportManager(isolatedDbManager, new SortManager());
                    return isolatedImportManager.SyncAllFolders();
                });
                var (added, removed, updated) = syncResult;
                StartupPerfLogger.Mark(
                    "MainWindow.StartupFolderSync.Deferred.SyncAllFolders.Completed",
                    $"ElapsedMs={syncSw.ElapsedMilliseconds}; Added={added}; Removed={removed}; Updated={updated}");

                if (_startupDeferredWorkCts.IsCancellationRequested)
                {
                    StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.Cancelled");
                    return;
                }

                var refreshSw = System.Diagnostics.Stopwatch.StartNew();
                bool hasMediaChanges = added > 0 || removed > 0 || updated > 0;
                LoadProjects(enableDetailedIcons: true, clearMediaPlaylistCache: hasMediaChanges);
                StartupPerfLogger.Mark(
                    "MainWindow.StartupFolderSync.Deferred.LoadProjects.Completed",
                    $"ElapsedMs={refreshSw.ElapsedMilliseconds}; ClearPlaylistCache={hasMediaChanges}");

                refreshSw.Restart();
                LoadSearchScopes();
                StartupPerfLogger.Mark(
                    "MainWindow.StartupFolderSync.Deferred.LoadSearchScopes.Completed",
                    $"ElapsedMs={refreshSw.ElapsedMilliseconds}");
                if (hasMediaChanges)
                {
                    // 仅在增删改导致缓存失效时再预热，避免无变更场景二次预热造成额外卡顿。
                    StartDeferredMediaPlaylistPrewarm();
                    StartupPerfLogger.Mark("MainWindow.VideoPlaylist.Prewarm.Queued", "Reason=AfterDeferredFolderSyncRefresh");
                }
                else
                {
                    StartupPerfLogger.Mark("MainWindow.VideoPlaylist.Prewarm.Skipped", "Reason=NoMediaChangesAfterDeferredSync");
                }

                StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.Completed", $"ElapsedMs={totalSw.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                StartupPerfLogger.Error("MainWindow.StartupFolderSync.Deferred.Failed", ex);
            }
        }

        /// <summary>
        /// 检查软件更新
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine("[MainWindow] 开始检查更新...");
//#endif
                var versionInfo = await UpdateService.CheckForUpdatesAsync();
                
                if (versionInfo != null)
                {
                    Dispatcher.Invoke(() => ShowTitleUpdateNotice(versionInfo));
                }
#if DEBUG
                else
                {
                    Dispatcher.Invoke(HideTitleUpdateNotice);
                    System.Diagnostics.Debug.WriteLine("[MainWindow] 当前已是最新版本");
                }
#else
                else
                {
                    Dispatcher.Invoke(HideTitleUpdateNotice);
                }
#endif
            }
            catch (Exception)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine("[MainWindow] 检查更新失败");
//#endif
                Dispatcher.Invoke(HideTitleUpdateNotice);
                // 静默失败，不影响用户使用
            }
        }

        private void ShowTitleUpdateNotice(VersionInfo versionInfo)
        {
            _pendingTitleUpdateVersionInfo = versionInfo;
            RefreshWindowTitleByRuntimeState();
        }

        private void HideTitleUpdateNotice()
        {
            _pendingTitleUpdateVersionInfo = null;
            RefreshWindowTitleByRuntimeState();
        }

        private void RefreshWindowTitleByRuntimeState()
        {
            if (_fpsMonitor?.IsMonitoring == true)
            {
                return;
            }

            if (_pendingTitleUpdateVersionInfo != null)
            {
                Title = $"{BaseWindowTitle} | 新版本 V{_pendingTitleUpdateVersionInfo.Version}";
            }
            else
            {
                Title = BaseWindowTitle;
            }
        }

        /// <summary>
        /// 窗口关闭事件 - 清理资源
        /// </summary>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine("主窗口正在关闭,清理资源...");
                _startupDeferredWorkCts.Cancel();
                MainWindow_Closing(sender, e);
                DisposeBibleSearchComponents();

                // 退出前等待认证状态落盘，避免“登录后重启又要登录”。
                try
                {
                    _authService.FlushAuthStateAsync().GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    // 忽略flush异常，不阻断退出流程
                }
                
                // 保存用户设置
                SaveSettings();

                // 兜底：如果仍在歌词模式，关闭窗口前强制保存一次
                if (_isLyricsMode)
                {
                    try
                    {
                        StopAutoSaveTimer();
                        SaveLyricsProject("Window_Closing", suppressUserError: true);
                    }
                    catch (Exception)
                    {
                        // 忽略歌词保存异常，不阻断退出
                    }
                }
                
                // 注意：PropertyChanged事件使用匿名方法订阅，无法直接取消订阅
                // ViewModel会随窗口关闭自动释放
                // 如果需要，应在订阅时保存匿名方法引用以便取消订阅
                
                // 停止并清理视频播放器
                _mediaModuleController?.Shutdown();
                _mediaModuleController = null;
                _videoPlayerManager = null;
                
                // 关闭投影窗口
                if (_projectionManager != null)
                {
                    _projectionManager.CloseProjection();
                    _projectionManager.Dispose();
                }
                
                // 释放全局热键
                if (_globalHotKeyManager != null)
                {
                    _globalHotKeyManager.Dispose();
                }
                
                // 停止并释放FPS监控器
                if (_fpsMonitor != null)
                {
                    _fpsMonitor.StopMonitoring();
                    _fpsMonitor.Dispose();
                }
                
                // 清理认证服务
                CleanupAuthService();
                
                // 处理圣经历史记录
                HandleBibleHistoryOnClosing();
                
                // 清理数据库连接（关闭WAL文件）
                CleanupDatabase();
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" 资源清理失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理圣经历史记录（退出时根据配置保存或清空）
        /// </summary>
        private void HandleBibleHistoryOnClosing()
        {
            try
            {
                if (_configManager.SaveBibleHistory)
                {
                    // 勾选了保存投影记录，保存当前历史记录
                    SaveBibleHistoryToConfig();
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine("[主窗口] 退出时已保存圣经历史记录");
                    //#endif
                }
                else
                {
                    // 没有勾选，清空历史记录
                    ClearAllBibleHistory();
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine("[主窗口] 退出时已清空圣经历史记录");
                    //#endif
                }
            }
            catch (Exception)
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[主窗口] 处理圣经历史记录失败: {ex.Message}");
                #endif
            }
        }
        
        /// <summary>
        /// 清理数据库连接，确保WAL文件被合并
        /// </summary>
        private void CleanupDatabase()
        {
            try
            {
                try
                {
                    DatabaseManagerService.CheckpointAndCloseConnections();
                }
                catch (Exception)
                {
                    // 忽略错误，不影响退出
                }

                // 释放UI持有的上下文引用（生命周期由 DatabaseManager 统一管理）
                _dbContext = null;
            }
            catch (Exception)
            {
                // 忽略所有数据库清理错误，不影响程序关闭
            }
        }

        #endregion
    }
}



