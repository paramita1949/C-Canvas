using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TextBox = System.Windows.Controls.TextBox;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        // IME 的预编辑内容不是已提交歌词；不能用于自动保存、投影或切片定位。
        private readonly HashSet<TextBox> _lyricsComposingEditors = new();
        private int _lyricsInputEpoch;

        private bool IsLyricsImeCompositionActive() => _lyricsComposingEditors?.Count > 0;

        private void LyricsEditor_CompositionStarted(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox editor)
            {
                _lyricsComposingEditors.Add(editor);
                _lyricsTypingSaveTimer?.Stop();
            }
            // 不设置 Handled：让 WPF / TSF 继续处理拼音、候选和提交。
        }

        private void LyricsEditor_CompositionCompleted(object sender, TextCompositionEventArgs e)
        {
            FinishLyricsComposition(sender as TextBox);
        }

        private void LyricsEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            FinishLyricsComposition(sender as TextBox);
        }

        private void FinishLyricsComposition(TextBox editor)
        {
            if (editor == null || !_lyricsComposingEditors.Remove(editor)) return;
            int epoch = _lyricsInputEpoch;
            // PreviewTextInput 先于原生文本容器的最终写入，后台读取最终提交结果。
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (epoch != _lyricsInputEpoch || _isLoadingLyricsProject || IsLyricsImeCompositionActive()) return;
                LyricsTextBox_TextChanged(editor, null);
            }), DispatcherPriority.Background);
        }
    }
}
