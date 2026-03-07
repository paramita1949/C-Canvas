using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models.Enums;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Lyrics Slice Mode
    /// </summary>
    public partial class MainWindow
    {
        private const int MinLyricsSliceLinesPerPage = 1;
        private const int MaxLyricsSliceLinesPerPage = 4;

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
            bool singleMode = _lyricsSplitMode == (int)ViewSplitMode.Single;
            if (singleMode)
            {
                _lyricsSliceModeEnabled = true;
            }
            else
            {
                _lyricsSliceModeEnabled = false;
            }

            if (LyricsSliceToolbar != null)
            {
                LyricsSliceToolbar.Visibility = singleMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (LyricsSlicePanel != null)
            {
                LyricsSlicePanel.Visibility = singleMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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

            if (BtnLyricsSliceRuleDefault != null)
            {
                bool defaultSelected = _lyricsSliceLinesPerPage <= 0;
                BtnLyricsSliceRuleDefault.Background = defaultSelected ? active : inactive;
                BtnLyricsSliceRuleDefault.BorderBrush = defaultSelected ? borderActive : borderInactive;
            }

            void Apply(System.Windows.Controls.Button btn, int value)
            {
                if (btn == null)
                {
                    return;
                }
                bool selected = _lyricsSliceLinesPerPage > 0 && _lyricsSliceLinesPerPage == value;
                btn.Background = selected ? active : inactive;
                btn.BorderBrush = selected ? borderActive : borderInactive;
            }

            Apply(BtnLyricsSliceRule1, 1);
            Apply(BtnLyricsSliceRule2, 2);
            Apply(BtnLyricsSliceRule3, 3);
            Apply(BtnLyricsSliceRule4, 4);

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
                ShowStatus("切片模式仅支持单画面");
                return;
            }

            _lyricsSliceModeEnabled = enabled;
            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                _lyricsSliceModeEnabled = true;
            }
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

        private void SetLyricsSliceRule(int linesPerPage, bool fromCustom = false)
        {
            linesPerPage = Math.Clamp(linesPerPage, MinLyricsSliceLinesPerPage, MaxLyricsSliceLinesPerPage);
            if (_lyricsSliceLinesPerPage <= 0 && linesPerPage > 0 && LyricsTextBox != null)
            {
                _lyricsSingleRawText = LyricsTextBox.Text ?? string.Empty;
                _lyricsSingleRawTextInitialized = true;
            }

            _lyricsSliceLinesPerPage = linesPerPage;
            _lyricsSliceRuleFromCustom = fromCustom;
            UpdateSliceRuleButtonsVisual();

            if (_lyricsSplitMode == (int)ViewSplitMode.Single)
            {
                GenerateLyricsSlicesFromSingleText(preserveIndex: false);
                ShowStatus($"已按每{_lyricsSliceLinesPerPage}行切片（忽略空白行）");
            }
        }

        private void GenerateLyricsSlicesFromSingleText(bool preserveIndex, bool applyMainScreenSpacing = true)
        {
            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                return;
            }

            string editorText = LyricsTextBox?.Text ?? string.Empty;
            string raw = (_lyricsSliceLinesPerPage > 0 && _lyricsSingleRawTextInitialized)
                ? (_lyricsSingleRawText ?? string.Empty)
                : editorText;
            var rows = LyricsSlicePlanner.NormalizeContentLines(raw);
            int desiredIndex = preserveIndex ? _lyricsCurrentSliceIndex : 0;
            _lyricsSliceItems.Clear();

            IReadOnlyList<LyricsSlicePlanner.Segment> segments;
            if (_lyricsSliceLinesPerPage <= 0)
            {
                segments = BuildSegmentsByBlankLines(raw);
                int blankRows = (raw ?? string.Empty)
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Count(line => line.Length == 0);
                Debug.WriteLine($"[LyricsSlice][Generate] mode=default(blank-line), rows={rows.Count}, blankRows={blankRows}, segments={segments.Count}");
            }
            else
            {
                segments = LyricsSlicePlanner.BuildSegments(
                    rows,
                    linesPerSlice: _lyricsSliceLinesPerPage,
                    useFreeCutPoints: false,
                    cutPoints: Array.Empty<int>());
                Debug.WriteLine($"[LyricsSlice][Generate] mode=fixed({_lyricsSliceLinesPerPage}), rows={rows.Count}, segments={segments.Count}");
            }

            for (int idx = 0; idx < segments.Count; idx++)
            {
                var segment = segments[idx];
                _lyricsSliceItems.Add(new LyricsSliceViewItem
                {
                    Index = idx,
                    StartLine = segment.StartLine,
                    EndLine = segment.EndLine,
                    Text = segment.Text
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
            if (applyMainScreenSpacing && _lyricsSliceLinesPerPage > 0)
            {
                // 仅 1/2/3/4 规则下应用主界面切片可视化。
                ApplySliceSpacingToMainScreenText();
            }
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        private static IReadOnlyList<LyricsSlicePlanner.Segment> BuildSegmentsByBlankLines(string raw)
        {
            var segments = new List<LyricsSlicePlanner.Segment>();
            string[] lines = (raw ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            var current = new List<string>();
            int contentLine = 0;
            int segmentStart = 0;

            void FlushCurrent()
            {
                if (current.Count == 0)
                {
                    return;
                }

                segments.Add(new LyricsSlicePlanner.Segment
                {
                    StartLine = segmentStart,
                    EndLine = contentLine,
                    Text = string.Join(Environment.NewLine, current)
                });
                current.Clear();
                segmentStart = 0;
            }

            foreach (string line in lines)
            {
                // 默认模式：仅“真正空白行”作为分割点。
                if (line.Length == 0)
                {
                    FlushCurrent();
                    continue;
                }

                string normalized = LyricsSlicePlanner.StripDisplayPrefix(line);
                if (normalized.Length == 0)
                {
                    // 非空行但清洗后为空时，忽略，不作为分割点。
                    continue;
                }

                contentLine++;
                if (segmentStart == 0)
                {
                    segmentStart = contentLine;
                }
                current.Add(normalized);
            }

            FlushCurrent();
            return segments;
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
            return LyricsSlicePlanner.StripDisplayPrefix(line);
        }

        private void BtnLyricsSliceRule1_Click(object sender, RoutedEventArgs e) => SetLyricsSliceRule(1, fromCustom: false);
        private void BtnLyricsSliceRule2_Click(object sender, RoutedEventArgs e) => SetLyricsSliceRule(2, fromCustom: false);
        private void BtnLyricsSliceRule3_Click(object sender, RoutedEventArgs e) => SetLyricsSliceRule(3, fromCustom: false);
        private void BtnLyricsSliceRule4_Click(object sender, RoutedEventArgs e) => SetLyricsSliceRule(4, fromCustom: false);
        private void BtnLyricsSliceRuleDefault_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[LyricsSlice][UI] click=default");
            _ = sender;
            _ = e;
            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                SetLyricsSplitMode(ViewSplitMode.Single, keepTextBridge: true);
            }

            _lyricsSliceModeEnabled = true;
            _lyricsSliceLinesPerPage = 0;
            if (!_lyricsSingleRawTextInitialized)
            {
                _lyricsSingleRawText = BuildPlainLyricsTextFromSliceItems();
                _lyricsSingleRawTextInitialized = true;
            }

            if (LyricsTextBox != null)
            {
                string restore = _lyricsSingleRawText ?? string.Empty;
                if (!string.Equals(LyricsTextBox.Text ?? string.Empty, restore, StringComparison.Ordinal))
                {
                    try
                    {
                        _isApplyingSliceVisualSpacing = true;
                        LyricsTextBox.Text = restore;
                    }
                    finally
                    {
                        _isApplyingSliceVisualSpacing = false;
                    }
                }
            }
            GenerateLyricsSlicesFromSingleText(preserveIndex: true, applyMainScreenSpacing: false);
            UpdateLyricsSliceUiState();
            ShowStatus("已切回默认单画面（空白行为切片点）");
        }

        private void BtnLyricsSliceRuleDefault_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            BtnLyricsSliceRuleDefault_Click(sender, new RoutedEventArgs());
        }

        private void BtnLyricsSliceGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (_lyricsSplitMode != (int)ViewSplitMode.Single)
            {
                ShowStatus("请先切回单画面再切片");
                return;
            }

            if (!_lyricsSliceModeEnabled)
            {
                SetLyricsSliceModeEnabled(true);
            }
            GenerateLyricsSlicesFromSingleText(preserveIndex: false);
            UpdateLyricsSliceUiState();
            ShowStatus($"已生成 {_lyricsSliceItems.Count} 个切片");
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

                bool isLastLine = line.BreakEnd >= text.Length;
                if (clampedCaret < line.BreakEnd || (isLastLine && clampedCaret == text.Length))
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


