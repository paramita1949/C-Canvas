using System;

namespace ImageColorChanger.UI.Controls
{
    public partial class DraggableTextBox
    {
        /// <summary>
        /// MCP/自动化入口使用：将文本框内容替换为纯文本并刷新 RichTextBox。
        /// </summary>
        public void SetPlainTextContentFromAutomation(string text)
        {
            if (Data == null)
            {
                return;
            }

            Data.Content = text ?? string.Empty;
            Data.RichTextSpans?.Clear();
            _isPlaceholderText = false;
            _placeholderText = DEFAULT_PLACEHOLDER;
            SyncTextToRichTextBox();
            ContentChanged?.Invoke(this, Data.Content);
        }
    }
}
