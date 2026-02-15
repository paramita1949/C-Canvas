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
        #region 生命周期与状态

        public void FocusTextBox(bool selectAll = false)

        {

            EnterEditMode(selectAll: selectAll);

        }

        public void ExitEditModePublic()

        {

            System.Windows.Input.Keyboard.ClearFocus();

            Focus();

        }

        public string GetEditStateDescription()

        {

            if (IsInEditMode)

                return "编辑中";

            else if (IsSelected)

                return "已选中";

            else

                return "未选中";

        }

        public void EnterEditModeForNew()

        {

            _isNewlyCreated = true;

            EnterEditMode(selectAll: true);

        }

        public void HideDecorations()

        {

            // 🔧 不隐藏边框，保留用户设置的边框样式（用于投影显示）

            // 只隐藏选择框和拖拽手柄等编辑装饰元素



            if (_selectionRect != null)

            {

                _selectionRect.Visibility = System.Windows.Visibility.Collapsed;

            }



            // 🔧 隐藏所有8个拖拽手柄

            if (_resizeThumbTopLeft != null)

                _resizeThumbTopLeft.Visibility = System.Windows.Visibility.Collapsed;

            if (_resizeThumbTopCenter != null)

                _resizeThumbTopCenter.Visibility = System.Windows.Visibility.Collapsed;

            if (_resizeThumbTopRight != null)

                _resizeThumbTopRight.Visibility = System.Windows.Visibility.Collapsed;

            if (_resizeThumbLeftCenter != null)

                _resizeThumbLeftCenter.Visibility = System.Windows.Visibility.Collapsed;

            if (_resizeThumbRightCenter != null)

                _resizeThumbRightCenter.Visibility = System.Windows.Visibility.Collapsed;

            if (_resizeThumbBottomLeft != null)

                _resizeThumbBottomLeft.Visibility = System.Windows.Visibility.Collapsed;

            if (_resizeThumbBottomCenter != null)

                _resizeThumbBottomCenter.Visibility = System.Windows.Visibility.Collapsed;

            if (_resizeThumbBottomRight != null)

                _resizeThumbBottomRight.Visibility = System.Windows.Visibility.Collapsed;



            if (IsInEditMode)

            {

                ExitEditMode();

            }

        }

        public void RestoreDecorations()

        {

            if (IsSelected)

            {

                SetSelected(true);

            }

        }

        #endregion
    }
}
