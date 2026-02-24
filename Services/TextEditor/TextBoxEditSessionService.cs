using System;
using System.Collections.Generic;

namespace ImageColorChanger.Services.TextEditor
{
    public sealed class TextBoxEditSessionService : ITextBoxEditSessionService
    {
        private readonly object _sync = new object();
        private readonly Dictionary<int, TextBoxEditSessionState> _states = new Dictionary<int, TextBoxEditSessionState>();

        public TextBoxEditSessionState GetState(int textElementId)
        {
            lock (_sync)
            {
                if (_states.TryGetValue(textElementId, out var state))
                {
                    return state;
                }

                return TextBoxEditSessionState.Idle;
            }
        }

        public void SetSelected(int textElementId, bool isSelected)
        {
            lock (_sync)
            {
                var current = GetStateInternal(textElementId);
                if (isSelected)
                {
                    if (current == TextBoxEditSessionState.Idle)
                    {
                        _states[textElementId] = TextBoxEditSessionState.Selected;
                    }
                    return;
                }

                if (current == TextBoxEditSessionState.Selected)
                {
                    _states[textElementId] = TextBoxEditSessionState.Idle;
                }
            }
        }

        public void SetEditing(int textElementId, bool isEditing)
        {
            lock (_sync)
            {
                if (isEditing)
                {
                    _states[textElementId] = TextBoxEditSessionState.Editing;
                    return;
                }

                var current = GetStateInternal(textElementId);
                if (current == TextBoxEditSessionState.Saving)
                {
                    return;
                }

                _states[textElementId] = current == TextBoxEditSessionState.Selected
                    ? TextBoxEditSessionState.Selected
                    : TextBoxEditSessionState.Idle;
            }
        }

        public IDisposable BeginSaving(IEnumerable<int> textElementIds)
        {
            var previousStates = new Dictionary<int, TextBoxEditSessionState>();

            lock (_sync)
            {
                if (textElementIds != null)
                {
                    foreach (var id in textElementIds)
                    {
                        if (id <= 0)
                        {
                            continue;
                        }

                        var current = GetStateInternal(id);
                        previousStates[id] = current;
                        _states[id] = TextBoxEditSessionState.Saving;
                    }
                }
            }

            return new SavingScope(this, previousStates);
        }

        private TextBoxEditSessionState GetStateInternal(int textElementId)
        {
            return _states.TryGetValue(textElementId, out var state)
                ? state
                : TextBoxEditSessionState.Idle;
        }

        private sealed class SavingScope : IDisposable
        {
            private readonly TextBoxEditSessionService _service;
            private readonly Dictionary<int, TextBoxEditSessionState> _previousStates;
            private bool _disposed;

            public SavingScope(TextBoxEditSessionService service, Dictionary<int, TextBoxEditSessionState> previousStates)
            {
                _service = service;
                _previousStates = previousStates ?? new Dictionary<int, TextBoxEditSessionState>();
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                lock (_service._sync)
                {
                    foreach (var item in _previousStates)
                    {
                        _service._states[item.Key] = item.Value;
                    }
                }

                _disposed = true;
            }
        }
    }
}
