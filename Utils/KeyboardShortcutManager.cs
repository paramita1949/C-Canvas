using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 键盘快捷键管理器（非投影模式）
    /// 处理前台按键事件，只有程序在前台时响应
    /// </summary>
    public class KeyboardShortcutManager
    {
        private readonly ShortcutActionHandler _actionHandler;
        private readonly UI.MainWindow _mainWindow;

        public KeyboardShortcutManager(UI.MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _actionHandler = new ShortcutActionHandler(mainWindow);
        }

        /// <summary>
        /// 处理键盘按键事件
        /// </summary>
        /// <param name="key">按下的键</param>
        /// <param name="modifiers">修饰键</param>
        /// <returns>是否处理了该按键</returns>
        public async Task<bool> HandleKeyAsync(Key key, ModifierKeys modifiers)
        {
            // 只处理没有修饰键的按键（Ctrl、Alt、Shift等）
            if (modifiers != ModifierKeys.None)
            {
                return false;
            }

            // 🔧 检查是否在投影模式下
            // 投影模式下，全局热键处理的按键不在这里处理
            bool isProjectionActive = _mainWindow.GetProjectionManager()?.IsProjectionActive ?? false;
            if (isProjectionActive)
            {
                // 投影模式下，这些按键由全局热键处理，这里不处理
                bool isGlobalHotKey = key == Key.Left || key == Key.Right || key == Key.F2 || 
                                      key == Key.PageUp || key == Key.PageDown || key == Key.Escape ||
                                      key == Key.F3;
                
                if (isGlobalHotKey)
                {
                    return false;
                }
            }

            switch (key)
            {
                case Key.Escape:
                    return await _actionHandler.HandleEscapeAsync();

                case Key.Left:
                    await _actionHandler.HandleLeftKeyAsync();
                    return true;

                case Key.Right:
                    await _actionHandler.HandleRightKeyAsync();
                    return true;

                case Key.PageUp:
                    _actionHandler.HandlePageUpKey();
                    return true;

                case Key.PageDown:
                    _actionHandler.HandlePageDownKey();
                    return true;

                case Key.F2:
                    _actionHandler.HandleF2Key();
                    return true;

                case Key.F3:
                    _actionHandler.HandleF3Key();
                    return true;

                case Key.Up:
                    _actionHandler.HandleUpKey();
                    return true;

                case Key.Down:
                    _actionHandler.HandleDownKey();
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 检查按键是否应该被文本编辑器处理
        /// </summary>
        /// <param name="key">按键</param>
        /// <returns>是否应该由文本编辑器处理</returns>
        public bool ShouldTextEditorHandle(Key key)
        {
            // 文本编辑器激活时，PageUp/PageDown由文本编辑器自己处理
            if (_mainWindow.IsTextEditorActive())
            {
                return key == Key.PageUp || key == Key.PageDown;
            }
            return false;
        }
    }
}

