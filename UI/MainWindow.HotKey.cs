using System;
using System.Windows;
using System.Windows.Input;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 全局热键管理

        private Utils.ShortcutActionHandler _shortcutActionHandler;
        private int _hotKeyIdLeft = -1;
        private int _hotKeyIdRight = -1;
        private int _hotKeyIdPageUp = -1;
        private int _hotKeyIdPageDown = -1;
        private bool _projectionNavigationHotKeysRegistered;

        /// <summary>
        /// 投影锁定且文本框处于编辑状态时，不执行全局导航热键，交给编辑器处理光标移动。
        /// </summary>
        private bool ShouldBypassProjectionNavigationHotKey()
        {
            return _isProjectionLocked && IsTextBoxInEditMode();
        }

        private void RegisterProjectionNavigationHotKeys()
        {
            if (_globalHotKeyManager == null || _shortcutActionHandler == null || _projectionNavigationHotKeysRegistered)
                return;

            _hotKeyIdLeft = _globalHotKeyManager.RegisterHotKey(
                Key.Left,
                ModifierKeys.None,
                () =>
                {
                    Dispatcher.InvokeAsync(async () =>
                    {
                        await _shortcutActionHandler.HandleLeftKeyAsync();
                    });
                });

            _hotKeyIdRight = _globalHotKeyManager.RegisterHotKey(
                Key.Right,
                ModifierKeys.None,
                () =>
                {
                    Dispatcher.InvokeAsync(async () =>
                    {
                        await _shortcutActionHandler.HandleRightKeyAsync();
                    });
                });

            _hotKeyIdPageUp = _globalHotKeyManager.RegisterHotKey(
                Key.PageUp,
                ModifierKeys.None,
                () =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        _shortcutActionHandler.HandlePageUpKey();
                    });
                });

            _hotKeyIdPageDown = _globalHotKeyManager.RegisterHotKey(
                Key.PageDown,
                ModifierKeys.None,
                () =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        _shortcutActionHandler.HandlePageDownKey();
                    });
                });

            _projectionNavigationHotKeysRegistered = true;
        }

        private void UnregisterProjectionNavigationHotKeys()
        {
            if (_globalHotKeyManager == null || !_projectionNavigationHotKeysRegistered)
                return;

            _globalHotKeyManager.UnregisterHotKey(_hotKeyIdLeft);
            _globalHotKeyManager.UnregisterHotKey(_hotKeyIdRight);
            _globalHotKeyManager.UnregisterHotKey(_hotKeyIdPageUp);
            _globalHotKeyManager.UnregisterHotKey(_hotKeyIdPageDown);

            _hotKeyIdLeft = -1;
            _hotKeyIdRight = -1;
            _hotKeyIdPageUp = -1;
            _hotKeyIdPageDown = -1;
            _projectionNavigationHotKeysRegistered = false;
        }

        internal void SyncProjectionNavigationHotKeys()
        {
            if (_projectionManager?.IsProjectionActive != true)
                return;

            if (ShouldBypassProjectionNavigationHotKey())
            {
                UnregisterProjectionNavigationHotKeys();
            }
            else
            {
                RegisterProjectionNavigationHotKeys();
            }
        }

        private void InitializeGlobalHotKeys()
        {
            try
            {
                // 创建全局热键管理器，但不立即注册热键
                _globalHotKeyManager = new Utils.GlobalHotKeyManager(this);
                
                // 创建快捷键业务逻辑处理器（投影和非投影共享）
                _shortcutActionHandler = new Utils.ShortcutActionHandler(this);
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 全局热键管理器初始化失败: {ex.Message}");
                System.Windows.MessageBox.Show($"全局热键管理器初始化失败: {ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 启用全局热键（仅在投影模式下调用）
        /// </summary>
        private void EnableGlobalHotKeys()
        {
            if (_globalHotKeyManager == null || _shortcutActionHandler == null)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("❌ 全局热键管理器或ActionHandler未初始化");
                #endif
                return;
            }

            try
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine("🔧 [全局热键] 开始注册全局热键...");
                //#endif

                RegisterProjectionNavigationHotKeys();
                
                // F2键: 播放/暂停（脚本/视频）
                _globalHotKeyManager.RegisterHotKey(
                    Key.F2,
                    ModifierKeys.None,
                    () =>
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine("🎯 [全局热键] F2键触发");
                        //#endif
                        Dispatcher.InvokeAsync(() =>
                        {
                            _shortcutActionHandler.HandleF2Key();
                        });
                    });
                
                // F3键: 合成播放（开始/停止）
                _globalHotKeyManager.RegisterHotKey(
                    Key.F3,
                    ModifierKeys.None,
                    () =>
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine("🎯 [全局热键] F3键触发");
                        //#endif
                        Dispatcher.InvokeAsync(async () =>
                        {
                            await _shortcutActionHandler.HandleF3KeyAsync();
                        });
                    });
                
                // ESC键: 关闭投影/停止视频/清空图片
                _globalHotKeyManager.RegisterHotKey(
                    Key.Escape,
                    ModifierKeys.None,
                    () =>
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine("🎯 [全局热键] ESC键触发");
                        //#endif
                        Dispatcher.InvokeAsync(async () =>
                        {
                            await _shortcutActionHandler.HandleEscapeAsync();
                        });
                    });
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine("✅ [全局热键] 全局热键注册完成（投影模式）");
                //#endif
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 禁用全局热键（退出投影模式时调用）
        /// </summary>
        private void DisableGlobalHotKeys()
        {
            if (_globalHotKeyManager == null)
                return;

            try
            {
                _globalHotKeyManager.UnregisterAllHotKeys();
                _projectionNavigationHotKeysRegistered = false;
                _hotKeyIdLeft = -1;
                _hotKeyIdRight = -1;
                _hotKeyIdPageUp = -1;
                _hotKeyIdPageDown = -1;
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine("✅ [全局热键] 全局热键已注销");
                //#endif
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}

