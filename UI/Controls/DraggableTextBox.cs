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
                Style = CreateNoPaddingTextBoxStyle()  // 🔧 使用自定义样式完全移除内边距
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
                catch (Exception)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ [TextBox] 移除内部边距失败");
                }
            };

            // 监听文本变化
            _textBox.TextChanged += (s, e) =>
            {
                Data.Content = _textBox.Text;
                ContentChanged?.Invoke(this, _textBox.Text);
            };

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
                
                // 如果点击在文本框内部或Grid内部（文本区域），允许文本编辑
                if (e.OriginalSource is WpfTextBox || e.OriginalSource is WpfGrid)
                {
                    //System.Diagnostics.Debug.WriteLine($"🖱️ 点击文本区域，尝试获取焦点");
                    //System.Diagnostics.Debug.WriteLine($"   TextBox状态: IsEnabled={_textBox.IsEnabled}, Focusable={_textBox.Focusable}, IsVisible={_textBox.IsVisible}, Visibility={_textBox.Visibility}");
                    
                    // 先选中控件（显示选中框）
                    Focus();
                    SetSelected(true);
                    
                    // 使用 Dispatcher 延迟聚焦，确保UI已更新
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _textBox.IsEnabled = true;
                        _textBox.Focusable = true;
                        bool focused = _textBox.Focus();
                        //System.Diagnostics.Debug.WriteLine($"🖱️ TextBox.Focus() 结果: {focused}, IsFocused={_textBox.IsFocused}");
                        if (focused)
                        {
                            //System.Diagnostics.Debug.WriteLine($"✅ TextBox 成功获得焦点");
                        }
                        else
                        {
                            //System.Diagnostics.Debug.WriteLine($"❌ TextBox 无法获得焦点");
                            //System.Diagnostics.Debug.WriteLine($"   父容器: Parent={Parent?.GetType().Name}");
                            //System.Diagnostics.Debug.WriteLine($"   IsLoaded={_textBox.IsLoaded}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                    
                    // 不处理此事件，让它继续路由到TextBox
                    return;
                }

                // 如果点击在边框或其他区域，先移除文本框焦点，然后启动拖拽
                if (_textBox.IsFocused)
                {
                    // 移除文本框焦点，让 DraggableTextBox 重新获得焦点
                    System.Windows.Input.Keyboard.ClearFocus();
                }
                
                // 选中控件并获得焦点
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
            
            if (selected)
            {
                // 选中时：显示虚线边框和淡蓝色半透明背景
                _selectionRect.Visibility = System.Windows.Visibility.Visible;
                _border.Background = new WpfSolidColorBrush(WpfColor.FromArgb(20, 33, 150, 243));
                _resizeThumb.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                // 未选中时：完全隐藏所有编辑元素
                _selectionRect.Visibility = System.Windows.Visibility.Collapsed;
                _border.Background = WpfBrushes.Transparent;
                _resizeThumb.Visibility = System.Windows.Visibility.Collapsed;
            }

            SelectionChanged?.Invoke(this, selected);
        }

        #endregion

        #region 键盘事件

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Del键删除（需要父窗口处理）
            if (e.Key == WpfKey.Delete && !_textBox.IsFocused)
            {
                e.Handled = true;
                // 触发删除事件由父窗口处理
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
        public void FocusTextBox()
        {
            _textBox.Focus();
            _textBox.SelectAll();
        }

        #endregion
    }
}
