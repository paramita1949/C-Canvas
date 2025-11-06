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
using ImageColorChanger.UI;
using SkiaSharp;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfResizeMode = System.Windows.ResizeMode;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using Screen = System.Windows.Forms.Screen;
using LibVLCSharp.WPF;
using Microsoft.Extensions.Caching.Memory;
using ImageColorChanger.Services;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影管理器
    /// 负责管理投影窗口、多屏幕支持、同步逻辑等功能
    /// </summary>
    public class ProjectionManager : IDisposable
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
        private readonly System.Windows.Controls.ComboBox _screenComboBox;

        // 投影窗口相关
        private Window _projectionWindow;
        private ScrollViewer _projectionScrollViewer;
        private Grid _projectionContainer;  // 容器Grid,用于控制滚动区域
        private System.Windows.Controls.Image _projectionImageControl;
        private BitmapSource _projectionImage;
        
        // 视频投影相关
        private Grid _projectionVideoContainer;  // 视频容器
        private VideoView _projectionVideoView;  // 视频视图
        private Grid _projectionMediaFileNameBorder;  // 媒体文件名容器
        private TextBlock _projectionMediaFileNameText;  // 媒体文件名文本
        
        // 圣经投影相关
        private Border _projectionBibleTitleBorder;  // 圣经标题容器（固定在顶部）
        private TextBlock _projectionBibleTitleText;  // 圣经标题文本

        // 屏幕管理
        private List<Screen> _screens;
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
        private OriginalDisplayMode _originalDisplayMode = OriginalDisplayMode.Stretch;
        private string _currentImagePath; // 用于缓存键生成
        
        // ⚡ 投影图片缓存
        private readonly IMemoryCache _projectionCache;
        
        // ⚡ 预渲染状态
        private bool _isPreRendering = false;
        private readonly object _preRenderLock = new object();
        
        // ⚡ 共享渲染缓存
        private BitmapSource _lastSharedBitmap = null;
        
        // 📊 共享渲染验证计数
        #if DEBUG
        private int _scrollVerifyCount = 0;
        #endif
        
        // 🔒 投影时间限制（未登录状态）
        private DateTime? _projectionStartTime;
        private System.Threading.Timer _projectionTimer;
        private long _projectionStartTick; // 使用 TickCount64 防篡改
        private string _localProjectionChecksum; // 本地校验和（额外防护层）
        
        /// <summary>
        /// 获取试用时长限制（随机30-60秒）
        /// 随机化使得破解者无法预测具体时长
        /// </summary>
        private int GetTrialDurationSeconds()
        {
            // 基于硬件ID生成伪随机数，保证每台电脑相对固定，但不同电脑不同
            var hardwareId = Services.AuthService.Instance.Username ?? Environment.MachineName;
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
                // 返回ScrollViewer的实际尺寸（已考虑DPI缩放）
                return (_projectionScrollViewer.ActualWidth, _projectionScrollViewer.ActualHeight);
            }
            
            if (_screens != null && _currentScreenIndex >= 0 && _currentScreenIndex < _screens.Count)
            {
                // 返回屏幕的物理尺寸
                var screen = _screens[_currentScreenIndex];
                return (screen.Bounds.Width, screen.Bounds.Height);
            }
            
            // 默认返回1920x1080
            return (1920, 1080);
        }

        public ProjectionManager(
            Window mainWindow,
            ScrollViewer mainScrollViewer,
            System.Windows.Controls.Image mainImageControl,
            ImageProcessor imageProcessor,
            System.Windows.Controls.ComboBox screenComboBox)
        {
            _mainWindow = mainWindow;
            _mainScrollViewer = mainScrollViewer;
            _mainImageControl = mainImageControl;
            _imageProcessor = imageProcessor;
            _screenComboBox = screenComboBox;

            _screens = new List<Screen>();
            _currentScreenIndex = 0;
            
            // ⚡ 初始化投影图片缓存
            _projectionCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 50, // 最多缓存50张投影图片（基于权重计算）
                CompactionPercentage = 0.25, // 达到上限时清理25%最少使用的项
                ExpirationScanFrequency = TimeSpan.FromMinutes(5) // 每5分钟扫描过期项
            });
            _syncEnabled = false;
            // _globalHotkeysEnabled = false; // TODO: 实现全局热键时再启用
            _lastSyncTime = DateTime.Now;

            InitializeScreenInfo();
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

        #region 初始化

        /// <summary>
        /// 初始化屏幕信息
        /// </summary>
        private void InitializeScreenInfo()
        {
            try
            {
                _screens = Screen.AllScreens.ToList();
                
                if (_screens.Count == 0)
                {
                    // 回退到主屏幕
                    _screens.Add(Screen.PrimaryScreen);
                }

                UpdateScreenComboBox();
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"初始化屏幕信息失败: {ex.Message}");
                _screens.Add(Screen.PrimaryScreen);
            }
        }

        /// <summary>
        /// 更新屏幕下拉框
        /// </summary>
        private void UpdateScreenComboBox()
        {
            if (_screenComboBox == null) return;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                _screenComboBox.Items.Clear();

                for (int i = 0; i < _screens.Count; i++)
                {
                    var screen = _screens[i];
                    string name = screen.Primary ? "主显示器" : $"显示器{i + 1}";
                    _screenComboBox.Items.Add(name);
                }

                // 默认选择第一个非主显示器（扩展屏）
                int defaultIndex = 0;
                for (int i = 0; i < _screens.Count; i++)
                {
                    if (!_screens[i].Primary)
                    {
                        defaultIndex = i;
                        break;
                    }
                }

                if (_screenComboBox.Items.Count > 0)
                {
                    _screenComboBox.SelectedIndex = defaultIndex;
                }
            });
        }

        #endregion

        #region 公有API方法

        /// <summary>
        /// 切换投影显示状态
        /// </summary>
        public bool ToggleProjection()
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine("🎬 ToggleProjection 被调用");
                // System.Diagnostics.Debug.WriteLine($"当前投影窗口状态: {(_projectionWindow != null ? "已打开" : "未打开")}");
                // System.Diagnostics.Debug.WriteLine($"当前图片: {(_currentImage != null ? $"{_currentImage.Width}x{_currentImage.Height}" : "null")}");
                // System.Diagnostics.Debug.WriteLine($"屏幕数量: {_screens.Count}");
                
                if (_projectionWindow != null)
                {
                    // System.Diagnostics.Debug.WriteLine("关闭投影窗口");
                    return CloseProjection();
                }
                else
                {
                    // 🔒 后台静默验证投影权限（仅在有网络时执行，不阻塞UI）
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
                                    //System.Diagnostics.Debug.WriteLine($"ℹ️ [投影] 检测到网络连接，开始后台验证");
                                    //#endif
                                    
                                    var (allowed, message) = await AuthService.Instance.VerifyProjectionPermissionAsync();
                                    
                                    //#if DEBUG
                                    //System.Diagnostics.Debug.WriteLine($"ℹ️ [投影] 后台网络验证结果: {message}（allowed={allowed}）");
                                    //#endif
                                    
                                    // 🔒 后台静默记录验证结果，不影响试用
                                    // 验证目的：防止破解者绕过登录，但不阻止正常试用
                                }
                                catch
                                {
                                    // 无网络，跳过验证
                                    //#if DEBUG
                                    //System.Diagnostics.Debug.WriteLine($"ℹ️ [投影] 无网络连接，跳过后台验证");
                                    //#endif
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"⚠️ [投影] 后台验证异常: {ex.Message}");
                            //#else
                            _ = ex; // 避免未使用变量警告
                            //#endif
                        }
                    });
                    
                    // 🔒 检查账号验证状态
                    if (!AuthService.Instance.IsAuthenticated)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine("⚠️ [投影] 未登录，将启用随机试用限制");
                        //#endif
                        
                        // 未登录，静默启用随机试用限制（不弹窗）
                        // 时长由 GetTrialDurationSeconds() 随机决定（30-60秒）
                    }
                    else if (!AuthService.Instance.CanUseProjection())
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine("❌ [投影] 账号已过期");
                        //#endif
                        
                        WpfMessageBox.Show(
                            "您的账号已过期，无法使用投影功能。\n请联系管理员续费。",
                            "账号已过期",
                            WpfMessageBoxButton.OK,
                            WpfMessageBoxImage.Warning);
                        return false;
                    }
                    
                    // System.Diagnostics.Debug.WriteLine("打开投影窗口");
                    return OpenProjection();
                }
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 切换投影失败: {ex.Message}");
                // System.Diagnostics.Debug.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                WpfMessageBox.Show($"投影失败: {ex.Message}", "错误", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 清空图片投影状态（用于切换到纯文字模式时）
        /// </summary>
        public void ClearImageState()
        {
//#if DEBUG
//            System.Diagnostics.Debug.WriteLine("🧹 [投影] 清空图片状态");
//#endif
            // 清空当前图片引用，防止图片投影逻辑干扰文字投影
            _currentImage = null;
            _currentImagePath = null;
            _isColorEffectEnabled = false;
            _zoomRatio = 1.0;
        }

        /// <summary>
        /// 同步歌词滚动位置到投影
        /// </summary>
        public void SyncLyricsScroll(ScrollViewer lyricsScrollViewer)
        {
            if (!_syncEnabled || _projectionWindow == null || lyricsScrollViewer == null)
                return;

            try
            {
                // 性能节流
                var currentTime = DateTime.Now;
                if (currentTime - _lastSyncTime < _syncThrottleInterval)
                    return;
                _lastSyncTime = currentTime;

                _mainWindow.Dispatcher.Invoke(() =>
                {
                    if (_projectionScrollViewer == null)
                        return;

                    // 🔧 歌词滚动同步：直接使用主屏滚动位置（两者内容高度相同）
                    double mainScrollTop = lyricsScrollViewer.VerticalOffset;
                    
                    // 🔧 关键：直接使用相同的滚动位置（因为两者渲染的是相同内容）
                    // 不需要比例计算，因为主屏和投影的内容高度是一样的
                    double projScrollTop = mainScrollTop;
                    
                    _projectionScrollViewer.ScrollToVerticalOffset(projScrollTop);

//#if DEBUG
//                    double mainScrollableHeight = lyricsScrollViewer.ScrollableHeight;
//                    double projScrollableHeight = _projectionScrollViewer.ScrollableHeight;
//                    double mainViewportHeight = lyricsScrollViewer.ViewportHeight;
//                    double projViewportHeight = _projectionScrollViewer.ViewportHeight;
//                    double mainExtentHeight = lyricsScrollViewer.ExtentHeight;
//                    double projExtentHeight = _projectionScrollViewer.ExtentHeight;
//                    
//                    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 滚动偏移: {projScrollTop:F2} (同步自主屏 {mainScrollTop:F2})");
//                    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 可滚动高度: {projScrollableHeight:F2} (主屏: {mainScrollableHeight:F2})");
//                    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 视口高度: {projViewportHeight:F2} (主屏: {mainViewportHeight:F2})");
//                    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 内容总高度: {projExtentHeight:F2} (主屏: {mainExtentHeight:F2})");
//                    
//                    if (_projectionBibleTitleBorder != null)
//                    {
//                        System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 固定标题高度: {_projectionBibleTitleBorder.ActualHeight:F2}");
//                        System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 固定标题可见: {_projectionBibleTitleBorder.Visibility}");
//                    }
//                    
//                    // 🔍 关键对比：主屏第一节经文顶部位置 vs 投影第一节经文顶部位置
//                    // 主屏：ScrollViewer.Padding.Top (应该是20)
//                    // 投影：渲染内容的顶部Padding (应该也是20)
//                    System.Diagnostics.Debug.WriteLine($"🔍 [对比] 内容高度差异: {projExtentHeight - mainExtentHeight:F2}");
//                    System.Diagnostics.Debug.WriteLine($"🔍 [对比] 可滚动高度差异: {projScrollableHeight - mainScrollableHeight:F2}");
//                    System.Diagnostics.Debug.WriteLine($"🔍 ========================");
//#endif
                });
            }
            catch (Exception)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"❌ [歌词滚动同步] 失败: {ex.Message}");
//#endif
            }
        }

        /// <summary>
        /// 同步圣经滚动位置到投影（与歌词完全一致）
        /// </summary>
        public void SyncBibleScroll(ScrollViewer bibleScrollViewer)
        {
            if (!_syncEnabled || _projectionWindow == null || bibleScrollViewer == null)
                return;

            try
            {
                // 性能节流
                var currentTime = DateTime.Now;
                if (currentTime - _lastSyncTime < _syncThrottleInterval)
                    return;
                _lastSyncTime = currentTime;

                _mainWindow.Dispatcher.Invoke(() =>
                {
                    if (_projectionScrollViewer == null)
                        return;

                    // 🔧 圣经滚动同步：按比例同步（因为投影屏幕做了拉伸）
                    double mainScrollTop = bibleScrollViewer.VerticalOffset;
                    
                    // 🔧 关键：投影屏幕拉伸后，需要按高度比例同步滚动
                    double mainExtentHeight = bibleScrollViewer.ExtentHeight;
                    double projExtentHeight = _projectionScrollViewer.ExtentHeight;
                    
                    double projScrollTop;
                    if (mainExtentHeight > 0 && projExtentHeight > 0)
                    {
                        // 按比例计算投影屏幕的滚动位置
                        double scrollRatio = mainScrollTop / mainExtentHeight;
                        projScrollTop = scrollRatio * projExtentHeight;
                    }
                    else
                    {
                        // 后备方案：直接使用相同的滚动位置
                        projScrollTop = mainScrollTop;
                    }
                    
                    _projectionScrollViewer.ScrollToVerticalOffset(projScrollTop);

//#if DEBUG
//                    double mainScrollableHeight = bibleScrollViewer.ScrollableHeight;
//                    double projScrollableHeight = _projectionScrollViewer.ScrollableHeight;
//                    double mainViewportHeight = bibleScrollViewer.ViewportHeight;
//                    double projViewportHeight = _projectionScrollViewer.ViewportHeight;
//                    // mainExtentHeight 和 projExtentHeight 已在上面定义，不重复定义
//                    
//                    //System.Diagnostics.Debug.WriteLine($"📊 [圣经投影] 滚动偏移: {projScrollTop:F2} (同步自主屏 {mainScrollTop:F2})");
//                    //System.Diagnostics.Debug.WriteLine($"📊 [圣经投影] 可滚动高度: {projScrollableHeight:F2} (主屏: {mainScrollableHeight:F2})");
//                    //System.Diagnostics.Debug.WriteLine($"📊 [圣经投影] 视口高度: {projViewportHeight:F2} (主屏: {mainViewportHeight:F2})");
//                    //System.Diagnostics.Debug.WriteLine($"📊 [圣经投影] 内容总高度: {projExtentHeight:F2} (主屏: {mainExtentHeight:F2})");
//                    //System.Diagnostics.Debug.WriteLine($"🔍 [圣经投影] 内容高度差异: {projExtentHeight - mainExtentHeight:F2}");
//                    //System.Diagnostics.Debug.WriteLine($"🔍 [圣经投影] 可滚动高度差异: {projScrollableHeight - mainScrollableHeight:F2}");
//                    //System.Diagnostics.Debug.WriteLine($"🔍 ========================");
//#endif
                });
            }
            catch (Exception)
            {
                // 忽略错误
            }
        }

        /// <summary>
        /// 更新投影文字内容（专门用于歌词/文本编辑器）
        /// 语义清晰：这是文字投影，不是图片投影
        /// </summary>
        public void UpdateProjectionText(SKBitmap renderedTextImage)
        {
//#if DEBUG
//            System.Diagnostics.Debug.WriteLine($"📝 [文字投影] 开始渲染 - 尺寸: {renderedTextImage?.Width}x{renderedTextImage?.Height}");
//#endif

            if (_projectionWindow == null || renderedTextImage == null)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"⚠️ [文字投影] 投影窗口或文字图像为空，跳过");
//#endif
                return;
            }

            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    // 直接转换并显示，无需缓存、变色、缩放等复杂逻辑
                    var bitmapSource = ConvertToBitmapSource(renderedTextImage);
                    
                    if (bitmapSource != null)
                    {
                        // 更新投影窗口的图像控件
                        _projectionImageControl.Source = bitmapSource;
                        _projectionImageControl.Width = renderedTextImage.Width;
                        _projectionImageControl.Height = renderedTextImage.Height;
                        
                        var screen = _screens[_currentScreenIndex];
                        double screenWidth = screen.Bounds.Width;
                        double screenHeight = screen.Bounds.Height;
                        
                        // 🔧 像素级对齐：与图片投影保持一致（左上角对齐+Margin）
                        _projectionImageControl.HorizontalAlignment = WpfHorizontalAlignment.Left;
                        _projectionImageControl.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                        
                        // 计算位置偏移量
                        double containerWidth = _projectionScrollViewer?.ActualWidth ?? screenWidth;
                        double containerHeight = _projectionScrollViewer?.ActualHeight ?? screenHeight;
                        if (containerWidth <= 0) containerWidth = screenWidth;
                        if (containerHeight <= 0) containerHeight = screenHeight;
                        
                        // 🔧 拉伸到容器宽度：图片宽度=主屏幕宽度，需要拉伸到投影屏幕宽度
                        _projectionImageControl.Stretch = System.Windows.Media.Stretch.Fill;
                        _projectionImageControl.Width = containerWidth; // 拉伸到容器宽度
                        _projectionImageControl.Height = renderedTextImage.Height * (containerWidth / renderedTextImage.Width); // 按比例调整高度
                        
                        double x = 0; // 拉伸后宽度=容器宽度，左对齐
                        double y = 0; // 顶部对齐
                        
                        _projectionImageControl.Margin = new System.Windows.Thickness(x, y, 0, 0);

//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] 原始图片尺寸: {renderedTextImage.Width}x{renderedTextImage.Height}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] 屏幕尺寸: {screenWidth}x{screenHeight}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] 容器尺寸: {containerWidth}x{containerHeight}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] ScrollViewer实际尺寸: {_projectionScrollViewer?.ActualWidth ?? 0}x{_projectionScrollViewer?.ActualHeight ?? 0}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] 计算偏移量 X: {x}, Y: {y}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] ImageControl对齐: H={_projectionImageControl.HorizontalAlignment}, V={_projectionImageControl.VerticalAlignment}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] ImageControl Margin: {_projectionImageControl.Margin}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] 原始图片尺寸: {renderedTextImage.Width}x{renderedTextImage.Height}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] 拉伸后ImageControl尺寸: {_projectionImageControl.Width}x{_projectionImageControl.Height}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] 拉伸比例: {containerWidth / renderedTextImage.Width:F4}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [文字投影-对齐] 拉伸模式: {_projectionImageControl.Stretch}");
//                        
//                        // 🔍 DPI相关调试信息
//                        var presentationSource = PresentationSource.FromVisual(_projectionWindow);
//                        if (presentationSource?.CompositionTarget != null)
//                        {
//                            double dpiScaleX = presentationSource.CompositionTarget.TransformToDevice.M11;
//                            double dpiScaleY = presentationSource.CompositionTarget.TransformToDevice.M22;
//                            System.Diagnostics.Debug.WriteLine($"📐 [文字投影-DPI] 投影窗口DPI缩放: X={dpiScaleX:F2}, Y={dpiScaleY:F2} (1.0=96DPI, 1.25=120DPI, 1.5=144DPI)");
//                            System.Diagnostics.Debug.WriteLine($"📐 [文字投影-DPI] 实际物理像素: {containerWidth * dpiScaleX:F0}x{containerHeight * dpiScaleY:F0}");
//                        }
//                        else
//                        {
//                            System.Diagnostics.Debug.WriteLine($"📐 [文字投影-DPI] 无法获取DPI信息");
//                        }
//#endif
                        
                        // 🔧 设置容器高度：使用拉伸后的高度
                        if (_projectionContainer != null)
                        {
                            // 圣经/歌词投影：容器高度 = 拉伸后的图片高度
                            _projectionContainer.Height = _projectionImageControl.Height;
                            _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"📐 [文字投影-滚动] 容器高度: {_projectionContainer.Height} (拉伸后)");
//                            System.Diagnostics.Debug.WriteLine($"📐 [文字投影-滚动] 原始图片高度: {renderedTextImage.Height}");
//                            System.Diagnostics.Debug.WriteLine($"📐 [文字投影-滚动] 高度缩放比例: {_projectionImageControl.Height / renderedTextImage.Height:F4}");
//#endif
                        }

//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"✅ [文字投影] 渲染完成 - 尺寸: {renderedTextImage.Width}x{renderedTextImage.Height}");
//#endif
                    }
                });
            }
            catch (Exception)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"❌ [文字投影] 渲染失败: {ex.Message}");
//#endif
            }
        }

        /// <summary>
        /// 更新投影图片 - 使用共享渲染模式
        /// </summary>
        public void UpdateProjectionImage(SKBitmap image, bool applyColorEffect, double zoomRatio, bool isOriginalMode, OriginalDisplayMode originalDisplayMode = OriginalDisplayMode.Stretch, bool bypassCache = false)
        {
//#if DEBUG
//            System.Diagnostics.Debug.WriteLine($"\n========== [UpdateProjectionImage] 被调用 ==========");
//            System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjectionImage] 图像尺寸: {image?.Width}x{image?.Height}");
//            System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjectionImage] 投影窗口: {(_projectionWindow != null ? "存在" : "null")}");
//            System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjectionImage] 变色效果: {applyColorEffect}");
//            System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjectionImage] 缩放比例: {zoomRatio:F2}");
//            System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjectionImage] 原图模式: {isOriginalMode}");
//            System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjectionImage] 显示模式: {originalDisplayMode}");
//            System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjectionImage] 绕过缓存: {bypassCache}");
//#endif
            
            // 🔍 检查缩放参数是否变化
            bool zoomChanged = Math.Abs(_zoomRatio - zoomRatio) > 0.001;

//#if DEBUG
//            System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjectionImage] 缩放改变: {zoomChanged} (旧:{_zoomRatio:F2} -> 新:{zoomRatio:F2})");
//#endif
            
            _currentImage = image;
            _isColorEffectEnabled = applyColorEffect;
            _isOriginalMode = isOriginalMode;
            _originalDisplayMode = originalDisplayMode;
            _currentImagePath = _imageProcessor?.CurrentImagePath; // 记录当前图片路径用于缓存键
            
            // 🔧 如果绕过缓存（如文本编辑器），生成唯一的缓存键
            if (bypassCache)
            {
                _currentImagePath = $"texteditor_{Guid.NewGuid()}";
            }

            if (_projectionWindow != null && image != null)
            {
                _zoomRatio = zoomRatio;
                
                // 🚀 共享渲染模式：尝试直接使用主屏的BitmapSource
                var mainScreenBitmap = _imageProcessor?.CurrentPhoto;
                if (mainScreenBitmap != null && !bypassCache)
                {
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"✅ [UpdateProjectionImage] 使用共享渲染模式 (主屏BitmapSource)");
//#endif
                    // ✅ 直接复用主屏渲染结果，零GPU开销
                    _ = UseSharedRenderingAsync(mainScreenBitmap);
                }
                else
                {
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"⚠️ [UpdateProjectionImage] 使用独立渲染模式 (mainScreenBitmap={mainScreenBitmap != null}, bypassCache={bypassCache})");
//#endif
                    // ⚠️ 降级：独立渲染（文本编辑器等特殊场景）
                    _ = PreRenderProjectionAsync();
                }
            }
//#if DEBUG
//            else
//            {
//                System.Diagnostics.Debug.WriteLine($"⚠️ [UpdateProjectionImage] 跳过渲染 (投影窗口={_projectionWindow != null}, 图像={image != null})");
//            }
//            System.Diagnostics.Debug.WriteLine($"========== [UpdateProjectionImage] 结束 ==========\n");
//#endif
        }
        
        /// <summary>
        /// 使用共享渲染模式 - 直接复用主屏BitmapSource
        /// </summary>
        private System.Threading.Tasks.Task UseSharedRenderingAsync(BitmapSource mainScreenBitmap)
        {
            if (_projectionWindow == null || mainScreenBitmap == null)
                return System.Threading.Tasks.Task.CompletedTask;

            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    //#if DEBUG
                    //var sw = System.Diagnostics.Stopwatch.StartNew();
                    //#endif
                    
                    var screen = _screens[_currentScreenIndex];
                    int screenWidth = screen.Bounds.Width;
                    int screenHeight = screen.Bounds.Height;

//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"\n========== [原图投影调试] 开始渲染 ==========");
//                    System.Diagnostics.Debug.WriteLine($"📺 [原图投影] 主屏BitmapSource尺寸: {mainScreenBitmap.PixelWidth}x{mainScreenBitmap.PixelHeight}");
//                    System.Diagnostics.Debug.WriteLine($"📺 [原图投影] 投影屏幕尺寸: {screenWidth}x{screenHeight}");
//                    System.Diagnostics.Debug.WriteLine($"📺 [原图投影] 原图模式: {_isOriginalMode}");
//                    System.Diagnostics.Debug.WriteLine($"📺 [原图投影] 显示模式: {_originalDisplayMode}");
//                    System.Diagnostics.Debug.WriteLine($"📺 [原图投影] 变色效果: {_isColorEffectEnabled}");
//                    System.Diagnostics.Debug.WriteLine($"📺 [原图投影] 缩放比例: {_zoomRatio:F2}");
//                    if (_currentImage != null)
//                    {
//                        System.Diagnostics.Debug.WriteLine($"📺 [原图投影] 当前SKBitmap尺寸: {_currentImage.Width}x{_currentImage.Height}");
//                    }
//#endif

                    // 计算投影屏显示尺寸
                    var (newWidth, newHeight) = CalculateImageSize(screenWidth, screenHeight);

//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"📐 [原图投影] 计算后的显示尺寸: {newWidth}x{newHeight}");
//#endif
                    
                    // 🚀 核心优化：直接使用主屏的BitmapSource
                    _projectionImage = mainScreenBitmap;
                    
                    // 更新UI
                    if (_projectionImageControl != null)
                    {
//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"🎨 [原图投影] 更新前 - Stretch属性: {_projectionImageControl.Stretch}");
//#endif
                        _projectionImageControl.Source = _projectionImage;
                        _projectionImageControl.Width = newWidth;
                        _projectionImageControl.Height = newHeight;
                        
                        // 🔧 关键修复：根据显示模式设置Stretch属性
                        // 拉伸模式：Fill（填满，可能变形） - 宽度填满屏幕
                        // 适中模式：Uniform（等比缩放，保持比例） - 完整显示图片
                        if (_isOriginalMode && _originalDisplayMode == OriginalDisplayMode.Stretch)
                        {
                            _projectionImageControl.Stretch = System.Windows.Media.Stretch.Fill;
                        }
                        else
                        {
                            _projectionImageControl.Stretch = System.Windows.Media.Stretch.Uniform;
                        }
                        
                        // 设置对齐和边距
                        if (_projectionScrollViewer != null && _projectionContainer != null)
                        {
                            double containerWidth = _projectionScrollViewer.ActualWidth;
                            double containerHeight = _projectionScrollViewer.ActualHeight;
                            if (containerWidth <= 0) containerWidth = screenWidth;
                            if (containerHeight <= 0) containerHeight = screenHeight;

//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"📦 [原图投影] 容器实际尺寸: {_projectionScrollViewer.ActualWidth}x{_projectionScrollViewer.ActualHeight}");
//                            System.Diagnostics.Debug.WriteLine($"📦 [原图投影] 容器使用尺寸: {containerWidth}x{containerHeight}");
//#endif
                            
                            // 🔧 使用Stretch=Uniform时，Image控件应该居中对齐
                            // 图片会在Image控件内自动居中并按比例缩放
                            double y = _isOriginalMode ? Math.Max(0, (containerHeight - newHeight) / 2.0) : 0;

//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"📍 [原图投影] 计算垂直偏移量 Y={y:F2}");
//#endif
                            
                            _projectionImageControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                            _projectionImageControl.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                            _projectionImageControl.Margin = new System.Windows.Thickness(0, y, 0, 0);

//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"🎯 [原图投影] 对齐方式: H={_projectionImageControl.HorizontalAlignment}, V={_projectionImageControl.VerticalAlignment}");
//                            System.Diagnostics.Debug.WriteLine($"🎯 [原图投影] Margin: {_projectionImageControl.Margin}");
//#endif
                            
                            // 设置滚动区域
                            double scrollHeight;
                            if (_isOriginalMode)
                            {
                                scrollHeight = newHeight <= screenHeight ? screenHeight : newHeight + screenHeight;
                                _projectionScrollViewer.VerticalScrollBarVisibility = newHeight <= screenHeight 
                                    ? System.Windows.Controls.ScrollBarVisibility.Hidden 
                                    : System.Windows.Controls.ScrollBarVisibility.Hidden;
                            }
                            else
                            {
                                scrollHeight = newHeight >= screenHeight ? newHeight + screenHeight : screenHeight;
                                _projectionScrollViewer.VerticalScrollBarVisibility = newHeight >= screenHeight 
                                    ? System.Windows.Controls.ScrollBarVisibility.Hidden 
                                    : System.Windows.Controls.ScrollBarVisibility.Hidden;
                            }
                            _projectionContainer.Height = scrollHeight;

//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"📏 [原图投影] 容器高度设置为: {scrollHeight:F2}");
//#endif
                        }

//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"🎨 [原图投影] 更新后 - Stretch属性: {_projectionImageControl.Stretch}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [原图投影] ImageControl尺寸: {_projectionImageControl.Width}x{_projectionImageControl.Height}");
//                        System.Diagnostics.Debug.WriteLine($"📐 [原图投影] BitmapSource尺寸: {mainScreenBitmap.PixelWidth}x{mainScreenBitmap.PixelHeight}");
//                        System.Diagnostics.Debug.WriteLine($"⚠️ [原图投影] 尺寸匹配: {(_projectionImageControl.Width == mainScreenBitmap.PixelWidth && _projectionImageControl.Height == mainScreenBitmap.PixelHeight ? "完全匹配" : "不匹配，Stretch=Uniform会自动缩放")}");
//                        System.Diagnostics.Debug.WriteLine($"========== [原图投影调试] 渲染完成 ==========\n");
//#endif
                    }
                    
                    //#if DEBUG
                    //sw.Stop();
                    //#endif
                });
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [共享渲染] 失败: {ex.Message}");
                //#else
                _ = ex;
                //#endif
            }
            
            return System.Threading.Tasks.Task.CompletedTask;
        }
        
        /// <summary>
        /// 同步渲染投影（主线程）- 独立渲染模式（降级方案）
        /// </summary>
        public System.Threading.Tasks.Task PreRenderProjectionAsync()
        {
            if (_projectionWindow == null || _currentImage == null)
                return System.Threading.Tasks.Task.CompletedTask;

            // ⚡ 防止重复预渲染
            lock (_preRenderLock)
            {
                if (_isPreRendering)
                {
                    //System.Diagnostics.Debug.WriteLine($"⚡ [PreRender] 已在预渲染中，跳过");
                    return System.Threading.Tasks.Task.CompletedTask;
                }
                _isPreRendering = true;
            }

            try
            {
                // 🎯 使用Invoke立即同步执行，避免队列积压导致显示旧图
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    
                    var screen = _screens[_currentScreenIndex];
                    int screenWidth = screen.Bounds.Width;
                    int screenHeight = screen.Bounds.Height;

                    // 计算缩放后的尺寸
                    var (newWidth, newHeight) = CalculateImageSize(screenWidth, screenHeight);
                    
                    string cacheKey = GenerateProjectionCacheKey(newWidth, newHeight);
                    
                    // 检查缓存
                    if (_projectionCache.TryGetValue(cacheKey, out BitmapSource cachedImage))
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"⚡ [PreRender] ✅ 缓存命中 (耗时: {sw.ElapsedMilliseconds}ms)");
                        //#endif
                        
                        // 直接使用缓存图片
                        _projectionImage = cachedImage;
                        if (_projectionImageControl != null)
                        {
                            _projectionImageControl.Source = _projectionImage;
                            _projectionImageControl.Width = newWidth;
                            _projectionImageControl.Height = newHeight;
                        }
                        return;
                    }
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"⚡ [PreRender] ❌ 缓存未命中，开始渲染...");
                    //#endif
                    
                    // 🎮 使用GPU加速渲染（如果GPU不可用，自动降级到CPU）
                    #if DEBUG
                    var gpuStart = System.Diagnostics.Stopwatch.StartNew();
                    #endif
                    
                    var processedImage = Core.GPUContext.Instance.ScaleImageGpu(
                        _currentImage, 
                        newWidth, 
                        newHeight, 
                        SKFilterQuality.High  // 保持最高质量
                    );
                    
                    //#if DEBUG
                    //gpuStart.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⚡ [PreRender GPU] 耗时: {gpuStart.ElapsedMilliseconds}ms, 尺寸: {newWidth}x{newHeight}, 质量: High");
                    //#endif

                    if (processedImage == null)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"❌ [PreRender] 渲染失败");
                        //#endif
                        return;
                    }

                    if (_isColorEffectEnabled)
                    {
                        _imageProcessor.ApplyYellowTextEffect(processedImage);
                    }

                    // 转换为BitmapSource（已经在UI线程）
                    BitmapSource projectionImage = ConvertToBitmapSource(processedImage);
                    processedImage.Dispose();
                    
                    // 加入缓存
                    var entryOptions = new MemoryCacheEntryOptions
                    {
                        Size = Math.Max(1, (newWidth * newHeight * 4) / (1024 * 1024)),
                        Priority = CacheItemPriority.Normal,
                        SlidingExpiration = TimeSpan.FromMinutes(10)
                    };
                    _projectionCache.Set(cacheKey, projectionImage, entryOptions);
                    
                    sw.Stop();
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"⚡ [PreRender] ✅ 渲染完成：总耗时 {sw.ElapsedMilliseconds}ms");
                    //#endif
                    
                    // 🎯 直接使用预渲染的图片，不调用UpdateProjection（避免重复渲染）
                    _projectionImage = projectionImage;
                    
                    // 直接更新UI
                    if (_projectionImageControl != null && _projectionImage != null)
                    {
                        _projectionImageControl.Source = _projectionImage;
                        _projectionImageControl.Width = newWidth;
                        _projectionImageControl.Height = newHeight;
                        
                        // 设置对齐和边距（与UpdateProjection保持一致）
                        if (_projectionScrollViewer != null && _projectionContainer != null)
                        {
                            double containerWidth = _projectionScrollViewer.ActualWidth;
                            double containerHeight = _projectionScrollViewer.ActualHeight;
                            if (containerWidth <= 0) containerWidth = screenWidth;
                            if (containerHeight <= 0) containerHeight = screenHeight;
                            
                            double x = Math.Max(0, (containerWidth - newWidth) / 2.0);
                            double y = _isOriginalMode ? Math.Max(0, (containerHeight - newHeight) / 2.0) : 0;
                            
                            _projectionImageControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                            _projectionImageControl.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                            _projectionImageControl.Margin = new System.Windows.Thickness(x, y, 0, 0);
                            
                            // 设置滚动区域
                            double scrollHeight;
                            if (_isOriginalMode)
                            {
                                scrollHeight = newHeight <= screenHeight ? screenHeight : newHeight + screenHeight;
                                _projectionScrollViewer.VerticalScrollBarVisibility = newHeight <= screenHeight 
                                    ? System.Windows.Controls.ScrollBarVisibility.Hidden 
                                    : System.Windows.Controls.ScrollBarVisibility.Hidden;
                            }
                            else
                            {
                                scrollHeight = newHeight >= screenHeight ? newHeight + screenHeight : screenHeight;
                                _projectionScrollViewer.VerticalScrollBarVisibility = newHeight >= screenHeight 
                                    ? System.Windows.Controls.ScrollBarVisibility.Hidden 
                                    : System.Windows.Controls.ScrollBarVisibility.Hidden;
                            }
                            _projectionContainer.Height = scrollHeight;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [PreRender] 预渲染失败: {ex.Message}");
                //#else
                _ = ex; // 避免未使用变量警告
                //#endif
            }
            finally
            {
                // ⚡ 重置预渲染标志
                lock (_preRenderLock)
                {
                    _isPreRendering = false;
                }
            }
            
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// 重置投影滚动位置到顶部
        /// </summary>
        public void ResetProjectionScroll()
        {
            if (_projectionWindow != null && _projectionScrollViewer != null)
            {
                _projectionWindow.Dispatcher.Invoke(() =>
                {
                    _projectionScrollViewer.ScrollToVerticalOffset(0);
                    //System.Diagnostics.Debug.WriteLine("✅ 投影滚动位置已重置为0");
                });
            }
        }

        /// <summary>
        /// 同步共享渲染 - 每一帧调用，使用主屏的BitmapSource更新投影窗口
        /// </summary>
        public void SyncSharedRendering()
        {
            if (!_syncEnabled || _projectionWindow == null)
                return;

            // 🚀 直接使用主屏的BitmapSource（零GPU开销）
            var mainScreenBitmap = _imageProcessor?.CurrentPhoto;
            
            // ⚡ 优化：只有当 BitmapSource 发生变化时才更新（避免无意义的UI刷新）
            if (mainScreenBitmap != null && mainScreenBitmap != _lastSharedBitmap)
            {
                _lastSharedBitmap = mainScreenBitmap;
                _ = UseSharedRenderingAsync(mainScreenBitmap);
            }
        }
        
        /// <summary>
        /// 同步投影滚动位置 - 使用绝对像素位置同步,通过原始图片作为中介
        /// </summary>
        public void SyncProjectionScroll()
        {
            if (!_syncEnabled || _projectionWindow == null || _currentImage == null)
                return;

            try
            {
                // 性能节流
                var currentTime = DateTime.Now;
                if (currentTime - _lastSyncTime < _syncThrottleInterval)
                    return;
                _lastSyncTime = currentTime;

                _mainWindow.Dispatcher.Invoke(() =>
                {
                    if (_projectionScrollViewer == null || _mainScrollViewer == null || _mainImageControl == null)
                        return;

                    // 获取主屏幕当前的绝对滚动位置
                    double mainScrollTop = _mainScrollViewer.VerticalOffset;

                    // 获取主屏幕和投影屏的画布/屏幕尺寸
                    double mainCanvasWidth = _mainScrollViewer.ActualWidth;
                    double mainCanvasHeight = _mainScrollViewer.ActualHeight;
                    
                    var screen = _screens[_currentScreenIndex];
                    int projScreenWidth = screen.Bounds.Width;
                    int projScreenHeight = screen.Bounds.Height;

                    // 计算主屏幕图片的实际显示高度
                    double mainImgHeight;
                    if (_isOriginalMode)
                    {
                        double widthRatio = mainCanvasWidth / _currentImage.Width;
                        double heightRatio = mainCanvasHeight / _currentImage.Height;
                        if (widthRatio < 1 || heightRatio < 1)
                        {
                            double scale = Math.Min(widthRatio, heightRatio);
                            mainImgHeight = _currentImage.Height * scale;
                        }
                        else
                        {
                            mainImgHeight = _currentImage.Height;
                        }
                    }
                    else
                    {
                        double baseRatio = mainCanvasWidth / _currentImage.Width;
                        double finalRatio = baseRatio * _zoomRatio;
                        mainImgHeight = _currentImage.Height * finalRatio;
                    }

                    // 计算投影屏幕图片的实际显示高度 (必须与CalculateImageSize逻辑一致!)
                    double projImgHeight;
                    if (_isOriginalMode)
                    {
                        // 获取投影ScrollViewer的实际DIU尺寸
                        double projCanvasWidth = _projectionScrollViewer?.ActualWidth ?? projScreenWidth;
                        double projCanvasHeight = _projectionScrollViewer?.ActualHeight ?? projScreenHeight;
                        if (projCanvasWidth <= 0) projCanvasWidth = projScreenWidth;
                        if (projCanvasHeight <= 0) projCanvasHeight = projScreenHeight;
                        
                        double widthRatio = projCanvasWidth / _currentImage.Width;
                        double heightRatio = projCanvasHeight / _currentImage.Height;
                        
                        double scaleRatio;
                        if (_originalDisplayMode == OriginalDisplayMode.Stretch)
                        {
                            // 拉伸模式：使用高度比例(宽度会被拉伸)
                            scaleRatio = heightRatio;
                        }
                        else
                        {
                            // 适中模式：使用较小的比例(等比缩放)
                            scaleRatio = Math.Min(widthRatio, heightRatio);
                        }
                        
                        // 应用放大限制
                        if (scaleRatio >= 1)
                        {
                            double screenArea = projCanvasWidth * projCanvasHeight;
                            double imageArea = _currentImage.Width * _currentImage.Height;
                            double areaRatio = screenArea / imageArea;
                            
                            double maxScale;
                            if (areaRatio > 16) maxScale = 6.0;
                            else if (areaRatio > 9) maxScale = 4.0;
                            else if (areaRatio > 4) maxScale = 3.0;
                            else maxScale = 2.0;
                            
                            scaleRatio = Math.Min(scaleRatio, maxScale);
                        }
                        
                        // 高度计算与CalculateImageSize一致
                        projImgHeight = _currentImage.Height * scaleRatio;
                    }
                    else
                    {
                        // 正常模式: 宽度填满,高度按比例,与CalculateImageSize一致
                        double projCanvasWidth = _projectionScrollViewer?.ActualWidth ?? projScreenWidth;
                        double projCanvasHeight = _projectionScrollViewer?.ActualHeight ?? projScreenHeight;
                        if (projCanvasWidth <= 0) projCanvasWidth = projScreenWidth;
                        if (projCanvasHeight <= 0) projCanvasHeight = projScreenHeight;
                        
                        double baseRatio = projCanvasWidth / _currentImage.Width;
                        double finalRatio = baseRatio * _zoomRatio;
                        projImgHeight = _currentImage.Height * finalRatio;
                    }

                    if (mainImgHeight == 0 || projImgHeight == 0)
                        return;

                    // 【关键】计算在原始图片上的相对位置 (0-1之间)
                    double originalRelativePos = mainScrollTop / mainImgHeight;

                    // 【关键】计算投影屏幕应该滚动到的绝对像素位置
                    double projScrollTop = originalRelativePos * projImgHeight;

                    // 应用到投影屏幕
                    _projectionScrollViewer.ScrollToVerticalOffset(projScrollTop);

                    // 📺 FPS监控：记录投影同步
                    (_mainWindow as UI.MainWindow)?._fpsMonitor?.RecordProjectionSync();
                    
                    #if DEBUG
                    // ⚡ 验证共享渲染状态（每60次滚动输出一次）
                    _scrollVerifyCount++;
                    if (_scrollVerifyCount % 60 == 0)
                    {
                        var mainBitmap = _imageProcessor?.CurrentPhoto;
                        var projBitmap = _projectionImageControl?.Source;
                        bool isShared = (mainBitmap != null && projBitmap != null && ReferenceEquals(mainBitmap, projBitmap));
                    }
                    #endif

                    // System.Diagnostics.Debug.WriteLine($"📜 同步: 主屏滚动={mainScrollTop:F0}, 主屏图高={mainImgHeight:F0}, 原图相对={originalRelativePos:P1}, 投影图高={projImgHeight:F0}, 投影滚动={projScrollTop:F0}");
                });
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"同步投影滚动失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取显示器信息列表
        /// </summary>
        public List<(string Name, bool IsPrimary, int Width, int Height)> GetMonitorInfo()
        {
            var monitors = new List<(string, bool, int, int)>();

            for (int i = 0; i < _screens.Count; i++)
            {
                var screen = _screens[i];
                string name = screen.Primary ? "主显示器" : $"显示器{i + 1}";
                monitors.Add((name, screen.Primary, screen.Bounds.Width, screen.Bounds.Height));
            }

            return monitors;
        }
        
        /// <summary>
        /// 获取当前投影显示器的分辨率
        /// </summary>
        public (int width, int height) GetCurrentProjectionSize()
        {
            if (_screens != null && _currentScreenIndex >= 0 && _currentScreenIndex < _screens.Count)
            {
                var screen = _screens[_currentScreenIndex];
                return (screen.Bounds.Width, screen.Bounds.Height);
            }
            return (1920, 1080); // 默认值
        }
        
        /// <summary>
        /// 获取投影窗口的VideoView（用于视频播放）
        /// </summary>
        public VideoView GetProjectionVideoView()
        {
            return _projectionVideoView;
        }
        
        /// <summary>
        /// 获取投影窗口（用于FPS监控）
        /// </summary>
        public Window GetProjectionWindow()
        {
            return _projectionWindow;
        }
        
        /// <summary>
        /// 设置投影窗口媒体文件名显示
        /// </summary>
        public void SetProjectionMediaFileName(string fileName, bool isAudioOnly)
        {
            if (_projectionWindow == null || _projectionMediaFileNameBorder == null || _projectionMediaFileNameText == null)
                return;
                
            _mainWindow.Dispatcher.Invoke(() =>
            {
                if (isAudioOnly)
                {
                    _projectionMediaFileNameText.Text = fileName;
                    _projectionMediaFileNameBorder.Visibility = Visibility.Visible;
                    //System.Diagnostics.Debug.WriteLine($"🎵 投影窗口显示音频文件名: {fileName}");
                }
                else
                {
                    _projectionMediaFileNameBorder.Visibility = Visibility.Collapsed;
                }
            });
        }
        
        /// <summary>
        /// 设置圣经标题（固定在顶部）
        /// </summary>
        public void SetBibleTitle(string title, bool visible)
        {
            if (_projectionWindow == null || _projectionBibleTitleBorder == null || _projectionBibleTitleText == null)
                return;
                
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _projectionBibleTitleText.Text = title;
                _projectionBibleTitleBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📖 [圣经标题] 设置: {title}, 可见: {visible}");
                //#endif
            });
        }
        
        /// <summary>
        /// 直接设置投影滚动位置（用于圣经同步）
        /// </summary>
        public void SetProjectionScrollPosition(double offset, bool shouldDebug = false)
        {
            if (!_syncEnabled || _projectionWindow == null || _projectionScrollViewer == null)
                return;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                // 🔧 尝试多次设置，确保生效
                _projectionScrollViewer.ScrollToVerticalOffset(offset);
                _projectionScrollViewer.InvalidateScrollInfo();
                _projectionScrollViewer.UpdateLayout();
                _projectionScrollViewer.ScrollToVerticalOffset(offset); // 再次设置
                
                //#if DEBUG
                //// 只在主屏幕要求输出时才输出（保持同步）
                //if (shouldDebug)
                //{
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 接收偏移: {offset:F2} (主屏传入)");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 实际偏移: {_projectionScrollViewer.VerticalOffset:F2} (双重设置后)");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 可滚动高度: {_projectionScrollViewer.ScrollableHeight:F2}");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 视口高度: {_projectionScrollViewer.ViewportHeight:F2}");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 内容总高度: {_projectionScrollViewer.ExtentHeight:F2}");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] CanContentScroll: {_projectionScrollViewer.CanContentScroll}");
                //    System.Diagnostics.Debug.WriteLine($"🔍 ========================");
                //}
                //#endif
            });
        }
        
        /// <summary>
        /// 按比例设置投影滚动位置（用于圣经同步，确保像素级对齐）
        /// </summary>
        public void SetProjectionScrollPositionByRatio(double scrollRatio, bool shouldDebug = false)
        {
            if (!_syncEnabled || _projectionWindow == null || _projectionScrollViewer == null)
                return;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                // 根据比例计算投影屏幕的滚动位置
                double projScrollableHeight = _projectionScrollViewer.ScrollableHeight;
                double projScrollOffset = scrollRatio * projScrollableHeight;
                
                _projectionScrollViewer.ScrollToVerticalOffset(projScrollOffset);
                _projectionScrollViewer.InvalidateScrollInfo();
                _projectionScrollViewer.UpdateLayout();
                _projectionScrollViewer.ScrollToVerticalOffset(projScrollOffset); // 再次设置
                
                //#if DEBUG
                //if (shouldDebug)
                //{
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 滚动比例: {scrollRatio:P2}");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 可滚动高度: {projScrollableHeight:F2}");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 计算滚动偏移: {projScrollOffset:F2}");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 实际滚动偏移: {_projectionScrollViewer.VerticalOffset:F2}");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 视口高度: {_projectionScrollViewer.ViewportHeight:F2}");
                //    System.Diagnostics.Debug.WriteLine($"📊 [投影屏幕] 内容总高度: {_projectionScrollViewer.ExtentHeight:F2}");
                //    System.Diagnostics.Debug.WriteLine($"🔍 ========================");
                //}
                //#endif
            });
        }
        
        /// <summary>
        /// 显示视频投影（隐藏图片，显示视频）
        /// </summary>
        public void ShowVideoProjection()
        {
            if (_projectionWindow == null)
            {
                // System.Diagnostics.Debug.WriteLine("❌ 投影窗口未打开");
                return;
            }
            
            _mainWindow.Dispatcher.Invoke(() =>
            {
                // 隐藏图片ScrollViewer
                if (_projectionScrollViewer != null)
                {
                    _projectionScrollViewer.Visibility = Visibility.Collapsed;
                }
                
                // 显示视频容器
                if (_projectionVideoContainer != null)
                {
                    _projectionVideoContainer.Visibility = Visibility.Visible;
                }
                
                // System.Diagnostics.Debug.WriteLine("✅ 已切换到视频投影模式");
            });
        }
        
        /// <summary>
        /// 显示图片投影（隐藏视频，显示图片）
        /// </summary>
        public void ShowImageProjection()
        {
            if (_projectionWindow == null)
            {
                // System.Diagnostics.Debug.WriteLine("❌ 投影窗口未打开");
                return;
            }
            
            _mainWindow.Dispatcher.Invoke(() =>
            {
                // 隐藏视频容器
                if (_projectionVideoContainer != null)
                {
                    _projectionVideoContainer.Visibility = Visibility.Collapsed;
                }
                
                // 显示图片ScrollViewer
                if (_projectionScrollViewer != null)
                {
                    _projectionScrollViewer.Visibility = Visibility.Visible;
                }
                
                // System.Diagnostics.Debug.WriteLine("✅ 已切换到图片投影模式");
            });
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 打开投影窗口
        /// </summary>
        private bool OpenProjection()
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine("📂 OpenProjection 开始执行");
                
                // 注释掉图片检查，允许在播放视频时也能开启投影
                // 视频投影时不需要 _currentImage
                /*
                // 检查是否有图片
                if (_currentImage == null)
                {
                    //System.Diagnostics.Debug.WriteLine("❌ 没有当前图片");
                    WpfMessageBox.Show("请先选中一张图片！", "警告", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return false;
                }
                
                //System.Diagnostics.Debug.WriteLine($"✅ 当前图片尺寸: {_currentImage.Width}x{_currentImage.Height}");
                */
                
                if (_currentImage != null)
                {
                    // System.Diagnostics.Debug.WriteLine($"✅ 当前图片尺寸: {_currentImage.Width}x{_currentImage.Height}");
                }
                else
                {
                    // System.Diagnostics.Debug.WriteLine("ℹ️ 无当前图片（可能正在播放视频）");
                }

                // 检查是否有多个屏幕
                // System.Diagnostics.Debug.WriteLine($"屏幕数量: {_screens.Count}");
                if (_screens.Count < 2)
                {
                    // System.Diagnostics.Debug.WriteLine("❌ 只有一个屏幕");
                    WpfMessageBox.Show("未检测到第二个显示器！", "警告", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return false;
                }
                
                // System.Diagnostics.Debug.WriteLine($"✅ 检测到 {_screens.Count} 个屏幕");

                // 获取选定的屏幕
                int selectedIndex = _screenComboBox?.SelectedIndex ?? 0;
                if (selectedIndex < 0 || selectedIndex >= _screens.Count)
                {
                    WpfMessageBox.Show("选定的显示器无效！", "错误", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    return false;
                }

                var screen = _screens[selectedIndex];
                _currentScreenIndex = selectedIndex;
                
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"📺 [投影屏幕] 选择的屏幕: 索引={selectedIndex}, 是否主屏={screen.Primary}");
//                System.Diagnostics.Debug.WriteLine($"📺 [投影屏幕] 屏幕分辨率: {screen.Bounds.Width}x{screen.Bounds.Height}");
//                System.Diagnostics.Debug.WriteLine($"📺 [投影屏幕] 屏幕位置: Left={screen.Bounds.Left}, Top={screen.Bounds.Top}");
//                System.Diagnostics.Debug.WriteLine($"📺 [投影屏幕] 工作区域: {screen.WorkingArea.Width}x{screen.WorkingArea.Height}");
//#endif

                // 检查是否是主显示器
                if (screen.Primary)
                {
                    // System.Diagnostics.Debug.WriteLine("❌ 选择的是主显示器");
                    WpfMessageBox.Show("不能投影到主显示器！", "警告", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return false;
                }
                
                // System.Diagnostics.Debug.WriteLine("✅ 准备创建投影窗口...");

                // 创建投影窗口
                _mainWindow.Dispatcher.Invoke(() =>
                {
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"📺 [投影窗口创建] 位置: Left={screen.Bounds.Left}, Top={screen.Bounds.Top}");
//                    System.Diagnostics.Debug.WriteLine($"📺 [投影窗口创建] 尺寸: {screen.Bounds.Width}x{screen.Bounds.Height}");
//#endif
                    
                    _projectionWindow = new Window
                    {
                        Title = "投影",
                        WindowStyle = WindowStyle.None,
                        ResizeMode = WpfResizeMode.NoResize,
                        WindowState = WindowState.Normal,
                        Background = WpfBrushes.Black,
                        Topmost = true,
                        ShowInTaskbar = false
                    };
                    
                    // 必须先设置内容再设置位置和大小，否则WPF可能会重置窗口位置

                    // 创建ScrollViewer
                    _projectionScrollViewer = new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                        Background = WpfBrushes.Black,
                        CanContentScroll = false // 🔧 使用像素级滚动，确保精确同步
                    };

                    // 创建容器Grid来控制滚动区域(类似主屏幕的imageContainer)
                    var projectionContainer = new Grid
                    {
                        Background = WpfBrushes.Black,
                        HorizontalAlignment = WpfHorizontalAlignment.Stretch,  // 容器水平拉伸填满
                        VerticalAlignment = System.Windows.VerticalAlignment.Top
                    };

                    // 创建Image控件 (初始设置,会在UpdateProjection中动态调整)
                    _projectionImageControl = new System.Windows.Controls.Image
                    {
                        Stretch = System.Windows.Media.Stretch.None,  // 🔧 不拉伸，使用原始尺寸，确保滚动精确
                        HorizontalAlignment = WpfHorizontalAlignment.Left,  // 🔧 左对齐
                        VerticalAlignment = System.Windows.VerticalAlignment.Top  // 顶部对齐
                    };

                    projectionContainer.Children.Add(_projectionImageControl);
                    _projectionScrollViewer.Content = projectionContainer;
                    
                    // 创建视频容器（叠加在图片容器上方，默认隐藏）
                    _projectionVideoContainer = new Grid
                    {
                        Background = WpfBrushes.Black,
                        Visibility = Visibility.Collapsed,  // 默认隐藏
                        HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                        VerticalAlignment = System.Windows.VerticalAlignment.Stretch
                    };
                    
                    // 创建VideoView控件
                    _projectionVideoView = new VideoView
                    {
                        HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                        VerticalAlignment = System.Windows.VerticalAlignment.Stretch
                    };
                    
                    // 在VideoView加载完成后触发事件
                    _projectionVideoView.Loaded += (s, e) =>
                    {
                        ProjectionVideoViewLoaded?.Invoke(this, _projectionVideoView);
                    };
                    
                    _projectionVideoContainer.Children.Add(_projectionVideoView);
                    
                    // 创建媒体文件名显示（用于纯音频文件，铺满整个区域）
                    _projectionMediaFileNameBorder = new Grid
                    {
                        Background = WpfBrushes.Black,
                        Visibility = Visibility.Collapsed
                    };
                    
                    var fileNameStack = new StackPanel
                    {
                        HorizontalAlignment = WpfHorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };
                    
                    var iconText = new TextBlock
                    {
                        Text = "🎵",
                        FontSize = 120,
                        HorizontalAlignment = WpfHorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 30),
                        Foreground = WpfBrushes.White
                    };
                    
                    _projectionMediaFileNameText = new TextBlock
                    {
                        Text = "媒体文件",
                        FontSize = 42,
                        FontWeight = FontWeights.Medium,
                        Foreground = WpfBrushes.White,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = WpfHorizontalAlignment.Center,
                        MaxWidth = 1200,
                        Padding = new Thickness(20, 0, 20, 0)
                    };
                    
                    fileNameStack.Children.Add(iconText);
                    fileNameStack.Children.Add(_projectionMediaFileNameText);
                    
                    _projectionMediaFileNameBorder.Children.Add(fileNameStack);
                    _projectionVideoContainer.Children.Add(_projectionMediaFileNameBorder);
                    
                    // 🔧 创建圣经标题层（固定在顶部，不滚动）
                    _projectionBibleTitleBorder = new Border
                    {
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)), // #1C1C1C
                        Padding = new Thickness(20, 15, 20, 15),
                        Visibility = Visibility.Collapsed,  // 默认隐藏
                        VerticalAlignment = System.Windows.VerticalAlignment.Top
                    };
                    System.Windows.Controls.Panel.SetZIndex(_projectionBibleTitleBorder, 100);  // 确保在最上层
                    
                    _projectionBibleTitleText = new TextBlock
                    {
                        Text = "",
                        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
                        FontSize = 32,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 87, 34)) // #FF5722
                    };
                    
                    _projectionBibleTitleBorder.Child = _projectionBibleTitleText;
                    
                    // 创建主Grid来容纳图片、视频和圣经标题
                    var mainGrid = new Grid
                    {
                        Background = WpfBrushes.Black
                    };
                    mainGrid.Children.Add(_projectionScrollViewer);  // 层0：可滚动内容
                    mainGrid.Children.Add(_projectionVideoContainer);  // 层1：视频
                    mainGrid.Children.Add(_projectionBibleTitleBorder);  // 层2：圣经标题（固定）
                    
                    _projectionWindow.Content = mainGrid;
                    
                    // 保存容器引用供后续使用
                    _projectionContainer = projectionContainer;

                    // 绑定键盘事件
                    _projectionWindow.KeyDown += ProjectionWindow_KeyDown;

                    // 绑定关闭事件
                    _projectionWindow.Closed += (s, e) => CloseProjection();
                    
                    // 重要：设置窗口位置和大小（必须在Show之前）
                    _projectionWindow.Left = screen.Bounds.Left;
                    _projectionWindow.Top = screen.Bounds.Top;
                    _projectionWindow.Width = screen.Bounds.Width;
                    _projectionWindow.Height = screen.Bounds.Height;
                    
                    // System.Diagnostics.Debug.WriteLine($"窗口位置已设置: Left={_projectionWindow.Left}, Top={_projectionWindow.Top}, Size={_projectionWindow.Width}x{_projectionWindow.Height}");

                    // 显示窗口
                    _projectionWindow.Show();
                    
                    // 再次确认窗口位置（WPF有时会自动调整）
                    _projectionWindow.Left = screen.Bounds.Left;
                    _projectionWindow.Top = screen.Bounds.Top;
                    
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"📺 [投影窗口显示] 显示后窗口位置: Left={_projectionWindow.Left}, Top={_projectionWindow.Top}");
//                    System.Diagnostics.Debug.WriteLine($"📺 [投影窗口显示] 显示后窗口尺寸: {_projectionWindow.ActualWidth}x{_projectionWindow.ActualHeight}");
//                    
//                    // 检测DPI
//                    var presentationSource = PresentationSource.FromVisual(_projectionWindow);
//                    if (presentationSource?.CompositionTarget != null)
//                    {
//                        double dpiScaleX = presentationSource.CompositionTarget.TransformToDevice.M11;
//                        double dpiScaleY = presentationSource.CompositionTarget.TransformToDevice.M22;
//                        System.Diagnostics.Debug.WriteLine($"📺 [投影窗口DPI] DPI缩放: X={dpiScaleX:F2}, Y={dpiScaleY:F2}");
//                        System.Diagnostics.Debug.WriteLine($"📺 [投影窗口DPI] 逻辑DPI: X={96 * dpiScaleX:F0}, Y={96 * dpiScaleY:F0}");
//                        System.Diagnostics.Debug.WriteLine($"📺 [投影窗口DPI] 物理像素: {_projectionWindow.ActualWidth * dpiScaleX:F0}x{_projectionWindow.ActualHeight * dpiScaleY:F0}");
//                    }
//#endif
                    
                    // 最大化到指定屏幕
                    _projectionWindow.WindowState = WindowState.Maximized;
                    
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"📺 [投影窗口显示] 最大化后窗口状态: State={_projectionWindow.WindowState}");
//                    System.Diagnostics.Debug.WriteLine($"📺 [投影窗口显示] 最大化后窗口尺寸: {_projectionWindow.ActualWidth}x{_projectionWindow.ActualHeight}");
//#endif
                    
                    // 确保窗口可以接收键盘焦点
                    _projectionWindow.Focusable = true;
                    _projectionWindow.Focus();
                    _projectionWindow.Activate();
                    
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine("✅ [投影窗口显示] 投影窗口已激活并获取焦点");
//#endif

                    // 🔧 从主窗口同步当前状态到投影（解决打开投影时图片为空的问题）
                    // 🎤 但如果处于歌词模式，跳过图片同步，避免显示图片
                    var mainWindow = _mainWindow as MainWindow;
                    bool isInLyricsMode = mainWindow?.IsInLyricsMode ?? false;
                    
                    if (_imageProcessor?.CurrentImage != null && !isInLyricsMode)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📺 [OpenProjection] 同步主窗口状态到投影:");
                        //#endif
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   图片: {_imageProcessor.CurrentImage.Width}x{_imageProcessor.CurrentImage.Height}");
                        //#endif
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   路径: {_imageProcessor.CurrentImagePath}");
                        //#endif
                        
                        // 直接设置内部状态（不触发预渲染）
                        _currentImage = _imageProcessor.CurrentImage;
                        _currentImagePath = _imageProcessor.CurrentImagePath;
                        _isColorEffectEnabled = _imageProcessor.IsInverted;
                        _zoomRatio = _imageProcessor.ZoomRatio;
                        _isOriginalMode = _imageProcessor.OriginalMode;
                        _originalDisplayMode = _imageProcessor.OriginalDisplayModeValue;
                        
                        // 更新投影内容
                        UpdateProjection();
                    }
//#if DEBUG
//                    else if (isInLyricsMode)
//                    {
//                        System.Diagnostics.Debug.WriteLine("🎤 [OpenProjection] 歌词模式，跳过图片同步");
//                    }
//#endif

                    // 启用同步
                    _syncEnabled = true;

                    // TODO: 设置全局热键
                });

                // 🔒 启动投影时间限制（未登录状态）
                if (!AuthService.Instance.IsAuthenticated)
                {
                    StartProjectionTimer();
                }
                else
                {
                    // 已登录但需要检查账号有效期
                    CheckAuthenticationPeriodically();
                }

                // 触发投影状态改变事件
                ProjectionStateChanged?.Invoke(this, true);

                return true;
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"投影开启失败: {ex.Message}", "错误", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 关闭投影窗口
        /// </summary>
        /// <returns>如果成功关闭了投影窗口返回true，如果没有投影窗口或关闭失败返回false</returns>
        public bool CloseProjection()
        {
            try
            {
                bool hadProjection = false;
                _syncEnabled = false;

                // 🔒 停止投影计时器
                StopProjectionTimer();

                _mainWindow.Dispatcher.Invoke(() =>
                {
                    if (_projectionWindow != null)
                    {
                        hadProjection = true;  // 标记有投影窗口需要关闭
                        _projectionWindow.KeyDown -= ProjectionWindow_KeyDown;
                        _projectionWindow.Close();
                        _projectionWindow = null;
                    }

                    _projectionScrollViewer = null;
                    _projectionContainer = null;
                    _projectionImageControl = null;
                    _projectionImage = null;
                });

                // TODO: 清理全局热键

                // 只有在真正关闭了投影窗口时才触发事件
                if (hadProjection)
                {
                    ProjectionStateChanged?.Invoke(this, false);
                }

                return hadProjection;  // 返回是否真正关闭了投影
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"关闭投影失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 更新投影内容
        /// </summary>
        private void UpdateProjection()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] ===== 开始更新投影 =====");
            //#endif
            
            if (_projectionWindow == null)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("⚠️ [UpdateProjection] 投影窗口为null");
                #endif
                return;
            }
            
            // 如果没有图片，可能是在播放视频，直接返回
            if (_currentImage == null)
            {
                return;
            }

            try
            {
                var invokeStart = sw.ElapsedMilliseconds;
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    var screen = _screens[_currentScreenIndex];
                    int screenWidth = screen.Bounds.Width;
                    int screenHeight = screen.Bounds.Height;

                    //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] 原图尺寸: {_currentImage.Width}x{_currentImage.Height}");
                    //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] 屏幕尺寸: {screenWidth}x{screenHeight}");
                    //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] 原图模式: {_isOriginalMode}, 显示模式: {_originalDisplayMode}, 变色: {_isColorEffectEnabled}, 缩放: {_zoomRatio}");

                    // 计算缩放后的尺寸
                    var calcStart = sw.ElapsedMilliseconds;
                    var (newWidth, newHeight) = CalculateImageSize(screenWidth, screenHeight);
                    var calcTime = sw.ElapsedMilliseconds - calcStart;
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] 计算尺寸: {calcTime}ms -> {newWidth}x{newHeight}");
                    //#endif

                    // ⚡ 生成缓存键
                    var keyStart = sw.ElapsedMilliseconds;
                    string cacheKey = GenerateProjectionCacheKey(newWidth, newHeight);
                    var keyTime = sw.ElapsedMilliseconds - keyStart;
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] 缓存键: {cacheKey}");
                    //#endif
                    
                    // ⚡ 检查缓存
                    var cacheCheckStart = sw.ElapsedMilliseconds;
                    if (_projectionCache.TryGetValue(cacheKey, out BitmapSource cachedBitmap))
                    {
                        var cacheTime = sw.ElapsedMilliseconds - cacheCheckStart;
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] ⚡ 缓存命中: {cacheTime}ms");
                        //#endif
                        _projectionImage = cachedBitmap;
                    }
                    else
                    {
                        var cacheTime = sw.ElapsedMilliseconds - cacheCheckStart;
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] 💾 缓存未命中: {cacheTime}ms，开始渲染...");
                        //#endif
                        
                        // 🎮 使用GPU加速渲染（缩放和可选的变色效果）
                        var renderStart = sw.ElapsedMilliseconds;
                        var processedImage = Core.GPUContext.Instance.ScaleImageGpu(
                            _currentImage, 
                            newWidth, 
                            newHeight, 
                            SKFilterQuality.High  // 保持最高质量
                        );
                        var renderTime = sw.ElapsedMilliseconds - renderStart;
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"⚡ [UpdateProjection GPU] 耗时: {renderTime}ms, 尺寸: {newWidth}x{newHeight}, 质量: High");
                        //#endif
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    ├─ GPU缩放: {renderTime}ms");
                        //#endif

                        if (processedImage == null)
                        {
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"❌ [UpdateProjection] 渲染失败");
                            #endif
                            return;
                        }

                        // 应用变色效果（CPU处理）
                        if (_isColorEffectEnabled)
                        {
                            var effectStart = sw.ElapsedMilliseconds;
                            _imageProcessor.ApplyYellowTextEffect(processedImage);
                            var effectTime = sw.ElapsedMilliseconds - effectStart;
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"    ├─ 变色效果: {effectTime}ms");
                            #endif
                        }

                        // 转换为BitmapSource
                        var convertStart = sw.ElapsedMilliseconds;
                        _projectionImage = ConvertToBitmapSource(processedImage);
                        processedImage.Dispose();
                        var convertTime = sw.ElapsedMilliseconds - convertStart;
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    ├─ 转换BitmapSource: {convertTime}ms");
                        //#endif
                        
                        // ⚡ 加入缓存
                        var cacheAddStart = sw.ElapsedMilliseconds;
                        var entryOptions = new MemoryCacheEntryOptions
                        {
                            // 按图片大小计算权重（1MB = 1权重单位）
                            Size = Math.Max(1, (newWidth * newHeight * 4) / (1024 * 1024)),
                            Priority = CacheItemPriority.Normal,
                            SlidingExpiration = TimeSpan.FromMinutes(10) // 10分钟未访问则过期
                        };
                        _projectionCache.Set(cacheKey, _projectionImage, entryOptions);
                        var cacheAddTime = sw.ElapsedMilliseconds - cacheAddStart;
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    └─ 加入缓存: {cacheAddTime}ms (权重: {entryOptions.Size})");
                        //#endif
                    }

                    //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] 更新Image控件: {newWidth}x{newHeight}");
                    // 更新Image控件
                    _projectionImageControl.Source = _projectionImage;
                    _projectionImageControl.Width = newWidth;
                    _projectionImageControl.Height = newHeight;

                    // 获取投影ScrollViewer的实际DIU尺寸用于居中计算
                    double containerWidth = _projectionScrollViewer?.ActualWidth ?? screenWidth;
                    double containerHeight = _projectionScrollViewer?.ActualHeight ?? screenHeight;
                    if (containerWidth <= 0) containerWidth = screenWidth;
                    if (containerHeight <= 0) containerHeight = screenHeight;
                    
                    // 计算居中位置 (完全模仿Python的逻辑,但使用DIU尺寸)
                    double x = Math.Max(0, (containerWidth - newWidth) / 2.0);
                    double y = Math.Max(0, (containerHeight - newHeight) / 2.0);
                    
                    // 设置对齐方式和位置 (模仿Python: anchor=tk.NW + 计算的x,y坐标)
                    _projectionImageControl.HorizontalAlignment = WpfHorizontalAlignment.Left;
                    _projectionImageControl.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                    
                    if (_isOriginalMode)
                    {
                        // 原图模式: 水平和垂直都居中 (Python: x=居中, y=居中)
                        _projectionImageControl.Margin = new System.Windows.Thickness(x, y, 0, 0);
                        // System.Diagnostics.Debug.WriteLine($"  📍 原图模式定位: 容器={containerWidth:F0}x{containerHeight:F0}, 图片={newWidth}x{newHeight}, 偏移=({x:F0},{y:F0})");
                    }
                    else
                    {
                        // 正常模式: 水平居中,垂直顶部 (Python: x=居中, y=0)
                        _projectionImageControl.Margin = new System.Windows.Thickness(x, 0, 0, 0);
                        // System.Diagnostics.Debug.WriteLine($"  📍 正常模式定位: 容器={containerWidth:F0}x{containerHeight:F0}, 图片={newWidth}x{newHeight}, 偏移=({x:F0},0)");
                    }

                    // 设置投影ScrollViewer的滚动区域 - 与主屏幕使用相同的逻辑!
                    // Python逻辑: 如果图片高度 > 屏幕高度,则滚动区域 = 图片高度 + 屏幕高度
                    // 这样可以将图片底部内容拉到顶部显示
                    if (_projectionContainer != null)
                    {
                        double scrollHeight;
                        if (_isOriginalMode)
                        {
                            // 原图模式
                            if (newHeight <= screenHeight)
                            {
                                scrollHeight = screenHeight;
                                _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                            }
                            else
                            {
                                scrollHeight = newHeight + screenHeight;
                                _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                            }
                        }
                        else
                        {
                            // 正常模式
                            // 注意: 即使图片高度等于屏幕高度,也需要额外空间以支持滚动到底部
                            if (newHeight >= screenHeight)
                            {
                                scrollHeight = newHeight + screenHeight;
                                _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                            }
                            else
                            {
                                scrollHeight = screenHeight;
                                _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                            }
                        }

                        // 设置容器高度来控制滚动区域(宽度拉伸填满屏幕)
                        _projectionContainer.Height = scrollHeight;
                        
                        //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] 投影滚动区域: 图片高度={newHeight}, 屏幕高度={screenHeight}, 滚动高度={scrollHeight}");
                    }
                    
                    var uiUpdateTime = sw.ElapsedMilliseconds - invokeStart;
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] UI更新: {uiUpdateTime}ms");
                    //#endif
                });
                
                sw.Stop();
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📺 [UpdateProjection] ===== 总耗时: {sw.ElapsedMilliseconds}ms =====\n");
                //#endif
            }
            catch (Exception ex)
            {
                sw.Stop();
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [UpdateProjection] 更新投影失败: {ex.Message} (耗时: {sw.ElapsedMilliseconds}ms)");
                //System.Diagnostics.Debug.WriteLine($"❌ [UpdateProjection] 堆栈: {ex.StackTrace}");
                //#else
                _ = ex; // 避免未使用变量警告
                //#endif
            }
        }

        /// <summary>
        /// 计算图片在投影屏幕上的尺寸
        /// </summary>
        private (int width, int height) CalculateImageSize(int screenWidth, int screenHeight)
        {
            // 关键: WPF使用设备独立单位(DIU),需要考虑DPI缩放
            // 获取投影ScrollViewer的实际DIU尺寸(如果已渲染)
            double canvasWidth = _projectionScrollViewer?.ActualWidth ?? screenWidth;
            double canvasHeight = _projectionScrollViewer?.ActualHeight ?? screenHeight;
            
            // 如果ScrollViewer还没有渲染,则假设使用屏幕物理尺寸
            if (canvasWidth <= 0) canvasWidth = screenWidth;
            if (canvasHeight <= 0) canvasHeight = screenHeight;
            
            // System.Diagnostics.Debug.WriteLine($"  📐 画布尺寸: 投影ScrollViewer={canvasWidth:F0}x{canvasHeight:F0} DIU (屏幕物理={screenWidth}x{screenHeight})");
            
            if (_isOriginalMode)
            {
                // 原图模式：根据显示模式和屏幕尺寸智能缩放
                double widthRatio = canvasWidth / _currentImage.Width;
                double heightRatio = canvasHeight / _currentImage.Height;
                
                double scaleRatio;

//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 原图模式 - 画布: {canvasWidth:F0}x{canvasHeight:F0}, 图片: {_currentImage.Width}x{_currentImage.Height}");
//                System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 宽度比例: {widthRatio:F4}, 高度比例: {heightRatio:F4}");
//#endif
                
                if (_originalDisplayMode == OriginalDisplayMode.Stretch)
                {
                    // 拉伸模式：使用高度比例,宽度会被拉伸填满屏幕
                    scaleRatio = heightRatio;
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 拉伸模式: 选择高度比例={scaleRatio:F4}");
//#endif
                }
                else
                {
                    // 适中模式：选择较小的比例确保完整显示(等比缩放)
                    scaleRatio = Math.Min(widthRatio, heightRatio);
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 适中模式: 选择较小比例={scaleRatio:F4}");
//#endif
                }

                // 智能缩放策略(放大限制)
                if (scaleRatio >= 1)
                {
                    // 图片小于屏幕，智能放大
                    double screenArea = canvasWidth * canvasHeight;
                    double imageArea = _currentImage.Width * _currentImage.Height;
                    double areaRatio = screenArea / imageArea;

                    double maxScale;
                    if (areaRatio > 16) maxScale = 6.0;
                    else if (areaRatio > 9) maxScale = 4.0;
                    else if (areaRatio > 4) maxScale = 3.0;
                    else maxScale = 2.0;

//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 放大限制 - 面积比: {areaRatio:F2}, 最大放大: {maxScale:F2}, 原始比例: {scaleRatio:F4}");
//#endif

                    scaleRatio = Math.Min(scaleRatio, maxScale);
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 应用放大限制后: {scaleRatio:F4}");
//#endif
                }

                // 关键修复: 拉伸模式下宽度填满ScrollViewer可用宽度(与主屏幕一致)
                int newWidth, newHeight;
                if (_originalDisplayMode == OriginalDisplayMode.Stretch)
                {
                    // 拉伸模式：宽度填满ScrollViewer可用宽度，高度按比例
                    newWidth = (int)canvasWidth;
                    newHeight = (int)(_currentImage.Height * scaleRatio);
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 拉伸模式计算 - 宽度=画布宽度={newWidth}, 高度={_currentImage.Height}x{scaleRatio:F4}={newHeight}");
//#endif
                }
                else
                {
                    // 适中模式：等比缩放
                    newWidth = (int)(_currentImage.Width * scaleRatio);
                    newHeight = (int)(_currentImage.Height * scaleRatio);
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 适中模式计算 - 等比缩放={newWidth}x{newHeight}");
//#endif
                }

//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 最终结果: {newWidth}x{newHeight}");
//#endif
                
                return (newWidth, newHeight);
            }
            else
            {
                // 正常模式：等比缩放宽度和高度（与主屏幕一致）
                double baseRatio = canvasWidth / _currentImage.Width;
                double finalRatio = baseRatio * _zoomRatio;

//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 正常模式 - 画布: {canvasWidth:F0}x{canvasHeight:F0}, 图片: {_currentImage.Width}x{_currentImage.Height}");
//                System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 基础比例: {baseRatio:F4}, 缩放比例: {_zoomRatio:F4}, 最终比例: {finalRatio:F4}");
//#endif
                
                // 等比缩放宽度和高度
                int newWidth = (int)(_currentImage.Width * finalRatio);
                int newHeight = (int)(_currentImage.Height * finalRatio);

//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 正常模式计算 - 等比缩放={newWidth}x{newHeight}");
//                System.Diagnostics.Debug.WriteLine($"  📊 [尺寸计算] 最终结果: {newWidth}x{newHeight}");
//#endif
                
                return (newWidth, newHeight);
            }
        }

        /// <summary>
        /// 生成投影缓存键
        /// </summary>
        private string GenerateProjectionCacheKey(int width, int height)
        {
            // 包含所有影响投影结果的参数
            return $"{_currentImagePath}_{width}x{height}_{(_isColorEffectEnabled ? "inverted" : "normal")}_{_isOriginalMode}_{_originalDisplayMode}_{_zoomRatio:F2}";
        }
        
        /// <summary>
        /// 清除投影缓存
        /// </summary>
        public void ClearProjectionCache()
        {
            if (_projectionCache is MemoryCache mc)
            {
                mc.Compact(1.0); // 清除100%的缓存项
                //System.Diagnostics.Debug.WriteLine("🧹 [投影缓存已清空]");
            }
        }
        
        /// <summary>
        /// 获取投影缓存统计信息
        /// </summary>
        public string GetProjectionCacheStats()
        {
            if (_projectionCache is MemoryCache mc)
            {
                var stats = mc.GetCurrentStatistics();
                return $"投影缓存项数: {stats?.CurrentEntryCount ?? 0}, 当前大小: {stats?.CurrentEstimatedSize ?? 0}";
            }
            return "投影缓存统计不可用";
        }
        
        /// <summary>
        /// 将SkiaSharp图片转换为WPF BitmapSource
        /// </summary>
        private WriteableBitmap ConvertToBitmapSource(SKBitmap skBitmap)
        {
            try
            {
                if (skBitmap == null)
                    return null;
                
                var info = skBitmap.Info;
                var wb = new WriteableBitmap(info.Width, info.Height, 96, 96, 
                                             System.Windows.Media.PixelFormats.Bgra32, null);
                
                wb.Lock();
                try
                {
                    unsafe
                    {
                        // ⚡ 直接复制像素数据，超快！
                        var src = skBitmap.GetPixels();
                        var dst = wb.BackBuffer;
                        var size = skBitmap.ByteCount;
                        
                        Buffer.MemoryCopy(
                            src.ToPointer(),
                            dst.ToPointer(),
                            size,
                            size
                        );
                    }
                    
                    wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, info.Width, info.Height));
                }
                finally
                {
                    wb.Unlock();
                }
                
                wb.Freeze();
                
                return wb;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 投影窗口键盘事件处理
        /// </summary>
        private void ProjectionWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                // 将所有快捷键转发到主窗口处理（包括视频播放快捷键）
                _mainWindow.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                    e.KeyboardDevice,
                    e.InputSource,
                    e.Timestamp,
                    e.Key)
                {
                    RoutedEvent = Window.PreviewKeyDownEvent
                });
                
                e.Handled = true;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 处理投影热键失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 导航到上一张图片
        /// </summary>
        private void NavigateToPreviousImage()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                // 调用主窗口的切换相似图片方法
                if (_mainWindow is MainWindow mainWindow)
                {
                    mainWindow.SwitchToPreviousSimilarImage();
                }
            });
        }

        /// <summary>
        /// 导航到下一张图片
        /// </summary>
        private void NavigateToNextImage()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                // 调用主窗口的切换相似图片方法
                if (_mainWindow is MainWindow mainWindow)
                {
                    mainWindow.SwitchToNextSimilarImage();
                }
            });
        }

        #endregion

        #region 投影时间限制

        /// <summary>
        /// 启动投影计时器（未登录状态的随机时间限制）
        /// 🔒 集成AuthService双重验证 + 本地校验和，三层防护
        /// </summary>
        private void StartProjectionTimer()
        {
            // 🔒 第1层：AuthService启动试用投影验证（生成加密令牌）
            AuthService.Instance.StartTrialProjection();
            
            // 🔒 第2层：本地记录（双重验证）
            _projectionStartTime = DateTime.Now;
            _projectionStartTick = Environment.TickCount64;
            
            // 🔒 第3层：生成本地校验和（额外防护）
            _localProjectionChecksum = GenerateLocalProjectionChecksum();
            
            // 创建计时器，每秒检查一次
            _projectionTimer = new System.Threading.Timer(
                CheckProjectionTimeLimit,
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1)
            );

            #if DEBUG
            int trialDuration = AuthService.Instance.GetTrialProjectionRemainingSeconds();
            if (trialDuration > 0)
            {
                System.Diagnostics.Debug.WriteLine($"⏰ [投影限制] 计时器已启动，{trialDuration}秒后自动关闭");
            }
            #endif
        }

        /// <summary>
        /// 停止投影计时器
        /// </summary>
        private void StopProjectionTimer()
        {
            _projectionTimer?.Dispose();
            _projectionTimer = null;
            _projectionStartTime = null;

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"⏰ [投影限制] 计时器已停止");
            #endif
        }

        /// <summary>
        /// 检查投影时间限制（定时器回调）
        /// 🔒 四重验证：登录状态 + AuthService令牌 + 本地时间 + 本地校验和
        /// </summary>
        private void CheckProjectionTimeLimit(object state)
        {
            if (!_projectionStartTime.HasValue || _projectionWindow == null)
            {
                StopProjectionTimer();
                return;
            }

            // 🔐 验证1：先检查登录状态
            if (AuthService.Instance.IsAuthenticated && AuthService.Instance.CanUseProjection())
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ [投影限制] 检测到已登录，取消时间限制");
                #endif
                StopProjectionTimer();
                AuthService.Instance.ResetTrialProjection();
                _localProjectionChecksum = null;
                return;
            }

            // 🔒 验证2：本地校验和验证（防止数据篡改）
            bool checksumValid = ValidateLocalProjectionChecksum();
            if (!checksumValid)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [投影限制] 本地校验和验证失败，数据可能被篡改！");
                #endif
                
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    CloseProjection();
                });
                
                StopProjectionTimer();
                AuthService.Instance.ResetTrialProjection();
                _localProjectionChecksum = null;
                return;
            }

            // 🔒 验证3：AuthService加密令牌验证（主要防护）
            bool authExpired = AuthService.Instance.IsTrialProjectionExpired();
            
            // 🔒 验证4：本地时间验证（辅助防护）
            long currentTick = Environment.TickCount64;
            long elapsedMilliseconds = currentTick - _projectionStartTick;
            int elapsedSeconds = (int)(elapsedMilliseconds / 1000);
            
            var elapsedByDateTime = (DateTime.Now - _projectionStartTime.Value).TotalSeconds;
            int actualElapsedSeconds = Math.Max(elapsedSeconds, (int)elapsedByDateTime);
            
            int trialDuration = GetTrialDurationSeconds();
            bool localExpired = actualElapsedSeconds >= trialDuration;

            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"⏰ [投影限制] 本地验证: {actualElapsedSeconds}/{trialDuration}秒, AuthService: {(authExpired ? "已过期" : "有效")}, 校验和: {(checksumValid ? "通过" : "失败")}");
            //
            //// 检测时间异常
            //if (Math.Abs(elapsedSeconds - elapsedByDateTime) > 5)
            //{
            //    System.Diagnostics.Debug.WriteLine($"⚠️ [投影限制] 检测到时间异常（差异{Math.Abs(elapsedSeconds - elapsedByDateTime):F1}秒）");
            //}
            //#endif

            // 🔒 任一验证失败，立即关闭投影
            if (authExpired || localExpired)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⏰ [投影限制] 时间已到，自动关闭投影");
                System.Diagnostics.Debug.WriteLine($"   - AuthService验证: {(authExpired ? "失败" : "通过")}");
                System.Diagnostics.Debug.WriteLine($"   - 本地时间验证: {(localExpired ? "失败" : "通过")}");
                System.Diagnostics.Debug.WriteLine($"   - 本地校验和: {(checksumValid ? "通过" : "失败")}");
                #endif

                _mainWindow.Dispatcher.Invoke(() =>
                {
                    CloseProjection();
                });

                StopProjectionTimer();
                AuthService.Instance.ResetTrialProjection();
                _localProjectionChecksum = null;
            }
        }

        /// <summary>
        /// 定期检查账号有效期（已登录状态）
        /// </summary>
        private void CheckAuthenticationPeriodically()
        {
            // 创建计时器，每20分钟检查一次账号状态
            _projectionTimer = new System.Threading.Timer(
                (state) =>
                {
                    if (_projectionWindow == null)
                    {
                        StopProjectionTimer();
                        return;
                    }

                    // 检查账号是否仍然有效
                    if (!AuthService.Instance.IsAuthenticated || !AuthService.Instance.CanUseProjection())
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"❌ [投影验证] 账号已失效，关闭投影");
                        #endif

                        _mainWindow.Dispatcher.Invoke(() =>
                        {
                            CloseProjection();
                            WpfMessageBox.Show(
                                "您的账号已过期，投影功能已自动关闭。",
                                "账号已过期",
                                WpfMessageBoxButton.OK,
                                WpfMessageBoxImage.Warning);
                        });

                        StopProjectionTimer();
                    }
                },
                null,
                TimeSpan.FromMinutes(5),  // 5分钟后首次检查
                TimeSpan.FromMinutes(20)  // 之后每20分钟检查一次
            );

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [投影验证] 已启动账号有效期监控（每20分钟检查一次）");
            #endif
        }

        /// <summary>
        /// 生成本地投影校验和（额外防护层，使用不同的密钥）
        /// </summary>
        private string GenerateLocalProjectionChecksum()
        {
            try
            {
                // 🔒 使用不同的密钥（与AuthService不同，增加破解难度）
                const string LOCAL_SECRET_KEY_1 = "ProjectionManager_Local_Checksum_2024";
                const string LOCAL_SECRET_KEY_2 = "MultiLayer_AntiCrack_Protection_System";
                
                var trialDuration = GetTrialDurationSeconds();
                var data = $"{LOCAL_SECRET_KEY_1}:{_projectionStartTick}:{trialDuration}:{Environment.ProcessorCount}:{LOCAL_SECRET_KEY_2}";
                
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
                    return Convert.ToBase64String(hashBytes);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 验证本地投影校验和
        /// </summary>
        private bool ValidateLocalProjectionChecksum()
        {
            if (string.IsNullOrEmpty(_localProjectionChecksum))
            {
                return false;
            }

            var expectedChecksum = GenerateLocalProjectionChecksum();
            return _localProjectionChecksum == expectedChecksum;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
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

