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
        #region 文本布局缓存

        /// <summary>
        /// 构建或更新行信息缓存
        /// </summary>
        private void UpdateLineCache(string text, float fontSize, string fontFamily)
        {
            // 检查缓存是否有效
            if (_cachedLines != null &&
                _cachedText == text &&
                _cachedFontSize == fontSize &&
                _cachedFontFamily == fontFamily)
            {
                return; // 缓存有效，无需重建
            }

            // 重建缓存
            _cachedText = text;
            _cachedFontSize = fontSize;
            _cachedFontFamily = fontFamily;
            _cachedLines = new List<LineInfo>();

            if (string.IsNullOrEmpty(text))
                return;

            // 分割文本为行
            int lineStart = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n' || text[i] == '\r')
                {
                    // 找到一行的结束
                    _cachedLines.Add(new LineInfo
                    {
                        Text = text.Substring(lineStart, i - lineStart),
                        StartIndex = lineStart,
                        EndIndex = i
                    });

                    // 跳过换行符（处理 \r\n）
                    if (i + 1 < text.Length && text[i] == '\r' && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    lineStart = i + 1;
                }
            }

            // 添加最后一行（如果有）
            if (lineStart < text.Length)
            {
                _cachedLines.Add(new LineInfo
                {
                    Text = text.Substring(lineStart),
                    StartIndex = lineStart,
                    EndIndex = text.Length
                });
            }
            else if (text.Length > 0 && (text[text.Length - 1] == '\n' || text[text.Length - 1] == '\r'))
            {
                // 文本以换行符结尾，添加空行
                _cachedLines.Add(new LineInfo
                {
                    Text = "",
                    StartIndex = text.Length,
                    EndIndex = text.Length
                });
            }
        }

        /// <summary>
        /// 获取指定字符位置所在的行索引
        /// </summary>
        private int GetLineIndexAtPosition(int position)
        {
            if (_cachedLines == null || _cachedLines.Count == 0)
                return 0;

            for (int i = 0; i < _cachedLines.Count; i++)
            {
                var line = _cachedLines[i];
                if (position >= line.StartIndex && position <= line.EndIndex)
                    return i;
            }

            return _cachedLines.Count - 1;
        }

        /// <summary>
        /// 清除缓存（文本内容改变时调用）
        /// </summary>
        private void InvalidateLineCache()
        {
            _cachedLines = null;
            _cachedText = null;
        }

        #endregion
    }
}
