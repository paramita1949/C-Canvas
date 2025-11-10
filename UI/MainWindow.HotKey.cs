using System;
using System.Windows;
using System.Windows.Input;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 全局热键管理

        private Utils.ShortcutActionHandler _shortcutActionHandler;

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
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("🔧 [全局热键] 开始注册全局热键...");
                #endif

                // Left键: 上一个（文本编辑器/视频/关键帧）
                _globalHotKeyManager.RegisterHotKey(
                    Key.Left,
                    ModifierKeys.None,
                    () =>
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("🎯 [全局热键] Left键触发");
                        #endif
                        Dispatcher.InvokeAsync(async () =>
                        {
                            await _shortcutActionHandler.HandleLeftKeyAsync();
                        });
                    });
                
                // Right键: 下一个（文本编辑器/视频/关键帧）
                _globalHotKeyManager.RegisterHotKey(
                    Key.Right,
                    ModifierKeys.None,
                    () =>
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("🎯 [全局热键] Right键触发");
                        #endif
                        Dispatcher.InvokeAsync(async () =>
                        {
                            await _shortcutActionHandler.HandleRightKeyAsync();
                        });
                    });
                
                // PageUp: 上一页（文本编辑器/原图/关键帧）
                _globalHotKeyManager.RegisterHotKey(
                    Key.PageUp,
                    ModifierKeys.None,
                    () =>
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("🎯 [全局热键] PageUp键触发");
                        #endif
                        Dispatcher.InvokeAsync(() =>
                        {
                            _shortcutActionHandler.HandlePageUpKey();
                        });
                    });
                
                // PageDown: 下一页（文本编辑器/原图/关键帧）
                _globalHotKeyManager.RegisterHotKey(
                    Key.PageDown,
                    ModifierKeys.None,
                    () =>
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("🎯 [全局热键] PageDown键触发");
                        #endif
                        Dispatcher.InvokeAsync(() =>
                        {
                            _shortcutActionHandler.HandlePageDownKey();
                        });
                    });
                
                // F2键: 播放/暂停（脚本/视频）
                _globalHotKeyManager.RegisterHotKey(
                    Key.F2,
                    ModifierKeys.None,
                    () =>
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("🎯 [全局热键] F2键触发");
                        #endif
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
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("🎯 [全局热键] F3键触发");
                        #endif
                        Dispatcher.InvokeAsync(() =>
                        {
                            _shortcutActionHandler.HandleF3Key();
                        });
                    });
                
                // ESC键: 关闭投影/停止视频/清空图片
                _globalHotKeyManager.RegisterHotKey(
                    Key.Escape,
                    ModifierKeys.None,
                    () =>
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("🎯 [全局热键] ESC键触发");
                        #endif
                        Dispatcher.InvokeAsync(async () =>
                        {
                            await _shortcutActionHandler.HandleEscapeAsync();
                        });
                    });
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("✅ [全局热键] 全局热键注册完成（投影模式）");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [全局热键] 启用全局热键失败: {ex.Message}");
                #endif
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
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("✅ [全局热键] 全局热键已注销");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [全局热键] 禁用全局热键失败: {ex.Message}");
                #endif
            }
        }

        #endregion
    }
}

