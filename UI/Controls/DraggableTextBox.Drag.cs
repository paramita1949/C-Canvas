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

                //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] 鼠标点击: 时间间隔={timeSinceLastClick:F0}ms, 双击={isDoubleClick}, 当前编辑模式={IsInEditMode}");

                if (_richTextBox != null)
                {
                    //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] RichTextBox状态: IsReadOnly={_richTextBox.IsReadOnly}, IsHitTestVisible={_richTextBox.IsHitTestVisible}, IsFocused={_richTextBox.IsFocused}");
                }

                // 如果是双击，进入编辑模式
                if (isDoubleClick)
                {
                    //System.Diagnostics.Debug.WriteLine("[DraggableTextBox] 检测到双击，进入编辑模式");
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
                    //System.Diagnostics.Debug.WriteLine("[DraggableTextBox] 已在编辑模式，忽略点击");
                    return;
                }

                //System.Diagnostics.Debug.WriteLine("[DraggableTextBox] 检测到单击，选中并启动拖拽");

                // 单击：选中控件（但不给RichTextBox焦点）
                SetSelected(true);

                // 只在选中后才给整个控件焦点（防止RichTextBox获得焦点）
                base.Focus();

                // 通知组件固定在参数位置，不允许鼠标拖动。
                if (IsNoticeComponentElement())
                {
                    e.Handled = true;
                    return;
                }

                // 启动拖拽
                _isDragging = true;
                _dragStartPoint = e.GetPosition(Parent as System.Windows.UIElement);
                CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (IsNoticeComponentElement())
            {
                return;
            }

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

            var pasteItem = new System.Windows.Controls.MenuItem
            {
                Header = "粘贴",
                Height = 36
            };
            pasteItem.Click += (s, args) =>
            {
                RequestPaste?.Invoke(this, EventArgs.Empty);
            };
            contextMenu.Items.Add(pasteItem);

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

        private void OnDragAreaMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton != WpfMouseButton.Left)
                return;

            if (IsNoticeComponentElement())
            {
                e.Handled = true;
                return;
            }

            // 启动拖动
            _isDragging = true;
            _dragStartPoint = e.GetPosition(Parent as System.Windows.UIElement);

            // 捕获鼠标到发送事件的控件
            var dragArea = sender as WpfBorder;
            dragArea?.CaptureMouse();

            e.Handled = true;

            //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] 拖动区域启动拖动");
        }

        private void OnDragAreaMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var dragArea = sender as WpfBorder;

            if (IsNoticeComponentElement())
            {
                return;
            }

            // 只在拖动状态下处理
            if (!_isDragging || dragArea == null || !dragArea.IsMouseCaptured)
                return;

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

            e.Handled = true;
        }

        private void OnDragAreaMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dragArea = sender as WpfBorder;

            if (_isDragging && dragArea != null && dragArea.IsMouseCaptured)
            {
                _isDragging = false;
                dragArea.ReleaseMouseCapture();
                DragEnded?.Invoke(this, EventArgs.Empty);
                e.Handled = true;

                //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] 拖动区域结束拖动");
            }
        }

        #endregion
    }
}
