using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;
using ImageColorChanger.UI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfResizeMode = System.Windows.ResizeMode;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using Screen = System.Windows.Forms.Screen;

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

        // 屏幕管理
        private List<Screen> _screens;
        private int _currentScreenIndex;

        // 状态管理
        private bool _syncEnabled;
        // private bool _globalHotkeysEnabled; // TODO: 实现全局热键时再启用

        // 性能优化
        private DateTime _lastSyncTime;
        private TimeSpan _syncThrottleInterval = TimeSpan.FromMilliseconds(16); // 约60FPS

        // 当前状态
        private Image<Rgba32> _currentImage;
        private bool _isColorEffectEnabled;
        private double _zoomRatio = 1.0;
        private bool _isOriginalMode;
        private OriginalDisplayMode _originalDisplayMode = OriginalDisplayMode.Stretch;

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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化屏幕信息失败: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"✅ 默认选择屏幕索引: {defaultIndex} ({_screenComboBox.Items[defaultIndex]})");
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
                System.Diagnostics.Debug.WriteLine("🎬 ToggleProjection 被调用");
                System.Diagnostics.Debug.WriteLine($"当前投影窗口状态: {(_projectionWindow != null ? "已打开" : "未打开")}");
                System.Diagnostics.Debug.WriteLine($"当前图片: {(_currentImage != null ? $"{_currentImage.Width}x{_currentImage.Height}" : "null")}");
                System.Diagnostics.Debug.WriteLine($"屏幕数量: {_screens.Count}");
                
                if (_projectionWindow != null)
                {
                    System.Diagnostics.Debug.WriteLine("关闭投影窗口");
                    return CloseProjection();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("打开投影窗口");
                    return OpenProjection();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 切换投影失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                WpfMessageBox.Show($"投影失败: {ex.Message}", "错误", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 更新投影图片
        /// </summary>
        public void UpdateProjectionImage(Image<Rgba32> image, bool applyColorEffect, double zoomRatio, bool isOriginalMode, OriginalDisplayMode originalDisplayMode = OriginalDisplayMode.Stretch)
        {
            _currentImage = image;
            _isColorEffectEnabled = applyColorEffect;
            _zoomRatio = zoomRatio;
            _isOriginalMode = isOriginalMode;
            _originalDisplayMode = originalDisplayMode;

            if (_projectionWindow != null && image != null)
            {
                UpdateProjection();
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

                    System.Diagnostics.Debug.WriteLine($"📜 同步: 主屏滚动={mainScrollTop:F0}, 主屏图高={mainImgHeight:F0}, 原图相对={originalRelativePos:P1}, 投影图高={projImgHeight:F0}, 投影滚动={projScrollTop:F0}");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"同步投影滚动失败: {ex.Message}");
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

        #endregion

        #region 私有方法

        /// <summary>
        /// 打开投影窗口
        /// </summary>
        private bool OpenProjection()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("📂 OpenProjection 开始执行");
                
                // 检查是否有图片
                if (_currentImage == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 没有当前图片");
                    WpfMessageBox.Show("请先选中一张图片！", "警告", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return false;
                }
                
                System.Diagnostics.Debug.WriteLine($"✅ 当前图片尺寸: {_currentImage.Width}x{_currentImage.Height}");

                // 检查是否有多个屏幕
                System.Diagnostics.Debug.WriteLine($"屏幕数量: {_screens.Count}");
                if (_screens.Count < 2)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 只有一个屏幕");
                    WpfMessageBox.Show("未检测到第二个显示器！", "警告", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return false;
                }
                
                System.Diagnostics.Debug.WriteLine($"✅ 检测到 {_screens.Count} 个屏幕");

                // 获取选定的屏幕
                int selectedIndex = _screenComboBox?.SelectedIndex ?? 0;
                if (selectedIndex < 0 || selectedIndex >= _screens.Count)
                {
                    WpfMessageBox.Show("选定的显示器无效！", "错误", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    return false;
                }

                var screen = _screens[selectedIndex];
                _currentScreenIndex = selectedIndex;
                
                System.Diagnostics.Debug.WriteLine($"选择的屏幕: 索引={selectedIndex}, 是否主屏={screen.Primary}, 分辨率={screen.Bounds.Width}x{screen.Bounds.Height}");

                // 检查是否是主显示器
                if (screen.Primary)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 选择的是主显示器");
                    WpfMessageBox.Show("不能投影到主显示器！", "警告", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                    return false;
                }
                
                System.Diagnostics.Debug.WriteLine("✅ 准备创建投影窗口...");

                // 创建投影窗口
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"创建窗口，位置: Left={screen.Bounds.Left}, Top={screen.Bounds.Top}, Size={screen.Bounds.Width}x{screen.Bounds.Height}");
                    
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
                        Background = WpfBrushes.Black
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
                        Stretch = System.Windows.Media.Stretch.Fill,  // 填充模式
                        HorizontalAlignment = WpfHorizontalAlignment.Center,  // 默认居中
                        VerticalAlignment = System.Windows.VerticalAlignment.Top
                    };

                    projectionContainer.Children.Add(_projectionImageControl);
                    _projectionScrollViewer.Content = projectionContainer;
                    _projectionWindow.Content = _projectionScrollViewer;
                    
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
                    
                    System.Diagnostics.Debug.WriteLine($"窗口位置已设置: Left={_projectionWindow.Left}, Top={_projectionWindow.Top}, Size={_projectionWindow.Width}x{_projectionWindow.Height}");

                    // 显示窗口
                    _projectionWindow.Show();
                    
                    // 再次确认窗口位置（WPF有时会自动调整）
                    _projectionWindow.Left = screen.Bounds.Left;
                    _projectionWindow.Top = screen.Bounds.Top;
                    
                    System.Diagnostics.Debug.WriteLine($"显示后窗口位置: Left={_projectionWindow.Left}, Top={_projectionWindow.Top}");
                    
                    // 最大化到指定屏幕
                    _projectionWindow.WindowState = WindowState.Maximized;
                    
                    System.Diagnostics.Debug.WriteLine($"最大化后窗口状态: State={_projectionWindow.WindowState}");
                    
                    // 确保窗口可以接收键盘焦点
                    _projectionWindow.Focusable = true;
                    _projectionWindow.Focus();
                    _projectionWindow.Activate();
                    System.Diagnostics.Debug.WriteLine("✅ 投影窗口已激活并获取焦点");

                    // 更新投影内容
                    UpdateProjection();

                    // 启用同步
                    _syncEnabled = true;

                    // TODO: 设置全局热键
                });

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
        public bool CloseProjection()
        {
            try
            {
                _syncEnabled = false;

                _mainWindow.Dispatcher.Invoke(() =>
                {
                    if (_projectionWindow != null)
                    {
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

                // 触发投影状态改变事件
                ProjectionStateChanged?.Invoke(this, false);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"关闭投影失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 更新投影内容
        /// </summary>
        private void UpdateProjection()
        {
            if (_projectionWindow == null || _currentImage == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ UpdateProjection: 窗口或图片为null");
                return;
            }

            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    var screen = _screens[_currentScreenIndex];
                    int screenWidth = screen.Bounds.Width;
                    int screenHeight = screen.Bounds.Height;

                    System.Diagnostics.Debug.WriteLine($"🖼️ 更新投影图片:");
                    System.Diagnostics.Debug.WriteLine($"  原图尺寸: {_currentImage.Width}x{_currentImage.Height}");
                    System.Diagnostics.Debug.WriteLine($"  屏幕尺寸: {screenWidth}x{screenHeight}");
                    System.Diagnostics.Debug.WriteLine($"  原图模式: {_isOriginalMode}, 显示模式: {_originalDisplayMode}, 变色: {_isColorEffectEnabled}, 缩放: {_zoomRatio}");

                    // 计算缩放后的尺寸
                    var (newWidth, newHeight) = CalculateImageSize(screenWidth, screenHeight);
                    
                    System.Diagnostics.Debug.WriteLine($"  计算后尺寸: {newWidth}x{newHeight}");

                    // 处理图片（缩放和可选的变色效果）
                    var processedImage = _currentImage.Clone(ctx =>
                    {
                        ctx.Resize(newWidth, newHeight, KnownResamplers.Lanczos3);
                    });

                    // 应用变色效果
                    if (_isColorEffectEnabled)
                    {
                        processedImage = _imageProcessor.ApplyYellowTextEffect(processedImage);
                        System.Diagnostics.Debug.WriteLine("  ✨ 已应用变色效果");
                    }

                    // 转换为BitmapSource
                    _projectionImage = ConvertToBitmapSource(processedImage);
                    processedImage.Dispose();

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
                        System.Diagnostics.Debug.WriteLine($"  📍 原图模式定位: 容器={containerWidth:F0}x{containerHeight:F0}, 图片={newWidth}x{newHeight}, 偏移=({x:F0},{y:F0})");
                    }
                    else
                    {
                        // 正常模式: 水平居中,垂直顶部 (Python: x=居中, y=0)
                        _projectionImageControl.Margin = new System.Windows.Thickness(x, 0, 0, 0);
                        System.Diagnostics.Debug.WriteLine($"  📍 正常模式定位: 容器={containerWidth:F0}x{containerHeight:F0}, 图片={newWidth}x{newHeight}, 偏移=({x:F0},0)");
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
                        
                        System.Diagnostics.Debug.WriteLine($"  📏 投影滚动区域: 图片高度={newHeight}, 屏幕高度={screenHeight}, 滚动高度={scrollHeight}");
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"  ✅ 投影图片已更新");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 更新投影失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"堆栈: {ex.StackTrace}");
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
            
            System.Diagnostics.Debug.WriteLine($"  📐 画布尺寸: 投影ScrollViewer={canvasWidth:F0}x{canvasHeight:F0} DIU (屏幕物理={screenWidth}x{screenHeight})");
            
            if (_isOriginalMode)
            {
                // 原图模式：根据显示模式和屏幕尺寸智能缩放
                double widthRatio = canvasWidth / _currentImage.Width;
                double heightRatio = canvasHeight / _currentImage.Height;
                
                double scaleRatio;
                
                if (_originalDisplayMode == OriginalDisplayMode.Stretch)
                {
                    // 拉伸模式：使用高度比例,宽度会被拉伸填满屏幕
                    scaleRatio = heightRatio;
                    System.Diagnostics.Debug.WriteLine($"  原图-拉伸模式: 宽比={widthRatio:F2}, 高比={heightRatio:F2}, 选择高比={scaleRatio:F2}");
                }
                else
                {
                    // 适中模式：选择较小的比例确保完整显示(等比缩放)
                    scaleRatio = Math.Min(widthRatio, heightRatio);
                    System.Diagnostics.Debug.WriteLine($"  原图-适中模式: 宽比={widthRatio:F2}, 高比={heightRatio:F2}, 选择较小比={scaleRatio:F2}");
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

                    scaleRatio = Math.Min(scaleRatio, maxScale);
                    System.Diagnostics.Debug.WriteLine($"  放大限制: 面积比={areaRatio:F2}, 最大放大={maxScale:F2}, 最终比={scaleRatio:F2}");
                }

                // 关键修复: 拉伸模式下宽度填满ScrollViewer可用宽度(与主屏幕一致)
                int newWidth, newHeight;
                if (_originalDisplayMode == OriginalDisplayMode.Stretch)
                {
                    // 拉伸模式：宽度填满ScrollViewer可用宽度，高度按比例
                    newWidth = (int)canvasWidth;
                    newHeight = (int)(_currentImage.Height * scaleRatio);
                    System.Diagnostics.Debug.WriteLine($"  拉伸计算: 宽度=画布宽度={newWidth}, 高度={_currentImage.Height}*{scaleRatio:F2}={newHeight}");
                }
                else
                {
                    // 适中模式：等比缩放
                    newWidth = (int)(_currentImage.Width * scaleRatio);
                    newHeight = (int)(_currentImage.Height * scaleRatio);
                    System.Diagnostics.Debug.WriteLine($"  适中计算: 等比缩放={newWidth}x{newHeight}");
                }
                
                return (newWidth, newHeight);
            }
            else
            {
                // 正常模式：宽度填满ScrollViewer,高度按比例(与主屏幕一致)
                double baseRatio = canvasWidth / _currentImage.Width;
                double finalRatio = baseRatio * _zoomRatio;
                
                int newWidth = (int)canvasWidth;  // 宽度填满
                int newHeight = (int)(_currentImage.Height * finalRatio);
                
                System.Diagnostics.Debug.WriteLine($"  正常模式缩放: 宽度填满={newWidth}, 高度={_currentImage.Height}*{finalRatio:F2}={newHeight}");
                
                return (newWidth, newHeight);
            }
        }

        /// <summary>
        /// 将ImageSharp图片转换为WPF BitmapSource
        /// </summary>
        private BitmapSource ConvertToBitmapSource(Image<Rgba32> image)
        {
            using var ms = new System.IO.MemoryStream();
            image.SaveAsPng(ms);
            ms.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }

        /// <summary>
        /// 投影窗口键盘事件处理
        /// </summary>
        private void ProjectionWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                switch (e.Key)
                {
                    case Key.Escape:
                        // ESC - 关闭投影
                        System.Diagnostics.Debug.WriteLine("⌨️ 投影热键: ESC - 关闭投影");
                        CloseProjection();
                        e.Handled = true;
                        break;

                    case Key.PageUp:
                        // PageUp - 上一张图片
                        System.Diagnostics.Debug.WriteLine("⌨️ 投影热键: PageUp - 上一张");
                        NavigateToPreviousImage();
                        e.Handled = true;
                        break;

                    case Key.PageDown:
                    case Key.Space:  // 空格键也可以翻页
                        // PageDown/Space - 下一张图片
                        System.Diagnostics.Debug.WriteLine("⌨️ 投影热键: PageDown/Space - 下一张");
                        NavigateToNextImage();
                        e.Handled = true;
                        break;

                    case Key.Up:
                        // 上方向键 - 向上滚动
                        _projectionScrollViewer?.LineUp();
                        e.Handled = true;
                        break;

                    case Key.Down:
                        // 下方向键 - 向下滚动
                        _projectionScrollViewer?.LineDown();
                        e.Handled = true;
                        break;

                    case Key.Home:
                        // Home - 滚动到顶部
                        _projectionScrollViewer?.ScrollToTop();
                        e.Handled = true;
                        break;

                    case Key.End:
                        // End - 滚动到底部
                        _projectionScrollViewer?.ScrollToBottom();
                        e.Handled = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 处理投影热键失败: {ex.Message}");
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

        #region IDisposable

        public void Dispose()
        {
            CloseProjection();
        }

        #endregion
    }
}

