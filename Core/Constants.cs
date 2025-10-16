namespace ImageColorChanger.Core
{
    /// <summary>
    /// 应用程序常量配置
    /// 所有魔法数字都在这里定义，便于维护和调整
    /// </summary>
    public static class Constants
    {
        // ==================== 图片限制 ====================
        
        /// <summary>最大图片文件大小（字节）- 100MB</summary>
        public const long MaxImageFileSizeBytes = 100 * 1024 * 1024;
        
        /// <summary>最大图片宽度（像素）</summary>
        public const int MaxImageWidth = 8192;
        
        /// <summary>最大图片高度（像素）</summary>
        public const int MaxImageHeight = 8192;
        
        /// <summary>JPEG保存质量（0-100）</summary>
        public const int JpegQuality = 95;
        
        
        // ==================== 缩放相关 ====================
        
        /// <summary>最大缩放比例（10倍）</summary>
        public const double MaxZoomRatio = 10.0;
        
        /// <summary>最小缩放比例（0.1倍）</summary>
        public const double MinZoomRatio = 0.1;
        
        /// <summary>默认缩放比例（原始大小）</summary>
        public const double DefaultZoomRatio = 1.0;
        
        /// <summary>动态缩放步长 - 大倍数（>2.0倍）</summary>
        public const double ZoomStepLarge = 1.15;
        
        /// <summary>动态缩放步长 - 中等倍数（1.0-2.0倍）</summary>
        public const double ZoomStepMedium = 1.08;
        
        /// <summary>动态缩放步长 - 小倍数（<1.0倍）</summary>
        public const double ZoomStepSmall = 1.05;
        
        /// <summary>缩放比例阈值 - 大倍数</summary>
        public const double ZoomRatioLargeThreshold = 2.0;
        
        /// <summary>缩放比例阈值 - 中等倍数</summary>
        public const double ZoomRatioMediumThreshold = 1.0;
        
        
        // ==================== 原图模式缩放策略 ====================
        
        /// <summary>屏幕面积是图片16倍以上时的最大缩放</summary>
        public const double OriginalModeMaxScaleArea16X = 6.0;
        
        /// <summary>屏幕面积是图片9倍以上时的最大缩放</summary>
        public const double OriginalModeMaxScaleArea9X = 4.0;
        
        /// <summary>屏幕面积是图片4倍以上时的最大缩放</summary>
        public const double OriginalModeMaxScaleArea4X = 3.0;
        
        /// <summary>默认的最大缩放倍数</summary>
        public const double OriginalModeMaxScaleDefault = 2.0;
        
        /// <summary>面积比例阈值 - 16倍</summary>
        public const double AreaRatioThreshold16X = 16;
        
        /// <summary>面积比例阈值 - 9倍</summary>
        public const double AreaRatioThreshold9X = 9;
        
        /// <summary>面积比例阈值 - 4倍</summary>
        public const double AreaRatioThreshold4X = 4;
        
        
        // ==================== 图片效果相关 ====================
        
        /// <summary>深色背景判断阈值（亮度 < 80 为深色）</summary>
        public const int DarkBackgroundThreshold = 80;
        
        /// <summary>深色背景下的亮色文字阈值（亮度 > 100）</summary>
        public const int LightTextBrightnessDarkBg = 100;
        
        /// <summary>浅色背景下的暗色文字阈值（亮度 < 150）</summary>
        public const int DarkTextBrightnessLightBg = 150;
        
        
        // ==================== 缓存管理 ====================
        
        /// <summary>普通图片缓存大小</summary>
        public const int MemoryCacheSize = 150;
        
        /// <summary>渲染缓存大小（BitmapSource缓存）- 支持多张图片×多种尺寸</summary>
        public const int RenderCacheSize = 500;
        
        /// <summary>预览图缓存大小</summary>
        public const int PreviewCacheSize = 50;
        
        /// <summary>内存警告阈值（百分比）</summary>
        public const int MemoryWarningThresholdPercent = 80;
        
        /// <summary>缓存清理阈值 - 渲染缓存达到此值时触发清理</summary>
        public const int RenderCacheCleanupThreshold = 400;
        
        
        // ==================== 性能相关 ====================
        
        /// <summary>60帧每秒的更新间隔（秒）</summary>
        public const double Fps60Interval = 1.0 / 60.0;
        
        /// <summary>33帧每秒的更新间隔（秒）</summary>
        public const double Fps33Interval = 1.0 / 33.0;
        
        /// <summary>滚动更新延迟（毫秒）</summary>
        public const int ScrollUpdateDelayMs = 16;
        
        /// <summary>滚动结束检测延迟（毫秒）</summary>
        public const int ScrollEndDelayMs = 100;
    }
    
    /// <summary>
    /// 背景类型枚举
    /// </summary>
    public enum BackgroundType
    {
        /// <summary>深色背景</summary>
        Black,
        
        /// <summary>浅色背景</summary>
        White
    }
    
    /// <summary>
    /// 原图显示模式
    /// </summary>
    public enum OriginalDisplayMode
    {
        /// <summary>适中模式 - 等比缩放，完整显示</summary>
        Fit,
        
        /// <summary>拉伸模式 - 宽度填满，高度按比例</summary>
        Stretch
    }
}

