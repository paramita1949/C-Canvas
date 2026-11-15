using System;
using System.Collections.Generic;
using System.Windows;

namespace ImageColorChanger.Managers
{
    /// <summary>投影目标身份与物理坐标规则。显示编号/左右位置/DPI不参与设备身份判断。</summary>
    public static class ProjectionPlacementPolicy
    {
        public static Rect GetPhysicalBounds(WpfScreenInfo screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            var r = screen.PhysicalBounds;
            if (r.IsEmpty || !IsInteger(r.X) || !IsInteger(r.Y) || !IsInteger(r.Width) || !IsInteger(r.Height)
                || r.Width <= 0 || r.Height <= 0 || r.Right > int.MaxValue || r.Bottom > int.MaxValue)
                throw new ArgumentException("显示器物理矩形无效。", nameof(screen));
            // 原点和尺寸均来自native虚拟桌面，不用目标DPI除绝对坐标。
            return r;
        }

        private static bool IsInteger(double n) => double.IsFinite(n) && n >= int.MinValue && n <= int.MaxValue && n == Math.Truncate(n);

        public static int ResolveSelectedIndex(IReadOnlyList<WpfScreenInfo> cached, int selectedIndex, IReadOnlyList<WpfScreenInfo> fresh)
        {
            if (cached == null || fresh == null || selectedIndex < 0 || selectedIndex >= cached.Count) return -1;
            string device = cached[selectedIndex]?.DeviceName;
            if (string.IsNullOrWhiteSpace(device)) return -1;
            int found = -1;
            for (int i = 0; i < fresh.Count; i++)
            {
                if (!string.Equals(device, fresh[i]?.DeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                if (found >= 0) return -1; // 重复身份有歧义，不能误选。
                found = i;
            }
            return found;
        }
    }
}
