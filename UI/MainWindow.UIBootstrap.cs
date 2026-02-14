using ImageColorChanger.Core;
using ImageColorChanger.Managers;

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

            _imageProcessor = new ImageProcessor(this, ImageScrollViewer, ImageDisplay, ImageContainer);
            _skiaRenderer = App.GetRequiredService<SkiaTextRenderer>();

            LoadSettings();
            InitializeAdaptiveFontSystem();

            _imageSaveManager = new ImageSaveManager(_imageProcessor);

            _projectionManager = new ProjectionManager(
                this,
                ImageScrollViewer,
                ImageDisplay,
                _imageProcessor,
                ScreenSelector,
                null);

            _projectionManager.ProjectionStateChanged += OnProjectionStateChanged;
            _projectionManager.ProjectionVideoViewLoaded += OnProjectionVideoViewLoaded;

            _originalManager = new OriginalManager(_dbManager, this);
            _preloadCacheManager = new PreloadCacheManager(_imageProcessor.GetMemoryCache(), _dbManager, _imageProcessor);

            InitializeVideoPlayer();

            ProjectTree.ItemsSource = _filteredProjectTreeItems;
            ProjectTree.PreviewMouseLeftButtonDown += ProjectTree_PreviewMouseLeftButtonDown;
            ProjectTree.PreviewMouseMove += ProjectTree_PreviewMouseMove;
            ProjectTree.Drop += ProjectTree_Drop;
            ProjectTree.DragOver += ProjectTree_DragOver;
            ProjectTree.DragLeave += ProjectTree_DragLeave;
            ProjectTree.AllowDrop = true;

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
    }
}
