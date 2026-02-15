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
        #region 光标移动

        /// <summary>
        /// 移动光标
        /// </summary>
        private void MoveCursor(int offset, bool extendSelection)
        {
            int newPosition = Math.Max(0, Math.Min(_cursorPosition + offset, GetTotalTextLength()));

            if (extendSelection)
            {
                // 扩展选择
                if (!_selectionStart.HasValue)
                {
                    _selectionStart = _cursorPosition;
                }
                _selectionEnd = newPosition;
            }
            else
            {
                // 清除选择
                ClearSelection();
            }

            _cursorPosition = newPosition;
            ResetCursorBlink();
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 移动光标到行首
        /// </summary>
        private void MoveCursorToLineStart(bool extendSelection)
        {
            string text = _textElement.Content ?? "";
            if (string.IsNullOrEmpty(text))
                return;

            // 找到当前光标所在行的起始位置
            int lineStart = _cursorPosition;

            // 向前查找，直到找到换行符或到达文本开头
            while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
            {
                lineStart--;
            }

            int newPosition = lineStart;

            if (extendSelection)
            {
                if (!_selectionStart.HasValue)
                {
                    _selectionStart = _cursorPosition;
                }
                _selectionEnd = newPosition;
            }
            else
            {
                ClearSelection();
            }

            _cursorPosition = newPosition;
            ResetCursorBlink();
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 移动光标到行尾
        /// </summary>
        private void MoveCursorToLineEnd(bool extendSelection)
        {
            string text = _textElement.Content ?? "";
            if (string.IsNullOrEmpty(text))
                return;

            // 找到当前光标所在行的结束位置
            int lineEnd = _cursorPosition;
            int textLength = text.Length;

            // 向后查找，直到找到换行符或到达文本末尾
            while (lineEnd < textLength && text[lineEnd] != '\n' && text[lineEnd] != '\r')
            {
                lineEnd++;
            }

            int newPosition = lineEnd;

            if (extendSelection)
            {
                if (!_selectionStart.HasValue)
                {
                    _selectionStart = _cursorPosition;
                }
                _selectionEnd = newPosition;
            }
            else
            {
                ClearSelection();
            }

            _cursorPosition = newPosition;
            ResetCursorBlink();
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 向上移动光标（移动到上一行）
        /// </summary>
        private void MoveCursorUp(bool extendSelection)
        {
            string text = _textElement.Content ?? "";
            if (string.IsNullOrEmpty(text))
                return;

            // 找到当前行的起始位置
            int currentLineStart = _cursorPosition;
            while (currentLineStart > 0 && text[currentLineStart - 1] != '\n' && text[currentLineStart - 1] != '\r')
            {
                currentLineStart--;
            }

            // 如果已经在第一行，不移动
            if (currentLineStart == 0)
                return;

            // 计算当前光标在行内的偏移量
            int offsetInLine = _cursorPosition - currentLineStart;

            // 找到上一行的起始位置
            int prevLineEnd = currentLineStart - 1;
            // 跳过换行符（处理 \r\n 的情况）
            while (prevLineEnd > 0 && (text[prevLineEnd] == '\n' || text[prevLineEnd] == '\r'))
            {
                prevLineEnd--;
            }

            int prevLineStart = prevLineEnd;
            while (prevLineStart > 0 && text[prevLineStart - 1] != '\n' && text[prevLineStart - 1] != '\r')
            {
                prevLineStart--;
            }

            // 计算上一行的长度
            int prevLineLength = prevLineEnd - prevLineStart + 1;

            // 尝试保持相同的列位置，如果上一行较短则移到行尾
            int newPosition = prevLineStart + Math.Min(offsetInLine, prevLineLength);

            if (extendSelection)
            {
                if (!_selectionStart.HasValue)
                {
                    _selectionStart = _cursorPosition;
                }
                _selectionEnd = newPosition;
            }
            else
            {
                ClearSelection();
            }

            _cursorPosition = newPosition;
            ResetCursorBlink();
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 向下移动光标（移动到下一行）
        /// </summary>
        private void MoveCursorDown(bool extendSelection)
        {
            string text = _textElement.Content ?? "";
            if (string.IsNullOrEmpty(text))
                return;

            // 找到当前行的起始和结束位置
            int currentLineStart = _cursorPosition;
            while (currentLineStart > 0 && text[currentLineStart - 1] != '\n' && text[currentLineStart - 1] != '\r')
            {
                currentLineStart--;
            }

            int currentLineEnd = _cursorPosition;
            while (currentLineEnd < text.Length && text[currentLineEnd] != '\n' && text[currentLineEnd] != '\r')
            {
                currentLineEnd++;
            }

            // 如果已经在最后一行，不移动
            if (currentLineEnd >= text.Length)
                return;

            // 计算当前光标在行内的偏移量
            int offsetInLine = _cursorPosition - currentLineStart;

            // 跳过换行符，找到下一行的起始位置
            int nextLineStart = currentLineEnd;
            while (nextLineStart < text.Length && (text[nextLineStart] == '\n' || text[nextLineStart] == '\r'))
            {
                nextLineStart++;
            }

            // 如果跳过换行符后已到达文本末尾，不移动
            if (nextLineStart >= text.Length)
                return;

            // 找到下一行的结束位置
            int nextLineEnd = nextLineStart;
            while (nextLineEnd < text.Length && text[nextLineEnd] != '\n' && text[nextLineEnd] != '\r')
            {
                nextLineEnd++;
            }

            // 计算下一行的长度
            int nextLineLength = nextLineEnd - nextLineStart;

            // 尝试保持相同的列位置，如果下一行较短则移到行尾
            int newPosition = nextLineStart + Math.Min(offsetInLine, nextLineLength);

            if (extendSelection)
            {
                if (!_selectionStart.HasValue)
                {
                    _selectionStart = _cursorPosition;
                }
                _selectionEnd = newPosition;
            }
            else
            {
                ClearSelection();
            }

            _cursorPosition = newPosition;
            ResetCursorBlink();
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
