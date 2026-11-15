using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ImageColorChanger.Managers
{
    /// <summary>只读Windows活动显示路径，按source GDI身份取target设备友好名；无日志/事件/系统设置写入。</summary>
    public static class MonitorDisplayNameResolver
    {
        public static void ApplyTo(IReadOnlyList<WpfScreenInfo> screens)
        {
            var names = GetActiveNames();
            foreach (var screen in screens)
                screen.FriendlyName = screen.DeviceName != null && names.TryGetValue(screen.DeviceName, out string name) ? name : null;
        }

        public static Dictionary<string, string> GetActiveNames()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (GetDisplayConfigBufferSizes(2, out uint pathCount, out uint modeCount) != 0 || pathCount > 1024 || modeCount > 4096) return names;
                    var paths = new PathInfo[Math.Max(1, (int)pathCount)];
                    var modes = new ModeInfo[Math.Max(1, (int)modeCount)];
                    int error = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                    if (error == 122) continue; // 热插拔导致buffer race，只重新查询，最多三次。
                    if (error != 0 || pathCount > paths.Length || modeCount > modes.Length) return names;
                    for (int i = 0; i < pathCount; i++)
                    {
                        var source = new SourceName { Header = new Header { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(), AdapterId = paths[i].Source.AdapterId, Id = paths[i].Source.Id } };
                        var target = new TargetName { Header = new Header { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), AdapterId = paths[i].Target.AdapterId, Id = paths[i].Target.Id } };
                        if (GetSourceName(ref source) != 0 || GetTargetName(ref target) != 0 || string.IsNullOrWhiteSpace(source.GdiName)) continue;
                        string friendly = ProjectionScreenLabel.NormalizeName(target.FriendlyName);
                        if (string.IsNullOrEmpty(friendly)) continue;
                        // 镜像路径可能共用source；显示组合名，不把某块target冒充另一块。
                        if (!names.TryGetValue(source.GdiName, out string existing)) names[source.GdiName] = friendly;
                        else if (!string.Equals(existing, friendly, StringComparison.OrdinalIgnoreCase)) names[source.GdiName] = existing + " / " + friendly;
                    }
                    return names;
                }
            }
            catch (Exception) { /* 远程/旧驱动/不可用API仅回退显示编号，不阻断投影。 */ }
            return names;
        }

        [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator, Denominator; }
        [StructLayout(LayoutKind.Sequential)] private struct SourceInfo { public Luid AdapterId; public uint Id, ModeIndex, Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct TargetInfo
        {
            public Luid AdapterId;
            public uint Id, ModeIndex, Technology, Rotation, Scaling;
            public Rational RefreshRate;
            public uint ScanLine;
            public int Available;
            public uint Flags;
        }
        [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
        [StructLayout(LayoutKind.Explicit, Size = 48)] private struct ModeUnion { [FieldOffset(0)] public uint Reserved; }
        [StructLayout(LayoutKind.Sequential)] private struct ModeInfo { public uint Type, Id; public Luid AdapterId; public ModeUnion Mode; }
        [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public Luid AdapterId; public uint Id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName
        {
            public Header Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
        {
            public Header Header;
            public uint Flags, Technology;
            public ushort Manufacturer, Product;
            public uint Connector;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
        }
        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] pathBuffer, ref uint modes, [Out] ModeInfo[] modeBuffer, IntPtr topology);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", CharSet = CharSet.Unicode)] private static extern int GetSourceName(ref SourceName name);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", CharSet = CharSet.Unicode)] private static extern int GetTargetName(ref TargetName name);
    }
}
