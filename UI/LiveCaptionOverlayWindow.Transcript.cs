using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.UI
{
    internal sealed partial class LiveCaptionOverlayWindow
    {
        private DispatcherOperation _inputMethodFocusOperation;
        private bool _isClosing;
        private bool _isClosed;
        private bool _historyExportInProgress;
        private static readonly RoutedUICommand ClearCaptionCommand = new("清屏", "ClearCaptionInput", typeof(LiveCaptionOverlayWindow));
        private static readonly RoutedUICommand ExportCaptionCommand = new("保存导出", "ExportCaptionInput", typeof(LiveCaptionOverlayWindow));

        public event Action<string> CaptionPrefixArchived;
        public string CaptionText => _captionTextAdapter.PublishedText;
        internal string CaptionCurrentText => _captionEditor.Text;
        internal bool HasPendingCaptionSnapshot => _captionTextAdapter.HasPendingSnapshot;
        internal bool IsCaptionTextComposing => _captionTextAdapter.IsComposing;
        internal long CaptionLeadingTextElementCount => _captionTextAdapter.LeadingTextElementCount;

        public void SetInputMethodMode(bool enabled)
        {
            if (_isClosing || _isClosed) return;
            bool changed = _inputMethodEnabled != enabled;
            bool wasSuppressed = _suppressRecognitionToggleEvents;
            _suppressRecognitionToggleEvents = true;
            try
            {
                _inputMethodEnabled = enabled;
                _inputMethodToggle.IsChecked = enabled;
                _realtimeRecognitionToggle.IsEnabled = !enabled;
                _shortPhraseRecognitionToggle.ToolTip = enabled
                    ? "识别输入法文字中的经文并加入历史，不启动麦克风"
                    : "识别语音中的经文并加入历史";
                _captionEditor.IsReadOnly = !enabled;
                _captionEditor.ToolTip = enabled
                    ? "支持输入法语音、键盘输入和粘贴；右键清屏或保存导出"
                    : "语音识别结果自动显示；可选中复制，右键清屏或保存导出";
                InputMethod.SetIsInputMethodEnabled(_captionEditor, enabled);
                if (changed) _captionTextAdapter.ResetRecognitionSegment();
                _captionStatusText.Text = enabled
                    ? "输入法语音 / 键盘输入    ·    右键清屏、保存导出    ·    Ctrl+S 保存"
                    : "语音识别自动显示    ·    可选中复制、清屏和导出    ·    Ctrl+S 保存";
                if (!enabled) CancelInputMethodFocus();
            }
            finally { _suppressRecognitionToggleEvents = wasSuppressed; }
            if (enabled) FocusInputMethodEditor();
        }

        public event Action CaptionCleared;

        public void ClearCaptionText()
        {
            if (_isClosing || _isClosed) return;
            _captionTextAdapter.Clear();
            CaptionCleared?.Invoke();
        }

        public void ApplyRecognitionText(LiveCaptionAsrText update) => _captionTextAdapter.ApplyRecognitionText(update);

        public void ResetRecognitionSession()
        {
            if (!_isClosing && !_isClosed) _captionTextAdapter.ResetRecognitionSegment();
        }

        public void FocusInputMethodEditor()
        {
            if (!_inputMethodEnabled || !IsVisible || _isClosing || _isClosed) return;
            Activate();
            RestoreInputMethodFocus();
            QueueInputMethodFocus();
        }

        private void QueueInputMethodFocus()
        {
            if (!_inputMethodEnabled || !IsVisible || _isClosing || _isClosed ||
                _inputMethodFocusOperation?.Status == DispatcherOperationStatus.Pending) return;
            _inputMethodFocusOperation = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _inputMethodFocusOperation = null;
                if (!_inputMethodEnabled || !IsVisible || !IsActive || _isClosing || _isClosed || _captionTextAdapter.IsComposing) return;
                if (_captionEditor.ContextMenu?.IsOpen == true || _historyExportInProgress) return;
                if (Mouse.Captured != null) return;
                if (Keyboard.FocusedElement is DependencyObject focused && Window.GetWindow(focused) != this) return;
                RestoreInputMethodFocus();
            }));
        }

        private void CancelInputMethodFocus()
        {
            _inputMethodFocusOperation?.Abort();
            _inputMethodFocusOperation = null;
        }

        private void CancelOverlayUiOperations()
        {
            CancelInputMethodFocus();
            _layoutApplyOperation?.Abort();
            _layoutApplyOperation = null;
            _layoutApplyScheduled = false;
        }

        private void RestoreInputMethodFocus()
        {
            if (!_isClosing && !_isClosed && !_captionTextAdapter.IsComposing) _captionEditor.Focus();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel) return;
            _isClosing = true;
            CancelOverlayUiOperations();
            _captionTextAdapter.Dispose();
            _floatingBoundsChangedTimer.Stop();
        }

        private void InitializeCaptionEditorMenu()
        {
            _captionEditor.CommandBindings.Add(new CommandBinding(ClearCaptionCommand, (_, _) =>
            {
                ClearCaptionText();
                _captionStatusText.Text = "已清屏，可继续输入；已保存的历史记录保留";
            }, (_, e) => e.CanExecute = CanUseCaptionDocument()));
            _captionEditor.CommandBindings.Add(new CommandBinding(ExportCaptionCommand,
                async (_, _) => await ExportCaptionHistoryAsync(),
                (_, e) => e.CanExecute = CanUseCaptionDocument() && !_historyExportInProgress));
            _captionEditor.InputBindings.Add(new KeyBinding(ExportCaptionCommand, Key.S, ModifierKeys.Control));

            var menu = new ContextMenu();
            menu.SetResourceReference(FrameworkElement.StyleProperty, "CaptionTranscriptMenuStyle");
            // ContextMenu 位于独立 Popup 树，显式共享窗口的局部样式资源。
            menu.Resources.MergedDictionaries.Add(_captionEditorResources);
            menu.Items.Add(CreateCaptionMenuItem("清屏", "CaptionIconEraser", ClearCaptionCommand,
                tooltip: "清空当前文字和字幕输出，开始新的记录；已保存的历史文件保留"));
            menu.Items.Add(CreateCaptionMenuItem("保存导出…", "IconLucideSave", ExportCaptionCommand, "Ctrl+S",
                "导出本次记录的全部字幕，包含已自动保存的早期文字"));
            var open = CreateCaptionMenuItem("打开记录文件夹", "IconLucideFolderOpen");
            open.Click += async (_, _) => await OpenCaptionHistoryDirectoryAsync();
            menu.Items.Add(open);
            var separator = new Separator();
            separator.SetResourceReference(FrameworkElement.StyleProperty, "CaptionTranscriptMenuSeparatorStyle");
            menu.Items.Add(separator);
            foreach (var entry in new[]
            {
                ("剪切", "CaptionIconScissors", ApplicationCommands.Cut, "Ctrl+X"),
                ("复制", "IconLucideCopy2", ApplicationCommands.Copy, "Ctrl+C"),
                ("粘贴", "CaptionIconClipboard", ApplicationCommands.Paste, "Ctrl+V"),
                ("全选", "IconLucideText", ApplicationCommands.SelectAll, "Ctrl+A")
            }) menu.Items.Add(CreateCaptionMenuItem(entry.Item1, entry.Item2, entry.Item3, entry.Item4));
            menu.Opened += (_, _) =>
            {
                CancelInputMethodFocus();
                CommandManager.InvalidateRequerySuggested();
            };
            menu.Closed += (_, _) => QueueInputMethodFocus();
            _captionEditor.ContextMenu = menu;
        }

        private bool CanUseCaptionDocument() => !_isClosing && !_isClosed &&
            !_captionTextAdapter.IsComposing && _captionEditor.Text.Length > 0;

        private MenuItem CreateCaptionMenuItem(string text, string iconKey, ICommand command = null,
            string gesture = "", string tooltip = null)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = TryFindResource(iconKey) as Geometry,
                Width = 16,
                Height = 16,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform
            };
            var item = new MenuItem
            {
                Header = text,
                Icon = icon,
                Command = command,
                CommandTarget = _captionEditor,
                InputGestureText = gesture,
                ToolTip = tooltip
            };
            icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = item });
            item.SetResourceReference(FrameworkElement.StyleProperty, "CaptionTranscriptMenuItemStyle");
            return item;
        }

        private async Task OpenCaptionHistoryDirectoryAsync()
        {
            try
            {
                string directory = _captionTextAdapter.HistoryDirectory;
                await Task.Run(() => Directory.CreateDirectory(directory));
                if (!_isClosing && !_isClosed) Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
            catch (Exception ex) { ShowCaptionHistoryError(ex); }
        }

        private async Task ExportCaptionHistoryAsync()
        {
            if (_historyExportInProgress || _isClosing || _isClosed) return;
            _historyExportInProgress = true;
            CancelInputMethodFocus();
            CommandManager.InvalidateRequerySuggested();
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存导出字幕记录",
                    Filter = "文本文件 (*.txt)|*.txt",
                    FileName = $"字幕记录-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                    DefaultExt = ".txt",
                    AddExtension = true
                };
                if (dialog.ShowDialog(this) == true)
                {
                    _captionStatusText.Text = "正在导出字幕记录…";
                    await _captionTextAdapter.ExportHistoryAsync(dialog.FileName);
                    if (!_isClosing && !_isClosed) _captionStatusText.Text = "字幕记录已保存";
                }
            }
            catch (Exception ex) { ShowCaptionHistoryError(ex); }
            finally
            {
                _historyExportInProgress = false;
                CommandManager.InvalidateRequerySuggested();
                QueueInputMethodFocus();
            }
        }

        private void ShowCaptionHistoryError(Exception ex)
        {
            if (!_isClosing && !_isClosed && IsVisible)
            {
                _captionStatusText.Text = "字幕记录操作未完成，请重试";
                System.Windows.MessageBox.Show(this, $"保存或打开字幕记录失败：{ex.Message}", "字幕记录",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
