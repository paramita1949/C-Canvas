using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 图片处理流程需要的宿主能力，隔离 Core 对 UI 主窗口的强类型依赖。
    /// </summary>
    public interface IImageProcessingHost
    {
        void ResetKeyframeIndex();
        SKColor GetCurrentTargetColor();
    }
}
