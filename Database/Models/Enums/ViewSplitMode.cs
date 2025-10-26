namespace ImageColorChanger.Database.Models.Enums
{
    /// <summary>
    /// 画面分割模式（在幻灯片编辑中使用）
    /// </summary>
    public enum ViewSplitMode
    {
        /// <summary>单画面 - 默认模式，一张图片占满整个显示区域</summary>
        Single = 0,
        
        /// <summary>左右分割 - 左右两块（50% | 50%），显示两张不同的图片</summary>
        Horizontal = 1,
        
        /// <summary>上下分割 - 上下两块（上50% / 下50%），显示两张不同的图片</summary>
        Vertical = 2,
        
        /// <summary>四宫格 - 2x2网格布局，显示四张不同的图片</summary>
        Quad = 3,
        
        /// <summary>三分割 - 左边上下分割，右边整个竖分割（左上1 | 左下2 | 右3）</summary>
        TripleSplit = 4,
    }
}

