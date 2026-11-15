using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 只读投影快照：每次重新读取 Windows 的屏幕、窗口及活动显示路径。
    /// 不创建窗口句柄、不改变屏幕配置或窗口位置、不采集画面/用户内容/设备序列号。
    /// 调用方应在窗口所属 UI 线程调用；不能访问的区段独立记录错误。
    /// </summary>
    public static class ProjectionDiagnosticSnapshotCollector
    {
        private const uint QueryOnlyActivePaths = 0x00000002;
        private const int ErrorInsufficientBuffer = 122;
        private const uint MonitorDefaultToNearest = 2;
        private const uint MonitorInfoPrimary = 1;
        private const uint InvalidModeIndex = 0xffffffff;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Capture(Window mainWindow, Window projectionWindow)
        {
            var text = new StringBuilder(4096);
            Line(text, "只读显示诊断", "snapshot.utc", DateTime.UtcNow.ToString("O", Invariant));
            Line(text, "坐标约定", "snapshot.coordinates", "native=Windows API coordinates (normally physical pixels in DPI-aware process); WPF=DIPs; native queries inherit existing DPI awareness");
            Line(text, "身份判读说明", "snapshot.identityNote", "GDI device name joins monitor/window/source; friendly name and output technology are evidence, not an automatic laptop/projector classification");
            var monitors = new List<MonitorSnapshot>();
            Section(text, "monitors", () => CaptureMonitors(text, monitors));
            var main = CaptureWindow(text, "main", "主窗口", mainWindow);
            var projection = CaptureWindow(text, "projection", "投影窗口", projectionWindow);
            Section(text, "comparison", () => CaptureComparisons(text, monitors, main, projection));
            Section(text, "queryDisplayConfig", () => CaptureActivePaths(text));
            return text.ToString();
        }

        private static void CaptureMonitors(StringBuilder text, List<MonitorSnapshot> monitors)
        {
            int index = 0;
            MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data) =>
            {
                string prefix = "monitor[" + index.ToString(Invariant) + "]";
                index++;
                Section(text, prefix, () =>
                {
                    var snapshot = ReadMonitor(text, prefix, monitor);
                    if (snapshot != null) monitors.Add(snapshot);
                });
                return true;
            };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                ApiError(text, "monitors", "EnumDisplayMonitors", Marshal.GetLastWin32Error());
            GC.KeepAlive(callback);
            Line(text, "有效屏幕数量", "monitors.count", Number(monitors.Count));
            Line(text, "枚举屏幕数量", "monitors.enumeratedCount", Number(index));
        }

        private static MonitorSnapshot ReadMonitor(StringBuilder text, string prefix, IntPtr monitor)
        {
            Line(text, "原生屏幕句柄", prefix + ".hmonitor", Hex(monitor));
            var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                ApiError(text, prefix, "GetMonitorInfoW", Marshal.GetLastWin32Error());
                return null;
            }
            var snapshot = new MonitorSnapshot
            {
                Handle = monitor,
                Device = info.Device ?? string.Empty,
                IsPrimary = (info.Flags & MonitorInfoPrimary) != 0
            };
            Line(text, "Windows 屏幕设备名", prefix + ".device", snapshot.Device);
            Line(text, "是否主屏", prefix + ".isPrimary", Boolean(snapshot.IsPrimary));
            Line(text, "原生屏幕边界", prefix + ".bounds", Rect(info.Monitor));
            Line(text, "原生工作区", prefix + ".workArea", Rect(info.Work));
            Section(text, prefix + ".dpi", () =>
            {
                int result = GetDpiForMonitor(monitor, 0, out uint dpiX, out uint dpiY);
                if (result == 0)
                {
                    Line(text, "有效屏幕 DPI", prefix + ".dpi", "x:" + Number(dpiX) + ",y:" + Number(dpiY));
                    Line(text, "DPI 查询 API", prefix + ".dpiApi", "GetDpiForMonitor(MDT_EFFECTIVE_DPI); result depends on existing process DPI awareness");
                }
                else ApiError(text, prefix + ".dpi", "GetDpiForMonitor HRESULT", result);
            });
            return snapshot;
        }

        private static WindowSnapshot CaptureWindow(StringBuilder text, string prefix, string label, Window window)
        {
            var result = new WindowSnapshot();
            Line(text, label + "是否存在", prefix + ".windowPresent", Boolean(window != null));
            if (window == null) return result;
            // Do not marshal synchronously to another dispatcher: diagnostics must not deadlock shutdown.
            if (!window.Dispatcher.CheckAccess())
            {
                Line(text, label + "线程错误", prefix + ".error", "WPF window dispatcher access required; no cross-thread access attempted");
                return result;
            }
            Section(text, prefix + ".wpf", () =>
            {
                Line(text, label + "WPF 设置位置/尺寸", prefix + ".wpfGeometry", "left:" + Number(window.Left) + ",top:" + Number(window.Top) + ",width:" + Number(window.Width) + ",height:" + Number(window.Height));
                Line(text, label + "WPF 实际尺寸", prefix + ".wpfActualSize", "width:" + Number(window.ActualWidth) + ",height:" + Number(window.ActualHeight));
                Line(text, label + "WPF 状态", prefix + ".wpfState", "state:" + window.WindowState + ",visible:" + Boolean(window.IsVisible) + ",active:" + Boolean(window.IsActive) + ",loaded:" + Boolean(window.IsLoaded) + ",topmost:" + Boolean(window.Topmost));
                var restore = window.RestoreBounds;
                Line(text, label + "WPF 恢复边界", prefix + ".wpfRestoreBounds", restore.IsEmpty ? "empty" : "left:" + Number(restore.Left) + ",top:" + Number(restore.Top) + ",width:" + Number(restore.Width) + ",height:" + Number(restore.Height));
                var dpi = VisualTreeHelper.GetDpi(window);
                Line(text, label + "WPF 渲染 DPI", prefix + ".wpfDpi", "x:" + Number(dpi.PixelsPerInchX) + ",y:" + Number(dpi.PixelsPerInchY) + ",scaleX:" + Number(dpi.DpiScaleX) + ",scaleY:" + Number(dpi.DpiScaleY));
            });
            Section(text, prefix + ".hwnd", () =>
            {
                // Handle only: EnsureHandle would create a native window and change application lifecycle.
                result.Handle = new WindowInteropHelper(window).Handle;
                Line(text, label + "原生窗口句柄", prefix + ".hwnd", Hex(result.Handle));
            });
            if (result.Handle == IntPtr.Zero)
            {
                Line(text, label + "原生窗口状态", prefix + ".nativeStatus", "not-created-or-unavailable; no handle creation attempted");
                return result;
            }
            Section(text, prefix + ".nativeRect", () =>
            {
                if (GetWindowRect(result.Handle, out NativeRect rect))
                    Line(text, label + "原生窗口边界", prefix + ".nativeRect", Rect(rect));
                else ApiError(text, prefix, "GetWindowRect", Marshal.GetLastWin32Error());
            });
            Section(text, prefix + ".nativeDpi", () =>
            {
                uint dpi = GetDpiForWindow(result.Handle);
                if (dpi != 0) Line(text, label + "原生窗口 DPI", prefix + ".nativeDpi", Number(dpi));
                else Line(text, label + "DPI 查询错误", prefix + ".nativeDpi.error", "GetDpiForWindow returned 0 (invalid/unavailable HWND; API has no documented last-error contract)");
            });
            Section(text, prefix + ".monitor", () =>
            {
                result.Monitor = MonitorFromWindow(result.Handle, MonitorDefaultToNearest);
                Line(text, label + "所在原生屏幕", prefix + ".hmonitor", Hex(result.Monitor));
                Line(text, label + "屏幕选择 API", prefix + ".monitorApi", "MonitorFromWindow(MONITOR_DEFAULTTONEAREST); largest intersection or nearest for offscreen window; minimized window uses pre-minimize rect");
                if (result.Monitor == IntPtr.Zero)
                {
                    Line(text, label + "所在屏幕查询错误", prefix + ".monitor.error", "MonitorFromWindow returned NULL (API has no documented last-error contract)");
                    return;
                }
                var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(result.Monitor, ref info))
                {
                    result.Device = info.Device;
                    result.IsPrimary = (info.Flags & MonitorInfoPrimary) != 0;
                    result.MonitorKnown = true;
                    Line(text, label + "所在屏幕设备名", prefix + ".device", result.Device);
                    Line(text, label + "所在屏幕是否主屏", prefix + ".isPrimary", Boolean(result.IsPrimary));
                }
                else ApiError(text, prefix + ".monitor", "GetMonitorInfoW", Marshal.GetLastWin32Error());
            });
            return result;
        }

        private static void CaptureComparisons(StringBuilder text, List<MonitorSnapshot> monitors, WindowSnapshot main, WindowSnapshot projection)
        {
            var primary = monitors.Find(m => m.IsPrimary);
            Line(text, "Windows 原生主屏设备名", "comparison.primaryDevice", primary?.Device ?? "unknown");
            Line(text, "主窗口在主屏", "comparison.mainOnPrimary", main.MonitorKnown ? Boolean(main.IsPrimary) : "unknown");
            Line(text, "投影窗口在主屏", "comparison.projectionOnPrimary", projection.MonitorKnown ? Boolean(projection.IsPrimary) : "unknown");
            Line(text, "两窗口在同一原生屏幕", "comparison.mainProjectionSameMonitor", main.MonitorKnown && projection.MonitorKnown ? Boolean(main.Monitor == projection.Monitor) : "unknown");
            Line(text, "原生设备映射", "comparison.deviceMapping", "main:" + (main.Device ?? "unknown") + ",projection:" + (projection.Device ?? "unknown") + ",primary:" + (primary?.Device ?? "unknown"));
        }

        private static void CaptureActivePaths(StringBuilder text)
        {
            // Active topology may change between buffer sizing and query (e.g. projector hot-plug).
            // Refresh both buffers and retry only ERROR_INSUFFICIENT_BUFFER, at most three times.
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Line(text, "活动路径查询次数", "queryDisplayConfig.attempt", Number(attempt));
                int error = GetDisplayConfigBufferSizes(QueryOnlyActivePaths, out uint pathCount, out uint modeCount);
                if (error != 0)
                {
                    ApiError(text, "queryDisplayConfig", "GetDisplayConfigBufferSizes", error);
                    return;
                }
                if (pathCount > 1024 || modeCount > 4096)
                {
                    Line(text, "显示路径数量异常", "queryDisplayConfig.error", "buffer safety limit; paths:" + Number(pathCount) + ",modes:" + Number(modeCount));
                    return;
                }
                var paths = new DisplayConfigPathInfo[Math.Max(1, (int)pathCount)];
                var modes = new DisplayConfigModeInfo[Math.Max(1, (int)modeCount)];
                error = QueryDisplayConfig(QueryOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (error == ErrorInsufficientBuffer)
                {
                    ApiError(text, "queryDisplayConfig", "QueryDisplayConfig buffer race", error);
                    continue;
                }
                if (error != 0)
                {
                    ApiError(text, "queryDisplayConfig", "QueryDisplayConfig", error);
                    return;
                }
                if (pathCount > paths.Length || modeCount > modes.Length)
                {
                    Line(text, "显示路径返回数量异常", "queryDisplayConfig.error", "returned counts exceed buffers");
                    return;
                }
                Line(text, "活动显示路径数量", "queryDisplayConfig.pathCount", Number(pathCount));
                Line(text, "活动显示模式数量", "queryDisplayConfig.modeCount", Number(modeCount));
                for (int i = 0; i < (int)pathCount; i++)
                {
                    var path = paths[i];
                    string prefix = "displayPath[" + Number(i) + "]";
                    Section(text, prefix, () => CapturePath(text, prefix, path, modes, modeCount));
                }
                return;
            }
            Line(text, "活动路径查询停止", "queryDisplayConfig.error", "ERROR_INSUFFICIENT_BUFFER after 3 attempts; topology changing; other snapshot sections retained");
        }

        private static void CapturePath(StringBuilder text, string prefix, DisplayConfigPathInfo path, DisplayConfigModeInfo[] modes, uint modeCount)
        {
            Line(text, "活动路径标记", prefix + ".flags", "0x" + path.Flags.ToString("X", Invariant));
            Line(text, "源显示身份", prefix + ".source", "adapterLuid:" + LuidText(path.Source.AdapterId) + ",id:" + Number(path.Source.Id) + ",modeIndex:" + Number(path.Source.ModeInfoIndex) + ",statusFlags:" + Number(path.Source.StatusFlags));
            Line(text, "目标显示身份", prefix + ".target", "adapterLuid:" + LuidText(path.Target.AdapterId) + ",id:" + Number(path.Target.Id) + ",modeIndex:" + Number(path.Target.ModeInfoIndex) + ",available:" + Boolean(path.Target.TargetAvailable != 0) + ",statusFlags:" + Number(path.Target.StatusFlags));
            Line(text, "目标输出连接技术", prefix + ".outputTechnology", OutputTechnology(path.Target.OutputTechnology));
            Line(text, "目标旋转/缩放/刷新率", prefix + ".targetMode", "rotation:" + Number(path.Target.Rotation) + ",scaling:" + Number(path.Target.Scaling) + ",refreshNumerator:" + Number(path.Target.RefreshRate.Numerator) + ",refreshDenominator:" + Number(path.Target.RefreshRate.Denominator) + ",scanLineOrdering:" + Number(path.Target.ScanLineOrdering));
            uint modeIndex = path.Source.ModeInfoIndex;
            if (modeIndex != InvalidModeIndex && modeIndex < modeCount && modes[modeIndex].InfoType == 1 && modes[modeIndex].Id == path.Source.Id && SameLuid(modes[modeIndex].AdapterId, path.Source.AdapterId))
            {
                var mode = modes[modeIndex].Mode.SourceMode;
                Line(text, "源桌面位置/尺寸", prefix + ".sourceMode", "x:" + Number(mode.Position.X) + ",y:" + Number(mode.Position.Y) + ",width:" + Number(mode.Width) + ",height:" + Number(mode.Height) + ",pixelFormat:" + Number(mode.PixelFormat));
            }
            else Line(text, "源桌面模式状态", prefix + ".sourceMode", "unavailable-or-invalid; see fresh native monitor bounds");
            Section(text, prefix + ".sourceName", () =>
            {
                var source = new DisplayConfigSourceDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader { Type = 1, Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(), AdapterId = path.Source.AdapterId, Id = path.Source.Id }
                };
                int error = DisplayConfigGetSourceDeviceInfo(ref source);
                if (error == 0) Line(text, "源 Windows 屏幕设备名", prefix + ".sourceGdiDeviceName", source.ViewGdiDeviceName);
                else ApiError(text, prefix + ".sourceName", "DisplayConfigGetDeviceInfo(GET_SOURCE_NAME)", error);
            });
            Section(text, prefix + ".targetName", () =>
            {
                var target = new DisplayConfigTargetDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader { Type = 2, Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(), AdapterId = path.Target.AdapterId, Id = path.Target.Id }
                };
                int error = DisplayConfigGetTargetDeviceInfo(ref target);
                if (error != 0)
                {
                    ApiError(text, prefix + ".targetName", "DisplayConfigGetDeviceInfo(GET_TARGET_NAME)", error);
                    return;
                }
                Line(text, "目标显示器友好名称", prefix + ".targetFriendlyName", string.IsNullOrWhiteSpace(target.MonitorFriendlyDeviceName) ? "unavailable" : target.MonitorFriendlyDeviceName);
                Line(text, "目标名称输出连接技术", prefix + ".targetNameOutputTechnology", OutputTechnology(target.OutputTechnology));
                Line(text, "目标名称状态标记", prefix + ".targetNameFlags", "0x" + target.Flags.ToString("X", Invariant));
                // Do not log MonitorDevicePath or EDID IDs: unnecessary for mapping and may identify hardware.
            });
        }

        private static string OutputTechnology(uint value)
        {
            string name = value switch
            {
                0xffffffff => "OTHER", 0 => "HD15", 1 => "SVIDEO", 2 => "COMPOSITE_VIDEO", 3 => "COMPONENT_VIDEO",
                4 => "DVI", 5 => "HDMI", 6 => "LVDS (embedded panel)", 8 => "D_JPN", 9 => "SDI",
                10 => "DISPLAYPORT_EXTERNAL", 11 => "DISPLAYPORT_EMBEDDED", 12 => "UDI_EXTERNAL", 13 => "UDI_EMBEDDED",
                14 => "SDTVDONGLE", 15 => "MIRACAST", 16 => "INDIRECT_WIRED", 17 => "INDIRECT_VIRTUAL",
                18 => "DISPLAYPORT_USB_TUNNEL", 0x80000000 => "INTERNAL (embedded panel)", _ => "UNKNOWN"
            };
            return name + "(" + Number(value) + ")";
        }

        private static bool SameLuid(AdapterLuid first, AdapterLuid second) => first.LowPart == second.LowPart && first.HighPart == second.HighPart;
        private static string LuidText(AdapterLuid value) => unchecked((uint)value.HighPart).ToString("X8", Invariant) + ":" + value.LowPart.ToString("X8", Invariant);
        private static string Boolean(bool value) => value ? "true" : "false";
        private static string Number(int value) => value.ToString(Invariant);
        private static string Number(uint value) => value.ToString(Invariant);
        private static string Number(double value) => value.ToString("R", Invariant);
        private static string Hex(IntPtr value) => "0x" + unchecked((ulong)value.ToInt64()).ToString("X", Invariant);
        private static string Rect(NativeRect value) => "left:" + Number(value.Left) + ",top:" + Number(value.Top) + ",right:" + Number(value.Right) + ",bottom:" + Number(value.Bottom) + ",width:" + ((long)value.Right - value.Left).ToString(Invariant) + ",height:" + ((long)value.Bottom - value.Top).ToString(Invariant);
        private static void Line(StringBuilder text, string label, string key, string value) => text.Append(label).Append(" | ").Append(key).Append('=').Append((value ?? "unknown").Replace("\r", " ").Replace("\n", " ")).AppendLine();
        private static void ApiError(StringBuilder text, string prefix, string api, int error) => Line(text, "原生 API 错误", prefix + ".error", api + "; code=" + Number(error) + "; hex=0x" + unchecked((uint)error).ToString("X8", Invariant));
        private static void Section(StringBuilder text, string name, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                // Type + HRESULT avoids accidental user content in an arbitrary exception message.
                Line(text, "诊断区段失败（其他区段继续）", name + ".error", ex.GetType().Name + "; hresult=0x" + unchecked((uint)ex.HResult).ToString("X8", Invariant));
            }
        }

        private sealed class MonitorSnapshot
        {
            public IntPtr Handle;
            public string Device;
            public bool IsPrimary;
        }
        private sealed class WindowSnapshot
        {
            public IntPtr Handle;
            public IntPtr Monitor;
            public string Device;
            public bool IsPrimary;
            public bool MonitorKnown;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public uint Size;
            public NativeRect Monitor, Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct AdapterLuid { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Rational { public uint Numerator, Denominator; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigPathSourceInfo
        {
            public AdapterLuid AdapterId;
            public uint Id, ModeInfoIndex, StatusFlags;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigPathTargetInfo
        {
            public AdapterLuid AdapterId;
            public uint Id, ModeInfoIndex, OutputTechnology, Rotation, Scaling;
            public Rational RefreshRate;
            public uint ScanLineOrdering;
            public int TargetAvailable; // Native BOOL occupies four bytes.
            public uint StatusFlags;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigPathInfo
        {
            public DisplayConfigPathSourceInfo Source;
            public DisplayConfigPathTargetInfo Target;
            public uint Flags;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigSourceMode
        {
            public uint Width, Height, PixelFormat;
            public NativePoint Position;
        }
        // DISPLAYCONFIG_MODE_INFO union is 48 bytes (largest member is target video signal).
        [StructLayout(LayoutKind.Explicit, Size = 48)]
        private struct DisplayConfigModeUnion { [FieldOffset(0)] public DisplayConfigSourceMode SourceMode; }
        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigModeInfo
        {
            public uint InfoType, Id;
            public AdapterLuid AdapterId;
            public DisplayConfigModeUnion Mode;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct DisplayConfigDeviceInfoHeader
        {
            public uint Type, Size;
            public AdapterLuid AdapterId;
            public uint Id;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayConfigSourceDeviceName
        {
            public DisplayConfigDeviceInfoHeader Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayConfigTargetDeviceName
        {
            public DisplayConfigDeviceInfoHeader Header;
            public uint Flags, OutputTechnology;
            public ushort EdidManufactureId, EdidProductCodeId;
            public uint ConnectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths, ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, IntPtr topologyId);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", CharSet = CharSet.Unicode)]
        private static extern int DisplayConfigGetSourceDeviceInfo(ref DisplayConfigSourceDeviceName packet);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", CharSet = CharSet.Unicode)]
        private static extern int DisplayConfigGetTargetDeviceInfo(ref DisplayConfigTargetDeviceName packet);
    }
}
