using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow TextEditor Toolbar Split-Line Drawing
    /// </summary>
    public partial class MainWindow
    {
        private void ClearSplitLines()
        {
            var splitLines = EditorCanvas.Children
                .OfType<Line>()
                .Where(l => l.Tag != null && l.Tag.ToString() == "SplitLine")
                .ToList();

            foreach (var line in splitLines)
            {
                EditorCanvas.Children.Remove(line);
            }
        }

        private void DrawVerticalLine(double x, double y1, double y2)
        {
            var line = new Line
            {
                X1 = x,
                Y1 = y1,
                X2 = x,
                Y2 = y2,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(SPLIT_LINE_COLOR_R, SPLIT_LINE_COLOR_G, SPLIT_LINE_COLOR_B)),
                StrokeThickness = SPLIT_LINE_THICKNESS_MAIN,
                StrokeDashArray = new DoubleCollection { SPLIT_LINE_DASH_LENGTH, SPLIT_LINE_DASH_GAP },
                Tag = "SplitLine",
                IsHitTestVisible = false
            };
            Canvas.SetZIndex(line, 1000);
            EditorCanvas.Children.Add(line);
        }

        private void DrawHorizontalLine(double y, double x1, double x2)
        {
            var line = new Line
            {
                X1 = x1,
                Y1 = y,
                X2 = x2,
                Y2 = y,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(SPLIT_LINE_COLOR_R, SPLIT_LINE_COLOR_G, SPLIT_LINE_COLOR_B)),
                StrokeThickness = SPLIT_LINE_THICKNESS_MAIN,
                StrokeDashArray = new DoubleCollection { SPLIT_LINE_DASH_LENGTH, SPLIT_LINE_DASH_GAP },
                Tag = "SplitLine",
                IsHitTestVisible = false
            };
            Canvas.SetZIndex(line, 1000);
            EditorCanvas.Children.Add(line);
        }
    }
}
