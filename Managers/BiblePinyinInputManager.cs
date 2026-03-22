using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using ImageColorChanger.Services;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 圣经拼音输入管理器（有状态管理器，归属 Managers 层）。
    /// </summary>
    public class BiblePinyinInputManager
    {
        private bool _isActive;
        private string _currentInput = "";
        private readonly BiblePinyinService _service;
        private readonly Func<ParseResult, System.Threading.Tasks.Task> _onLocationConfirmed;
        private readonly Func<string, System.Collections.Generic.List<BibleBookMatch>, System.Threading.Tasks.Task> _onHintUpdate;
        private readonly Action _onDeactivate;

        public bool IsActive => _isActive;
        public string CurrentInput => _currentInput;

        public BiblePinyinInputManager(
            BiblePinyinService service,
            Func<ParseResult, System.Threading.Tasks.Task> onLocationConfirmed,
            Func<string, System.Collections.Generic.List<BibleBookMatch>, System.Threading.Tasks.Task> onHintUpdate,
            Action onDeactivate = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _onLocationConfirmed = onLocationConfirmed;
            _onHintUpdate = onHintUpdate;
            _onDeactivate = onDeactivate;
        }

        public async void Activate()
        {
            _isActive = true;
            _currentInput = "";
            LogDebug("Activate", "activated and cleared input");
            await UpdateHintAsync();
        }

        public void Deactivate()
        {
            _isActive = false;
            _currentInput = "";
            LogDebug("Deactivate", "deactivated and cleared input");
            _onDeactivate?.Invoke();
        }

        public async System.Threading.Tasks.Task ProcessKeyAsync(Key key)
        {
            if (!_isActive)
            {
                LogDebug("ProcessKey", $"skip inactive key={key}");
                return;
            }
            LogDebug("ProcessKey", $"key={key}, input='{_currentInput}'");

            if (key == Key.Escape)
            {
                LogDebug("ProcessKey", "Esc -> deactivate");
                Deactivate();
                return;
            }

            if (key == Key.Back)
            {
                if (_currentInput.Length > 0)
                    _currentInput = _currentInput.Substring(0, _currentInput.Length - 1);
                LogDebug("ProcessKey", $"Back -> input='{_currentInput}'");
                await UpdateHintAsync();
                return;
            }

            if (key == Key.Enter || key == Key.Return)
            {
                var trimmedInput = _currentInput.Trim();
                var result = await _service.ParseAsync(trimmedInput);
                LogDebug("ProcessEnterResult", $"parse success={result.Success}, type={result.Type}, input='{trimmedInput}'");
                if (result.Success && result.Type != LocationType.Book)
                {
                    if (_onLocationConfirmed != null)
                        await _onLocationConfirmed(result);
                    Deactivate();
                }
                return;
            }

            if (key == Key.Space)
            {
                var parts = _currentInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && parts[0].All(ch => char.IsLower(ch)))
                {
                    var matches = _service.FindBooksByPinyin(parts[0]);
                    if (matches.Count == 1)
                    {
                        parts[0] = matches[0].BookName;
                        _currentInput = string.Join(" ", parts);
                    }
                }

                _currentInput += " ";
                LogDebug("ProcessKey", $"Space -> input='{_currentInput}'");
                await UpdateHintAsync();
                return;
            }

            if (TryHandleBookCandidateSelectionByNumber(key))
            {
                await UpdateHintAsync();
                return;
            }

            char c = KeyToChar(key);
            if (c != '\0')
            {
                bool hasBookName = _currentInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Any(ch => ch >= 0x4E00 && ch <= 0x9FA5) ?? false;

                if (hasBookName && char.IsLetter(c))
                    return;

                _currentInput += c;
                LogDebug("ProcessKey", $"append '{c}' -> input='{_currentInput}'");

                var currentParts = _currentInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (currentParts.Length > 0 && currentParts[0].All(ch => char.IsLower(ch)))
                {
                    var matches = _service.FindBooksByPinyin(currentParts[0]);
                    if (matches.Count == 1)
                    {
                        currentParts[0] = matches[0].BookName;
                        _currentInput = string.Join(" ", currentParts);
                    }
                }

                await UpdateHintAsync();
                return;
            }

            LogDebug("ProcessKey", $"ignored key={key} (no mapped char)");
        }

        private bool TryHandleBookCandidateSelectionByNumber(Key key)
        {
            if (!TryGetCandidateIndexFromNumberKey(key, out int targetIndex))
            {
                return false;
            }

            var parts = _currentInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            var firstPart = parts[0];
            if (!firstPart.All(ch => char.IsLower(ch)))
            {
                return false;
            }

            var matches = _service.FindBooksByPinyin(firstPart);
            if (matches.Count <= 1)
            {
                return false;
            }

            if (targetIndex < 0 || targetIndex >= matches.Count)
            {
                LogDebug("ProcessKey", $"candidate index out of range: key={key}, targetIndex={targetIndex}, matches={matches.Count}");
                return true;
            }

            var selectedBookName = matches[targetIndex].BookName;
            if (parts.Length == 1)
            {
                _currentInput = $"{selectedBookName} ";
            }
            else
            {
                var tail = string.Join(" ", parts.Skip(1));
                _currentInput = string.IsNullOrWhiteSpace(tail)
                    ? $"{selectedBookName} "
                    : $"{selectedBookName} {tail}";
            }

            LogDebug("ProcessKey", $"select candidate {targetIndex + 1} -> input='{_currentInput}'");
            return true;
        }

        private static bool TryGetCandidateIndexFromNumberKey(Key key, out int targetIndex)
        {
            targetIndex = key switch
            {
                Key.D1 or Key.NumPad1 => 0,
                Key.D2 or Key.NumPad2 => 1,
                Key.D3 or Key.NumPad3 => 2,
                Key.D4 or Key.NumPad4 => 3,
                Key.D5 or Key.NumPad5 => 4,
                Key.D6 or Key.NumPad6 => 5,
                Key.D7 or Key.NumPad7 => 6,
                Key.D8 or Key.NumPad8 => 7,
                Key.D9 or Key.NumPad9 => 8,
                _ => -1
            };

            return targetIndex >= 0;
        }

        private async System.Threading.Tasks.Task UpdateHintAsync()
        {
            var displayText = await _service.FormatDisplayAsync(_currentInput);
            var matches = new System.Collections.Generic.List<BibleBookMatch>();

            var parts = _currentInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var firstPart = parts[0];
                if (firstPart.All(c => char.IsLower(c)))
                {
                    matches = _service.FindBooksByPinyin(firstPart);
                }
            }

            if (_onHintUpdate != null)
                await _onHintUpdate(displayText, matches);

            LogDebug("UpdateHint", $"input='{_currentInput}', display='{displayText}', matches={matches.Count}");
        }

        private char KeyToChar(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                return (char)('a' + (key - Key.A));
            }

            if (key >= Key.D0 && key <= Key.D9)
            {
                return (char)('0' + (key - Key.D0));
            }

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return (char)('0' + (key - Key.NumPad0));
            }

            if (key == Key.OemMinus || key == Key.Subtract)
            {
                return '-';
            }

            return '\0';
        }

        [Conditional("DEBUG")]
        private static void LogDebug(string stage, string detail)
        {
            // 调试阶段结束：保持空实现，便于后续快速重新启用。
        }
    }
}
