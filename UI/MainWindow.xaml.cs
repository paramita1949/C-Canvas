using System;
using System.ComponentModel;
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
using LibVLCSharp.WPF;

namespace ImageColorChanger.UI
{
    public partial class MainWindow : Window, INotifyPropertyChanged
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
        private int _currentImageId = 0; // 当前加载的图片ID

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
        private ProjectionManager _projectionManager;
        private OriginalManager _originalManager;
        private PreloadCacheManager _preloadCacheManager; // 智能预缓存管理器
        
        // 视频播放相关
        private VideoPlayerManager _videoPlayerManager;
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
        
        // MVVM - 新架构的PlaybackControlViewModel
        internal ViewModels.PlaybackControlViewModel _playbackViewModel;

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
            
            // 初始化GPU处理器
            InitializeGpuProcessor();
            
            // 初始化UI
            InitializeUI();
            
            // 初始化新的PlaybackControlViewModel
            InitializePlaybackViewModel();
            
            // 🆕 初始化文本编辑器
            InitializeTextEditor();
        }
        
        /// <summary>
        /// 初始化新的PlaybackControlViewModel
        /// </summary>
        private void InitializePlaybackViewModel()
        {
            try
            {
                _playbackViewModel = App.GetRequiredService<ViewModels.PlaybackControlViewModel>();
                
                // 订阅倒计时更新事件
                var countdownService = App.GetRequiredService<Services.Interfaces.ICountdownService>();
                countdownService.CountdownUpdated += (s, e) =>
                {
                    Dispatcher.Invoke(() => {
                        CountdownText.Text = $"倒: {e.RemainingTime:F1}";
                    });
                };
                
                // 订阅ViewModel属性变化，自动更新按钮状态
                _playbackViewModel.PropertyChanged += (s, e) =>
                {
                    Dispatcher.Invoke(() => {
                        switch (e.PropertyName)
                        {
                            case "IsRecording":
                                BtnRecord.Content = _playbackViewModel.IsRecording ? "⏹ 停止" : "⏺ 录制";
                                break;
                            case "IsPlaying":
                                BtnPlay.Content = _playbackViewModel.IsPlaying ? "⏹ 停止" : "▶ 播放";
                                BtnPauseResume.IsEnabled = _playbackViewModel.IsPlaying;
                                // 播放时显示绿色，停止时恢复默认灰色
                                BtnPlay.Background = _playbackViewModel.IsPlaying 
                                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(BUTTON_ACTIVE_COLOR_R, BUTTON_ACTIVE_COLOR_G, BUTTON_ACTIVE_COLOR_B))
                                    : System.Windows.SystemColors.ControlBrush;
                                
                                // 🎯 停止播放时重置倒计时显示
                                if (!_playbackViewModel.IsPlaying)
                                {
                                    CountdownText.Text = "倒: --";
                                }
                                break;
                            case "IsPaused":
                                BtnPauseResume.Content = _playbackViewModel.IsPaused ? "▶ 继续" : "⏸ 暂停";
                                break;
                            case "PlayCount":
                                string text = _playbackViewModel.PlayCount == -1 ? "∞" : _playbackViewModel.PlayCount.ToString();
                                BtnPlayCount.Content = $"🔄 {text}次";
                                break;
                            case "HasTimingData":
                                // 有数据时显示绿色，无数据时恢复默认
                                BtnScript.Background = _playbackViewModel.HasTimingData 
                                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(BUTTON_ACTIVE_COLOR_R, BUTTON_ACTIVE_COLOR_G, BUTTON_ACTIVE_COLOR_B))
                                    : System.Windows.SystemColors.ControlBrush;
                                break;
                        }
                    });
                };
                
                // 🎯 手动初始化UI状态（因为订阅事件时ViewModel已经加载完成，错过了初始PropertyChanged事件）
                Dispatcher.Invoke(() => {
                    BtnRecord.Content = _playbackViewModel.IsRecording ? "⏹ 停止" : "⏺ 录制";
                    BtnPlay.Content = _playbackViewModel.IsPlaying ? "⏹ 停止" : "▶ 播放";
                    BtnPauseResume.Content = _playbackViewModel.IsPaused ? "▶ 继续" : "⏸ 暂停";
                    BtnPauseResume.IsEnabled = _playbackViewModel.IsPlaying;
                    
                    // 🎯 重要：初始化播放次数显示
                    string playCountText = _playbackViewModel.PlayCount == -1 ? "∞" : _playbackViewModel.PlayCount.ToString();
                    BtnPlayCount.Content = $"🔄 {playCountText}次";
                    
                });
                
                // 订阅播放服务事件（关键帧跳转、原图切换）
                var serviceFactory = App.GetRequiredService<Services.PlaybackServiceFactory>();
                
                // 关键帧模式事件
                var keyframePlayback = serviceFactory.GetPlaybackService(Database.Models.Enums.PlaybackMode.Keyframe);
                if (keyframePlayback is Services.Implementations.KeyframePlaybackService kfService)
                {
                    kfService.JumpToKeyframeRequested += async (s, e) =>
                    {
                        var jumpTime = System.Diagnostics.Stopwatch.StartNew();
                        System.Diagnostics.Debug.WriteLine($"\n🎯 ========== 关键帧跳转开始 ==========");
                        System.Diagnostics.Debug.WriteLine($"🎯 目标关键帧: ID={e.KeyframeId}, Position={e.Position:F4}, 直接跳转={e.UseDirectJump}");
                        
                        await Dispatcher.InvokeAsync(() => {
                            if (_keyframeManager != null)
                            {
                                // 🔧 根据UseDirectJump标志选择跳转方式（参考Python版本：keytime.py 第1199-1213行）
                                var scrollStart = jumpTime.ElapsedMilliseconds;
                                if (e.UseDirectJump)
                                {
                                    // 直接跳转，不使用滚动动画（用于循环回第一帧或首次播放）
                                    ImageScrollViewer.ScrollToVerticalOffset(e.Position * ImageScrollViewer.ScrollableHeight);
                                    var scrollTime = jumpTime.ElapsedMilliseconds - scrollStart;
                                    System.Diagnostics.Debug.WriteLine($"⚡ [跳转] 直接跳转: {scrollTime}ms");
                                }
                                else
                                {
                                    // 使用平滑滚动动画
                                    _keyframeManager.SmoothScrollTo(e.Position);
                                    var scrollTime = jumpTime.ElapsedMilliseconds - scrollStart;
                                    System.Diagnostics.Debug.WriteLine($"🎬 [跳转] 平滑滚动启动: {scrollTime}ms");
                                }
                                
                                // 🔧 更新关键帧索引和指示器（参考Python版本：keytime.py 第1184-1221行）
                                // 1. 查找当前关键帧的索引（从缓存，性能优化）
                                var indexStart = jumpTime.ElapsedMilliseconds;
                                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                                if (keyframes != null)
                                {
                                    for (int i = 0; i < keyframes.Count; i++)
                                    {
                                        if (keyframes[i].Id == e.KeyframeId)
                                        {
                                            // 2. 更新关键帧索引
                                            _keyframeManager.UpdateKeyframeIndex(i);
                                            var indexTime = jumpTime.ElapsedMilliseconds - indexStart;
                                            System.Diagnostics.Debug.WriteLine($"🎯 [跳转] 更新索引: {indexTime}ms -> #{i + 1}");
                                            break;
                                        }
                                    }
                                }
                                
                                // 3. 更新指示器和预览线
                                var uiStart = jumpTime.ElapsedMilliseconds;
                                _keyframeManager?.UpdatePreviewLines();
                                var uiTime = jumpTime.ElapsedMilliseconds - uiStart;
                                System.Diagnostics.Debug.WriteLine($"🎯 [跳转] 更新UI: {uiTime}ms");
                                
                                jumpTime.Stop();
                                System.Diagnostics.Debug.WriteLine($"🎯 ========== 关键帧跳转完成: {jumpTime.ElapsedMilliseconds}ms ==========\n");
                            }
                        });
                    };
                }
                
                // 注意：原图模式的SwitchImageRequested事件订阅已移至MainWindow.Original.cs中
                // 在StartOriginalModePlaybackAsync()中订阅，在StopOriginalModePlaybackAsync()中取消订阅
                // 避免重复订阅导致图片被加载两次
                
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ PlaybackControlViewModel 初始化失败: {ex.Message}");
            }
        }

        private void InitializeGpuProcessor()
        {
            // 🎮 初始化GPU上下文（自动检测GPU可用性）
            var gpuContext = Core.GPUContext.Instance;
            
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($"🎮 GPU加速状态: {(gpuContext.IsGpuAvailable ? "✅ 已启用" : "⚠️ 已降级到CPU")}");
            System.Diagnostics.Debug.WriteLine($"📊 GPU信息: {gpuContext.GpuInfo}");
            System.Diagnostics.Debug.WriteLine("========================================");
            
            // 在UI显示GPU状态
            Dispatcher.InvokeAsync(() =>
            {
                if (gpuContext.IsGpuAvailable)
                {
                    ShowStatus($"🎮 GPU加速已启用 - {gpuContext.GpuInfo}");
                }
                else
                {
                    ShowStatus($"⚠️ GPU不可用，已降级到CPU渲染");
                }
            });
        }

        private void InitializeUI()
        {
            // 初始化数据库
            InitializeDatabase();
            
            // 初始化关键帧系统（必须在数据库初始化之后）
            InitializeKeyframeSystem();
            
            // 初始化图片处理器
            _imageProcessor = new ImageProcessor(this, ImageScrollViewer, ImageDisplay, ImageContainer);
            
            // 加载用户设置（必须在 _imageProcessor 创建之后）
            LoadSettings();
            
            // 初始化保存管理器
            _imageSaveManager = new ImageSaveManager(_imageProcessor);
            
            // 初始化投影管理器
            _projectionManager = new ProjectionManager(
                this,
                ImageScrollViewer,
                ImageDisplay,
                _imageProcessor,
                ScreenSelector
            );
            
            // 订阅投影状态改变事件
            _projectionManager.ProjectionStateChanged += OnProjectionStateChanged;
            
            // 订阅投影VideoView加载完成事件
            _projectionManager.ProjectionVideoViewLoaded += OnProjectionVideoViewLoaded;
            
            // 初始化原图管理器
            _originalManager = new OriginalManager(_dbManager, this);
            
            // 初始化智能预缓存管理器（使用ImageProcessor的缓存实例和渲染器）
            _preloadCacheManager = new PreloadCacheManager(_imageProcessor.GetMemoryCache(), _dbManager, _imageProcessor);
            
            // 初始化视频播放器
            InitializeVideoPlayer();
            
            // 初始化项目树
            ProjectTree.ItemsSource = _projectTreeItems;
            
            // 添加拖拽事件处理
            ProjectTree.PreviewMouseLeftButtonDown += ProjectTree_PreviewMouseLeftButtonDown;
            ProjectTree.PreviewMouseMove += ProjectTree_PreviewMouseMove;
            ProjectTree.Drop += ProjectTree_Drop;
            ProjectTree.DragOver += ProjectTree_DragOver;
            ProjectTree.DragLeave += ProjectTree_DragLeave;
            ProjectTree.AllowDrop = true;
            
            // 初始化屏幕选择器
            InitializeScreenSelector();
            
            // 添加滚动同步
            ImageScrollViewer.ScrollChanged += ImageScrollViewer_ScrollChanged;
            
            // 加载项目
            LoadProjects();
            
            // 初始化全局热键
            InitializeGlobalHotKeys();
        }
        
        /// <summary>
        /// 滚动事件处理 - 同步投影和更新预览线
        /// </summary>
        private void ImageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            _projectionManager?.SyncProjectionScroll();
            
            // 更新关键帧预览线和指示块
            _keyframeManager?.UpdatePreviewLines();
        }
        
        /// <summary>
        /// 更新投影内容
        /// </summary>
        public void UpdateProjection()
        {
            System.Diagnostics.Debug.WriteLine($"🎬 [MainWindow.UpdateProjection] 被调用");
            System.Diagnostics.Debug.WriteLine($"   _imageProcessor.CurrentImage = {_imageProcessor?.CurrentImage?.Width}x{_imageProcessor?.CurrentImage?.Height}");
            System.Diagnostics.Debug.WriteLine($"   _projectionManager = {_projectionManager != null}");
            System.Diagnostics.Debug.WriteLine($"   _projectionManager.IsProjectionActive = {_projectionManager?.IsProjectionActive}");
            
            if (_imageProcessor.CurrentImage != null)
            {
                if (_projectionManager != null && _projectionManager.IsProjectionActive)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ [MainWindow.UpdateProjection] 调用 UpdateProjectionImage");
                    _projectionManager?.UpdateProjectionImage(
                        _imageProcessor.CurrentImage,
                        _isColorEffectEnabled,
                        _currentZoom,
                        _originalMode,
                        _originalDisplayMode  // 传递原图显示模式
                    );
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ [UpdateProjection] 投影未开启，跳过");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ [UpdateProjection] _imageProcessor.CurrentImage 为 null");
            }
        }

        private void InitializeDatabase()
        {
            try
            {
                // 创建配置管理器（使用默认路径：主程序目录/config.json）
                _configManager = new ConfigManager();
                
                // 创建数据库管理器（使用默认路径：主程序目录/pyimages.db）
                _dbManager = new DatabaseManager();
                
                // 执行数据库迁移
                _dbManager.MigrateAddLoopCount();
                _dbManager.MigrateAddHighlightColor();
                
                // 创建排序和搜索管理器
                _sortManager = new SortManager();
                _searchManager = new SearchManager(_dbManager, _configManager);
                
            // 创建导入管理器
            _importManager = new ImportManager(_dbManager, _sortManager);
            
            // 加载搜索范围选项
            LoadSearchScopes();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                //System.Diagnostics.Debug.WriteLine($"数据库初始化失败: {ex}");
            }
        }
        
        /// <summary>
        /// 初始化全局热键管理器（不立即注册热键）
        /// </summary>
        // 全局热键相关方法已移至 MainWindow.HotKey.cs

        private void InitializeScreenSelector()
        {
            // 屏幕选择器由 ProjectionManager 管理，这里不需要再初始化
            // ProjectionManager 会在初始化时自动填充屏幕列表并选择扩展屏
        }
        
        /// <summary>
        /// 初始化视频播放器
        /// </summary>
        private void InitializeVideoPlayer()
        {
            try
            {
                // 创建视频播放管理器（此时只初始化LibVLC，不创建MediaPlayer）
                _videoPlayerManager = new VideoPlayerManager(this);
                
                // 订阅视频轨道检测事件
                _videoPlayerManager.VideoTrackDetected += VideoPlayerManager_VideoTrackDetected;
                
                // 创建VideoView控件并添加到VideoContainer
                _mainVideoView = new VideoView
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                    Margin = new Thickness(0)
                };
                
                VideoContainer.Children.Add(_mainVideoView);
                
                
                // 等待VideoView完成布局并有了实际尺寸后，再创建MediaPlayer（避免小窗口）
                bool mediaPlayerInitialized = false;
                SizeChangedEventHandler sizeChangedHandler = null;
                
                sizeChangedHandler = (s, e) =>
                {
                    try
                    {
                        // 只在VideoView有实际尺寸且MediaPlayer未初始化时执行
                        if (!mediaPlayerInitialized && _mainVideoView.ActualWidth > 0 && _mainVideoView.ActualHeight > 0)
                        {
                            //System.Diagnostics.Debug.WriteLine("🟡 ===== 主窗口 VideoView 尺寸就绪 =====");
                            //System.Diagnostics.Debug.WriteLine($"🟡 _mainVideoView.ActualWidth: {_mainVideoView.ActualWidth}");
                            //System.Diagnostics.Debug.WriteLine($"🟡 _mainVideoView.ActualHeight: {_mainVideoView.ActualHeight}");
                            
                            // 创建MediaPlayer并立即绑定到VideoView（此时VideoView已有尺寸）
                            _videoPlayerManager.InitializeMediaPlayer(_mainVideoView);
                            
                            // 设置为主窗口VideoView
                            _videoPlayerManager.SetMainVideoView(_mainVideoView);
                            
                            mediaPlayerInitialized = true;
                            
                            // 取消订阅，避免重复触发
                            _mainVideoView.SizeChanged -= sizeChangedHandler;
                            
                            //System.Diagnostics.Debug.WriteLine("✅ 主窗口VideoView处理完成（有尺寸）");
                            //System.Diagnostics.Debug.WriteLine("🟡 ===== 主窗口 VideoView 初始化完成 =====");
                        }
                    }
                    catch (Exception)
                    {
                        //System.Diagnostics.Debug.WriteLine($"❌ MediaPlayer绑定失败: {ex.Message}");
                        //System.Diagnostics.Debug.WriteLine($"❌ 堆栈: {ex.StackTrace}");
                    }
                };
                
                _mainVideoView.SizeChanged += sizeChangedHandler;
                
                // 订阅事件
                _videoPlayerManager.PlayStateChanged += OnVideoPlayStateChanged;
                _videoPlayerManager.MediaChanged += OnVideoMediaChanged;
                _videoPlayerManager.MediaEnded += OnVideoMediaEnded;
                _videoPlayerManager.ProgressUpdated += OnVideoProgressUpdated;
                
                // 设置默认音量
                _videoPlayerManager.SetVolume(50);
                VolumeSlider.Value = 50;
                
                // 初始化播放模式按钮显示（默认为随机播放）
                BtnPlayMode.Content = "🔀";
                BtnPlayMode.ToolTip = "播放模式：随机";
                
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 视频播放器初始化失败: {ex.Message}");
                MessageBox.Show($"视频播放器初始化失败: {ex.Message}\n\n部分功能可能无法使用。", 
                    "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 从数据库加载项目树
        /// </summary>
        private void LoadProjects()
        {
            try
            {
                _projectTreeItems.Clear();

                // 获取所有文件夹
                var folders = _dbManager.GetAllFolders();

                // 获取根目录的文件
                var rootFiles = _dbManager.GetRootMediaFiles();

                // 获取所有手动排序的文件夹ID
                var manualSortFolderIds = _dbManager.GetManualSortFolderIds();

                // 添加文件夹到项目树
                foreach (var folder in folders)
                {
                    // 检查是否为手动排序文件夹
                    bool isManualSort = manualSortFolderIds.Contains(folder.Id);
                    
                    // 获取文件夹中的文件
                    var files = _dbManager.GetMediaFilesByFolder(folder.Id);
                    
                    // 检查文件夹是否包含媒体文件（视频/音频）
                    bool hasMediaFiles = files.Any(f => f.FileType == FileType.Video || f.FileType == FileType.Audio);
                    
                    // 检查是否有播放模式标记
                    string folderPlayMode = _dbManager.GetFolderVideoPlayMode(folder.Id);
                    
                    // 检查是否有变色标记
                    bool hasColorEffectMark = _dbManager.HasFolderAutoColorEffect(folder.Id);
                    
                    // 获取文件夹 Material Design 图标（按优先级显示）
                    string iconKind, iconColor;
                    if (hasMediaFiles)
                    {
                        // 优先级1: 媒体文件夹，显示播放模式图标
                        if (!string.IsNullOrEmpty(folderPlayMode))
                        {
                            // 如果有设置播放模式标记，显示对应图标
                            (iconKind, iconColor) = GetPlayModeIcon(folderPlayMode);
                        }
                        else
                        {
                            // 默认显示随机播放图标
                            (iconKind, iconColor) = ("Shuffle", "#FF9800");  // 随机播放 - 橙色
                        }
                    }
                    else if (!string.IsNullOrEmpty(folderPlayMode))
                    {
                        // 优先级2: 非媒体文件夹但有播放模式标记（兼容性）
                        (iconKind, iconColor) = GetPlayModeIcon(folderPlayMode);
                    }
                    else if (hasColorEffectMark)
                    {
                        // 优先级3: 变色标记，显示变色图标
                        (iconKind, iconColor) = ("Palette", "#FF6B6B");  // 调色板图标 - 红色
                    }
                    else
                    {
                        // 优先级4: 原图/手动排序图标
                        (iconKind, iconColor) = _originalManager.GetFolderIconKind(folder.Id, isManualSort);
                    }
                    
                    var folderItem = new ProjectTreeItem
                    {
                        Id = folder.Id,
                        Name = folder.Name,  // 不再在名称前添加emoji，改用图标样式
                        Icon = iconKind,  // 保留用于后备
                        IconKind = iconKind,
                        IconColor = iconColor,
                        Type = TreeItemType.Folder,
                        Path = folder.Path,
                        Children = new ObservableCollection<ProjectTreeItem>()
                    };

                    // 获取文件夹中的文件（添加原图标记图标）
                    foreach (var file in files)
                    {
                        // 获取 Material Design 图标
                        string fileIconKind = "File";
                        string fileIconColor = "#95E1D3";
                        if (file.FileType == FileType.Image)
                        {
                            (fileIconKind, fileIconColor) = _originalManager.GetImageIconKind(file.Id);
                        }
                        
                        folderItem.Children.Add(new ProjectTreeItem
                        {
                            Id = file.Id,
                            Name = file.Name,
                            Icon = fileIconKind,
                            IconKind = fileIconKind,
                            IconColor = fileIconColor,
                            Type = TreeItemType.File,
                            Path = file.Path,
                            FileType = file.FileType
                        });
                    }

                    _projectTreeItems.Add(folderItem);
                }

                // 添加根目录的独立文件
                foreach (var file in rootFiles)
                {
                    // 获取 Material Design 图标
                    string rootFileIconKind = "File";
                    string rootFileIconColor = "#95E1D3";
                    if (file.FileType == FileType.Image)
                    {
                        (rootFileIconKind, rootFileIconColor) = _originalManager.GetImageIconKind(file.Id);
                    }
                    
                    _projectTreeItems.Add(new ProjectTreeItem
                    {
                        Id = file.Id,
                        Name = file.Name,
                        Icon = rootFileIconKind,
                        IconKind = rootFileIconKind,
                        IconColor = rootFileIconColor,
                        Type = TreeItemType.File,
                        Path = file.Path,
                        FileType = file.FileType
                    });
                }

                // 加载文本项目
                LoadTextProjectsToTree();

                // System.Diagnostics.Debug.WriteLine($"📂 加载项目: {folders.Count} 个文件夹, {rootFiles.Count} 个独立文件");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"加载项目失败: {ex}");
            }
        }

        /// <summary>
        /// 加载文本项目到项目树
        /// </summary>
        private void LoadTextProjectsToTree()
        {
            try
            {
                // 延迟初始化 _textProjectManager（如果还未初始化）
                if (_textProjectManager == null)
                {
                    if (_dbManager == null)
                    {
                        //System.Diagnostics.Debug.WriteLine("⚠️ _dbManager 未初始化，跳过加载文本项目");
                        return;
                    }
                    
                    _textProjectManager = new TextProjectManager(_dbManager.GetDbContext());
                }

                var textProjects = _textProjectManager.GetAllProjectsAsync().GetAwaiter().GetResult();
                
                foreach (var project in textProjects)
                {
                    //System.Diagnostics.Debug.WriteLine($"  - 添加文本项目到树: ID={project.Id}, Name={project.Name}");
                    
                    _projectTreeItems.Add(new ProjectTreeItem
                    {
                        Id = project.Id,
                        Name = project.Name,
                        Icon = "FileDocument",
                        IconKind = "FileDocument",
                        IconColor = "#2196F3",  // 蓝色
                        Type = TreeItemType.TextProject,
                        Path = null  // 文本项目没有物理路径
                    });
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载文本项目失败: {ex.Message}");
                //System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 根据文件类型获取图标
        /// </summary>
        private string GetFileIcon(FileType fileType)
        {
            return fileType switch
            {
                FileType.Image => "🖼️",
                FileType.Video => "🎬",
                FileType.Audio => "🎵",
                _ => "📄"
            };
        }

        /// <summary>
        /// 加载用户设置 - 从 config.json
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                // 从 ConfigManager 加载原图显示模式
                _originalDisplayMode = _configManager.OriginalDisplayMode;
                _imageProcessor.OriginalDisplayModeValue = _originalDisplayMode;
                
                // 加载缩放比例
                _currentZoom = _configManager.ZoomRatio;
                
                // 加载目标颜色
                _currentTargetColor = new SKColor(
                    _configManager.TargetColorR,
                    _configManager.TargetColorG,
                    _configManager.TargetColorB
                );
                _currentTargetColorName = _configManager.TargetColorName ?? "淡黄";
                
                // 加载导航栏宽度
                if (NavigationPanelColumn != null)
                {
                    NavigationPanelColumn.Width = new GridLength(_configManager.NavigationPanelWidth);
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存用户设置 - 到 config.json
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                // 保存原图显示模式到 ConfigManager
                _configManager.OriginalDisplayMode = _originalDisplayMode;
                
                // 保存缩放比例
                _configManager.ZoomRatio = _currentZoom;
                
                // 使用 ConfigManager 的统一方法保存目标颜色
                _configManager.SetCurrentColor(_currentTargetColor.Red, _currentTargetColor.Green, _currentTargetColor.Blue, _currentTargetColorName);
                
                // System.Diagnostics.Debug.WriteLine($"✅ 已保存设置到 config.json (颜色: {_currentTargetColorName})");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 保存设置失败: {ex.Message}");
            }
        }

        #endregion

        #region 顶部菜单栏事件

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            // 创建导入菜单
            var contextMenu = new ContextMenu();
            contextMenu.FontSize = 14;

            // 导入单个文件
            var importFileItem = new MenuItem { Header = "导入单个文件" };
            importFileItem.Click += (s, args) => ImportSingleFile();
            contextMenu.Items.Add(importFileItem);

            // 导入文件夹
            var importFolderItem = new MenuItem { Header = "导入文件夹" };
            importFolderItem.Click += (s, args) => ImportFolder();
            contextMenu.Items.Add(importFolderItem);

            contextMenu.Items.Add(new Separator());

            // 另存图片
            var saveImageItem = new MenuItem { Header = "另存图片" };
            saveImageItem.Click += (s, args) => SaveCurrentImage();
            contextMenu.Items.Add(saveImageItem);

            contextMenu.Items.Add(new Separator());

            // 字号设置
            var fontSizeItem = new MenuItem { Header = "字号设置" };
            
            // 文件夹字号子菜单
            var folderFontSizeItem = new MenuItem { Header = "文件夹字号" };
            foreach (var size in new[] { 13.0, 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0, 22.0, 24.0, 26.0, 28.0, 30.0 })
            {
                var menuItem = new MenuItem 
                { 
                    Header = $"{size}",
                    IsCheckable = true,
                    IsChecked = Math.Abs(_configManager.FolderFontSize - size) < 0.1
                };
                menuItem.Click += (s, args) => SetFolderFontSize(size);
                folderFontSizeItem.Items.Add(menuItem);
            }
            fontSizeItem.Items.Add(folderFontSizeItem);

            // 文件字号子菜单
            var fileFontSizeItem = new MenuItem { Header = "文件字号" };
            foreach (var size in new[] { 13.0, 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0, 22.0, 24.0, 26.0, 28.0, 30.0 })
            {
                var menuItem = new MenuItem 
                { 
                    Header = $"{size}",
                    IsCheckable = true,
                    IsChecked = Math.Abs(_configManager.FileFontSize - size) < 0.1
                };
                menuItem.Click += (s, args) => SetFileFontSize(size);
                fileFontSizeItem.Items.Add(menuItem);
            }
            fontSizeItem.Items.Add(fileFontSizeItem);

            // 文件夹标签字号子菜单（搜索结果显示）
            var folderTagFontSizeItem = new MenuItem { Header = "文件夹标签字号" };
            foreach (var size in new[] { 8.0, 9.0, 10.0, 11.0, 12.0, 13.0, 14.0, 15.0, 16.0, 18.0, 20.0 })
            {
                var menuItem = new MenuItem 
                { 
                    Header = $"{size}",
                    IsCheckable = true,
                    IsChecked = Math.Abs(_configManager.FolderTagFontSize - size) < 0.1
                };
                menuItem.Click += (s, args) => SetFolderTagFontSize(size);
                folderTagFontSizeItem.Items.Add(menuItem);
            }
            fontSizeItem.Items.Add(folderTagFontSizeItem);

            contextMenu.Items.Add(fontSizeItem);

            // 显示菜单
            contextMenu.PlacementTarget = BtnImport;
            contextMenu.IsOpen = true;
        }

        /// <summary>
        /// 导入单个文件
        /// </summary>
        private void ImportSingleFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = ImportManager.GetFileDialogFilter(),
                Title = "选择媒体文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var mediaFile = _importManager.ImportSingleFile(openFileDialog.FileName);
                if (mediaFile != null)
                {
                    LoadProjects(); // 刷新项目树
                    LoadSearchScopes(); // 刷新搜索范围
                    ShowStatus($"✅ 已导入: {mediaFile.Name}");
                }
            }
        }

        /// <summary>
        /// 导入文件夹
        /// </summary>
        private void ImportFolder()
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择要导入的文件夹",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var (folder, newFiles, existingFiles) = _importManager.ImportFolder(folderDialog.SelectedPath);
                
                if (folder != null)
                {
                    LoadProjects(); // 刷新项目树
                    LoadSearchScopes(); // 刷新搜索范围
                    
                    // 🔧 清除缓存，确保使用最新的数据库数据
                    _originalManager?.ClearCache();
                    
                    // ⚡ 清除图片LRU缓存
                    _imageProcessor?.ClearImageCache();
                    
                    // ⚡ 清除投影缓存
                    _projectionManager?.ClearProjectionCache();
                    
                    //System.Diagnostics.Debug.WriteLine("🔄 文件夹导入完成，已清除所有缓存");
                    
                    ShowStatus($"✅ 已导入文件夹: {folder.Name} (新增 {newFiles.Count} 个文件)");
                }
            }
        }

        /// <summary>
        /// 保存当前图片
        /// </summary>
        private void SaveCurrentImage()
        {
            if (_imageSaveManager != null)
            {
                _imageSaveManager.SaveEffectImage(_imagePath);
            }
        }

        /// <summary>
        /// 设置文件夹字号
        /// </summary>
        private void SetFolderFontSize(double size)
        {
            _configManager.FolderFontSize = size;
            OnPropertyChanged(nameof(FolderFontSize));
            ShowStatus($"✅ 文件夹字号已设置为: {size}");
        }

        /// <summary>
        /// 设置文件字号
        /// </summary>
        private void SetFileFontSize(double size)
        {
            _configManager.FileFontSize = size;
            OnPropertyChanged(nameof(FileFontSize));
            ShowStatus($"✅ 文件字号已设置为: {size}");
        }

        /// <summary>
        /// 设置文件夹标签字号（搜索结果显示）
        /// </summary>
        private void SetFolderTagFontSize(double size)
        {
            _configManager.FolderTagFontSize = size;
            OnPropertyChanged(nameof(FolderTagFontSize));
            ShowStatus($"✅ 文件夹标签字号已设置为: {size}");
        }

        /// <summary>
        /// 投影状态改变事件处理
        /// </summary>
        private void OnProjectionStateChanged(object sender, bool isActive)
        {
            Dispatcher.Invoke(() =>
            {
                if (isActive)
                {
                    BtnProjection.Content = "🖥 结束";
                    BtnProjection.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 淡绿色
                    ShowStatus("✅ 投影已开启");
                    
                    // 启用全局热键（投影模式下）
                    EnableGlobalHotKeys();
                    
                    // 如果当前正在播放视频，立即切换到视频投影模式
                    if (_videoPlayerManager != null && _videoPlayerManager.IsPlaying)
                    {
                        // 立即切换到视频投影模式，让VideoView获得正确尺寸
                        _projectionManager.ShowVideoProjection();
                        //System.Diagnostics.Debug.WriteLine("📹 检测到正在播放视频，立即切换到视频投影模式");
                    }
                    // 如果选中了视频文件但未播放，直接在投影屏幕播放
                    else if (!string.IsNullOrEmpty(_imagePath) && IsVideoFile(_imagePath))
                    {
                        // 先准备投影环境
                        var projectionVideoView = _projectionManager.GetProjectionVideoView();
                        if (projectionVideoView != null)
                        {
                            // 主屏幕：隐藏视频（不在主屏幕显示）
                            VideoContainer.Visibility = Visibility.Collapsed;
                            
                            // 切换到视频投影模式
                            _projectionManager.ShowVideoProjection();
                            
                            // 先隐藏文件名，等视频轨道检测完成后再决定是否显示
                            string fileName = System.IO.Path.GetFileName(_imagePath);
                            _projectionManager.SetProjectionMediaFileName(fileName, false);
                            
                            // 设置待播放视频路径，等待MediaPlayer创建完成后播放
                            _pendingProjectionVideoPath = _imagePath;
                            //System.Diagnostics.Debug.WriteLine($"🟠 设置待投影播放视频: {fileName}");
                            
                            ShowStatus($"🎬 准备投影播放: {fileName}");
                        }
                    }
                }
                else
                {
                    BtnProjection.Content = "🖥 投影";
                    BtnProjection.Background = Brushes.Transparent; // 使用透明背景，让样式生效
                    
                    // 禁用全局热键（前台模式）
                    DisableGlobalHotKeys();
                    
                    // 清理投影超时定时器
                    if (_projectionTimeoutTimer != null)
                    {
                        _projectionTimeoutTimer.Stop();
                        _projectionTimeoutTimer = null;
                        //System.Diagnostics.Debug.WriteLine("🧹 已清理投影超时定时器");
                    }
                    
                    // 如果当前正在播放视频，停止播放并重置VideoView绑定
                    if (_videoPlayerManager != null && _videoPlayerManager.IsPlaying)
                    {
                        //System.Diagnostics.Debug.WriteLine("📹 关闭投影，停止视频播放");
                        
                        // 先停止播放
                        _videoPlayerManager.Stop();
                        
                        // 重置VideoView绑定状态，确保下次播放时不会出错
                        // 将VideoView切换回主窗口（但不播放）
                        //System.Diagnostics.Debug.WriteLine("🔧 重置VideoView绑定到主窗口");
                        var _mainVideoView = this.FindName("MainVideoView") as LibVLCSharp.WPF.VideoView;
                        if (_mainVideoView != null)
                        {
                            _videoPlayerManager.SetMainVideoView(_mainVideoView);
                        }
                        
                        // 隐藏媒体控制栏
                        MediaPlayerPanel.Visibility = Visibility.Collapsed;
                        
                        // 隐藏视频容器
                        VideoContainer.Visibility = Visibility.Collapsed;
                        
                        ShowStatus("⏹ 视频播放已停止");
                    }
                    
                    // 重置投影模式标志
                    _videoPlayerManager?.ResetProjectionMode();
                }
            });
        }

        /// <summary>
        /// 投影VideoView加载完成事件处理
        /// </summary>
        private void OnProjectionVideoViewLoaded(object sender, VideoView projectionVideoView)
        {
            try
            {
                //System.Diagnostics.Debug.WriteLine("🟠 ===== 投影窗口 VideoView Loaded事件触发 =====");
                //System.Diagnostics.Debug.WriteLine($"🟠 projectionVideoView: {(projectionVideoView != null ? "存在" : "null")}");
                //System.Diagnostics.Debug.WriteLine($"🟠 projectionVideoView.ActualWidth: {projectionVideoView?.ActualWidth}");
                //System.Diagnostics.Debug.WriteLine($"🟠 projectionVideoView.ActualHeight: {projectionVideoView?.ActualHeight}");
                
                // 如果VideoView尺寸为0，等待SizeChanged事件
                if (projectionVideoView != null && (projectionVideoView.ActualWidth == 0 || projectionVideoView.ActualHeight == 0))
                {
                    //System.Diagnostics.Debug.WriteLine("⚠️ 投影VideoView尺寸为0，等待SizeChanged事件");
                    
                    bool initialized = false;
                    SizeChangedEventHandler sizeChangedHandler = null;
                    
                    sizeChangedHandler = (s, e) =>
                    {
                        if (!initialized && projectionVideoView.ActualWidth > 0 && projectionVideoView.ActualHeight > 0)
                        {
                            //System.Diagnostics.Debug.WriteLine("🟠 ===== 投影窗口 VideoView 尺寸就绪 =====");
                            //System.Diagnostics.Debug.WriteLine($"🟠 projectionVideoView尺寸: {projectionVideoView.ActualWidth}x{projectionVideoView.ActualHeight}");
                            
                            if (_videoPlayerManager != null)
                            {
                                _videoPlayerManager.InitializeMediaPlayer(projectionVideoView);
                                _videoPlayerManager.SetProjectionVideoView(projectionVideoView);
                                
                                // 如果当前正在播放视频，现在启用视频投屏
                                if (_videoPlayerManager.IsPlaying)
                                {
                                    //System.Diagnostics.Debug.WriteLine("📹 投影VideoView加载完成，现在启用视频投屏");
                                    EnableVideoProjection();
                                }
                            }
                            
                            initialized = true;
                            projectionVideoView.SizeChanged -= sizeChangedHandler;
                            
                            //System.Diagnostics.Debug.WriteLine("✅ 投影窗口MediaPlayer已创建并绑定（有尺寸）");
                            
                            // 如果有待播放的视频，现在开始播放
                            if (!string.IsNullOrEmpty(_pendingProjectionVideoPath))
                            {
                                //System.Diagnostics.Debug.WriteLine($"🟠 检测到待播放视频，开始播放: {System.IO.Path.GetFileName(_pendingProjectionVideoPath)}");
                                PlayPendingProjectionVideo();
                            }
                        }
                    };
                    
                    projectionVideoView.SizeChanged += sizeChangedHandler;
                    
                    // 添加超时机制，如果3秒后SizeChanged事件没有触发，强制启用视频投屏
                    _projectionTimeoutTimer = new System.Windows.Threading.DispatcherTimer();
                    _projectionTimeoutTimer.Interval = TimeSpan.FromSeconds(3);
                    _projectionTimeoutTimer.Tick += (s, e) =>
                    {
                        _projectionTimeoutTimer.Stop();
                        _projectionTimeoutTimer = null;
                        if (!initialized)
                        {
                            //System.Diagnostics.Debug.WriteLine("⏰ 投影VideoView尺寸检测超时，强制启用视频投屏");
                            
                            if (_videoPlayerManager != null)
                            {
                                // 强制创建新的MediaPlayer给投影VideoView
                                _videoPlayerManager.InitializeMediaPlayer(projectionVideoView);
                                _videoPlayerManager.SetProjectionVideoView(projectionVideoView);
                                
                                // 如果当前正在播放视频，现在启用视频投屏
                                if (_videoPlayerManager.IsPlaying)
                                {
                                    //System.Diagnostics.Debug.WriteLine("📹 超时后强制启用视频投屏");
                                    EnableVideoProjection();
                                }
                            }
                            
                            initialized = true;
                            projectionVideoView.SizeChanged -= sizeChangedHandler;
                        }
                    };
                    _projectionTimeoutTimer.Start();
                }
                else if (projectionVideoView != null)
                {
                    //System.Diagnostics.Debug.WriteLine("✅ 投影VideoView已有尺寸，直接初始化");
                    
                    // VideoView已有尺寸，直接创建MediaPlayer
                    if (_videoPlayerManager != null)
                    {
                        _videoPlayerManager.InitializeMediaPlayer(projectionVideoView);
                        _videoPlayerManager.SetProjectionVideoView(projectionVideoView);
                        //System.Diagnostics.Debug.WriteLine("✅ 投影窗口MediaPlayer已创建并绑定到VideoView");
                        
                        // 如果当前正在播放视频，现在启用视频投屏
                        if (_videoPlayerManager.IsPlaying)
                        {
                            //System.Diagnostics.Debug.WriteLine("📹 投影VideoView直接初始化完成，现在启用视频投屏");
                            EnableVideoProjection();
                        }
                        
                        // 如果有待播放的视频，现在开始播放
                        if (!string.IsNullOrEmpty(_pendingProjectionVideoPath))
                        {
                            //System.Diagnostics.Debug.WriteLine($"🟠 检测到待播放视频，开始播放: {System.IO.Path.GetFileName(_pendingProjectionVideoPath)}");
                            PlayPendingProjectionVideo();
                        }
                    }
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 投影MediaPlayer绑定失败: {ex.Message}");
                //System.Diagnostics.Debug.WriteLine($"❌ 堆栈: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 播放待投影的视频
        /// </summary>
        private void PlayPendingProjectionVideo()
        {
            try
            {
                if (string.IsNullOrEmpty(_pendingProjectionVideoPath))
                    return;
                
                string videoPath = _pendingProjectionVideoPath;
                _pendingProjectionVideoPath = null; // 清除待播放路径
                
                // 切换到投影模式
                _videoPlayerManager.SwitchToProjectionMode();
                
                // 构建播放列表
                BuildVideoPlaylist(videoPath);
                
                // 开始播放
                _videoPlayerManager.Play(videoPath);
                
                ShowStatus($"🎬 正在投影播放: {System.IO.Path.GetFileName(videoPath)}");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 播放待投影视频失败: {ex.Message}");
            }
        }

        private void BtnProjection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 🆕 如果是文本编辑器模式，先更新投影内容
                if (TextEditorPanel.Visibility == Visibility.Visible && _currentTextProject != null)
                {
                    // 如果是打开投影操作，先渲染内容
                    if (!_projectionManager.IsProjectionActive)
                    {
                        // 先打开投影窗口
                        _projectionManager.ToggleProjection();
                        
                        // 然后更新内容
                        if (_projectionManager.IsProjectionActive)
                        {
                            UpdateProjectionFromCanvas();
                        }
                    }
                    else
                    {
                        // 如果已经打开，直接关闭
                        _projectionManager.ToggleProjection();
                    }
                }
                else
                {
                    // 普通模式，直接切换投影
                    _projectionManager.ToggleProjection();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"投影操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnSync.IsEnabled = false;
                BtnSync.Content = "🔄 同步中...";
                BtnSync.Background = new SolidColorBrush(Colors.LightGreen);

                var (added, removed, updated) = _importManager.SyncAllFolders();
                
                LoadProjects(); // 刷新项目树
                LoadSearchScopes(); // 刷新搜索范围
                
                ShowStatus($"🔄 同步完成: 新增 {added}, 删除 {removed}");
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 同步失败: {ex.Message}");
            }
            finally
            {
                BtnSync.IsEnabled = true;
                BtnSync.Content = "🔄 同步";
                BtnSync.Background = Brushes.Transparent; // 使用透明背景，让样式生效
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ResetView();
        }

        private void BtnOriginal_Click(object sender, RoutedEventArgs e)
        {
            ToggleOriginalMode();
        }

        private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ResetZoom();
            ShowStatus("已重置缩放比例");
        }

        private void BtnContact_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var contactWindow = new ContactWindow();
                contactWindow.Owner = this;
                contactWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开联系窗口失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 切换原图模式
        /// </summary>
        private void ToggleOriginalMode()
        {
            _originalMode = !_originalMode;
            _imageProcessor.OriginalMode = _originalMode;
            
            // 更新按钮样式
            if (_originalMode)
            {
                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                ShowStatus("✅ 已启用原图模式");
                
                // 在原图模式下,查找相似图片
                if (_currentImageId > 0)
                {
                    bool foundSimilar = _originalManager.FindSimilarImages(_currentImageId);
                    if (foundSimilar)
                    {
                        //System.Diagnostics.Debug.WriteLine("✅ 原图模式: 已找到相似图片");
                    }
                }
            }
            else
            {
                BtnOriginal.Background = Brushes.Transparent; // 使用透明背景，让样式生效
                ShowStatus("✅ 已关闭原图模式");
            }
            
            // 重新显示图片
            _imageProcessor.UpdateImage();
            
            // 更新投影窗口
            UpdateProjection();
        }

        /// <summary>
        /// 重置视图状态以进入文本编辑器
        /// </summary>
        private void ResetViewStateForTextEditor()
        {
            // 关闭原图模式
            if (_originalMode)
            {
                _originalMode = false;
                _imageProcessor.OriginalMode = false;
                BtnOriginal.Background = Brushes.Transparent;
                //System.Diagnostics.Debug.WriteLine("🔄 文本编辑器模式：已关闭原图模式");
            }
            
            // 重置缩放比例为1.0
            if (Math.Abs(_imageProcessor.ZoomRatio - 1.0) > 0.001)
            {
                _imageProcessor.ZoomRatio = 1.0;
                //System.Diagnostics.Debug.WriteLine("🔄 文本编辑器模式：已重置缩放比例为1.0");
            }
            
            // 关闭变色效果
            if (_isColorEffectEnabled)
            {
                _isColorEffectEnabled = false;
                BtnColorEffect.Background = Brushes.Transparent;
                //System.Diagnostics.Debug.WriteLine("🔄 文本编辑器模式：已关闭变色效果");
            }
            
            // 清除当前图片ID
            _currentImageId = 0;
            
            //System.Diagnostics.Debug.WriteLine("✅ 视图状态已重置为文本编辑器模式");
        }

        // BtnColorEffect_Click 已移至 MainWindow.Color.cs

        #endregion

        #region 关键帧控制栏事件
        // 注意：关键帧相关方法已移至 MainWindow.Keyframe.cs partial class

        /// <summary>
        /// 播放次数按钮点击事件：循环切换 1→2→...→10→∞→1
        /// </summary>
        private void BtnPlayCount_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackViewModel == null) return;
            
            // 循环切换：1→2→3→...→10→∞→1
            if (_playbackViewModel.PlayCount == -1)
            {
                _playbackViewModel.PlayCount = 1;
            }
            else if (_playbackViewModel.PlayCount >= 10)
            {
                _playbackViewModel.PlayCount = -1; // 无限循环
            }
            else
            {
                _playbackViewModel.PlayCount++;
            }
            
            // PlayCount属性的setter会自动触发SavePlayCountSetting
        }

        /// <summary>
        /// 播放次数按钮滚轮事件：限制在1-10次和无限循环之间
        /// </summary>
        private void BtnPlayCount_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_playbackViewModel == null) return;
            
            int delta = e.Delta > 0 ? 1 : -1;
            
            if (_playbackViewModel.PlayCount == -1)
            {
                // 无限循环：向上滚动→1次，向下滚动→10次
                _playbackViewModel.PlayCount = delta > 0 ? 1 : 10;
            }
            else
            {
                int newCount = _playbackViewModel.PlayCount + delta;
                if (newCount < 1)
                    _playbackViewModel.PlayCount = -1; // <1 变成无限循环
                else if (newCount > 10)
                    _playbackViewModel.PlayCount = -1; // >10 变成无限循环
                else
                    _playbackViewModel.PlayCount = newCount;
            }
            
            // PlayCount属性的setter会自动触发SavePlayCountSetting
            e.Handled = true;
        }

        /// <summary>
        /// 播放次数按钮双击事件：直接在按钮上编辑数字
        /// </summary>
        private void BtnPlayCount_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_playbackViewModel == null) return;
            
            // 保存原始内容
            var originalContent = BtnPlayCount.Content;
            
            // 创建TextBox替换按钮内容
            var textBox = new System.Windows.Controls.TextBox
            {
                Text = _playbackViewModel.PlayCount == -1 ? "∞" : _playbackViewModel.PlayCount.ToString(),
                FontSize = 14,
                Padding = new Thickness(5),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.DodgerBlue,
                BorderThickness = new Thickness(2)
            };

            // 替换按钮内容
            BtnPlayCount.Content = textBox;
            
            // 🔧 使用Dispatcher延迟聚焦，确保TextBox已完全渲染
            Dispatcher.BeginInvoke(new Action(() =>
            {
                textBox.Focus();
                textBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);

            // 失去焦点时恢复
            textBox.LostFocus += (s, args) =>
            {
                RestoreButton();
            };

            // 键盘事件处理
            textBox.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    // 确认输入
                    string input = textBox.Text.Trim();
                    
                    // 处理无限循环
                    if (input == "∞" || input == "-1")
                    {
                        _playbackViewModel.PlayCount = -1;
                        RestoreButton();
                        args.Handled = true;
                        return;
                    }
                    
                    // 验证数字输入
                    if (int.TryParse(input, out int count))
                    {
                        if (count >= 1 && count <= 10)
                        {
                            _playbackViewModel.PlayCount = count;
                            RestoreButton();
                        }
                        else
                        {
                            // 超出范围，恢复原值
                            ShowStatus("播放次数必须在 1-10 之间或 ∞");
                            RestoreButton();
                        }
                    }
                    else
                    {
                        // 无效输入，恢复原值
                        ShowStatus("请输入有效的数字或 ∞");
                        RestoreButton();
                    }
                    args.Handled = true;
                }
                else if (args.Key == Key.Escape)
                {
                    // 取消输入
                    RestoreButton();
                    args.Handled = true;
                }
            };

            void RestoreButton()
            {
                // 恢复按钮内容
                string text = _playbackViewModel.PlayCount == -1 ? "∞" : _playbackViewModel.PlayCount.ToString();
                BtnPlayCount.Content = $"🔄 {text}次";
                BtnPlayCount.Focus();
            }
            
            e.Handled = true;
        }

        private async void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackViewModel == null) return;
            
            // 同步当前图片ID到ViewModel
            _playbackViewModel.CurrentImageId = _currentImageId;
            _playbackViewModel.CurrentMode = _originalMode 
                ? Database.Models.Enums.PlaybackMode.Original 
                : Database.Models.Enums.PlaybackMode.Keyframe;
            
            // 如果准备开始录制（当前未在录制状态）
            if (!_playbackViewModel.IsRecording)
            {
                // 原图模式：先跳转到第一张相似图片
                if (_originalMode && _originalManager != null)
                {
                    // 查找相似图片
                    if (_originalManager.HasSimilarImages() || _originalManager.FindSimilarImages(_currentImageId))
                    {
                        // 获取第一张相似图片
                        var firstImageResult = _originalManager.GetFirstSimilarImage();
                        if (firstImageResult.success && firstImageResult.firstImageId.HasValue)
                        {
                            // 检查当前是否是第一张
                            if (_currentImageId != firstImageResult.firstImageId.Value)
                            {
                                //System.Diagnostics.Debug.WriteLine($"📹 [原图录制] 当前不在第一张 (当前ID:{_currentImageId}, 第一张ID:{firstImageResult.firstImageId.Value})，跳转到第一张");
                                
                                // 直接跳转到第一张图
                                _currentImageId = firstImageResult.firstImageId.Value;
                                LoadImage(firstImageResult.firstImagePath);
                                
                                // 短暂延迟确保UI更新
                                await Task.Delay(UI_UPDATE_DELAY_MILLISECONDS);
                                
                                ShowStatus($"✅ 已跳转到第一张相似图片");
                                //System.Diagnostics.Debug.WriteLine("✅ [原图录制] 已跳转到第一张，准备开始录制");
                            }
                            else
                            {
                                //System.Diagnostics.Debug.WriteLine("✅ [原图录制] 当前已在第一张");
                            }
                        }
                    }
                }
                // 关键帧模式：跳转到第一帧
                else if (!_originalMode && _keyframeManager != null)
                {
                    var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                    if (keyframes != null && keyframes.Count > 0)
                    {
                        // 如果当前不在第一帧，先直接跳转到第一帧
                        if (_keyframeManager.CurrentKeyframeIndex != 0)
                        {
                            //System.Diagnostics.Debug.WriteLine($"📹 [录制] 当前在第 {_keyframeManager.CurrentKeyframeIndex + 1} 帧，先跳转到第一帧");
                            
                            // 直接跳转到第一帧（不使用滚动动画）
                            _keyframeManager.UpdateKeyframeIndex(0);
                            var firstKeyframe = keyframes[0];
                            var targetOffset = firstKeyframe.Position * ImageScrollViewer.ScrollableHeight;
                            ImageScrollViewer.ScrollToVerticalOffset(targetOffset);
                            
                            if (IsProjectionEnabled)
                            {
                                UpdateProjection();
                            }
                            
                            _ = _keyframeManager.UpdateKeyframeIndicatorsAsync(); // 异步执行不等待
                            ShowStatus($"关键帧 1/{keyframes.Count}");
                            
                            //System.Diagnostics.Debug.WriteLine("✅ [录制] 已跳转到第一帧，准备开始录制");
                        }
                    }
                }
            }
            
            // 执行录制命令
            await _playbackViewModel.ToggleRecordingCommand.ExecuteAsync(null);
        }

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackViewModel == null) return;
            
            // 🎯 原图模式需要特殊处理（订阅事件）
            if (_originalMode)
            {
                // 如果正在播放，停止播放
                if (_playbackViewModel.IsPlaying)
                {
                    await StopOriginalModePlaybackAsync();
                }
                else
                {
                    // 开始播放（会订阅事件）
                    await StartOriginalModePlaybackAsync();
                }
            }
            else
            {
                // 关键帧模式直接使用ViewModel命令
                _playbackViewModel.CurrentImageId = _currentImageId;
                _playbackViewModel.CurrentMode = Database.Models.Enums.PlaybackMode.Keyframe;
                await _playbackViewModel.TogglePlaybackCommand.ExecuteAsync(null);
            }
        }

        /// <summary>
        /// 脚本按钮点击事件：显示和编辑脚本（支持关键帧和原图模式）
        /// 参考Python版本：script_manager.py 第40-50行（关键帧）
        /// 参考Python版本：keytime.py 行2170-2233（原图）
        /// </summary>
        private async void BtnScript_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            try
            {
                // 🎯 检查是否应该使用原图模式
                if (ShouldUseOriginalMode())
                {
                    await OpenOriginalModeScriptEditor();
                }
                else
                {
                    await OpenKeyframeModeScriptEditor();
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 显示脚本窗口失败: {ex.Message}");
                ShowStatus($"❌ 显示脚本失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 打开关键帧模式脚本编辑器
        /// </summary>
        private async System.Threading.Tasks.Task OpenKeyframeModeScriptEditor()
        {
            // 获取时间序列数据
            var timingRepository = App.GetRequiredService<Repositories.Interfaces.ITimingRepository>();
            var timings = await timingRepository.GetTimingSequenceAsync(_currentImageId);
            
            if (timings == null || timings.Count == 0)
            {
                MessageBox.Show("当前图片没有录制的时间数据", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 创建并显示脚本编辑窗口
            var scriptWindow = new ScriptEditWindow(_currentImageId, timings.ToList())
            {
                Owner = this
            };

            // 如果保存成功，刷新UI状态
            if (scriptWindow.ShowDialog() == true)
            {
                ShowStatus("✅ 关键帧脚本已更新");
            }
        }
        
        /// <summary>
        /// 打开原图模式脚本编辑器
        /// </summary>
        private async System.Threading.Tasks.Task OpenOriginalModeScriptEditor()
        {
            // 获取原图模式时间序列数据
            var originalRepo = App.GetRequiredService<Repositories.Interfaces.IOriginalModeRepository>();
            
            // 🎯 先通过当前图片ID查找BaseImageId（可能当前图片不是录制时的起始图片）
            var baseImageId = await originalRepo.FindBaseImageIdBySimilarImageAsync(_currentImageId);
            
            if (!baseImageId.HasValue)
            {
                // 如果找不到BaseImageId，尝试直接用_currentImageId查询
                baseImageId = _currentImageId;
            }
            
            //System.Diagnostics.Debug.WriteLine($"📝 [原图脚本] CurrentImageId={_currentImageId}, BaseImageId={baseImageId.Value}");
            
            var timings = await originalRepo.GetOriginalTimingSequenceAsync(baseImageId.Value);
            
            if (timings == null || timings.Count == 0)
            {
                MessageBox.Show("当前图片没有录制的原图模式时间数据", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 创建并显示脚本编辑窗口（使用BaseImageId）
            var scriptWindow = new ScriptEditWindow(baseImageId.Value, timings)
            {
                Owner = this
            };

            // 如果保存成功，刷新UI状态
            if (scriptWindow.ShowDialog() == true)
            {
                ShowStatus("✅ 原图模式脚本已更新");
            }
        }

        private async void BtnPauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackViewModel == null) return;
            await _playbackViewModel.TogglePauseCommand.ExecuteAsync(null);
        }
        

        #endregion

        #region 项目树事件

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_searchManager == null) return;

                string searchTerm = SearchBox.Text?.Trim() ?? "";
                string searchScope = (SearchScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";

                // System.Diagnostics.Debug.WriteLine($"🔍 搜索: 关键词='{searchTerm}', 范围='{searchScope}'");

                // 如果搜索词为空，重新加载所有项目
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    LoadProjects();
                    return;
                }

                // 执行搜索
                var searchResults = _searchManager.SearchProjects(searchTerm, searchScope);
                
                // System.Diagnostics.Debug.WriteLine($"📊 搜索结果: {searchResults?.Count ?? 0} 项");

                if (searchResults == null)
                {
                    LoadProjects();
                    return;
                }

                // 更新项目树
                _projectTreeItems.Clear();
                foreach (var item in searchResults)
                {
                    _projectTreeItems.Add(item);
                }

                // 不需要重新设置ItemsSource，ObservableCollection会自动通知UI更新
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 搜索失败: {ex}");
                MessageBox.Show($"搜索失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 双击搜索框清空内容
        /// </summary>
        private void SearchBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
            
            // 🆕 新增: 折叠所有展开的文件夹节点
            CollapseAllFolders();
            ShowStatus("✅ 已清除搜索并折叠所有文件夹");
        }

        /// <summary>
        /// 加载搜索范围选项
        /// </summary>
        private void LoadSearchScopes()
        {
            try
            {
                if (_searchManager == null) return;

                var scopes = _searchManager.GetSearchScopes();
                SearchScope.Items.Clear();
                
                foreach (var scope in scopes)
                {
                    var item = new ComboBoxItem { Content = scope };
                    SearchScope.Items.Add(item);
                }

                // 默认选中"全部"
                if (SearchScope.Items.Count > 0)
                {
                    SearchScope.SelectedIndex = 0;
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"加载搜索范围失败: {ex}");
            }
        }

        private void ProjectTree_MouseClick(object sender, MouseButtonEventArgs e)
        {
            // 获取点击的项目
            if (e.OriginalSource is FrameworkElement element)
            {
                var treeViewItem = FindParent<TreeViewItem>(element);
                if (treeViewItem != null && treeViewItem.DataContext is ProjectTreeItem selectedItem)
                {
                    // 🆕 处理文本项目节点：单击加载项目
                    if (selectedItem.Type == TreeItemType.Project || selectedItem.Type == TreeItemType.TextProject)
                    {
                        int projectId = selectedItem.Id;
                        _ = LoadTextProjectAsync(projectId);
                        return;
                    }

                    // 处理文件夹节点：单击展开/折叠
                    if (selectedItem.Type == TreeItemType.Folder)
                    {
                        // 🆕 自动退出文本编辑器（如果正在编辑项目）
                        AutoExitTextEditorIfNeeded();
                        
                        // 🆕 新增: 折叠其他所有文件夹节点
                        CollapseOtherFolders(selectedItem);
                        
                        // 切换展开/折叠状态(通过数据绑定的属性,更可靠)
                        selectedItem.IsExpanded = !selectedItem.IsExpanded;
                        
                        // 检查文件夹是否有原图标记,自动开关原图模式
                        bool hasFolderMark = _originalManager.CheckOriginalMark(ItemType.Folder, selectedItem.Id);
                        
                        if (hasFolderMark && !_originalMode)
                        {
                            // 文件夹有原图标记,自动启用原图模式
                            //System.Diagnostics.Debug.WriteLine($"🎯 文件夹有原图标记,自动启用原图模式: {selectedItem.Name}(黄色)");
                            _originalMode = true;
                            _imageProcessor.OriginalMode = true;
                            BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                            
                            // 🔑 关键修复: 检查当前显示的图片是否属于其他文件夹,如果是则清空显示
                            if (_currentImageId > 0 && !string.IsNullOrEmpty(_imagePath))
                            {
                                var currentMediaFile = _dbManager.GetMediaFileById(_currentImageId);
                                if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                                {
                                    // 如果当前图片不属于这个原图文件夹,清空显示
                                    if (currentMediaFile.FolderId.Value != selectedItem.Id)
                                    {
                                        //System.Diagnostics.Debug.WriteLine($"🎯 当前图片不属于原图文件夹,清空显示");
                                        ClearImageDisplay();
                                    }
                                }
                            }
                            
                            ShowStatus($"✅ 已启用原图模式: {selectedItem.Name}(黄色)");
                        }
                        else if (!hasFolderMark && _originalMode)
                        {
                            // 文件夹没有原图标记,自动关闭原图模式
                            //System.Diagnostics.Debug.WriteLine($"🎯 文件夹无原图标记,自动关闭原图模式: {selectedItem.Name}");
                            _originalMode = false;
                            _imageProcessor.OriginalMode = false;
                            BtnOriginal.Background = Brushes.Transparent; // 使用透明背景，让样式生效
                            
                            // 🔑 关键修复: 检查当前显示的图片是否属于其他文件夹,如果是则清空显示
                            if (_currentImageId > 0 && !string.IsNullOrEmpty(_imagePath))
                            {
                                var currentMediaFile = _dbManager.GetMediaFileById(_currentImageId);
                                if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                                {
                                    // 如果当前图片不属于这个非原图文件夹,清空显示
                                    if (currentMediaFile.FolderId.Value != selectedItem.Id)
                                    {
                                        //System.Diagnostics.Debug.WriteLine($"🎯 当前图片不属于非原图文件夹,清空显示");
                                        ClearImageDisplay();
                                    }
                                }
                            }
                            
                            ShowStatus($"✅ 已关闭原图模式: {selectedItem.Name}");
                        }
                        
                        // 🎨 变色功能逻辑：只在切换到不同文件夹时才自动调整变色状态
                        bool isSameFolder = (_currentFolderId == selectedItem.Id);
                        
                        if (!isSameFolder)
                        {
                            // 切换到不同文件夹：检查标记并自动调整变色状态
                            bool hasColorEffectMark = _dbManager.HasFolderAutoColorEffect(selectedItem.Id);
                            
                            if (hasColorEffectMark && !_isColorEffectEnabled)
                            {
                                // 文件夹有变色标记，只更新 MainWindow 状态（不触发 ImageProcessor）
                                //System.Diagnostics.Debug.WriteLine($"🎨 文件夹有变色标记，更新UI状态: {selectedItem.Name}");
                                _isColorEffectEnabled = true;
                                // ⚠️ 关键：不设置 _imageProcessor.IsInverted，因为它的 setter 会自动调用 UpdateImage()
                                // 只在 LoadImage() 时才同步状态到 ImageProcessor
                                BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // 金色
                                
                                ShowStatus($"✅ 已切换到变色文件夹: {selectedItem.Name}");
                            }
                            else if (!hasColorEffectMark && _isColorEffectEnabled)
                            {
                                // 文件夹没有变色标记，只更新 MainWindow 状态（不触发 ImageProcessor）
                                //System.Diagnostics.Debug.WriteLine($"🎨 文件夹无变色标记，更新UI状态: {selectedItem.Name}");
                                _isColorEffectEnabled = false;
                                // ⚠️ 关键：不设置 _imageProcessor.IsInverted，因为它的 setter 会自动调用 UpdateImage()
                                // 只在 LoadImage() 时才同步状态到 ImageProcessor
                                BtnColorEffect.Background = Brushes.Transparent;
                                
                                ShowStatus($"✅ 已切换到无变色文件夹: {selectedItem.Name}");
                            }
                            
                            // 🎯 更新当前文件夹ID
                            _currentFolderId = selectedItem.Id;
                        }
                        // else: 重复点击同一文件夹，保持变色状态不变
                        
                        e.Handled = true; // 阻止默认行为
                    }
                    // 处理文件节点：单击加载
                    else if (selectedItem.Type == TreeItemType.File && !string.IsNullOrEmpty(selectedItem.Path))
                    {
                        // 🆕 自动退出文本编辑器（如果正在编辑项目）
                        AutoExitTextEditorIfNeeded();
                        
                        // 保存当前图片ID
                        _currentImageId = selectedItem.Id;
                        
                        // 🔑 关键优化: 检查文件所在文件夹的原图标记和变色标记,自动开关模式
                        var mediaFile = _dbManager.GetMediaFileById(_currentImageId);
                        if (mediaFile != null && mediaFile.FolderId.HasValue)
                        {
                            // 检查原图标记
                            bool hasFolderOriginalMark = _originalManager.CheckOriginalMark(ItemType.Folder, mediaFile.FolderId.Value);
                            
                            if (hasFolderOriginalMark && !_originalMode)
                            {
                                // 父文件夹有原图标记,自动启用原图模式
                                //System.Diagnostics.Debug.WriteLine($"🎯 文件所在文件夹有原图标记,自动启用原图模式");
                                _originalMode = true;
                                _imageProcessor.OriginalMode = true;
                                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                            }
                            else if (!hasFolderOriginalMark && _originalMode)
                            {
                                // 父文件夹没有原图标记,自动关闭原图模式
                                //System.Diagnostics.Debug.WriteLine($"🎯 文件所在文件夹无原图标记,自动关闭原图模式");
                                _originalMode = false;
                                _imageProcessor.OriginalMode = false;
                                BtnOriginal.Background = Brushes.Transparent; // 使用透明背景，让样式生效
                            }
                            
                            // 🎨 变色功能逻辑优化：
                            // 1. 如果切换到不同文件夹，根据标记自动开启/关闭
                            // 2. 如果是同文件夹内切换图片，保持当前变色状态不变
                            int newFolderId = mediaFile.FolderId.Value;
                            bool isSameFolder = (_currentFolderId == newFolderId);
                            
                            if (!isSameFolder)
                            {
                                // 切换到不同文件夹：根据标记自动调整变色状态
                                bool hasFolderColorEffectMark = _dbManager.HasFolderAutoColorEffect(newFolderId);
                                
                                if (hasFolderColorEffectMark && !_isColorEffectEnabled)
                                {
                                    // 文件夹有变色标记，自动启用变色效果
                                    //System.Diagnostics.Debug.WriteLine($"🎨 切换到变色文件夹，自动启用变色效果");
                                    _isColorEffectEnabled = true;
                                    BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // 金色
                                }
                                else if (!hasFolderColorEffectMark && _isColorEffectEnabled)
                                {
                                    // 文件夹没有变色标记，自动关闭变色效果
                                    //System.Diagnostics.Debug.WriteLine($"🎨 切换到非变色文件夹，自动关闭变色效果");
                                    _isColorEffectEnabled = false;
                                    BtnColorEffect.Background = Brushes.Transparent;
                                }
                                
                                // 更新当前文件夹ID
                                _currentFolderId = newFolderId;
                            }
                            // else: 同文件夹内切换图片，保持当前变色状态不变
                        }
                        
                        if (System.IO.File.Exists(selectedItem.Path))
                        {
                            // 根据文件类型进行不同处理
                            switch (selectedItem.FileType)
                            {
                                case FileType.Image:
                                    // 切换回图片模式
                                    SwitchToImageMode();
                                    // 加载图片（预缓存已在LoadImage中触发）
                                    LoadImage(selectedItem.Path);
                                    // ShowStatus($"📷 已加载: {selectedItem.Name}");
                                    break;
                                
                                case FileType.Video:
                                case FileType.Audio:
                                    // 视频/音频：单击只选中，不播放
                                    // 保存当前选中的视频路径（用于双击播放和投影播放）
                                    _imagePath = selectedItem.Path;
                                    string fileType = selectedItem.FileType == FileType.Video ? "视频" : "音频";
                                    ShowStatus($"✅ 已选中{fileType}: {selectedItem.Name} (双击播放)");
                                    break;
                            }
                        }
                        else
                        {
                            ShowStatus($"❌ 文件不存在: {selectedItem.Name}");
                        }
                    }
                }
            }
        }

        private void ProjectTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ⏱️ 性能调试：测量切换图片响应时间
            var clickTime = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine($"\n🖱️ ========== 用户双击切换图片 ==========");
            
            // 获取双击的项目
            if (e.OriginalSource is FrameworkElement element)
            {
                var treeViewItem = FindParent<TreeViewItem>(element);
                if (treeViewItem != null && treeViewItem.DataContext is ProjectTreeItem selectedItem)
                {
                    // 只处理文件节点的双击
                    if (selectedItem.Type == TreeItemType.File && !string.IsNullOrEmpty(selectedItem.Path))
                    {
                        if (System.IO.File.Exists(selectedItem.Path))
                        {
                            // 根据文件类型进行处理
                            switch (selectedItem.FileType)
                            {
                                case FileType.Video:
                                case FileType.Audio:
                                    // 检查投影状态
                                    if (_projectionManager != null && _projectionManager.IsProjectionActive)
                                    {
                                        // 投影已开启，直接在投影屏幕播放
                                        LoadAndDisplayVideoOnProjection(selectedItem.Path);
                                    }
                                    else
                                    {
                                        // 投影未开启，在主屏幕播放
                                        LoadAndDisplayVideo(selectedItem.Path);
                                    }
                                    
                                    string fileType = selectedItem.FileType == FileType.Video ? "视频" : "音频";
                                    ShowStatus($"🎬 正在播放: {selectedItem.Name}");
                                    break;
                                    
                                case FileType.Image:
                                    // 图片双击也加载（保持原有行为）
                                    System.Diagnostics.Debug.WriteLine($"📷 切换到图片: {selectedItem.Name}");
                                    var switchStart = clickTime.ElapsedMilliseconds;
                                    
                                    SwitchToImageMode();
                                    
                                    // 🔧 关键修复：手动选择图片时，停止当前播放
                                    if (_playbackViewModel != null && _playbackViewModel.IsPlaying)
                                    {
                                        System.Diagnostics.Debug.WriteLine("🛑 停止当前播放");
                                        _ = _playbackViewModel.StopPlaybackCommand.ExecuteAsync(null);
                                    }
                                    
                                    var loadStart = clickTime.ElapsedMilliseconds;
                                    LoadImage(selectedItem.Path);
                                    var loadTime = clickTime.ElapsedMilliseconds - loadStart;
                                    
                                    clickTime.Stop();
                                    System.Diagnostics.Debug.WriteLine($"⏱️ [切换图片] 准备耗时: {switchStart}ms, 加载耗时: {loadTime}ms, 总耗时: {clickTime.ElapsedMilliseconds}ms");
                                    System.Diagnostics.Debug.WriteLine($"========================================\n");
                                    
                                    // ⚡ 预缓存已在LoadImage中触发，无需重复
                                    break;
                            }
                        }
                        else
                        {
                            ShowStatus($"❌ 文件不存在: {selectedItem.Name}");
                        }
                    }
                }
            }
        }
        
        private void ProjectTree_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 获取右键点击的项目
            if (e.OriginalSource is FrameworkElement element)
            {
                var treeViewItem = FindParent<TreeViewItem>(element);
                
                // 🆕 如果点击在空白区域（没有TreeViewItem），显示新建项目菜单
                if (treeViewItem == null)
                {
                    var contextMenu = new ContextMenu();
                    contextMenu.FontSize = 14;
                    
                    var newProjectItem = new MenuItem { Header = "📝 新建项目" };
                    newProjectItem.Click += async (s, args) =>
                    {
                        string projectName = await GenerateDefaultProjectNameAsync();
                        await CreateTextProjectAsync(projectName);
                    };
                    contextMenu.Items.Add(newProjectItem);
                    
                    contextMenu.IsOpen = true;
                    contextMenu.PlacementTarget = sender as UIElement;
                    e.Handled = true;
                    return;
                }
                
                if (treeViewItem != null && treeViewItem.DataContext is ProjectTreeItem item)
                {
                    // 创建右键菜单
                    var contextMenu = new ContextMenu();
                    contextMenu.FontSize = 14;

                    if (item.Type == TreeItemType.Folder)
                    {
                        // 文件夹右键菜单
                        
                        // 检查文件夹是否包含视频/音频文件或图片文件
                        var folderFiles = _dbManager.GetMediaFilesByFolder(item.Id);
                        bool hasVideoOrAudio = folderFiles.Any(f => f.FileType == FileType.Video || f.FileType == FileType.Audio);
                        bool hasImages = folderFiles.Any(f => f.FileType == FileType.Image);
                        
                        // 只有图片文件夹才显示原图标记菜单
                        if (hasImages)
                        {
                            // 文件夹原图标记菜单
                            bool hasFolderMark = _originalManager.CheckOriginalMark(ItemType.Folder, item.Id);
                            
                            if (hasFolderMark)
                            {
                                // 如果已有标记,显示"取消原图"
                                var unmarkFolderItem = new MenuItem { Header = "取消原图" };
                                unmarkFolderItem.Click += (s, args) => UnmarkOriginalFolder(item);
                                contextMenu.Items.Add(unmarkFolderItem);
                            }
                            else
                            {
                                // 如果没有标记,显示原图标记选项
                                var markFolderMenuItem = new MenuItem { Header = "标记为原图" };
                                
                                // 循环模式
                                var loopFolderItem = new MenuItem { Header = "循环模式" };
                                loopFolderItem.Click += (s, args) => MarkFolderAsOriginal(item, MarkType.Loop);
                                markFolderMenuItem.Items.Add(loopFolderItem);
                                
                                // 顺序模式
                                var sequenceFolderItem = new MenuItem { Header = "顺序模式" };
                                sequenceFolderItem.Click += (s, args) => MarkFolderAsOriginal(item, MarkType.Sequence);
                                markFolderMenuItem.Items.Add(sequenceFolderItem);
                                
                                contextMenu.Items.Add(markFolderMenuItem);
                            }
                            
                            contextMenu.Items.Add(new Separator());
                            
                            // 变色效果标记菜单（只有图片文件夹）
                            bool hasColorEffectMark = _dbManager.HasFolderAutoColorEffect(item.Id);
                            
                            if (hasColorEffectMark)
                            {
                                // 已有变色标记，显示"取消变色"
                                var unmarkColorItem = new MenuItem { Header = "🎨 取消变色标记" };
                                unmarkColorItem.Click += (s, args) => UnmarkFolderColorEffect(item);
                                contextMenu.Items.Add(unmarkColorItem);
                            }
                            else
                            {
                                // 没有变色标记，显示"标记为变色"
                                var markColorItem = new MenuItem { Header = "🎨 标记为变色" };
                                markColorItem.Click += (s, args) => MarkFolderColorEffect(item);
                                contextMenu.Items.Add(markColorItem);
                            }
                            
                            contextMenu.Items.Add(new Separator());
                        }
                        
                        // 只有包含视频/音频的文件夹才显示播放模式菜单
                        if (hasVideoOrAudio)
                        {
                            // 视频播放模式菜单
                            var currentPlayMode = _dbManager.GetFolderVideoPlayMode(item.Id);
                            var playModeMenuItem = new MenuItem { Header = "🎵 视频播放模式" };
                            
                            // 顺序播放
                            var sequentialItem = new MenuItem 
                            { 
                                Header = "⬍⬆ 顺序播放",
                                IsCheckable = true,
                                IsChecked = currentPlayMode == "sequential"
                            };
                            sequentialItem.Click += (s, args) => SetFolderPlayMode(item, "sequential");
                            playModeMenuItem.Items.Add(sequentialItem);
                            
                            // 随机播放
                            var randomItem = new MenuItem 
                            { 
                                Header = "🔀 随机播放",
                                IsCheckable = true,
                                IsChecked = currentPlayMode == "random"
                            };
                            randomItem.Click += (s, args) => SetFolderPlayMode(item, "random");
                            playModeMenuItem.Items.Add(randomItem);
                            
                            // 列表循环
                            var loopAllItem = new MenuItem 
                            { 
                                Header = "🔁 列表循环",
                                IsCheckable = true,
                                IsChecked = currentPlayMode == "loop_all"
                            };
                            loopAllItem.Click += (s, args) => SetFolderPlayMode(item, "loop_all");
                            playModeMenuItem.Items.Add(loopAllItem);
                            
                            playModeMenuItem.Items.Add(new Separator());
                            
                            // 清除标记
                            var clearModeItem = new MenuItem { Header = "✖ 清除播放模式" };
                            clearModeItem.Click += (s, args) => ClearFolderPlayMode(item);
                            playModeMenuItem.Items.Add(clearModeItem);
                            
                            contextMenu.Items.Add(playModeMenuItem);
                            contextMenu.Items.Add(new Separator());
                        }
                        
                        // 检查是否为手动排序文件夹
                        bool isManualSort = _dbManager.IsManualSortFolder(item.Id);
                        if (isManualSort)
                        {
                            var resetSortItem = new MenuItem { Header = "🔄 重置排序" };
                            resetSortItem.Click += (s, args) => ResetFolderSort(item);
                            contextMenu.Items.Add(resetSortItem);
                            contextMenu.Items.Add(new Separator());
                        }
                        
                        // 标记高亮色菜单
                        var highlightColorItem = new MenuItem { Header = "🎨 标记高亮色" };
                        highlightColorItem.Click += (s, args) => SetFolderHighlightColor(item);
                        contextMenu.Items.Add(highlightColorItem);
                        
                        contextMenu.Items.Add(new Separator());
                        
                        // 文件夹顺序调整菜单
                        var moveUpItem = new MenuItem { Header = "⬆️ 上移" };
                        moveUpItem.Click += (s, args) => MoveFolderUp(item);
                        contextMenu.Items.Add(moveUpItem);
                        
                        var moveDownItem = new MenuItem { Header = "⬇️ 下移" };
                        moveDownItem.Click += (s, args) => MoveFolderDown(item);
                        contextMenu.Items.Add(moveDownItem);
                        
                        contextMenu.Items.Add(new Separator());
                        
                        var deleteItem = new MenuItem { Header = "删除文件夹" };
                        deleteItem.Click += (s, args) => DeleteFolder(item);
                        contextMenu.Items.Add(deleteItem);

                        var syncItem = new MenuItem { Header = "同步文件夹" };
                        syncItem.Click += (s, args) => SyncFolder(item);
                        contextMenu.Items.Add(syncItem);
                    }
                    else if (item.Type == TreeItemType.File)
                    {
                        // 文件右键菜单
                        
                        // 原图标记菜单
                        if (item.FileType == FileType.Image)
                        {
                            bool hasOriginalMark = _originalManager.CheckOriginalMark(ItemType.Image, item.Id);
                            
                            if (hasOriginalMark)
                            {
                                // 如果已有标记,显示"取消原图"
                                var unmarkItem = new MenuItem { Header = "取消原图" };
                                unmarkItem.Click += (s, args) => UnmarkOriginal(item);
                                contextMenu.Items.Add(unmarkItem);
                            }
                            else
                            {
                                // 如果没有标记,显示原图标记选项
                                var markMenuItem = new MenuItem { Header = "标记为原图" };
                                
                                // 循环模式
                                var loopItem = new MenuItem { Header = "循环模式" };
                                loopItem.Click += (s, args) => MarkAsOriginal(item, MarkType.Loop);
                                markMenuItem.Items.Add(loopItem);
                                
                                // 顺序模式
                                var sequenceItem = new MenuItem { Header = "顺序模式" };
                                sequenceItem.Click += (s, args) => MarkAsOriginal(item, MarkType.Sequence);
                                markMenuItem.Items.Add(sequenceItem);
                                
                                contextMenu.Items.Add(markMenuItem);
                            }
                            
                            contextMenu.Items.Add(new Separator());
                        }
                        
                        var deleteItem = new MenuItem { Header = "删除文件" };
                        deleteItem.Click += (s, args) => DeleteFile(item);
                        contextMenu.Items.Add(deleteItem);
                    }
                    else if (item.Type == TreeItemType.Project || item.Type == TreeItemType.TextProject)
                    {
                        // 文本项目右键菜单
                        var renameItem = new MenuItem { Header = "✏️ 重命名" };
                        renameItem.Click += (s, args) => RenameTextProjectAsync(item);
                        contextMenu.Items.Add(renameItem);
                        
                        contextMenu.Items.Add(new Separator());
                        
                        var deleteItem = new MenuItem { Header = "🗑️ 删除项目" };
                        deleteItem.Click += async (s, args) => await DeleteTextProjectAsync(item);
                        contextMenu.Items.Add(deleteItem);
                    }

                    contextMenu.IsOpen = true;
                }
            }
        }

        /// <summary>
        /// 删除文件夹
        /// </summary>
        private void DeleteFolder(ProjectTreeItem item)
        {
            var result = MessageBox.Show(
                $"确定要删除文件夹 '{item.Name}' 吗？\n这将从项目中移除该文件夹及其所有文件。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                _dbManager.DeleteFolder(item.Id);
                LoadProjects();           // 刷新项目树
                LoadSearchScopes();       // 刷新搜索范围
                ShowStatus($"🗑️ 已删除文件夹: {item.Name}");
            }
        }

        /// <summary>
        /// 同步文件夹
        /// </summary>
        private void SyncFolder(ProjectTreeItem item)
        {
            var (added, removed, updated) = _importManager.SyncFolder(item.Id);
            LoadProjects();
            ShowStatus($"🔄 同步完成: {item.Name} (新增 {added}, 删除 {removed})");
        }
        
        /// <summary>
        /// 设置文件夹的视频播放模式
        /// </summary>
        private void SetFolderPlayMode(ProjectTreeItem item, string playMode)
        {
            try
            {
                _dbManager.SetFolderVideoPlayMode(item.Id, playMode);
                
                string[] modeNames = { "顺序播放", "随机播放", "列表循环" };
                string modeName = playMode switch
                {
                    "sequential" => modeNames[0],
                    "random" => modeNames[1],
                    "loop_all" => modeNames[2],
                    _ => "未知"
                };
                
                // 刷新项目树以更新图标
                LoadProjects();
                
                ShowStatus($"✅ 已设置文件夹 [{item.Name}] 的播放模式: {modeName}");
                //System.Diagnostics.Debug.WriteLine($"✅ 文件夹 [{item.Name}] 播放模式: {modeName}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 设置播放模式失败: {ex.Message}");
                MessageBox.Show($"设置播放模式失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 清除文件夹的视频播放模式
        /// </summary>
        private void ClearFolderPlayMode(ProjectTreeItem item)
        {
            try
            {
                _dbManager.ClearFolderVideoPlayMode(item.Id);
                
                // 刷新项目树以更新图标
                LoadProjects();
                
                ShowStatus($"✅ 已清除文件夹 [{item.Name}] 的播放模式");
                //System.Diagnostics.Debug.WriteLine($"✅ 已清除文件夹 [{item.Name}] 的播放模式");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 清除播放模式失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 标记文件夹自动变色
        /// </summary>
        private void MarkFolderColorEffect(ProjectTreeItem item)
        {
            try
            {
                _dbManager.MarkFolderAutoColorEffect(item.Id);
                LoadProjects();
                ShowStatus($"✅ 已标记文件夹 [{item.Name}] 自动变色");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 标记变色失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 取消文件夹变色标记
        /// </summary>
        private void UnmarkFolderColorEffect(ProjectTreeItem item)
        {
            try
            {
                _dbManager.UnmarkFolderAutoColorEffect(item.Id);
                LoadProjects();
                ShowStatus($"✅ 已取消文件夹 [{item.Name}] 的变色标记");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 取消变色标记失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 设置文件夹高亮颜色
        /// </summary>
        private void SetFolderHighlightColor(ProjectTreeItem item)
        {
            try
            {
                // 创建系统颜色选择对话框
                var colorDialog = new System.Windows.Forms.ColorDialog();
                colorDialog.FullOpen = true; // 默认展开自定义颜色面板
                colorDialog.AnyColor = true; // 允许选择任意颜色
                
                // 如果文件夹已有自定义颜色，设置为初始颜色
                string existingColor = _dbManager.GetFolderHighlightColor(item.Id);
                if (!string.IsNullOrEmpty(existingColor))
                {
                    try
                    {
                        var wpfColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(existingColor);
                        colorDialog.Color = System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
                    }
                    catch { }
                }
                
                // 显示颜色选择对话框
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // 将选中的颜色转换为十六进制格式
                    var selectedColor = colorDialog.Color;
                    string colorHex = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
                    
                    // 设置自定义颜色
                    _dbManager.SetFolderHighlightColor(item.Id, colorHex);
                    ShowStatus($"✅ 已设置文件夹 [{item.Name}] 的高亮颜色: {colorHex}");
                    
                    // 刷新项目树
                    LoadProjects();
                    
                    // 如果当前有搜索内容，刷新搜索结果
                    string searchTerm = SearchBox.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(searchTerm))
                    {
                        string searchScope = (SearchScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";
                        var searchResults = _searchManager.SearchProjects(searchTerm, searchScope);
                        
                        if (searchResults != null)
                        {
                            _projectTreeItems.Clear();
                            foreach (var result in searchResults)
                            {
                                _projectTreeItems.Add(result);
                            }
                            // 不需要重新设置ItemsSource，ObservableCollection会自动通知UI更新
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 设置高亮颜色失败: {ex.Message}");
                MessageBox.Show($"设置高亮颜色失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 获取播放模式对应的图标
        /// </summary>
        private (string iconKind, string iconColor) GetPlayModeIcon(string playMode)
        {
            return playMode switch
            {
                "sequential" => ("SortAscending", ICON_COLOR_SEQUENTIAL),  // 顺序播放 - 蓝色
                "random" => ("Shuffle", ICON_COLOR_RANDOM),                // 随机播放 - 橙色
                "loop_all" => ("Repeat", ICON_COLOR_LOOP),                 // 列表循环 - 绿色
                _ => ("Shuffle", ICON_COLOR_RANDOM)                        // 默认随机播放 - 橙色
            };
        }

        /// <summary>
        /// 重置文件夹排序（取消手动排序，恢复自动排序）
        /// </summary>
        private void ResetFolderSort(ProjectTreeItem item)
        {
            var result = MessageBox.Show(
                $"确定要重置文件夹 '{item.Name}' 的排序吗？\n将按照文件名自动排序。",
                "确认重置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 取消手动排序标记
                    _dbManager.UnmarkFolderAsManualSort(item.Id);
                    
                    // 重新应用自动排序规则
                    var files = _dbManager.GetMediaFilesByFolder(item.Id);
                    if (files.Count > 0)
                    {
                        // 使用SortManager的排序键对文件进行排序
                        var sortedFiles = files
                            .Select(f => new
                            {
                                File = f,
                                SortKey = _sortManager.GetSortKey(f.Name + System.IO.Path.GetExtension(f.Path))
                            })
                            .OrderBy(x => x.SortKey.prefixNumber)
                            .ThenBy(x => x.SortKey.pinyinPart)
                            .ThenBy(x => x.SortKey.suffixNumber)
                            .Select(x => x.File)
                            .ToList();

                        // 更新OrderIndex
                        for (int i = 0; i < sortedFiles.Count; i++)
                        {
                            sortedFiles[i].OrderIndex = i + 1;
                        }

                        // 保存更改
                        _dbManager.UpdateMediaFilesOrder(sortedFiles);
                    }
                    
                    LoadProjects();
                    ShowStatus($"✅ 已重置文件夹排序: {item.Name}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"重置排序失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 标记文件夹为原图
        /// </summary>
        private void MarkFolderAsOriginal(ProjectTreeItem item, MarkType markType)
        {
            bool success = _originalManager.AddOriginalMark(ItemType.Folder, item.Id, markType);
            
            if (success)
            {
                string modeText = markType == MarkType.Loop ? "循环" : "顺序";
                ShowStatus($"✅ 已标记文件夹为原图({modeText}): {item.Name}");
                
                // 立即刷新项目树显示
                LoadProjects();
            }
            else
            {
                ShowStatus($"❌ 标记文件夹失败: {item.Name}");
            }
        }

        /// <summary>
        /// 取消文件夹原图标记
        /// </summary>
        private void UnmarkOriginalFolder(ProjectTreeItem item)
        {
            bool success = _originalManager.RemoveOriginalMark(ItemType.Folder, item.Id);
            
            if (success)
            {
                ShowStatus($"✅ 已取消文件夹原图标记: {item.Name}");
                
                // 刷新项目树显示
                LoadProjects();
            }
            else
            {
                ShowStatus($"❌ 取消文件夹标记失败: {item.Name}");
            }
        }

        /// <summary>
        /// 标记为原图
        /// </summary>
        private void MarkAsOriginal(ProjectTreeItem item, MarkType markType)
        {
            bool success = _originalManager.AddOriginalMark(ItemType.Image, item.Id, markType);
            
            if (success)
            {
                string modeText = markType == MarkType.Loop ? "循环" : "顺序";
                ShowStatus($"✅ 已标记为原图({modeText}): {item.Name}");
                
                // 立即刷新项目树显示
                LoadProjects();
                
                // 如果标记的是当前正在显示的图片,自动启用原图模式
                if (_currentImageId == item.Id && !_originalMode)
                {
                    //System.Diagnostics.Debug.WriteLine($"🎯 自动启用原图模式: {item.Name}");
                    _originalMode = true;
                    _imageProcessor.OriginalMode = true;
                    
                    // 更新按钮样式
                    BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                    
                    // 查找相似图片
                    _originalManager.FindSimilarImages(_currentImageId);
                    
                    // 重新显示图片
                    _imageProcessor.UpdateImage();
                    
                    // 更新投影窗口
                    UpdateProjection();
                    
                    ShowStatus("✅ 已自动启用原图模式");
                }
            }
            else
            {
                ShowStatus($"❌ 标记失败: {item.Name}");
            }
        }

        /// <summary>
        /// 取消原图标记
        /// </summary>
        private void UnmarkOriginal(ProjectTreeItem item)
        {
            bool success = _originalManager.RemoveOriginalMark(ItemType.Image, item.Id);
            
            if (success)
            {
                ShowStatus($"✅ 已取消原图标记: {item.Name}");
                
                // 立即刷新项目树显示
                LoadProjects();
                
                // 如果取消的是当前正在显示的图片,关闭原图模式
                if (_currentImageId == item.Id && _originalMode)
                {
                    //System.Diagnostics.Debug.WriteLine($"🎯 自动关闭原图模式: {item.Name}");
                    _originalMode = false;
                    _imageProcessor.OriginalMode = false;
                    
                    // 更新按钮样式
                    BtnOriginal.Background = Brushes.Transparent; // 使用透明背景，让样式生效
                    
                    // 重新显示图片
                    _imageProcessor.UpdateImage();
                    
                    // 更新投影窗口
                    UpdateProjection();
                    
                    ShowStatus("✅ 已自动关闭原图模式");
                }
            }
            else
            {
                ShowStatus($"❌ 取消标记失败: {item.Name}");
            }
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        private void DeleteFile(ProjectTreeItem item)
        {
            var result = MessageBox.Show(
                $"确定要删除文件 '{item.Name}' 吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                _dbManager.DeleteMediaFile(item.Id);
                LoadProjects();
                ShowStatus($"🗑️ 已删除文件: {item.Name}");
            }
        }

        /// <summary>
        /// 查找父级元素
        /// </summary>
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        /// <summary>
        /// 折叠所有文件夹节点
        /// </summary>
        private void CollapseAllFolders()
        {
            try
            {
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                foreach (var item in treeItems)
                {
                    if (item.Type == TreeItemType.Folder)
                    {
                        CollapseFolder(item);
                    }
                }
                // System.Diagnostics.Debug.WriteLine("📁 已折叠所有文件夹节点");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"折叠所有文件夹失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 折叠除指定文件夹外的所有其他文件夹
        /// </summary>
        private void CollapseOtherFolders(ProjectTreeItem exceptFolder)
        {
            try
            {
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                foreach (var item in treeItems)
                {
                    if (item.Type == TreeItemType.Folder && item.Id != exceptFolder.Id)
                    {
                        CollapseFolder(item);
                    }
                }
                // System.Diagnostics.Debug.WriteLine($"📁 已折叠除 {exceptFolder.Name} 外的所有文件夹");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"折叠其他文件夹失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 递归折叠文件夹及其子文件夹
        /// </summary>
        private void CollapseFolder(ProjectTreeItem folder)
        {
            if (folder == null) return;
            
            // 折叠当前文件夹
            folder.IsExpanded = false;
            
            // 递归折叠子文件夹
            if (folder.Children != null)
            {
                foreach (var child in folder.Children)
                {
                    if (child.Type == TreeItemType.Folder)
                    {
                        CollapseFolder(child);
                    }
                }
            }
        }

        #endregion

        #region 图像处理核心功能

        private void LoadImage(string path)
        {
            // ⏱️ 性能调试：测量图片加载总耗时
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                _imagePath = path;
                
                // 🔄 重置缩放状态（切换图片时恢复默认缩放）
                _currentZoom = 1.0;
                if (_imageProcessor != null)
                {
                    _imageProcessor.ZoomRatio = 1.0;
                }
                
                // 🎨 关键修复：在加载图片之前，同步变色效果状态到 ImageProcessor
                // 这样 ImageProcessor 在生成缓存时就知道是否需要应用变色效果
                _imageProcessor.IsInverted = _isColorEffectEnabled;
                
                // 使用ImageProcessor加载图片
                var loadStart = sw.ElapsedMilliseconds;
                bool success = _imageProcessor.LoadImage(path);
                var loadTime = sw.ElapsedMilliseconds - loadStart;
                System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ImageProcessor.LoadImage: {loadTime}ms");
                
                if (success)
                {
                    // 🔧 性能优化：移除不必要的克隆，直接使用_imageProcessor的引用
                    // ImageProcessor内部管理图片资源和背景检测
                    
                    // ⭐ 关键逻辑: 检查当前图片是否有原图标记,自动启用/关闭原图模式
                    if (_currentImageId > 0)
                    {
                        var dbCheckStart = sw.ElapsedMilliseconds;
                        bool shouldUseOriginal = _originalManager.ShouldUseOriginalMode(_currentImageId);
                        var dbCheckTime = sw.ElapsedMilliseconds - dbCheckStart;
                        System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 数据库检查原图标记: {dbCheckTime}ms");
                        
                        if (shouldUseOriginal && !_originalMode)
                        {
                            // 图片有原图标记,但原图模式未启用 -> 自动启用
                            //System.Diagnostics.Debug.WriteLine($"🎯 自动启用原图模式: 图片ID={_currentImageId}");
                            _originalMode = true;
                            _imageProcessor.OriginalMode = true;
                            
                            // 更新按钮样式
                            BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                            
                            ShowStatus("✅ 已自动启用原图模式");
                        }
                        else if (!shouldUseOriginal && _originalMode)
                        {
                            // 图片没有原图标记,但原图模式已启用 -> 保持原图模式(不自动关闭)
                            // 用户可能在浏览一组原图,中途打开了非原图,应该保持原图模式
                            //System.Diagnostics.Debug.WriteLine($"ℹ️ 保持原图模式: 图片ID={_currentImageId}");
                        }
                        
                        // 🔧 关键修复：如果原图模式已启用，无论是否自动启用，都需要查找相似图片
                        // 这样切换到新歌曲时，相似图片列表会更新为新歌曲的图片
                        if (_originalMode)
                        {
                            var findStart = sw.ElapsedMilliseconds;
                            _originalManager.FindSimilarImages(_currentImageId);
                            var findTime = sw.ElapsedMilliseconds - findStart;
                            System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 查找相似图片: {findTime}ms");
                            
                            // ⚡ 立即触发智能预缓存（不等待用户操作）
                            // 这样第一次切换时预缓存已经完成或接近完成
                            _ = TriggerSmartPreload();
                        }
                        
                        // 🌲 同步项目树选中状态
                        var treeStart = sw.ElapsedMilliseconds;
                        SelectTreeItemById(_currentImageId);
                        var treeTime = sw.ElapsedMilliseconds - treeStart;
                        System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 同步项目树: {treeTime}ms");
                    }
                    
                    // 颜色效果由 ImageProcessor 内部处理
                    
                    // 更新投影
                    var projStart = sw.ElapsedMilliseconds;
                    UpdateProjection();
                    var projTime = sw.ElapsedMilliseconds - projStart;
                    System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 更新投影: {projTime}ms");
                    
                    // 更新关键帧预览线和指示块
                    var kfStart = sw.ElapsedMilliseconds;
                    _keyframeManager?.UpdatePreviewLines();
                    var kfTime = sw.ElapsedMilliseconds - kfStart;
                    System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 更新关键帧预览: {kfTime}ms");
                    
                    // 🔧 更新 PlaybackViewModel 状态（检查时间数据，更新脚本按钮颜色）
                    if (_playbackViewModel != null && _currentImageId > 0)
                    {
                        _ = _playbackViewModel.SetCurrentImageAsync(_currentImageId, 
                            _originalMode ? Database.Models.Enums.PlaybackMode.Original : Database.Models.Enums.PlaybackMode.Keyframe);
                    }
                    
                    sw.Stop();
                    System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== LoadImage 总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    ShowStatus($"✅ 已加载：{Path.GetFileName(path)}");
                }
                else
                {
                    throw new Exception("图片加载失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开图片: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStatus("❌ 加载失败");
            }
        }

        /// <summary>
        /// 清空图片显示
        /// </summary>
        private void ClearImageDisplay()
        {
            try
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("\n🗑️ ========== ClearImageDisplay 被调用 ==========");
                System.Diagnostics.Debug.WriteLine($"   清空前 _imagePath: {_imagePath ?? "null"}");
                System.Diagnostics.Debug.WriteLine($"   清空前 _currentImageId: {_currentImageId}");
#endif
                
                // 清空图片路径
                _imagePath = null;
                _currentImageId = 0;
#if DEBUG
                System.Diagnostics.Debug.WriteLine("   步骤1: _imagePath 和 _currentImageId 已清空");
#endif
                
                // 清空ImageProcessor（内部管理图片资源）
#if DEBUG
                System.Diagnostics.Debug.WriteLine("   步骤2: 调用 _imageProcessor.ClearCurrentImage()");
#endif
                _imageProcessor.ClearCurrentImage();
#if DEBUG
                System.Diagnostics.Debug.WriteLine("   步骤2: _imageProcessor.ClearCurrentImage() 完成");
#endif
                
                // 重置缩放
                _currentZoom = 1.0;
#if DEBUG
                System.Diagnostics.Debug.WriteLine("   步骤3: _currentZoom 重置为 1.0");
#endif
                
                ShowStatus("✅ 已清空图片显示");
#if DEBUG
                System.Diagnostics.Debug.WriteLine("🎯 已清空图片显示");
                System.Diagnostics.Debug.WriteLine("========== ClearImageDisplay 完成 ==========\n");
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 清空图片显示失败: {ex.Message}");
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
#endif
            }
        }

        /// <summary>
        /// 在项目树中选中指定ID的节点
        /// </summary>
        private void SelectTreeItemById(int itemId)
        {
            try
            {
                // 递归查找并选中节点
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                SelectTreeItemRecursive(treeItems, itemId);
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"选中项目树节点失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 递归查找并选中树节点
        /// </summary>
        private bool SelectTreeItemRecursive(IEnumerable<ProjectTreeItem> items, int targetId)
        {
            foreach (var item in items)
            {
                if (item.Id == targetId && item.Type == TreeItemType.File)
                {
                    // 找到目标节点,设置为选中状态
                    item.IsSelected = true;
                    
                    // 确保父节点展开
                    ExpandParentNodes(item);
                    
                    // System.Diagnostics.Debug.WriteLine($"✅ 已选中项目树节点: {item.Name}");
                    return true;
                }
                
                // 递归查找子节点
                if (item.Children != null && item.Children.Count > 0)
                {
                    if (SelectTreeItemRecursive(item.Children, targetId))
                    {
                        // 如果在子节点中找到,展开当前节点
                        item.IsExpanded = true;
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// 展开父节点
        /// </summary>
        private void ExpandParentNodes(ProjectTreeItem item)
        {
            // 在WPF TreeView中,需要通过递归查找父节点
            // 这里简化处理:直接展开所有节点路径
            var allItems = ProjectTree.Items.Cast<ProjectTreeItem>();
            ExpandParentNodesRecursive(allItems, item);
        }

        /// <summary>
        /// 递归展开父节点
        /// </summary>
        private bool ExpandParentNodesRecursive(IEnumerable<ProjectTreeItem> items, ProjectTreeItem target)
        {
            foreach (var item in items)
            {
                if (item == target)
                {
                    return true;
                }
                
                if (item.Children != null && item.Children.Count > 0)
                {
                    if (ExpandParentNodesRecursive(item.Children, target))
                    {
                        item.IsExpanded = true;
                        return true;
                    }
                }
            }
            
            return false;
        }

        

        // 颜色效果相关方法已移至 MainWindow.Color.cs

        #endregion

        // 图片缩放和拖动功能已移至 MainWindow.Zoom.cs

        // 媒体播放器事件已移至 MainWindow.Media.cs

        #region 辅助方法

        private void ResetView()
        {
            ResetZoom();
            ShowStatus("✅ 视图已重置");
        }

        public void ShowStatus(string message)
        {
            // 保持固定标题，不显示状态信息
            // Title = $"Canvas Cast V2.5.5 - {message}";
            
            // 可以在这里输出到调试控制台（可选）
            //System.Diagnostics.Debug.WriteLine($"状态: {message}");
        }

        public SKColor GetCurrentTargetColor()
        {
            return _currentTargetColor;
        }

        protected override void OnClosed(EventArgs e)
        {
            _imageProcessor?.Dispose();
            base.OnClosed(e);
        }

        #endregion

        #region 右键菜单

        /// <summary>
        /// 导航栏分隔条拖动完成事件 - 保存宽度
        /// </summary>
        private void NavigationSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (NavigationPanelColumn != null)
            {
                double newWidth = NavigationPanelColumn.ActualWidth;
                _configManager.NavigationPanelWidth = newWidth;
                // System.Diagnostics.Debug.WriteLine($"✅ 导航栏宽度已保存: {newWidth}");
            }
        }

        private void ImageScrollViewer_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (_imageProcessor.CurrentImage == null)
                return;

            // 使用XAML中定义的ContextMenu
            var contextMenu = ImageScrollViewer.ContextMenu;
            if (contextMenu == null)
            {
                contextMenu = new ContextMenu();
                ImageScrollViewer.ContextMenu = contextMenu;
            }
            
            contextMenu.FontSize = 14;
            
            // 清除除了"滚动速度"和"滚动函数"之外的所有菜单项
            var scrollSpeedMenu = contextMenu.Items.Cast<object>()
                .FirstOrDefault(item => item is MenuItem mi && mi.Header.ToString() == "滚动速度");
            var scrollEasingMenu = contextMenu.Items.Cast<object>()
                .FirstOrDefault(item => item is MenuItem mi && mi.Header.ToString() == "滚动函数");
            
            contextMenu.Items.Clear();
            
            // 重新添加滚动速度和滚动函数菜单
            if (scrollSpeedMenu != null)
            {
                contextMenu.Items.Add(scrollSpeedMenu);
                // 更新滚动速度菜单的选中状态
                if (_keyframeManager != null)
                {
                    foreach (var item in ((MenuItem)scrollSpeedMenu).Items)
                    {
                        if (item is MenuItem subMenu && subMenu.Tag != null)
                        {
                            if (double.TryParse(subMenu.Tag.ToString(), out double speed))
                            {
                                subMenu.IsChecked = Math.Abs(speed - _keyframeManager.ScrollDuration) < 0.01;
                            }
                        }
                    }
                }
            }
            if (scrollEasingMenu != null)
            {
                contextMenu.Items.Add(scrollEasingMenu);
                // 更新滚动函数菜单的选中状态
                if (_keyframeManager != null)
                {
                    foreach (var item in ((MenuItem)scrollEasingMenu).Items)
                    {
                        if (item is MenuItem subMenu && subMenu.Tag != null)
                        {
                            string tag = subMenu.Tag.ToString();
                            if (tag == "Linear")
                            {
                                subMenu.IsChecked = _keyframeManager.IsLinearScrolling;
                            }
                            else
                            {
                                subMenu.IsChecked = !_keyframeManager.IsLinearScrolling && 
                                                    tag == _keyframeManager.ScrollEasingType;
                            }
                        }
                    }
                }
            }
            if (scrollSpeedMenu != null || scrollEasingMenu != null)
            {
                contextMenu.Items.Add(new Separator());
            }

            // 变色颜色子菜单
            var colorMenuItem = new MenuItem { Header = "变色颜色" };

            // 从 ConfigManager 获取所有颜色预设
            var allPresets = _configManager.GetAllColorPresets();
            
            foreach (var preset in allPresets)
            {
                var menuItem = new MenuItem 
                { 
                    Header = preset.Name,
                    IsCheckable = true,
                    IsChecked = _currentTargetColor.Red == preset.R && 
                               _currentTargetColor.Green == preset.G && 
                               _currentTargetColor.Blue == preset.B
                };
                
                // 捕获当前预设到局部变量
                var currentPreset = preset;
                
                menuItem.Click += (s, args) =>
                {
                    _currentTargetColor = currentPreset.ToSKColor();
                    _currentTargetColorName = currentPreset.Name; // 保存颜色名称
                    if (_isColorEffectEnabled)
                    {
                        // 如果颜色效果已启用，清除缓存并更新显示
                        _imageProcessor.ClearCache();
                        _imageProcessor.UpdateImage();
                    }
                    // 保存颜色设置
                    SaveSettings();
                    ShowStatus($"✨ 已切换颜色: {currentPreset.Name}");
                };
                colorMenuItem.Items.Add(menuItem);
            }

            // 添加分隔线
            colorMenuItem.Items.Add(new Separator());

            // 自定义颜色
            var customColorItem = new MenuItem { Header = "自定义颜色..." };
            customColorItem.Click += (s, args) => OpenColorPicker();
            colorMenuItem.Items.Add(customColorItem);
            
            // 保存当前颜色为预设
            if (_currentTargetColorName == "自定义")
            {
                var savePresetItem = new MenuItem { Header = "保存当前颜色为预设..." };
                savePresetItem.Click += (s, args) => SaveCurrentColorAsPreset();
                colorMenuItem.Items.Add(savePresetItem);
            }

            contextMenu.Items.Add(colorMenuItem);

            // 原图模式显示切换菜单(仅在原图模式下显示)
            if (_originalMode)
            {
                contextMenu.Items.Add(new Separator());
                
                var displayModeMenuItem = new MenuItem { Header = "原图模式" };
                
                // 拉伸模式
                var stretchItem = new MenuItem 
                { 
                    Header = "拉伸", 
                    IsCheckable = true,
                    IsChecked = _originalDisplayMode == OriginalDisplayMode.Stretch
                };
                stretchItem.Click += (s, args) =>
                {
                    if (_originalDisplayMode != OriginalDisplayMode.Stretch)
                    {
                        _originalDisplayMode = OriginalDisplayMode.Stretch;
                        _imageProcessor.OriginalDisplayModeValue = _originalDisplayMode;
                        _imageProcessor.UpdateImage();
                        UpdateProjection();
                        ShowStatus("✅ 原图模式: 拉伸显示");
                    }
                };
                displayModeMenuItem.Items.Add(stretchItem);
                
                // 适中模式
                var fitItem = new MenuItem 
                { 
                    Header = "适中", 
                    IsCheckable = true,
                    IsChecked = _originalDisplayMode == OriginalDisplayMode.Fit
                };
                fitItem.Click += (s, args) =>
                {
                    if (_originalDisplayMode != OriginalDisplayMode.Fit)
                    {
                        _originalDisplayMode = OriginalDisplayMode.Fit;
                        _imageProcessor.OriginalDisplayModeValue = _originalDisplayMode;
                        _imageProcessor.UpdateImage();
                        UpdateProjection();
                        ShowStatus("✅ 原图模式: 适中显示");
                    }
                };
                displayModeMenuItem.Items.Add(fitItem);
                
                contextMenu.Items.Add(displayModeMenuItem);
            }

            // 显示菜单
            contextMenu.IsOpen = true;
        }

        #endregion

        #region 窗口事件处理

        /// <summary>
        /// 窗口关闭事件 - 清理资源
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine("🔚 主窗口正在关闭,清理资源...");
                
                // 保存用户设置
                SaveSettings();
                
                // 取消订阅事件，防止内存泄漏
                if (_videoPlayerManager != null)
                {
                    _videoPlayerManager.VideoTrackDetected -= VideoPlayerManager_VideoTrackDetected;
                    _videoPlayerManager.PlayStateChanged -= OnVideoPlayStateChanged;
                    _videoPlayerManager.MediaChanged -= OnVideoMediaChanged;
                    _videoPlayerManager.MediaEnded -= OnVideoMediaEnded;
                    _videoPlayerManager.ProgressUpdated -= OnVideoProgressUpdated;
                }
                
                // 注意：PropertyChanged事件使用匿名方法订阅，无法直接取消订阅
                // ViewModel会随窗口关闭自动释放
                // 如果需要，应在订阅时保存匿名方法引用以便取消订阅
                
                // 停止并清理视频播放器
                if (_videoPlayerManager != null)
                {
                    _videoPlayerManager.Stop();
                    _videoPlayerManager.Dispose();
                }
                
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
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 资源清理失败: {ex.Message}");
            }
        }

        #endregion

        #region 键盘事件处理

        /// <summary>
        /// 主窗口键盘事件处理
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 🆕 文本编辑器模式：PageUp/PageDown 用于切换幻灯片
            if (TextEditorPanel.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.PageUp || e.Key == Key.PageDown)
                {
                    // 让 TextEditorPanel 的 PreviewKeyDown 事件处理
                    // 这里不做处理，直接返回
                    return;
                }
            }

            // ESC键: 关闭投影(优先级最高,不论是否原图模式)
            if (e.Key == Key.Escape)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("\n⌨️ ========== 主窗口热键: ESC ==========");
                System.Diagnostics.Debug.WriteLine($"   触发时间: {DateTime.Now:HH:mm:ss:fff}");
                System.Diagnostics.Debug.WriteLine($"   _videoPlayerManager != null: {_videoPlayerManager != null}");
                System.Diagnostics.Debug.WriteLine($"   _videoPlayerManager.IsPlaying: {_videoPlayerManager?.IsPlaying}");
                System.Diagnostics.Debug.WriteLine($"   _projectionManager != null: {_projectionManager != null}");
                System.Diagnostics.Debug.WriteLine($"   _projectionManager.IsProjectionActive: {_projectionManager?.IsProjectionActive}");
#endif
                
                bool handled = false;
                
                // 优先关闭投影（CloseProjection现在只在有投影时返回true）
                if (_projectionManager != null)
                {
                    bool wasClosed = _projectionManager.CloseProjection();
                    if (wasClosed)
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 已关闭投影");
#endif
                        handled = true;
                    }
#if DEBUG
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 无投影需要关闭");
                    }
#endif
                }
                
                // 如果没有投影需要关闭，且正在播放视频，则停止播放并重置界面
                if (!handled && _videoPlayerManager != null && _videoPlayerManager.IsPlaying)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 检测到视频播放，调用 SwitchToImageMode()");
#endif
                    SwitchToImageMode();
                    handled = true;
                }
#if DEBUG
                else if (!handled)
                {
                    System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 无需处理视频停止");
                }
#endif
                
                if (handled)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 事件已处理");
#endif
                    e.Handled = true;
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("========== 主窗口热键 ESC 处理完成 ==========\n");
#endif
                    return;
                }
#if DEBUG
                else
                {
                    System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 事件未处理");
                    System.Diagnostics.Debug.WriteLine("========== 主窗口热键 ESC 处理完成 ==========\n");
                }
#endif
            }
            
            // 在投影模式下，让全局热键处理这些按键，前台不处理
            if (_projectionManager != null && _projectionManager.IsProjectionActive)
            {
                // 检查是否是全局热键相关的按键
                bool isGlobalHotKey = (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.F2 || 
                                     e.Key == Key.PageUp || e.Key == Key.PageDown || e.Key == Key.Escape);
                
                if (isGlobalHotKey)
                {
                    // 在投影模式下，让全局热键处理这些按键
                    //System.Diagnostics.Debug.WriteLine($"⌨️ 投影模式下，让全局热键处理: {e.Key}");
                    return; // 不处理，让全局热键处理
                }
            }
            
            // 视频播放控制快捷键
            if (_videoPlayerManager != null && _videoPlayerManager.IsPlaying)
            {
                bool handled = false;
                
                switch (e.Key)
                {
                    case Key.F2:
                        // F2键：播放/暂停
                        if (_videoPlayerManager.IsPaused)
                        {
                            _videoPlayerManager.Play();
                            //System.Diagnostics.Debug.WriteLine("⌨️ F2键: 继续播放");
                        }
                        else
                        {
                            _videoPlayerManager.Pause();
                            //System.Diagnostics.Debug.WriteLine("⌨️ F2键: 暂停播放");
                        }
                        handled = true;
                        break;
                        
                    case Key.Left:
                        // 左方向键：上一首
                        _videoPlayerManager.PlayPrevious();
                        //System.Diagnostics.Debug.WriteLine("⌨️ 左方向键: 上一首");
                        handled = true;
                        break;
                        
                    case Key.Right:
                        // 右方向键：下一首
                        _videoPlayerManager.PlayNext();
                        //System.Diagnostics.Debug.WriteLine("⌨️ 右方向键: 下一首");
                        handled = true;
                        break;
                }
                
                if (handled)
                {
                    e.Handled = true;
                    return;
                }
            }
            
            // 原图模式下的相似图片切换
            if (_originalMode && _currentImageId > 0)
            {
                bool handled = false;
                
                switch (e.Key)
                {
                    case Key.PageUp:
                        // 切换到上一张相似图片
                        handled = SwitchSimilarImage(false);
                        break;
                        
                    case Key.PageDown:
                        // 切换到下一张相似图片
                        handled = SwitchSimilarImage(true);
                        break;
                }
                
                if (handled)
                {
                    e.Handled = true;
                }
            }
            // 关键帧模式下的关键帧切换
            else if (!_originalMode && _currentImageId > 0)
            {
                bool handled = false;
                
                switch (e.Key)
                {
                    case Key.PageUp:
                        // 上一个关键帧
                        BtnPrevKeyframe_Click(null, null);
                        //System.Diagnostics.Debug.WriteLine("⌨️ PageUp: 上一个关键帧");
                        handled = true;
                        break;
                        
                    case Key.PageDown:
                        // 下一个关键帧
                        BtnNextKeyframe_Click(null, null);
                        //System.Diagnostics.Debug.WriteLine("⌨️ PageDown: 下一个关键帧");
                        handled = true;
                        break;
                }
                
                if (handled)
                {
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 切换相似图片
        /// </summary>
        private bool SwitchSimilarImage(bool isNext)
        {
            // ⏱️ 性能调试：测量原图切换总耗时
            var sw = System.Diagnostics.Stopwatch.StartNew();
            //System.Diagnostics.Debug.WriteLine($"");
            //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 开始切换相似图片 (方向: {(isNext ? "下一张" : "上一张")}) ==========");
            
            var switchStart = sw.ElapsedMilliseconds;
            var result = _originalManager.SwitchSimilarImage(isNext, _currentImageId);
            var switchTime = sw.ElapsedMilliseconds - switchStart;
            //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] OriginalManager.SwitchSimilarImage: {switchTime}ms");
            
            if (result.success && result.newImageId.HasValue)
            {
                int fromImageId = _currentImageId;  // 保存切换前的ID
                int toImageId = result.newImageId.Value;
                
                _currentImageId = toImageId;
                
                var loadStart = sw.ElapsedMilliseconds;
                LoadImage(result.newImagePath);
                var loadTotalTime = sw.ElapsedMilliseconds - loadStart;
                // LoadImage内部已有详细分解，这里只记录进入时间
                //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] LoadImage调用（含所有子步骤）: {loadTotalTime}ms");
                
                // 🎯 触发智能预缓存（异步执行，不阻塞）
                _ = TriggerSmartPreload();
                
                // 🎯 如果正在录制原图模式，记录切换时间（异步执行，不阻塞）
                _ = OnSimilarImageSwitched(fromImageId, toImageId, result.isLoopCompleted);
                
                sw.Stop();
                string direction = isNext ? "下一张" : "上一张";
                //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 相似图片切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                //System.Diagnostics.Debug.WriteLine($"");
                ShowStatus($"✅ 已切换到{direction}相似图片: {Path.GetFileName(result.newImagePath)}");
                return true;
            }
            
            sw.Stop();
            return false;
        }

        /// <summary>
        /// 智能预缓存：根据当前模式自动触发精准预缓存
        /// </summary>
        private async System.Threading.Tasks.Task TriggerSmartPreload()
        {
            try
            {
                if (_preloadCacheManager == null || _currentImageId <= 0)
                    return;
                
                // 获取当前文件信息
                var currentFile = _dbManager.GetMediaFileById(_currentImageId);
                if (currentFile == null)
                    return;
                
                // 判断是否处于原图模式
                if (_originalMode)
                {
                    // 原图模式：判断是循环模式还是顺序模式
                    var markType = _originalManager.GetOriginalMarkType(ItemType.Image, _currentImageId);
                    
                    // 如果图片本身没有标记，检查文件夹标记
                    if (markType == null && currentFile.FolderId.HasValue)
                    {
                        markType = _originalManager.GetOriginalMarkType(ItemType.Folder, currentFile.FolderId.Value);
                    }
                    
                    if (markType == MarkType.Loop)
                    {
                        // 🔄 循环模式：预缓存相似图片
                        //System.Diagnostics.Debug.WriteLine("📦 [智能预缓存] 触发：原图循环模式");
                        
                        // 确保已查找相似图片
                        if (!_originalManager.HasSimilarImages())
                        {
                            _originalManager.FindSimilarImages(_currentImageId);
                        }
                        
                        // 获取相似图片列表
                        var similarImages = GetSimilarImagesFromOriginalManager();
                        await _preloadCacheManager.PreloadForLoopModeAsync(_currentImageId, similarImages);
                    }
                    else if (markType == MarkType.Sequence)
                    {
                        // ➡️ 顺序模式：预缓存后续10张图
                        //System.Diagnostics.Debug.WriteLine("📦 [智能预缓存] 触发：原图顺序模式");
                        
                        if (currentFile.FolderId.HasValue)
                        {
                            await _preloadCacheManager.PreloadForSequenceModeAsync(_currentImageId, currentFile.FolderId.Value);
                        }
                    }
                }
                else
                {
                    // 关键帧模式：当前图片已加载，无需额外预缓存
                    await _preloadCacheManager.PreloadForKeyframeModeAsync(_currentImageId);
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ [智能预缓存] 失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 从OriginalManager获取相似图片列表
        /// </summary>
        private List<(int id, string name, string path)> GetSimilarImagesFromOriginalManager()
        {
            try
            {
                return _originalManager.GetSimilarImages();
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"⚠️ 获取相似图片列表失败: {ex.Message}");
                return new List<(int id, string name, string path)>();
            }
        }

        /// <summary>
        /// 切换到下一张相似图片 (公共方法,供投影窗口调用)
        /// </summary>
        public void SwitchToNextSimilarImage()
        {
            // 如果当前在原图模式下,确保已查找相似图片
            if (_originalMode && _currentImageId > 0)
            {
                // 检查是否需要重新查找相似图片
                if (!_originalManager.HasSimilarImages())
                {
                    //System.Diagnostics.Debug.WriteLine("⚠️ 相似图片列表为空,重新查找...");
                    _originalManager.FindSimilarImages(_currentImageId);
                }
            }
            
            SwitchSimilarImage(true);
        }

        /// <summary>
        /// 切换到上一张相似图片 (公共方法,供投影窗口调用)
        /// </summary>
        public void SwitchToPreviousSimilarImage()
        {
            // 如果当前在原图模式下,确保已查找相似图片
            if (_originalMode && _currentImageId > 0)
            {
                // 检查是否需要重新查找相似图片
                if (!_originalManager.HasSimilarImages())
                {
                    //System.Diagnostics.Debug.WriteLine("⚠️ 相似图片列表为空,重新查找...");
                    _originalManager.FindSimilarImages(_currentImageId);
                }
            }
            
            SwitchSimilarImage(false);
        }

        #endregion

        #region 拖拽事件处理

        /// <summary>
        /// 鼠标按下事件 - 记录拖拽起始点
        /// </summary>
        private void ProjectTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            
            // 获取点击的TreeViewItem
            var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (treeViewItem != null)
            {
                _draggedItem = treeViewItem.DataContext as ProjectTreeItem;
            }
        }

        /// <summary>
        /// 鼠标移动事件 - 开始拖拽
        /// </summary>
        private void ProjectTree_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null)
            {
                System.Windows.Point currentPosition = e.GetPosition(null);
                System.Windows.Vector diff = _dragStartPoint - currentPosition;

                // 检查是否移动了足够的距离
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // 允许拖拽文件和文件夹（不允许拖拽Project）
                    if (_draggedItem.Type == TreeItemType.File || _draggedItem.Type == TreeItemType.Folder)
                    {
                        System.Windows.DragDrop.DoDragDrop(ProjectTree, _draggedItem, System.Windows.DragDropEffects.Move);
                    }
                    
                    _draggedItem = null;
                }
            }
        }

        /// <summary>
        /// 拖拽悬停事件 - 显示拖拽效果
        /// </summary>
        private void ProjectTree_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(ProjectTreeItem)))
            {
                // 获取当前悬停的TreeViewItem
                var targetTreeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                if (targetTreeViewItem != null)
                {
                    var targetItem = targetTreeViewItem.DataContext as ProjectTreeItem;
                    
                    _dragOverItem = targetItem;
                    
                    // 获取拖拽源项
                    var sourceItem = e.Data.GetData(typeof(ProjectTreeItem)) as ProjectTreeItem;
                    
                    // 检查是否是有效的拖放目标
                    // 文件只能拖到文件上，文件夹只能拖到文件夹上
                    bool isValidDrop = false;
                    if (sourceItem != null && targetItem != null)
                    {
                        if (sourceItem.Type == TreeItemType.File && targetItem.Type == TreeItemType.File)
                        {
                            isValidDrop = true;
                        }
                        else if (sourceItem.Type == TreeItemType.Folder && targetItem.Type == TreeItemType.Folder)
                        {
                            isValidDrop = true;
                        }
                    }
                    
                    if (isValidDrop)
                    {
                        e.Effects = System.Windows.DragDropEffects.Move;
                        
                        // 显示拖拽插入位置指示器（蓝色横线）
                        ShowDragIndicator(targetTreeViewItem);
                    }
                    else
                    {
                        e.Effects = System.Windows.DragDropEffects.None;
                        HideDragIndicator();
                    }
                }
                else
                {
                    e.Effects = System.Windows.DragDropEffects.None;
                    HideDragIndicator();
                }
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
                HideDragIndicator();
            }
            e.Handled = true;
        }

        /// <summary>
        /// 拖拽离开事件 - 清除高亮
        /// </summary>
        private void ProjectTree_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            ClearDragHighlight();
        }

        /// <summary>
        /// 放置事件 - 执行拖拽排序
        /// </summary>
        private void ProjectTree_Drop(object sender, System.Windows.DragEventArgs e)
        {
            // 清除拖拽高亮
            ClearDragHighlight();
            
            if (e.Data.GetDataPresent(typeof(ProjectTreeItem)))
            {
                var sourceItem = e.Data.GetData(typeof(ProjectTreeItem)) as ProjectTreeItem;
                
                // 获取目标TreeViewItem
                var targetTreeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                if (targetTreeViewItem != null)
                {
                    var targetItem = targetTreeViewItem.DataContext as ProjectTreeItem;
                    
                    if (sourceItem != null && targetItem != null && sourceItem != targetItem)
                    {
                        // 文件排序：只允许在同一文件夹内拖拽排序
                        if (sourceItem.Type == TreeItemType.File && targetItem.Type == TreeItemType.File)
                        {
                            ReorderFiles(sourceItem, targetItem);
                        }
                        // 文件夹排序：只允许根级别文件夹之间排序
                        else if (sourceItem.Type == TreeItemType.Folder && targetItem.Type == TreeItemType.Folder)
                        {
                            ReorderFolders(sourceItem, targetItem);
                        }
                    }
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// TreeViewItem鼠标进入事件 - 显示完整文件名提示
        /// </summary>
        private void TreeViewItem_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (sender is TreeViewItem treeViewItem && treeViewItem.DataContext is ProjectTreeItem item)
                {
                    // 获取显示文本（文件名或文件夹名）
                    string displayText = item.Name;
                    
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        // 设置提示框文本
                        FileNameTooltipText.Text = displayText;
                        
                        // 显示提示框
                        FileNameTooltipPopup.IsOpen = true;
                    }
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"显示文件名提示时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// TreeViewItem鼠标离开事件 - 隐藏提示框
        /// </summary>
        private void TreeViewItem_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                // 隐藏提示框
                FileNameTooltipPopup.IsOpen = false;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"隐藏文件名提示时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// TreeView鼠标移动事件 - 更新提示框位置跟随鼠标
        /// </summary>
        private void ProjectTree_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (FileNameTooltipPopup.IsOpen)
                {
                    // 获取鼠标相对于ProjectTree的位置
                    System.Windows.Point mousePos = e.GetPosition(ProjectTree);
                    
                    // 设置提示框位置（鼠标右下方偏移一点）
                    FileNameTooltipPopup.HorizontalOffset = mousePos.X + 15;
                    FileNameTooltipPopup.VerticalOffset = mousePos.Y + 15;
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"更新提示框位置时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示拖拽插入位置指示器
        /// </summary>
        private void ShowDragIndicator(TreeViewItem targetItem)
        {
            try
            {
                if (DragIndicatorLine == null || targetItem == null) return;

                // 获取TreeViewItem相对于ProjectTree的位置
                var position = targetItem.TranslatePoint(new System.Windows.Point(0, 0), ProjectTree);
                
                // 获取目标项的数据
                var targetData = targetItem.DataContext as ProjectTreeItem;
                if (targetData == null) return;
                
                // 精确计算文件名的起始位置
                // TreeView缩进 + 图标宽度 + 图标右边距 = 文件名起始位置
                double treeViewIndent = targetData.Type == TreeItemType.File ? 19 : 0; // 文件的TreeView缩进
                double iconWidth = 18; // PackIcon宽度
                double iconMargin = 8; // PackIcon右边距
                double textStartPosition = treeViewIndent + iconWidth + iconMargin; // 文件名实际开始位置
                
                // 根据文件名长度智能调整横线长度
                double lineLength;
                if (!string.IsNullOrEmpty(targetData.Name))
                {
                    // 基于文件名长度估算宽度（每个字符约7px，中文字符约12px）
                    double estimatedWidth = 0;
                    foreach (char c in targetData.Name)
                    {
                        estimatedWidth += (c > 127) ? 12 : 7; // 中文字符宽度更大
                    }
                    lineLength = Math.Min(estimatedWidth + 10, 160); // 最大160px，加10px缓冲
                    lineLength = Math.Max(lineLength, 60); // 最小60px
                }
                else
                {
                    lineLength = 80; // 默认长度
                }
                
                // 设置指示线的位置和长度
                Canvas.SetTop(DragIndicatorLine, position.Y);
                DragIndicatorLine.X1 = textStartPosition;
                DragIndicatorLine.X2 = textStartPosition + lineLength;
                DragIndicatorLine.Y1 = 0;
                DragIndicatorLine.Y2 = 0;
                
                // 显示指示线
                DragIndicatorLine.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"显示拖拽指示器失败: {ex}");
            }
        }

        /// <summary>
        /// 隐藏拖拽插入位置指示器
        /// </summary>
        private void HideDragIndicator()
        {
            try
            {
                if (DragIndicatorLine != null)
                {
                    DragIndicatorLine.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"隐藏拖拽指示器失败: {ex}");
            }
        }

        /// <summary>
        /// 清除拖拽高亮效果
        /// </summary>
        private void ClearDragHighlight()
        {
            _dragOverItem = null;
            HideDragIndicator();
        }

        /// <summary>
        /// 递归清除TreeView中所有项的边框
        /// </summary>
        private void ClearTreeViewItemBorders(ItemsControl itemsControl)
        {
            if (itemsControl == null) return;

            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                var item = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (item != null)
                {
                    item.BorderThickness = new Thickness(0);
                    item.BorderBrush = null;
                    
                    // 递归处理子项
                    if (item.HasItems)
                    {
                        ClearTreeViewItemBorders(item);
                    }
                }
            }
        }

        /// <summary>
        /// 查找指定类型的父元素
        /// </summary>
        private T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        /// <summary>
        /// 重新排序文件
        /// </summary>
        private void ReorderFiles(ProjectTreeItem sourceItem, ProjectTreeItem targetItem)
        {
            // 防止重复执行
            if (_isDragInProgress) return;
            _isDragInProgress = true;
            
            try
            {
                // 获取源文件和目标文件所属的文件夹
                int? sourceFolderId = GetFileFolderId(sourceItem);
                int? targetFolderId = GetFileFolderId(targetItem);

                // 只允许在同一文件夹内排序
                if (sourceFolderId != targetFolderId)
                {
                    ShowStatus("❌ 只能在同一文件夹内拖拽排序");
                    return;
                }

                // 如果有文件夹ID，标记为手动排序
                if (sourceFolderId.HasValue)
                {
                    _dbManager.MarkFolderAsManualSort(sourceFolderId.Value);
                }

                // 获取文件夹中的所有文件
                var files = sourceFolderId.HasValue 
                    ? _dbManager.GetMediaFilesByFolder(sourceFolderId.Value)
                    : _dbManager.GetRootMediaFiles();

                // 找到源文件和目标文件的索引
                int sourceIndex = files.FindIndex(f => f.Id == sourceItem.Id);
                int targetIndex = files.FindIndex(f => f.Id == targetItem.Id);

                if (sourceIndex == -1 || targetIndex == -1)
                {
                    ShowStatus("❌ 无法找到文件");
                    return;
                }

                // 移除源文件
                var sourceFile = files[sourceIndex];
                files.RemoveAt(sourceIndex);

                // 插入到目标位置
                if (sourceIndex < targetIndex)
                {
                    files.Insert(targetIndex, sourceFile);
                }
                else
                {
                    files.Insert(targetIndex, sourceFile);
                }

                // 更新所有文件的OrderIndex
                for (int i = 0; i < files.Count; i++)
                {
                    files[i].OrderIndex = i + 1;
                }

                // 保存更改
                _dbManager.UpdateMediaFilesOrder(files);

                // 🔑 关键修复：直接在内存中更新顺序，避免重新加载整个TreeView
                UpdateTreeItemOrder(sourceFolderId, files);
                
                ShowStatus($"✅ 已重新排序: {sourceItem.Name}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"重新排序失败: {ex}");
                ShowStatus($"❌ 排序失败: {ex.Message}");
            }
            finally
            {
                // 确保标志被重置
                _isDragInProgress = false;
            }
        }

        /// <summary>
        /// 重新排序文件夹（拖拽）
        /// </summary>
        private void ReorderFolders(ProjectTreeItem sourceItem, ProjectTreeItem targetItem)
        {
            // 防止重复执行
            if (_isDragInProgress) return;
            _isDragInProgress = true;
            
            try
            {
                // 获取所有文件夹
                var folders = _dbManager.GetAllFolders();
                
                // 找到源文件夹和目标文件夹的索引
                int sourceIndex = folders.FindIndex(f => f.Id == sourceItem.Id);
                int targetIndex = folders.FindIndex(f => f.Id == targetItem.Id);

                if (sourceIndex == -1 || targetIndex == -1)
                {
                    ShowStatus("❌ 无法找到文件夹");
                    return;
                }

                // 移除源文件夹
                var sourceFolder = folders[sourceIndex];
                folders.RemoveAt(sourceIndex);

                // 插入到目标位置
                if (sourceIndex < targetIndex)
                {
                    folders.Insert(targetIndex, sourceFolder);
                }
                else
                {
                    folders.Insert(targetIndex, sourceFolder);
                }

                // 更新所有文件夹的OrderIndex
                for (int i = 0; i < folders.Count; i++)
                {
                    folders[i].OrderIndex = i + 1;
                }

                // 保存更改到数据库
                _dbManager.UpdateFoldersOrder(folders);

                // 更新TreeView中的文件夹顺序
                UpdateFolderTreeItemOrder(folders);
                
                ShowStatus($"✅ 已重新排序文件夹: {sourceItem.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"重新排序文件夹失败: {ex}");
                ShowStatus($"❌ 文件夹排序失败: {ex.Message}");
            }
            finally
            {
                // 确保标志被重置
                _isDragInProgress = false;
            }
        }

        /// <summary>
        /// 文件夹上移
        /// </summary>
        private void MoveFolderUp(ProjectTreeItem folderItem)
        {
            try
            {
                // 获取所有文件夹
                var folders = _dbManager.GetAllFolders();
                
                // 找到当前文件夹的索引
                int currentIndex = folders.FindIndex(f => f.Id == folderItem.Id);
                
                if (currentIndex == -1)
                {
                    ShowStatus("❌ 无法找到文件夹");
                    return;
                }
                
                // 如果已经是第一个，无法上移
                if (currentIndex == 0)
                {
                    ShowStatus("⚠️ 已经是第一个文件夹");
                    return;
                }
                
                // 与上一个文件夹交换位置
                var currentFolder = folders[currentIndex];
                folders.RemoveAt(currentIndex);
                folders.Insert(currentIndex - 1, currentFolder);
                
                // 更新所有文件夹的OrderIndex
                for (int i = 0; i < folders.Count; i++)
                {
                    folders[i].OrderIndex = i + 1;
                }
                
                // 保存到数据库
                _dbManager.UpdateFoldersOrder(folders);
                
                // 更新UI
                UpdateFolderTreeItemOrder(folders);
                
                ShowStatus($"✅ 已上移: {folderItem.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"文件夹上移失败: {ex}");
                ShowStatus($"❌ 上移失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 文件夹下移
        /// </summary>
        private void MoveFolderDown(ProjectTreeItem folderItem)
        {
            try
            {
                // 获取所有文件夹
                var folders = _dbManager.GetAllFolders();
                
                // 找到当前文件夹的索引
                int currentIndex = folders.FindIndex(f => f.Id == folderItem.Id);
                
                if (currentIndex == -1)
                {
                    ShowStatus("❌ 无法找到文件夹");
                    return;
                }
                
                // 如果已经是最后一个，无法下移
                if (currentIndex == folders.Count - 1)
                {
                    ShowStatus("⚠️ 已经是最后一个文件夹");
                    return;
                }
                
                // 与下一个文件夹交换位置
                var currentFolder = folders[currentIndex];
                folders.RemoveAt(currentIndex);
                folders.Insert(currentIndex + 1, currentFolder);
                
                // 更新所有文件夹的OrderIndex
                for (int i = 0; i < folders.Count; i++)
                {
                    folders[i].OrderIndex = i + 1;
                }
                
                // 保存到数据库
                _dbManager.UpdateFoldersOrder(folders);
                
                // 更新UI
                UpdateFolderTreeItemOrder(folders);
                
                ShowStatus($"✅ 已下移: {folderItem.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"文件夹下移失败: {ex}");
                ShowStatus($"❌ 下移失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 轻量级更新TreeView中的文件顺序（避免重新加载整个TreeView）
        /// </summary>
        private void UpdateTreeItemOrder(int? folderId, List<MediaFile> sortedFiles)
        {
            try
            {
                if (folderId.HasValue)
                {
                    // 更新文件夹内的文件顺序
                    var folderItem = _projectTreeItems.FirstOrDefault(f => f.Type == TreeItemType.Folder && f.Id == folderId.Value);
                    if (folderItem?.Children != null)
                    {
                        // 保存当前展开状态
                        bool wasExpanded = folderItem.IsExpanded;
                        
                        // 清空并重新添加文件（保持正确顺序）
                        folderItem.Children.Clear();
                        
                        foreach (var file in sortedFiles)
                        {
                            // 获取图标
                            string fileIconKind = "File";
                            string fileIconColor = "#95E1D3";
                            if (file.FileType == FileType.Image)
                            {
                                (fileIconKind, fileIconColor) = _originalManager.GetImageIconKind(file.Id);
                            }
                            
                            folderItem.Children.Add(new ProjectTreeItem
                            {
                                Id = file.Id,
                                Name = file.Name,
                                Icon = fileIconKind,
                                IconKind = fileIconKind,
                                IconColor = fileIconColor,
                                Type = TreeItemType.File,
                                Path = file.Path,
                                FileType = file.FileType
                            });
                        }
                        
                        // 恢复展开状态（延迟执行避免绑定冲突）
                        if (wasExpanded)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                folderItem.IsExpanded = true;
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                        
                        // 更新文件夹图标
                        // 检查文件夹是否包含媒体文件
                        bool hasMediaFiles = sortedFiles.Any(f => f.FileType == FileType.Video || f.FileType == FileType.Audio);
                        string folderPlayMode = _dbManager.GetFolderVideoPlayMode(folderId.Value);
                        
                        string iconKind, iconColor;
                        if (hasMediaFiles)
                        {
                            // 媒体文件夹保持播放模式图标
                            if (!string.IsNullOrEmpty(folderPlayMode))
                            {
                                (iconKind, iconColor) = GetPlayModeIcon(folderPlayMode);
                            }
                            else
                            {
                                (iconKind, iconColor) = ("Shuffle", "#FF9800");  // 默认随机播放
                            }
                        }
                        else
                        {
                            // 非媒体文件夹显示手动排序图标
                            (iconKind, iconColor) = _originalManager.GetFolderIconKind(folderId.Value, true);
                        }
                        
                        folderItem.IconKind = iconKind;
                        folderItem.IconColor = iconColor;
                    }
                }
                else
                {
                    // 更新根目录文件顺序 - 这种情况比较复杂，暂时还是用LoadProjects
                    LoadProjects();
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"更新TreeView顺序失败: {ex}");
                // 如果轻量级更新失败，回退到完整刷新
                LoadProjects();
            }
        }

        /// <summary>
        /// 轻量级更新TreeView中的文件夹顺序（避免重新加载整个TreeView）
        /// </summary>
        private void UpdateFolderTreeItemOrder(List<Folder> sortedFolders)
        {
            try
            {
                // 创建一个字典来快速查找新的顺序索引
                var orderDict = sortedFolders.Select((f, index) => new { f.Id, Order = index })
                    .ToDictionary(x => x.Id, x => x.Order);
                
                // 对 _projectTreeItems 中的文件夹进行排序（排除Project节点）
                var folders = _projectTreeItems.Where(item => item.Type == TreeItemType.Folder).ToList();
                var nonFolders = _projectTreeItems.Where(item => item.Type != TreeItemType.Folder).ToList();
                
                // 根据新的OrderIndex排序文件夹
                folders = folders.OrderBy(f => orderDict.ContainsKey(f.Id) ? orderDict[f.Id] : int.MaxValue).ToList();
                
                // 清空并重新添加（保持正确顺序）
                _projectTreeItems.Clear();
                
                // 先添加非文件夹项（如Project节点）
                foreach (var item in nonFolders)
                {
                    _projectTreeItems.Add(item);
                }
                
                // 再添加排序后的文件夹
                foreach (var folder in folders)
                {
                    _projectTreeItems.Add(folder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新文件夹TreeView顺序失败: {ex}");
                // 如果轻量级更新失败，回退到完整刷新
                LoadProjects();
            }
        }

        /// <summary>
        /// 获取文件所属的文件夹ID
        /// </summary>
        private int? GetFileFolderId(ProjectTreeItem fileItem)
        {
            // 在_projectTreeItems中查找该文件所属的文件夹
            foreach (var item in _projectTreeItems)
            {
                if (item.Type == TreeItemType.Folder && item.Children != null)
                {
                    if (item.Children.Any(c => c.Id == fileItem.Id))
                    {
                        return item.Id;
                    }
                }
            }
            
            // 如果没找到，说明是根目录文件
            return null;
        }

        #endregion
        
        #region 视频播放相关
        
        /// <summary>
        /// 视频播放状态改变事件
        /// </summary>
        private void OnVideoPlayStateChanged(object sender, bool isPlaying)
        {
            Dispatcher.Invoke(() =>
            {
                if (isPlaying)
                {
                    BtnMediaPlayPause.Content = "⏸";
                    
                    // 如果投影已开启且当前在主屏幕播放视频，自动启用视频投影
                    // 但如果已经在投影模式播放，就不要重复调用（避免闪烁）
                    if (_projectionManager != null && _projectionManager.IsProjectionActive)
                    {
                        if (_videoPlayerManager != null && !_videoPlayerManager.IsProjectionEnabled)
                        {
                            //System.Diagnostics.Debug.WriteLine("📹 视频开始播放，自动启用视频投影");
                            EnableVideoProjection();
                        }
                        else
                        {
                            //System.Diagnostics.Debug.WriteLine("✅ 已在投影模式播放，跳过重复启用");
                        }
                    }
                }
                else
                {
                    BtnMediaPlayPause.Content = "▶";
                }
            });
        }
        
        /// <summary>
        /// 视频媒体改变事件
        /// </summary>
        private void OnVideoMediaChanged(object sender, string mediaPath)
        {
            // System.Diagnostics.Debug.WriteLine($"📹 媒体已改变: {System.IO.Path.GetFileName(mediaPath)}");
            
            // 自动选中正在播放的文件
            SelectMediaFileByPath(mediaPath);
        }
        
        /// <summary>
        /// 根据路径选中文件节点
        /// </summary>
        private void SelectMediaFileByPath(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return;
                
                // 在项目树中查找并选中对应的文件
                foreach (var folderItem in _projectTreeItems)
                {
                    if (folderItem.Type == TreeItemType.Folder && folderItem.Children != null)
                    {
                        foreach (var fileItem in folderItem.Children)
                        {
                            if (fileItem.Type == TreeItemType.File && fileItem.Path == filePath)
                            {
                                // 展开父文件夹
                                folderItem.IsExpanded = true;
                                
                                // 取消其他所有选中
                                ClearAllSelections();
                                
                                // 选中当前文件
                                fileItem.IsSelected = true;
                                
                                //System.Diagnostics.Debug.WriteLine($"✅ 已自动选中文件: {fileItem.Name}");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 自动选中文件失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清除所有选中状态
        /// </summary>
        private void ClearAllSelections()
        {
            foreach (var folderItem in _projectTreeItems)
            {
                folderItem.IsSelected = false;
                if (folderItem.Children != null)
                {
                    foreach (var fileItem in folderItem.Children)
                    {
                        fileItem.IsSelected = false;
                    }
                }
            }
        }
        
        /// <summary>
        /// 视频播放结束事件
        /// </summary>
        private void OnVideoMediaEnded(object sender, EventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("🏁 视频播放结束");
        }
        
        /// <summary>
        /// 视频播放进度更新事件
        /// </summary>
        private void OnVideoProgressUpdated(object sender, (float position, long currentTime, long totalTime) progress)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isUpdatingProgress)
                {
                    _isUpdatingProgress = true;
                    
                    // 更新进度条
                    MediaProgressSlider.Value = progress.position * 100;
                    
                    // 更新时间显示
                    var currentSeconds = progress.currentTime / MILLISECONDS_PER_SECOND;
                    var totalSeconds = progress.totalTime / MILLISECONDS_PER_SECOND;
                    
                    var currentStr = $"{currentSeconds / 60:00}:{currentSeconds % 60:00}";
                    var totalStr = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
                    
                    MediaCurrentTime.Text = currentStr;
                    MediaTotalTime.Text = totalStr;
                    
                    _isUpdatingProgress = false;
                }
            });
        }
        
        /// <summary>
        /// 检查文件是否为视频
        /// </summary>
        private bool IsVideoFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            
            var ext = System.IO.Path.GetExtension(filePath).ToLower();
            var videoExtensions = new[] { 
                ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg",
                ".rm", ".rmvb", ".3gp", ".f4v", ".ts", ".mts", ".m2ts", ".vob", ".ogv"
            };
            
            return videoExtensions.Contains(ext);
        }
        
        /// <summary>
        /// 加载并显示媒体文件（图片或视频）
        /// </summary>
        private void LoadAndDisplayMedia(string filePath, int mediaId)
        {
            try
            {
                if (IsVideoFile(filePath))
                {
                    // 加载视频
                    LoadAndDisplayVideo(filePath);
                }
                else
                {
                    // 加载图片（使用现有的逻辑）
                    LoadImage(filePath);
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载媒体文件失败: {ex.Message}");
                MessageBox.Show($"加载媒体文件失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 在投影屏幕加载并播放视频（投影状态下使用）
        /// </summary>
        private void LoadAndDisplayVideoOnProjection(string videoPath)
        {
            try
            {
                //System.Diagnostics.Debug.WriteLine($"📹 ===== LoadAndDisplayVideoOnProjection 开始 =====");
                //System.Diagnostics.Debug.WriteLine($"📹 文件: {System.IO.Path.GetFileName(videoPath)}");
                
                var projectionVideoView = _projectionManager.GetProjectionVideoView();
                //System.Diagnostics.Debug.WriteLine($"🔍 投影VideoView: {(projectionVideoView != null ? "存在" : "null")}");
                
                if (projectionVideoView != null)
                {
                    //System.Diagnostics.Debug.WriteLine("步骤1: 隐藏主屏幕视频");
                    VideoContainer.Visibility = Visibility.Collapsed;
                    
                    //System.Diagnostics.Debug.WriteLine("步骤2: 显示投影视频");
                    _projectionManager.ShowVideoProjection();
                    
                    // 🔥 关键修复：检查投影窗口是否已经初始化完成
                    if (_videoPlayerManager != null && _videoPlayerManager.IsProjectionEnabled)
                    {
                        // 投影已经初始化完成，直接播放
                        //System.Diagnostics.Debug.WriteLine("✅ 投影已初始化，直接播放");
                        
                        // 切换到投影模式（如果还没切换）
                        _videoPlayerManager.SwitchToProjectionMode();
                        
                        // 构建播放列表并播放
                        BuildVideoPlaylist(videoPath);
                        _videoPlayerManager.Play(videoPath);
                        
                        var fileName = System.IO.Path.GetFileName(videoPath);
                        ShowStatus($"🎬 正在投影播放: {fileName}");
                    }
                    else
                    {
                        // 投影还未初始化，设置待播放路径，等待初始化完成后播放
                        _pendingProjectionVideoPath = videoPath;
                        //System.Diagnostics.Debug.WriteLine($"🟠 设置待投影播放视频: {System.IO.Path.GetFileName(videoPath)}");
                        ShowStatus($"🎬 准备投影播放: {System.IO.Path.GetFileName(videoPath)}");
                    }
                    
                    //System.Diagnostics.Debug.WriteLine($"📹 ===== LoadAndDisplayVideoOnProjection 完成 =====");
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 投影播放视频失败: {ex.Message}");
                MessageBox.Show($"投影播放视频失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 视频轨道检测事件处理
        /// </summary>
        private void VideoPlayerManager_VideoTrackDetected(object sender, bool hasVideo)
        {
            try
            {
                //System.Diagnostics.Debug.WriteLine($"🎬 收到视频轨道检测结果: HasVideo={hasVideo}");
                
                // 🔥 关键修复：使用 VideoPlayerManager 的当前播放文件，而不是 _imagePath
                string currentPath = _videoPlayerManager?.CurrentMediaPath;
                string fileName = !string.IsNullOrEmpty(currentPath) 
                    ? System.IO.Path.GetFileName(currentPath) 
                    : "未知文件";
                
                // 主窗口：显示或隐藏文件名
                if (!hasVideo)
                {
                    MediaFileNameText.Text = fileName;
                    MediaFileNameBorder.Visibility = Visibility.Visible;
                    //System.Diagnostics.Debug.WriteLine($"🎵 无视频轨道，显示文件名: {fileName}");
                }
                else
                {
                    MediaFileNameBorder.Visibility = Visibility.Collapsed;
                    //System.Diagnostics.Debug.WriteLine($"📹 有视频轨道，隐藏文件名");
                }
                
                // 投影窗口：如果投影已开启，同步显示
                if (_projectionManager != null && _projectionManager.IsProjectionActive)
                {
                    _projectionManager.SetProjectionMediaFileName(fileName, !hasVideo);
                }
                
                // 更新状态栏
                string icon = hasVideo ? "📹" : "🎵";
                string type = hasVideo ? "视频" : "音频";
                ShowStatus($"{icon} {type}: {fileName}");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 处理视频轨道检测失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 加载并显示视频
        /// </summary>
        private void LoadAndDisplayVideo(string videoPath)
        {
            try
            {
                // 显示视频播放区域
                VideoContainer.Visibility = Visibility.Visible;
                
                // 先隐藏文件名，等视频轨道检测完成后再决定是否显示
                MediaFileNameBorder.Visibility = Visibility.Collapsed;
                
                // 隐藏媒体控制栏（改用快捷键控制）
                // MediaPlayerPanel.Visibility = Visibility.Visible;
                
                // 强制刷新布局，确保VideoView就绪
                VideoContainer.UpdateLayout();
                
                // 构建播放列表（获取当前文件所在文件夹的所有视频文件）
                BuildVideoPlaylist(videoPath);
                
                // 加载并播放视频（视频轨道检测会在播放开始后自动触发）
                if (_videoPlayerManager != null)
                {
                    _videoPlayerManager.Play(videoPath);
                }
                
                // 如果投影已开启，视频投影会在OnVideoPlayStateChanged事件中自动启用
                
                string fileName = System.IO.Path.GetFileName(videoPath);
                ShowStatus($"📹 正在加载: {fileName}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载视频失败: {ex.Message}");
                MessageBox.Show($"加载视频失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        
        /// <summary>
        /// 构建视频播放列表
        /// </summary>
        private void BuildVideoPlaylist(string currentVideoPath)
        {
            try
            {
                if (_videoPlayerManager == null || _dbManager == null) return;
                
                // 方法1: 通过路径在所有文件夹中查找
                MediaFile currentMediaFile = null;
                
                // 先在根目录查找
                var rootFiles = _dbManager.GetRootMediaFiles();
                currentMediaFile = rootFiles.FirstOrDefault(f => f.Path == currentVideoPath);
                
                // 如果根目录没找到，遍历所有文件夹查找
                if (currentMediaFile == null)
                {
                    var folders = _dbManager.GetAllFolders();
                    foreach (var folder in folders)
                    {
                        var folderFiles = _dbManager.GetMediaFilesByFolder(folder.Id);
                        currentMediaFile = folderFiles.FirstOrDefault(f => f.Path == currentVideoPath);
                        if (currentMediaFile != null)
                            break;
                    }
                }
                
                if (currentMediaFile == null)
                {
                    //System.Diagnostics.Debug.WriteLine("❌ 未找到当前视频文件信息");
                    return;
                }
                
                List<string> playlist = new List<string>();
                
                // 获取同一文件夹下的所有视频文件
                if (currentMediaFile.FolderId.HasValue)
                {
                    var folderFiles = _dbManager.GetMediaFilesByFolder(currentMediaFile.FolderId.Value);
                    
                    // 筛选出视频文件
                    var videoFiles = folderFiles
                        .Where(f => f.FileType == FileType.Video)
                        .OrderBy(f => f.OrderIndex ?? 0)
                        .ThenBy(f => f.Name)
                        .ToList();
                    
                    playlist = videoFiles.Select(f => f.Path).ToList();
                    
                    //System.Diagnostics.Debug.WriteLine($"📋 构建播放列表: 文件夹 [{currentMediaFile.Folder?.Name}] 中有 {playlist.Count} 个视频");
                }
                else
                {
                    // 根目录文件
                    var videoFiles = rootFiles
                        .Where(f => f.FileType == FileType.Video)
                        .OrderBy(f => f.OrderIndex ?? 0)
                        .ThenBy(f => f.Name)
                        .ToList();
                    
                    playlist = videoFiles.Select(f => f.Path).ToList();
                    
                    //System.Diagnostics.Debug.WriteLine($"📋 构建播放列表: 根目录中有 {playlist.Count} 个视频");
                }
                
                // 设置播放列表到VideoPlayerManager
                if (playlist.Count > 0)
                {
                    _videoPlayerManager.SetPlaylist(playlist);
                    
                    // 找到当前视频在播放列表中的索引
                    int currentIndex = playlist.IndexOf(currentVideoPath);
                    if (currentIndex >= 0)
                    {
                        //System.Diagnostics.Debug.WriteLine($"📹 当前视频索引: {currentIndex + 1}/{playlist.Count}");
                    }
                    
                    // 根据文件夹标记自动设置播放模式
                    if (currentMediaFile.FolderId.HasValue)
                    {
                        string folderPlayMode = _dbManager.GetFolderVideoPlayMode(currentMediaFile.FolderId.Value);
                        if (!string.IsNullOrEmpty(folderPlayMode))
                        {
                            PlayMode mode = folderPlayMode switch
                            {
                                "sequential" => PlayMode.Sequential,
                                "random" => PlayMode.Random,
                                "loop_all" => PlayMode.LoopAll,
                                _ => PlayMode.Sequential
                            };
                            
                            _videoPlayerManager.SetPlayMode(mode);
                            
                            string[] modeNames = { "顺序", "随机", "单曲", "列表" };
                            //System.Diagnostics.Debug.WriteLine($"🎵 根据文件夹标记自动设置播放模式: {modeNames[(int)mode]}");
                            ShowStatus($"🎵 播放模式: {modeNames[(int)mode]}");
                        }
                    }
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine("⚠️ 播放列表为空");
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 构建播放列表失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 切换回图片显示模式
        /// </summary>
        private void SwitchToImageMode()
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("\n🔄 ========== SwitchToImageMode 被调用 ==========");
            System.Diagnostics.Debug.WriteLine($"   当前时间: {DateTime.Now:HH:mm:ss:fff}");
            System.Diagnostics.Debug.WriteLine($"   _videoPlayerManager != null: {_videoPlayerManager != null}");
            System.Diagnostics.Debug.WriteLine($"   _videoPlayerManager.IsPlaying: {_videoPlayerManager?.IsPlaying}");
            System.Diagnostics.Debug.WriteLine($"   当前 _imagePath: {_imagePath ?? "null"}");
            System.Diagnostics.Debug.WriteLine($"   当前 _currentImageId: {_currentImageId}");
#endif
            
            // 停止视频播放
            if (_videoPlayerManager != null && _videoPlayerManager.IsPlaying)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("   步骤1: 停止视频播放");
#endif
                _videoPlayerManager.Stop();
            }
#if DEBUG
            else
            {
                System.Diagnostics.Debug.WriteLine("   步骤1: 视频未播放，跳过停止");
            }
#endif
            
            // 隐藏视频播放区域
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"   步骤2: 隐藏视频容器 (当前: {VideoContainer.Visibility})");
#endif
            VideoContainer.Visibility = Visibility.Collapsed;
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"   步骤2: 视频容器已设置为 {VideoContainer.Visibility}");
#endif
            
            // 隐藏媒体控制栏
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"   步骤3: 隐藏媒体控制栏 (当前: {MediaPlayerPanel.Visibility})");
#endif
            MediaPlayerPanel.Visibility = Visibility.Collapsed;
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"   步骤3: 媒体控制栏已设置为 {MediaPlayerPanel.Visibility}");
#endif
            
            // 清空图片显示（避免回到之前的图片）
#if DEBUG
            System.Diagnostics.Debug.WriteLine("   步骤4: 调用 ClearImageDisplay()");
#endif
            ClearImageDisplay();
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"   步骤4: ClearImageDisplay() 完成");
            System.Diagnostics.Debug.WriteLine($"   清空后 _imagePath: {_imagePath ?? "null"}");
            System.Diagnostics.Debug.WriteLine($"   清空后 _currentImageId: {_currentImageId}");
            System.Diagnostics.Debug.WriteLine("========== SwitchToImageMode 完成 ==========\n");
#endif
        }
        
        /// <summary>
        /// 启用视频投屏
        /// </summary>
        private void EnableVideoProjection()
        {
            try
            {
                if (_videoPlayerManager == null || _projectionManager == null) return;
                
                //System.Diagnostics.Debug.WriteLine("📹 启用视频投屏");
                
                // 隐藏主屏幕的视频容器
                VideoContainer.Visibility = Visibility.Collapsed;
                
                // 切换到视频投影模式
                _projectionManager.ShowVideoProjection();
                
                // 启用视频投影（VideoView已在Loaded事件中绑定）
                _videoPlayerManager.EnableProjection();
                
                ShowStatus("✅ 视频投屏已启用");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 启用视频投屏失败: {ex.Message}");
                MessageBox.Show($"启用视频投屏失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 禁用视频投屏
        /// </summary>
        private void DisableVideoProjection()
        {
            try
            {
                if (_videoPlayerManager == null) return;
                
                //System.Diagnostics.Debug.WriteLine("📹 禁用视频投屏");
                
                // 禁用视频投影
                _videoPlayerManager.DisableProjection();
                
                // 如果投影窗口还在，切换回图片投影模式
                if (_projectionManager != null && _projectionManager.IsProjectionActive)
                {
                    _projectionManager.ShowImageProjection();
                }
                
                ShowStatus("🔴 视频投屏已禁用");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 禁用视频投屏失败: {ex.Message}");
            }
        }
        
        #endregion
    }

    #region 数据模型

    public class ProjectTreeItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        
        private string _name;
        public string Name 
        { 
            get => _name; 
            set 
            { 
                if (_name != value) 
                { 
                    _name = value; 
                    OnPropertyChanged(nameof(Name)); 
                } 
            } 
        }
        
        public string Icon { get; set; }
        private string _iconKind;
        public string IconKind 
        { 
            get => _iconKind; 
            set 
            { 
                if (_iconKind != value) 
                { 
                    _iconKind = value; 
                    OnPropertyChanged(nameof(IconKind)); 
                } 
            } 
        }

        private string _iconColor = "#666666";
        public string IconColor 
        { 
            get => _iconColor; 
            set 
            { 
                if (_iconColor != value) 
                { 
                    _iconColor = value; 
                    OnPropertyChanged(nameof(IconColor)); 
                } 
            } 
        }
        public TreeItemType Type { get; set; }
        public string Path { get; set; }
        public FileType FileType { get; set; }
        public ObservableCollection<ProjectTreeItem> Children { get; set; } = new ObservableCollection<ProjectTreeItem>();
        
        // 文件夹标签（用于在搜索结果中显示所属文件夹）
        public string FolderName { get; set; }  // 所属文件夹名称
        public string FolderColor { get; set; } = "#666666";  // 文件夹标记颜色
        public bool ShowFolderTag { get; set; } = false;  // 是否显示文件夹标签

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged(nameof(IsEditing));
                }
            }
        }

        // 编辑前的原始名称
        public string OriginalName { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum TreeItemType
    {
        Project,
        Folder,
        File,
        Image,
        Video,
        Audio,
        TextProject  // 文本项目
    }

    #endregion
}
