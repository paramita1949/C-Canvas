using System;
using System.Linq;

namespace ImageColorChanger.Managers
{
    /// <summary>仅显示映射。设备身份仍是DeviceName，不能从此字符串反推选屏。</summary>
    public static class ProjectionScreenLabel
    {
        public static string GetLabel(WpfScreenInfo screen, int index)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            string friendly = NormalizeName(screen.FriendlyName);
            string name = string.IsNullOrEmpty(friendly) ? $"显示器{index + 1}" : friendly;
            return name + (screen.IsPrimary ? "（主显示器）" : "（扩展显示器）");
        }

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            string printable = new string(name.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            return string.Join(" ", printable.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
