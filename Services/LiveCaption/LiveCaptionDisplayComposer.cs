using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ImageColorChanger.Services.LiveCaption
{
    internal readonly struct LiveCaptionRenderFrame
    {
        public LiveCaptionRenderFrame(string display, int highlightStart, bool hasChanged)
        {
            Display = display ?? string.Empty;
            HighlightStart = Math.Clamp(highlightStart, 0, Display.Length);
            HasChanged = hasChanged;
        }

        public string Display { get; }

        public int HighlightStart { get; }

        public bool HasChanged { get; }
    }

    internal sealed class LiveCaptionDisplayComposer
    {
        private readonly Queue<string> _committed = new();
        private readonly int _maxCommittedHistory;
        private readonly int _displayLineLimit;
        private readonly int _lineCharLimit;
        private readonly int _maxSourceChars;

        private string _draft = string.Empty;
        private string _lastFinal = string.Empty;

        public LiveCaptionDisplayComposer(
            int lineCharLimit = 34,
            int displayLineLimit = 2,
            int maxCommittedHistory = 96,
            int maxSourceChars = 320)
        {
            _lineCharLimit = Math.Max(8, lineCharLimit);
            _displayLineLimit = Math.Max(1, displayLineLimit);
            _maxCommittedHistory = Math.Max(16, maxCommittedHistory);
            _maxSourceChars = Math.Max(64, maxSourceChars);
        }

        public string CurrentDisplay { get; private set; } = string.Empty;

        public int CurrentHighlightStart { get; private set; }

        public void Reset()
        {
            _committed.Clear();
            _draft = string.Empty;
            _lastFinal = string.Empty;
            CurrentDisplay = string.Empty;
            CurrentHighlightStart = 0;
            LiveCaptionDebugLogger.Log("Composer: reset.");
        }

        public LiveCaptionRenderFrame Push(in LiveCaptionAsrText update)
        {
            string normalized = Normalize(update.Text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new LiveCaptionRenderFrame(CurrentDisplay, CurrentHighlightStart, hasChanged: false);
            }

            bool accepted = update.IsFinal
                ? AcceptFinal(normalized)
                : AcceptInterim(normalized);

            if (!accepted)
            {
                return new LiveCaptionRenderFrame(CurrentDisplay, CurrentHighlightStart, hasChanged: false);
            }

            string nextDisplay = BuildDisplayText();
            if (string.Equals(nextDisplay, CurrentDisplay, StringComparison.Ordinal))
            {
                return new LiveCaptionRenderFrame(CurrentDisplay, CurrentHighlightStart, hasChanged: false);
            }

            int nextHighlight = ComputeHighlightStart(CurrentDisplay, nextDisplay);
            CurrentDisplay = nextDisplay;
            CurrentHighlightStart = nextHighlight;
            LiveCaptionDebugLogger.Log(
                $"Composer: final={update.IsFinal}, draft='{TrimForLog(_draft)}', committed={_committed.Count}, display='{TrimForLog(CurrentDisplay)}'");
            return new LiveCaptionRenderFrame(CurrentDisplay, CurrentHighlightStart, hasChanged: true);
        }

        private bool AcceptFinal(string normalized)
        {
            if (string.Equals(normalized, _lastFinal, StringComparison.Ordinal))
            {
                return false;
            }

            _draft = string.Empty;
            _lastFinal = normalized;
            EnqueueCommitted(normalized);
            return true;
        }

        private bool AcceptInterim(string normalized)
        {
            if (ShouldRotateDraftToNextUtterance(_draft, normalized))
            {
                // 某些实时接口只回中间结果且会“从新句起点重报”，这里把旧草稿滚入历史，避免界面卡住不更新。
                EnqueueCommitted(_draft);
                _lastFinal = _draft;
                _draft = normalized;
                return true;
            }

            string merged = MergeDraftStable(_draft, normalized);
            if (string.Equals(merged, _draft, StringComparison.Ordinal))
            {
                return false;
            }

            _draft = merged;
            return true;
        }

        private static bool ShouldRotateDraftToNextUtterance(string currentDraft, string incoming)
        {
            if (string.IsNullOrWhiteSpace(currentDraft) || string.IsNullOrWhiteSpace(incoming))
            {
                return false;
            }

            if (incoming.StartsWith(currentDraft, StringComparison.Ordinal) ||
                currentDraft.StartsWith(incoming, StringComparison.Ordinal))
            {
                return false;
            }

            int overlap = FindOverlap(currentDraft, incoming);
            if (overlap >= 3)
            {
                return false;
            }

            // 新片段明显更短，且与旧草稿无前缀/后缀关系，可判定为“下一句话开始”。
            if (incoming.Length + 3 < currentDraft.Length)
            {
                return true;
            }

            // 极短片段重启（如“在宁波”“他”）也视为新句启动，避免显示冻结。
            return incoming.Length <= 8 && currentDraft.Length >= 20;
        }

        private void EnqueueCommitted(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (_committed.Count > 0 && string.Equals(_committed.Last(), text, StringComparison.Ordinal))
            {
                return;
            }

            _committed.Enqueue(text);
            while (_committed.Count > _maxCommittedHistory)
            {
                _committed.Dequeue();
            }
        }

        private string BuildDisplayText()
        {
            string source = string.Concat(_committed);
            if (!string.IsNullOrWhiteSpace(_draft))
            {
                source += _draft;
            }

            source = Normalize(source);
            if (source.Length == 0)
            {
                return string.Empty;
            }

            if (source.Length > _maxSourceChars)
            {
                source = source.Substring(source.Length - _maxSourceChars);
            }

            return RenderWindow(source);
        }

        private string RenderWindow(string source)
        {
            var lines = new List<string>(_displayLineLimit) { string.Empty };

            foreach (char rawChar in source)
            {
                if (rawChar == '\r' || rawChar == '\n')
                {
                    continue;
                }

                int tailIndex = lines.Count - 1;
                string tail = lines[tailIndex];
                if (tail.Length >= _lineCharLimit)
                {
                    lines.Add(string.Empty);
                    if (lines.Count > _displayLineLimit)
                    {
                        lines.RemoveAt(0);
                    }

                    tailIndex = lines.Count - 1;
                    tail = lines[tailIndex];
                }

                char next = rawChar;
                if (char.IsWhiteSpace(next))
                {
                    if (tail.Length == 0 || tail[^1] == ' ')
                    {
                        continue;
                    }

                    next = ' ';
                }

                lines[tailIndex] = tail + next;
            }

            return string.Join(
                Environment.NewLine,
                lines.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        }

        private static int ComputeHighlightStart(string previous, string current)
        {
            if (string.IsNullOrEmpty(current))
            {
                return 0;
            }

            if (string.IsNullOrEmpty(previous))
            {
                return current.Length;
            }

            if (current.StartsWith(previous, StringComparison.Ordinal))
            {
                return previous.Length;
            }

            int overlap = FindOverlap(previous, current);
            if (overlap >= 4 && overlap < current.Length)
            {
                return overlap;
            }

            return current.Length;
        }

        private static string MergeDraftStable(string currentDraft, string incoming)
        {
            if (string.IsNullOrWhiteSpace(incoming))
            {
                return currentDraft;
            }

            if (string.IsNullOrWhiteSpace(currentDraft))
            {
                return incoming;
            }

            if (incoming.StartsWith(currentDraft, StringComparison.Ordinal))
            {
                return incoming;
            }

            if (currentDraft.StartsWith(incoming, StringComparison.Ordinal))
            {
                // ASR中间结果经常回缩，直接保持较长内容，减少闪烁。
                return currentDraft;
            }

            int overlap = FindOverlap(currentDraft, incoming);
            if (overlap >= 3 && overlap < incoming.Length)
            {
                return currentDraft + incoming.Substring(overlap);
            }

            // 尽量保留已有草稿，只在明显新增时替换，避免一句话来回重写。
            if (incoming.Length + 3 < currentDraft.Length)
            {
                return currentDraft;
            }

            return incoming;
        }

        private static int FindOverlap(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return 0;
            }

            int max = Math.Min(left.Length, right.Length);
            for (int len = max; len >= 1; len--)
            {
                if (string.Compare(left, left.Length - len, right, 0, len, StringComparison.Ordinal) == 0)
                {
                    return len;
                }
            }

            return 0;
        }

        private static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(input.Length);
            bool previousSpace = false;
            foreach (char raw in input)
            {
                char c = raw;
                if (c == '\r' || c == '\n' || char.IsWhiteSpace(c))
                {
                    if (previousSpace)
                    {
                        continue;
                    }

                    sb.Append(' ');
                    previousSpace = true;
                    continue;
                }

                previousSpace = false;
                sb.Append(c);
            }

            return sb.ToString().Trim();
        }

        private static string TrimForLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Length <= 120 ? text : text.Substring(0, 120) + "...";
        }
    }
}
