namespace ImageColorChanger.Database.Models.Enums
{
    /// <summary>
    /// 播放状态枚举
    /// </summary>
    public enum PlaybackStatus
    {
        /// <summary>空闲状态</summary>
        Idle,
        
        /// <summary>录制中</summary>
        Recording,
        
        /// <summary>播放中</summary>
        Playing,
        
        /// <summary>暂停中</summary>
        Paused,
        
        /// <summary>停止</summary>
        Stopped
    }
}

