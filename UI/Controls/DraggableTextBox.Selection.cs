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
        #region 选中状态

        private void OnGotFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] OnGotFocus 触发, 当前IsSelected={IsSelected}, IsInEditMode={IsInEditMode}");

            SetSelected(true);

            //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] OnGotFocus: 设置选中状态完成");
        }

        private void OnLostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] OnLostFocus 触发, 当前IsSelected={IsSelected}, IsInEditMode={IsInEditMode}");

            // 移除自动退出编辑模式的逻辑
            // 编辑模式只通过ESC键、主窗口取消选中、或点击其他编辑框来退出
            // 这样避免焦点事件时序冲突
            //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] OnLostFocus: 不自动退出编辑模式，避免时序冲突");
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;

            if (selected)
            {
                _selectionRect.Visibility = System.Windows.Visibility.Visible;
                //  不覆盖用户设置的背景色，保持当前背景

                var resizeVisibility = IsNoticeComponentElement()
                    ? System.Windows.Visibility.Collapsed
                    : System.Windows.Visibility.Visible;

                _resizeThumbTopLeft.Visibility = resizeVisibility;
                _resizeThumbTopCenter.Visibility = resizeVisibility;
                _resizeThumbTopRight.Visibility = resizeVisibility;
                _resizeThumbLeftCenter.Visibility = resizeVisibility;
                _resizeThumbRightCenter.Visibility = resizeVisibility;
                _resizeThumbBottomLeft.Visibility = resizeVisibility;
                _resizeThumbBottomCenter.Visibility = resizeVisibility;
                _resizeThumbBottomRight.Visibility = resizeVisibility;
            }
            else
            {
                _selectionRect.Visibility = System.Windows.Visibility.Collapsed;
                //  不覆盖用户设置的背景色，重新应用背景样式
                ApplyBackgroundStyle();

                _resizeThumbTopLeft.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbTopCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbTopRight.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbLeftCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbRightCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbBottomLeft.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbBottomCenter.Visibility = System.Windows.Visibility.Collapsed;
                _resizeThumbBottomRight.Visibility = System.Windows.Visibility.Collapsed;
            }

            SelectionChanged?.Invoke(this, selected);
        }

        #endregion
    }
}

