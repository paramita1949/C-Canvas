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
                    //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] ESC键按下，当前编辑模式={IsInEditMode}");

                    // 如果在编辑模式，先退出编辑模式
                    if (IsInEditMode)
                    {
                        ExitEditMode();
                    }

                    System.Windows.Input.Keyboard.ClearFocus();
                    Focus();
                    e.Handled = true;
                }
            }
        }

        #endregion
    }
}
