using System;

namespace ImageColorChanger.Services.Ai
{
    /// <summary>把输入法的可编辑全文合并为有节制的新增文字或修订，避免布局刷新重复调用AI。</summary>
    public sealed class AiInputMethodTurnAggregator
    {
        private string _snapshot = string.Empty;
        private string _delivered = string.Empty;
        private DateTimeOffset _changedAt;
        private DateTimeOffset _pendingSince;
        private DateTimeOffset _lastSentAt = DateTimeOffset.MinValue;
        private bool _pending;

        public void Reset(string baseline = "")
        {
            _snapshot = _delivered = baseline ?? string.Empty;
            _pending = false;
            _lastSentAt = DateTimeOffset.MinValue;
        }

        public void Observe(string snapshot, DateTimeOffset now)
        {
            snapshot ??= string.Empty;
            if (string.Equals(_snapshot, snapshot, StringComparison.Ordinal)) return;
            _snapshot = snapshot;
            if (string.IsNullOrWhiteSpace(snapshot))
            {
                _delivered = snapshot;
                _pending = false;
                return;
            }
            if (string.Equals(_delivered, snapshot, StringComparison.Ordinal))
            {
                _pending = false;
                return;
            }
            if (!_pending) _pendingSince = now;
            _pending = true;
            _changedAt = now;
        }

        public bool TryFlush(DateTimeOffset now, out AiAsrTurnEnvelope turn)
        {
            turn = null;
            if (!_pending || now - _lastSentAt < TimeSpan.FromSeconds(4)) return false;
            if (now - _changedAt < TimeSpan.FromSeconds(1) && now - _pendingSince < TimeSpan.FromSeconds(4)) return false;

            bool append = _snapshot.StartsWith(_delivered, StringComparison.Ordinal);
            string text = (append ? _snapshot[_delivered.Length..] : _snapshot).Trim();
            _delivered = _snapshot;
            _pending = false;
            if (string.IsNullOrWhiteSpace(text)) return false;

            _lastSentAt = now;
            turn = new AiAsrTurnEnvelope
            {
                TurnId = "ime-" + Guid.NewGuid().ToString("N"),
                Text = append ? text : "【输入法修订：以下为修订后的原文，请以此替换先前相关内容】\n" + text,
                CapturedAt = now,
                IsFinal = true,
                Source = append ? "input-method" : "input-method-correction"
            };
            return true;
        }
    }
}
