using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageColorChanger.Services.LiveCaption
{
    internal readonly struct LiveCaptionRenderFrame
    {
        public LiveCaptionRenderFrame(string display, int highlightStart, bool hasChanged)
        {
            Display = display ?? string.Empty;
            HighlightStart = CaptionTextElements.FloorBoundary(Display, highlightStart);
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
        private int _lineCharLimit;
        private readonly int _maxSourceChars;

        private string _draft = string.Empty;
        private string _lastFinal = string.Empty;

        public LiveCaptionDisplayComposer(
            int lineCharLimit = 34,
            int displayLineLimit = 2,
            int maxCommittedHistory = 96,
            int maxSourceChars = 320)
        {
            _lineCharLimit = Math.Max(1, lineCharLimit);
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

        public void SetLineCharLimit(int lineCharLimit)
        {
            _lineCharLimit = Math.Max(1, lineCharLimit);
        }

        public LiveCaptionRenderFrame PushInputSnapshot(string text)
            => PushInputSnapshot(text, 0);

        public LiveCaptionRenderFrame PushInputSnapshot(string text, long leadingTextElements)
        {
            // 已发布的工作区快照保留原始行进度；移出旧文不能让剩余字幕重新切行。
            string nextDisplay = BuildDisplayText(text, preserveLineBreaks: true, leadingTextElements);
            if (string.Equals(nextDisplay, CurrentDisplay, StringComparison.Ordinal))
            {
                return new LiveCaptionRenderFrame(CurrentDisplay, CurrentHighlightStart, hasChanged: false);
            }

            int nextHighlight = ComputeHighlightStart(CurrentDisplay, nextDisplay);
            CurrentDisplay = nextDisplay;
            CurrentHighlightStart = nextHighlight;
            return new LiveCaptionRenderFrame(CurrentDisplay, CurrentHighlightStart, hasChanged: true);
        }

        public LiveCaptionRenderFrame Push(in LiveCaptionAsrText update)
        {
            string normalized = CaptionRecognitionTextPolicy.Normalize(update.Text);
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
            if (CaptionRecognitionTextPolicy.ShouldRotateDraftToNextUtterance(_draft, normalized))
            {
                // 某些实时接口只回中间结果且会“从新句起点重报”，这里把旧草稿滚入历史，避免界面卡住不更新。
                EnqueueCommitted(_draft);
                _lastFinal = _draft;
                _draft = normalized;
                return true;
            }

            string merged = CaptionRecognitionTextPolicy.MergeDraftStable(_draft, normalized);
            if (string.Equals(merged, _draft, StringComparison.Ordinal))
            {
                return false;
            }

            _draft = merged;
            return true;
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

            return BuildDisplayText(source);
        }

        private string BuildDisplayText(string source, bool preserveLineBreaks = false, long leadingTextElements = 0)
        {
            source = preserveLineBreaks ? source ?? string.Empty : CaptionRecognitionTextPolicy.Normalize(source);
            if (source.Length == 0)
            {
                return string.Empty;
            }

            if (!preserveLineBreaks && source.Length > _maxSourceChars)
            {
                // 仅按“整行”裁剪，避免每来一个字就把开头挤掉造成横移感。
                int[] starts = System.Globalization.StringInfo.ParseCombiningCharacters(source);
                int overflow = starts.Length - _maxSourceChars;
                int trimStep = Math.Max(1, _lineCharLimit);
                int trim = (overflow / trimStep) * trimStep;
                if (trim > 0)
                {
                    LiveCaptionDebugLogger.Log(
                        $"[ComposerTrim] overflow={overflow}, trim={trim}, step={trimStep}, sourceLen={source.Length}, max={_maxSourceChars}");
                    source = source.Substring(starts[trim]);
                }
            }

            return RenderWindow(source, leadingTextElements);
        }

        private string RenderWindow(string source, long leadingTextElements = 0)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            return CaptionTextElements.TakeLastWrappedLines(source, _lineCharLimit, _displayLineLimit, leadingTextElements);
        }

        private static int ComputeHighlightStart(string previous, string current)
        {
            if (string.IsNullOrEmpty(current))
            {
                return 0;
            }

            if (string.IsNullOrEmpty(previous))
            {
                // 首帧不做“最新字高亮”，避免整段突变为高亮色。
                return current.Length;
            }

            if (current.StartsWith(previous, StringComparison.Ordinal))
            {
                return CaptionTextElements.FloorBoundary(current, previous.Length);
            }

            int overlap = CaptionRecognitionTextPolicy.FindOverlap(previous, current);
            if (overlap >= 4 && overlap < current.Length)
            {
                return CaptionTextElements.FloorBoundary(current, overlap);
            }

            // 无法稳定识别新增片段时，不高亮，避免整段颜色跳变。
            return current.Length;
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
