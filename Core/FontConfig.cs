using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 字体配置文件模型
    /// </summary>
    public class FontConfig
    {
        /// <summary>
        /// 字体分类列表
        /// </summary>
        [JsonPropertyName("fontCategories")]
        public List<FontCategory> FontCategories { get; set; } = new List<FontCategory>();

        /// <summary>
        /// 默认字体名称
        /// </summary>
        [JsonPropertyName("defaultFont")]
        public string DefaultFont { get; set; } = "Microsoft YaHei UI";
    }

    /// <summary>
    /// 字体分类
    /// </summary>
    public class FontCategory
    {
        /// <summary>
        /// 分类名称（如：中文字体、英文字体、数字字体）
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// 该分类下的字体列表
        /// </summary>
        [JsonPropertyName("fonts")]
        public List<CustomFont> Fonts { get; set; } = new List<CustomFont>();
    }

    /// <summary>
    /// 自定义字体信息
    /// </summary>
    public class CustomFont
    {
        /// <summary>
        /// 显示名称（如：思源黑体）
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// 字体文件路径（相对于 Fonts 文件夹，如：Chinese/SourceHanSansCN.ttf）
        /// </summary>
        [JsonPropertyName("file")]
        public string File { get; set; }

        /// <summary>
        /// 字体族名称（FontFamily，如：Source Han Sans CN）
        /// </summary>
        [JsonPropertyName("family")]
        public string Family { get; set; }

        /// <summary>
        /// 字重（如：Regular, Bold, Light）
        /// </summary>
        [JsonPropertyName("weight")]
        public string Weight { get; set; } = "Regular";

        /// <summary>
        /// 预览文本（如：思源黑体 ABCabc 123）
        /// </summary>
        [JsonPropertyName("preview")]
        public string Preview { get; set; }

        /// <summary>
        /// 是否为收藏字体
        /// </summary>
        [JsonPropertyName("isFavorite")]
        public bool IsFavorite { get; set; } = false;
    }
}

