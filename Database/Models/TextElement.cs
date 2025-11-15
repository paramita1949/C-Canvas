using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 文本元素实体
    /// 表示画布上的一个可编辑文本框
    /// </summary>
    [Table("text_elements")]
    public class TextElement
    {
        /// <summary>
        /// 元素ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 所属项目ID（外键，兼容旧数据）
        /// </summary>
        [Column("project_id")]
        public int? ProjectId { get; set; }

        /// <summary>
        /// 所属幻灯片ID（外键）
        /// </summary>
        [Column("slide_id")]
        public int? SlideId { get; set; }

        /// <summary>
        /// X坐标（左上角）
        /// </summary>
        [Column("x")]
        public double X { get; set; }

        /// <summary>
        /// Y坐标（左上角）
        /// </summary>
        [Column("y")]
        public double Y { get; set; }

        /// <summary>
        /// 宽度
        /// </summary>
        [Column("width")]
        public double Width { get; set; } = 300;

        /// <summary>
        /// 高度
        /// </summary>
        [Column("height")]
        public double Height { get; set; } = 100;

        /// <summary>
        /// 图层顺序（Z-Index，数值越大越在上层）
        /// </summary>
        [Column("z_index")]
        public int ZIndex { get; set; } = 0;

        /// <summary>
        /// 文本内容
        /// </summary>
        [Column("content")]
        public string Content { get; set; } = "";

        /// <summary>
        /// 字体名称
        /// </summary>
        [Column("font_family")]
        public string FontFamily { get; set; } = "Microsoft YaHei UI";

        /// <summary>
        /// 字号
        /// </summary>
        [Column("font_size")]
        public double FontSize { get; set; } = 24;

        /// <summary>
        /// 字体颜色（#RRGGBB格式）
        /// </summary>
        [Column("font_color")]
        public string FontColor { get; set; } = "#000000";

        /// <summary>
        /// 是否加粗（0=否，1=是）
        /// </summary>
        [Column("is_bold")]
        public int IsBold { get; set; } = 0;

        /// <summary>
        /// 文本对齐方式（Left/Center/Right）
        /// </summary>
        [Column("text_align")]
        public string TextAlign { get; set; } = "Left";

        /// <summary>
        /// 是否下划线（0=否，1=是）
        /// </summary>
        [Column("is_underline")]
        public int IsUnderline { get; set; } = 0;

        /// <summary>
        /// 是否斜体（0=否，1=是）
        /// </summary>
        [Column("is_italic")]
        public int IsItalic { get; set; } = 0;

        // ========== 边框样式 ==========

        /// <summary>
        /// 边框颜色（#RRGGBB格式）
        /// </summary>
        [Column("border_color")]
        public string BorderColor { get; set; } = "#000000";

        /// <summary>
        /// 边框宽度（0-20px）
        /// </summary>
        [Column("border_width")]
        public double BorderWidth { get; set; } = 2;  // ✅ 默认 2px

        /// <summary>
        /// 边框圆角（0-100px）
        /// </summary>
        [Column("border_radius")]
        public double BorderRadius { get; set; } = 0;

        /// <summary>
        /// 边框透明度（0-100，0%=完全不透明，100%=完全透明）
        /// </summary>
        [Column("border_opacity")]
        public int BorderOpacity { get; set; } = 100;  // ✅ 默认完全透明（无边框）

        // ========== 背景样式 ==========

        /// <summary>
        /// 背景颜色（#RRGGBB格式）
        /// </summary>
        [Column("background_color")]
        public string BackgroundColor { get; set; } = "Transparent";  // ✅ 默认透明

        /// <summary>
        /// 背景圆角（0-100px）
        /// </summary>
        [Column("background_radius")]
        public double BackgroundRadius { get; set; } = 0;

        /// <summary>
        /// 背景透明度（0-100，0%=完全不透明，100%=完全透明）
        /// </summary>
        [Column("background_opacity")]
        public int BackgroundOpacity { get; set; } = 100;  // ✅ 默认完全透明（无背景）

        // ========== 阴影样式 ==========

        /// <summary>
        /// 阴影颜色（#RRGGBB格式）
        /// </summary>
        [Column("shadow_color")]
        public string ShadowColor { get; set; } = "#000000";

        /// <summary>
        /// 阴影X偏移（px）
        /// </summary>
        [Column("shadow_offset_x")]
        public double ShadowOffsetX { get; set; } = 0;

        /// <summary>
        /// 阴影Y偏移（px）
        /// </summary>
        [Column("shadow_offset_y")]
        public double ShadowOffsetY { get; set; } = 0;

        /// <summary>
        /// 阴影模糊半径（px）
        /// </summary>
        [Column("shadow_blur")]
        public double ShadowBlur { get; set; } = 0;

        /// <summary>
        /// 阴影不透明度（0-100）
        /// </summary>
        [Column("shadow_opacity")]
        public int ShadowOpacity { get; set; } = 0;

        // ========== 间距样式 ==========

        /// <summary>
        /// 行间距（1.0-2.5）
        /// </summary>
        [Column("line_spacing")]
        public double LineSpacing { get; set; } = 1.2;

        /// <summary>
        /// 字间距（0.00-0.36）
        /// </summary>
        [Column("letter_spacing")]
        public double LetterSpacing { get; set; } = 0.0;

        /// <summary>
        /// 是否有对称伙伴（0=否，1=是）
        /// </summary>
        [Column("is_symmetric")]
        public int IsSymmetric { get; set; } = 0;

        /// <summary>
        /// 对称伙伴元素ID（如果有）
        /// </summary>
        [Column("symmetric_pair_id")]
        public int? SymmetricPairId { get; set; }

        /// <summary>
        /// 对称类型（Horizontal/Vertical）
        /// </summary>
        [Column("symmetric_type")]
        public string SymmetricType { get; set; }

        /// <summary>
        /// 导航属性：所属项目（兼容旧数据）
        /// </summary>
        [ForeignKey("ProjectId")]
        public virtual TextProject Project { get; set; }

        /// <summary>
        /// 导航属性：所属幻灯片
        /// </summary>
        [ForeignKey("SlideId")]
        public virtual Slide Slide { get; set; }

        /// <summary>
        /// 辅助属性：是否加粗（布尔类型）
        /// </summary>
        [NotMapped]
        public bool IsBoldBool
        {
            get => IsBold == 1;
            set => IsBold = value ? 1 : 0;
        }

        /// <summary>
        /// 辅助属性：是否下划线（布尔类型）
        /// </summary>
        [NotMapped]
        public bool IsUnderlineBool
        {
            get => IsUnderline == 1;
            set => IsUnderline = value ? 1 : 0;
        }

        /// <summary>
        /// 辅助属性：是否斜体（布尔类型）
        /// </summary>
        [NotMapped]
        public bool IsItalicBool
        {
            get => IsItalic == 1;
            set => IsItalic = value ? 1 : 0;
        }

        /// <summary>
        /// 辅助属性：是否有对称伙伴（布尔类型）
        /// </summary>
        [NotMapped]
        public bool IsSymmetricBool
        {
            get => IsSymmetric == 1;
            set => IsSymmetric = value ? 1 : 0;
        }

        #region 富文本片段（RichText）

        /// <summary>
        /// 富文本片段列表（导航属性）
        /// 如果为空或null，则使用 Content 字段作为纯文本
        /// </summary>
        [NotMapped]
        public List<RichTextSpan> RichTextSpans { get; set; } = new List<RichTextSpan>();

        /// <summary>
        /// 是否启用富文本模式
        /// </summary>
        [NotMapped]
        public bool IsRichTextMode => RichTextSpans != null && RichTextSpans.Count > 0;

        #endregion
    }
}

