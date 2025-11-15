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
        /// 是否处于编辑模式
        /// </summary>
        public bool IsEditing { get; set; } = false;

        /// <summary>
        /// 光标位置（字符索引）
        /// </summary>
        public int CursorPosition { get; set; } = 0;

        /// <summary>
        /// 光标是否可见（用于闪烁动画）
        /// </summary>
        public bool CursorVisible { get; set; } = true;

        /// <summary>
        /// 选择起始位置（null 表示无选择）
        /// </summary>
        public int? SelectionStart { get; set; } = null;

        /// <summary>
        /// 选择结束位置（null 表示无选择）
        /// </summary>
        public int? SelectionEnd { get; set; } = null;

        /// <summary>
        /// 生成缓存键（用于渲染缓存）
        /// </summary>
        public string GetCacheKey()
        {
            // 编辑模式下禁用缓存（光标和选择需要实时渲染）
            if (IsEditing)
            {
                return $"TB_EDIT_{Text}_{Size.Width}_{Size.Height}_{CursorPosition}_{SelectionStart}_{SelectionEnd}_{CursorVisible}_{DateTime.Now.Ticks}";
            }

            return $"TB_{Text}_{Size.Width}_{Size.Height}_{Style.FontSize}_{Style.TextColor}_{Style.FontFamily}_{Style.IsBold}_{Style.LineSpacing}_{Style.LetterSpacing}";
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
        /// 是否下划线
        /// </summary>
        public bool IsUnderline { get; set; } = false;

        /// <summary>
        /// 行距倍数（1.0 = 紧凑，1.5 = 1.5倍行距）
        /// </summary>
        public float LineSpacing { get; set; } = 1.2f;

        /// <summary>
        /// 字间距（字符间额外间距，单位：em）
        /// </summary>
        public float LetterSpacing { get; set; } = 0.0f;

        // ========== 边框样式 ==========

        /// <summary>
        /// 边框颜色
        /// </summary>
        public SKColor BorderColor { get; set; } = SKColors.Black;

        /// <summary>
        /// 边框宽度（0 = 无边框）
        /// </summary>
        public float BorderWidth { get; set; } = 0f;

        /// <summary>
        /// 边框圆角半径
        /// </summary>
        public float BorderRadius { get; set; } = 0f;

        /// <summary>
        /// 边框不透明度（0-100）
        /// </summary>
        public int BorderOpacity { get; set; } = 0;

        // ========== 背景样式 ==========

        /// <summary>
        /// 背景颜色
        /// </summary>
        public SKColor BackgroundColor { get; set; } = SKColors.White;

        /// <summary>
        /// 背景圆角半径
        /// </summary>
        public float BackgroundRadius { get; set; } = 0f;

        /// <summary>
        /// 背景不透明度（0-100）
        /// </summary>
        public int BackgroundOpacity { get; set; } = 0;

        // ========== 阴影样式 ==========

        /// <summary>
        /// 阴影颜色
        /// </summary>
        public SKColor ShadowColor { get; set; } = SKColors.Black;

        /// <summary>
        /// 阴影X偏移
        /// </summary>
        public float ShadowOffsetX { get; set; } = 0f;

        /// <summary>
        /// 阴影Y偏移
        /// </summary>
        public float ShadowOffsetY { get; set; } = 0f;

        /// <summary>
        /// 阴影模糊半径
        /// </summary>
        public float ShadowBlur { get; set; } = 0f;

        /// <summary>
        /// 阴影不透明度（0-100）
        /// </summary>
        public int ShadowOpacity { get; set; } = 0;

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
    /// 阴影类型枚举
    /// </summary>
    public enum ShadowType
    {
        /// <summary>
        /// 无阴影
        /// </summary>
        None = 0,

        /// <summary>
        /// 外部阴影（Drop Shadow）
        /// </summary>
        DropShadow = 1,

        /// <summary>
        /// 内部阴影（Inner Shadow）
        /// </summary>
        InnerShadow = 2,

        /// <summary>
        /// 透视阴影（Perspective Shadow）
        /// </summary>
        PerspectiveShadow = 3
    }

    /// <summary>
    /// 阴影预设方案枚举
    /// </summary>
    public enum ShadowPreset
    {
        /// <summary>
        /// 自定义（用户手动调整参数）
        /// </summary>
        Custom = 0,

        // ========== 外部阴影预设 ==========

        /// <summary>
        /// 柔和阴影 - 距离4px，模糊8px
        /// </summary>
        DropSoft = 1,

        /// <summary>
        /// 标准阴影 - 距离8px，模糊12px
        /// </summary>
        DropStandard = 2,

        /// <summary>
        /// 强烈阴影 - 距离12px，模糊20px
        /// </summary>
        DropStrong = 3,

        // ========== 内部阴影预设 ==========

        /// <summary>
        /// 细腻内阴影 - 距离2px，模糊4px
        /// </summary>
        InnerSubtle = 11,

        /// <summary>
        /// 标准内阴影 - 距离4px，模糊8px
        /// </summary>
        InnerStandard = 12,

        /// <summary>
        /// 深度内阴影 - 距离6px，模糊12px
        /// </summary>
        InnerDeep = 13,

        // ========== 透视阴影预设 ==========

        /// <summary>
        /// 近距透视 - X6px/Y2px，模糊6px
        /// </summary>
        PerspectiveNear = 21,

        /// <summary>
        /// 中距透视 - X12px/Y4px，模糊10px
        /// </summary>
        PerspectiveMedium = 22,

        /// <summary>
        /// 远距透视 - X20px/Y6px，模糊16px
        /// </summary>
        PerspectiveFar = 23
    }

    /// <summary>
    /// 阴影预设参数配置
    /// </summary>
    public static class ShadowPresetConfig
    {
        /// <summary>
        /// 获取预设的阴影参数
        /// </summary>
        public static (float offsetX, float offsetY, float blur) GetPresetParams(ShadowPreset preset)
        {
            return preset switch
            {
                // 外部阴影
                ShadowPreset.DropSoft => (4f, 4f, 8f),
                ShadowPreset.DropStandard => (8f, 8f, 12f),
                ShadowPreset.DropStrong => (12f, 12f, 20f),

                // 内部阴影（使用负偏移模拟内阴影效果）
                ShadowPreset.InnerSubtle => (-2f, -2f, 4f),
                ShadowPreset.InnerStandard => (-4f, -4f, 8f),
                ShadowPreset.InnerDeep => (-6f, -6f, 12f),

                // 透视阴影
                ShadowPreset.PerspectiveNear => (6f, 2f, 6f),
                ShadowPreset.PerspectiveMedium => (12f, 4f, 10f),
                ShadowPreset.PerspectiveFar => (20f, 6f, 16f),

                _ => (0f, 0f, 0f)
            };
        }

        /// <summary>
        /// 获取预设的阴影类型
        /// </summary>
        public static ShadowType GetPresetType(ShadowPreset preset)
        {
            return preset switch
            {
                ShadowPreset.DropSoft or ShadowPreset.DropStandard or ShadowPreset.DropStrong
                    => ShadowType.DropShadow,

                ShadowPreset.InnerSubtle or ShadowPreset.InnerStandard or ShadowPreset.InnerDeep
                    => ShadowType.InnerShadow,

                ShadowPreset.PerspectiveNear or ShadowPreset.PerspectiveMedium or ShadowPreset.PerspectiveFar
                    => ShadowType.PerspectiveShadow,

                _ => ShadowType.None
            };
        }

        /// <summary>
        /// 获取预设的显示名称
        /// </summary>
        public static string GetPresetName(ShadowPreset preset)
        {
            return preset switch
            {
                ShadowPreset.DropSoft => "柔和阴影",
                ShadowPreset.DropStandard => "标准阴影",
                ShadowPreset.DropStrong => "强烈阴影",
                ShadowPreset.InnerSubtle => "细腻内阴影",
                ShadowPreset.InnerStandard => "标准内阴影",
                ShadowPreset.InnerDeep => "深度内阴影",
                ShadowPreset.PerspectiveNear => "近距透视",
                ShadowPreset.PerspectiveMedium => "中距透视",
                ShadowPreset.PerspectiveFar => "远距透视",
                _ => "自定义"
            };
        }
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

