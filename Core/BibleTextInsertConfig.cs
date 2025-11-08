using System;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 圣经经文插入配置
    /// </summary>
    public class BibleTextInsertConfig
    {
        /// <summary>
        /// 样式布局（默认：标题在上面）
        /// </summary>
        public BibleTextInsertStyle Style { get; set; } = BibleTextInsertStyle.TitleOnTop;
        
        /// <summary>
        /// 统一字体（标题和经文共用，默认：微软雅黑）
        /// </summary>
        public string FontFamily { get; set; } = "微软雅黑";
        
        /// <summary>
        /// 标题样式
        /// </summary>
        public BibleTitleStyle TitleStyle { get; set; } = new BibleTitleStyle();
        
        /// <summary>
        /// 经文样式
        /// </summary>
        public BibleVerseStyle VerseStyle { get; set; } = new BibleVerseStyle();
        
        /// <summary>
        /// 高级选项 - 插入后自动隐藏导航栏
        /// </summary>
        public bool AutoHideNavigationAfterInsert { get; set; } = true;
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
        /// 字体大小
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
        /// 颜色（默认：棕黄色 #D2691E）
        /// </summary>
        public string ColorHex { get; set; } = "#D2691E";
        
        /// <summary>
        /// 字体大小
        /// </summary>
        public float FontSize { get; set; } = 30f;
        
        /// <summary>
        /// 是否粗体（默认：false）
        /// </summary>
        public bool IsBold { get; set; } = false;
        
        /// <summary>
        /// 节距（默认：10，每节之间的间距）
        /// </summary>
        public float VerseSpacing { get; set; } = 10f;
        
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
                return SKColor.Parse("#D2691E"); // 默认棕黄色
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
}

