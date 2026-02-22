using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// WPF 屏幕信息助手（混合方案：使用 Windows Forms Screen 获取物理分辨率 + GetDpiForMonitor 获取真实 DPI）
    /// </summary>
    public static class WpfScreenHelper
    {
        #region Windows API 声明

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
            MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
        }

        private enum MONITOR_DPI_TYPE
        {
            MDT_EFFECTIVE_DPI = 0,
            MDT_ANGULAR_DPI = 1,
            MDT_RAW_DPI = 2,
            MDT_DEFAULT = MDT_EFFECTIVE_DPI
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        private const int MONITORINFOF_PRIMARY = 0x00000001;
        private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
        private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

        #endregion

        /// <summary>
        /// 获取所有显示器信息（优先使用 EnumDisplayDevices/EnumDisplaySettings，失败时回退 Screen API）
        /// 说明：
        /// 1) EnumDisplaySettings 返回物理像素，通常更稳定，不受 DPI 虚拟化影响
        /// 2) 某些环境下若枚举失败，再使用 Screen API 兜底
        /// </summary>
        public static List<WpfScreenInfo> GetAllScreens()
        {
            var preferred = GetAllScreensAlternative();
            if (preferred.Count > 0)
            {
#if DEBUG
                // System.Diagnostics.Debug.WriteLine("ℹ️ [WpfScreenHelper] 使用 EnumDisplayDevices 作为主检测结果");
#endif
                return preferred;
            }

#if DEBUG
            // System.Diagnostics.Debug.WriteLine("⚠️ [WpfScreenHelper] EnumDisplayDevices 未返回有效结果，回退到 Screen API");
#endif

            var screens = new List<WpfScreenInfo>();

            // 使用 Windows Forms Screen API 获取物理分辨率
            foreach (var screen in WinFormsScreen.AllScreens)
            {
                // 获取设备名称
                string deviceName = screen.DeviceName;

                // 获取显示器的真实 DPI
                uint dpiX = 96, dpiY = 96;
                try
                {
                    // 获取显示器句柄
                    IntPtr hMonitor = MonitorFromPoint(
                        new POINT { x = screen.Bounds.Left + 1, y = screen.Bounds.Top + 1 },
                        MONITOR_DEFAULTTONEAREST
                    );

                    if (hMonitor != IntPtr.Zero)
                    {
                        GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                    }
                }
                catch (Exception ex)
                {
#if DEBUG
                    // System.Diagnostics.Debug.WriteLine($"⚠️ [DPI] 获取 {deviceName} 的 DPI 失败: {ex.Message}，使用默认值 96");
                    _ = ex;
#else
                    _ = ex; // 避免编译警告
#endif
                }

                var wpfScreen = new WpfScreenInfo
                {
                    DeviceName = deviceName,
                    IsPrimary = screen.Primary,
                    // Screen.Bounds 返回的是物理像素
                    PhysicalBounds = new Rect(
                        screen.Bounds.X,
                        screen.Bounds.Y,
                        screen.Bounds.Width,
                        screen.Bounds.Height
                    ),
                    WorkArea = new Rect(
                        screen.WorkingArea.X,
                        screen.WorkingArea.Y,
                        screen.WorkingArea.Width,
                        screen.WorkingArea.Height
                    ),
                    DpiScale = (dpiX / 96.0, dpiY / 96.0)
                };

#if DEBUG
                // System.Diagnostics.Debug.WriteLine($"📺 [Screen API] {(wpfScreen.IsPrimary ? "主" : "扩展")} - {deviceName}");
                // System.Diagnostics.Debug.WriteLine($"   物理分辨率: {screen.Bounds.Width}×{screen.Bounds.Height} (Screen.Bounds)");
                // System.Diagnostics.Debug.WriteLine($"   真实 DPI: {dpiX}×{dpiY} ({dpiX / 96.0 * 100:F0}%)");
                // System.Diagnostics.Debug.WriteLine($"   WPF 单位: {wpfScreen.WpfWidth:F0}×{wpfScreen.WpfHeight:F0}");
#endif

                screens.Add(wpfScreen);
            }

            // 按主显示器优先排序
            screens.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));

#if DEBUG
            // System.Diagnostics.Debug.WriteLine($"✅ [Screen API] 共检测到 {screens.Count} 个显示器");
#endif

            return screens;
        }

        /// <summary>
        /// 备用方法：使用 EnumDisplayDevices 检测显示器（推荐！可获取正确的物理分辨率）
        /// 根据 Microsoft 文档：EnumDisplaySettings 返回物理像素，不受 DPI 虚拟化影响
        /// </summary>
        public static List<WpfScreenInfo> GetAllScreensAlternative()
        {
            var screens = new List<WpfScreenInfo>();

            // 第一步：获取所有 HMONITOR 句柄（用于尝试获取 DPI）
            var monitorHandles = new Dictionary<string, IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
                {
                    var mi = new MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(mi);
                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        monitorHandles[mi.szDevice] = hMonitor;
                    }
                    return true;
                }, IntPtr.Zero);

            // 第二步：使用 EnumDisplayDevices 枚举显示设备
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            d.cb = Marshal.SizeOf(d);

            for (uint id = 0; EnumDisplayDevices(null, id, ref d, 0); id++)
            {
                if ((d.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                {
                    DEVMODE dm = new DEVMODE();
                    dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

                    if (EnumDisplaySettings(d.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                    {
                        // 尝试获取 DPI（对于某些 USB 显示器可能不准确）
                        uint dpiX = 96, dpiY = 96;
                        bool dpiFromMonitor = false;

                        if (monitorHandles.TryGetValue(d.DeviceName, out IntPtr hMonitor))
                        {
                            try
                            {
                                uint tempDpiX, tempDpiY;
                                if (GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out tempDpiX, out tempDpiY) == 0)
                                {
                                    // 只有当分辨率匹配时才使用 GetDpiForMonitor 的结果
                                    // （USB 显示器可能被错误地报告为主显示器的 DPI）
                                    dpiX = tempDpiX;
                                    dpiY = tempDpiY;
                                    dpiFromMonitor = true;
                                }
                            }
                            catch { }
                        }

                        // 如果无法从 GetDpiForMonitor 获取，使用默认 96 DPI
                        // （大多数外接显示器和投影仪都是 100% 缩放）

                        var screen = new WpfScreenInfo
                        {
                            DeviceName = d.DeviceName,
                            IsPrimary = (d.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                            PhysicalBounds = new Rect(
                                dm.dmPositionX,
                                dm.dmPositionY,
                                dm.dmPelsWidth,
                                dm.dmPelsHeight
                            ),
                            WorkArea = new Rect(
                                dm.dmPositionX,
                                dm.dmPositionY,
                                dm.dmPelsWidth,
                                dm.dmPelsHeight
                            ),
                            DpiScale = (dpiX / 96.0, dpiY / 96.0)
                        };

#if DEBUG
                        // System.Diagnostics.Debug.WriteLine($"📺 [EnumDisplayDevices] {(screen.IsPrimary ? "主" : "扩展")} - {d.DeviceString}");
                        // System.Diagnostics.Debug.WriteLine($"   设备: {d.DeviceName}");
                        // System.Diagnostics.Debug.WriteLine($"   物理分辨率: {dm.dmPelsWidth}×{dm.dmPelsHeight} @ {dm.dmDisplayFrequency}Hz (EnumDisplaySettings)");
                        // System.Diagnostics.Debug.WriteLine($"   DPI: {dpiX}×{dpiY} ({dpiX / 96.0 * 100:F0}%) {(dpiFromMonitor ? "[GetDpiForMonitor]" : "[默认值]")}");
                        // System.Diagnostics.Debug.WriteLine($"   WPF 单位: {screen.WpfWidth}×{screen.WpfHeight}");
                        _ = dpiFromMonitor;
#else
                        _ = dpiFromMonitor; // 避免 Release 模式下的未使用变量警告
#endif

                        screens.Add(screen);
                    }
                }
                d.cb = Marshal.SizeOf(d);
            }

            screens.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));

#if DEBUG
            // System.Diagnostics.Debug.WriteLine($"✅ [EnumDisplayDevices] 共检测到 {screens.Count} 个显示器");
#endif

            return screens;
        }

        /// <summary>
        /// 获取主显示器
        /// </summary>
        public static WpfScreenInfo GetPrimaryScreen()
        {
            var screens = GetAllScreens();
            return screens.Find(s => s.IsPrimary) ?? screens[0];
        }

        /// <summary>
        /// 获取窗口所在的显示器
        /// </summary>
        public static WpfScreenInfo GetScreenFromWindow(Window window)
        {
            if (window == null)
                return GetPrimaryScreen();

            var windowRect = new Rect(
                window.Left,
                window.Top,
                window.ActualWidth,
                window.ActualHeight
            );

            var screens = GetAllScreens();
            WpfScreenInfo bestMatch = screens[0];
            double maxOverlap = 0;

            foreach (var screen in screens)
            {
                var overlap = GetOverlapArea(windowRect, screen.WpfBounds);
                if (overlap > maxOverlap)
                {
                    maxOverlap = overlap;
                    bestMatch = screen;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// 获取 DPI 缩放因子
        /// </summary>
        public static (double X, double Y) GetDpiScale(Visual visual)
        {
            if (visual == null)
                return (1.0, 1.0);

            try
            {
                var source = PresentationSource.FromVisual(visual);
                if (source?.CompositionTarget != null)
                {
                    var matrix = source.CompositionTarget.TransformToDevice;
                    return (matrix.M11, matrix.M22);
                }
            }
            catch
            {
                // 忽略错误，返回默认值
            }

            return (1.0, 1.0);
        }

        /// <summary>
        /// 计算两个矩形的重叠面积
        /// </summary>
        private static double GetOverlapArea(Rect rect1, Rect rect2)
        {
            var intersection = Rect.Intersect(rect1, rect2);
            if (intersection.IsEmpty)
                return 0;

            return intersection.Width * intersection.Height;
        }
    }

    /// <summary>
    /// WPF 屏幕信息类（替代 System.Windows.Forms.Screen）
    /// </summary>
    public class WpfScreenInfo
    {
        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName { get; set; }

        /// <summary>
        /// 是否为主显示器
        /// </summary>
        public bool IsPrimary { get; set; }

        /// <summary>
        /// 物理像素边界（用于窗口定位）
        /// </summary>
        public Rect PhysicalBounds { get; set; }

        /// <summary>
        /// 工作区（去除任务栏）
        /// </summary>
        public Rect WorkArea { get; set; }

        private (double X, double Y)? _dpiScale;
        private Rect? _wpfBounds;

        /// <summary>
        /// 设置 DPI 缩放因子（从窗口获取）
        /// </summary>
        public void UpdateDpiScale(Visual visual)
        {
            _dpiScale = WpfScreenHelper.GetDpiScale(visual);
            _wpfBounds = null; // 清除缓存
        }

        /// <summary>
        /// DPI 缩放因子
        /// </summary>
        public (double X, double Y) DpiScale
        {
            get => _dpiScale ?? (1.0, 1.0);
            set
            {
                _dpiScale = value;
                _wpfBounds = null; // 清除缓存
            }
        }

        /// <summary>
        /// WPF 设备独立单位边界（用于布局计算）
        /// </summary>
        public Rect WpfBounds
        {
            get
            {
                if (_wpfBounds == null)
                {
                    var dpi = DpiScale;
                    _wpfBounds = new Rect(
                        PhysicalBounds.Left / dpi.X,
                        PhysicalBounds.Top / dpi.Y,
                        PhysicalBounds.Width / dpi.X,
                        PhysicalBounds.Height / dpi.Y
                    );
                }
                return _wpfBounds.Value;
            }
        }

        /// <summary>
        /// 物理像素宽度
        /// </summary>
        public int PhysicalWidth => (int)PhysicalBounds.Width;

        /// <summary>
        /// 物理像素高度
        /// </summary>
        public int PhysicalHeight => (int)PhysicalBounds.Height;

        /// <summary>
        /// WPF 单位宽度
        /// </summary>
        public int WpfWidth => (int)WpfBounds.Width;

        /// <summary>
        /// WPF 单位高度
        /// </summary>
        public int WpfHeight => (int)WpfBounds.Height;

        public override string ToString()
        {
            var dpi = DpiScale;
            return $"{DeviceName} ({(IsPrimary ? "主显示器" : "副显示器")}) - " +
                   $"物理: {PhysicalWidth}×{PhysicalHeight}, " +
                   $"WPF: {WpfWidth}×{WpfHeight}, " +
                   $"DPI: {dpi.X * 100:F0}%";
        }
    }
}

