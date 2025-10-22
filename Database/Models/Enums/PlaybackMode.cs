namespace ImageColorChanger.Database.Models.Enums
{
    /// <summary>
    /// 播放模式枚举
    /// </summary>
    public enum PlaybackMode
    {
        /// <summary>关键帧模式</summary>
        Keyframe,
        
        /// <summary>原图模式</summary>
        Original,
        
        /// <summary>合成播放模式（根据总时长平滑滚动）</summary>
        Composite
    }
}

