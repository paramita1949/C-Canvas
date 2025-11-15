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
    public class RichTextEditor
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

        #region 键盘输入处理

        /// <summary>
        /// 处理键盘输入
        /// </summary>
        public void HandleKeyDown(System.Windows.Input.KeyEventArgs e)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"⌨️ [KeyDown] Key={e.Key}, Modifiers={Keyboard.Modifiers}, Handled={e.Handled}");
#endif

            if (!_isEditing)
                return;

            // Ctrl 组合键
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.A:  // 全选
                        SelectAll();
                        e.Handled = true;
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"✅ [KeyDown] Ctrl+A 全选");
#endif
                        return;
                    case Key.C:  // 复制
                        CopySelection();
                        e.Handled = true;
                        return;
                    case Key.X:  // 剪切
                        CutSelection();
                        e.Handled = true;
                        return;
                    case Key.V:  // 粘贴
                        PasteFromClipboard();
                        e.Handled = true;
                        return;
                }
            }

            // 方向键和功能键
            bool shiftPressed = Keyboard.Modifiers == ModifierKeys.Shift;
            switch (e.Key)
            {
                case Key.Left:
                    MoveCursor(-1, shiftPressed);
                    e.Handled = true;
                    return;
                case Key.Right:
                    MoveCursor(1, shiftPressed);
                    e.Handled = true;
                    return;
                case Key.Up:
                    MoveCursorUp(shiftPressed);
                    e.Handled = true;
                    return;
                case Key.Down:
                    MoveCursorDown(shiftPressed);
                    e.Handled = true;
                    return;
                case Key.Home:
                    MoveCursorToLineStart(shiftPressed);
                    e.Handled = true;
                    return;
                case Key.End:
                    MoveCursorToLineEnd(shiftPressed);
                    e.Handled = true;
                    return;
                case Key.Back:  // Backspace
                    DeleteBackward();
                    e.Handled = true;
                    return;
                case Key.Delete:
                    DeleteForward();
                    e.Handled = true;
                    return;
                case Key.Enter:
                    InsertText("\n");
                    e.Handled = true;
                    return;
            }

            // 🔧 空格键特殊处理：直接插入，不等待 TextInput
            // 原因：在某些输入法状态下，空格键的 TextInput 事件可能不触发
            if (e.Key == Key.Space)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚡ [KeyDown] 空格键直接插入");
#endif
                InsertText(" ");
                e.Handled = true;
                return;
            }

            // 🔧 其他可打印字符：阻止 KeyDown 默认行为，等待 TextInput 事件处理
            // 这样可以正确处理 IME 输入（中文、日文等）
            if (IsTextInputKey(e.Key))
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"🚫 [KeyDown] 可打印字符被拦截: Key={e.Key}, 等待 TextInput 事件处理");
#endif
                e.Handled = true;
                return;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"⚠️ [KeyDown] 未处理的按键: Key={e.Key}");
#endif
        }

        /// <summary>
        /// 判断按键是否会产生文本输入
        /// </summary>
        private bool IsTextInputKey(Key key)
        {
            // 字母和数字键
            if ((key >= Key.A && key <= Key.Z) || (key >= Key.D0 && key <= Key.D9))
                return true;

            // 小键盘数字键
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return true;

            // 标点符号和特殊字符键
            // 这些键在不同键盘布局下可能产生不同字符，统一由 TextInput 处理
            if (key >= Key.Oem1 && key <= Key.Oem102)
                return true;

            // 小键盘运算符
            if (key == Key.Add || key == Key.Subtract || key == Key.Multiply || key == Key.Divide || key == Key.Decimal)
                return true;

            return false;
        }

        /// <summary>
        /// 处理文本输入
        /// </summary>
        public void HandleTextInput(TextCompositionEventArgs e)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"✍️ [HandleTextInput] Text='{e.Text}', Length={e.Text?.Length}, IsEditing={_isEditing}");
            if (!string.IsNullOrEmpty(e.Text))
            {
                foreach (char c in e.Text)
                {
                    System.Diagnostics.Debug.WriteLine($"   字符: '{c}' (U+{((int)c):X4})");
                }
            }
#endif

            if (!_isEditing || string.IsNullOrEmpty(e.Text))
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [HandleTextInput] 被忽略: IsEditing={_isEditing}, TextEmpty={string.IsNullOrEmpty(e.Text)}");
#endif
                return;
            }

            InsertText(e.Text);
            e.Handled = true;

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [HandleTextInput] 已插入文本，光标位置: {_cursorPosition}");
#endif
        }

        /// <summary>
        /// 插入文本
        /// </summary>
        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // 如果有选中文本，先删除
            if (HasSelection)
            {
                DeleteSelection();
            }

            // 插入文本到光标位置
            InsertTextAtPosition(_cursorPosition, text);

            // 移动光标到插入文本后
            _cursorPosition += text.Length;
            ResetCursorBlink();

            // 清除缓存
            InvalidateLineCache();

            ContentChanged?.Invoke(this, EventArgs.Empty);
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 向后删除（Backspace）
        /// </summary>
        private void DeleteBackward()
        {
            if (HasSelection)
            {
                DeleteSelection();
            }
            else if (_cursorPosition > 0)
            {
                DeleteTextAtPosition(_cursorPosition - 1, 1);
                _cursorPosition--;
                ResetCursorBlink();
                InvalidateLineCache();
                ContentChanged?.Invoke(this, EventArgs.Empty);
                CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 向前删除（Delete）
        /// </summary>
        private void DeleteForward()
        {
            if (HasSelection)
            {
                DeleteSelection();
            }
            else if (_cursorPosition < GetTotalTextLength())
            {
                DeleteTextAtPosition(_cursorPosition, 1);
                ResetCursorBlink();
                InvalidateLineCache();
                ContentChanged?.Invoke(this, EventArgs.Empty);
                CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 删除选中文本
        /// </summary>
        private void DeleteSelection()
        {
            if (!HasSelection)
                return;

            int start = SelectionStart;
            int length = SelectionLength;

            DeleteTextAtPosition(start, length);
            _cursorPosition = start;
            ClearSelection();

            InvalidateLineCache();
            ContentChanged?.Invoke(this, EventArgs.Empty);
            CursorOrSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

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
            using var paint = new SkiaSharp.SKPaint
            {
                TextSize = fontSize,
                IsAntialias = true,
                Typeface = SkiaSharp.SKTypeface.FromFamilyName(fontFamily)
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
                    float width = paint.MeasureText(substring);

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

