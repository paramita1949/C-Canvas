using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiAsrSemanticWindow
    {
        private readonly object _gate = new();
        private readonly Queue<AiAsrTurnEnvelope> _turns = new();
        private readonly int _maxTurnCount;
        private int _version;
        private DateTimeOffset _updatedAt = DateTimeOffset.MinValue;

        public AiAsrSemanticWindow(int maxTurnCount = 8)
        {
            _maxTurnCount = Math.Max(1, maxTurnCount);
        }

        public AiAsrSemanticWindowSnapshot AddTurn(AiAsrTurnEnvelope turn)
        {
            if (turn == null || string.IsNullOrWhiteSpace(turn.Text))
            {
                return CreateSnapshot();
            }

            lock (_gate)
            {
                _turns.Enqueue(turn);
                while (_turns.Count > _maxTurnCount)
                {
                    _turns.Dequeue();
                }

                _version++;
                _updatedAt = turn.CapturedAt == default ? DateTimeOffset.Now : turn.CapturedAt;
                return CreateSnapshotNoLock();
            }
        }

        public AiAsrSemanticWindowSnapshot CreateSnapshot()
        {
            lock (_gate)
            {
                return CreateSnapshotNoLock();
            }
        }

        private AiAsrSemanticWindowSnapshot CreateSnapshotNoLock()
        {
            var turns = _turns.ToList();
            string text = string.Join("\n", turns.Select(t => $"[{t.CapturedAt:HH:mm:ss}] {t.Text}"));
            return new AiAsrSemanticWindowSnapshot(_version, turns, text, _updatedAt);
        }
    }

    public sealed class AiAsrSemanticWindowSnapshot
    {
        public AiAsrSemanticWindowSnapshot(
            int version,
            IReadOnlyList<AiAsrTurnEnvelope> turns,
            string windowText,
            DateTimeOffset updatedAt)
        {
            Version = version;
            Turns = turns ?? Array.Empty<AiAsrTurnEnvelope>();
            WindowText = windowText ?? string.Empty;
            UpdatedAt = updatedAt;
        }

        public int Version { get; }
        public IReadOnlyList<AiAsrTurnEnvelope> Turns { get; }
        public string WindowText { get; }
        public DateTimeOffset UpdatedAt { get; }
    }
}
