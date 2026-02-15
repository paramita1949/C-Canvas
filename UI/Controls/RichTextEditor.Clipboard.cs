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
        #region 剪贴板操作

        /// <summary>
        /// 复制选中文本
        /// </summary>
        private void CopySelection()
        {
            if (!HasSelection)
                return;

            string selectedText = GetSelectedText();
            if (!string.IsNullOrEmpty(selectedText))
            {
                System.Windows.Clipboard.SetText(selectedText);
            }
        }

        /// <summary>
        /// 剪切选中文本
        /// </summary>
        private void CutSelection()
        {
            if (!HasSelection)
                return;

            CopySelection();
            DeleteSelection();
        }

        /// <summary>
        /// 从剪贴板粘贴
        /// </summary>
        private void PasteFromClipboard()
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                string text = System.Windows.Clipboard.GetText();
                InsertText(text);
            }
        }

        #endregion
    }
}
