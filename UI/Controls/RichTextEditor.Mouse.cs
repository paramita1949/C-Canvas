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
        #region 鼠标事件处理

        /// <summary>
        /// 处理鼠标左键按下（开始拖拽选择）
        /// </summary>
        public void HandleMouseDown(System.Windows.Point mousePosition, double actualWidth, double actualHeight, float fontSize, string fontFamily)
        {
            if (!_isEditing)
                return;

            // 将鼠标位置转换为字符索引
            int charIndex = CalculateCharacterIndexFromPosition(mousePosition, actualWidth, actualHeight, fontSize, fontFamily);

            // 设置光标位置
            _cursorPosition = charIndex;

            // 清除选择
            _selectionStart = null;
            _selectionEnd = null;

            // 开始拖拽
            _isDragging = true;

            ResetCursorBlink();
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 处理鼠标移动（拖拽选择文本）
        /// </summary>
        public void HandleMouseMove(System.Windows.Point mousePosition, double actualWidth, double actualHeight, float fontSize, string fontFamily)
        {
            if (!_isEditing || !_isDragging)
                return;

            // 将鼠标位置转换为字符索引
            int charIndex = CalculateCharacterIndexFromPosition(mousePosition, actualWidth, actualHeight, fontSize, fontFamily);

            // 设置选择范围
            if (_selectionStart == null)
            {
                _selectionStart = _cursorPosition;
            }

            _selectionEnd = charIndex;
            _cursorPosition = charIndex;

            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 处理鼠标左键释放（结束拖拽选择）
        /// </summary>
        public void HandleMouseUp()
        {
            _isDragging = false;

            // 如果选择起始和结束位置相同，清除选择
            if (_selectionStart.HasValue && _selectionEnd.HasValue && _selectionStart.Value == _selectionEnd.Value)
            {
                _selectionStart = null;
                _selectionEnd = null;
                CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 将屏幕坐标转换为字符索引（使用 SkiaSharp 精确测量）
        /// </summary>
        private int CalculateCharacterIndexFromPosition(System.Windows.Point mousePosition, double actualWidth, double actualHeight, float fontSize, string fontFamily)
        {
            string text = _textElement.Content ?? "";
            if (string.IsNullOrEmpty(text))
                return 0;

            // 计算内容区域（减去 Padding）
            const double paddingLeft = 15.0;
            const double paddingTop = 15.0;

            // 计算鼠标相对于内容区域的位置
            double relativeX = mousePosition.X - paddingLeft;
            double relativeY = mousePosition.Y - paddingTop;

            if (relativeX < 0) return 0;
            if (relativeY < 0) return 0;

            // 创建 SkiaSharp Paint 用于精确测量
            using var font = new SkiaSharp.SKFont
            {
                Typeface = SkiaSharp.SKTypeface.FromFamilyName(fontFamily),
                Size = fontSize,
                Subpixel = true
            };

            // 计算行高（与渲染器保持一致）
            float lineSpacing = 1.2f; // 默认行距
            float lineHeight = fontSize * lineSpacing;

            // 分割文本为行
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.None);

            // 确定鼠标所在的行
            int lineIndex = (int)(relativeY / lineHeight);
            if (lineIndex >= lines.Length)
                return text.Length;

            // 计算前面所有行的字符总数
            int charOffset = 0;
            for (int i = 0; i < lineIndex && i < lines.Length; i++)
            {
                charOffset += lines[i].Length + 1; // +1 for newline character
            }

            // 精确计算当前行内的字符位置
            if (lineIndex < lines.Length)
            {
                string currentLine = lines[lineIndex];
                if (string.IsNullOrEmpty(currentLine))
                    return charOffset;

                // 逐字符测量，找到最接近鼠标位置的字符
                float currentX = 0;
                int bestCharIndex = 0;
                float minDistance = float.MaxValue;

                for (int i = 0; i <= currentLine.Length; i++)
                {
                    // 测量从行首到当前字符的宽度
                    string substring = currentLine.Substring(0, i);
                    float width = font.MeasureText(substring);

                    // 计算鼠标到当前位置的距离
                    float distance = Math.Abs((float)relativeX - width);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestCharIndex = i;
                        currentX = width;
                    }
                    else
                    {
                        // 距离开始增大，说明已经找到最近的位置
                        break;
                    }
                }

                return charOffset + bestCharIndex;
            }

            return Math.Min(charOffset, text.Length);
        }

        #endregion
    }
}
