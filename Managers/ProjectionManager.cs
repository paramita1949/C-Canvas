using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;
using SkiaSharp;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
//  已移除 Windows Forms 依赖，使用 WPF 原生 API
// using Screen = System.Windows.Forms.Screen;
using LibVLCSharp.WPF;
using Microsoft.Extensions.Caching.Memory;
using System.Windows.Threading;
namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影管理器
    /// 负责管理投影窗口、多屏幕支持、同步逻辑等功能
    /// </summary>
    public partial class ProjectionManager : IDisposable
    {
        #region 事件

        /// <summary>
        /// 投影状态改变事件
        /// </summary>
        public event EventHandler<bool> ProjectionStateChanged;

        /// <summary>
        /// 投影VideoView加载完成事件
        /// </summary>
        public event EventHandler<VideoView> ProjectionVideoViewLoaded;

        #endregion

        // 主应用引用
        private readonly Window _mainWindow;
        private readonly ScrollViewer _mainScrollViewer;
        private readonly System.Windows.Controls.Image _mainImageControl;
        private readonly ImageProcessor _imageProcessor;
        private readonly GPUContext _gpuContext;
        private readonly System.Windows.Controls.ComboBox _screenComboBox;

        // 投影窗口相关
        private Window _projectionWindow;
        private ScrollViewer _projectionScrollViewer;
        private Grid _projectionContainer;  // 容器Grid,用于控制滚动区域
        private System.Windows.Controls.Image _projectionImageControl;
        private BitmapSource _projectionImage;
        private Grid _projectionNoticeOverlayContainer; // 通知覆盖层容器
        private System.Windows.Controls.Image _projectionNoticeOverlayImage; // 通知覆盖层图像
        
        // 视频投影相关
        private Grid _projectionVideoContainer;  // 视频容器
        private VideoView _projectionVideoView;  // 视频视图
        private Grid _projectionMediaFileNameBorder;  // 媒体文件名容器
        private TextBlock _projectionMediaFileNameText;  // 媒体文件名文本
        
        // 圣经投影相关
        private Border _projectionBibleTitleBorder;  // 圣经标题容器（固定在顶部）
        private TextBlock _projectionBibleTitleText;  // 圣经标题文本
        private Border _projectionBiblePopupBorder;  // 圣经弹窗容器
        private TextBlock _projectionBiblePopupReferenceText; // 圣经弹窗引用文本
        private ScrollViewer _projectionBiblePopupContentScrollViewer; // 圣经弹窗正文滚动容器
        private TextBlock _projectionBiblePopupContentText; // 圣经弹窗正文文本
        private System.Windows.Controls.Button _projectionBiblePopupCloseButton; // 圣经弹窗关闭按钮
        private DispatcherTimer _projectionBiblePopupTimer; // 圣经弹窗自动关闭计时器
        
        // VisualBrush投影相关（圣经经文）
        private System.Windows.Shapes.Rectangle _projectionVisualBrushRect;  // 用于显示VisualBrush的矩形
        private ScrollViewer _currentBibleScrollViewer;  // 当前正在投影的圣经ScrollViewer
        
        //  锁定模式视频投影相关（独立 MediaElement）
        private System.Windows.Controls.MediaElement _projectionMediaElement;  // 投影窗口的独立 MediaElement（锁定模式使用）
        private string _lockedVideoPath;  // 锁定模式下的视频路径（用于保持播放）

        //  VLC D3D11 渲染相关（锁定模式）
        private LibVLCSharp.Shared.LibVLC _projectionLibVLC;  // LibVLC 实例
        private LibVLCSharp.Shared.MediaPlayer _projectionVlcPlayer;  // VLC 播放器
        private VlcD3D11Renderer _projectionVlcRenderer;  // D3D11 渲染器
        private System.Windows.Controls.Image _projectionVideoImage;  // 显示视频的 Image 控件
        private VideoPlayerManager _videoPlayerManager;  // 视频播放管理器引用
        private readonly IProjectionAuthPolicy _authPolicy;  // 投影授权策略
        private readonly IProjectionUiNotifier _uiNotifier;  // UI通知适配器
        private readonly IProjectionHost _host;  // 宿主能力适配器
        private readonly IProjectionWindowFactory _windowFactory;  // 投影窗口工厂
        
        //  文本层缓存（避免重复转换）
        private System.Windows.Media.Imaging.BitmapSource _cachedTextLayerBitmap;
        private long _cachedTextLayerTimestamp;
        
        //  已删除：视频预解析缓存（简化为跟主屏幕WPF一样的逻辑）

        // 屏幕管理（WPF 原生）
        private List<WpfScreenInfo> _screens;
        private int _currentScreenIndex;

        // 状态管理
        private bool _syncEnabled;
        // private bool _globalHotkeysEnabled; // TODO: 实现全局热键时再启用

        // 性能优化
        private DateTime _lastSyncTime;
        private TimeSpan _syncThrottleInterval = TimeSpan.FromMilliseconds(8); // 约120FPS（与Python版本一致）

        // 当前状态
        private SKBitmap _currentImage;
        private bool _isColorEffectEnabled;
        private double _zoomRatio = 1.0;
        private bool _isOriginalMode;
        private OriginalDisplayMode _originalDisplayMode = OriginalDisplayMode.Fit;
        private int _originalTopScalePercent = 80;
        private string _currentImagePath; // 用于缓存键生成
        
        //  投影图片缓存
        private readonly IMemoryCache _projectionCache;
        private readonly ProjectionImageRenderService _projectionImageRenderService;
        
        //  预渲染状态
        private bool _isPreRendering = false;
        private readonly object _preRenderLock = new object();
        
        //  共享渲染缓存
        private BitmapSource _lastSharedBitmap = null;

        // BibleTitle 低频诊断日志门控（仅关键参数变化时输出）
        private string _lastBibleTitleDiagSignature;
        
        
        //  投影时间限制（未登录状态）
        private DateTime? _projectionStartTime;
        private System.Threading.Timer _projectionTimer;
        private long _projectionStartTick; // 使用 TickCount64 防篡改
        private string _localProjectionChecksum; // 本地校验和（额外防护层）
        private bool _isClosingProjectionWindow; // 防止窗口关闭事件重入

        private readonly struct ProjectionRenderContext
        {
            public ProjectionRenderContext(int screenWidth, int screenHeight, int imageWidth, int imageHeight)
            {
                ScreenWidth = screenWidth;
                ScreenHeight = screenHeight;
                ImageWidth = imageWidth;
                ImageHeight = imageHeight;
            }

            public int ScreenWidth { get; }
            public int ScreenHeight { get; }
            public int ImageWidth { get; }
            public int ImageHeight { get; }
        }

        private readonly struct ProjectionScreenMetrics
        {
            public ProjectionScreenMetrics(int physicalWidth, int physicalHeight, int wpfWidth, int wpfHeight)
            {
                PhysicalWidth = physicalWidth;
                PhysicalHeight = physicalHeight;
                WpfWidth = wpfWidth;
                WpfHeight = wpfHeight;
            }

            public int PhysicalWidth { get; }
            public int PhysicalHeight { get; }
            public int WpfWidth { get; }
            public int WpfHeight { get; }
        }

        private const int DefaultProjectionWidth = 1920;
        private const int DefaultProjectionHeight = 1080;
        
        /// <summary>
        /// 获取试用时长限制（随机30-60秒）
        /// 随机化使得破解者无法预测具体时长
        /// </summary>
        private int GetTrialDurationSeconds()
        {
            // 基于硬件ID生成伪随机数，保证每台电脑相对固定，但不同电脑不同
            var hardwareId = _authPolicy.GetIdentitySeed();
            var hashCode = hardwareId.GetHashCode();
            var seed = Math.Abs(hashCode);
            var random = new Random(seed);
            
            // 30-60秒随机
            int minSeconds = 30;
            int maxSeconds = 60;
            return random.Next(minSeconds, maxSeconds + 1);
        }

        /// <summary>
        /// 是否正在投影
        /// </summary>
        public bool IsProjecting => _projectionWindow != null;

        /// <summary>
        /// 获取当前投影屏幕的实际尺寸（考虑DPI缩放）
        /// </summary>
        public (double width, double height) GetProjectionScreenSize()
        {
            if (_projectionScrollViewer != null && _projectionScrollViewer.ActualWidth > 0)
            {
                // 返回ScrollViewer的实际尺寸（已考虑DPI缩放，WPF单位）
                return (_projectionScrollViewer.ActualWidth, _projectionScrollViewer.ActualHeight);
            }
            
            var screen = GetCurrentProjectionScreenOrNull();
            if (screen != null)
            {
                //  返回屏幕的WPF单位（而不是物理像素）
                return (screen.WpfWidth, screen.WpfHeight);
            }
            
            return (DefaultProjectionWidth, DefaultProjectionHeight);
        }

        /// <summary>
        /// 获取投影容器（用于动画）
        /// </summary>
        public UIElement GetProjectionContainer()
        {
            // 优先返回 ScrollViewer（包含所有投影内容）
            if (_projectionScrollViewer != null)
                return _projectionScrollViewer;
            
            // 如果没有 ScrollViewer，返回容器 Grid
            if (_projectionContainer != null)
                return _projectionContainer;
            
            // 最后返回窗口本身
            return _projectionWindow;
        }

        public ProjectionManager(
            Window mainWindow,
            ScrollViewer mainScrollViewer,
            System.Windows.Controls.Image mainImageControl,
            ImageProcessor imageProcessor,
            GPUContext gpuContext,
            System.Windows.Controls.ComboBox screenComboBox,
            IProjectionAuthPolicy authPolicy,
            IProjectionUiNotifier uiNotifier,
            IProjectionHost host,
            IProjectionWindowFactory windowFactory,
            VideoPlayerManager videoPlayerManager = null)
        {
            _mainWindow = mainWindow;
            _mainScrollViewer = mainScrollViewer;
            _mainImageControl = mainImageControl;
            _imageProcessor = imageProcessor;
            _gpuContext = gpuContext ?? throw new ArgumentNullException(nameof(gpuContext));
            _screenComboBox = screenComboBox;
            _authPolicy = authPolicy ?? throw new ArgumentNullException(nameof(authPolicy));
            _uiNotifier = uiNotifier ?? throw new ArgumentNullException(nameof(uiNotifier));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
            _videoPlayerManager = videoPlayerManager;

            _screens = new List<WpfScreenInfo>();
            _currentScreenIndex = 0;
            
            //  初始化投影图片缓存
            _projectionCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 50, // 最多缓存50张投影图片（基于权重计算）
                CompactionPercentage = 0.25, // 达到上限时清理25%最少使用的项
                ExpirationScanFrequency = TimeSpan.FromMinutes(5) // 每5分钟扫描过期项
            });
            _projectionImageRenderService = new ProjectionImageRenderService(_imageProcessor, _gpuContext, _projectionCache);
            _syncEnabled = false;
            // _globalHotkeysEnabled = false; // TODO: 实现全局热键时再启用
            _lastSyncTime = DateTime.Now;

            InitializeScreenInfo();
        }

        /// <summary>
        /// 显式注入视频播放器管理器，避免通过反射访问私有字段。
        /// </summary>
        /// <param name="videoPlayerManager">视频播放器管理器</param>
        public void AttachVideoPlayerManager(VideoPlayerManager videoPlayerManager)
        {
            _videoPlayerManager = videoPlayerManager;
        }

        #region 属性

        /// <summary>
        /// 投影窗口是否打开
        /// </summary>
        public bool IsProjectionActive => _projectionWindow != null;

        /// <summary>
        /// 是否启用同步
        /// </summary>
        public bool IsSyncEnabled => _syncEnabled;

        #endregion

        #region 公有API方法

        /// <summary>
        /// 切换投影显示状态
        /// </summary>
        public bool ToggleProjection()
        {
            try
            {
                // //System.Diagnostics.Debug.WriteLine(" ToggleProjection 被调用");
                // //System.Diagnostics.Debug.WriteLine($"当前投影窗口状态: {(_projectionWindow != null ? "已打开" : "未打开")}");
                // //System.Diagnostics.Debug.WriteLine($"当前图片: {(_currentImage != null ? $"{_currentImage.Width}x{_currentImage.Height}" : "null")}");
                // //System.Diagnostics.Debug.WriteLine($"屏幕数量: {_screens.Count}");
                
                if (_projectionWindow != null)
                {
                    // //System.Diagnostics.Debug.WriteLine("关闭投影窗口");
                    return CloseProjection();
                }
                else
                {
                    //  后台静默验证投影权限（仅在有网络时执行，不阻塞UI）
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 快速检测网络可用性（1秒超时）
                            using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1) })
                            {
                                try
                                {
                                    await client.GetAsync("https://www.baidu.com", System.Threading.CancellationToken.None);
                                    
                                    // 有网络，执行验证
                                    //#if DEBUG
                                    //System.Diagnostics.Debug.WriteLine($" [投影] 检测到网络连接，开始后台验证");
                                    //#endif
                                    
                                    var (allowed, message) = await _authPolicy.VerifyProjectionPermissionAsync();
                                    
                                    //#if DEBUG
                                    //System.Diagnostics.Debug.WriteLine($" [投影] 后台网络验证结果: {message}（allowed={allowed}）");
                                    //#endif
                                    
                                    //  后台静默记录验证结果，不影响试用
                                    // 验证目的：防止破解者绕过登录，但不阻止正常试用
                                }
                                catch (TaskCanceledException)
                                {
                                    // 超时或取消均视为“当前不可用网络探测”，非致命
#if DEBUG
                                    System.Diagnostics.Debug.WriteLine(" [投影] 网络探测超时/取消，跳过后台验证");
#endif
                                }
                                catch (OperationCanceledException)
                                {
                                    // 防御性分支：与 TaskCanceledException 含义一致，统一降级处理
#if DEBUG
                                    System.Diagnostics.Debug.WriteLine(" [投影] 网络探测已取消，跳过后台验证");
#endif
                                }
                                catch (System.Net.Http.HttpRequestException)
                                {
                                    // 无网络，跳过验证
#if DEBUG
                                    System.Diagnostics.Debug.WriteLine(" [投影] 无网络连接，跳过后台验证");
#endif
                                }
                            }
                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($" [投影] 后台验证异常: {ex.Message}");
#else
                            _ = ex; // 避免未使用变量警告
#endif
                        }
                    });
                    
                    //  检查账号验证状态
                    if (!_authPolicy.IsAuthenticated)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine(" [投影] 未登录，将启用随机试用限制");
                        //#endif
                        
                        // 未登录，静默启用随机试用限制（不弹窗）
                        // 时长由 GetTrialDurationSeconds() 随机决定（30-60秒）
                    }
                    else if (!_authPolicy.CanUseProjection())
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine(" [投影] 账号已过期");
                        //#endif
                        
                        _uiNotifier.ShowMessage(
                            "账号已过期",
                            "您的账号已过期，无法使用投影功能。\n请联系管理员续费。",
                            ProjectionUiMessageLevel.Warning);
                        return false;
                    }
                    
                    // //System.Diagnostics.Debug.WriteLine("打开投影窗口");
                    return OpenProjection();
                }
            }
            catch (Exception ex)
            {
                // //System.Diagnostics.Debug.WriteLine($" 切换投影失败: {ex.Message}");
                // //System.Diagnostics.Debug.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                _uiNotifier.ShowMessage("错误", $"投影失败: {ex.Message}", ProjectionUiMessageLevel.Error);
                return false;
            }
        }

        /// <summary>
        ///  使用 VisualBrush 投影圣经经文（100%像素级一致）
        /// </summary>
        /// <param name="bibleScrollViewer">主屏幕的圣经 ScrollViewer</param>
        public void UpdateBibleProjectionWithVisualBrush(ScrollViewer bibleScrollViewer)
        {
            if (_projectionWindow == null || bibleScrollViewer == null)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [VisualBrush投影] 投影窗口或ScrollViewer为空，跳过");
                //#endif
                return;
            }

            try
            {
                //  第一步：先清空旧的投影内容
                RunOnMainDispatcher(() =>
                {
                    // 保存当前圣经ScrollViewer引用
                    _currentBibleScrollViewer = bibleScrollViewer;
                    
                    //  清空投影窗口，重置状态
                    if (_projectionScrollViewer != null)
                    {
                        _projectionScrollViewer.ScrollToTop();
                    }
                    
                    if (_projectionVisualBrushRect != null)
                    {
                        // 清空旧的 VisualBrush，断开绑定
                        _projectionVisualBrushRect.Fill = null;
                        _projectionVisualBrushRect.Visibility = Visibility.Collapsed;
                        _projectionVisualBrushRect.Width = double.NaN;
                        _projectionVisualBrushRect.Height = double.NaN;
                    }
                    
                    if (_projectionContainer != null)
                    {
                        // 重置容器高度
                        _projectionContainer.Height = double.NaN;
                    }
                    
                    // 强制投影窗口更新布局，清除缓存
                    if (_projectionScrollViewer != null)
                    {
                        _projectionScrollViewer.UpdateLayout();
                    }
                    
                    // 隐藏图片投影控件
                    if (_projectionImageControl != null)
                        _projectionImageControl.Visibility = Visibility.Collapsed;
                });
                
                //  第二步：同步创建新的 VisualBrush
                RunOnMainDispatcher(() =>
                {
                    //  强制更新主屏幕布局
                    bibleScrollViewer.UpdateLayout();
                    
                    if (_projectionVisualBrushRect != null)
                    {
                        //  获取 ScrollViewer 的内容（StackPanel），不包含滚动条
                        var mainContent = bibleScrollViewer.Content as UIElement;
                        if (mainContent == null)
                            return;
                        
                        //  再次强制更新，确保内容已完全渲染
                        mainContent.UpdateLayout();
                        
                        //  验证内容尺寸是否有效（避免使用未渲染完成的尺寸）
                        if (mainContent.RenderSize.Width < 10 || mainContent.RenderSize.Height < 10)
                            return;
                        
                        //  获取投影屏幕尺寸（转换为WPF设备独立单位，考虑DPI缩放）
                        var projectionSize = GetCurrentProjectionSize();
                        double projectionWidth = projectionSize.width;
                        double projectionHeight = projectionSize.height;
                        
                        //  计算缩放比例（水平拉伸填满）
                        double scaleRatio = projectionWidth / mainContent.RenderSize.Width;
                        double scaledHeight = mainContent.RenderSize.Height * scaleRatio;
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [经文投影-DPI] 投影屏幕WPF单位: {projectionWidth}×{projectionHeight}");
                        //System.Diagnostics.Debug.WriteLine($" [经文投影-DPI] 源内容尺寸: {mainContent.RenderSize.Width:F1}×{mainContent.RenderSize.Height:F1}");
                        //System.Diagnostics.Debug.WriteLine($" [经文投影-DPI] 缩放比例: {scaleRatio:F3}");
                        //#endif
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [RenderTargetBitmap] 开始独立渲染");
                        //#endif
                        
                        //  使用 RenderTargetBitmap "拍照"主屏幕内容（独立渲染，不受缩放插值影响）
                        int renderWidth = (int)mainContent.RenderSize.Width;
                        int renderHeight = (int)mainContent.RenderSize.Height;
                        
                        var renderBitmap = new RenderTargetBitmap(
                            renderWidth,
                            renderHeight,
                            96, // DPI X
                            96, // DPI Y
                            PixelFormats.Pbgra32);
                        
                        renderBitmap.Render(mainContent);
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [RenderTargetBitmap] 拍照完成: {renderWidth}×{renderHeight}");
                        //#endif
                        
                        //  使用 Image 控件显示位图（替代 VisualBrush）
                        if (_projectionImageControl != null)
                        {
                            _projectionImageControl.Source = renderBitmap;
                            _projectionImageControl.Stretch = System.Windows.Media.Stretch.Fill;
                            _projectionImageControl.Width = projectionWidth;
                            _projectionImageControl.Height = scaledHeight;
                            _projectionImageControl.HorizontalAlignment = WpfHorizontalAlignment.Left;
                            _projectionImageControl.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                            _projectionImageControl.Visibility = Visibility.Visible;
                            
                            //  设置高质量缩放
                            RenderOptions.SetBitmapScalingMode(_projectionImageControl, BitmapScalingMode.HighQuality);
                            RenderOptions.SetEdgeMode(_projectionImageControl, EdgeMode.Aliased);
                        }
                        
                        //  隐藏 VisualBrush 矩形
                        if (_projectionVisualBrushRect != null)
                        {
                            _projectionVisualBrushRect.Visibility = Visibility.Collapsed;
                            _projectionVisualBrushRect.Fill = null; // 清除旧的 VisualBrush 绑定
                        }
                        
                        //  配置投影窗口滚动条（隐藏滚动条）
                        if (_projectionScrollViewer != null)
                        {
                            _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                            _projectionScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
                            
                            //  强制立即更新布局，确保ExtentHeight等属性准确
                            _projectionScrollViewer.UpdateLayout();
                            
                            //  同步滚动位置
                            if (_projectionScrollViewer != null && _projectionContainer != null && bibleScrollViewer.ExtentHeight > 0)
                            {
                                double scrollRatio = bibleScrollViewer.VerticalOffset / bibleScrollViewer.ExtentHeight;
                                double projScrollOffset = scrollRatio * _projectionScrollViewer.ExtentHeight;
                                _projectionScrollViewer.ScrollToVerticalOffset(projScrollOffset);
                            }
                        }
                    }
                });
            }
            catch (Exception)
            {
                // 静默处理异常
            }
        }
        
        /// <summary>
        ///  使用 VisualBrush 更新视频背景投影（镜像主屏视频 + 叠加文本层）
        /// </summary>
        /// <param name="videoVisualBrush">主屏幕视频的 VisualBrush</param>
        /// <param name="textLayer">文本层（透明背景的 SKBitmap）</param>
        public void UpdateProjectionWithVideo(VisualBrush videoVisualBrush, SKBitmap textLayer)
        {
            if (_projectionWindow == null || videoVisualBrush == null)
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [PM-视频投影] 投影窗口或 VisualBrush 为空，跳过");
#endif
                return;
            }

            try
            {
#if DEBUG
                var totalTime = System.Diagnostics.Stopwatch.StartNew();
#endif

                RunOnMainDispatcher(() =>
                {
#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [PM-视频投影] ===== ProjectionManager.UpdateProjectionWithVideo 开始 =====");
                    var step1Time = System.Diagnostics.Stopwatch.StartNew();
#endif

                    //  步骤1：设置视频背景（使用 VisualBrush）
                    if (_projectionVisualBrushRect != null)
                    {
                        _projectionVisualBrushRect.Fill = videoVisualBrush;
                        _projectionVisualBrushRect.Visibility = Visibility.Visible;
                        _projectionVisualBrushRect.Width = _projectionWindow.ActualWidth;
                        _projectionVisualBrushRect.Height = _projectionWindow.ActualHeight;
                        
                        //  GPU 加速优化：启用缓存并使用低质量缩放提升性能
                        RenderOptions.SetCachingHint(_projectionVisualBrushRect, CachingHint.Cache);  // 启用GPU缓存
                        RenderOptions.SetBitmapScalingMode(_projectionVisualBrushRect, BitmapScalingMode.LowQuality);  // 优先性能
                        
#if DEBUG
                        step1Time.Stop();
                        //System.Diagnostics.Debug.WriteLine($" [PM-视频投影] 步骤1完成：设置 VisualBrush，尺寸: {_projectionVisualBrushRect.Width}x{_projectionVisualBrushRect.Height} (耗时: {step1Time.ElapsedMilliseconds} ms)");
                        //System.Diagnostics.Debug.WriteLine($" [PM-视频投影-GPU加速] BitmapCache: {(_projectionVisualBrushRect.CacheMode != null ? "已启用" : "未启用")}");
                        //System.Diagnostics.Debug.WriteLine($" [PM-视频投影-GPU加速] CachingHint: {RenderOptions.GetCachingHint(_projectionVisualBrushRect)}, BitmapScalingMode: {RenderOptions.GetBitmapScalingMode(_projectionVisualBrushRect)}");
#endif
                    }

                    //  步骤2：叠加文本层
                    if (textLayer != null)
                    {
#if DEBUG
                        var step2Time = System.Diagnostics.Stopwatch.StartNew();
#endif
                        var textBitmapSource = ConvertToBitmapSource(textLayer);
                        
                        if (textBitmapSource != null && _projectionImageControl != null)
                        {
                            _projectionImageControl.Source = textBitmapSource;
                            _projectionImageControl.Width = _projectionWindow.ActualWidth;
                            _projectionImageControl.Height = _projectionWindow.ActualHeight;
                            _projectionImageControl.Stretch = System.Windows.Media.Stretch.Fill;
                            _projectionImageControl.HorizontalAlignment = WpfHorizontalAlignment.Center;
                            _projectionImageControl.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                            _projectionImageControl.Visibility = Visibility.Visible;  //  确保可见
                            
#if DEBUG
                            step2Time.Stop();
                            //System.Diagnostics.Debug.WriteLine($" [PM-视频投影] 步骤2完成：叠加文本层，尺寸: {textLayer.Width}x{textLayer.Height} (耗时: {step2Time.ElapsedMilliseconds} ms)");
                            //System.Diagnostics.Debug.WriteLine($" [PM-视频投影] 文本层可见性: {_projectionImageControl.Visibility}, ZIndex: {System.Windows.Controls.Panel.GetZIndex(_projectionImageControl)}");
                            //System.Diagnostics.Debug.WriteLine($" [PM-视频投影] 视频层ZIndex: {System.Windows.Controls.Panel.GetZIndex(_projectionVisualBrushRect)}");
#endif
                        }
                        //#if DEBUG
                        //else
                        //{
                        //    if (textBitmapSource == null)
                        //        System.Diagnostics.Debug.WriteLine($" [PM-视频投影] 文本层位图转换失败");
                        //    if (_projectionImageControl == null)
                        //        System.Diagnostics.Debug.WriteLine($" [PM-视频投影] 投影Image控件为空");
                        //}
                        //#endif
                    }
                    //#if DEBUG
                    //else
                    //{
                    //    System.Diagnostics.Debug.WriteLine($" [PM-视频投影] textLayer 为空，跳过文本层叠加");
                    //}
                    //#endif

#if DEBUG
                    totalTime.Stop();
                    //System.Diagnostics.Debug.WriteLine($" [PM-视频投影] 视频投影更新完成");
                    //System.Diagnostics.Debug.WriteLine($"⏱ [PM-视频投影] 总耗时: {totalTime.ElapsedMilliseconds} ms");
                    //System.Diagnostics.Debug.WriteLine($" [PM-视频投影] ===== 完成 =====\n");
#endif
                });
            }
            catch (Exception
#if DEBUG
                ex
#endif
            )
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [PM-视频投影] 更新失败: {ex.Message}");
                _ = ex;
#endif
            }
        }
        
        /// <summary>
        ///  锁定模式：使用 VLC D3D11 渲染器播放视频（解决 Airspace 问题）
        /// </summary>
        /// <param name="videoPath">视频文件路径</param>
        /// <param name="loopEnabled">是否循环播放</param>
        /// <param name="textLayer">文本层（透明背景的 SKBitmap）</param>
        public void UpdateProjectionWithLockedVideo(string videoPath, bool loopEnabled, SKBitmap textLayer)
        {
            //  使用 D3D11 渲染器实现（解决 Airspace 问题）
            UpdateProjectionWithLockedVideoD3D11(videoPath, loopEnabled, textLayer);
            
            // 保留旧的 MediaElement 实现作为备份（如果需要可以切换回来）
            // UpdateProjectionWithLockedVideoMediaElement(videoPath, loopEnabled, textLayer);
        }
        
        /// <summary>
        ///  旧的 MediaElement 实现（保留作为备份）
        /// </summary>
        private void UpdateProjectionWithLockedVideoMediaElement(string videoPath, bool loopEnabled, SKBitmap textLayer)
        {
#if DEBUG
            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] ===== 开始 =====");
            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] videoPath: {videoPath ?? "null"}");
            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] loopEnabled: {loopEnabled}");
            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] textLayer: {(textLayer != null ? $"{textLayer.Width}x{textLayer.Height}" : "null")}");
            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] _projectionWindow: {(_projectionWindow != null ? "存在" : "null")}");
            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 文件存在: {(!string.IsNullOrEmpty(videoPath) ? System.IO.File.Exists(videoPath).ToString() : "N/A")}");
            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] _projectionMediaElement: {(_projectionMediaElement != null ? "存在" : "null")}");
            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] _lockedVideoPath: {_lockedVideoPath ?? "null"}");
#endif
            
            if (_projectionWindow == null || string.IsNullOrEmpty(videoPath) || !System.IO.File.Exists(videoPath))
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 投影窗口或视频路径无效，跳过");
                //if (_projectionWindow == null)
                //    System.Diagnostics.Debug.WriteLine($"   - _projectionWindow 为 null");
                //if (string.IsNullOrEmpty(videoPath))
                //    System.Diagnostics.Debug.WriteLine($"   - videoPath 为空");
                //if (!string.IsNullOrEmpty(videoPath) && !System.IO.File.Exists(videoPath))
                //    System.Diagnostics.Debug.WriteLine($"   - 文件不存在: {videoPath}");
#endif
                return;
            }

            RunOnMainDispatcher(() =>
            {
                try
                {
                    // 如果视频路径改变，创建新的 MediaElement
                    if (_projectionMediaElement == null || _lockedVideoPath != videoPath)
                    {
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 创建新的 MediaElement");
                        //System.Diagnostics.Debug.WriteLine($"   - _projectionMediaElement: {(_projectionMediaElement == null ? "null" : "存在")}");
                        //System.Diagnostics.Debug.WriteLine($"   - _lockedVideoPath: {_lockedVideoPath ?? "null"}");
                        //System.Diagnostics.Debug.WriteLine($"   - videoPath: {videoPath}");
                        //System.Diagnostics.Debug.WriteLine($"   - 路径是否改变: {_lockedVideoPath != videoPath}");
#endif
                        // 释放旧的 MediaElement
                        if (_projectionMediaElement != null)
                        {
#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 释放旧的 MediaElement");
#endif
                            _projectionMediaElement.Stop();
                            _projectionMediaElement.Close();
                            if (_projectionContainer != null && _projectionContainer.Children.Contains(_projectionMediaElement))
                            {
                                _projectionContainer.Children.Remove(_projectionMediaElement);
                            }
                        }

                        // 创建新的 MediaElement（应用所有 GPU 优化）
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 创建新的 MediaElement");
#endif
                        _projectionMediaElement = new System.Windows.Controls.MediaElement
                        {
                            LoadedBehavior = System.Windows.Controls.MediaState.Manual,
                            UnloadedBehavior = System.Windows.Controls.MediaState.Manual,
                            Stretch = System.Windows.Media.Stretch.Uniform,  //  改为 Uniform，和主屏幕一致
                            Volume = 0,
                            ScrubbingEnabled = true,
                            
                            //  启用 GPU 硬件加速缓存（和主屏幕 MediaElement 一样）
                            CacheMode = new BitmapCache
                            {
                                EnableClearType = false,  // 视频不需要ClearType
                                RenderAtScale = 1.0,      // 1080p适配，减少GPU内存占用
                                SnapsToDevicePixels = true
                            }
                        };
                        
                        //  设置 GPU 渲染优化（和主屏幕 MediaElement 一样）
                        RenderOptions.SetBitmapScalingMode(_projectionMediaElement, BitmapScalingMode.LowQuality);  // 优先性能
                        RenderOptions.SetCachingHint(_projectionMediaElement, CachingHint.Cache);  // 强制启用缓存
                        
                        // 设置循环播放
                        if (loopEnabled)
                        {
                            _projectionMediaElement.MediaEnded += (s, e) =>
                            {
                                _projectionMediaElement.Position = TimeSpan.Zero;
                                _projectionMediaElement.Play();
                            };
                        }

                        // 添加到投影窗口（必须在设置 Source 之前添加到视觉树）
                        if (_projectionContainer != null)
                        {
                            System.Windows.Controls.Canvas.SetZIndex(_projectionMediaElement, 0);
                            
                            //  设置 MediaElement 的布局属性（参考主屏幕的实现）
                            // MediaElement 在 Grid 中，使用 Stretch 自动填充
                            _projectionMediaElement.HorizontalAlignment = WpfHorizontalAlignment.Stretch;
                            _projectionMediaElement.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
                            _projectionMediaElement.Visibility = Visibility.Visible;  //  立即显示，和 VisualBrush 重叠
                            
                            //  关键优化：MediaElement 和 VisualBrush 同时显示，MediaOpened 后再隐藏 VisualBrush
                            // 这样可以避免切换时的卡顿（无缝切换）
                            
                            // 添加到容器（Grid 会自动让子元素填充）
                            _projectionContainer.Children.Insert(0, _projectionMediaElement);
#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] MediaElement 已添加到投影窗口");
                            //System.Diagnostics.Debug.WriteLine($"   - 窗口尺寸: {_projectionWindow.ActualWidth}x{_projectionWindow.ActualHeight}");
                            //System.Diagnostics.Debug.WriteLine($"   - MediaElement 尺寸: {_projectionMediaElement.Width}x{_projectionMediaElement.Height}");
                            //System.Diagnostics.Debug.WriteLine($"   - MediaElement 实际尺寸: {_projectionMediaElement.ActualWidth}x{_projectionMediaElement.ActualHeight}");
                            //System.Diagnostics.Debug.WriteLine($"   - 可见性: {_projectionMediaElement.Visibility}");
                            //System.Diagnostics.Debug.WriteLine($"   - ZIndex: {System.Windows.Controls.Canvas.GetZIndex(_projectionMediaElement)}");
                            //System.Diagnostics.Debug.WriteLine($"   - 容器子元素数量: {_projectionContainer.Children.Count}");
                            //System.Diagnostics.Debug.WriteLine($"   - IsLoaded: {_projectionMediaElement.IsLoaded}");
#endif
                            
                            //  直接设置 Source 并调用 Play()（参考主屏幕的实现）
                            //  关键差异分析：
                            // 1. 主屏幕的 MediaElement 在 Canvas 中，直接设置 Source → 添加到 Canvas → 调用 Play()
                            // 2. 投影的 MediaElement 在 Grid 中，应该采用相同的逻辑
                            // 3. 主屏幕没有等待 MediaOpened 事件，直接调用 Play()
                            _projectionMediaElement.Source = new Uri(videoPath, UriKind.Absolute);
                            
                            //  直接调用 Play()，不等待 MediaOpened 事件（和主屏幕一致）
                            _projectionMediaElement.Play();
                            
#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] Source 已设置并调用 Play()");
                            //System.Diagnostics.Debug.WriteLine($"   - 视频路径: {videoPath}");
                            //System.Diagnostics.Debug.WriteLine($"   - MediaElement 实际尺寸: {_projectionMediaElement.ActualWidth}x{_projectionMediaElement.ActualHeight}");
#endif
                        }
#if DEBUG
                        //else
                        //{
                        //    System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] _projectionContainer 为 null，无法添加 MediaElement");
                        //}
#endif

                        //  监听 MediaOpened 事件：视频加载完成后隐藏 VisualBrush（MediaElement 已经显示）
                        _projectionMediaElement.MediaOpened += (s, e) =>
                        {
#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] MediaElement MediaOpened 事件触发");
                            //System.Diagnostics.Debug.WriteLine($"   - 分辨率: {_projectionMediaElement.NaturalVideoWidth}x{_projectionMediaElement.NaturalVideoHeight}");
                            //System.Diagnostics.Debug.WriteLine($"   - 时长: {_projectionMediaElement.NaturalDuration.TimeSpan.TotalSeconds:F1}秒");
                            //System.Diagnostics.Debug.WriteLine($"   - 当前状态: {_projectionMediaElement.LoadedBehavior}");
                            //System.Diagnostics.Debug.WriteLine($"   - 尺寸: {_projectionMediaElement.ActualWidth}x{_projectionMediaElement.ActualHeight}");
#endif
                            //  视频加载完成后，隐藏 VisualBrush（MediaElement 已经显示，无缝切换）
                            if (_projectionVisualBrushRect != null)
                            {
                                _projectionVisualBrushRect.Visibility = Visibility.Collapsed;
                            }
#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 视频已加载，隐藏 VisualBrush");
#endif
                        };
                        
                        _projectionMediaElement.MediaFailed += (s, e) =>
                        {
#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] MediaElement 加载失败");
                            //System.Diagnostics.Debug.WriteLine($"   - 错误: {e.ErrorException?.Message ?? "未知错误"}");
                            //System.Diagnostics.Debug.WriteLine($"   - 错误代码: {e.ErrorException?.GetType().Name ?? "N/A"}");
                            //System.Diagnostics.Debug.WriteLine($"   - Source: {_projectionMediaElement.Source}");
                            //System.Diagnostics.Debug.WriteLine($"   - 实际尺寸: {_projectionMediaElement.ActualWidth}x{_projectionMediaElement.ActualHeight}");
                            //System.Diagnostics.Debug.WriteLine($"   - 可见性: {_projectionMediaElement.Visibility}");
                            //if (e.ErrorException != null)
                            //{
                            //    System.Diagnostics.Debug.WriteLine($"   - 堆栈: {e.ErrorException.StackTrace}");
                            //}
#endif
                        };
                        
                        // 添加加载状态监控
#if DEBUG
                        //_projectionMediaElement.Loaded += (s, e) =>
                        //{
                        //    System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] MediaElement Loaded 事件触发");
                        //};
                        
                        //_projectionMediaElement.Unloaded += (s, e) =>
                        //{
                        //    System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] MediaElement Unloaded 事件触发");
                        //};
#endif

                        _lockedVideoPath = videoPath;

#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 创建独立 MediaElement（已应用 GPU 优化）: {System.IO.Path.GetFileName(videoPath)}");
                        //System.Diagnostics.Debug.WriteLine($"   - 循环播放: {loopEnabled}");
                        //System.Diagnostics.Debug.WriteLine($"   - 已添加到投影窗口: {(_projectionContainer != null && _projectionContainer.Children.Contains(_projectionMediaElement))}");
#endif
                    }
#if DEBUG
                    //else
                    //{
                    //    System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] MediaElement 已存在且路径未改变，跳过创建");
                    //}
#endif

                    // 叠加文本层（如果提供）
                    if (textLayer != null)
                    {
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 叠加文本层: {textLayer.Width}x{textLayer.Height}");
#endif
                        var textBitmapSource = ConvertToBitmapSource(textLayer);
                        if (textBitmapSource != null && _projectionImageControl != null)
                        {
                            _projectionImageControl.Source = textBitmapSource;
                            _projectionImageControl.Visibility = Visibility.Visible;
                            System.Windows.Controls.Canvas.SetZIndex(_projectionImageControl, 1);
#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 文本层已叠加");
#endif
                        }
#if DEBUG
                        //else
                        //{
                        //    System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 文本层叠加失败");
                        //    System.Diagnostics.Debug.WriteLine($"   - textBitmapSource: {(textBitmapSource != null ? "存在" : "null")}");
                        //    System.Diagnostics.Debug.WriteLine($"   - _projectionImageControl: {(_projectionImageControl != null ? "存在" : "null")}");
                        //}
#endif
                    }
                    
                    //  VisualBrush 矩形已在添加 MediaElement 时隐藏，这里不需要重复操作
                    
                    //  确保 MediaElement 在最底层，文本层在上层
                    if (_projectionMediaElement != null && _projectionImageControl != null)
                    {
                        System.Windows.Controls.Canvas.SetZIndex(_projectionMediaElement, 0);
                        System.Windows.Controls.Canvas.SetZIndex(_projectionImageControl, 1);
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] ZIndex 设置完成");
                        //System.Diagnostics.Debug.WriteLine($"   - MediaElement ZIndex: {System.Windows.Controls.Canvas.GetZIndex(_projectionMediaElement)}");
                        //System.Diagnostics.Debug.WriteLine($"   - ImageControl ZIndex: {System.Windows.Controls.Canvas.GetZIndex(_projectionImageControl)}");
#endif
                    }
                    
#if DEBUG
                    // 延迟检查 MediaElement 状态（等待布局完成）
                    //System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
                    //{
                    //    _mainWindow.Dispatcher.Invoke(() =>
                    //    {
                    //        if (_projectionMediaElement != null)
                    //        {
                    //            System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] 延迟检查 MediaElement 状态");
                    //            System.Diagnostics.Debug.WriteLine($"   - 可见性: {_projectionMediaElement.Visibility}");
                    //            System.Diagnostics.Debug.WriteLine($"   - 实际尺寸: {_projectionMediaElement.ActualWidth}x{_projectionMediaElement.ActualHeight}");
                    //            System.Diagnostics.Debug.WriteLine($"   - 是否在容器中: {(_projectionContainer != null && _projectionContainer.Children.Contains(_projectionMediaElement))}");
                    //            System.Diagnostics.Debug.WriteLine($"   - 是否已加载: {_projectionMediaElement.IsLoaded}");
                    //            System.Diagnostics.Debug.WriteLine($"   - NaturalDuration: {(_projectionMediaElement.NaturalDuration.HasTimeSpan ? _projectionMediaElement.NaturalDuration.TimeSpan.TotalSeconds.ToString("F1") + "秒" : "未就绪")}");
                    //        }
                    //    });
                    //});
                    
                    //System.Diagnostics.Debug.WriteLine($" [UpdateProjectionWithLockedVideo] ===== 完成 =====");
#endif
                }
                catch (Exception
#if DEBUG
                    ex
#endif
                )
                {
#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [锁定视频] 更新失败: {ex.Message}");
                    _ = ex;
#endif
                }
            });
        }
        
        /// <summary>
        /// 获取锁定模式下的视频路径
        /// </summary>
        public string GetLockedVideoPath()
        {
            return _lockedVideoPath;
        }
        
        /// <summary>
        /// 清理锁定模式的视频资源
        /// </summary>
        public void ClearLockedVideo()
        {
            if (_projectionWindow == null)
                return;

            TryRunOnProjectionDispatcher(() =>
            {
                try
                {
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($" [ClearLockedVideo] 清除锁定视频");
//#endif

                    // 停止 VLC 播放器
                    if (_projectionVlcPlayer != null)
                    {
                        _projectionVlcPlayer.Stop();
                    }

                    // 清除 D3D11 渲染器
                    if (_projectionVlcRenderer != null)
                    {
                        _projectionVlcRenderer.Dispose();
                        _projectionVlcRenderer = null;
                    }

                    // 清理旧的 MediaElement（如果存在）
                    if (_projectionMediaElement != null)
                    {
                        _projectionMediaElement.Stop();
                        _projectionMediaElement.Close();
                        if (_projectionContainer != null && _projectionContainer.Children.Contains(_projectionMediaElement))
                        {
                            _projectionContainer.Children.Remove(_projectionMediaElement);
                        }
                        _projectionMediaElement = null;
                    }

                    // 隐藏视频容器
                    if (_projectionVideoContainer != null)
                    {
                        _projectionVideoContainer.Visibility = Visibility.Collapsed;
                    }

                    // 清除 Image 源
                    if (_projectionVideoImage != null)
                    {
                        _projectionVideoImage.Source = null;
                    }

                    _lockedVideoPath = null;

//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($" 锁定视频已清除");
//#endif
                }
                catch (Exception ex)
                {
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($" [ClearLockedVideo] 错误: {ex.Message}");
//#else
                    _ = ex; // 避免未使用变量警告
//#endif
                }
            });
        }
        
        #endregion

        #region IDisposable

        public void Dispose()
        {
            //  优化4：统一清理 D3D11 资源
            DisposeD3D11Resources();
            
            CloseProjection();
            
            // 释放投影缓存
            if (_projectionCache is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        #endregion
    }
}



