using System;

namespace ImageColorChanger.Services.TextEditor.Components.Notice
{
    public enum NoticeDirection
    {
        LeftToRight = 0,
        RightToLeft = 1,
        PingPong = 2
    }

    public enum NoticePosition
    {
        Top = 0,
        Center = 1,
        Bottom = 2
    }

    [Flags]
    public enum NoticePositionFlags
    {
        None = 0,
        Top = 1 << 0,
        Center = 1 << 1,
        Bottom = 1 << 2,
        All = Top | Center | Bottom
    }

    public sealed class NoticeComponentConfig
    {
        public const string DefaultNoticeColorHex = "#FF8A00";
        public const int MinBarHeightLevel = 1;
        public const int MaxBarHeightLevel = 4;

        private int _speed = 45;
        private int _durationMinutes = 3;
        private string _defaultColorHex = DefaultNoticeColorHex;
        private double _barHeight = 120;
        private NoticePositionFlags _positionFlags = NoticePositionFlags.Top;

        /// <summary>
        /// 是否开启滚动。
        /// </summary>
        public bool ScrollingEnabled { get; set; } = false;

        public NoticePosition Position { get; set; } = NoticePosition.Top;

        /// <summary>
        /// 位置多选（顶部/居中/底部可同时选）。
        /// </summary>
        public NoticePositionFlags PositionFlags
        {
            get => NormalizePositionFlags(_positionFlags);
            set => _positionFlags = NormalizePositionFlags(value);
        }

        public NoticeDirection Direction { get; set; } = NoticeDirection.LeftToRight;

        /// <summary>
        /// 滚动速度，范围 0-100。
        /// </summary>
        public int Speed
        {
            get => _speed;
            set => _speed = Math.Clamp(value, 0, 100);
        }

        /// <summary>
        /// 自动关闭时长（分钟），范围 1-10。
        /// </summary>
        public int DurationMinutes
        {
            get => _durationMinutes;
            set => _durationMinutes = Math.Clamp(value, 1, 10);
        }

        /// <summary>
        /// 通知默认背景色（#RRGGBB）。
        /// </summary>
        public string DefaultColorHex
        {
            get => string.IsNullOrWhiteSpace(_defaultColorHex) ? DefaultNoticeColorHex : _defaultColorHex;
            set => _defaultColorHex = string.IsNullOrWhiteSpace(value) ? DefaultNoticeColorHex : value.Trim();
        }

        /// <summary>
        /// 通知背景条高度（像素）。
        /// </summary>
        public double BarHeight
        {
            get => _barHeight;
            set => _barHeight = Math.Clamp(value, 40, 320);
        }

        public bool AutoClose { get; set; } = true;

        /// <summary>
        /// 滚动调试开关（输出滚动关键指标日志）。
        /// </summary>
        public bool DebugEnabled { get; set; } = false;

        public static NoticePositionFlags NormalizePositionFlags(NoticePositionFlags flags)
        {
            var normalized = flags & NoticePositionFlags.All;
            return normalized == NoticePositionFlags.None ? NoticePositionFlags.Top : normalized;
        }

        public static NoticePositionFlags ToFlags(NoticePosition position)
        {
            return position switch
            {
                NoticePosition.Center => NoticePositionFlags.Center,
                NoticePosition.Bottom => NoticePositionFlags.Bottom,
                _ => NoticePositionFlags.Top
            };
        }

        public static NoticePosition GetPrimaryPosition(NoticePositionFlags flags)
        {
            var normalized = NormalizePositionFlags(flags);
            if ((normalized & NoticePositionFlags.Top) == NoticePositionFlags.Top)
            {
                return NoticePosition.Top;
            }

            if ((normalized & NoticePositionFlags.Center) == NoticePositionFlags.Center)
            {
                return NoticePosition.Center;
            }

            return NoticePosition.Bottom;
        }

        public static bool HasPosition(NoticePositionFlags flags, NoticePosition position)
        {
            var normalized = NormalizePositionFlags(flags);
            return position switch
            {
                NoticePosition.Center => (normalized & NoticePositionFlags.Center) == NoticePositionFlags.Center,
                NoticePosition.Bottom => (normalized & NoticePositionFlags.Bottom) == NoticePositionFlags.Bottom,
                _ => (normalized & NoticePositionFlags.Top) == NoticePositionFlags.Top
            };
        }

        public static double GetBarHeightByLevel(int level)
        {
            return level switch
            {
                1 => 120,
                2 => 160,
                3 => 200,
                4 => 240,
                _ => 120
            };
        }

        public static int GetBarHeightLevel(double height)
        {
            double clamped = Math.Clamp(height, 40, 320);
            double[] candidates =
            {
                GetBarHeightByLevel(1),
                GetBarHeightByLevel(2),
                GetBarHeightByLevel(3),
                GetBarHeightByLevel(4)
            };

            int bestLevel = 1;
            double minDistance = double.MaxValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                double distance = Math.Abs(candidates[i] - clamped);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    bestLevel = i + 1;
                }
            }

            return Math.Clamp(bestLevel, MinBarHeightLevel, MaxBarHeightLevel);
        }
    }
}
