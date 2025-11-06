using System;
using ImageColorChanger.Database.Models;
using WpfBorder = System.Windows.Controls.Border;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfGrid = System.Windows.Controls.Grid;
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
    /// 可拖拽、可调整大小的文本框控件
    /// </summary>
    public class DraggableTextBox : WpfUserControl
    {
        #region 字段

        private bool _isDragging = false;
        private WpfPoint _dragStartPoint;
        private WpfBorder _border;
        private WpfTextBox _textBox;
        private WpfThumb _resizeThumb;
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
        /// 是否处于编辑模式（TextBox有焦点）
        /// </summary>
        public bool IsInEditMode => _textBox.IsFocused;
        
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

        public DraggableTextBox(TextElement element)
        {
            Data = element ?? throw new ArgumentNullException(nameof(element));
            InitializeComponent();
            LoadFromData();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 创建无内边距的TextBox样式
        /// </summary>
        private System.Windows.Style CreateNoPaddingTextBoxStyle()
        {
            var style = new System.Windows.Style(typeof(WpfTextBox));
            
            // 设置Padding为0
            style.Setters.Add(new System.Windows.Setter(WpfTextBox.PaddingProperty, new System.Windows.Thickness(0)));
            
            // 🔧 关键：创建自定义模板，移除内部ScrollViewer的Margin
            var template = new System.Windows.Controls.ControlTemplate(typeof(WpfTextBox));
            
            // 创建模板内容：一个没有Margin的ScrollViewer
            var factory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ScrollViewer));
            factory.Name = "PART_ContentHost";
            factory.SetValue(System.Windows.FrameworkElement.MarginProperty, new System.Windows.Thickness(0));
            factory.SetValue(System.Windows.Controls.ScrollViewer.PaddingProperty, new System.Windows.Thickness(0));
            
            template.VisualTree = factory;
            style.Setters.Add(new System.Windows.Setter(WpfTextBox.TemplateProperty, template));
            
            return style;
        }

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

            // 文本框
            _textBox = new WpfTextBox
            {
                TextWrapping = System.Windows.TextWrapping.Wrap,
                AcceptsReturn = true,
                BorderThickness = new System.Windows.Thickness(0),
                BorderBrush = WpfBrushes.Transparent,
                Background = WpfBrushes.Transparent,
                Padding = new System.Windows.Thickness(0),  // 🔧 修改：移除内边距，让文字可以真正贴边
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                // 移除默认的焦点视觉样式
                FocusVisualStyle = null,
                IsEnabled = true,  // 确保TextBox可用
                Focusable = true,   // 确保TextBox可获得焦点
                Style = CreateNoPaddingTextBoxStyle(),  // 🔧 使用自定义样式完全移除内边距
                // 🎨 设置光标颜色为亮蓝色（任何背景都清晰可见）
                CaretBrush = new WpfSolidColorBrush(WpfColor.FromRgb(0, 150, 255))  // 亮蓝色 #0096FF
            };
            
            // 禁用所有文本装饰和下划线
            _textBox.SpellCheck.IsEnabled = false;  // 禁用拼写检查
            
            // 保持输入法启用
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(_textBox, true);
            
            // 移除装饰层的代码（在Loaded事件中处理，避免阻塞焦点）
            _textBox.Loaded += (s, e) =>
            {
                try
                {
                    // 移除装饰层中的装饰器（如拼写检查下划线）
                    var adornerLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(_textBox);
                    if (adornerLayer != null)
                    {
                        var adorners = adornerLayer.GetAdorners(_textBox);
                        if (adorners != null)
                        {
                            foreach (var adorner in adorners)
                            {
                                adornerLayer.Remove(adorner);
                            }
                        }
                    }
                    
                    // 🔧 强制移除TextBox内部ScrollViewer的Margin
                    // TextBox的默认模板内部有一个ScrollViewer（PART_ContentHost），它有默认的2px边距
                    var template = _textBox.Template;
                    if (template != null)
                    {
                        var scrollViewer = template.FindName("PART_ContentHost", _textBox) as System.Windows.FrameworkElement;
                        if (scrollViewer != null)
                        {
                            scrollViewer.Margin = new System.Windows.Thickness(0);
                            //System.Diagnostics.Debug.WriteLine($"✅ [TextBox] 已移除PART_ContentHost的Margin");
                        }
                    }
                }
                catch (Exception ex)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [TextBox] 移除内部边距失败: {ex.Message}");
                    #else
                    _ = ex; // 避免未使用变量警告
                    #endif
                }
            };

            // 监听文本变化
            _textBox.TextChanged += (s, e) =>
            {
                Data.Content = _textBox.Text;
                ContentChanged?.Invoke(this, _textBox.Text);
            };

            // 🔧 禁用TextBox的默认鼠标事件，改为由外层控件统一处理
            _textBox.IsHitTestVisible = false;  // 让鼠标事件穿透到外层
            
            // 文本框获得焦点时阻止拖拽
            _textBox.GotFocus += (s, e) =>
            {
                _textBox.Cursor = WpfCursors.IBeam;
                //System.Diagnostics.Debug.WriteLine($"📝 TextBox 获得焦点: IsEnabled={_textBox.IsEnabled}, Focusable={_textBox.Focusable}");
                
                // 如果是占位符文字，清空内容并恢复正常颜色
                if (_isPlaceholderText)
                {
                    _textBox.Text = "";
                    Data.Content = "";
                    _textBox.Foreground = new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(Data.FontColor));
                    _isPlaceholderText = false;
                    //System.Diagnostics.Debug.WriteLine($"✨ 清除占位符文字");
                }
            };
            _textBox.LostFocus += (s, e) =>
            {
                _textBox.Cursor = WpfCursors.Arrow;
                // 🔧 退出编辑模式时，恢复IsHitTestVisible=false
                _textBox.IsHitTestVisible = false;
                //System.Diagnostics.Debug.WriteLine($"📝 TextBox 失去焦点");
                
                // 如果失去焦点时内容为空，恢复占位符
                if (string.IsNullOrWhiteSpace(_textBox.Text))
                {
                    _textBox.Text = DEFAULT_PLACEHOLDER;
                    Data.Content = DEFAULT_PLACEHOLDER;
                    _textBox.Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(150, 150, 150));
                    _isPlaceholderText = true;
                    //System.Diagnostics.Debug.WriteLine($"✨ 恢复占位符文字");
                }
            };
            
            // 监听键盘输入
            _textBox.PreviewKeyDown += (s, e) =>
            {
                //System.Diagnostics.Debug.WriteLine($"⌨️ TextBox 键盘输入: Key={e.Key}");
            };

            // 虚线选中框（叠加在文本框上方）
            _selectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = WpfBrushes.DodgerBlue,
                StrokeThickness = 4,  // 增加线条粗细，从2改为4
                StrokeDashArray = new System.Windows.Media.DoubleCollection { 5, 3 },
                Fill = WpfBrushes.Transparent,
                IsHitTestVisible = false,  // 不阻挡鼠标事件
                Visibility = System.Windows.Visibility.Collapsed
            };

            // 调整大小手柄（右下角）
            _resizeThumb = new WpfThumb
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
            _resizeThumb.DragDelta += ResizeThumb_DragDelta;

            grid.Children.Add(_textBox);
            grid.Children.Add(_selectionRect);
            grid.Children.Add(_resizeThumb);
            _border.Child = grid;

            Content = _border;

            // 设置控件属性
            Focusable = true;
            Cursor = WpfCursors.SizeAll;
            FocusVisualStyle = null;  // 移除默认的焦点视觉样式

            // 绑定事件
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
            MouseRightButtonDown += OnMouseRightButtonDown;
            GotFocus += OnGotFocus;
            LostFocus += OnLostFocus;
            KeyDown += OnKeyDown;
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

            // 文本内容
            _textBox.Text = Data.Content;
            
            // 检查是否是占位符文字
            _isPlaceholderText = (Data.Content == DEFAULT_PLACEHOLDER);

            // 样式
            //System.Diagnostics.Debug.WriteLine($"🔍 LoadFromData - 字体: {Data.FontFamily}");
            _textBox.FontFamily = new WpfFontFamily(Data.FontFamily);
            _textBox.FontSize = Data.FontSize * 2;  // 实际渲染时放大2倍
            
            // 如果是占位符，使用灰色；否则使用设定的颜色
            if (_isPlaceholderText)
            {
                _textBox.Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(150, 150, 150));
            }
            else
            {
                _textBox.Foreground = new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(Data.FontColor));
            }
            
            _textBox.FontWeight = Data.IsBoldBool ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
            
            _textBox.TextAlignment = Data.TextAlign switch
            {
                "Center" => System.Windows.TextAlignment.Center,
                "Right" => System.Windows.TextAlignment.Right,
                _ => System.Windows.TextAlignment.Left
            };
        }

        #endregion

        #region 拖拽功能

        private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == WpfMouseButton.Left)
            {
                //System.Diagnostics.Debug.WriteLine($"🖱️ OnMouseDown: OriginalSource={e.OriginalSource?.GetType().Name}");
                
                // 🔧 优化双击检测逻辑
                var now = DateTime.Now;
                var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;
                bool isDoubleClick = timeSinceLastClick < DOUBLE_CLICK_INTERVAL;
                _lastClickTime = now;
                
                // 如果是双击，进入编辑模式（不需要先选中）
                if (isDoubleClick)
                {
                    //System.Diagnostics.Debug.WriteLine($"🖱️ 双击检测到，进入编辑模式");
                    // 先选中控件
                    Focus();
                    SetSelected(true);
                    
                    // 🔧 双击时：
                    // - 如果是占位符或新建的框，全选（方便快速替换）
                    // - 否则只定位光标（方便继续编辑）
                    bool shouldSelectAll = _isPlaceholderText || _isNewlyCreated;
                    EnterEditMode(selectAll: shouldSelectAll);
                    _isNewlyCreated = false;  // 清除新建标记
                    e.Handled = true;
                    return;
                }
                
                // 如果已经在编辑模式（TextBox有焦点），不做处理
                if (_textBox.IsFocused)
                {
                    //System.Diagnostics.Debug.WriteLine($"🖱️ TextBox已有焦点，继续编辑");
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

                // 计算新位置
                double newX = Data.X + deltaX;
                double newY = Data.Y + deltaY;

                // 🔧 完全移除边界限制，允许文本框自由拖拽到任何位置
                // 这样文字可以真正贴到Canvas边缘甚至超出
                
                Data.X = newX;
                Data.Y = newY;

                WpfCanvas.SetLeft(this, Data.X);
                WpfCanvas.SetTop(this, Data.Y);

                _dragStartPoint = currentPoint;

                // 触发位置改变事件（用于对称联动）
                PositionChanged?.Invoke(this, new WpfPoint(Data.X, Data.Y));
            }
        }

        private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                
                // 触发拖动结束事件
                DragEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 右键点击事件 - 显示右键菜单
        /// </summary>
        private void OnMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 先选中当前文本框
            Focus();
            SetSelected(true);

            // 创建右键菜单
            var contextMenu = new System.Windows.Controls.ContextMenu();
            
            // 🔑 尝试从应用程序资源获取样式，如果失败则手动设置
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
                // 如果获取样式失败，手动设置属性
                contextMenu.FontSize = 14;
                contextMenu.BorderThickness = new System.Windows.Thickness(0);
                contextMenu.Background = new WpfSolidColorBrush(WpfColor.FromRgb(45, 45, 48));
                contextMenu.Foreground = WpfBrushes.White;
            }

            // 复制选项
            var copyItem = new System.Windows.Controls.MenuItem
            {
                Header = "复制",
                Height = 36
            };
            copyItem.Click += (s, args) =>
            {
                // 触发复制请求事件
                RequestCopy?.Invoke(this, EventArgs.Empty);
            };
            contextMenu.Items.Add(copyItem);

            // 删除选项
            var deleteItem = new System.Windows.Controls.MenuItem
            {
                Header = "删除",
                Height = 36
            };
            deleteItem.Click += (s, args) =>
            {
                // 触发删除请求事件
                RequestDelete?.Invoke(this, EventArgs.Empty);
            };
            contextMenu.Items.Add(deleteItem);

            // 显示菜单
            contextMenu.PlacementTarget = this;
            contextMenu.IsOpen = true;
            
            e.Handled = true;
        }

        #endregion

        #region 调整大小功能

        private void ResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            double newWidth = Width + e.HorizontalChange;
            double newHeight = Height + e.VerticalChange;

            // 🔧 完全移除Canvas边界限制，只保留最小尺寸限制
            // 这样调整大小时也可以自由超出Canvas边界

            // 最小尺寸限制
            if (newWidth > 50)
            {
                Width = newWidth;
                Data.Width = newWidth;
            }

            if (newHeight > 30)
            {
                Height = newHeight;
                Data.Height = newHeight;
            }

            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"📐 [TextBox] 调整大小: {Data.Width}×{Data.Height}, IsSelected={IsSelected}, 背景={_border.Background}");
            //#endif

            // 触发尺寸改变事件
            SizeChanged?.Invoke(this, new WpfSize(Data.Width, Data.Height));
        }

        #endregion

        #region 选中状态

        private void OnGotFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            SetSelected(true);
        }

        private void OnLostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            // 不在这里取消选中，由外部（画布点击）来控制
            // 这样可以保持选中框显示，直到点击画布空白处
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"📋 [TextBox] SetSelected={selected}, 当前背景={_border.Background}");
            //#endif
            
            if (selected)
            {
                // 选中时：显示虚线边框和淡蓝色半透明背景
                _selectionRect.Visibility = System.Windows.Visibility.Visible;
                _border.Background = new WpfSolidColorBrush(WpfColor.FromArgb(20, 33, 150, 243));
                _resizeThumb.Visibility = System.Windows.Visibility.Visible;
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [TextBox] 已设置选中背景: {_border.Background}");
                //#endif
            }
            else
            {
                // 未选中时：完全隐藏所有编辑元素
                _selectionRect.Visibility = System.Windows.Visibility.Collapsed;
                _border.Background = WpfBrushes.Transparent;
                _resizeThumb.Visibility = System.Windows.Visibility.Collapsed;
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🔄 [TextBox] 已设置透明背景: {_border.Background}");
                //#endif
            }

            SelectionChanged?.Invoke(this, selected);
        }

        #endregion

        #region 键盘事件

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // 只在未处于编辑模式时处理快捷键
            if (!_textBox.IsFocused)
            {
                // Del键删除
                if (e.Key == WpfKey.Delete)
                {
                    // 触发删除请求事件
                    RequestDelete?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
                // 🔧 移除Enter/F2快捷键进入编辑模式的功能
                // 现在只能通过双击进入编辑模式
            }
            else
            {
                // 🔧 编辑模式中按Esc退出编辑
                if (e.Key == WpfKey.Escape)
                {
                    // 移除TextBox焦点，返回选中状态
                    System.Windows.Input.Keyboard.ClearFocus();
                    Focus();  // 焦点回到DraggableTextBox
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
                _textBox.FontFamily = fontFamily;
                //System.Diagnostics.Debug.WriteLine($"🎨 应用字体到TextBox: {fontFamily.Source}");
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
                _textBox.FontFamily = new WpfFontFamily(fontFamily);
            }

            if (fontSize.HasValue)
            {
                Data.FontSize = fontSize.Value;
                _textBox.FontSize = fontSize.Value * 2;  // 实际渲染时放大2倍
            }

            if (color != null)
            {
                Data.FontColor = color;
                _textBox.Foreground = new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(color));
            }

            if (isBold.HasValue)
            {
                Data.IsBoldBool = isBold.Value;
                _textBox.FontWeight = isBold.Value ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
            }

            if (textAlign != null)
            {
                Data.TextAlign = textAlign;
                _textBox.TextAlignment = textAlign switch
                {
                    "Center" => System.Windows.TextAlignment.Center,
                    "Right" => System.Windows.TextAlignment.Right,
                    _ => System.Windows.TextAlignment.Left
                };
            }
        }

        /// <summary>
        /// 聚焦到文本框（进入编辑模式）
        /// </summary>
        /// <param name="selectAll">是否全选文本</param>
        public void FocusTextBox(bool selectAll = false)
        {
            EnterEditMode(selectAll: selectAll);
        }
        
        /// <summary>
        /// 进入编辑模式
        /// </summary>
        /// <param name="selectAll">是否全选文本（默认false，只定位光标）</param>
        private void EnterEditMode(bool selectAll = false)
        {
            // 启用TextBox的鼠标交互
            _textBox.IsHitTestVisible = true;
            _textBox.IsEnabled = true;
            _textBox.Focusable = true;
            
            // 聚焦并处理光标
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool focused = _textBox.Focus();
                if (focused)
                {
                    if (selectAll)
                    {
                        // 全选文本（用于快速替换内容）
                        _textBox.SelectAll();
                        //System.Diagnostics.Debug.WriteLine($"✅ 进入编辑模式：全选文本");
                    }
                    else
                    {
                        // 只定位光标到文本末尾（保持光标闪动）
                        _textBox.SelectionStart = _textBox.Text.Length;
                        _textBox.SelectionLength = 0;
                        //System.Diagnostics.Debug.WriteLine($"✅ 进入编辑模式：光标定位到末尾");
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
        
        /// <summary>
        /// 退出编辑模式（返回选中状态）
        /// </summary>
        public void ExitEditMode()
        {
            // 移除TextBox焦点，返回选中状态
            System.Windows.Input.Keyboard.ClearFocus();
            Focus(); // 焦点回到DraggableTextBox
            //System.Diagnostics.Debug.WriteLine("📝 退出编辑模式");
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
        /// 快速进入编辑模式（新建文本框专用，自动全选占位符）
        /// </summary>
        public void EnterEditModeForNew()
        {
            _isNewlyCreated = true;
            EnterEditMode(selectAll: true);  // 新建时全选，方便直接输入替换占位符
        }

        /// <summary>
        /// 隐藏UI装饰元素（用于保存缩略图时获得纯净的视觉效果）
        /// </summary>
        public void HideDecorations()
        {
            if (_border != null)
            {
                _border.BorderBrush = WpfBrushes.Transparent;
                _border.Background = WpfBrushes.Transparent;  // 🔧 关键：确保背景完全透明
            }
            if (_selectionRect != null)
            {
                _selectionRect.Visibility = System.Windows.Visibility.Collapsed;
            }
            if (_resizeThumb != null)
            {
                _resizeThumb.Visibility = System.Windows.Visibility.Collapsed;
            }
            // 隐藏光标（如果正在编辑）
            if (_textBox != null && _textBox.IsFocused)
            {
                System.Windows.Input.Keyboard.ClearFocus();
            }
            
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🎨 [TextBox] HideDecorations - 背景已设置为透明: {_border.Background}");
            //#endif
        }

        /// <summary>
        /// 恢复UI装饰元素（保存缩略图后恢复正常状态）
        /// </summary>
        public void RestoreDecorations()
        {
            if (IsSelected)
            {
                SetSelected(true);  // 重新应用选中状态
            }
        }

        #endregion
    }
}
