using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 键盘输入入口
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 主窗口键盘事件处理（新架构 V5.8.4.9）
        /// 统一由KeyboardShortcutManager处理，投影模式由全局热键处理
        /// </summary>
        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
#if DEBUG
            // System.Diagnostics.Debug.WriteLine($" [KeyDown] Key={e.Key}, 投影={_projectionManager?.IsProjectionActive ?? false}");
#endif
            var key = ResolveEffectiveKey(e);
            LogBibleQuickLocateDebug(
                "WindowPreviewKeyDown",
                $"raw={e.Key}, effective={key}, mods={Keyboard.Modifiers}, handled={e.Handled}, focused={Keyboard.FocusedElement?.GetType().Name ?? "null"}");

            if (key == Key.Escape && HasAnyBibleVersePopupVisible())
            {
                HideBibleVersePopupIfVisible();
                e.Handled = true;
                LogBibleQuickLocateDebug("WindowPreviewKeyDown", "handled Escape for verse popup");
                return;
            }

            if (await TryHandleBibleQuickLocationFromWindowAsync(key))
            {
                e.Handled = true;
                LogBibleQuickLocateDebug("WindowPreviewKeyDown", $"handled by TryHandleBibleQuickLocationFromWindowAsync, key={key}");
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && (key == Key.C || key == Key.V))
            {
                if (SlideListBox?.IsKeyboardFocusWithin == true)
                {
                    return;
                }

                if (ProjectTree?.IsKeyboardFocusWithin == true)
                {
                    if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
                    {
                        return;
                    }

                    if (key == Key.C)
                    {
                        bool copied = await TryCopySelectedTextProjectFromTreeAsync();
                        if (copied)
                        {
                            e.Handled = true;
                        }
                    }
                    return;
                }
            }

            if (_isLyricsMode && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // 让歌词编辑框自身处理 Ctrl+C/X/V/A，避免发布版路由差异导致行为不一致。
                if (key == Key.C || key == Key.X || key == Key.V || key == Key.A)
                {
                    return;
                }
            }

            SyncProjectionNavigationHotKeys();

            if (TryHandleLyricsNavigationHotKeys(e))
            {
                e.Handled = true;
                return;
            }

            if (_keyboardShortcutManager != null && _keyboardShortcutManager.ShouldTextEditorHandle(e.Key))
            {
                return;
            }

            if (_keyboardShortcutManager != null)
            {
                bool handled = await _keyboardShortcutManager.HandleKeyAsync(e.Key, Keyboard.Modifiers);
                if (handled)
                {
                    e.Handled = true;
                }
            }

        }
    }
}

