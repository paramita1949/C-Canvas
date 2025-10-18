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
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"\n🎯 ========== 关键帧跳转开始 ==========");
                        #endif
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🎯 目标关键帧: ID={e.KeyframeId}, Position={e.Position:F4}, 直接跳转={e.UseDirectJump}");
                        #endif
                        
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
                                    #if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"⚡ [跳转] 直接跳转: {scrollTime}ms");
                                    #endif
                                }
                                else
                                {
                                    // 使用平滑滚动动画
                                    _keyframeManager.SmoothScrollTo(e.Position);
                                    var scrollTime = jumpTime.ElapsedMilliseconds - scrollStart;
                                    #if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"🎬 [跳转] 平滑滚动启动: {scrollTime}ms");
                                    #endif
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
                                            #if DEBUG
                                            System.Diagnostics.Debug.WriteLine($"🎯 [跳转] 更新索引: {indexTime}ms -> #{i + 1}");
                                            #endif
                                            break;
                                        }
                                    }
                                }
                                
                                // 3. 更新指示器和预览线
                                var uiStart = jumpTime.ElapsedMilliseconds;
                                _keyframeManager?.UpdatePreviewLines();
                                var uiTime = jumpTime.ElapsedMilliseconds - uiStart;
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"🎯 [跳转] 更新UI: {uiTime}ms");
                                #endif
                                
                                jumpTime.Stop();
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"🎯 ========== 关键帧跳转完成: {jumpTime.ElapsedMilliseconds}ms ==========\n");
                                #endif
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
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine("========================================");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🎮 GPU加速状态: {(gpuContext.IsGpuAvailable ? "✅ 已启用" : "⚠️ 已降级到CPU")}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"📊 GPU信息: {gpuContext.GpuInfo}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine("========================================");
            #endif
            
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
            
            // 🔧 确保主窗口获得焦点以接收键盘事件
            this.Loaded += (s, e) => 
            {
                this.Focus();
                this.Activate();
#if DEBUG
                System.Diagnostics.Debug.WriteLine("✅ [焦点] 主窗口已激活并获得焦点");
#endif
            };
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
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🎬 [MainWindow.UpdateProjection] 被调用");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   _imageProcessor.CurrentImage = {_imageProcessor?.CurrentImage?.Width}x{_imageProcessor?.CurrentImage?.Height}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   _projectionManager = {_projectionManager != null}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   _projectionManager.IsProjectionActive = {_projectionManager?.IsProjectionActive}");
            #endif
            
            if (_imageProcessor.CurrentImage != null)
            {
                if (_projectionManager != null && _projectionManager.IsProjectionActive)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ [MainWindow.UpdateProjection] 调用 UpdateProjectionImage");
                    #endif
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
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [UpdateProjection] 投影未开启，跳过");
                    #endif
                }
            }
            else
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [UpdateProjection] _imageProcessor.CurrentImage 为 null");
                #endif
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
        // 设置管理相关方法已移至 MainWindow.Settings.cs

        #endregion

        #region 顶部菜单栏事件

        // 导入文件相关方法已移至 MainWindow.Import.cs

        // 字号设置方法已移至 MainWindow.Settings.cs

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

        // 项目树事件已移至 MainWindow.ProjectTree.cs


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
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ImageProcessor.LoadImage: {loadTime}ms");
                #endif
                
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
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 数据库检查原图标记: {dbCheckTime}ms");
                        #endif
                        
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
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 查找相似图片: {findTime}ms");
                            #endif
                            
                            // ⚡ 立即触发智能预缓存（不等待用户操作）
                            // 这样第一次切换时预缓存已经完成或接近完成
                            _ = TriggerSmartPreload();
                        }
                        
                        // 🌲 同步项目树选中状态
                        var treeStart = sw.ElapsedMilliseconds;
                        SelectTreeItemById(_currentImageId);
                        var treeTime = sw.ElapsedMilliseconds - treeStart;
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 同步项目树: {treeTime}ms");
                        #endif
                        
                        // 🔧 修复：更新播放控制状态（检查录制数据，更新脚本按钮颜色）
                        if (_playbackViewModel != null)
                        {
                            var mode = _originalMode ? Database.Models.Enums.PlaybackMode.Original : Database.Models.Enums.PlaybackMode.Keyframe;
                            _ = _playbackViewModel.SetCurrentImageAsync(_currentImageId, mode);
                        }
                    }
                    
                    // 颜色效果由 ImageProcessor 内部处理
                    
                    // 更新投影
                    var projStart = sw.ElapsedMilliseconds;
                    UpdateProjection();
                    var projTime = sw.ElapsedMilliseconds - projStart;
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 更新投影: {projTime}ms");
                    #endif
                    
                    // 更新关键帧预览线和指示块
                    var kfStart = sw.ElapsedMilliseconds;
                    _keyframeManager?.UpdatePreviewLines();
                    var kfTime = sw.ElapsedMilliseconds - kfStart;
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⏱️ [性能] 更新关键帧预览: {kfTime}ms");
                    #endif
                    
                    // 🔧 更新 PlaybackViewModel 状态（检查时间数据，更新脚本按钮颜色）
                    if (_playbackViewModel != null && _currentImageId > 0)
                    {
                        _ = _playbackViewModel.SetCurrentImageAsync(_currentImageId, 
                            _originalMode ? Database.Models.Enums.PlaybackMode.Original : Database.Models.Enums.PlaybackMode.Keyframe);
                    }
                    
                    sw.Stop();
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== LoadImage 总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    #endif
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
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   清空前 _imagePath: {_imagePath ?? "null"}");
                #endif
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   清空前 _currentImageId: {_currentImageId}");
                #endif
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
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("========== ClearImageDisplay 完成 ==========\n");
                #endif
#endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 清空图片显示失败: {ex.Message}");
                #endif
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

        // 右键菜单处理已移至 MainWindow.ContextMenu.cs


        // 窗口生命周期事件已移至 MainWindow.Lifecycle.cs
        
        /// <summary>
        /// 窗口激活事件 - 确保能接收键盘事件
        /// </summary>
        private void Window_Activated(object sender, EventArgs e)
        {
            this.Focus();
#if DEBUG
            System.Diagnostics.Debug.WriteLine("✅ [焦点] 窗口已激活，焦点已恢复");
#endif
        }
        
        /// <summary>
        /// 图片区域点击事件 - 恢复主窗口焦点
        /// </summary>
        private void ImageArea_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 点击图片区域时，让主窗口获得焦点，以便接收键盘事件
            this.Focus();
        }

        #region 键盘事件处理

        /// <summary>
        /// 主窗口键盘事件处理
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"⌨️ [DEBUG] Window_PreviewKeyDown 触发: Key={e.Key}");
#endif
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
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   触发时间: {DateTime.Now:HH:mm:ss:fff}");
                #endif
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   _videoPlayerManager != null: {_videoPlayerManager != null}");
                #endif
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   _videoPlayerManager.IsPlaying: {_videoPlayerManager?.IsPlaying}");
                #endif
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   _projectionManager != null: {_projectionManager != null}");
                #endif
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"   _projectionManager.IsProjectionActive: {_projectionManager?.IsProjectionActive}");
                #endif
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
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 无投影需要关闭");
                        #endif
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
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 无需处理视频停止");
                    #endif
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
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 事件未处理");
                    #endif
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("========== 主窗口热键 ESC 处理完成 ==========\n");
                    #endif
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

        // 拖拽事件处理已移至 MainWindow.DragDrop.cs

        
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
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   当前时间: {DateTime.Now:HH:mm:ss:fff}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   _videoPlayerManager != null: {_videoPlayerManager != null}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   _videoPlayerManager.IsPlaying: {_videoPlayerManager?.IsPlaying}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   当前 _imagePath: {_imagePath ?? "null"}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   当前 _currentImageId: {_currentImageId}");
            #endif
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
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("   步骤1: 视频未播放，跳过停止");
                #endif
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
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   清空后 _imagePath: {_imagePath ?? "null"}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"   清空后 _currentImageId: {_currentImageId}");
            #endif
            #if DEBUG
            System.Diagnostics.Debug.WriteLine("========== SwitchToImageMode 完成 ==========\n");
            #endif
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
