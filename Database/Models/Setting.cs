using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 通用设置实体
    /// </summary>
    [Table("settings")]
    public class Setting
    {
        /// <summary>
        /// 设置键名
        /// </summary>
        [Key]
        [Column("key")]
        public string Key { get; set; }

        /// <summary>
        /// 设置值
        /// </summary>
        [Required]
        [Column("value")]
        public string Value { get; set; }
    }

    /// <summary>
    /// UI设置实体
    /// </summary>
    [Table("ui_settings")]
    public class UISetting
    {
        /// <summary>
        /// 设置键名
        /// </summary>
        [Key]
        [Column("key")]
        public string Key { get; set; }

        /// <summary>
        /// 设置值（通常是JSON字符串）
        /// </summary>
        [Required]
        [Column("value")]
        public string Value { get; set; }
    }
}

