using System;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 圣经经文插入配置
    /// </summary>
    public class BibleTextInsertConfig
    {
        private int _popupDurationMinutes = 3;
        private int _popupVerseCount = 3;

        /// <summary>
        /// 样式布局（默认：标题在上面）
        /// </summary>
        public BibleTextInsertStyle Style { get; set; } = BibleTextInsertStyle.TitleOnTop;

        /// <summary>
        /// 统一字体（标题和经文共用，默认：等线）
        /// </summary>
        public string FontFamily { get; set; } = "DengXian";

        /// <summary>
        /// 标题样式
        /// </summary>
        public BibleTitleStyle TitleStyle { get; set; } = new BibleTitleStyle();

        /// <summary>
        /// 经文样式
        /// </summary>
        public BibleVerseStyle VerseStyle { get; set; } = new BibleVerseStyle();

        /// <summary>
        /// 节号样式
        /// </summary>
        public BibleVerseNumberStyle VerseNumberStyle { get; set; } = new BibleVerseNumberStyle();

        /// <summary>
        /// 高级选项 - 插入后自动隐藏导航栏
        /// </summary>
        public bool AutoHideNavigationAfterInsert { get; set; } = true;

        /// <summary>
        /// 弹窗经文位置
        /// </summary>
        public BiblePopupPosition PopupPosition { get; set; } = BiblePopupPosition.Bottom;

        /// <summary>
        /// 弹窗统一字体（标题和经文共用）
        /// </summary>
        public string PopupFontFamily { get; set; } = "DengXian";

        /// <summary>
        /// 弹窗标题样式
        /// </summary>
        public BibleTitleStyle PopupTitleStyle { get; set; } = new BibleTitleStyle();

        /// <summary>
        /// 弹窗经文样式
        /// </summary>
        public BibleVerseStyle PopupVerseStyle { get; set; } = new BibleVerseStyle();

        /// <summary>
        /// 弹窗节号样式
        /// </summary>
        public BibleVerseNumberStyle PopupVerseNumberStyle { get; set; } = new BibleVerseNumberStyle();

        /// <summary>
        /// 弹窗背景色（不含透明度）
        /// </summary>
        public string PopupBackgroundColorHex { get; set; } = "#000000";

        /// <summary>
        /// 弹窗背景透明度（0=不透明，100=完全透明）
        /// </summary>
        public int PopupBackgroundOpacity { get; set; } = 0;

        /// <summary>
        /// 弹窗自动关闭时间（分钟，范围 1-10，默认 3）
        /// </summary>
        public int PopupDurationMinutes
        {
            get => _popupDurationMinutes;
            set => _popupDurationMinutes = Math.Clamp(value, 1, 10);
        }

        /// <summary>
        /// 弹窗显示节数（范围 1-10，默认 3）
        /// </summary>
        public int PopupVerseCount
        {
            get => _popupVerseCount;
            set => _popupVerseCount = Math.Clamp(value, 1, 10);
        }
    }

    /// <summary>
    /// 标题样式
    /// </summary>
    public class BibleTitleStyle
    {
        /// <summary>
        /// 颜色（默认：红色 #FF0000）
        /// </summary>
        public string ColorHex { get; set; } = "#FF0000";

        /// <summary>
        /// 字体大小（默认：50）
        /// </summary>
        public float FontSize { get; set; } = 50f;
        
        /// <summary>
        /// 是否粗体（默认：true）
        /// </summary>
        public bool IsBold { get; set; } = true;
        
        /// <summary>
        /// 获取 SKColor
        /// </summary>
        public SKColor GetSKColor()
        {
            try
            {
                return SKColor.Parse(ColorHex);
            }
            catch
            {
                return SKColor.Parse("#FF0000"); // 默认红色
            }
        }
        
        /// <summary>
        /// 设置 SKColor
        /// </summary>
        public void SetSKColor(SKColor color)
        {
            ColorHex = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        }
    }

    /// <summary>
    /// 经文样式
    /// </summary>
    public class BibleVerseStyle
    {
        /// <summary>
        /// 颜色（默认：橙色 #FF9A35）
        /// </summary>
        public string ColorHex { get; set; } = "#FF9A35";

        /// <summary>
        /// 字体大小（默认：36）
        /// </summary>
        public float FontSize { get; set; } = 36f;

        /// <summary>
        /// 是否粗体（默认：false）
        /// </summary>
        public bool IsBold { get; set; } = false;

        /// <summary>
        /// 节距（行间距倍数，默认：1.2，范围 1.0-2.5）
        /// </summary>
        public float VerseSpacing { get; set; } = 1.2f;

        /// <summary>
        /// 获取 SKColor
        /// </summary>
        public SKColor GetSKColor()
        {
            try
            {
                return SKColor.Parse(ColorHex);
            }
            catch
            {
                return SKColor.Parse("#FF9A35"); // 默认橙色
            }
        }

        /// <summary>
        /// 设置 SKColor
        /// </summary>
        public void SetSKColor(SKColor color)
        {
            ColorHex = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        }
    }

    /// <summary>
    /// 节号样式
    /// </summary>
    public class BibleVerseNumberStyle
    {
        /// <summary>
        /// 颜色（默认：黄色 #FFFF00）
        /// </summary>
        public string ColorHex { get; set; } = "#FFFF00";

        /// <summary>
        /// 字体大小（默认：40）
        /// </summary>
        public float FontSize { get; set; } = 40f;

        /// <summary>
        /// 是否粗体（默认：true）
        /// </summary>
        public bool IsBold { get; set; } = true;

        /// <summary>
        /// 获取 SKColor
        /// </summary>
        public SKColor GetSKColor()
        {
            try
            {
                return SKColor.Parse(ColorHex);
            }
            catch
            {
                return SKColor.Parse("#FFFF00"); // 默认黄色
            }
        }

        /// <summary>
        /// 设置 SKColor
        /// </summary>
        public void SetSKColor(SKColor color)
        {
            ColorHex = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        }
    }

    /// <summary>
    /// 样式布局枚举
    /// </summary>
    public enum BibleTextInsertStyle
    {
        /// <summary>
        /// 标题在上面
        /// </summary>
        TitleOnTop = 0,

        /// <summary>
        /// 标题在下面
        /// </summary>
        TitleAtBottom = 1,

        /// <summary>
        /// 标注在末尾（默认）
        /// </summary>
        InlineAtEnd = 2
    }

    /// <summary>
    /// 圣经弹窗位置
    /// </summary>
    public enum BiblePopupPosition
    {
        Top = 0,
        Center = 1,
        Bottom = 2
    }
}

