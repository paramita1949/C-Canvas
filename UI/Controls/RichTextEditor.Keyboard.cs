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

//#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"⚠️ [KeyDown] 未处理的按键: Key={e.Key}");
//#endif
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
    }
}
