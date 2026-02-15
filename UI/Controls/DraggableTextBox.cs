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
using WpfRect = System.Windows.Rect;

namespace ImageColorChanger.UI.Controls
{
    public partial class DraggableTextBox : WpfUserControl
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

        // 🔧 四个拖动区域（用于在编辑模式下拖动文本框）
        private WpfBorder _dragAreaTop;
        private WpfBorder _dragAreaBottom;
        private WpfBorder _dragAreaLeft;
        private WpfBorder _dragAreaRight;

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
        /// 让光标颜色跟随当前插入点文字颜色，避免在富文本片段里“看起来不闪烁”。
        /// </summary>
        private void UpdateCaretBrushForCurrentPosition()
        {
            if (_richTextBox == null)
                return;

            try
            {
                System.Windows.Media.Color caretColor = System.Windows.Media.Colors.White;
                var caretPosition = _richTextBox.CaretPosition;

                if (caretPosition != null)
                {
                    var caretRange = new System.Windows.Documents.TextRange(caretPosition, caretPosition);
                    var foreground = caretRange.GetPropertyValue(System.Windows.Documents.TextElement.ForegroundProperty)
                        as System.Windows.Media.SolidColorBrush;
                    if (foreground != null && foreground.Color.A > 0)
                    {
                        caretColor = foreground.Color;
                    }
                    else if (!string.IsNullOrEmpty(Data?.FontColor))
                    {
                        caretColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Data.FontColor);
                    }
                }

                _richTextBox.CaretBrush = new System.Windows.Media.SolidColorBrush(caretColor);
            }
            catch
            {
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
        /// 请求粘贴事件（由右键菜单触发）
        /// </summary>
        public event EventHandler RequestPaste;

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
    }
}
