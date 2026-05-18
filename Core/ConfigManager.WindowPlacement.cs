using System;
using System.Collections.Generic;

namespace ImageColorChanger.Core
{
    public sealed class WindowPlacementState
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsCollapsed { get; set; }
    }

    public partial class ConfigManager
    {
        public bool TryGetWindowPlacement(string key, out WindowPlacementState state)
        {
            state = null;
            string normalizedKey = NormalizeWindowPlacementKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return false;
            }

            var placements = _config.WindowPlacements ?? new Dictionary<string, WindowPlacementState>(StringComparer.Ordinal);
            if (!placements.TryGetValue(normalizedKey, out WindowPlacementState saved) || saved == null)
            {
                return false;
            }

            if (!IsFinite(saved.Left) ||
                !IsFinite(saved.Top) ||
                !IsFinite(saved.Width) ||
                !IsFinite(saved.Height) ||
                saved.Width <= 0 ||
                saved.Height <= 0)
            {
                return false;
            }

            state = new WindowPlacementState
            {
                Left = saved.Left,
                Top = saved.Top,
                Width = saved.Width,
                Height = saved.Height,
                IsCollapsed = saved.IsCollapsed
            };
            return true;
        }

        public void SetWindowPlacement(
            string key,
            double left,
            double top,
            double width,
            double height,
            bool isCollapsed = false)
        {
            string normalizedKey = NormalizeWindowPlacementKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey) ||
                !IsFinite(left) ||
                !IsFinite(top) ||
                !IsFinite(width) ||
                !IsFinite(height) ||
                width <= 0 ||
                height <= 0)
            {
                return;
            }

            _config.WindowPlacements ??= new Dictionary<string, WindowPlacementState>(StringComparer.Ordinal);
            if (!_config.WindowPlacements.TryGetValue(normalizedKey, out WindowPlacementState current) || current == null)
            {
                current = new WindowPlacementState();
                _config.WindowPlacements[normalizedKey] = current;
            }

            bool changed =
                Math.Abs(current.Left - left) > 0.1 ||
                Math.Abs(current.Top - top) > 0.1 ||
                Math.Abs(current.Width - width) > 0.1 ||
                Math.Abs(current.Height - height) > 0.1 ||
                current.IsCollapsed != isCollapsed;

            if (!changed)
            {
                return;
            }

            current.Left = left;
            current.Top = top;
            current.Width = Math.Max(1, width);
            current.Height = Math.Max(1, height);
            current.IsCollapsed = isCollapsed;
            SaveConfig();
        }

        private static string NormalizeWindowPlacementKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public partial class AppConfig
    {
        public Dictionary<string, WindowPlacementState> WindowPlacements { get; set; } = new();
    }
}
