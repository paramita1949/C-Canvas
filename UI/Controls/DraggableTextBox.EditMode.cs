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
        #region 编辑模式



        /// <summary>

        /// 进入编辑模式（双击时）

        /// </summary>

        private void EnterEditMode(bool selectAll = true)

        {

            //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] EnterEditMode 被调用, selectAll={selectAll}, 当前IsInEditMode={IsInEditMode}");



            // 清除占位符文字

            if (_isPlaceholderText)

            {

                Data.Content = "";

                _isPlaceholderText = false;

            }



            // 设置 RichTextBox 为可编辑

            if (_richTextBox != null)

            {

                _richTextBox.IsReadOnly = false;

                _richTextBox.IsHitTestVisible = true;  // 编辑模式下允许接收鼠标事件

                _richTextBox.Focus();
                UpdateCaretBrushForCurrentPosition();



                //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] RichTextBox已设置为可编辑并获取焦点");



                if (selectAll)

                {

                    _richTextBox.SelectAll();

                    //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] 已全选文本");

                }

            }

            if (System.Windows.Application.Current?.MainWindow is ImageColorChanger.UI.MainWindow mainWindow)
            {
                mainWindow.SyncProjectionNavigationHotKeys();
            }

            EditModeChanged?.Invoke(this, true);

        }



        /// <summary>

        /// 退出编辑模式（失去焦点或按Esc时）

        /// </summary>

        public void ExitEditMode()

        {

            //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] ExitEditMode 开始调用, 当前IsInEditMode={IsInEditMode}");



            if (!IsInEditMode)

            {

                //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] 当前不在编辑模式，直接返回");

                return;

            }



            // 退出编辑时仅提取快照用于持久化，不重建文档以避免视觉重排。
            _ = CaptureSnapshotForSave();



            // 设置 RichTextBox 为只读

            if (_richTextBox != null)

            {

                //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] 设置RichTextBox为只读模式");

                _richTextBox.IsReadOnly = true;

                _richTextBox.IsHitTestVisible = false;  // 只读模式下不拦截鼠标事件

                //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] RichTextBox状态: IsReadOnly={_richTextBox.IsReadOnly}, IsHitTestVisible={_richTextBox.IsHitTestVisible}");

            }

            if (System.Windows.Application.Current?.MainWindow is ImageColorChanger.UI.MainWindow mainWindow)
            {
                mainWindow.SyncProjectionNavigationHotKeys();
            }

            EditModeChanged?.Invoke(this, false);



            // 检查是否为空，如果为空则恢复占位符

            if (string.IsNullOrWhiteSpace(Data.Content))

            {

                Data.Content = DEFAULT_PLACEHOLDER;

                _isPlaceholderText = true;

                SyncTextToRichTextBox();

            }



            //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] ExitEditMode 完成，最终IsInEditMode={IsInEditMode}");



            // 验证状态一致性

            if (_richTextBox != null)

            {

                //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] 状态验证: RichTextBox.IsReadOnly={_richTextBox.IsReadOnly}, IsHitTestVisible={_richTextBox.IsHitTestVisible}");

                //System.Diagnostics.Debug.WriteLine($"[DraggableTextBox] 状态验证: 计算的IsInEditMode={IsInEditMode}");

            }

        }



        #endregion
    }
}


