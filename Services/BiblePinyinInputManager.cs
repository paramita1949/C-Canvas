using System;
using System.Windows.Input;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 圣经拼音输入管理器
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

        /// <summary>
        /// 激活拼音输入模式
        /// </summary>
        public async void Activate()
        {
            _isActive = true;
            _currentInput = "";
            await UpdateHintAsync();
        }

        /// <summary>
        /// 取消激活
        /// </summary>
        public void Deactivate()
        {
            _isActive = false;
            _currentInput = "";
            
            // 触发退出回调（用于恢复IME等）
            _onDeactivate?.Invoke();
        }

        /// <summary>
        /// 处理键盘输入
        /// </summary>
        public async System.Threading.Tasks.Task ProcessKeyAsync(Key key)
        {
            if (!_isActive) return;

            // ESC: 取消输入
            if (key == Key.Escape)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine("[拼音输入] 处理ESC键 - 取消输入");
                //#endif
                
                Deactivate();
                return;
            }

            // Backspace: 删除字符
            if (key == Key.Back)
            {
                if (_currentInput.Length > 0)
                    _currentInput = _currentInput.Substring(0, _currentInput.Length - 1);
                await UpdateHintAsync();
                return;
            }

            // 回车: 确认并跳转
            if (key == Key.Enter || key == Key.Return)
            {
                var trimmedInput = _currentInput.Trim();
                var result = await _service.ParseAsync(trimmedInput);
                
                // 只要解析成功且有章节信息就跳转
                if (result.Success && result.Type != LocationType.Book)
                {
                    if (_onLocationConfirmed != null)
                        await _onLocationConfirmed(result);
                    Deactivate();
                }
                return;
            }

            // 空格: 确认当前输入（分隔符）
            if (key == Key.Space)
            {
                // 先检查当前输入是否是拼音，如果是则自动替换为书卷名
                var parts = _currentInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && parts[0].All(ch => char.IsLower(ch)))
                {
                    // 第一部分是拼音，尝试替换
                    var matches = _service.FindBooksByPinyin(parts[0]);
                    if (matches.Count == 1)
                    {
                        // 唯一匹配，替换拼音为书卷名
                        parts[0] = matches[0].BookName;
                        _currentInput = string.Join(" ", parts);
                    }
                }
                
                _currentInput += " ";
                
                // 更新提示（显示带空格的格式化文本）
                await UpdateHintAsync();
                
                return;
            }

            // 字母、数字、连字符
            char c = KeyToChar(key);
            
            if (c != '\0')
            {
                // 检查是否已经有书卷名（中文）
                bool hasBookName = _currentInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Any(ch => ch >= 0x4E00 && ch <= 0x9FA5) ?? false;
                
                // 如果已有书卷名，禁止输入字母
                if (hasBookName && char.IsLetter(c))
                    return;
                
                _currentInput += c;
                
                // 实时检查：只要是唯一匹配就自动替换（不需要完全匹配）
                var currentParts = _currentInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (currentParts.Length > 0 && currentParts[0].All(ch => char.IsLower(ch)))
                {
                    var matches = _service.FindBooksByPinyin(currentParts[0]);
                    if (matches.Count == 1)
                    {
                        // 唯一匹配，立即替换
                        currentParts[0] = matches[0].BookName;
                        _currentInput = string.Join(" ", currentParts);
                    }
                }
                
                await UpdateHintAsync();
            }
        }

        /// <summary>
        /// 更新提示框
        /// </summary>
        private async System.Threading.Tasks.Task UpdateHintAsync()
        {
            var displayText = await _service.FormatDisplayAsync(_currentInput);
            var matches = new System.Collections.Generic.List<BibleBookMatch>();
            
            // 如果当前输入只是拼音，查找匹配的书卷
            var parts = _currentInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var firstPart = parts[0];
                // 判断第一部分是否是拼音（小写字母）
                if (firstPart.All(c => char.IsLower(c)))
                {
                    matches = _service.FindBooksByPinyin(firstPart);
                }
            }
            
            if (_onHintUpdate != null)
                await _onHintUpdate(displayText, matches);
        }

        /// <summary>
        /// 将按键转换为字符
        /// </summary>
        private char KeyToChar(Key key)
        {
            // 字母键 (A-Z)
            if (key >= Key.A && key <= Key.Z)
            {
                return (char)('a' + (key - Key.A));
            }

            // 数字键 (0-9)
            if (key >= Key.D0 && key <= Key.D9)
            {
                return (char)('0' + (key - Key.D0));
            }

            // 小键盘数字键
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return (char)('0' + (key - Key.NumPad0));
            }

            // 连字符
            if (key == Key.OemMinus || key == Key.Subtract)
            {
                return '-';
            }

            return '\0';
        }
    }
}

