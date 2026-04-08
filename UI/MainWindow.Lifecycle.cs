using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private const string DeferredSyncFolderQuickStampSettingKey = "ProjectTree.DeferredSyncFolderQuickStamps";
        private const string DeferredSyncLastFullUtcTicksSettingKey = "ProjectTree.DeferredSyncLastFullUtcTicks";
        private static readonly TimeSpan DeferredSyncForceFullInterval = TimeSpan.FromHours(12);
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
#if DEBUG
            InitializeTopBarButtonDebugProbe();
#endif
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
            EnsureAutoDeleteSyncStarted();
            // 静默后台预热 LibVLC（不占 UI 线程），降低首次播放初始化开销。
            StartDeferredVideoPlayerInitialization(delayMs: 2000);
            StartupPerfLogger.Mark("MainWindow.VideoPlayer.Prewarm.Queued", "Reason=StartupCoreReady; DelayMs=2000");
            StartDeferredMediaPlaylistPrewarm();
            StartupPerfLogger.Mark("MainWindow.VideoPlaylist.Prewarm.Queued", "Reason=StartupCoreReady");
            QueueDeferredStartupFolderSync();

            // 延迟5秒后检查更新，避免影响启动速度
            await Task.Delay(5000);
            StartupPerfLogger.Mark("MainWindow.UpdateCheck.DelayElapsed");
            await CheckForUpdatesAsync();
            StartupPerfLogger.Mark("MainWindow.UpdateCheck.Completed");
        }

#if DEBUG
        private static bool EnableTopBarButtonProbe =>
            string.Equals(
                Environment.GetEnvironmentVariable("CANVAS_ENABLE_BTN_PROBE"),
                "1",
                StringComparison.Ordinal);
        private int _topBarButtonProbeLayoutTickCount;

        private void InitializeTopBarButtonDebugProbe()
        {
            if (!EnableTopBarButtonProbe)
            {
                return;
            }

            try
            {
                if (BtnColorEffect == null || BtnAiCaption == null)
                {
                    System.Diagnostics.Debug.WriteLine("[BtnProbe] init skipped: button reference is null.");
                    return;
                }

                BtnColorEffect.SizeChanged -= TopBarButtonProbe_SizeChanged;
                BtnAiCaption.SizeChanged -= TopBarButtonProbe_SizeChanged;
                BtnColorEffect.SizeChanged += TopBarButtonProbe_SizeChanged;
                BtnAiCaption.SizeChanged += TopBarButtonProbe_SizeChanged;
                LayoutUpdated -= TopBarButtonProbe_LayoutUpdated;
                LayoutUpdated += TopBarButtonProbe_LayoutUpdated;

                LogTopBarButtonMetrics("init");

                Dispatcher.BeginInvoke(new Action(() => LogTopBarButtonMetrics("dispatcher-loaded")),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                Dispatcher.BeginInvoke(new Action(() => LogTopBarButtonMetrics("dispatcher-render")),
                    System.Windows.Threading.DispatcherPriority.Render);
                Dispatcher.BeginInvoke(new Action(() => LogTopBarButtonMetrics("dispatcher-idle")),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Dispatcher.BeginInvoke(new Action(() => LogTopBarButtonMetrics("dispatcher-context-idle")),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BtnProbe] init failed: {ex.Message}");
            }
        }

        private void TopBarButtonProbe_LayoutUpdated(object sender, EventArgs e)
        {
            // 只采样前几次布局，避免刷屏。
            if (_topBarButtonProbeLayoutTickCount >= 6)
            {
                LayoutUpdated -= TopBarButtonProbe_LayoutUpdated;
                return;
            }

            _topBarButtonProbeLayoutTickCount++;
            LogTopBarButtonMetrics($"layout-updated#{_topBarButtonProbeLayoutTickCount}");
        }

        private void TopBarButtonProbe_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var source = sender as FrameworkElement;
            string sourceName = source?.Name ?? "<unknown>";
            System.Diagnostics.Debug.WriteLine(
                $"[BtnProbe] SizeChanged source={sourceName}, widthChanged={e.WidthChanged}, heightChanged={e.HeightChanged}, " +
                $"new=({e.NewSize.Width:0.##},{e.NewSize.Height:0.##}), prev=({e.PreviousSize.Width:0.##},{e.PreviousSize.Height:0.##})");
            LogTopBarButtonMetrics($"size-changed:{sourceName}");
        }

        private void LogTopBarButtonMetrics(string stage)
        {
            if (BtnColorEffect == null || BtnAiCaption == null)
            {
                return;
            }

            string colorStyle = BtnColorEffect.Style?.ToString() ?? "<null>";
            string captionStyle = BtnAiCaption.Style?.ToString() ?? "<null>";
            string captionWidthBinding = BtnAiCaption.ReadLocalValue(FrameworkElement.WidthProperty)?.ToString() ?? "<unset>";
            var widthBindingExpr = System.Windows.Data.BindingOperations.GetBindingExpression(BtnAiCaption, FrameworkElement.WidthProperty);
            string widthBindingStatus = widthBindingExpr?.Status.ToString() ?? "<no-binding-expr>";
            string widthBindingResolvedSource = widthBindingExpr?.ResolvedSource?.GetType().Name ?? "<null>";
            var colorParent = BtnColorEffect.Parent as FrameworkElement;
            var captionParent = BtnAiCaption.Parent as FrameworkElement;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            double widthDiff = Math.Abs(BtnColorEffect.ActualWidth - BtnAiCaption.ActualWidth);
            double heightDiff = Math.Abs(BtnColorEffect.ActualHeight - BtnAiCaption.ActualHeight);

            System.Diagnostics.Debug.WriteLine(
                $"[BtnProbe:{stage}] " +
                $"ColorEffect Actual={BtnColorEffect.ActualWidth:0.##}x{BtnColorEffect.ActualHeight:0.##}, Desired={BtnColorEffect.DesiredSize.Width:0.##}x{BtnColorEffect.DesiredSize.Height:0.##}, Width={BtnColorEffect.Width}, MinWidth={BtnColorEffect.MinWidth}, Padding={BtnColorEffect.Padding}, Margin={BtnColorEffect.Margin}, FontSize={BtnColorEffect.FontSize}, Style={colorStyle}");

            System.Diagnostics.Debug.WriteLine(
                $"[BtnProbe:{stage}] " +
                $"AiCaption  Actual={BtnAiCaption.ActualWidth:0.##}x{BtnAiCaption.ActualHeight:0.##}, Desired={BtnAiCaption.DesiredSize.Width:0.##}x{BtnAiCaption.DesiredSize.Height:0.##}, Width={BtnAiCaption.Width}, MinWidth={BtnAiCaption.MinWidth}, Padding={BtnAiCaption.Padding}, Margin={BtnAiCaption.Margin}, FontSize={BtnAiCaption.FontSize}, Style={captionStyle}, WidthLocalValue={captionWidthBinding}, WidthBindingStatus={widthBindingStatus}, WidthBindingSourceType={widthBindingResolvedSource}");

            System.Diagnostics.Debug.WriteLine(
                $"[BtnProbe:{stage}] " +
                $"Window={ActualWidth:0.##}x{ActualHeight:0.##}, DpiScale=({dpi.DpiScaleX:0.###},{dpi.DpiScaleY:0.###}), " +
                $"ColorParent={colorParent?.ActualWidth:0.##}x{colorParent?.ActualHeight:0.##}, CaptionParent={captionParent?.ActualWidth:0.##}x{captionParent?.ActualHeight:0.##}, " +
                $"Diff width={widthDiff:0.##}, height={heightDiff:0.##}");

            if (widthDiff > 0.5 || heightDiff > 0.5)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BtnProbe:{stage}] [WARN] 按钮尺寸不一致: ColorEffect={BtnColorEffect.ActualWidth:0.##}x{BtnColorEffect.ActualHeight:0.##}, " +
                    $"AiCaption={BtnAiCaption.ActualWidth:0.##}x{BtnAiCaption.ActualHeight:0.##}");
            }
        }
#endif

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
                var quickCheckSw = System.Diagnostics.Stopwatch.StartNew();
                var quickStamps = CaptureDeferredSyncFolderQuickStamps();
                bool hasQuickStampBaseline = HasDeferredSyncQuickStampBaseline();
                if (!hasQuickStampBaseline && quickStamps.Count > 0)
                {
                    SaveDeferredSyncQuickStampSnapshot(quickStamps);
                    SaveDeferredSyncLastFullUtc(DateTime.UtcNow);
                    StartupPerfLogger.Mark(
                        "MainWindow.StartupFolderSync.Deferred.QuickCheck.Completed",
                        $"ElapsedMs={quickCheckSw.ElapsedMilliseconds}; FolderCount={quickStamps.Count}; QuickStampChanged=False; ForceFullSync=False; BaselineInitialized=True");
                    StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.SyncAllFolders.Skipped", "Reason=QuickStampBaselineInitialized");
                    StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.Completed", $"ElapsedMs={totalSw.ElapsedMilliseconds}; SkippedFullSync=True");
                    return;
                }

                bool forceFullSync = ShouldForceDeferredFullSync();
                bool quickStampChanged = HasDeferredSyncQuickStampChanged(quickStamps);
                StartupPerfLogger.Mark(
                    "MainWindow.StartupFolderSync.Deferred.QuickCheck.Completed",
                    $"ElapsedMs={quickCheckSw.ElapsedMilliseconds}; FolderCount={quickStamps.Count}; QuickStampChanged={quickStampChanged}; ForceFullSync={forceFullSync}; BaselineInitialized=False");

                if (!quickStampChanged && !forceFullSync)
                {
                    StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.SyncAllFolders.Skipped", "Reason=QuickStampUnchanged");
                    StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.Completed", $"ElapsedMs={totalSw.ElapsedMilliseconds}; SkippedFullSync=True");
                    return;
                }

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
                SaveDeferredSyncQuickStampSnapshot(quickStamps);
                SaveDeferredSyncLastFullUtc(DateTime.UtcNow);

                if (_startupDeferredWorkCts.IsCancellationRequested)
                {
                    StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Deferred.Cancelled");
                    return;
                }

                var refreshSw = System.Diagnostics.Stopwatch.StartNew();
                bool hasMediaChanges = added > 0 || removed > 0 || updated > 0;
                LoadProjects(enableDetailedIcons: true, clearMediaPlaylistCache: hasMediaChanges);
                NotifyProjectFoldersMayChanged();
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

        private Dictionary<int, long> CaptureDeferredSyncFolderQuickStamps()
        {
            var result = new Dictionary<int, long>();
            try
            {
                var folders = DatabaseManagerService.GetAllFolders();
                foreach (var folder in folders)
                {
                    long stamp = 0;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(folder.Path) && Directory.Exists(folder.Path))
                        {
                            stamp = Directory.GetLastWriteTimeUtc(folder.Path).Ticks;
                        }
                    }
                    catch
                    {
                        stamp = 0;
                    }

                    result[folder.Id] = stamp;
                }
            }
            catch
            {
            }

            return result;
        }

        private bool HasDeferredSyncQuickStampChanged(Dictionary<int, long> currentStamps)
        {
            var previous = LoadDeferredSyncQuickStampSnapshot();
            if (previous.Count != currentStamps.Count)
            {
                return true;
            }

            foreach (var pair in currentStamps)
            {
                if (!previous.TryGetValue(pair.Key, out long previousStamp) || previousStamp != pair.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasDeferredSyncQuickStampBaseline()
        {
            try
            {
                string raw = DatabaseManagerService.GetUISetting(DeferredSyncFolderQuickStampSettingKey, string.Empty) ?? string.Empty;
                return !string.IsNullOrWhiteSpace(raw);
            }
            catch
            {
                return false;
            }
        }

        private Dictionary<int, long> LoadDeferredSyncQuickStampSnapshot()
        {
            var result = new Dictionary<int, long>();
            try
            {
                string raw = DatabaseManagerService.GetUISetting(DeferredSyncFolderQuickStampSettingKey, string.Empty) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return result;
                }

                foreach (string token in raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = token.Split('=');
                    if (pair.Length != 2)
                    {
                        continue;
                    }

                    if (!int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int folderId) || folderId <= 0)
                    {
                        continue;
                    }

                    if (!long.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long stamp))
                    {
                        continue;
                    }

                    result[folderId] = stamp;
                }
            }
            catch
            {
            }

            return result;
        }

        private void SaveDeferredSyncQuickStampSnapshot(Dictionary<int, long> stamps)
        {
            try
            {
                string serialized = string.Join("|", stamps
                    .OrderBy(p => p.Key)
                    .Select(p => $"{p.Key.ToString(CultureInfo.InvariantCulture)}={p.Value.ToString(CultureInfo.InvariantCulture)}"));
                DatabaseManagerService.SaveUISetting(DeferredSyncFolderQuickStampSettingKey, serialized);
            }
            catch
            {
            }
        }

        private bool ShouldForceDeferredFullSync()
        {
            try
            {
                string rawTicks = DatabaseManagerService.GetUISetting(DeferredSyncLastFullUtcTicksSettingKey, string.Empty) ?? string.Empty;
                if (!long.TryParse(rawTicks, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) || ticks <= 0)
                {
                    return true;
                }

                DateTime lastUtc = new DateTime(ticks, DateTimeKind.Utc);
                return DateTime.UtcNow - lastUtc >= DeferredSyncForceFullInterval;
            }
            catch
            {
                return true;
            }
        }

        private void SaveDeferredSyncLastFullUtc(DateTime utcNow)
        {
            try
            {
                DatabaseManagerService.SaveUISetting(
                    DeferredSyncLastFullUtcTicksSettingKey,
                    utcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
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
                StopAutoDeleteSync();
                DisposeLiveCaption();
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



