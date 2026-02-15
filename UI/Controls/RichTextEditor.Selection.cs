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
        #region 文本选择

        /// <summary>
        /// 设置选择范围
        /// </summary>
        public void SetSelection(int start, int end)
        {
            int totalLength = GetTotalTextLength();
            _selectionStart = Math.Max(0, Math.Min(start, totalLength));
            _selectionEnd = Math.Max(0, Math.Min(end, totalLength));
            _cursorPosition = _selectionEnd.Value;
            ResetCursorBlink();
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 清除选择
        /// </summary>
        public void ClearSelection()
        {
            _selectionStart = null;
            _selectionEnd = null;
        }

        /// <summary>
        /// 全选
        /// </summary>
        public void SelectAll()
        {
            int totalLength = GetTotalTextLength();
            if (totalLength > 0)
            {
                SetSelection(0, totalLength);
            }
        }

        #endregion
    }
}
