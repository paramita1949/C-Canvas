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
        #region 辅助方法

        /// <summary>
        /// 重置光标闪烁（使光标立即显示）
        /// </summary>
        private void ResetCursorBlink()
        {
            _cursorVisible = true;
            _cursorBlinkTimer.Stop();
            _cursorBlinkTimer.Start();
        }

        /// <summary>
        /// 获取文本总长度
        /// </summary>
        private int GetTotalTextLength()
        {
            if (_textElement.IsRichTextMode)
            {
                return _textElement.RichTextSpans.Sum(span => span.Text.Length);
            }
            else
            {
                return _textElement.Content?.Length ?? 0;
            }
        }

        /// <summary>
        /// 获取选中的文本
        /// </summary>
        private string GetSelectedText()
        {
            if (!HasSelection)
                return string.Empty;

            int start = SelectionStart;
            int length = SelectionLength;

            if (_textElement.IsRichTextMode)
            {
                // 从 RichTextSpans 中提取选中文本
                var result = new System.Text.StringBuilder();
                int currentPos = 0;

                foreach (var span in _textElement.RichTextSpans.OrderBy(s => s.SpanOrder))
                {
                    int spanLength = span.Text.Length;
                    int spanEnd = currentPos + spanLength;

                    if (spanEnd <= start)
                    {
                        currentPos = spanEnd;
                        continue;
                    }

                    if (currentPos >= start + length)
                        break;

                    int copyStart = Math.Max(0, start - currentPos);
                    int copyEnd = Math.Min(spanLength, start + length - currentPos);
                    int copyLength = copyEnd - copyStart;

                    if (copyLength > 0)
                    {
                        result.Append(span.Text.Substring(copyStart, copyLength));
                    }

                    currentPos = spanEnd;
                }

                return result.ToString();
            }
            else
            {
                return _textElement.Content.Substring(start, length);
            }
        }

        /// <summary>
        /// 在指定位置插入文本
        /// </summary>
        private void InsertTextAtPosition(int position, string text)
        {
            if (_textElement.IsRichTextMode)
            {
                // TODO: 实现 RichTextSpans 的文本插入逻辑
                // 当前简化实现：转换为纯文本模式
                ConvertToPlainText();
                _textElement.Content = _textElement.Content.Insert(position, text);
            }
            else
            {
                if (_textElement.Content == null)
                    _textElement.Content = "";
                _textElement.Content = _textElement.Content.Insert(position, text);
            }
        }

        /// <summary>
        /// 在指定位置删除文本
        /// </summary>
        private void DeleteTextAtPosition(int position, int length)
        {
            if (_textElement.IsRichTextMode)
            {
                // TODO: 实现 RichTextSpans 的文本删除逻辑
                // 当前简化实现：转换为纯文本模式
                ConvertToPlainText();
                _textElement.Content = _textElement.Content.Remove(position, length);
            }
            else
            {
                if (_textElement.Content != null && position + length <= _textElement.Content.Length)
                {
                    _textElement.Content = _textElement.Content.Remove(position, length);
                }
            }
        }

        /// <summary>
        /// 转换为纯文本模式（临时方案）
        /// </summary>
        private void ConvertToPlainText()
        {
            if (_textElement.IsRichTextMode)
            {
                var allText = string.Join("", _textElement.RichTextSpans.OrderBy(s => s.SpanOrder).Select(s => s.Text));
                _textElement.Content = allText;
                _textElement.RichTextSpans.Clear();
            }
        }

        #endregion
    }
}
