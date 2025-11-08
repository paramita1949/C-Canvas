using System;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Core;
using ImageColorChanger.Utils;
using SkiaSharp;
using WpfBorder = System.Windows.Controls.Border;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfGrid = System.Windows.Controls.Grid;
using WpfImage = System.Windows.Controls.Image;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfThumb = System.Windows.Controls.Primitives.Thumb;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKey = System.Windows.Input.Key;
using WpfMouseButton = System.Windows.Input.MouseButton;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 可拖拽、可调整大小的文本框控件（使用SkiaSharp渲染）
    /// </summary>
    public class DraggableTextBox : WpfUserControl
    {
        #region 字段

        private bool _isDragging = false;
        private WpfPoint _dragStartPoint;
        private WpfBorder _border;
        
        // ✅ 新增：SkiaSharp渲染相关
        private WpfImage _renderImage;              // 显示SkiaSharp渲染结果
        private WpfTextBox _editTextBox;            // 编辑模式下临时使用
        private bool _isEditing = false;            // 是否处于编辑模式
        private readonly SkiaTextRenderer _skiaRenderer;
        
        private WpfThumb _resizeThumb;  // 右下角（保留兼容性）
        private WpfThumb _resizeThumbTopLeft;     // 上左
        private WpfThumb _resizeThumbTopCenter;   // 上中
        private WpfThumb _resizeThumbTopRight;    // 上右
        private WpfThumb _resizeThumbBottomLeft;  // 下左
        private WpfThumb _resizeThumbBottomCenter; // 下中
        private WpfThumb _resizeThumbBottomRight; // 下右（即原_resizeThumb）
        private System.Windows.Shapes.Rectangle _selectionRect;  // 虚线选中框
        private bool _isPlaceholderText = false;  // 标记是否是占位符文字
        private const string DEFAULT_PLACEHOLDER = "双击编辑文字";  // 默认占位符
        private DateTime _lastClickTime = DateTime.MinValue;  // 记录上次点击时间，用于双击检测
        private const int DOUBLE_CLICK_INTERVAL = 500;  // 双击间隔（毫秒）
        private bool _isNewlyCreated = false;  // 标记是否是新创建的文本框

        #endregion

        #region 属性

        /// <summary>
        /// 绑定的数据模型
        /// </summary>
        public TextElement Data { get; set; }

        /// <summary>
        /// 是否被选中
        /// </summary>
        public bool IsSelected { get; private set; }
        
        /// <summary>
        /// 是否处于编辑模式
        /// </summary>
        public bool IsInEditMode => _isEditing;
        
        /// <summary>
        /// 获取内部TextBox控件(用于圣经经文插入等功能)
        /// </summary>
        public WpfTextBox InternalTextBox => _editTextBox;
        
        /// <summary>
        /// 标记为新创建的文本框（用于自动进入编辑模式）
        /// </summary>
        public bool IsNewlyCreated
        {
            get => _isNewlyCreated;
            set => _isNewlyCreated = value;
        }

        #endregion

        #region 事件

        /// <summary>
        /// 位置改变事件（用于对称联动）
        /// </summary>
        public event EventHandler<WpfPoint> PositionChanged;

        /// <summary>
        /// 尺寸改变事件
        /// </summary>
        public new event EventHandler<WpfSize> SizeChanged;

        /// <summary>
        /// 内容改变事件
        /// </summary>
        public event EventHandler<string> ContentChanged;

        /// <summary>
        /// 选中状态改变事件
        /// </summary>
        public event EventHandler<bool> SelectionChanged;

        /// <summary>
        /// 拖动结束事件
        /// </summary>
        public event EventHandler DragEnded;

        /// <summary>
        /// 请求删除事件（由右键菜单或DEL键触发）
        /// </summary>
        public event EventHandler RequestDelete;

        /// <summary>
        /// 请求复制事件（由右键菜单触发）
        /// </summary>
        public event EventHandler RequestCopy;

        #endregion

        #region 构造函数

        public DraggableTextBox(TextElement element, SkiaTextRenderer renderer)
        {
            Data = element ?? throw new ArgumentNullException(nameof(element));
            _skiaRenderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            InitializeComponent();
            LoadFromData();
            RenderTextBox(); // 初始渲染
        }

        #endregion

        #region 初始化

        private void InitializeComponent()
        {
            // 主边框（默认完全透明）
            _border = new WpfBorder
            {
                BorderBrush = WpfBrushes.Transparent,
                BorderThickness = new System.Windows.Thickness(2),
                Background = WpfBrushes.Transparent,  // 完全透明
                CornerRadius = new System.Windows.CornerRadius(3)
            };

            var grid = new WpfGrid
            {
                Background = WpfBrushes.Transparent  // 设置透明背景，使鼠标事件能够穿透
            };

            // ✅ 显示层：Image控件（显示SkiaSharp渲染结果）
            _renderImage = new WpfImage
            {
                Stretch = System.Windows.Media.Stretch.Fill,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };
            grid.Children.Add(_renderImage);
            
            // ✅ 编辑层：TextBox（默认隐藏）
            _editTextBox = new WpfTextBox
            {
                Visibility = System.Windows.Visibility.Collapsed,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                AcceptsReturn = true,
                BorderThickness = new System.Windows.Thickness(0),
                BorderBrush = WpfBrushes.Transparent,
                Background = WpfBrushes.Transparent,
                Foreground = WpfBrushes.Transparent,  // 🔧 透明文字（只显示光标）
                Padding = new System.Windows.Thickness(5),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                FocusVisualStyle = null,
                IsEnabled = true,
                Focusable = true,
                CaretBrush = new WpfSolidColorBrush(WpfColor.FromRgb(0, 150, 255))  // 亮蓝色光标
            };
            
            // 禁用拼写检查
            _editTextBox.SpellCheck.IsEnabled = false;
            
            // 监听文本变化 - 实时渲染（不调整外框大小）
            _editTextBox.TextChanged += (s, e) =>
            {
                Data.Content = _editTextBox.Text;
                
                // 🔧 实时渲染（所见即所得）
                if (_isEditing)
                {
                    RenderTextBox();
                }
                
                ContentChanged?.Invoke(this, _editTextBox.Text);
            };
            
            // 监听焦点事件
            _editTextBox.GotFocus += (s, e) =>
            {
                _editTextBox.Cursor = WpfCursors.IBeam;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"✏️ [DraggableTextBox] 进入编辑模式");
#endif
                // 如果是占位符文字，清空内容
                if (_isPlaceholderText)
                {
                    _editTextBox.Text = "";
                    Data.Content = "";
                    _editTextBox.Foreground = new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(Data.FontColor));
                    _isPlaceholderText = false;
                }
            };
            
            _editTextBox.LostFocus += (s, e) =>
            {
                _editTextBox.Cursor = WpfCursors.Arrow;
                ExitEditMode();
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"💾 [DraggableTextBox] 退出编辑模式");
#endif
            };
            
            grid.Children.Add(_editTextBox);

            // 虚线选中框（叠加在文本框上方）
            _selectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = WpfBrushes.DodgerBlue,
                StrokeThickness = 4,
                StrokeDashArray = new System.Windows.Media.DoubleCollection { 5, 3 },
                Fill = WpfBrushes.Transparent,
                IsHitTestVisible = false,
                Visibility = System.Windows.Visibility.Collapsed
            };

            // 创建6个调整大小手柄
            _resizeThumbTopLeft = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Left,
                System.Windows.VerticalAlignment.Top,
                WpfCursors.SizeNWSE,
                new System.Windows.Thickness(-6, -6, 0, 0)
            );
            _resizeThumbTopLeft.DragDelta += (s, e) => ResizeFromCorner(e, -1, -1, true, true);
            
            _resizeThumbTopCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Center,
                System.Windows.VerticalAlignment.Top,
                WpfCursors.SizeNS,
                new System.Windows.Thickness(0, -6, 0, 0)
            );
            _resizeThumbTopCenter.DragDelta += (s, e) => ResizeFromEdge(e, 0, -1, false, true);
            
            _resizeThumbTopRight = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Right,
                System.Windows.VerticalAlignment.Top,
                WpfCursors.SizeNESW,
                new System.Windows.Thickness(0, -6, -6, 0)
            );
            _resizeThumbTopRight.DragDelta += (s, e) => ResizeFromCorner(e, 1, -1, false, true);
            
            _resizeThumbBottomLeft = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Left,
                System.Windows.VerticalAlignment.Bottom,
                WpfCursors.SizeNESW,
                new System.Windows.Thickness(-6, 0, 0, -6)
            );
            _resizeThumbBottomLeft.DragDelta += (s, e) => ResizeFromCorner(e, -1, 1, true, false);
            
            _resizeThumbBottomCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Center,
                System.Windows.VerticalAlignment.Bottom,
                WpfCursors.SizeNS,
                new System.Windows.Thickness(0, 0, 0, -6)
            );
            _resizeThumbBottomCenter.DragDelta += (s, e) => ResizeFromEdge(e, 0, 1, false, false);
            
            _resizeThumbBottomRight = new WpfThumb
            {
                Width = 12,
                Height = 12,
                Background = WpfBrushes.DodgerBlue,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Cursor = WpfCursors.SizeNWSE,
                Margin = new System.Windows.Thickness(0, 0, -6, -6),
                Visibility = System.Windows.Visibility.Collapsed
            };
            _resizeThumbBottomRight.DragDelta += (s, e) => ResizeFromCorner(e, 1, 1, false, false);
            _resizeThumb = _resizeThumbBottomRight; // 兼容性别名

            grid.Children.Add(_selectionRect);
            grid.Children.Add(_resizeThumbTopLeft);
            grid.Children.Add(_resizeThumbTopCenter);
            grid.Children.Add(_resizeThumbTopRight);
            grid.Children.Add(_resizeThumbBottomLeft);
            grid.Children.Add(_resizeThumbBottomCenter);
            grid.Children.Add(_resizeThumbBottomRight);
            _border.Child = grid;

            Content = _border;

            // 设置控件属性
            Focusable = true;
            Cursor = WpfCursors.SizeAll;
            FocusVisualStyle = null;

            // 绑定事件
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
            MouseRightButtonDown += OnMouseRightButtonDown;
            GotFocus += OnGotFocus;
            LostFocus += OnLostFocus;
            KeyDown += OnKeyDown;
            
            // 监听尺寸变化，退出编辑模式后才重新渲染
            base.SizeChanged += (s, e) =>
            {
                if (!_isEditing && e.NewSize.Width > 0 && e.NewSize.Height > 0)
                {
                    RenderTextBox();
                }
            };
        }

        /// <summary>
        /// 从数据模型加载显示
        /// </summary>
        private void LoadFromData()
        {
            // 位置和尺寸
            WpfCanvas.SetLeft(this, Data.X);
            WpfCanvas.SetTop(this, Data.Y);
            Width = Data.Width;
            Height = Data.Height;
            WpfPanel.SetZIndex(this, Data.ZIndex);

            // 检查是否是占位符文字
            _isPlaceholderText = (Data.Content == DEFAULT_PLACEHOLDER);
        }

        #endregion

        #region 字体名称处理
        
        /// <summary>
        /// 清理字体名称：从 WPF 格式转换为纯字体名称
        /// WPF 格式：./CCanvas_Fonts/思源宋体-Regular.ttf#思源宋体
        /// 纯字体名：思源宋体
        /// </summary>
        private string CleanFontFamilyName(string fontFamily)
        {
            if (string.IsNullOrEmpty(fontFamily))
                return "Microsoft YaHei UI";
            
            // 检查是否是 WPF 格式 (包含 # 符号)
            if (fontFamily.Contains("#"))
            {
                // 提取 # 后面的字体名称
                int hashIndex = fontFamily.IndexOf('#');
                string cleanName = fontFamily.Substring(hashIndex + 1);
                
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔧 [DraggableTextBox] 字体名称清理: {fontFamily} -> {cleanName}");
#endif
                
                return cleanName;
            }
            
            return fontFamily;
        }
        
        #endregion

        #region SkiaSharp渲染

        /// <summary>
        /// 渲染文本框（使用SkiaSharp）
        /// </summary>
        private void RenderTextBox()
        {
            // 🔧 允许编辑模式下也渲染（实时预览）
            if (ActualWidth <= 0 || ActualHeight <= 0)
                return;
            
            try
            {
                // 🔧 清理字体名称：移除 WPF 格式 (./CCanvas_Fonts/xxx.ttf#字体名)
                string cleanFontFamily = CleanFontFamilyName(Data.FontFamily);
                
                var context = new TextBoxRenderContext
                {
                    Text = Data.Content,
                    Size = new SKSize((float)ActualWidth, (float)ActualHeight),
                    Style = new TextStyle
                    {
                        FontFamily = cleanFontFamily,
                        FontSize = (float)Data.FontSize * 2,  // 🔧 渲染时放大2倍（与编辑模式一致）
                        TextColor = TextStyle.ParseColor(Data.FontColor),
                        IsBold = Data.IsBoldBool,
                        LineSpacing = 1.2f
                    },
                    Alignment = SkiaWpfHelper.ToSkTextAlign(Data.TextAlign),
                    Padding = new SKRect(5f, 5f, 5f, 5f), // 小边距
                    BackgroundColor = null // 透明背景
                };
                
                var bitmap = _skiaRenderer.RenderTextBox(context);
                _renderImage.Source = SkiaWpfHelper.ConvertToWpfBitmap(bitmap);
                
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"🎨 [DraggableTextBox] 渲染完成: {ActualWidth}x{ActualHeight}");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [DraggableTextBox] 渲染失败: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        #endregion

        #region 编辑模式

        /// <summary>
        /// 进入编辑模式（双击时）
        /// </summary>
        private void EnterEditMode(bool selectAll = true)
        {
            _isEditing = true;
            
            // 🔧 两层都显示：底层SkiaSharp渲染，顶层透明TextBox用于输入
            _renderImage.Visibility = System.Windows.Visibility.Visible;
            _editTextBox.Visibility = System.Windows.Visibility.Visible;
            _editTextBox.Text = Data.Content;
            _editTextBox.FontSize = Data.FontSize * 2;  // 实际渲染时放大2倍
            _editTextBox.FontFamily = new WpfFontFamily(Data.FontFamily);
            
            // 🔧 编辑框文字保持透明（只显示光标和选区）
            _editTextBox.Foreground = WpfBrushes.Transparent;
            
            _editTextBox.FontWeight = Data.IsBoldBool ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
            
            _editTextBox.TextAlignment = Data.TextAlign switch
            {
                "Center" => System.Windows.TextAlignment.Center,
                "Right" => System.Windows.TextAlignment.Right,
                _ => System.Windows.TextAlignment.Left
            };
            
            // 聚焦并处理光标
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool focused = _editTextBox.Focus();
                if (focused)
                {
                    if (selectAll)
                    {
                        _editTextBox.SelectAll();
                    }
                    else
                    {
                        _editTextBox.SelectionStart = _editTextBox.Text.Length;
                        _editTextBox.SelectionLength = 0;
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>
        /// 退出编辑模式（失去焦点或按Esc时）
        /// </summary>
        public void ExitEditMode()
        {
            if (!_isEditing)
                return;
            
            _isEditing = false;
            
            // 保存编辑内容
            string newContent = _editTextBox.Text;
            
            // 检查是否为空，如果为空则恢复占位符
            if (string.IsNullOrWhiteSpace(newContent))
            {
                newContent = DEFAULT_PLACEHOLDER;
                _isPlaceholderText = true;
            }
            else
            {
                _isPlaceholderText = false;
            }
            
            if (newContent != Data.Content)
            {
                Data.Content = newContent;
                ContentChanged?.Invoke(this, newContent);
            }
            
            _editTextBox.Visibility = System.Windows.Visibility.Collapsed;
            _renderImage.Visibility = System.Windows.Visibility.Visible;
            
            // 重新渲染
            RenderTextBox();
        }

        #endregion

        #region 拖拽功能

        private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == WpfMouseButton.Left)
            {
                // 优化双击检测逻辑
                var now = DateTime.Now;
                var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;
                bool isDoubleClick = timeSinceLastClick < DOUBLE_CLICK_INTERVAL;
                _lastClickTime = now;
                
                // 如果是双击，进入编辑模式
                if (isDoubleClick)
                {
                    Focus();
                    SetSelected(true);
                    
                    // 双击时：如果是占位符或新建的框，全选
                    bool shouldSelectAll = _isPlaceholderText || _isNewlyCreated;
                    EnterEditMode(selectAll: shouldSelectAll);
                    _isNewlyCreated = false;
                    e.Handled = true;
                    return;
                }
                
                // 如果已经在编辑模式，不做处理
                if (_isEditing)
                {
                    return;
                }
                
                // 单击：选中控件
                Focus();
                SetSelected(true);
                
                // 启动拖拽
                _isDragging = true;
                _dragStartPoint = e.GetPosition(Parent as System.Windows.UIElement);
                CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging && IsMouseCaptured)
            {
                WpfPoint currentPoint = e.GetPosition(Parent as System.Windows.UIElement);
                double deltaX = currentPoint.X - _dragStartPoint.X;
                double deltaY = currentPoint.Y - _dragStartPoint.Y;

                double newX = Data.X + deltaX;
                double newY = Data.Y + deltaY;
                
                Data.X = newX;
                Data.Y = newY;

                WpfCanvas.SetLeft(this, Data.X);
                WpfCanvas.SetTop(this, Data.Y);

                _dragStartPoint = currentPoint;

                PositionChanged?.Invoke(this, new WpfPoint(Data.X, Data.Y));
            }
        }

        private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                DragEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Focus();
            SetSelected(true);

            var contextMenu = new System.Windows.Controls.ContextMenu();
            
            try
            {
                var style = System.Windows.Application.Current.MainWindow?.FindResource("NoBorderContextMenuStyle") as System.Windows.Style;
                if (style != null)
                {
                    contextMenu.Style = style;
                }
            }
            catch
            {
                contextMenu.FontSize = 14;
                contextMenu.BorderThickness = new System.Windows.Thickness(0);
                contextMenu.Background = new WpfSolidColorBrush(WpfColor.FromRgb(45, 45, 48));
                contextMenu.Foreground = WpfBrushes.White;
            }

            var copyItem = new System.Windows.Controls.MenuItem
            {
                Header = "复制",
                Height = 36
            };
            copyItem.Click += (s, args) =>
            {
                RequestCopy?.Invoke(this, EventArgs.Empty);
            };
            contextMenu.Items.Add(copyItem);

            var deleteItem = new System.Windows.Controls.MenuItem
            {
                Header = "删除",
                Height = 36
            };
            deleteItem.Click += (s, args) =>
            {
                RequestDelete?.Invoke(this, EventArgs.Empty);
            };
            contextMenu.Items.Add(deleteItem);

            contextMenu.PlacementTarget = this;
            contextMenu.IsOpen = true;
            
            e.Handled = true;
        }

        #endregion

        #region 调整大小功能

        private WpfThumb CreateResizeThumb(
            System.Windows.HorizontalAlignment hAlign,
            System.Windows.VerticalAlignment vAlign,
            System.Windows.Input.Cursor cursor,
            System.Windows.Thickness margin)
        {
            var thumb = new WpfThumb
            {
                Width = 12,
                Height = 12,
                Background = WpfBrushes.DodgerBlue,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign,
                Cursor = cursor,
                Margin = margin,
                Visibility = System.Windows.Visibility.Collapsed
            };
            
            // 监听拖拽结束事件，重新渲染
            thumb.DragCompleted += (s, e) =>
            {
                RenderTextBox();
            };
            
            return thumb;
        }

        private void ResizeFromCorner(
            System.Windows.Controls.Primitives.DragDeltaEventArgs e,
            int xDir, int yDir, bool adjustX, bool adjustY)
        {
            double newWidth = Width + (e.HorizontalChange * xDir);
            double newHeight = Height + (e.VerticalChange * yDir);

            if (newWidth > 50)
            {
                Width = newWidth;
                Data.Width = newWidth;
                
                if (adjustX)
                {
                    double newX = Data.X - (e.HorizontalChange * xDir);
                    WpfCanvas.SetLeft(this, newX);
                    Data.X = newX;
                }
            }

            if (newHeight > 30)
            {
                Height = newHeight;
                Data.Height = newHeight;
                
                if (adjustY)
                {
                    double newY = Data.Y - (e.VerticalChange * yDir);
                    WpfCanvas.SetTop(this, newY);
                    Data.Y = newY;
                }
            }

            SizeChanged?.Invoke(this, new WpfSize(Width, Height));
        }

        private void ResizeFromEdge(
            System.Windows.Controls.Primitives.DragDeltaEventArgs e,
            int xDir, int yDir, bool adjustX, bool adjustY)
        {
            if (xDir != 0)
            {
                double newWidth = Width + (e.HorizontalChange * xDir);
                if (newWidth > 50)
                {
                    Width = newWidth;
                    Data.Width = newWidth;
                    
                    if (adjustX)
                    {
                        double newX = Data.X - (e.HorizontalChange * xDir);
                        WpfCanvas.SetLeft(this, newX);
                        Data.X = newX;
                    }
                }
            }

            if (yDir != 0)
            {
                double newHeight = Height + (e.VerticalChange * yDir);
                if (newHeight > 30)
                {
                    Height = newHeight;
                    Data.Height = newHeight;
                    
                    if (adjustY)
                    {
                        double newY = Data.Y - (e.VerticalChange * yDir);
                        WpfCanvas.SetTop(this, newY);
                        Data.Y = newY;
                    }
                }
            }

            SizeChanged?.Invoke(this, new WpfSize(Width, Height));
        }

        #endregion

        #region 选中状态

        private void OnGotFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            SetSelected(true);
        }

        private void OnLostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            // 不在这里取消选中，由外部控制
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            
            if (selected)
            {
                _selectionRect.Visibility = System.Windows.Visibility.Visible;
                _border.Background = new WpfSolidColorBrush(WpfColor.FromArgb(20, 33, 150, 243));
                
                _resizeThumbTopLeft.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbTopCenter.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbTopRight.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbBottomLeft.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbBottomCenter.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbBottomRight.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                _selectionRect.Visibility = System.Windows.Visibility.Collapsed;
                _border.Background = WpfBrushes.Transparent;
                
                _resizeThumbTopLeft.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbTopCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbTopRight.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbBottomLeft.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbBottomCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbBottomRight.Visibility = System.Windows.Visibility.Collapsed;
            }

            SelectionChanged?.Invoke(this, selected);
        }

        #endregion

        #region 键盘事件

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isEditing)
            {
                if (e.Key == WpfKey.Delete)
                {
                    RequestDelete?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
            }
            else
            {
                if (e.Key == WpfKey.Escape)
                {
                    System.Windows.Input.Keyboard.ClearFocus();
                    Focus();
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 应用字体（接受FontFamily对象，不改变Data）
        /// </summary>
        public void ApplyFontFamily(System.Windows.Media.FontFamily fontFamily)
        {
            if (fontFamily != null)
            {
                Data.FontFamily = fontFamily.Source;
                RenderTextBox();
            }
        }

        /// <summary>
        /// 应用样式（由工具栏调用）
        /// </summary>
        public void ApplyStyle(string fontFamily = null, double? fontSize = null, 
                               string color = null, bool? isBold = null, string textAlign = null)
        {
            if (fontFamily != null)
            {
                Data.FontFamily = fontFamily;
            }

            if (fontSize.HasValue)
            {
                Data.FontSize = fontSize.Value;
            }

            if (color != null)
            {
                Data.FontColor = color;
            }

            if (isBold.HasValue)
            {
                Data.IsBoldBool = isBold.Value;
            }

            if (textAlign != null)
            {
                Data.TextAlign = textAlign;
            }
            
            // 重新渲染
            RenderTextBox();
        }

        /// <summary>
        /// 聚焦到文本框（进入编辑模式）
        /// </summary>
        public void FocusTextBox(bool selectAll = false)
        {
            EnterEditMode(selectAll: selectAll);
        }
        
        /// <summary>
        /// 退出编辑模式（返回选中状态）
        /// </summary>
        public void ExitEditModePublic()
        {
            System.Windows.Input.Keyboard.ClearFocus();
            Focus();
        }
        
        /// <summary>
        /// 获取当前编辑状态描述（用于调试）
        /// </summary>
        public string GetEditStateDescription()
        {
            if (IsInEditMode)
                return "编辑中";
            else if (IsSelected)
                return "已选中";
            else
                return "未选中";
        }
        
        /// <summary>
        /// 快速进入编辑模式（新建文本框专用）
        /// </summary>
        public void EnterEditModeForNew()
        {
            _isNewlyCreated = true;
            EnterEditMode(selectAll: true);
        }

        /// <summary>
        /// 隐藏UI装饰元素（用于保存缩略图/投影渲染）
        /// </summary>
        public void HideDecorations()
        {
            if (_border != null)
            {
                _border.BorderBrush = WpfBrushes.Transparent;
                _border.Background = WpfBrushes.Transparent;
            }
            if (_selectionRect != null)
            {
                _selectionRect.Visibility = System.Windows.Visibility.Collapsed;
            }
            if (_resizeThumb != null)
            {
                _resizeThumb.Visibility = System.Windows.Visibility.Collapsed;
            }
            if (_editTextBox != null && _editTextBox.IsFocused)
            {
                System.Windows.Input.Keyboard.ClearFocus();
            }
        }

        /// <summary>
        /// 恢复UI装饰元素
        /// </summary>
        public void RestoreDecorations()
        {
            if (IsSelected)
            {
                SetSelected(true);
            }
        }
        
        /// <summary>
        /// 获取用于投影的渲染结果
        /// </summary>
        public System.Windows.Media.Imaging.BitmapSource GetRenderedBitmap()
        {
            return _renderImage.Source as System.Windows.Media.Imaging.BitmapSource;
        }

        #endregion
    }
}
