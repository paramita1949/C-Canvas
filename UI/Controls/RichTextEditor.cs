using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 行信息缓存（用于优化光标导航和鼠标定位）
    /// </summary>
    internal class LineInfo
    {
        public string Text { get; set; }
        public int StartIndex { get; set; }  // 在完整文本中的起始索引
        public int EndIndex { get; set; }    // 在完整文本中的结束索引（不含换行符）
        public int Length => EndIndex - StartIndex;
    }

    /// <summary>
    /// 富文本编辑器核心 - 管理光标、文本选择、键盘输入
    /// </summary>
    public partial class RichTextEditor
    {
        #region 字段

        private TextElement _textElement;
        private DispatcherTimer _cursorBlinkTimer;
        private bool _cursorVisible = true;
        private int _cursorPosition = 0;  // 光标位置（字符索引）
        private int? _selectionStart = null;  // 选择起始位置
        private int? _selectionEnd = null;    // 选择结束位置
        private bool _isEditing = false;
        private bool _isDragging = false;  // 鼠标拖拽选择状态

        // 文本布局缓存
        private string _cachedText = null;
        private List<LineInfo> _cachedLines = null;
        private float _cachedFontSize = 0;
        private string _cachedFontFamily = null;

        #endregion

        #region 属性

        /// <summary>
        /// 是否处于编辑模式
        /// </summary>
        public bool IsEditing => _isEditing;

        /// <summary>
        /// 光标位置（字符索引）
        /// </summary>
        public int CursorPosition
        {
            get => _cursorPosition;
            set
            {
                _cursorPosition = Math.Max(0, Math.Min(value, GetTotalTextLength()));
                ResetCursorBlink();
            }
        }

        /// <summary>
        /// 光标是否可见（用于闪烁动画）
        /// </summary>
        public bool CursorVisible => _cursorVisible;

        /// <summary>
        /// 是否有文本选择
        /// </summary>
        public bool HasSelection => _selectionStart.HasValue && _selectionEnd.HasValue && _selectionStart != _selectionEnd;

        /// <summary>
        /// 选择起始位置
        /// </summary>
        public int SelectionStart => HasSelection ? Math.Min(_selectionStart.Value, _selectionEnd.Value) : _cursorPosition;

        /// <summary>
        /// 选择结束位置
        /// </summary>
        public int SelectionEnd => HasSelection ? Math.Max(_selectionStart.Value, _selectionEnd.Value) : _cursorPosition;

        /// <summary>
        /// 选择的文本长度
        /// </summary>
        public int SelectionLength => SelectionEnd - SelectionStart;

        /// <summary>
        /// 获取光标的视觉位置（用于 IME 候选框定位）
        /// </summary>
        /// <param name="actualWidth">控件实际宽度</param>
        /// <param name="actualHeight">控件实际高度</param>
        /// <param name="fontSize">字体大小</param>
        /// <param name="fontFamily">字体名称</param>
        /// <returns>光标位置（相对于控件左上角）</returns>
        public System.Windows.Point GetCursorVisualPosition(double actualWidth, double actualHeight, float fontSize, string fontFamily)
        {
            // 使用与渲染器相同的逻辑计算光标位置
            const double paddingLeft = 15.0;
            const double paddingTop = 15.0;

            string text = _textElement.Content ?? "";
            if (string.IsNullOrEmpty(text) || _cursorPosition == 0)
            {
                // 光标在开头
                return new System.Windows.Point(paddingLeft, paddingTop);
            }

            // 简化实现：基于字符索引和平均字符宽度估算
            // TODO: 未来使用 TextLayoutEngine 精确计算
            double avgCharWidth = fontSize * 0.5;
            double lineHeight = fontSize * 1.2;

            // 分割文本为行
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.None);

            int currentPos = 0;
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                int lineEnd = currentPos + line.Length;

                if (_cursorPosition <= lineEnd)
                {
                    // 光标在当前行
                    int charInLine = _cursorPosition - currentPos;
                    double x = paddingLeft + charInLine * avgCharWidth;
                    double y = paddingTop + lineIndex * lineHeight;
                    return new System.Windows.Point(x, y);
                }

                currentPos = lineEnd + 1; // +1 for newline
            }

            // 光标在末尾
            int lastLineIndex = lines.Length - 1;
            double endX = paddingLeft + lines[lastLineIndex].Length * avgCharWidth;
            double endY = paddingTop + lastLineIndex * lineHeight;
            return new System.Windows.Point(endX, endY);
        }

        #endregion

        #region 事件

        /// <summary>
        /// 文本内容改变事件
        /// </summary>
        public event EventHandler ContentChanged;

        /// <summary>
        /// 光标或选择改变事件（需要重新渲染）
        /// </summary>
        public event EventHandler CursorOrSelectionChanged;

        #endregion

        #region 构造函数

        public RichTextEditor(TextElement textElement)
        {
            _textElement = textElement;

            // 初始化光标闪烁定时器（500ms 间隔）
            _cursorBlinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _cursorBlinkTimer.Tick += (s, e) =>
            {
                _cursorVisible = !_cursorVisible;
                CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        #endregion
    }
}
