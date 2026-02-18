using System.Windows;
using ImageColorChanger.Core;
using ImageColorChanger.Managers;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow UI 启动装配
    /// </summary>
    public partial class MainWindow
    {
        private void InitializeUI()
        {
            GPUContext.EnableBitmapCache(ImageScrollViewer, enableHighQuality: true);
            GPUContext.EnableBitmapCache(ImageDisplay, enableHighQuality: true);

            InitializeDatabase();
            InitializeKeyframeSystem();

            _imageProcessor = new ImageProcessor(new MainWindowImageProcessingHost(this), _gpuContext, ImageScrollViewer, ImageDisplay, ImageContainer);
            _skiaRenderer = _mainWindowServices.GetRequired<SkiaTextRenderer>();

            LoadSettings();
            InitializeAdaptiveFontSystem();

            _projectionManager = CreateProjectionManager();

            _projectionManager.ProjectionStateChanged += OnProjectionStateChanged;
            _projectionManager.ProjectionVideoViewLoaded += OnProjectionVideoViewLoaded;

            var dbManager = DatabaseManagerService;
            _originalManager = new OriginalManager(dbManager, this);
            _preloadCacheManager = new PreloadCacheManager(_imageProcessor.GetMemoryCache(), dbManager, _imageProcessor);
            _projectTreeSelectionStateController = new Modules.ProjectTreeSelectionStateController(dbManager, _originalManager);
            _projectTreeFolderMenuStateController = new Modules.ProjectTreeFolderMenuStateController(dbManager, _originalManager);

            InitializeVideoPlayer();

            InitializeProjectTreeBootstrap();

            InitializeScreenSelector();
            ImageScrollViewer.ScrollChanged += ImageScrollViewer_ScrollChanged;
            LoadProjects();

            InitializeGlobalHotKeys();
            InitializeShortcutManagers();

            Loaded += (s, e) =>
            {
                Focus();
                Activate();
            };
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
