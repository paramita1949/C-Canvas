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
        #region 调整大小功能

        private WpfThumb CreateResizeThumb(
            System.Windows.HorizontalAlignment hAlign,
            System.Windows.VerticalAlignment vAlign,
            System.Windows.Input.Cursor cursor,
            System.Windows.Thickness margin)
        {
            var thumb = new WpfThumb
            {
                Width = 16,  //  增大控制点，更容易看清
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
    }
}

