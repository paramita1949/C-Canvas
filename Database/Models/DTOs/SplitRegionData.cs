namespace ImageColorChanger.Database.Models.DTOs
{
    /// <summary>
    /// 画面分割区域数据（用于JSON序列化）
    /// </summary>
    public class SplitRegionData
    {
        /// <summary>
        /// 区域索引（0-3，取决于分割模式）
        /// </summary>
        public int RegionIndex { get; set; }
        
        /// <summary>
        /// 图片路径
        /// </summary>
        public string ImagePath { get; set; }
    }
}

