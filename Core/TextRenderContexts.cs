using System;
using System.Collections.Generic;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 文本框渲染上下文
    /// </summary>
    public class TextBoxRenderContext
    {
        /// <summary>
        /// 文本内容
        /// </summary>
        public string Text { get; set; } = string.Empty;
        
        /// <summary>
        /// 文本框尺寸
        /// </summary>
        public SKSize Size { get; set; }
        
        /// <summary>
        /// 文本样式
        /// </summary>
        public TextStyle Style { get; set; } = new TextStyle();
        
        /// <summary>
        /// 背景颜色（可选，null表示透明）
        /// </summary>
        public SKColor? BackgroundColor { get; set; }
        
        /// <summary>
        /// 边距 (Left, Top, Right, Bottom)
        /// </summary>
        public SKRect Padding { get; set; }
        
        /// <summary>
        /// 文本对齐方式
        /// </summary>
        public SKTextAlign Alignment { get; set; } = SKTextAlign.Left;
        
        /// <summary>
        /// 生成缓存键（用于渲染缓存）
        /// </summary>
        public string GetCacheKey()
        {
            return $"TB_{Text}_{Size.Width}_{Size.Height}_{Style.FontSize}_{Style.TextColor}_{Style.FontFamily}_{Style.IsBold}";
        }
    }
    
    /// <summary>
    /// 圣经经文渲染上下文
    /// </summary>
    public class BibleRenderContext
    {
        /// <summary>
        /// 经文列表
        /// </summary>
        public List<BibleVerseItem> Verses { get; set; } = new List<BibleVerseItem>();
        
        /// <summary>
        /// 标题样式
        /// </summary>
        public TextStyle TitleStyle { get; set; } = new TextStyle();
        
        /// <summary>
        /// 经文样式
        /// </summary>
        public TextStyle VerseStyle { get; set; } = new TextStyle();
        
        /// <summary>
        /// 节号样式
        /// </summary>
        public TextStyle VerseNumberStyle { get; set; } = new TextStyle();
        
        /// <summary>
        /// 渲染尺寸
        /// </summary>
        public SKSize Size { get; set; }
        
        /// <summary>
        /// 背景颜色
        /// </summary>
        public SKColor BackgroundColor { get; set; } = SKColors.Black;
        
        /// <summary>
        /// 边距 (Left, Top, Right, Bottom)
        /// </summary>
        public SKRect Padding { get; set; }
        
        /// <summary>
        /// 节间距（像素）
        /// </summary>
        public float VerseSpacing { get; set; } = 8f;
        
        /// <summary>
        /// 高亮颜色
        /// </summary>
        public SKColor HighlightColor { get; set; } = SKColors.Yellow;
    }
    
    /// <summary>
    /// 歌词渲染上下文
    /// </summary>
    public class LyricsRenderContext
    {
        /// <summary>
        /// 歌词文本
        /// </summary>
        public string Text { get; set; } = string.Empty;
        
        /// <summary>
        /// 渲染尺寸
        /// </summary>
        public SKSize Size { get; set; }
        
        /// <summary>
        /// 文本样式
        /// </summary>
        public TextStyle Style { get; set; } = new TextStyle();
        
        /// <summary>
        /// 文本对齐方式
        /// </summary>
        public SKTextAlign Alignment { get; set; } = SKTextAlign.Center;
        
        /// <summary>
        /// 边距 (Left, Top, Right, Bottom)
        /// </summary>
        public SKRect Padding { get; set; }
        
        /// <summary>
        /// 背景颜色
        /// </summary>
        public SKColor BackgroundColor { get; set; } = SKColors.Black;
    }
    
    /// <summary>
    /// 圣经经文单项
    /// </summary>
    public class BibleVerseItem
    {
        /// <summary>
        /// 是否是标题行
        /// </summary>
        public bool IsTitle { get; set; }
        
        /// <summary>
        /// 节号（如 "1"）
        /// </summary>
        public string VerseNumber { get; set; } = string.Empty;
        
        /// <summary>
        /// 经文内容
        /// </summary>
        public string Text { get; set; } = string.Empty;
        
        /// <summary>
        /// 是否高亮显示
        /// </summary>
        public bool IsHighlighted { get; set; }
    }
    
    /// <summary>
    /// 文本样式
    /// </summary>
    public class TextStyle
    {
        /// <summary>
        /// 字体名称
        /// </summary>
        public string FontFamily { get; set; } = "微软雅黑";
        
        /// <summary>
        /// 字体大小
        /// </summary>
        public float FontSize { get; set; } = 20f;
        
        /// <summary>
        /// 文本颜色
        /// </summary>
        public SKColor TextColor { get; set; } = SKColors.White;
        
        /// <summary>
        /// 是否粗体
        /// </summary>
        public bool IsBold { get; set; } = false;
        
        /// <summary>
        /// 是否斜体
        /// </summary>
        public bool IsItalic { get; set; } = false;
        
        /// <summary>
        /// 行距倍数（1.0 = 紧凑，1.5 = 1.5倍行距）
        /// </summary>
        public float LineSpacing { get; set; } = 1.2f;
        
        /// <summary>
        /// 文本特效（可选）
        /// </summary>
        public TextEffectStyle Effect { get; set; }
        
        /// <summary>
        /// 从Hex颜色字符串创建SKColor
        /// </summary>
        public static SKColor ParseColor(string hexColor)
        {
            try
            {
                if (string.IsNullOrEmpty(hexColor))
                    return SKColors.White;
                
                return SKColor.Parse(hexColor);
            }
            catch
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [TextStyle] 颜色解析失败: {hexColor}，使用默认白色");
#endif
                return SKColors.White;
            }
        }
        
        /// <summary>
        /// 将SKColor转换为Hex字符串
        /// </summary>
        public static string ToHexString(SKColor color)
        {
            return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        }
    }
    
    /// <summary>
    /// 文本特效样式（描边、阴影等高级效果）
    /// </summary>
    public class TextEffectStyle
    {
        /// <summary>
        /// 描边颜色（null表示不使用描边）
        /// </summary>
        public SKColor? StrokeColor { get; set; }
        
        /// <summary>
        /// 描边宽度（像素）
        /// </summary>
        public float StrokeWidth { get; set; } = 2f;
        
        /// <summary>
        /// 阴影颜色（null表示不使用阴影）
        /// </summary>
        public SKColor? ShadowColor { get; set; }
        
        /// <summary>
        /// 阴影偏移量
        /// </summary>
        public SKPoint ShadowOffset { get; set; } = new SKPoint(2, 2);
        
        /// <summary>
        /// 阴影模糊半径
        /// </summary>
        public float ShadowBlur { get; set; } = 2f;
        
        /// <summary>
        /// 渐变着色器（TODO: 扩展功能）
        /// </summary>
        public SKShader GradientShader { get; set; }
    }
    
    /// <summary>
    /// 文本布局结果
    /// </summary>
    public class TextLayout
    {
        /// <summary>
        /// 文本行列表
        /// </summary>
        public List<TextLine> Lines { get; set; } = new List<TextLine>();
        
        /// <summary>
        /// 总尺寸
        /// </summary>
        public SKSize TotalSize { get; set; }
    }
    
    /// <summary>
    /// 单行文本布局信息
    /// </summary>
    public class TextLine
    {
        /// <summary>
        /// 文本内容
        /// </summary>
        public string Text { get; set; } = string.Empty;
        
        /// <summary>
        /// 位置坐标
        /// </summary>
        public SKPoint Position { get; set; }
        
        /// <summary>
        /// 尺寸
        /// </summary>
        public SKSize Size { get; set; }
    }
}

