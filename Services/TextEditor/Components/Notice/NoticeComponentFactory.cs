using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Services.TextEditor.Components.Notice
{
    public static class NoticeComponentFactory
    {
        public const string NoticeComponentType = "Notice";
        public const string DefaultNoticeBackgroundColor = "#FF8A00";
        public const int DefaultNoticeBackgroundOpacity = 20;
        public const string DefaultNoticeContent = "请输入通知内容";

        public static TextElement BuildDefault(
            int slideId,
            int canvasWidth,
            int canvasHeight,
            int zIndex,
            string defaultTextColor,
            NoticeComponentConfig defaultConfig = null)
        {
            const double defaultX = 0;
            double width = canvasWidth > 0 ? canvasWidth : 600;
            double effectiveCanvasHeight = canvasHeight > 0 ? canvasHeight : 900;

            var cfg = NoticeComponentConfigCodec.Normalize(defaultConfig);
            cfg.ScrollingEnabled = false;
            double height = cfg.BarHeight;
            cfg.Position = NoticeComponentConfig.GetPrimaryPosition(cfg.PositionFlags);
            double defaultY = cfg.Position switch
            {
                NoticePosition.Center => System.Math.Max(0, (effectiveCanvasHeight - height) / 2.0),
                NoticePosition.Bottom => System.Math.Max(0, effectiveCanvasHeight - height),
                _ => 0
            };
            string textAlign = cfg.Direction == NoticeDirection.RightToLeft ? "Right" : "Left";

            return new TextElement
            {
                SlideId = slideId,
                X = defaultX,
                Y = defaultY,
                Width = width,
                Height = height,
                ZIndex = zIndex,
                Content = DefaultNoticeContent,
                FontFamily = "Microsoft YaHei UI",
                FontSize = 54,
                FontColor = string.IsNullOrWhiteSpace(defaultTextColor) ? "#FFFFFF" : defaultTextColor,
                TextAlign = textAlign,
                TextVerticalAlign = "Middle",
                BackgroundColor = cfg.DefaultColorHex,
                BackgroundOpacity = DefaultNoticeBackgroundOpacity,
                ComponentType = NoticeComponentType,
                ComponentConfigJson = NoticeComponentConfigCodec.Serialize(cfg)
            };
        }
    }
}
