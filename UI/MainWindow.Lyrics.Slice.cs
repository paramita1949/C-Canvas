using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Database.Models.Enums;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Lyrics Slice Mode
    /// </summary>
    public partial class MainWindow
    {
        private sealed class LyricsSliceViewItem
        {
            public int Index { get; set; }
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public string Text { get; set; } = "";
            public string Header => $"{Index + 1}. [{StartLine}-{EndLine}]";

            public override string ToString()
            {
                return $"{Header}{Environment.NewLine}{Text}";
            }
        }

        private void UpdateLyricsSliceUiState()
        {
            if (LyricsSlicePanel != null)
            {
                LyricsSlicePanel.Visibility = (_lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (BtnLyricsSliceToggle != null)
            {
                BtnLyricsSliceToggle.Background = _lyricsSliceModeEnabled
                    ? new SolidColorBrush(WpfColor.FromRgb(76, 175, 80))
                    : new SolidColorBrush(WpfColor.FromRgb(44, 44, 44));
            }

            UpdateSliceRuleButtonsVisual();
            UpdateLyricsSliceStateText();
        }

        private void UpdateSliceRuleButtonsVisual()
        {
            var active = new SolidColorBrush(WpfColor.FromRgb(255, 152, 0));
            var inactive = new SolidColorBrush(WpfColor.FromRgb(44, 44, 44));
            var borderActive = new SolidColorBrush(WpfColor.FromRgb(255, 183, 77));
            var borderInactive = new SolidColorBrush(WpfColor.FromRgb(68, 68, 68));

            void Apply(System.Windows.Controls.Button btn, int value)
            {
                if (btn == null)
                {
                    return;
                }
                bool selected = _lyricsSliceLinesPerPage == value;
                btn.Background = selected ? active : inactive;
                btn.BorderBrush = selected ? borderActive : borderInactive;
            }

            Apply(BtnLyricsSliceRule1, 1);
            Apply(BtnLyricsSliceRule2, 2);
            Apply(BtnLyricsSliceRule3, 3);
        }

        private void UpdateLyricsSliceStateText()
        {
            if (LyricsSliceStateText == null)
            {
                return;
            }

            int total = _lyricsSliceItems.Count;
            int current = total == 0 ? 0 : Math.Clamp(_lyricsCurrentSliceIndex + 1, 1, total);
            LyricsSliceStateText.Text = $"{current}/{total}";
        }

        private void SetLyricsSliceModeEnabled(bool enabled)
        {
            if (_lyricsSplitMode != (int)ViewSplitMode.Single && enabled)
            {
                ShowStatus("⚠️ 切片模式仅支持单画面");
                return;
            }

            _lyricsSliceModeEnabled = enabled;
            if (_lyricsSliceModeEnabled)
            {
                GenerateLyricsSlicesFromSingleText(preserveIndex: true);
            }
            else
            {
                if (LyricsTextBox != null)
                {
                    var lines = (LyricsTextBox.Text ?? string.Empty)
                        .Replace("\r\n", "\n")
                        .Replace('\r', '\n')
                        .Split('\n')
                        .Where(line => !string.IsNullOrWhiteSpace(line));
                    LyricsTextBox.Text = string.Join(Environment.NewLine, lines);
                }

                _lyricsSliceItems.Clear();
                _lyricsCurrentSliceIndex = 0;
                if (LyricsSliceList != null)
                {
                    LyricsSliceList.ItemsSource = null;
                }
                UpdateLyricsSliceStateText();
            }

            UpdateLyricsSliceUiState();
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        private void SetLyricsSliceRule(int linesPerPage)
        {
            linesPerPage = Math.Clamp(linesPerPage, 1, 3);
            _lyricsSliceLinesPerPage = linesPerPage;
            UpdateSliceRuleButtonsVisual();

            if (_lyricsSliceModeEnabled)
            {
                GenerateLyricsSlicesFromSingleText(preserveIndex: false);
                ShowStatus($"✅ 已按每{_lyricsSliceLinesPerPage}行切片");
            }
        }

        private void GenerateLyricsSlicesFromSingleText(bool preserveIndex, bool applyMainScreenSpacing = true)
        {
            if (!_lyricsSliceModeEnabled || _lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                return;
            }

            string raw = LyricsTextBox?.Text ?? "";
            var rows = raw.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();

            int desiredIndex = preserveIndex ? _lyricsCurrentSliceIndex : 0;
            _lyricsSliceItems.Clear();

            int group = Math.Max(1, _lyricsSliceLinesPerPage);
            for (int i = 0, idx = 0; i < rows.Count; i += group, idx++)
            {
                var segment = rows.Skip(i).Take(group).ToList();
                _lyricsSliceItems.Add(new LyricsSliceViewItem
                {
                    Index = idx,
                    StartLine = i + 1,
                    EndLine = i + segment.Count,
                    Text = string.Join(Environment.NewLine, segment)
                });
            }

            _lyricsCurrentSliceIndex = _lyricsSliceItems.Count == 0
                ? 0
                : Math.Clamp(desiredIndex, 0, _lyricsSliceItems.Count - 1);

            if (LyricsSliceList != null)
            {
                try
                {
                    _isUpdatingLyricsSliceList = true;
                    LyricsSliceList.ItemsSource = _lyricsSliceItems;
                    LyricsSliceList.SelectedIndex = _lyricsSliceItems.Count == 0 ? -1 : _lyricsCurrentSliceIndex;
                }
                finally
                {
                    _isUpdatingLyricsSliceList = false;
                }
            }

            UpdateLyricsSliceStateText();
            HighlightCurrentSliceInMainEditor();
            if (applyMainScreenSpacing)
            {
                ApplySliceSpacingToMainScreenText();
            }
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        private void GoToLyricsSlice(int index)
        {
            if (!_lyricsSliceModeEnabled || _lyricsSliceItems.Count == 0)
            {
                return;
            }

            _lyricsCurrentSliceIndex = Math.Clamp(index, 0, _lyricsSliceItems.Count - 1);
            if (LyricsSliceList != null)
            {
                try
                {
                    _isUpdatingLyricsSliceList = true;
                    LyricsSliceList.SelectedIndex = _lyricsCurrentSliceIndex;
                    LyricsSliceList.ScrollIntoView(LyricsSliceList.SelectedItem);
                }
                finally
                {
                    _isUpdatingLyricsSliceList = false;
                }
            }
            UpdateLyricsSliceStateText();
            MoveLyricsEditorCaretToSliceStart(_lyricsCurrentSliceIndex);
            HighlightCurrentSliceInMainEditor();

            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        private string GetCurrentSingleLyricsProjectionText()
        {
            if (_lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single && _lyricsSliceItems.Count > 0)
            {
                int index = Math.Clamp(_lyricsCurrentSliceIndex, 0, _lyricsSliceItems.Count - 1);
                return _lyricsSliceItems[index].Text ?? "";
            }

            return LyricsTextBox?.Text ?? "";
        }

        private void BtnLyricsSliceToggle_Click(object sender, RoutedEventArgs e)
        {
            SetLyricsSliceModeEnabled(!_lyricsSliceModeEnabled);
            ShowStatus(_lyricsSliceModeEnabled ? "✅ 已启用切片模式" : "✅ 已关闭切片模式");
        }

        private void BtnLyricsSliceRule1_Click(object sender, RoutedEventArgs e) => SetLyricsSliceRule(1);
        private void BtnLyricsSliceRule2_Click(object sender, RoutedEventArgs e) => SetLyricsSliceRule(2);
        private void BtnLyricsSliceRule3_Click(object sender, RoutedEventArgs e) => SetLyricsSliceRule(3);

        private void BtnLyricsSliceGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                ShowStatus("⚠️ 请先切回单画面再切片");
                return;
            }

            if (!_lyricsSliceModeEnabled)
            {
                SetLyricsSliceModeEnabled(true);
            }
            GenerateLyricsSlicesFromSingleText(preserveIndex: false);
            UpdateLyricsSliceUiState();
            ShowStatus($"✅ 已生成 {_lyricsSliceItems.Count} 个切片");
        }

        private void BtnLyricsSlicePrev_Click(object sender, RoutedEventArgs e)
        {
            GoToLyricsSlice(NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex - 1));
        }

        private void BtnLyricsSliceNext_Click(object sender, RoutedEventArgs e)
        {
            GoToLyricsSlice(NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex + 1));
        }

        private void LyricsSliceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingLyricsSliceList || !_lyricsSliceModeEnabled || LyricsSliceList == null)
            {
                return;
            }

            if (LyricsSliceList.SelectedIndex >= 0 && LyricsSliceList.SelectedIndex < _lyricsSliceItems.Count)
            {
                GoToLyricsSlice(LyricsSliceList.SelectedIndex);
            }
        }

        private void LyricsSliceList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_lyricsSliceModeEnabled || _lyricsSliceItems.Count == 0)
            {
                return;
            }

            int target = e.Delta < 0
                ? NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex + 1)
                : NormalizeSliceIndexForLoop(_lyricsCurrentSliceIndex - 1);
            GoToLyricsSlice(target);
            e.Handled = true;
        }

        private void LyricsTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (sender != LyricsTextBox || _isUpdatingSliceFromCaret || _isApplyingSliceVisualSpacing)
            {
                return;
            }

            SyncSliceSelectionFromEditorCaret();
        }

        private int NormalizeSliceIndexForLoop(int index)
        {
            if (_lyricsSliceItems.Count == 0)
            {
                return 0;
            }

            int total = _lyricsSliceItems.Count;
            int normalized = index % total;
            if (normalized < 0)
            {
                normalized += total;
            }
            return normalized;
        }

        private void SyncSliceSelectionFromEditorCaret()
        {
            if (LyricsTextBox == null || !_lyricsSliceModeEnabled || _lyricsSplitMode != (int)ViewSplitMode.Single || _lyricsSliceItems.Count == 0)
            {
                return;
            }

            int lineIndex;
            try
            {
                lineIndex = LyricsTextBox.GetLineIndexFromCharacterIndex(LyricsTextBox.CaretIndex);
            }
            catch
            {
                return;
            }

            int line = GetContentLineNumberFromDisplayLineIndex(lineIndex);
            if (line <= 0)
            {
                return;
            }
            int target = _lyricsCurrentSliceIndex;
            for (int i = 0; i < _lyricsSliceItems.Count; i++)
            {
                var item = _lyricsSliceItems[i];
                if (line >= item.StartLine && line <= item.EndLine)
                {
                    target = i;
                    break;
                }
            }

            if (target == _lyricsCurrentSliceIndex)
            {
                return;
            }

            try
            {
                _isUpdatingSliceFromCaret = true;
                _lyricsCurrentSliceIndex = target;
                if (LyricsSliceList != null)
                {
                    _isUpdatingLyricsSliceList = true;
                    LyricsSliceList.SelectedIndex = _lyricsCurrentSliceIndex;
                    LyricsSliceList.ScrollIntoView(LyricsSliceList.SelectedItem);
                    _isUpdatingLyricsSliceList = false;
                }

                UpdateLyricsSliceStateText();
                HighlightCurrentSliceInMainEditor();

                if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
                {
                    RenderLyricsToProjection();
                }
            }
            finally
            {
                _isUpdatingLyricsSliceList = false;
                _isUpdatingSliceFromCaret = false;
            }
        }

        private void HighlightCurrentSliceInMainEditor()
        {
            if (LyricsTextBox == null || !_lyricsSliceModeEnabled || _lyricsSplitMode != (int)ViewSplitMode.Single || _lyricsSliceItems.Count == 0)
            {
                return;
            }

            int index = Math.Clamp(_lyricsCurrentSliceIndex, 0, _lyricsSliceItems.Count - 1);
            int startContentLine = Math.Max(1, _lyricsSliceItems[index].StartLine);
            int endContentLine = Math.Max(startContentLine, _lyricsSliceItems[index].EndLine);

            int startLine = GetDisplayLineIndexFromContentLineNumber(startContentLine);
            int endLine = GetDisplayLineIndexFromContentLineNumber(endContentLine);
            if (startLine < 0 || endLine < startLine)
            {
                return;
            }

            int startChar;
            int endChar;
            try
            {
                startChar = LyricsTextBox.GetCharacterIndexFromLineIndex(startLine);
                int nextLine = endLine + 1;
                if (nextLine < LyricsTextBox.LineCount)
                {
                    endChar = LyricsTextBox.GetCharacterIndexFromLineIndex(nextLine);
                }
                else
                {
                    endChar = LyricsTextBox.Text?.Length ?? 0;
                }
            }
            catch
            {
                return;
            }

            int length = Math.Max(0, endChar - startChar);
            if (startChar < 0)
            {
                return;
            }

            LyricsTextBox.SelectionBrush = new SolidColorBrush(WpfColor.FromRgb(255, 152, 0));
            LyricsTextBox.SelectionOpacity = 0.35;
            try
            {
                LyricsTextBox.Focus();
                LyricsTextBox.Select(startChar, length);
            }
            catch
            {
                // 仅跳过本次高亮，避免影响歌词主流程（加载/保存/投影）
            }
        }

        private void MoveLyricsEditorCaretToSliceStart(int sliceIndex)
        {
            if (LyricsTextBox == null || !_lyricsSliceModeEnabled || _lyricsSplitMode != (int)ViewSplitMode.Single || _lyricsSliceItems.Count == 0)
            {
                return;
            }

            int index = Math.Clamp(sliceIndex, 0, _lyricsSliceItems.Count - 1);
            int lineIndex = GetDisplayLineIndexFromContentLineNumber(Math.Max(1, _lyricsSliceItems[index].StartLine));
            if (lineIndex < 0)
            {
                lineIndex = 0;
            }
            try
            {
                LyricsTextBox.GetCharacterIndexFromLineIndex(lineIndex);
            }
            catch
            {
                lineIndex = 0;
            }

            LyricsTextBox.ScrollToLine(lineIndex);
        }

        private void ApplySliceSpacingToMainScreenText()
        {
            if (LyricsTextBox == null || !_lyricsSliceModeEnabled || _lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                return;
            }

            string spaced = string.Join(Environment.NewLine + Environment.NewLine, _lyricsSliceItems.Select(x => x.Text ?? string.Empty));
            if (string.Equals(LyricsTextBox.Text ?? string.Empty, spaced, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                _isApplyingSliceVisualSpacing = true;
                LyricsTextBox.Text = spaced;
            }
            finally
            {
                _isApplyingSliceVisualSpacing = false;
            }
        }

        private int GetDisplayLineIndexFromContentLineNumber(int contentLineNumber)
        {
            if (LyricsTextBox == null || LyricsTextBox.LineCount <= 0 || contentLineNumber <= 0)
            {
                return -1;
            }

            int count = 0;
            for (int i = 0; i < LyricsTextBox.LineCount; i++)
            {
                string line = LyricsTextBox.GetLineText(i);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    count++;
                    if (count == contentLineNumber)
                    {
                        return i;
                    }
                }
            }

            return LyricsTextBox.LineCount - 1;
        }

        private int GetContentLineNumberFromDisplayLineIndex(int displayLineIndex)
        {
            if (LyricsTextBox == null || LyricsTextBox.LineCount <= 0 || displayLineIndex < 0)
            {
                return 0;
            }

            int count = 0;
            int last = Math.Min(displayLineIndex, LyricsTextBox.LineCount - 1);
            for (int i = 0; i <= last; i++)
            {
                string line = LyricsTextBox.GetLineText(i);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
