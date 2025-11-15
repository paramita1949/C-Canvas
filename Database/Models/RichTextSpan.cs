using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 富文本片段 - 每个片段可以有独立的样式
    /// </summary>
    [Table("rich_text_spans")]
    public class RichTextSpan
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 所属文本框ID（外键）
        /// </summary>
        [Column("text_element_id")]
        public int TextElementId { get; set; }

        /// <summary>
        /// 片段在文本框中的顺序（从0开始）
        /// </summary>
        [Column("span_order")]
        public int SpanOrder { get; set; }

        /// <summary>
        /// 片段文本内容
        /// </summary>
        [Column("text")]
        public string Text { get; set; } = "";

        #region 字体样式

        /// <summary>
        /// 字体名称（继承自父文本框，可覆盖）
        /// </summary>
        [Column("font_family")]
        public string FontFamily { get; set; } = null;

        /// <summary>
        /// 字体大小（继承自父文本框，可覆盖）
        /// </summary>
        [Column("font_size")]
        public double? FontSize { get; set; } = null;

        /// <summary>
        /// 字体颜色（继承自父文本框，可覆盖）
        /// </summary>
        [Column("font_color")]
        public string FontColor { get; set; } = null;

        /// <summary>
        /// 是否加粗 (0=否, 1=是)
        /// </summary>
        [Column("is_bold")]
        public int IsBold { get; set; } = 0;

        /// <summary>
        /// 是否斜体 (0=否, 1=是)
        /// </summary>
        [Column("is_italic")]
        public int IsItalic { get; set; } = 0;

        /// <summary>
        /// 是否下划线 (0=否, 1=是)
        /// </summary>
        [Column("is_underline")]
        public int IsUnderline { get; set; } = 0;

        #endregion

        #region 边框样式

        /// <summary>
        /// 边框颜色
        /// </summary>
        [Column("border_color")]
        public string BorderColor { get; set; } = null;

        /// <summary>
        /// 边框宽度 (px)
        /// </summary>
        [Column("border_width")]
        public double? BorderWidth { get; set; } = null;

        /// <summary>
        /// 边框圆角 (px)
        /// </summary>
        [Column("border_radius")]
        public double? BorderRadius { get; set; } = null;

        /// <summary>
        /// 边框不透明度 (0-100)
        /// </summary>
        [Column("border_opacity")]
        public int? BorderOpacity { get; set; } = null;

        #endregion

        #region 背景样式

        /// <summary>
        /// 背景颜色
        /// </summary>
        [Column("background_color")]
        public string BackgroundColor { get; set; } = null;

        /// <summary>
        /// 背景圆角 (px)
        /// </summary>
        [Column("background_radius")]
        public double? BackgroundRadius { get; set; } = null;

        /// <summary>
        /// 背景不透明度 (0-100)
        /// </summary>
        [Column("background_opacity")]
        public int? BackgroundOpacity { get; set; } = null;

        #endregion

        #region 阴影样式

        /// <summary>
        /// 阴影颜色
        /// </summary>
        [Column("shadow_color")]
        public string ShadowColor { get; set; } = null;

        /// <summary>
        /// 阴影X偏移 (px)
        /// </summary>
        [Column("shadow_offset_x")]
        public double? ShadowOffsetX { get; set; } = null;

        /// <summary>
        /// 阴影Y偏移 (px)
        /// </summary>
        [Column("shadow_offset_y")]
        public double? ShadowOffsetY { get; set; } = null;

        /// <summary>
        /// 阴影模糊半径 (px)
        /// </summary>
        [Column("shadow_blur")]
        public double? ShadowBlur { get; set; } = null;

        /// <summary>
        /// 阴影不透明度 (0-100)
        /// </summary>
        [Column("shadow_opacity")]
        public int? ShadowOpacity { get; set; } = null;

        #endregion

        #region 辅助属性（不映射到数据库）

        [NotMapped]
        public bool IsBoldBool
        {
            get => IsBold == 1;
            set => IsBold = value ? 1 : 0;
        }

        [NotMapped]
        public bool IsItalicBool
        {
            get => IsItalic == 1;
            set => IsItalic = value ? 1 : 0;
        }

        [NotMapped]
        public bool IsUnderlineBool
        {
            get => IsUnderline == 1;
            set => IsUnderline = value ? 1 : 0;
        }

        #endregion
    }
}

