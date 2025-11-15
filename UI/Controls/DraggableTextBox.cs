using System;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Core;
using ImageColorChanger.Utils;
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
    /// 可拖拽、可调整大小的文本框控件（使用 WPF RichTextBox）
    /// </summary>
    public class DraggableTextBox : WpfUserControl
    {
        #region 字段

        private bool _isDragging = false;
        private WpfPoint _dragStartPoint;
        private WpfBorder _border;

        // ✅ WPF RichTextBox 控件
        private System.Windows.Controls.RichTextBox _richTextBox;

        // 🔧 同步标志：防止 TextChanged 事件循环
        private bool _isSyncing = false;

        private WpfThumb _resizeThumb;  // 右下角（保留兼容性）
        private WpfThumb _resizeThumbTopLeft;     // 上左
        private WpfThumb _resizeThumbTopCenter;   // 上中
        private WpfThumb _resizeThumbTopRight;    // 上右
        private WpfThumb _resizeThumbLeftCenter;  // 左中
        private WpfThumb _resizeThumbRightCenter; // 右中
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
        public bool IsInEditMode => _richTextBox != null && !_richTextBox.IsReadOnly;

        /// <summary>
        /// 获取 WPF RichTextBox 控件（用于圣经经文插入等功能）
        /// </summary>
        public System.Windows.Controls.RichTextBox RichTextBox => _richTextBox;

        /// <summary>
        /// 检测是否有选中文本
        /// </summary>
        public bool HasTextSelection()
        {
            return _richTextBox != null && !_richTextBox.Selection.IsEmpty;
        }

        /// <summary>
        /// ✅ 检测选中文字是否为加粗（使用 WPF 原生 API）
        /// </summary>
        public bool IsSelectionBold()
        {
            if (_richTextBox == null || _richTextBox.Selection.IsEmpty)
                return Data.IsBoldBool; // 无选中时返回全局状态

            var fontWeight = _richTextBox.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontWeightProperty);

            // 如果选中文字样式不一致，返回 DependencyProperty.UnsetValue
            if (fontWeight == System.Windows.DependencyProperty.UnsetValue)
                return false; // 样式不一致时默认返回 false

            return fontWeight.Equals(System.Windows.FontWeights.Bold);
        }

        /// <summary>
        /// ✅ 检测选中文字是否为斜体（使用 WPF 原生 API）
        /// </summary>
        public bool IsSelectionItalic()
        {
            if (_richTextBox == null || _richTextBox.Selection.IsEmpty)
                return Data.IsItalicBool; // 无选中时返回全局状态

            var fontStyle = _richTextBox.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontStyleProperty);

            if (fontStyle == System.Windows.DependencyProperty.UnsetValue)
                return false;

            return fontStyle.Equals(System.Windows.FontStyles.Italic);
        }

        /// <summary>
        /// ✅ 检测选中文字是否有下划线（使用 WPF 原生 API）
        /// </summary>
        public bool IsSelectionUnderline()
        {
            if (_richTextBox == null || _richTextBox.Selection.IsEmpty)
                return Data.IsUnderlineBool; // 无选中时返回全局状态

            var textDecorations = _richTextBox.Selection.GetPropertyValue(System.Windows.Documents.Inline.TextDecorationsProperty);

            if (textDecorations == System.Windows.DependencyProperty.UnsetValue)
                return false;

            return textDecorations != null && textDecorations.Equals(System.Windows.TextDecorations.Underline);
        }

        /// <summary>
        /// ✅ 获取选中文字的字体（使用 WPF 原生 API）
        /// </summary>
        public string GetSelectionFontFamily()
        {
            if (_richTextBox == null || _richTextBox.Selection.IsEmpty)
                return Data.FontFamily; // 无选中时返回全局字体

            var fontFamily = _richTextBox.Selection.GetPropertyValue(System.Windows.Documents.TextElement.FontFamilyProperty);

            if (fontFamily == System.Windows.DependencyProperty.UnsetValue)
                return Data.FontFamily;

            return (fontFamily as System.Windows.Media.FontFamily)?.Source ?? Data.FontFamily;
        }

        /// <summary>
        /// ✅ 修复光标样式：防止光标继承斜体样式
        /// </summary>
        private void FixCaretStyle()
        {
            if (_richTextBox == null || _richTextBox.Selection == null)
                return;

            try
            {
                // 当光标位置没有选中文本时（插入点），重置字体样式为 Normal
                if (_richTextBox.Selection.IsEmpty)
                {
                    // 获取当前插入点的字体样式
                    var currentFontStyle = _richTextBox.Selection.GetPropertyValue(
                        System.Windows.Documents.TextElement.FontStyleProperty);

                    // 如果是斜体，临时重置为 Normal（仅影响光标，不影响已有文本）
                    if (currentFontStyle != null &&
                        currentFontStyle.Equals(System.Windows.FontStyles.Italic))
                    {
                        // 使用 ApplyPropertyValue 设置插入点的默认样式
                        _richTextBox.Selection.ApplyPropertyValue(
                            System.Windows.Documents.TextElement.FontStyleProperty,
                            System.Windows.FontStyles.Normal);
                    }
                }
            }
            catch
            {
                // 忽略异常，避免影响正常编辑
            }
        }

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

        /// <summary>
        /// ✅ 文本选择改变事件（用于更新工具栏按钮状态）
        /// </summary>
        public event EventHandler TextSelectionChanged;

        #endregion

        #region 构造函数

        public DraggableTextBox(TextElement element)
        {
            Data = element ?? throw new ArgumentNullException(nameof(element));
            InitializeComponent();
            LoadFromData();
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
                CornerRadius = new System.Windows.CornerRadius(3),
                ClipToBounds = true  // ✅ 裁剪内容到圆角边界
            };

            var grid = new WpfGrid
            {
                Background = WpfBrushes.Transparent  // 设置透明背景，使鼠标事件能够穿透
            };

            // ✅ 初始化 WPF RichTextBox
            _richTextBox = new System.Windows.Controls.RichTextBox
            {
                // 基本属性
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Background = WpfBrushes.Transparent,
                BorderThickness = new System.Windows.Thickness(0),
                Padding = new System.Windows.Thickness(5),

                // 编辑属性
                AcceptsReturn = true,
                AcceptsTab = false,
                IsReadOnly = true,  // 默认只读，双击后可编辑

                // 🔧 只读模式下不拦截鼠标事件，允许父控件处理拖拽和双击
                IsHitTestVisible = false,

                // 🔧 文本和光标颜色设置
                Foreground = WpfBrushes.White,  // 默认白色文本（在深色背景上可见）
                CaretBrush = WpfBrushes.White,  // 白色光标

                // ✅ 隐藏滚动条
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Hidden
            };

            // 监听文本改变事件
            _richTextBox.TextChanged += (s, e) =>
            {
                // 🔧 防止同步过程中的循环调用
                if (_isSyncing)
                    return;

                // ✅ 动态应用行高到所有段落（包括新创建的段落）
                ApplyLineHeightToAllParagraphs();

                // 同步文本到 Data.Content
                SyncTextFromRichTextBox();
                ContentChanged?.Invoke(this, Data.Content);
            };

            // ✅ 监听文本选择改变事件（用于更新工具栏按钮状态）
            _richTextBox.SelectionChanged += (s, e) =>
            {
                // 触发事件通知 MainWindow 更新工具栏
                TextSelectionChanged?.Invoke(this, EventArgs.Empty);

                // ✅ 优化光标样式：防止光标继承斜体样式
                FixCaretStyle();
            };

            grid.Children.Add(_richTextBox);

            // WPF RichTextBox 自动处理鼠标事件，无需手动处理

            // 虚线选中框（叠加在文本框上方）
            _selectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = WpfBrushes.DodgerBlue,
                StrokeThickness = 1,  // ✅ 非常细的边框
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
                new System.Windows.Thickness(-8, -8, 0, 0)  // ✅ 统一边距适配 16×16 尺寸
            );
            _resizeThumbTopLeft.DragDelta += (s, e) => ResizeFromCorner(e, -1, -1, true, true);

            _resizeThumbTopCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Center,
                System.Windows.VerticalAlignment.Top,
                WpfCursors.SizeNS,
                new System.Windows.Thickness(0, -8, 0, 0)  // ✅ 统一边距适配 16×16 尺寸
            );
            _resizeThumbTopCenter.DragDelta += (s, e) => ResizeFromEdge(e, 0, -1, false, true);

            _resizeThumbTopRight = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Right,
                System.Windows.VerticalAlignment.Top,
                WpfCursors.SizeNESW,
                new System.Windows.Thickness(0, -8, -8, 0)  // ✅ 统一边距适配 16×16 尺寸
            );
            _resizeThumbTopRight.DragDelta += (s, e) => ResizeFromCorner(e, 1, -1, false, true);

            _resizeThumbBottomLeft = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Left,
                System.Windows.VerticalAlignment.Bottom,
                WpfCursors.SizeNESW,
                new System.Windows.Thickness(-8, 0, 0, -8)  // ✅ 统一边距适配 16×16 尺寸
            );
            _resizeThumbBottomLeft.DragDelta += (s, e) => ResizeFromCorner(e, -1, 1, true, false);

            _resizeThumbBottomCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Center,
                System.Windows.VerticalAlignment.Bottom,
                WpfCursors.SizeNS,
                new System.Windows.Thickness(0, 0, 0, -8)  // ✅ 统一边距适配 16×16 尺寸
            );
            _resizeThumbBottomCenter.DragDelta += (s, e) => ResizeFromEdge(e, 0, 1, false, false);

            // ✅ 左中控制点（调整宽度）
            _resizeThumbLeftCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Left,
                System.Windows.VerticalAlignment.Center,
                WpfCursors.SizeWE,
                new System.Windows.Thickness(-8, 0, 0, 0)  // ✅ 统一边距适配 16×16 尺寸
            );
            _resizeThumbLeftCenter.DragDelta += (s, e) => ResizeFromEdge(e, -1, 0, true, false);

            // ✅ 右中控制点（调整宽度）
            _resizeThumbRightCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Right,
                System.Windows.VerticalAlignment.Center,
                WpfCursors.SizeWE,
                new System.Windows.Thickness(0, 0, -8, 0)  // ✅ 统一边距适配 16×16 尺寸
            );
            _resizeThumbRightCenter.DragDelta += (s, e) => ResizeFromEdge(e, 1, 0, false, false);

            _resizeThumbBottomRight = new WpfThumb
            {
                Width = 16,  // ✅ 增大控制点，更容易看清
                Height = 16,
                Background = WpfBrushes.DodgerBlue,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Cursor = WpfCursors.SizeNWSE,
                Margin = new System.Windows.Thickness(0, 0, -8, -8),  // ✅ 调整边距适配新尺寸
                Visibility = System.Windows.Visibility.Collapsed
            };
            _resizeThumbBottomRight.DragDelta += (s, e) => ResizeFromCorner(e, 1, 1, false, false);
            _resizeThumb = _resizeThumbBottomRight; // 兼容性别名

            grid.Children.Add(_selectionRect);
            grid.Children.Add(_resizeThumbTopLeft);
            grid.Children.Add(_resizeThumbTopCenter);
            grid.Children.Add(_resizeThumbTopRight);
            grid.Children.Add(_resizeThumbLeftCenter);
            grid.Children.Add(_resizeThumbRightCenter);
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
            
            // 监听尺寸变化
            base.SizeChanged += (s, e) =>
            {
                // WPF RichTextBox 自动处理尺寸变化，无需手动渲染
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

            // 同步文本到 RichTextBox
            SyncTextToRichTextBox();
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
                
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"🔧 [DraggableTextBox] 字体名称清理: {fontFamily} -> {cleanName}");
//#endif
                
                return cleanName;
            }
            
            return fontFamily;
        }
        
        #endregion

        #region SkiaSharp渲染



        #endregion

        #region 编辑模式

        /// <summary>
        /// 进入编辑模式（双击时）
        /// </summary>
        private void EnterEditMode(bool selectAll = true)
        {
            // 清除占位符文字
            if (_isPlaceholderText)
            {
                Data.Content = "";
                _isPlaceholderText = false;
            }

            // 设置 RichTextBox 为可编辑
            if (_richTextBox != null)
            {
                _richTextBox.IsReadOnly = false;
                _richTextBox.IsHitTestVisible = true;  // 🔧 编辑模式下允许接收鼠标事件
                _richTextBox.Focus();

                if (selectAll)
                {
                    _richTextBox.SelectAll();
                }
            }
        }

        /// <summary>
        /// 退出编辑模式（失去焦点或按Esc时）
        /// </summary>
        public void ExitEditMode()
        {
            if (!IsInEditMode)
                return;

            // 设置 RichTextBox 为只读
            if (_richTextBox != null)
            {
                _richTextBox.IsReadOnly = true;
                _richTextBox.IsHitTestVisible = false;  // 🔧 只读模式下不拦截鼠标事件
            }

            // 检查是否为空，如果为空则恢复占位符
            if (string.IsNullOrWhiteSpace(Data.Content))
            {
                Data.Content = DEFAULT_PLACEHOLDER;
                _isPlaceholderText = true;
                SyncTextToRichTextBox();
            }
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
                if (IsInEditMode)
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
                Width = 16,  // ✅ 增大控制点，更容易看清
                Height = 16,
                Background = WpfBrushes.DodgerBlue,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign,
                Cursor = cursor,
                Margin = margin,
                Visibility = System.Windows.Visibility.Collapsed
            };
            
            // 监听拖拽结束事件
            thumb.DragCompleted += (s, e) =>
            {
                // WPF RichTextBox 自动处理，无需手动渲染
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
                // ✅ 不覆盖用户设置的背景色，保持当前背景

                _resizeThumbTopLeft.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbTopCenter.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbTopRight.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbLeftCenter.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbRightCenter.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbBottomLeft.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbBottomCenter.Visibility = System.Windows.Visibility.Visible;
                _resizeThumbBottomRight.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                _selectionRect.Visibility = System.Windows.Visibility.Collapsed;
                // ✅ 不覆盖用户设置的背景色，重新应用背景样式
                ApplyBackgroundStyle();

                _resizeThumbTopLeft.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbTopCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbTopRight.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbLeftCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbRightCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbBottomLeft.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbBottomCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbBottomRight.Visibility = System.Windows.Visibility.Collapsed;
            }

            SelectionChanged?.Invoke(this, selected);
        }

        #endregion

        #region 输入法支持


        #endregion

        #region 键盘事件

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!IsInEditMode)
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
                ApplyStylesToRichTextBox();
            }
        }

        /// <summary>
        /// 应用样式（由工具栏调用）
        /// </summary>
        public void ApplyStyle(string fontFamily = null, double? fontSize = null,
                               string color = null, bool? isBold = null, string textAlign = null,
                               bool? isUnderline = null, bool? isItalic = null,
                               string borderColor = null, double? borderWidth = null,
                               double? borderRadius = null, int? borderOpacity = null,
                               string backgroundColor = null, double? backgroundRadius = null,
                               int? backgroundOpacity = null,
                               string shadowColor = null, double? shadowOffsetX = null,
                               double? shadowOffsetY = null, double? shadowBlur = null,
                               int? shadowOpacity = null,
                               double? lineSpacing = null, double? letterSpacing = null)
        {
            if (fontFamily != null)
            {
                Data.FontFamily = fontFamily;
            }

            bool needsRichTextResync = false;

            if (fontSize.HasValue)
            {
                Data.FontSize = fontSize.Value;

                // 🔧 如果有 RichTextSpans，同步更新所有片段的字体大小
                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)
                {
                    double scaleFactor = fontSize.Value / Data.RichTextSpans.First().FontSize.GetValueOrDefault(40);
                    foreach (var span in Data.RichTextSpans)
                    {
                        if (span.FontSize.HasValue)
                        {
                            span.FontSize = span.FontSize.Value * scaleFactor;
                        }
                    }
                    needsRichTextResync = true;
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"🔍 [ApplyStyle] 更新 {Data.RichTextSpans.Count} 个富文本片段的字体大小，缩放比例={scaleFactor:F2}");
//#endif
                }
            }

            if (color != null)
            {
                Data.FontColor = color;
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"🎨 [ApplyStyle] 应用全局颜色: {color}, 当前 RichTextSpans 数量: {Data.RichTextSpans?.Count ?? 0}");
//#endif
                // 🔧 应用全局颜色时，清除局部样式，重新渲染
                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)
                {
                    Data.RichTextSpans.Clear();
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"🔄 [ApplyStyle] 应用全局颜色，清除局部样式");
//#endif
                }
                // 🔧 无论是否有 RichTextSpans，都需要重新渲染以应用颜色到 Run 对象
                needsRichTextResync = true;
            }

            if (isBold.HasValue)
            {
                Data.IsBoldBool = isBold.Value;
                // 🔧 应用全局加粗时，清除局部样式，重新渲染
                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)
                {
                    Data.RichTextSpans.Clear();
                    needsRichTextResync = true;
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"🔄 [ApplyStyle] 应用全局加粗，清除局部样式");
//#endif
                }
            }

            if (textAlign != null)
            {
                Data.TextAlign = textAlign;
            }

            if (isUnderline.HasValue)
            {
                Data.IsUnderlineBool = isUnderline.Value;
                // 🔧 应用全局下划线时，清除局部样式，重新渲染
                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)
                {
                    Data.RichTextSpans.Clear();
                    needsRichTextResync = true;
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"🔄 [ApplyStyle] 应用全局下划线，清除局部样式");
//#endif
                }
            }

            if (isItalic.HasValue)
            {
                Data.IsItalicBool = isItalic.Value;
                // 🔧 应用全局斜体时，清除局部样式，重新渲染
                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)
                {
                    Data.RichTextSpans.Clear();
                    needsRichTextResync = true;
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"🔄 [ApplyStyle] 应用全局斜体，清除局部样式");
//#endif
                }
            }

            // 边框样式
            if (borderColor != null)
            {
                Data.BorderColor = borderColor;
            }

            if (borderWidth.HasValue)
            {
                Data.BorderWidth = borderWidth.Value;
            }

            if (borderRadius.HasValue)
            {
                Data.BorderRadius = borderRadius.Value;
            }

            if (borderOpacity.HasValue)
            {
                Data.BorderOpacity = borderOpacity.Value;
            }

            // 背景样式
            if (backgroundColor != null)
            {
                Data.BackgroundColor = backgroundColor;
            }

            if (backgroundRadius.HasValue)
            {
                Data.BackgroundRadius = backgroundRadius.Value;
            }

            if (backgroundOpacity.HasValue)
            {
                Data.BackgroundOpacity = backgroundOpacity.Value;
            }

            // 阴影样式
            if (shadowColor != null)
            {
                Data.ShadowColor = shadowColor;
            }

            if (shadowOffsetX.HasValue)
            {
                Data.ShadowOffsetX = shadowOffsetX.Value;
            }

            if (shadowOffsetY.HasValue)
            {
                Data.ShadowOffsetY = shadowOffsetY.Value;
            }

            if (shadowBlur.HasValue)
            {
                Data.ShadowBlur = shadowBlur.Value;
            }

            if (shadowOpacity.HasValue)
            {
                Data.ShadowOpacity = shadowOpacity.Value;
            }

            // 间距样式
            if (lineSpacing.HasValue)
            {
                Data.LineSpacing = lineSpacing.Value;
            }

            if (letterSpacing.HasValue)
            {
                Data.LetterSpacing = letterSpacing.Value;
            }

            // 🔧 如果更新了 RichTextSpans，需要重新渲染
            if (needsRichTextResync)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [ApplyStyle] 重新渲染富文本内容");
#endif
                SyncTextToRichTextBox();
            }
            else
            {
                // 应用样式到 RichTextBox
                ApplyStylesToRichTextBox();
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"🎨 [ApplyStyle] 样式已应用 - 边框:{Data.BorderColor}/{Data.BorderWidth}px/透明度{Data.BorderOpacity}%, 背景:{Data.BackgroundColor}/透明度{Data.BackgroundOpacity}%, 加粗:{Data.IsBold}, 斜体:{Data.IsItalic}");
#endif

            // 🔧 触发内容改变事件，通知主窗口保存样式到数据库
            ContentChanged?.Invoke(this, Data.Content);
        }

        /// <summary>
        /// 应用样式到选中文本（使用 WPF 原生 API）
        /// </summary>
        public void ApplyStyleToSelection(System.Windows.Media.FontFamily fontFamilyObj = null,
                                          string fontFamily = null, double? fontSize = null,
                                          string color = null, bool? isBold = null,
                                          bool? isUnderline = null, bool? isItalic = null,
                                          string borderColor = null, double? borderWidth = null,
                                          double? borderRadius = null, int? borderOpacity = null,
                                          string backgroundColor = null, double? backgroundRadius = null,
                                          int? backgroundOpacity = null,
                                          string shadowColor = null, double? shadowOffsetX = null,
                                          double? shadowOffsetY = null, double? shadowBlur = null,
                                          int? shadowOpacity = null)
        {
// #if DEBUG
//             System.Diagnostics.Debug.WriteLine($"🎨 [ApplyStyleToSelection] 使用 WPF 原生 API - isBold:{isBold}, isItalic:{isItalic}, isUnderline:{isUnderline}");
// #endif

            // 检查是否有选中文本
            if (_richTextBox == null || _richTextBox.Selection.IsEmpty)
            {
// #if DEBUG
//                 System.Diagnostics.Debug.WriteLine($"⚠️ [ApplyStyleToSelection] 无选中文本，回退到全局样式");
// #endif
                // 无选择时，应用到整个文本框
                ApplyStyle(fontFamily, fontSize, color, isBold, null, isUnderline, isItalic,
                          borderColor, borderWidth, borderRadius, borderOpacity,
                          backgroundColor, backgroundRadius, backgroundOpacity,
                          shadowColor, shadowOffsetX, shadowOffsetY, shadowBlur, shadowOpacity);
                return;
            }

            // ✅ 使用 WPF 原生 TextRange API
            var selection = _richTextBox.Selection;

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"📍 [ApplyStyleToSelection] 选中文本: '{selection.Text}'");
#endif

            // ✅ 应用加粗样式（WPF 原生 API）
            if (isBold.HasValue)
            {
                selection.ApplyPropertyValue(
                    System.Windows.Documents.TextElement.FontWeightProperty,
                    isBold.Value ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal);
                // 🔧 同时更新 Data 对象，确保保存到数据库
                Data.IsBoldBool = isBold.Value;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"  ✅ 应用加粗: {isBold.Value}, Data.IsBold={Data.IsBold}");
#endif
            }

            // ✅ 应用斜体样式（WPF 原生 API）
            if (isItalic.HasValue)
            {
                selection.ApplyPropertyValue(
                    System.Windows.Documents.TextElement.FontStyleProperty,
                    isItalic.Value ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal);
                // 🔧 同时更新 Data 对象，确保保存到数据库
                Data.IsItalicBool = isItalic.Value;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"  ✅ 应用斜体: {isItalic.Value}, Data.IsItalic={Data.IsItalic}");
#endif
            }

            // ✅ 应用下划线样式（WPF 原生 API）
            if (isUnderline.HasValue)
            {
                selection.ApplyPropertyValue(
                    System.Windows.Documents.Inline.TextDecorationsProperty,
                    isUnderline.Value ? System.Windows.TextDecorations.Underline : null);
                // 🔧 同时更新 Data 对象，确保保存到数据库
                Data.IsUnderlineBool = isUnderline.Value;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"  ✅ 应用下划线: {isUnderline.Value}, Data.IsUnderline={Data.IsUnderline}");
#endif
            }

            // ✅ 应用文字颜色（WPF 原生 API）
            if (color != null)
            {
                try
                {
                    var wpfColor = (WpfColor)WpfColorConverter.ConvertFromString(color);
                    selection.ApplyPropertyValue(
                        System.Windows.Documents.TextElement.ForegroundProperty,
                        new WpfSolidColorBrush(wpfColor));
// #if DEBUG
//                     System.Diagnostics.Debug.WriteLine($"  ✅ 应用颜色: {color}");
// #endif
                }
                catch (Exception)
                {
// #if DEBUG
//                     System.Diagnostics.Debug.WriteLine($"  ❌ 颜色转换失败: {ex.Message}");
// #endif
                }
            }

            // ✅ 应用字体（WPF 原生 API）- 优先使用 FontFamily 对象
            if (fontFamilyObj != null)
            {
                selection.ApplyPropertyValue(
                    System.Windows.Documents.TextElement.FontFamilyProperty,
                    fontFamilyObj);
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"  ✅ 应用字体对象: {fontFamilyObj.Source}");
#endif
            }
            else if (fontFamily != null)
            {
                selection.ApplyPropertyValue(
                    System.Windows.Documents.TextElement.FontFamilyProperty,
                    new System.Windows.Media.FontFamily(fontFamily));
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"  ✅ 应用字体字符串: {fontFamily}");
#endif
            }

            // ✅ 应用字号（WPF 原生 API）
            if (fontSize.HasValue)
            {
                selection.ApplyPropertyValue(
                    System.Windows.Documents.TextElement.FontSizeProperty,
                    fontSize.Value);
                // 🔧 同时更新 Data 对象
                Data.FontSize = fontSize.Value;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"  ✅ 应用字号: {fontSize.Value}");
#endif
            }

            // 🔧 更新 Data 对象的边框样式（确保保存到数据库）
            if (borderColor != null)
                Data.BorderColor = borderColor;
            if (borderWidth.HasValue)
                Data.BorderWidth = borderWidth.Value;
            if (borderRadius.HasValue)
                Data.BorderRadius = borderRadius.Value;
            if (borderOpacity.HasValue)
                Data.BorderOpacity = borderOpacity.Value;

            // 🔧 更新 Data 对象的背景样式（确保保存到数据库）
            if (backgroundColor != null)
                Data.BackgroundColor = backgroundColor;
            if (backgroundRadius.HasValue)
                Data.BackgroundRadius = backgroundRadius.Value;
            if (backgroundOpacity.HasValue)
                Data.BackgroundOpacity = backgroundOpacity.Value;

            // 🔧 更新 Data 对象的文字颜色（确保保存到数据库）
            if (color != null)
                Data.FontColor = color;

            // 🔧 应用边框和背景样式到 UI
            ApplyBorderStyle();
            ApplyBackgroundStyle();

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"🎨 [ApplyStyleToSelection] 完成 - 使用 WPF 原生 API");
            System.Diagnostics.Debug.WriteLine($"🎨 [ApplyStyleToSelection] 样式已应用 - 边框:{Data.BorderColor}/{Data.BorderWidth}px/透明度{Data.BorderOpacity}%, 背景:{Data.BackgroundColor}/透明度{Data.BackgroundOpacity}%, 加粗:{Data.IsBold}, 斜体:{Data.IsItalic}");
#endif

            // 🔧 触发内容改变事件，通知主窗口保存样式到数据库
            ContentChanged?.Invoke(this, Data.Content);
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"📢 [ApplyStyleToSelection] 已触发 ContentChanged 事件");
#endif
        }

        /// <summary>
        /// ✅ 动态应用行高到所有段落（包括新创建的段落）
        /// </summary>
        private void ApplyLineHeightToAllParagraphs()
        {
            if (_richTextBox == null || Data == null)
                return;

            double lineSpacingMultiplier = Data.LineSpacing > 0 ? Data.LineSpacing : 1.2;
            double lineHeight = Data.FontSize * lineSpacingMultiplier;

            foreach (var block in _richTextBox.Document.Blocks)
            {
                if (block is System.Windows.Documents.Paragraph paragraph)
                {
                    paragraph.Margin = new System.Windows.Thickness(0);
                    paragraph.LineHeight = lineHeight;
                    paragraph.LineStackingStrategy = System.Windows.LineStackingStrategy.BlockLineHeight;
                }
            }
        }

        /// <summary>
        /// ⚠️ 应用字间距到所有段落
        /// 注意：WPF RichTextBox 原生不支持字间距属性
        /// 字间距数据会保存到数据库，但在编辑器中无视觉效果
        /// 未来可在导出图片时应用字间距效果
        /// </summary>
        private void ApplyLetterSpacingToAllParagraphs()
        {
            // WPF RichTextBox 不支持字间距，暂不实现
            // 字间距数据仅保存到 Data.LetterSpacing，不应用到 UI
        }



        /// <summary>
        /// [已废弃] 将纯文本模式转换为 RichTextSpans 模式
        /// 使用 WPF 原生 API 后不再需要此方法
        /// </summary>
        [Obsolete("使用 WPF 原生 TextRange API，不再需要手动转换模式")]
        private void ConvertToRichTextMode()
        {
            // 保留空实现以兼容旧代码
        }

        /// <summary>
        /// [已废弃] 在选择边界处分割 RichTextSpans
        /// 使用 WPF 原生 TextRange API 后不再需要手动分割
        /// </summary>
        [Obsolete("使用 WPF 原生 TextRange API，不再需要手动分割文本")]
        private void SplitSpansAtSelection(int selStart, int selEnd)
        {
            // 保留空实现以兼容旧代码
        }

        /// <summary>
        /// 克隆 RichTextSpan
        /// </summary>
        private RichTextSpan CloneSpan(RichTextSpan source)
        {
            return new RichTextSpan
            {
                TextElementId = source.TextElementId,
                SpanOrder = source.SpanOrder,
                Text = source.Text,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                FontColor = source.FontColor,
                IsBold = source.IsBold,
                IsUnderline = source.IsUnderline,
                IsItalic = source.IsItalic,
                BorderColor = source.BorderColor,
                BorderWidth = source.BorderWidth,
                BorderRadius = source.BorderRadius,
                BorderOpacity = source.BorderOpacity,
                BackgroundColor = source.BackgroundColor,
                BackgroundRadius = source.BackgroundRadius,
                BackgroundOpacity = source.BackgroundOpacity,
                ShadowColor = source.ShadowColor,
                ShadowOffsetX = source.ShadowOffsetX,
                ShadowOffsetY = source.ShadowOffsetY,
                ShadowBlur = source.ShadowBlur,
                ShadowOpacity = source.ShadowOpacity
            };
        }

        /// <summary>
        /// [已废弃] 合并相邻的相同样式 spans（优化）
        /// 使用 WPF 原生 API 后，FlowDocument 自动优化样式
        /// </summary>
        [Obsolete("使用 WPF 原生 FlowDocument，自动优化样式合并")]
        private void MergeAdjacentSpans()
        {
            // 保留空实现以兼容旧代码
        }

        /// <summary>
        /// 检查两个 spans 是否有相同样式
        /// </summary>
        private bool SpansHaveSameStyle(RichTextSpan a, RichTextSpan b)
        {
            return a.FontFamily == b.FontFamily &&
                   a.FontSize == b.FontSize &&
                   a.FontColor == b.FontColor &&
                   a.IsBold == b.IsBold &&
                   a.IsUnderline == b.IsUnderline &&
                   a.IsItalic == b.IsItalic &&
                   a.BorderColor == b.BorderColor &&
                   a.BorderWidth == b.BorderWidth &&
                   a.BorderRadius == b.BorderRadius &&
                   a.BorderOpacity == b.BorderOpacity &&
                   a.BackgroundColor == b.BackgroundColor &&
                   a.BackgroundRadius == b.BackgroundRadius &&
                   a.BackgroundOpacity == b.BackgroundOpacity &&
                   a.ShadowColor == b.ShadowColor &&
                   a.ShadowOffsetX == b.ShadowOffsetX &&
                   a.ShadowOffsetY == b.ShadowOffsetY &&
                   a.ShadowBlur == b.ShadowBlur &&
                   a.ShadowOpacity == b.ShadowOpacity;
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
            // 🔧 不隐藏边框，保留用户设置的边框样式（用于投影显示）
            // 只隐藏选择框和拖拽手柄等编辑装饰元素

            if (_selectionRect != null)
            {
                _selectionRect.Visibility = System.Windows.Visibility.Collapsed;
            }

            // 🔧 隐藏所有8个拖拽手柄
            if (_resizeThumbTopLeft != null)
                _resizeThumbTopLeft.Visibility = System.Windows.Visibility.Collapsed;
            if (_resizeThumbTopCenter != null)
                _resizeThumbTopCenter.Visibility = System.Windows.Visibility.Collapsed;
            if (_resizeThumbTopRight != null)
                _resizeThumbTopRight.Visibility = System.Windows.Visibility.Collapsed;
            if (_resizeThumbLeftCenter != null)
                _resizeThumbLeftCenter.Visibility = System.Windows.Visibility.Collapsed;
            if (_resizeThumbRightCenter != null)
                _resizeThumbRightCenter.Visibility = System.Windows.Visibility.Collapsed;
            if (_resizeThumbBottomLeft != null)
                _resizeThumbBottomLeft.Visibility = System.Windows.Visibility.Collapsed;
            if (_resizeThumbBottomCenter != null)
                _resizeThumbBottomCenter.Visibility = System.Windows.Visibility.Collapsed;
            if (_resizeThumbBottomRight != null)
                _resizeThumbBottomRight.Visibility = System.Windows.Visibility.Collapsed;

            if (IsInEditMode)
            {
                ExitEditMode();
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
        /// 获取用于投影的渲染结果（包含边框和背景）
        /// </summary>
        public System.Windows.Media.Imaging.BitmapSource GetRenderedBitmap()
        {
            if (_border == null)
                return null;

            // 🔧 渲染整个 Border 容器（包含边框、背景和 RichTextBox）
            var renderTarget = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)ActualWidth,
                (int)ActualHeight,
                96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);

            // 🔧 渲染 _border 而不是 _richTextBox，以包含边框和背景样式
            renderTarget.Render(_border);
            return renderTarget;
        }

        #endregion

        #region WPF RichTextBox 辅助方法

        /// <summary>
        /// 从 RichTextBox 同步文本到 Data.Content
        /// </summary>
        private void SyncTextFromRichTextBox()
        {
            if (_richTextBox == null)
                return;

            try
            {
                var textRange = new System.Windows.Documents.TextRange(
                    _richTextBox.Document.ContentStart,
                    _richTextBox.Document.ContentEnd);

                Data.Content = textRange.Text;
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [SyncTextFromRichTextBox] 失败: {ex.Message}");
#endif
            }
        }

        /// <summary>
        /// 从 Data.Content 同步文本到 RichTextBox
        /// </summary>
        private void SyncTextToRichTextBox()
        {
            if (_richTextBox == null)
                return;

            try
            {
                // 🔧 设置同步标志，防止 TextChanged 事件循环
                _isSyncing = true;

                _richTextBox.Document.Blocks.Clear();

                // 🔧 如果有 RichTextSpans，渲染富文本片段
                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)
                {
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"📥 [加载RichTextSpans] 文本框 ID={Data.Id} 开始加载 {Data.RichTextSpans.Count} 个片段");
//#endif
                    var paragraph = new System.Windows.Documents.Paragraph();
                    paragraph.Margin = new System.Windows.Thickness(0);

                    // 按 SpanOrder 排序后渲染
                    var sortedSpans = Data.RichTextSpans.OrderBy(s => s.SpanOrder).ToList();

                    foreach (var span in sortedSpans)
                    {
                        var run = new System.Windows.Documents.Run(span.Text ?? "");

                        // 应用字体
                        if (!string.IsNullOrEmpty(span.FontFamily))
                        {
                            var fontFamily = FontService.Instance.GetFontFamilyByFamily(span.FontFamily);
                            if (fontFamily != null)
                                run.FontFamily = fontFamily;
                        }

                        // 应用字体大小
                        if (span.FontSize.HasValue && span.FontSize.Value > 0)
                            run.FontSize = span.FontSize.Value;

                        // 应用颜色
                        if (!string.IsNullOrEmpty(span.FontColor))
                        {
                            try
                            {
                                var color = (WpfColor)WpfColorConverter.ConvertFromString(span.FontColor);
                                run.Foreground = new WpfSolidColorBrush(color);
//#if DEBUG
//                                System.Diagnostics.Debug.WriteLine($"  📦 片段 {span.SpanOrder}: 文本='{span.Text}', 字体={span.FontFamily}, 字号={span.FontSize}, 颜色={span.FontColor}, 加粗={span.IsBold}, 斜体={span.IsItalic}");
//#endif
                            }
                            catch (Exception ex)
                            {
//#if DEBUG
//                                System.Diagnostics.Debug.WriteLine($"  ❌ 片段 {span.SpanOrder} 颜色解析失败: {span.FontColor}, 错误: {ex.Message}");
//#endif
                                _ = ex;
                            }
                        }
                        else
                        {
//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"  📦 片段 {span.SpanOrder}: 文本='{span.Text}', 字体={span.FontFamily}, 字号={span.FontSize}, 颜色=null, 加粗={span.IsBold}, 斜体={span.IsItalic}");
//#endif
                        }

                        // 应用粗体
                        run.FontWeight = span.IsBold == 1
                            ? System.Windows.FontWeights.Bold
                            : System.Windows.FontWeights.Normal;

                        // 应用斜体
                        run.FontStyle = span.IsItalic == 1
                            ? System.Windows.FontStyles.Italic
                            : System.Windows.FontStyles.Normal;

                        // 应用下划线
                        if (span.IsUnderline == 1)
                            run.TextDecorations = System.Windows.TextDecorations.Underline;

                        paragraph.Inlines.Add(run);
                    }

                    _richTextBox.Document.Blocks.Add(paragraph);
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"📥 [加载RichTextSpans] 文本框 ID={Data.Id} 加载完成");
//#endif
                }
                else
                {
                    // 🔧 普通文本：创建 Run 并应用全局样式
                    var paragraph = new System.Windows.Documents.Paragraph();
                    var run = new System.Windows.Documents.Run(Data.Content ?? "");

                    // 应用全局样式到 Run
                    if (Data.IsBold == 1)
                        run.FontWeight = System.Windows.FontWeights.Bold;
                    if (Data.IsItalic == 1)
                        run.FontStyle = System.Windows.FontStyles.Italic;
                    if (Data.IsUnderline == 1)
                        run.TextDecorations = System.Windows.TextDecorations.Underline;

                    // 应用颜色
                    if (!string.IsNullOrEmpty(Data.FontColor))
                    {
                        try
                        {
                            var color = (WpfColor)WpfColorConverter.ConvertFromString(Data.FontColor);
                            run.Foreground = new WpfSolidColorBrush(color);
                        }
                        catch { }
                    }

                    paragraph.Inlines.Add(run);
                    _richTextBox.Document.Blocks.Add(paragraph);
                }

                // 应用样式（包括 RichTextSpans）
                ApplyStylesToRichTextBox();
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [SyncTextToRichTextBox] 失败: {ex.Message}");
#endif
            }
            finally
            {
                // 🔧 清除同步标志
                _isSyncing = false;
            }
        }

        /// <summary>
        /// 提取 FlowDocument 的内容和样式到 RichTextSpan 列表
        /// </summary>
        public List<Database.Models.RichTextSpan> ExtractRichTextSpansFromFlowDocument()
        {
            var spans = new List<Database.Models.RichTextSpan>();

            if (_richTextBox == null || _richTextBox.Document == null)
                return spans;

            int spanOrder = 0;

            try
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"📤 [提取RichTextSpans] 文本框 ID={Data.Id} 开始提取");
//#endif
                // 遍历所有段落
                foreach (var block in _richTextBox.Document.Blocks)
                {
                    if (block is System.Windows.Documents.Paragraph paragraph)
                    {
                        // 遍历段落中的所有 Inline 元素
                        foreach (var inline in paragraph.Inlines)
                        {
                            if (inline is System.Windows.Documents.Run run)
                            {
                                // 提取文本
                                string text = run.Text;
                                if (string.IsNullOrEmpty(text))
                                    continue;

                                // 提取样式
                                var span = new Database.Models.RichTextSpan
                                {
                                    TextElementId = Data.Id,
                                    SpanOrder = spanOrder++,
                                    Text = text
                                };

                                // 字体
                                if (run.FontFamily != null)
                                {
                                    span.FontFamily = run.FontFamily.Source;
                                }

                                // 字号
                                if (!double.IsNaN(run.FontSize) && run.FontSize > 0)
                                {
                                    span.FontSize = run.FontSize;
                                }

                                // 颜色
                                if (run.Foreground is WpfSolidColorBrush brush)
                                {
                                    var color = brush.Color;
                                    span.FontColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                                }

                                // 加粗
                                span.IsBold = (run.FontWeight == System.Windows.FontWeights.Bold) ? 1 : 0;

                                // 斜体
                                span.IsItalic = (run.FontStyle == System.Windows.FontStyles.Italic) ? 1 : 0;

                                // 下划线
                                span.IsUnderline = (run.TextDecorations == System.Windows.TextDecorations.Underline) ? 1 : 0;

//#if DEBUG
//                                System.Diagnostics.Debug.WriteLine($"  📦 片段 {spanOrder - 1}: 文本='{text}', 字体={span.FontFamily}, 字号={span.FontSize}, 颜色={span.FontColor}, 加粗={span.IsBold}, 斜体={span.IsItalic}");
//#endif
                                spans.Add(span);
                            }
                        }
                    }
                }
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"📤 [提取RichTextSpans] 文本框 ID={Data.Id} 提取完成，共 {spans.Count} 个片段");
//#endif
            }
            catch (Exception ex)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"❌ [ExtractRichTextSpans] 提取失败: {ex.Message}");
//#endif
                _ = ex;
            }

            return spans;
        }

        /// <summary>
        /// 将 RichTextSpan 样式应用到 RichTextBox
        /// </summary>
        private void ApplyStylesToRichTextBox()
        {
            if (_richTextBox == null)
                return;

            try
            {
                // 🔧 使用 FontService 加载字体（支持自定义字体文件）
                // 优先使用 GetFontFamilyByFamily（支持字体族名称和完整路径）
                var fontFamily = FontService.Instance.GetFontFamilyByFamily(Data.FontFamily);

                // 如果失败，尝试使用 GetFontFamily（支持字体显示名称）
                if (fontFamily == null)
                {
                    fontFamily = FontService.Instance.GetFontFamily(Data.FontFamily);
                }

                if (fontFamily != null)
                {
                    _richTextBox.FontFamily = fontFamily;
                }
                else
                {
                    // 降级：直接使用字体名称（可能是系统字体）
                    _richTextBox.FontFamily = new WpfFontFamily(Data.FontFamily);
                }

                // 🔧 如果有 RichTextSpans，不应用全局字体样式（保持每个片段的独立样式）
                bool hasRichTextSpans = Data.RichTextSpans != null && Data.RichTextSpans.Count > 0;

                // 解析文本颜色（用于光标颜色）
                var color = (WpfColor)WpfColorConverter.ConvertFromString(Data.FontColor);

                if (!hasRichTextSpans)
                {
                    _richTextBox.FontSize = Data.FontSize;

                    // 🔧 设置加粗
                    _richTextBox.FontWeight = Data.IsBoldBool
                        ? System.Windows.FontWeights.Bold
                        : System.Windows.FontWeights.Normal;

                    // 🔧 设置斜体
                    _richTextBox.FontStyle = Data.IsItalicBool
                        ? System.Windows.FontStyles.Italic
                        : System.Windows.FontStyles.Normal;

                    // 设置文本颜色
                    _richTextBox.Foreground = new WpfSolidColorBrush(color);
                }
                else
                {
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"🔍 [ApplyStylesToRichTextBox] 检测到 {Data.RichTextSpans.Count} 个富文本片段，跳过全局字体样式应用");
//#endif
                }

                // 🔧 设置光标颜色为文本颜色（确保可见）
                _richTextBox.CaretBrush = new WpfSolidColorBrush(color);

                // ✅ 应用行高到所有段落
                ApplyLineHeightToAllParagraphs();

                // ⚠️ 字间距功能暂不支持（WPF 限制）
                // ApplyLetterSpacingToAllParagraphs();

                // 设置文本对齐
                switch (Data.TextAlign)
                {
                    case "Left":
                        _richTextBox.Document.TextAlignment = System.Windows.TextAlignment.Left;
                        break;
                    case "Center":
                        _richTextBox.Document.TextAlignment = System.Windows.TextAlignment.Center;
                        break;
                    case "Right":
                        _richTextBox.Document.TextAlignment = System.Windows.TextAlignment.Right;
                        break;
                }

                // ✅ WPF 原生 FlowDocument 已包含所有样式信息
                // 不再需要从 RichTextSpans 表重新构建样式
                // 样式通过 TextRange.ApplyPropertyValue 直接应用到 FlowDocument

                // ✅ 应用边框样式到 Border 容器
                ApplyBorderStyle();

                // ✅ 应用背景样式到 RichTextBox
                ApplyBackgroundStyle();
            }
            catch (Exception)
            {
// #if DEBUG
//                 System.Diagnostics.Debug.WriteLine($"❌ [ApplyStylesToRichTextBox] 失败: {ex.Message}");
// #endif
            }
        }

        /// <summary>
        /// ✅ 应用边框样式到 Border 容器
        /// </summary>
        private void ApplyBorderStyle()
        {
            if (_border == null)
                return;

            try
            {
                // 边框透明度为 100% 或宽度为 0 时，隐藏边框
                if (Data.BorderOpacity >= 100 || Data.BorderWidth <= 0)
                {
                    _border.BorderThickness = new System.Windows.Thickness(0);
                    _border.BorderBrush = WpfBrushes.Transparent;
                    return;
                }

                // 解析边框颜色
                var borderColor = (WpfColor)WpfColorConverter.ConvertFromString(Data.BorderColor);

                // ✅ 应用透明度（反转逻辑：0% = 完全不透明，100% = 完全透明）
                byte alpha = (byte)(255 * (100 - Data.BorderOpacity) / 100.0);
                var borderColorWithAlpha = WpfColor.FromArgb(alpha, borderColor.R, borderColor.G, borderColor.B);

                // 设置边框
                _border.BorderBrush = new WpfSolidColorBrush(borderColorWithAlpha);
                _border.BorderThickness = new System.Windows.Thickness(Data.BorderWidth);
                _border.CornerRadius = new System.Windows.CornerRadius(Data.BorderRadius);

// #if DEBUG
//                 System.Diagnostics.Debug.WriteLine($"✅ [ApplyBorderStyle] 颜色={Data.BorderColor}, 宽度={Data.BorderWidth}, 圆角={Data.BorderRadius}, 透明度={Data.BorderOpacity}%");
// #endif
            }
            catch (Exception)
            {
// #if DEBUG
//                 System.Diagnostics.Debug.WriteLine($"❌ [ApplyBorderStyle] 失败: {ex.Message}");
// #endif
            }
        }

        /// <summary>
        /// ✅ 应用背景样式到 Border 容器（支持圆角）
        /// </summary>
        private void ApplyBackgroundStyle()
        {
            if (_richTextBox == null || _border == null)
                return;

            try
            {
                // 背景透明度为 100% 时，使用透明背景
                if (Data.BackgroundOpacity >= 100)
                {
                    _border.Background = WpfBrushes.Transparent;
                    _richTextBox.Background = WpfBrushes.Transparent;
                    // 圆角仍然应用到 Border（与边框圆角共享）
                    ApplyBackgroundCornerRadius();
                    return;
                }

                // 解析背景颜色
                WpfColor backgroundColor;
                if (Data.BackgroundColor == "Transparent" || string.IsNullOrEmpty(Data.BackgroundColor))
                {
                    _border.Background = WpfBrushes.Transparent;
                    _richTextBox.Background = WpfBrushes.Transparent;
                    ApplyBackgroundCornerRadius();
                    return;
                }
                else
                {
                    backgroundColor = (WpfColor)WpfColorConverter.ConvertFromString(Data.BackgroundColor);
                }

                // ✅ 应用透明度（反转逻辑：0% = 完全不透明，100% = 完全透明）
                byte alpha = (byte)(255 * (100 - Data.BackgroundOpacity) / 100.0);
                var backgroundColorWithAlpha = WpfColor.FromArgb(alpha, backgroundColor.R, backgroundColor.G, backgroundColor.B);

                // ✅ 设置背景到 Border 容器（支持圆角）
                _border.Background = new WpfSolidColorBrush(backgroundColorWithAlpha);
                // RichTextBox 保持透明，让 Border 的背景透过
                _richTextBox.Background = WpfBrushes.Transparent;

                // ✅ 应用背景圆角到 Border 容器
                ApplyBackgroundCornerRadius();

// #if DEBUG
//                 System.Diagnostics.Debug.WriteLine($"✅ [ApplyBackgroundStyle] 颜色={Data.BackgroundColor}, 透明度={Data.BackgroundOpacity}%, 圆角={Data.BackgroundRadius}");
// #endif
            }
            catch (Exception)
            {
// #if DEBUG
//                 System.Diagnostics.Debug.WriteLine($"❌ [ApplyBackgroundStyle] 失败: {ex.Message}");
// #endif
            }
        }

        /// <summary>
        /// ✅ 应用背景圆角到 Border 容器
        /// </summary>
        private void ApplyBackgroundCornerRadius()
        {
            if (_border == null)
                return;

            try
            {
                // 背景圆角和边框圆角取最大值（确保圆角效果正确显示）
                double maxRadius = Math.Max(Data.BackgroundRadius, Data.BorderRadius);
                _border.CornerRadius = new System.Windows.CornerRadius(maxRadius);
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}
