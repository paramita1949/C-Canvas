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
        #region 渲染输出

        public System.Windows.Media.Imaging.BitmapSource GetRenderedBitmap(double scaleX = 1.0, double scaleY = 1.0)

        {

            if (_border == null)

                return null;



            // 根据缩放比例计算物理像素尺寸（用于高清投影）

            int physicalWidth = (int)Math.Ceiling(ActualWidth * scaleX);

            int physicalHeight = (int)Math.Ceiling(ActualHeight * scaleY);



            // 如果缩放比例为1.0，使用简单渲染（向后兼容）

            if (Math.Abs(scaleX - 1.0) < 0.01 && Math.Abs(scaleY - 1.0) < 0.01)

            {

                var renderTarget = new System.Windows.Media.Imaging.RenderTargetBitmap(

                    (int)ActualWidth,

                    (int)ActualHeight,

                    96, 96,

                    System.Windows.Media.PixelFormats.Pbgra32);

                renderTarget.Render(_border);

                return renderTarget;

            }



            // 高清渲染模式：使用 DrawingVisual 进行缩放渲染

            var drawingVisual = new System.Windows.Media.DrawingVisual();

            using (var context = drawingVisual.RenderOpen())

            {

                // 创建 VisualBrush 来捕获 _border 的内容

                var visualBrush = new System.Windows.Media.VisualBrush(_border)

                {

                    Stretch = System.Windows.Media.Stretch.None,

                    ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute,

                    Viewbox = new WpfRect(0, 0, ActualWidth, ActualHeight)

                };



                // 应用缩放变换

                context.PushTransform(new System.Windows.Media.ScaleTransform(scaleX, scaleY));



                // 绘制内容

                context.DrawRectangle(visualBrush, null, new WpfRect(0, 0, ActualWidth, ActualHeight));

            }



            // 渲染到高分辨率位图（DPI 仍然标记为 96，避免 WPF 自动缩放）

            var renderTargetHD = new System.Windows.Media.Imaging.RenderTargetBitmap(

                physicalWidth,

                physicalHeight,

                96, 96,  //  固定 96 DPI，避免 DPI 元数据导致的缩放问题

                System.Windows.Media.PixelFormats.Pbgra32);



            renderTargetHD.Render(drawingVisual);

            return renderTargetHD;

        }

        #endregion
    }
}


