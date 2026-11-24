using System;
using System.Collections.Generic;

namespace ImageColorChanger.Services.LiveCaption
{
    internal readonly record struct CaptionBibleTextProbe(string Text, bool IsFinal);

    /// <summary>可编辑全文转为局部文字探针。整理前缀平移基线；修订不重放无关旧文。</summary>
    internal sealed class CaptionBibleTextBuffer
    {
        private string _snapshot = string.Empty;
        private string _delivered = string.Empty;
        private DateTimeOffset _changedAt;
        private DateTimeOffset _pendingSince;
        private bool _pending;
        private string _lastRealtimeInterim = string.Empty;
        private string _lastRealtimeFinal = string.Empty;
        private DateTimeOffset _lastRealtimeAttempt;
        private const int ContextLength = 96;
        private const int ProbeLength = 512;

        public void Reset(string baseline = "")
        {
            _snapshot = _delivered = baseline ?? string.Empty;
            _pending = false;
            _lastRealtimeInterim = _lastRealtimeFinal = string.Empty;
            _lastRealtimeAttempt = DateTimeOffset.MinValue;
        }

        public bool Observe(string snapshot, DateTimeOffset now)
        {
            snapshot ??= string.Empty;
            if (string.Equals(snapshot, _snapshot, StringComparison.Ordinal)) return false;
            bool revision = !snapshot.StartsWith(_snapshot, StringComparison.Ordinal);
            _snapshot = snapshot;
            if (snapshot.Length == 0) { _delivered = snapshot; _pending = false; return revision; }
            if (!_pending) _pendingSince = now;
            _pending = true;
            _changedAt = now;
            return revision;
        }

        public IReadOnlyList<CaptionBibleTextProbe> TryFlush(DateTimeOffset now, bool force = false)
        {
            if (!_pending) return Array.Empty<CaptionBibleTextProbe>();
            bool stable = force || now - _changedAt >= TimeSpan.FromMilliseconds(900);
            if (!stable && now - _pendingSince < TimeSpan.FromSeconds(4)) return Array.Empty<CaptionBibleTextProbe>();

            int common = 0;
            while (common < _snapshot.Length && common < _delivered.Length && _snapshot[common] == _delivered[common]) common++;
            int end = _snapshot.Length;
            // 删除本身不产生新增经文；修订仅扫描变化处和它所在的句子。
            if (common < _delivered.Length)
            {
                int suffix = 0;
                while (suffix < _snapshot.Length - common && suffix < _delivered.Length - common &&
                    _snapshot[_snapshot.Length - 1 - suffix] == _delivered[_delivered.Length - 1 - suffix]) suffix++;
                end -= suffix;
                while (end < _snapshot.Length && !IsBoundary(_snapshot[end])) end++;
            }
            int start = common;
            while (start > 0 && common - start < ContextLength && !IsBoundary(_snapshot[start - 1])) start--;
            var probes = new List<CaptionBibleTextProbe>();
            int segmentStart = start;
            for (int i = start; i < end; i++)
            {
                if (!IsBoundary(_snapshot[i]) && i - segmentStart < ProbeLength - 1) continue;
                AddProbe(probes, _snapshot.Substring(segmentStart, i - segmentStart + 1), stable || IsBoundary(_snapshot[i]));
                segmentStart = i + 1;
            }
            if (segmentStart < end) AddProbe(probes, _snapshot.Substring(segmentStart, end - segmentStart), stable);
            _delivered = _snapshot;
            _pending = !stable; // 连续输入的保守探针仍须在停顿时补一次final。
            _pendingSince = now;
            return probes;
        }

        public void Rebase(string removedPrefix)
        {
            if (string.IsNullOrEmpty(removedPrefix)) return;
            if (_snapshot.StartsWith(removedPrefix, StringComparison.Ordinal)) _snapshot = _snapshot[removedPrefix.Length..];
            if (_delivered.StartsWith(removedPrefix, StringComparison.Ordinal)) _delivered = _delivered[removedPrefix.Length..];
        }

        public bool TryCreateRealtimeProbe(string text, bool isFinal, DateTimeOffset now, out CaptionBibleTextProbe probe)
        {
            probe = default;
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) return false;
            if (isFinal)
            {
                if (string.Equals(text, _lastRealtimeFinal, StringComparison.Ordinal)) return false;
                _lastRealtimeFinal = text;
                probe = new CaptionBibleTextProbe(text, true);
                return true; // 同样文字的interim不能阻止final的内容反查。
            }
            string tail = text.Length <= 30 ? text : text[^30..];
            if (tail.Length < 6 || tail == _lastRealtimeInterim || now - _lastRealtimeAttempt < TimeSpan.FromSeconds(3)) return false;
            _lastRealtimeInterim = tail;
            _lastRealtimeAttempt = now;
            probe = new CaptionBibleTextProbe(tail, false);
            return true;
        }

        private static bool IsBoundary(char c) => c is '。' or '！' or '？' or ';' or '；' or '\n' or '\r' or '!' or '?';
        private static void AddProbe(List<CaptionBibleTextProbe> probes, string text, bool isFinal)
        {
            text = text.Trim();
            if (!string.IsNullOrWhiteSpace(text)) probes.Add(new CaptionBibleTextProbe(text, isFinal));
        }
    }
}
