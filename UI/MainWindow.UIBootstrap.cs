using System;
using System.Diagnostics;
using System.Threading;
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

            stepSw.Restart();
            InitializeVideoPlayer();
            StartupPerfLogger.Mark("MainWindow.InitializeUI.VideoPlayer.Initialized", $"ElapsedMs={stepSw.ElapsedMilliseconds}");

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
