using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ImageColorChanger.Services.LiveCaption;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ImageColorChanger.UI.LiveCaption
{
    /// <summary>统一输入与识别记录；合并输出通知，只在安全空闲点收缩已经保存的旧文。</summary>
    internal sealed class CaptionTranscriptTextBoxAdapter : IDisposable
    {
        private readonly TextBox _editor;
        private readonly InputMethodCaptionSnapshotBuffer _buffer = new();
        private readonly DispatcherTimer _replacementTimer;
        private readonly DispatcherTimer _publishTimer;
        private readonly DispatcherTimer _maintenanceTimer;
        private readonly InputMethodCaptionHistoryStore _history;
        private DispatcherOperation _completionOperation;
        private bool _completionRequiresFocusLost;
        private bool _publishQueued;
        private bool _archiveInProgress;
        private int _documentRevision;
        private long _nextArchiveAttemptTick;
        private bool _enabled;
        private bool _disposed;
        private bool _clearing;
        private int _compositionRevision;
        private int _changeSequence;
        private string _recognitionDraft = string.Empty;
        private string _recognitionLastFinal = string.Empty;

        public CaptionTranscriptTextBoxAdapter(TextBox editor, InputMethodCaptionHistoryStore history = null)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _history = history ?? new InputMethodCaptionHistoryStore();
            _editor.UndoLimit = InputMethodCaptionRetentionPolicy.UndoLimit;
            _replacementTimer = new DispatcherTimer(DispatcherPriority.Background, editor.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _replacementTimer.Tick += OnReplacementTimer;
            _publishTimer = new DispatcherTimer(DispatcherPriority.Background, editor.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _publishTimer.Tick += OnPublishTimer;
            _maintenanceTimer = new DispatcherTimer(DispatcherPriority.Background, editor.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _maintenanceTimer.Tick += OnMaintenanceTimer;
            _editor.TextChanged += OnTextChanged;
            _editor.GotKeyboardFocus += OnGotKeyboardFocus;
            _editor.LostKeyboardFocus += OnLostKeyboardFocus;
            _editor.PreviewKeyDown += OnPreviewKeyDown;
            TextCompositionManager.AddPreviewTextInputStartHandler(_editor, OnCompositionStarted);
            TextCompositionManager.AddPreviewTextInputUpdateHandler(_editor, OnCompositionUpdated);
            TextCompositionManager.AddPreviewTextInputHandler(_editor, OnTextInput);
        }

        public event Action<string> SnapshotPublished;
        public event Action<string> PrefixArchived;
        public event Action<string> ArchiveStatusChanged;
        public string PublishedText => _buffer.PublishedText;
        public bool IsComposing => _buffer.IsComposing;
        public bool HasPendingSnapshot => _buffer.HasPendingSnapshot;
        public long LeadingTextElementCount { get; private set; }
        public string HistoryDirectory => _history.RootDirectory;

        public void SetEnabled(bool enabled)
        {
            if (_disposed || _enabled == enabled) return;
            _enabled = enabled;
            _compositionRevision++;
            _documentRevision++;
            CancelPendingNotifications();
            _maintenanceTimer.Stop();
            _buffer.Reset(enabled ? _editor.Text : string.Empty);
            if (enabled) _maintenanceTimer.Start();
            Log("mode-changed");
        }

        public void Clear()
        {
            if (_disposed) return;
            _compositionRevision++;
            _documentRevision++;
            CancelPendingNotifications();
            _clearing = true;
            bool undo = _editor.IsUndoEnabled;
            try
            {
                _editor.IsUndoEnabled = false;
                _editor.Clear();
            }
            finally { _editor.IsUndoEnabled = undo; _clearing = false; }
            LeadingTextElementCount = 0;
            _history.Reset();
            _recognitionDraft = string.Empty;
            _recognitionLastFinal = string.Empty;
            _buffer.Reset(_editor.Text);
            if (_enabled) SnapshotPublished?.Invoke(_buffer.PublishedText);
        }

        public System.Threading.Tasks.Task ExportHistoryAsync(string path) => _history.ExportAsync(path, _editor.Text);

        public void ResetRecognitionSegment()
        {
            _compositionRevision++;
            _documentRevision++;
            CancelPendingNotifications();
            _recognitionDraft = string.Empty;
            _recognitionLastFinal = string.Empty;
            PublishIfChanged(_buffer.Reset(_editor.Text));
        }

        public void ApplyRecognitionText(in LiveCaptionAsrText update)
        {
            if (_disposed || !_enabled || !_editor.IsReadOnly) return;
            string incoming = CaptionRecognitionTextPolicy.Normalize(update.Text);
            if (incoming.Length == 0 || (update.IsFinal && incoming == _recognitionLastFinal)) return;

            if (!update.IsFinal && CaptionRecognitionTextPolicy.ShouldRotateDraftToNextUtterance(_recognitionDraft, incoming))
            {
                _recognitionLastFinal = _recognitionDraft;
                _recognitionDraft = string.Empty;
            }
            string next = update.IsFinal ? incoming : CaptionRecognitionTextPolicy.MergeDraftStable(_recognitionDraft, incoming);
            if (next != _recognitionDraft) ReplaceRecognitionTail(next);
            _recognitionDraft = update.IsFinal ? string.Empty : next;
            if (update.IsFinal) _recognitionLastFinal = incoming;
        }

        private void ReplaceRecognitionTail(string next)
        {
            int start = _editor.Text.Length - _recognitionDraft.Length;
            int selectionStart = _editor.SelectionStart;
            int selectionEnd = selectionStart + _editor.SelectionLength;
            double offset = _editor.VerticalOffset;
            bool follow = _editor.SelectionLength == 0 &&
                _editor.VerticalOffset + _editor.ViewportHeight >= _editor.ExtentHeight - 2;
            bool undo = _editor.IsUndoEnabled;
            _clearing = true;
            try
            {
                _editor.IsUndoEnabled = false;
                _editor.BeginChange();
                try
                {
                    _editor.Select(start, _recognitionDraft.Length);
                    _editor.SelectedText = next;
                    if (follow) _editor.Select(_editor.Text.Length, 0);
                    else
                    {
                        int Map(int position) => position <= start ? position : start + Math.Min(position - start, next.Length);
                        int mappedStart = Map(selectionStart);
                        _editor.Select(mappedStart, Math.Max(0, Map(selectionEnd) - mappedStart));
                    }
                }
                finally { _editor.EndChange(); }
                if (follow) _editor.ScrollToEnd();
                else _editor.ScrollToVerticalOffset(offset);
            }
            finally { _editor.IsUndoEnabled = undo; _clearing = false; }
            PublishIfChanged(_buffer.Reset(_editor.Text));
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
            if (!_enabled || _disposed || _editor.IsReadOnly) return;
            _compositionRevision++;
            AbortCompletion();
            _replacementTimer.Stop();
            _buffer.BeginComposition();
            Log("composition-begin");
        }

        private void OnCompositionUpdated(object sender, TextCompositionEventArgs e)
        {
            if (!_enabled || _disposed || _editor.IsReadOnly) return;
            // 兼容只观察到 Update 的控件事件链；已有合成不重复推进版本。
            if (!_buffer.IsComposing)
            {
                _compositionRevision++;
                AbortCompletion();
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
            if (_completionOperation?.Status == DispatcherOperationStatus.Pending)
            {
                _completionRequiresFocusLost &= requireFocusLost;
                return;
            }
            int revision = _compositionRevision;
            _completionRequiresFocusLost = requireFocusLost;
            // PreviewTextInput 在 TextBox 默认提交之前发生，不能在预览事件里读最终文本。
            _completionOperation = _editor.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _completionOperation = null;
                if (!_enabled || _disposed || revision != _compositionRevision) return;
                if (_completionRequiresFocusLost && _editor.IsKeyboardFocusWithin) return;
                _replacementTimer.Stop();
                PublishIfChanged(_buffer.CompleteComposition(_editor.Text));
                Log("composition-settled");
            }));
        }

        private void PublishIfChanged(bool changed)
        {
            if (!changed || !_enabled || _disposed) return;
            _publishQueued = true;
            if (!_publishTimer.IsEnabled) _publishTimer.Start();
        }

        private void OnPublishTimer(object sender, EventArgs e) => PublishPendingSnapshot();

        private void PublishPendingSnapshot()
        {
            _publishTimer.Stop();
            if (!_publishQueued || !_enabled || _disposed) return;
            _publishQueued = false;
            if (_buffer.PublishedText.Length == 0)
            {
                LeadingTextElementCount = 0;
                _history.Reset();
            }
            SnapshotPublished?.Invoke(_buffer.PublishedText);
        }

        private async void OnMaintenanceTimer(object sender, EventArgs e)
        {
            if (!_enabled || _disposed || _archiveInProgress || _buffer.IsComposing || _buffer.HasPendingSnapshot ||
                Environment.TickCount64 < _nextArchiveAttemptTick) return;
            string text = _editor.Text;
            // 自动识别只保留工作区，回看选区不会使记录无限增长；未确认的临时句不裁剪。
            int protectedStart = Math.Min(_editor.IsReadOnly ? text.Length : _editor.SelectionStart,
                text.Length - _recognitionDraft.Length);
            int trim = InputMethodCaptionRetentionPolicy.GetTrimLength(text, protectedStart);
            if (trim == 0) return;

            _archiveInProgress = true;
            int revision = _documentRevision;
            string prefix = text.Substring(0, trim);
            InputMethodCaptionHistoryStore.StagedPrefix staged = null;
            try
            {
                staged = await _history.StagePrefixAsync(prefix);
                if (!CanTrimPrefix(prefix, revision)) return;
                await _history.CommitAsync(staged);
                if (!CanTrimPrefix(prefix, revision)) return;
                PublishPendingSnapshot();

                int selectionStart = _editor.SelectionStart;
                int selectionLength = _editor.SelectionLength;
                long leading = InputMethodCaptionRetentionPolicy.GetLeadingTextElementsAfterTrim(prefix, LeadingTextElementCount);
                bool undo = _editor.IsUndoEnabled;
                _clearing = true;
                try
                {
                    _editor.IsUndoEnabled = false;
                    _editor.BeginChange();
                    try
                    {
                        _editor.Select(0, trim);
                        _editor.SelectedText = string.Empty;
                        int mappedStart = Math.Max(0, selectionStart - trim);
                        _editor.Select(mappedStart, Math.Max(0, selectionStart + selectionLength - trim - mappedStart));
                    }
                    finally { _editor.EndChange(); }
                }
                finally
                {
                    _editor.IsUndoEnabled = undo;
                    _clearing = false;
                }
                _history.Accept(staged);
                LeadingTextElementCount = leading;
                _buffer.Reset(_editor.Text);
                PrefixArchived?.Invoke(prefix);
                SnapshotPublished?.Invoke(_buffer.PublishedText);
                ArchiveStatusChanged?.Invoke("早期文字已自动保存；右键可导出全部字幕记录");
                Log("history-compacted");
            }
            catch (Exception ex)
            {
                _nextArchiveAttemptTick = Environment.TickCount64 + 10000;
                InputMethodCaptionDiagnostics.Log($"editor archive=failed type={ex.GetType().Name}");
                if (!_disposed && _enabled)
                    ArchiveStatusChanged?.Invoke("自动保存未完成，旧文保留；可右键保存全部字幕记录");
            }
            finally
            {
                try { await _history.DiscardAsync(staged); }
                catch (Exception ex) { InputMethodCaptionDiagnostics.Log($"editor archive-cleanup=failed type={ex.GetType().Name}"); }
                _archiveInProgress = false;
            }
        }

        private bool CanTrimPrefix(string prefix, int revision) => !_disposed && _enabled && revision == _documentRevision &&
            !_buffer.IsComposing && !_buffer.HasPendingSnapshot &&
            (_editor.IsReadOnly || _editor.SelectionStart >= prefix.Length) &&
            prefix.Length <= _editor.Text.Length - _recognitionDraft.Length &&
            _editor.Text.StartsWith(prefix, StringComparison.Ordinal);

        private void AbortCompletion()
        {
            _completionOperation?.Abort();
            _completionOperation = null;
        }

        private void CancelPendingNotifications()
        {
            _replacementTimer.Stop();
            _publishTimer.Stop();
            _publishQueued = false;
            AbortCompletion();
        }

        private void Log(string action) => InputMethodCaptionDiagnostics.Log(
            $"editor {action} mode={_enabled} focused={_editor.IsKeyboardFocusWithin} composing={_buffer.IsComposing} sequence={_changeSequence} chars={_editor.Text.Length} pending={_buffer.HasPendingSnapshot}");

        public void Dispose()
        {
            if (_disposed) return;
            // 真正关闭窗口时保留最后工作区；不等待磁盘，也不在销毁编辑控件时重建全文。
            _ = _history.SaveFinalAsync(_editor.Text).ContinueWith(task =>
            {
                if (task.IsFaulted) InputMethodCaptionDiagnostics.Log($"editor final-save=failed type={task.Exception?.GetBaseException().GetType().Name}");
            }, System.Threading.Tasks.TaskScheduler.Default);
            _disposed = true;
            _enabled = false;
            _compositionRevision++;
            _documentRevision++;
            CancelPendingNotifications();
            _maintenanceTimer.Stop();
            _buffer.Reset();
            _replacementTimer.Tick -= OnReplacementTimer;
            _publishTimer.Tick -= OnPublishTimer;
            _maintenanceTimer.Tick -= OnMaintenanceTimer;
            _editor.TextChanged -= OnTextChanged;
            _editor.GotKeyboardFocus -= OnGotKeyboardFocus;
            _editor.LostKeyboardFocus -= OnLostKeyboardFocus;
            _editor.PreviewKeyDown -= OnPreviewKeyDown;
            TextCompositionManager.RemovePreviewTextInputStartHandler(_editor, OnCompositionStarted);
            TextCompositionManager.RemovePreviewTextInputUpdateHandler(_editor, OnCompositionUpdated);
            TextCompositionManager.RemovePreviewTextInputHandler(_editor, OnTextInput);
            SnapshotPublished = null;
            PrefixArchived = null;
            ArchiveStatusChanged = null;
        }
    }
}
