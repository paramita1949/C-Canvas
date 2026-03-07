using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows;
using ImageColorChanger.Core;
using ImageColorChanger.Managers;
using ImageColorChanger.Services;
using ImageColorChanger.Utils;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow UI 启动装配
    /// </summary>
    public partial class MainWindow
    {
        private readonly CancellationTokenSource _startupDeferredWorkCts = new();
        private bool _isDeferredStartupUiWorkScheduled;
        private bool _videoPlayerDeferredInitQueued;
        private long _lastProjectTreeInteractionUtcTicks;
        private static readonly TimeSpan VideoPrewarmQuietWindow = TimeSpan.FromSeconds(4);

        private void InitializeUI()
        {
            var totalSw = Stopwatch.StartNew();
            StartupPerfLogger.Mark("MainWindow.InitializeUI.Internal.Begin");

            var stepSw = Stopwatch.StartNew();
            GPUContext.EnableBitmapCache(ImageScrollViewer, enableHighQuality: true);
            GPUContext.EnableBitmapCache(ImageDisplay, enableHighQuality: true);
            StartupPerfLogger.Mark("MainWindow.InitializeUI.BitmapCache.Enabled", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            stepSw.Restart();
            InitializeDatabase();
            StartupPerfLogger.Mark("MainWindow.InitializeUI.Database.Initialized", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            stepSw.Restart();
            InitializeKeyframeSystem();
            StartupPerfLogger.Mark("MainWindow.InitializeUI.KeyframeSystem.Initialized", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            stepSw.Restart();
            _imageProcessor = new ImageProcessor(new MainWindowImageProcessingHost(this), _gpuContext, ImageScrollViewer, ImageDisplay, ImageContainer);
            _skiaRenderer = _mainWindowServices.GetRequired<SkiaTextRenderer>();
            StartupPerfLogger.Mark("MainWindow.InitializeUI.ImagePipeline.Ready", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            stepSw.Restart();
            LoadSettings();
            InitializeAdaptiveFontSystem();
            StartupPerfLogger.Mark("MainWindow.InitializeUI.SettingsAndFont.Ready", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            stepSw.Restart();
            _projectionManager = CreateProjectionManager();
            StartupPerfLogger.Mark("MainWindow.InitializeUI.ProjectionManager.Created", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            stepSw.Restart();
            _projectionManager.ProjectionStateChanged += OnProjectionStateChanged;
            _projectionManager.ProjectionVideoViewLoaded += OnProjectionVideoViewLoaded;
            StartupPerfLogger.Mark("MainWindow.InitializeUI.ProjectionEvents.Wired", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            stepSw.Restart();
            var dbManager = DatabaseManagerService;
            _originalManager = new OriginalManager(dbManager, this);
            _preloadCacheManager = new PreloadCacheManager(_imageProcessor.GetMemoryCache(), dbManager, _imageProcessor);
            _projectTreeSelectionStateController = new Modules.ProjectTreeSelectionStateController(dbManager, _originalManager);
            _projectTreeFolderMenuStateController = new Modules.ProjectTreeFolderMenuStateController(dbManager, _originalManager);
            StartupPerfLogger.Mark("MainWindow.InitializeUI.Controllers.Ready", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            StartupPerfLogger.Mark("MainWindow.InitializeUI.VideoPlayer.Deferred", "Reason=Cold-start video stack initialization can be expensive");

            stepSw.Restart();
            InitializeProjectTreeBootstrap();
            StartupPerfLogger.Mark("MainWindow.InitializeUI.ProjectTree.BootstrapInitialized", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            stepSw.Restart();
            InitializeScreenSelector();
            ImageScrollViewer.ScrollChanged += ImageScrollViewer_ScrollChanged;
            StartupPerfLogger.Mark("MainWindow.InitializeUI.ScreenSelectorAndScrollHook.Ready", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

            StartupPerfLogger.Mark("MainWindow.InitializeUI.ProjectTree.Load.Deferred", "Reason=Window_Loaded will refresh after startup sync");
            ScheduleDeferredStartupUiWork();

            Loaded += (s, e) =>
            {
                Focus();
                Activate();
            };

            StartupPerfLogger.Mark("MainWindow.InitializeUI.Internal.End", $"ElapsedMs={totalSw.ElapsedMilliseconds}");
        }

        private void ScheduleDeferredStartupUiWork()
        {
            if (_isDeferredStartupUiWorkScheduled)
            {
                return;
            }

            _isDeferredStartupUiWorkScheduled = true;
            StartupPerfLogger.Mark("MainWindow.InitializeUI.DeferredInit.Queued");
            Loaded += OnLoadedScheduleDeferredStartupUiWork;
        }

        private void OnLoadedScheduleDeferredStartupUiWork(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoadedScheduleDeferredStartupUiWork;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_startupDeferredWorkCts.IsCancellationRequested)
                {
                    StartupPerfLogger.Mark("MainWindow.InitializeUI.DeferredInit.Cancelled");
                    return;
                }

                var sw = Stopwatch.StartNew();
                InitializeGlobalHotKeys();
                StartupPerfLogger.Mark("MainWindow.InitializeUI.DeferredInit.GlobalHotKeys.Initialized", $"ElapsedMs={sw.ElapsedMilliseconds}");

                sw.Restart();
                InitializeShortcutManagers();
                StartupPerfLogger.Mark("MainWindow.InitializeUI.DeferredInit.ShortcutManagers.Initialized", $"ElapsedMs={sw.ElapsedMilliseconds}");
            }));
        }

        private void StartDeferredVideoPlayerInitialization(int delayMs = 2000)
        {
            if (_videoPlayerDeferredInitQueued)
            {
                return;
            }

            _videoPlayerDeferredInitQueued = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, _startupDeferredWorkCts.Token);
                    }

                    await WaitForProjectTreeIdleAsync(_startupDeferredWorkCts.Token);

                    if (_startupDeferredWorkCts.IsCancellationRequested)
                    {
                        StartupPerfLogger.Mark("MainWindow.VideoPlayer.DeferredInit.Cancelled");
                        return;
                    }

                    if (_videoPlayerManager != null)
                    {
                        StartupPerfLogger.Mark("MainWindow.VideoPlayer.DeferredInit.Skipped", "Reason=AlreadyInitialized");
                        return;
                    }

                    var sw = Stopwatch.StartNew();
                    StartupPerfLogger.Mark("MainWindow.VideoPlayer.Prewarm.Begin", "Reason=DeferredAfterStartupCoreReady");
                    VideoPlayerManager.PrewarmLibVlc();
                    StartupPerfLogger.Mark("MainWindow.VideoPlayer.Prewarm.Completed", $"ElapsedMs={sw.ElapsedMilliseconds}");
                }
                catch (TaskCanceledException)
                {
                    StartupPerfLogger.Mark("MainWindow.VideoPlayer.DeferredInit.Cancelled");
                }
                catch (OperationCanceledException)
                {
                    StartupPerfLogger.Mark("MainWindow.VideoPlayer.DeferredInit.Cancelled");
                }
            });
        }

        private async Task WaitForProjectTreeIdleAsync(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var idle = GetProjectTreeIdleDuration();
                if (idle >= VideoPrewarmQuietWindow)
                {
                    return;
                }

                await Task.Delay(800, token);
            }
        }

        private TimeSpan GetProjectTreeIdleDuration()
        {
            long ticks = Interlocked.Read(ref _lastProjectTreeInteractionUtcTicks);
            if (ticks <= 0)
            {
                return TimeSpan.MaxValue;
            }

            var lastUtc = new DateTime(ticks, DateTimeKind.Utc);
            var delta = DateTime.UtcNow - lastUtc;
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        private void MarkProjectTreeInteractionForVideoPrewarm()
        {
            Interlocked.Exchange(ref _lastProjectTreeInteractionUtcTicks, DateTime.UtcNow.Ticks);
        }

        private void ForwardProjectionKeyDownFromProjection(System.Windows.Input.KeyEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            RaiseEvent(new System.Windows.Input.KeyEventArgs(
                e.KeyboardDevice,
                e.InputSource,
                e.Timestamp,
                e.Key)
            {
                RoutedEvent = Window.PreviewKeyDownEvent
            });
        }
    }
}
