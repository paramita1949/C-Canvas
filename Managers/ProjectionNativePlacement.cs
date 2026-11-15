using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ImageColorChanger.Managers
{
    /// <summary>只操作指定投影HWND；不切换系统主屏、不改变显示模式/排列。</summary>
    public static class ProjectionNativePlacement
    {
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MONITORINFOEX
        {
            public int Size; public RECT Monitor, Work; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        private delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        private const uint NoZOrder = 0x0004, NoActivate = 0x0010;

        private static MONITORINFOEX FindDevice(string device)
        {
            MONITORINFOEX found = default;
            bool matched = false;
            if (string.IsNullOrWhiteSpace(device)) throw new InvalidOperationException("选定的显示设备无效。请重新选择显示器。");
            bool ok = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, dc, rect, data) =>
            {
                var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(monitor, ref info) && string.Equals(info.Device, device, StringComparison.OrdinalIgnoreCase))
                {
                    found = info; matched = true;
                }
                return true;
            }, IntPtr.Zero);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!matched) throw new InvalidOperationException("选定的显示器已断开或改变。请重新选择显示器。");
            return found;
        }

        private static Rect ToRect(RECT r) => new Rect(r.Left, r.Top, (long)r.Right - r.Left, (long)r.Bottom - r.Top);

        public static void Place(Window window, WpfScreenInfo screen)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            window.Dispatcher.VerifyAccess();
            // 先验证实际设备，再创建HWND；无效目标不能闪现在主屏。
            var monitor = FindDevice(screen.DeviceName);
            screen.IsPrimary = (monitor.Flags & 1) != 0;
            if (screen.IsPrimary)
                throw new InvalidOperationException("选定的显示器现在是主显示器。请重新选择外部显示器。");
            screen.PhysicalBounds = ToRect(monitor.Monitor);
            screen.WorkArea = ToRect(monitor.Work);
            screen.DpiScale = screen.DpiScale; // 新物理矩形使WPF布局缓存失效。
            var bounds = ProjectionPlacementPolicy.GetPhysicalBounds(screen);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.WindowState = WindowState.Normal;
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (!SetWindowPos(hwnd, IntPtr.Zero, (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height, NoZOrder | NoActivate))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public static bool Verify(Window window, WpfScreenInfo screen, out string detail)
        {
            window.Dispatcher.VerifyAccess();
            var expected = FindDevice(screen.DeviceName);
            screen.IsPrimary = (expected.Flags & 1) != 0;
            if (screen.IsPrimary)
            {
                detail = "TargetBecamePrimary; TargetDevice=" + expected.Device;
                return false;
            }
            var hwnd = new WindowInteropHelper(window).Handle;
            var monitor = MonitorFromWindow(hwnd, 0); // 不用nearest回退掩盖错屏。
            var actual = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
            if (hwnd == IntPtr.Zero || monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref actual) || !GetWindowRect(hwnd, out var rect))
            {
                detail = "NativePlacementUnavailable"; return false;
            }
            bool sameDevice = string.Equals(expected.Device, actual.Device, StringComparison.OrdinalIgnoreCase);
            bool sameBounds = rect.Left == expected.Monitor.Left && rect.Top == expected.Monitor.Top && rect.Right == expected.Monitor.Right && rect.Bottom == expected.Monitor.Bottom;
            detail = $"TargetDevice={expected.Device}; ActualDevice={actual.Device}; DeviceMatches={sameDevice}; PhysicalBoundsMatches={sameBounds}";
            return sameDevice && sameBounds;
        }
    }
}
