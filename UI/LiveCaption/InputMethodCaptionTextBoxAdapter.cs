using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ImageColorChanger.Services.LiveCaption;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ImageColorChanger.UI.LiveCaption
{
    /// <summary>仅适配标准编辑/合成事件，不裁剪文档，不控制光标，不调用渲染或语音服务。</summary>
    internal sealed class InputMethodCaptionTextBoxAdapter : IDisposable
    {
        private readonly TextBox _editor;
        private readonly InputMethodCaptionSnapshotBuffer _buffer = new();
        private readonly DispatcherTimer _replacementTimer;
        private bool _enabled;
        private bool _disposed;
        private bool _clearing;
        private int _compositionRevision;
        private int _changeSequence;

        public InputMethodCaptionTextBoxAdapter(TextBox editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _replacementTimer = new DispatcherTimer(DispatcherPriority.Background, editor.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _replacementTimer.Tick += OnReplacementTimer;
            _editor.TextChanged += OnTextChanged;
            _editor.GotKeyboardFocus += OnGotKeyboardFocus;
            _editor.LostKeyboardFocus += OnLostKeyboardFocus;
            _editor.PreviewKeyDown += OnPreviewKeyDown;
            TextCompositionManager.AddPreviewTextInputStartHandler(_editor, OnCompositionStarted);
            TextCompositionManager.AddPreviewTextInputUpdateHandler(_editor, OnCompositionUpdated);
            TextCompositionManager.AddPreviewTextInputHandler(_editor, OnTextInput);
        }

        public event Action<string> SnapshotPublished;
        public string PublishedText => _buffer.PublishedText;
        public bool IsComposing => _buffer.IsComposing;
        public bool HasPendingSnapshot => _buffer.HasPendingSnapshot;

        public void SetEnabled(bool enabled)
        {
            if (_disposed || _enabled == enabled) return;
            _enabled = enabled;
            _compositionRevision++;
            _replacementTimer.Stop();
            _buffer.Reset(_editor.Text);
            Log("mode-changed");
        }

        public void Clear()
        {
            if (_disposed) return;
            _compositionRevision++;
            _replacementTimer.Stop();
            _clearing = true;
            try { _editor.Clear(); }
            finally { _clearing = false; }
            _buffer.Reset(_editor.Text);
            if (_enabled) SnapshotPublished?.Invoke(_buffer.PublishedText);
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            _changeSequence++;
            Log("text-changed");
            if (!_enabled || _disposed || _clearing) return;

            _replacementTimer.Stop();
            PublishIfChanged(_buffer.ObserveText(_editor.Text));
            if (_buffer.HasPendingSnapshot && !_buffer.IsComposing) _replacementTimer.Start();
        }

        private void OnReplacementTimer(object sender, EventArgs e)
        {
            _replacementTimer.Stop();
            if (_enabled && !_disposed) PublishIfChanged(_buffer.FlushPending());
        }

        private void OnCompositionStarted(object sender, TextCompositionEventArgs e)
        {
            if (!_enabled || _disposed) return;
            _compositionRevision++;
            _replacementTimer.Stop();
            _buffer.BeginComposition();
            Log("composition-begin");
        }

        private void OnCompositionUpdated(object sender, TextCompositionEventArgs e)
        {
            if (!_enabled || _disposed) return;
            // 兼容只观察到 Update 的控件事件链；已有合成不重复推进版本。
            if (!_buffer.IsComposing)
            {
                _compositionRevision++;
                _buffer.BeginComposition();
                _replacementTimer.Stop();
            }
            Log("composition-update");
        }

        private void OnTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_buffer.IsComposing) QueueCompletion(requireFocusLost: false);
        }

        private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Log("focus-gained");

        private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Log("focus-lost");
            QueueCompletion(requireFocusLost: true);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) QueueCompletion(requireFocusLost: false);
        }

        private void QueueCompletion(bool requireFocusLost)
        {
            if (!_enabled || _disposed) return;
            int revision = _compositionRevision;
            // PreviewTextInput 在 TextBox 默认提交之前发生，不能在预览事件里读最终文本。
            _editor.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!_enabled || _disposed || revision != _compositionRevision) return;
                if (requireFocusLost && _editor.IsKeyboardFocusWithin) return;
                _replacementTimer.Stop();
                PublishIfChanged(_buffer.CompleteComposition(_editor.Text));
                Log("composition-settled");
            }));
        }

        private void PublishIfChanged(bool changed)
        {
            if (changed) SnapshotPublished?.Invoke(_buffer.PublishedText);
        }

        private void Log(string action) => InputMethodCaptionDiagnostics.Log(
            $"editor {action} mode={_enabled} focused={_editor.IsKeyboardFocusWithin} composing={_buffer.IsComposing} sequence={_changeSequence} chars={_editor.Text.Length} pending={_buffer.HasPendingSnapshot}");

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;
            _compositionRevision++;
            _replacementTimer.Stop();
            _replacementTimer.Tick -= OnReplacementTimer;
            _editor.TextChanged -= OnTextChanged;
            _editor.GotKeyboardFocus -= OnGotKeyboardFocus;
            _editor.LostKeyboardFocus -= OnLostKeyboardFocus;
            _editor.PreviewKeyDown -= OnPreviewKeyDown;
            TextCompositionManager.RemovePreviewTextInputStartHandler(_editor, OnCompositionStarted);
            TextCompositionManager.RemovePreviewTextInputUpdateHandler(_editor, OnCompositionUpdated);
            TextCompositionManager.RemovePreviewTextInputHandler(_editor, OnTextInput);
            SnapshotPublished = null;
        }
    }
}
