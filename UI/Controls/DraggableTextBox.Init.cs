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
    public partial class DraggableTextBox
    {
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
                ClipToBounds = false  // 不裁剪内容，防止字体顶部/底部被遮挡
            };

            var grid = new WpfGrid
            {
                Background = WpfBrushes.Transparent  // 设置透明背景，使鼠标事件能够穿透
            };

            // 定义拖动区域的宽度
            const double dragAreaWidth = 30;

            // 创建四个拖动区域（上、下、左、右）
            _dragAreaTop = new WpfBorder
            {
                Background = WpfBrushes.Transparent,
                Height = dragAreaWidth,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Cursor = WpfCursors.SizeAll
            };
            _dragAreaTop.MouseLeftButtonDown += OnDragAreaMouseDown;
            _dragAreaTop.MouseMove += OnDragAreaMouseMove;
            _dragAreaTop.MouseLeftButtonUp += OnDragAreaMouseUp;

            _dragAreaBottom = new WpfBorder
            {
                Background = WpfBrushes.Transparent,
                Height = dragAreaWidth,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Cursor = WpfCursors.SizeAll
            };
            _dragAreaBottom.MouseLeftButtonDown += OnDragAreaMouseDown;
            _dragAreaBottom.MouseMove += OnDragAreaMouseMove;
            _dragAreaBottom.MouseLeftButtonUp += OnDragAreaMouseUp;

            _dragAreaLeft = new WpfBorder
            {
                Background = WpfBrushes.Transparent,
                Width = dragAreaWidth,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Cursor = WpfCursors.SizeAll
            };
            _dragAreaLeft.MouseLeftButtonDown += OnDragAreaMouseDown;
            _dragAreaLeft.MouseMove += OnDragAreaMouseMove;
            _dragAreaLeft.MouseLeftButtonUp += OnDragAreaMouseUp;

            _dragAreaRight = new WpfBorder
            {
                Background = WpfBrushes.Transparent,
                Width = dragAreaWidth,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Cursor = WpfCursors.SizeAll
            };
            _dragAreaRight.MouseLeftButtonDown += OnDragAreaMouseDown;
            _dragAreaRight.MouseMove += OnDragAreaMouseMove;
            _dragAreaRight.MouseLeftButtonUp += OnDragAreaMouseUp;

            //  初始化 WPF RichTextBox
            _richTextBox = new System.Windows.Controls.RichTextBox
            {
                // 基本属性
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Background = WpfBrushes.Transparent,
                BorderThickness = new System.Windows.Thickness(0),
                Padding = new System.Windows.Thickness(10, 10, 10, 10),  // 增加 Padding 防止文本被边框遮挡

                // 编辑属性
                AcceptsReturn = true,
                AcceptsTab = false,
                IsReadOnly = true,  // 默认只读，双击后可编辑

                // 只读模式下不拦截鼠标事件，允许父控件处理拖拽和双击
                IsHitTestVisible = false,

                // 文本和光标颜色设置
                Foreground = WpfBrushes.White,  // 默认白色文本（在深色背景上可见）
                CaretBrush = WpfBrushes.White,  // 白色光标

                //  隐藏滚动条
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Hidden
            };

            // 监听文本改变事件
            _richTextBox.TextChanged += (s, e) =>
            {
                // 防止同步过程中的循环调用
                if (_isSyncing)
                    return;

                //  动态应用行高到所有段落（包括新创建的段落）
                ApplyLineHeightToAllParagraphs();

                // 同步文本到 Data.Content
                SyncTextFromRichTextBox();
                ContentChanged?.Invoke(this, Data.Content);
            };

            //  监听文本选择改变事件（用于更新工具栏按钮状态）
            _richTextBox.SelectionChanged += (s, e) =>
            {
                // 触发事件通知 MainWindow 更新工具栏
                TextSelectionChanged?.Invoke(this, EventArgs.Empty);

                //  优化光标样式：防止光标继承斜体样式
                FixCaretStyle();
                UpdateCaretBrushForCurrentPosition();
            };

            grid.Children.Add(_richTextBox);

            // 添加四个拖动区域到 Grid（在 RichTextBox 上层，用于编辑模式下拖动）
            grid.Children.Add(_dragAreaTop);
            grid.Children.Add(_dragAreaBottom);
            grid.Children.Add(_dragAreaLeft);
            grid.Children.Add(_dragAreaRight);

            // WPF RichTextBox 自动处理鼠标事件，无需手动处理

            // 虚线选中框（叠加在文本框上方）
            _selectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = WpfBrushes.DodgerBlue,
                StrokeThickness = 1,  //  非常细的边框
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
                new System.Windows.Thickness(-8, -8, 0, 0)  //  统一边距适配 16×16 尺寸
            );
            _resizeThumbTopLeft.DragDelta += (s, e) => ResizeFromCorner(e, -1, -1, true, true);

            _resizeThumbTopCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Center,
                System.Windows.VerticalAlignment.Top,
                WpfCursors.SizeNS,
                new System.Windows.Thickness(0, -8, 0, 0)  //  统一边距适配 16×16 尺寸
            );
            _resizeThumbTopCenter.DragDelta += (s, e) => ResizeFromEdge(e, 0, -1, false, true);

            _resizeThumbTopRight = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Right,
                System.Windows.VerticalAlignment.Top,
                WpfCursors.SizeNESW,
                new System.Windows.Thickness(0, -8, -8, 0)  //  统一边距适配 16×16 尺寸
            );
            _resizeThumbTopRight.DragDelta += (s, e) => ResizeFromCorner(e, 1, -1, false, true);

            _resizeThumbBottomLeft = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Left,
                System.Windows.VerticalAlignment.Bottom,
                WpfCursors.SizeNESW,
                new System.Windows.Thickness(-8, 0, 0, -8)  //  统一边距适配 16×16 尺寸
            );
            _resizeThumbBottomLeft.DragDelta += (s, e) => ResizeFromCorner(e, -1, 1, true, false);

            _resizeThumbBottomCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Center,
                System.Windows.VerticalAlignment.Bottom,
                WpfCursors.SizeNS,
                new System.Windows.Thickness(0, 0, 0, -8)  //  统一边距适配 16×16 尺寸
            );
            _resizeThumbBottomCenter.DragDelta += (s, e) => ResizeFromEdge(e, 0, 1, false, false);

            //  左中控制点（调整宽度）
            _resizeThumbLeftCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Left,
                System.Windows.VerticalAlignment.Center,
                WpfCursors.SizeWE,
                new System.Windows.Thickness(-8, 0, 0, 0)  //  统一边距适配 16×16 尺寸
            );
            _resizeThumbLeftCenter.DragDelta += (s, e) => ResizeFromEdge(e, -1, 0, true, false);

            //  右中控制点（调整宽度）
            _resizeThumbRightCenter = CreateResizeThumb(
                System.Windows.HorizontalAlignment.Right,
                System.Windows.VerticalAlignment.Center,
                WpfCursors.SizeWE,
                new System.Windows.Thickness(0, 0, -8, 0)  //  统一边距适配 16×16 尺寸
            );
            _resizeThumbRightCenter.DragDelta += (s, e) => ResizeFromEdge(e, 1, 0, false, false);

            _resizeThumbBottomRight = new WpfThumb
            {
                Width = 16,  //  增大控制点，更容易看清
                Height = 16,
                Background = WpfBrushes.DodgerBlue,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Cursor = WpfCursors.SizeNWSE,
                Margin = new System.Windows.Thickness(0, 0, -8, -8),  //  调整边距适配新尺寸
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
//                System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] 字体名称清理: {fontFamily} -> {cleanName}");
//#endif
                
                return cleanName;
            }
            
            return fontFamily;
        }
        
        #endregion

        #region SkiaSharp渲染



        #endregion
    }
}


