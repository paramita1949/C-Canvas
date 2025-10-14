using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 全局热键管理器
    /// 支持在软件后台时也能响应按键
    /// </summary>
    public class GlobalHotKeyManager : IDisposable
    {
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        #endregion

        #region 常量定义

        // 修饰键常量
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        // WM_HOTKEY 消息
        private const int WM_HOTKEY = 0x0312;

        #endregion

        #region 字段

        private readonly Window _window;
        private readonly IntPtr _windowHandle;
        private HwndSource _source;
        private readonly Dictionary<int, Action> _hotKeyHandlers = new Dictionary<int, Action>();
        private int _currentId = 9000; // 起始ID

        #endregion

        #region 构造函数

        public GlobalHotKeyManager(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));

            // 获取窗口句柄
            var helper = new WindowInteropHelper(window);
            _windowHandle = helper.Handle;

            // 如果窗口还未加载，等待加载后再注册
            if (_windowHandle == IntPtr.Zero)
            {
                window.SourceInitialized += Window_SourceInitialized;
            }
            else
            {
                RegisterWindowHook();
            }
        }

        #endregion

        #region 初始化

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var helper = new WindowInteropHelper(_window);
            if (helper.Handle != IntPtr.Zero)
            {
                RegisterWindowHook();
            }
        }

        private void RegisterWindowHook()
        {
            var helper = new WindowInteropHelper(_window);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(HwndHook);
        }

        #endregion

        #region 热键处理

        /// <summary>
        /// 窗口消息钩子
        /// </summary>
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_hotKeyHandlers.TryGetValue(id, out Action handler))
                {
                    try
                    {
                        // 在UI线程上执行处理器
                        _window.Dispatcher.InvokeAsync(() =>
                        {
                            handler?.Invoke();
                        });
                        handled = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 全局热键处理失败: {ex.Message}");
                    }
                }
            }
            return IntPtr.Zero;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 注册全局热键
        /// </summary>
        /// <param name="key">按键</param>
        /// <param name="modifiers">修饰键（Ctrl、Alt、Shift等）</param>
        /// <param name="handler">热键触发时的处理方法</param>
        /// <returns>热键ID，用于后续注销，失败返回-1</returns>
        public int RegisterHotKey(Key key, ModifierKeys modifiers, Action handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var helper = new WindowInteropHelper(_window);
            if (helper.Handle == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("❌ 窗口句柄无效，无法注册全局热键");
                return -1;
            }

            // 转换修饰键
            uint mod = 0;
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                mod |= MOD_ALT;
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                mod |= MOD_CONTROL;
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                mod |= MOD_SHIFT;
            if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
                mod |= MOD_WIN;

            // 添加 MOD_NOREPEAT，防止按住键时重复触发
            mod |= MOD_NOREPEAT;

            // 转换虚拟键码
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            // 注册热键
            int id = _currentId++;
            bool success = RegisterHotKey(helper.Handle, id, mod, vk);

            if (success)
            {
                _hotKeyHandlers[id] = handler;
                System.Diagnostics.Debug.WriteLine($"✅ 已注册全局热键: {modifiers}+{key} (ID={id})");
                return id;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"❌ 注册全局热键失败: {modifiers}+{key}");
                return -1;
            }
        }

        /// <summary>
        /// 注销全局热键
        /// </summary>
        /// <param name="id">热键ID</param>
        public void UnregisterHotKey(int id)
        {
            if (id < 0)
                return;

            var helper = new WindowInteropHelper(_window);
            if (helper.Handle != IntPtr.Zero)
            {
                bool success = UnregisterHotKey(helper.Handle, id);
                if (success)
                {
                    _hotKeyHandlers.Remove(id);
                    System.Diagnostics.Debug.WriteLine($"✅ 已注销全局热键 (ID={id})");
                }
            }
        }

        /// <summary>
        /// 注销所有全局热键
        /// </summary>
        public void UnregisterAllHotKeys()
        {
            var helper = new WindowInteropHelper(_window);
            if (helper.Handle != IntPtr.Zero)
            {
                foreach (int id in _hotKeyHandlers.Keys)
                {
                    UnregisterHotKey(helper.Handle, id);
                }
                _hotKeyHandlers.Clear();
                System.Diagnostics.Debug.WriteLine("✅ 已注销所有全局热键");
            }
        }

        #endregion

        #region 资源释放

        public void Dispose()
        {
            UnregisterAllHotKeys();

            if (_source != null)
            {
                _source.RemoveHook(HwndHook);
                _source = null;
            }
        }

        #endregion
    }
}

