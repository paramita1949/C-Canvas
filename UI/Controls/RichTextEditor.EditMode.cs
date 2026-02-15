using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.UI.Controls
{
    public partial class RichTextEditor
    {
        #region 编辑模式控制

        /// <summary>
        /// 设置文本内容（从外部同步，不触发 ContentChanged 事件）
        /// </summary>
        public void SetText(string text)
        {
            _textElement.Content = text ?? "";

            // 调整光标位置，确保不超出文本长度
            int textLength = GetTotalTextLength();
            if (_cursorPosition > textLength)
            {
                _cursorPosition = textLength;
            }

            // 清除缓存
            InvalidateLineCache();

            // 触发光标位置改变事件（用于重新渲染）
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 进入编辑模式
        /// </summary>
        public void EnterEditMode(bool selectAll = false)
        {
            _isEditing = true;
            _cursorBlinkTimer.Start();

            if (selectAll)
            {
                SelectAll();
            }
            else
            {
                // 光标移到末尾
                _cursorPosition = GetTotalTextLength();
                ClearSelection();
            }

            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 退出编辑模式
        /// </summary>
        public void ExitEditMode()
        {
            _isEditing = false;
            _cursorBlinkTimer.Stop();
            _cursorVisible = false;
            ClearSelection();
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
