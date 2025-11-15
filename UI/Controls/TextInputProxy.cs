using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 文本输入代理 - 使用隐藏的原生 TextBox 处理 IME 输入
    /// 这是 WPF 自定义文本编辑器的标准做法，解决输入法候选框位置问题
    /// </summary>
    public class TextInputProxy
    {
        private readonly System.Windows.Controls.TextBox _hiddenTextBox;
        private readonly FrameworkElement _hostControl;
        private bool _isUpdatingFromExternal = false;

        /// <summary>
        /// 文本改变事件（由 TextBox 触发）
        /// </summary>
        public event EventHandler<string> TextChanged;

        /// <summary>
        /// 光标位置改变事件
        /// </summary>
        public event EventHandler<int> SelectionChanged;

        public TextInputProxy(FrameworkElement hostControl)
        {
            _hostControl = hostControl;

            // 创建隐藏的 TextBox
            _hiddenTextBox = new System.Windows.Controls.TextBox
            {
                // 完全透明，不可见
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.Transparent,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CaretBrush = System.Windows.Media.Brushes.Transparent,

                // 无边框样式
                Padding = new Thickness(0),
                Margin = new Thickness(0),

                // 多行文本
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,

                // 🔧 调整尺寸：足够大以接收输入事件
                Width = 100,
                Height = 50,

                // 确保可以接收输入
                IsEnabled = true,
                IsReadOnly = false,
                Focusable = true
            };

            // 设置附加属性（不能在对象初始化器中设置）
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(_hiddenTextBox, true);
            System.Windows.Controls.SpellCheck.SetIsEnabled(_hiddenTextBox, false);
            System.Windows.Controls.Panel.SetZIndex(_hiddenTextBox, 999); // 🔧 提高层级，确保在最上层

            // 监听文本改变
            _hiddenTextBox.TextChanged += OnTextBoxTextChanged;
            
            // 监听光标位置改变
            _hiddenTextBox.SelectionChanged += OnTextBoxSelectionChanged;

            // 监听键盘事件（用于特殊按键处理）
            _hiddenTextBox.PreviewKeyDown += OnTextBoxPreviewKeyDown;
        }

        /// <summary>
        /// 获取隐藏的 TextBox 控件（需要添加到可视树）
        /// </summary>
        public System.Windows.Controls.TextBox GetTextBox() => _hiddenTextBox;

        /// <summary>
        /// 从外部更新文本内容（不触发 TextChanged 事件）
        /// </summary>
        public void SetText(string text)
        {
            _isUpdatingFromExternal = true;
            _hiddenTextBox.Text = text ?? "";
            _isUpdatingFromExternal = false;
        }

        /// <summary>
        /// 从外部更新光标位置（不触发 SelectionChanged 事件）
        /// </summary>
        public void SetSelection(int start, int length = 0)
        {
            _isUpdatingFromExternal = true;
            _hiddenTextBox.SelectionStart = Math.Max(0, Math.Min(start, _hiddenTextBox.Text.Length));
            _hiddenTextBox.SelectionLength = length;
            _isUpdatingFromExternal = false;
        }

        /// <summary>
        /// 更新 TextBox 位置（跟随光标）
        /// </summary>
        public void UpdatePosition(System.Windows.Point position)
        {
            Canvas.SetLeft(_hiddenTextBox, position.X);
            Canvas.SetTop(_hiddenTextBox, position.Y);
        }

        /// <summary>
        /// 获得焦点
        /// </summary>
        public void Focus()
        {
            _hiddenTextBox.Focus();
            System.Windows.Input.Keyboard.Focus(_hiddenTextBox);
        }

        /// <summary>
        /// 失去焦点
        /// </summary>
        public void Blur()
        {
            System.Windows.Input.Keyboard.ClearFocus();
        }

        private void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromExternal)
                return;

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"📝 [TextInputProxy] 文本改变: '{_hiddenTextBox.Text}'");
#endif

            TextChanged?.Invoke(this, _hiddenTextBox.Text);
        }

        private void OnTextBoxSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingFromExternal)
                return;

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"🎯 [TextInputProxy] 光标位置: {_hiddenTextBox.SelectionStart}");
#endif

            SelectionChanged?.Invoke(this, _hiddenTextBox.SelectionStart);
        }

        private void OnTextBoxPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"⌨️ [TextInputProxy] 按键: {e.Key}");
#endif
            // 特殊按键可以在这里处理
            // 例如：Esc 退出编辑模式
        }
    }
}

