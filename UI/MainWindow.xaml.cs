using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SkiaSharp;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Collections.ObjectModel;
using System.Linq;
using ImageColorChanger.Core;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using ImageColorChanger.Services;
using ImageColorChanger.UI.Composition;
using ImageColorChanger.UI.Modules;
using LibVLCSharp.WPF;

namespace ImageColorChanger.UI
{
    public partial class MainWindow : Window, INotifyPropertyChanged, Managers.Keyframes.IKeyframeUiHost
    {
        #region INotifyPropertyChanged 实现

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region 字段

        #region 常量定义

        // 时间相关常量（毫秒）
        private const int BUTTON_DEBOUNCE_MILLISECONDS = 300;  // 按钮防抖时间
        private const int UI_UPDATE_DELAY_MILLISECONDS = 100;   // UI更新延迟

        // 缩放相关常量
        private const double MinZoom = Constants.MinZoomRatio;
        private const double MaxZoom = Constants.MaxZoomRatio;
        private const double ZoomStep = 0.05;

        // 时间转换常量
        private const int MILLISECONDS_PER_SECOND = 1000;

        // 默认颜色常量（RGB）
        private const byte DEFAULT_TARGET_COLOR_R = 255;  // 秋麒麟色 R
        private const byte DEFAULT_TARGET_COLOR_G = 165;  // 秋麒麟色 G
        private const byte DEFAULT_TARGET_COLOR_B = 79;   // 秋麒麟色 B
        private const string DEFAULT_TARGET_COLOR_NAME = "秋麒麟";

        // UI按钮激活颜色（RGB）
        private const byte BUTTON_ACTIVE_COLOR_R = 144;   // 浅绿色 R (LightGreen)
        private const byte BUTTON_ACTIVE_COLOR_G = 238;   // 浅绿色 G
        private const byte BUTTON_ACTIVE_COLOR_B = 144;   // 浅绿色 B

        // UI按钮强调颜色（RGB）
        private const byte BUTTON_EMPHASIS_COLOR_R = 255;  // 金色 R
        private const byte BUTTON_EMPHASIS_COLOR_G = 215;  // 金色 G
        private const byte BUTTON_EMPHASIS_COLOR_B = 0;    // 金色 B

        // 播放模式图标颜色（十六进制）
        private const string ICON_COLOR_SEQUENTIAL = "#2196F3";  // 顺序播放 - 蓝色
        private const string ICON_COLOR_RANDOM = "#FF9800";      // 随机播放 - 橙色
        private const string ICON_COLOR_LOOP = "#4CAF50";        // 列表循环 - 绿色
        private const string ICON_COLOR_PALETTE = "#FF6B6B";     // 变色标记 - 红色
        private const string ICON_COLOR_FILE = "#95E1D3";        // 文件图标 - 青色
        private const string ICON_COLOR_TEXT = "#2196F3";        // 文本项目 - 蓝色
        private const string ICON_COLOR_DEFAULT = "#666666";     // 默认图标 - 灰色

        #endregion

        // 图像处理相关
        private ImageProcessor _imageProcessor;
        private string _imagePath;

        // 图片缩放相关
        private double _currentZoom = 1.0;

        // 图片拖动相关
        private bool _isDragging = false;
        private System.Windows.Point _dragStartPoint;

        // 变色功能相关
        private bool _isColorEffectEnabled = false;
        private SKColor _currentTargetColor = new SKColor(DEFAULT_TARGET_COLOR_R, DEFAULT_TARGET_COLOR_G, DEFAULT_TARGET_COLOR_B);
        private string _currentTargetColorName = DEFAULT_TARGET_COLOR_NAME;
        private int? _currentFolderId = null; // 当前文件夹ID，用于判断是否切换了文件夹

        // 项目数据
        private ObservableCollection<ProjectTreeItem> _projectTreeItems = new ObservableCollection<ProjectTreeItem>();
        private ObservableCollection<ProjectTreeItem> _filteredProjectTreeItems = new ObservableCollection<ProjectTreeItem>(); // 🆕 过滤后的项目树
        private int _currentImageId = 0; // 当前加载的图片ID
        
        // 🆕 视图模式枚举
        private enum NavigationViewMode
        {
            Files,      // 文件模式：显示文件夹和单文件
            Projects,   // 项目模式：显示TextProject节点
            Bible       // 圣经模式：显示圣经导航
        }
        private NavigationViewMode _currentViewMode = NavigationViewMode.Files; // 🆕 当前视图模式，默认显示文件

        // 原图模式相关
        private bool _originalMode = false;
        private OriginalDisplayMode _originalDisplayMode = OriginalDisplayMode.Stretch;

        // TreeView拖拽相关
        private ProjectTreeItem _draggedItem = null;
        private ProjectTreeItem _dragOverItem = null;
        private bool _isDragInProgress = false;

        // 数据库和管理器
        private DatabaseManager _dbManager;
        private ConfigManager _configManager;
        private ImportManager _importManager;
        private ImageSaveManager _imageSaveManager;
        private SearchManager _searchManager;
        private SortManager _sortManager;
        public ProjectionManager _projectionManager;  // ⚡ public for AnimationHelper access
        private OriginalManager _originalManager;
        private PreloadCacheManager _preloadCacheManager; // 智能预缓存管理器
        private SlideExportManager _slideExportManager; // 幻灯片导出管理器
        private SlideImportManager _slideImportManager; // 幻灯片导入管理器
        
        // 视频播放相关
        private VideoPlayerManager _videoPlayerManager;
        private IVideoBackgroundManager _videoBackgroundManager;
        private VideoView _mainVideoView;
        private bool _isUpdatingProgress = false; // 防止进度条更新时触发事件
        private string _pendingProjectionVideoPath = null;
        private System.Windows.Threading.DispatcherTimer _projectionTimeoutTimer = null; // 待投影播放的视频路径
        
        // 按钮防抖动
        private DateTime _lastPlayModeClickTime = DateTime.MinValue;
        private DateTime _lastMediaPrevClickTime = DateTime.MinValue;
        
        // 全局热键管理器
        private Utils.GlobalHotKeyManager _globalHotKeyManager;
        private DateTime _lastMediaNextClickTime = DateTime.MinValue;
        
        // 实时FPS监控器
        public Utils.RealTimeFpsMonitor _fpsMonitor;
        
        // MVVM - 新架构的PlaybackControlViewModel
        internal ViewModels.PlaybackControlViewModel _playbackViewModel;
        private Services.PlaybackServiceFactory _playbackServiceFactory;
        private Services.Interfaces.ICountdownService _countdownService;
        private Repositories.Interfaces.ITimingRepository _timingRepository;
        private Repositories.Interfaces.IOriginalModeRepository _originalModeRepository;
        private Repositories.Interfaces.ICompositeScriptRepository _compositeScriptRepository;
        private Microsoft.Extensions.Caching.Memory.IMemoryCache _memoryCache;
        private Repositories.Interfaces.IMediaFileRepository _mediaFileRepository;
        private EventHandler<Services.Interfaces.CountdownUpdateEventArgs> _countdownUpdatedHandler;
        private PropertyChangedEventHandler _playbackPropertyChangedHandler;
        private EventHandler<Services.Implementations.JumpToKeyframeEventArgs> _jumpToKeyframeRequestedHandler;
        private Services.Implementations.KeyframePlaybackService _keyframePlaybackService;
        
        // ✅ SkiaSharp文本渲染器
        private SkiaTextRenderer _skiaRenderer;
        private GPUContext _gpuContext;
        private PakManager _pakManager;
        private SkiaFontService _skiaFontService;

        // 模块控制器
        private readonly AuthService _authService;
        private AuthModuleController _authModuleController;
        private BibleModuleController _bibleModuleController;
        private MediaModuleController _mediaModuleController;
        private MainWindowComposer _mainWindowComposer;
        private MainWindowServices _mainWindowServices;

        #endregion

        #region 公共属性（用于数据绑定）

        /// <summary>
        /// 文件夹字号（用于XAML绑定）
        /// </summary>
        public double FolderFontSize => _configManager?.FolderFontSize ?? 26.0;

        /// <summary>
        /// 文件字号（用于XAML绑定）
        /// </summary>
        public double FileFontSize => _configManager?.FileFontSize ?? 26.0;

        /// <summary>
        /// 文件夹标签字号（搜索结果显示，用于XAML绑定）
        /// </summary>
        public double FolderTagFontSize => _configManager?.FolderTagFontSize ?? 18.0;

        #endregion

        #region 初始化

        public MainWindow()
        {
            InitializeComponent();

            _mainWindowComposer = MainWindowComposer.CreateDefault();
            _mainWindowServices = _mainWindowComposer.Compose();
            _authService = _mainWindowServices.GetRequired<AuthService>();
            _videoBackgroundManager = _mainWindowServices.GetRequired<IVideoBackgroundManager>();
            _gpuContext = _mainWindowServices.GetRequired<GPUContext>();
            _pakManager = _mainWindowServices.GetRequired<PakManager>();
            _skiaFontService = _mainWindowServices.GetRequired<SkiaFontService>();
            
            // 初始化GPU处理器
            InitializeGpuProcessor();
            
            // 初始化UI
            InitializeUI();
            
            // 初始化新的PlaybackControlViewModel
            InitializePlaybackViewModel();
            
            // 🆕 初始化文本编辑器
            InitializeTextEditor();
            
            // 初始化FPS监控器
            InitializeFpsMonitor();
            
            // 🆕 监听主窗口失去焦点和状态变化，自动关闭圣经样式 Popup
            this.Deactivated += MainWindow_Deactivated;
            this.StateChanged += MainWindow_StateChanged;
            this.LocationChanged += MainWindow_LocationChanged;
            
            // 🔐 初始化认证服务
            InitializeAuthService();
        }
        
        /// <summary>
        /// 初始化全局热键管理器（不立即注册热键）
        /// </summary>
        // 全局热键相关方法已移至 MainWindow.HotKey.cs
        
        /// <summary>
        /// 加载用户设置 - 从 config.json
        /// </summary>
        // 设置管理相关方法已移至 MainWindow.Settings.cs

        #endregion

        #region 顶部菜单栏事件

        // 导入文件相关方法已移至 MainWindow.Import.cs

        // 字号设置方法已移至 MainWindow.Settings.cs

        // BtnColorEffect_Click 已移至 MainWindow.Color.cs

        #endregion

        #region 关键帧控制栏事件
        // 注意：关键帧相关方法已移至 MainWindow.Keyframe.cs partial class
        #endregion

        // 项目树事件已移至 MainWindow.ProjectTree.cs


        #region 图像处理核心功能

        

        // 颜色效果相关方法已移至 MainWindow.Color.cs

        #endregion

        // 图片缩放和拖动功能已移至 MainWindow.Zoom.cs

        // 媒体播放器事件已移至 MainWindow.Media.cs

        // 右键菜单处理已移至 MainWindow.ContextMenu.cs


        // 窗口生命周期事件已移至 MainWindow.Lifecycle.cs
        
        // 拖拽事件处理已移至 MainWindow.DragDrop.cs

        

        
    }

}
