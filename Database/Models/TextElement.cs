using System;
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
        /// 所属项目ID（外键）
        /// </summary>
        [Required]
        [Column("project_id")]
        public int ProjectId { get; set; }

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
        /// 导航属性：所属项目
        /// </summary>
        [ForeignKey("ProjectId")]
        public virtual TextProject Project { get; set; }

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
        /// 辅助属性：是否有对称伙伴（布尔类型）
        /// </summary>
        [NotMapped]
        public bool IsSymmetricBool
        {
            get => IsSymmetric == 1;
            set => IsSymmetric = value ? 1 : 0;
        }
    }
}

