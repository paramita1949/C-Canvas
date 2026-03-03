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
        // 已拆分：样式/生命周期/渲染见 DraggableTextBox.Style|Lifecycle|Render.cs

        public void SetNoticeTextOffset(double offsetX)
        {
            if (_richTextBox == null)
            {
                return;
            }

            if (double.IsNaN(offsetX) || double.IsInfinity(offsetX))
            {
                offsetX = 0;
            }

            var transform = _richTextBox.RenderTransform as System.Windows.Media.TranslateTransform;
            if (transform == null)
            {
                transform = new System.Windows.Media.TranslateTransform();
                _richTextBox.RenderTransform = transform;
            }

            transform.X = Math.Abs(offsetX) < 0.01 ? 0 : offsetX;
            transform.Y = 0;
        }
    }
}
