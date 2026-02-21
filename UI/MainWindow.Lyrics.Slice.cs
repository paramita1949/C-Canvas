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

        private readonly struct LyricsLogicalLine
        {
            public LyricsLogicalLine(int start, int contentEnd, int breakEnd, bool isContent)
            {
                Start = start;
                ContentEnd = contentEnd;
                BreakEnd = breakEnd;
                IsContent = isContent;
            }

            public int Start { get; }
            public int ContentEnd { get; }
            public int BreakEnd { get; }
            public bool IsContent { get; }
        }

        private void UpdateLyricsSliceUiState()
        {
            if (LyricsSliceToolbar != null)
            {
                LyricsSliceToolbar.Visibility = _lyricsSplitMode == (int)ViewSplitMode.Single
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

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
                    // 退出切片时恢复纯歌词（不带编号与视觉空行）
                    string plain = BuildPlainLyricsTextFromSliceItems();
                    if (string.IsNullOrWhiteSpace(plain))
                    {
                        var lines = (LyricsTextBox.Text ?? string.Empty)
                            .Replace("\r\n", "\n")
                            .Replace('\r', '\n')
                            .Split('\n')
                            .Where(line => !string.IsNullOrWhiteSpace(line));
                        plain = string.Join(Environment.NewLine, lines);
                    }

                    LyricsTextBox.Text = plain;
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
                .Select(line => RemoveSliceNumberPrefix(line.Trim()))
                .Where(line => !string.IsNullOrWhiteSpace(line))
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
            try
            {
                _isUpdatingSliceFromCaret = true;
                MoveLyricsEditorCaretToSliceStart(_lyricsCurrentSliceIndex);
            }
            finally
            {
                _isUpdatingSliceFromCaret = false;
            }

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
                return StripSliceVisualNumbering(_lyricsSliceItems[index].Text ?? "");
            }

            string text = LyricsTextBox?.Text ?? "";
            if (_lyricsSliceModeEnabled && _lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                return StripSliceVisualNumbering(text);
            }

            return text;
        }

        private static string StripSliceVisualNumbering(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var normalized = lines
                .Select(line => line?.Trim() ?? string.Empty)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(RemoveSliceNumberPrefix);

            return string.Join(Environment.NewLine, normalized);
        }

        private static string RemoveSliceNumberPrefix(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            int i = 0;
            while (i < line.Length && char.IsDigit(line[i]))
            {
                i++;
            }

            if (i <= 0 || i >= line.Length || line[i] != '.')
            {
                return line;
            }

            int next = i + 1;
            while (next < line.Length && char.IsWhiteSpace(line[next]))
            {
                next++;
            }

            return next < line.Length ? line.Substring(next) : string.Empty;
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

            int line = GetContentLineNumberFromCaretIndex(LyricsTextBox.CaretIndex);
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
            if (LyricsTextBox == null)
            {
                return;
            }

            // 取消 TextBox 选区高亮方案，避免触发 Selection/文本容器重入问题。
            // 当前仅保留右侧切片列表高亮与切片定位能力。
            try
            {
                if (LyricsTextBox.SelectionLength > 0)
                {
                    LyricsTextBox.SelectionLength = 0;
                }
            }
            catch
            {
                // 安全兜底：不影响切片主流程
            }
        }

        private void MoveLyricsEditorCaretToSliceStart(int sliceIndex)
        {
            if (LyricsTextBox == null || !_lyricsSliceModeEnabled || _lyricsSplitMode != (int)ViewSplitMode.Single || _lyricsSliceItems.Count == 0)
            {
                return;
            }

            int index = Math.Clamp(sliceIndex, 0, _lyricsSliceItems.Count - 1);
            int targetContentLine = Math.Max(1, _lyricsSliceItems[index].StartLine);
            if (!TryGetCharRangeForContentLines(targetContentLine, targetContentLine, out int startChar, out _))
            {
                return;
            }

            try
            {
                LyricsTextBox.Focus();
                LyricsTextBox.CaretIndex = Math.Clamp(startChar, 0, (LyricsTextBox.Text ?? string.Empty).Length);
                LyricsTextBox.SelectionLength = 0;
            }
            catch
            {
                // 忽略一次失败，避免影响切片切换主流程
            }
        }

        private void ApplySliceSpacingToMainScreenText()
        {
            if (LyricsTextBox == null || !_lyricsSliceModeEnabled || _lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                return;
            }

            // 主屏显示编号，仅用于定位；投影与持久化在其他流程中会清洗掉编号前缀。
            string spaced = string.Join(
                Environment.NewLine + Environment.NewLine,
                _lyricsSliceItems.Select((x, i) =>
                {
                    string body = x.Text ?? string.Empty;
                    return $"{i + 1}. {body}";
                }));
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

        internal string BuildPlainLyricsTextFromSliceItems()
        {
            if (_lyricsSliceItems.Count == 0)
            {
                return string.Empty;
            }

            var lines = _lyricsSliceItems
                .SelectMany(item => (item.Text ?? string.Empty)
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n'))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim());

            return string.Join(Environment.NewLine, lines);
        }

        private bool TryGetCharRangeForContentLines(int startContentLine, int endContentLine, out int startChar, out int endChar)
        {
            startChar = -1;
            endChar = -1;
            if (LyricsTextBox == null || startContentLine <= 0 || endContentLine < startContentLine)
            {
                return false;
            }

            string text = LyricsTextBox.Text ?? string.Empty;
            var lines = GetLyricsLogicalLines(text);
            int count = 0;
            foreach (var line in lines)
            {
                if (!line.IsContent)
                {
                    continue;
                }

                count++;
                if (count == startContentLine)
                {
                    startChar = line.Start;
                }

                if (count == endContentLine)
                {
                    endChar = line.ContentEnd;
                    break;
                }
            }

            if (startChar < 0)
            {
                return false;
            }

            if (endChar < startChar)
            {
                endChar = startChar;
            }

            return true;
        }

        private int GetContentLineNumberFromCaretIndex(int caretIndex)
        {
            if (LyricsTextBox == null)
            {
                return 0;
            }

            string text = LyricsTextBox.Text ?? string.Empty;
            int clampedCaret = Math.Clamp(caretIndex, 0, text.Length);
            var lines = GetLyricsLogicalLines(text);
            int contentLineCount = 0;
            foreach (var line in lines)
            {
                if (line.IsContent)
                {
                    contentLineCount++;
                }

                if (clampedCaret <= line.BreakEnd)
                {
                    return contentLineCount;
                }
            }

            return contentLineCount;
        }

        private static List<LyricsLogicalLine> GetLyricsLogicalLines(string text)
        {
            var result = new List<LyricsLogicalLine>();
            string source = text ?? string.Empty;
            if (source.Length == 0)
            {
                result.Add(new LyricsLogicalLine(0, 0, 0, false));
                return result;
            }

            int i = 0;
            while (i < source.Length)
            {
                int start = i;
                while (i < source.Length && source[i] != '\r' && source[i] != '\n')
                {
                    i++;
                }

                int contentEnd = i;
                bool isContent = IsNonWhitespaceSegment(source, start, contentEnd);

                if (i < source.Length)
                {
                    if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
                    {
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }

                int breakEnd = i;
                result.Add(new LyricsLogicalLine(start, contentEnd, breakEnd, isContent));
            }

            if (source.Length > 0)
            {
                char last = source[source.Length - 1];
                if (last == '\r' || last == '\n')
                {
                    result.Add(new LyricsLogicalLine(source.Length, source.Length, source.Length, false));
                }
            }

            return result;
        }

        private static bool IsNonWhitespaceSegment(string text, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
