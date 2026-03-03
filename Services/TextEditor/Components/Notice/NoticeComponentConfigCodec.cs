using System;
using System.Text.Json;

namespace ImageColorChanger.Services.TextEditor.Components.Notice
{
    public static class NoticeComponentConfigCodec
    {
        private static readonly JsonSerializerOptions DeserializeOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static string Serialize(NoticeComponentConfig cfg)
        {
            return JsonSerializer.Serialize(Normalize(cfg));
        }

        public static NoticeComponentConfig Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new NoticeComponentConfig();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<NoticeComponentConfig>(json, DeserializeOptions);
                return Normalize(parsed);
            }
            catch
            {
                return new NoticeComponentConfig();
            }
        }

        public static NoticeComponentConfig Normalize(NoticeComponentConfig cfg)
        {
            cfg ??= new NoticeComponentConfig();

            var positionFlags = NormalizePositionFlags(cfg);
            return new NoticeComponentConfig
            {
                ScrollingEnabled = cfg.ScrollingEnabled,
                Position = NoticeComponentConfig.GetPrimaryPosition(positionFlags),
                PositionFlags = positionFlags,
                Direction = IsDefinedDirection(cfg.Direction) ? cfg.Direction : NoticeDirection.LeftToRight,
                Speed = cfg.Speed,
                DurationMinutes = cfg.DurationMinutes,
                DefaultColorHex = NormalizeColorHex(cfg.DefaultColorHex),
                BarHeight = cfg.BarHeight,
                AutoClose = cfg.AutoClose,
                DebugEnabled = cfg.DebugEnabled
            };
        }

        private static bool IsDefinedDirection(NoticeDirection direction)
        {
            return direction == NoticeDirection.LeftToRight
                || direction == NoticeDirection.RightToLeft
                || direction == NoticeDirection.PingPong;
        }

        private static bool IsDefinedPosition(NoticePosition position)
        {
            return position == NoticePosition.Top
                || position == NoticePosition.Center
                || position == NoticePosition.Bottom;
        }

        private static NoticePositionFlags NormalizePositionFlags(NoticeComponentConfig cfg)
        {
            if (cfg == null)
            {
                return NoticePositionFlags.Top;
            }

            var flags = NoticeComponentConfig.NormalizePositionFlags(cfg.PositionFlags);
            if (flags == NoticePositionFlags.Top && IsDefinedPosition(cfg.Position) && cfg.Position != NoticePosition.Top)
            {
                // 兼容旧配置：无 flags 时沿用单选 Position。
                flags = NoticeComponentConfig.ToFlags(cfg.Position);
            }

            return flags;
        }

        private static string NormalizeColorHex(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return NoticeComponentConfig.DefaultNoticeColorHex;
            }

            string trimmed = color.Trim();
            if (!trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                trimmed = "#" + trimmed;
            }

            if (trimmed.Length == 7 || trimmed.Length == 9)
            {
                return trimmed.ToUpperInvariant();
            }

            return NoticeComponentConfig.DefaultNoticeColorHex;
        }
    }
}
