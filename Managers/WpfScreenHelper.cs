using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// WPF 原生屏幕信息助手（替代 Windows Forms Screen API）
    /// 使用 Windows API + WPF，完全移除 Windows Forms 依赖
    /// </summary>
    public static class WpfScreenHelper
    {
        #region Windows API 声明

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
            MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

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

        private const int MONITORINFOF_PRIMARY = 0x00000001;

        #endregion

        /// <summary>
        /// 获取所有显示器信息
        /// </summary>
        public static List<WpfScreenInfo> GetAllScreens()
        {
            var screens = new List<WpfScreenInfo>();

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
                {
                    var mi = new MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(mi);

                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        var screen = new WpfScreenInfo
                        {
                            DeviceName = mi.szDevice,
                            IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                            PhysicalBounds = new Rect(
                                mi.rcMonitor.left,
                                mi.rcMonitor.top,
                                mi.rcMonitor.right - mi.rcMonitor.left,
                                mi.rcMonitor.bottom - mi.rcMonitor.top
                            ),
                            WorkArea = new Rect(
                                mi.rcWork.left,
                                mi.rcWork.top,
                                mi.rcWork.right - mi.rcWork.left,
                                mi.rcWork.bottom - mi.rcWork.top
                            )
                        };

                        screens.Add(screen);
                    }

                    return true;
                }, IntPtr.Zero);

            // 按主显示器优先排序
            screens.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));

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

