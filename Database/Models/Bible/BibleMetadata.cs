using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models.Bible
{
    /// <summary>
    /// 圣经数据库元数据模型
    /// </summary>
    [Table("metadata")]
    public class BibleMetadata
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 元数据键名
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 元数据值
        /// </summary>
        public string Value { get; set; }
    }
}

