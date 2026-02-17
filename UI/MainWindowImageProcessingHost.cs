using ImageColorChanger.Core;
using SkiaSharp;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 到图片处理宿主接口的适配器。
    /// </summary>
    public sealed class MainWindowImageProcessingHost : IImageProcessingHost
    {
        private readonly MainWindow _mainWindow;

        public MainWindowImageProcessingHost(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new System.ArgumentNullException(nameof(mainWindow));
        }

        public void ResetKeyframeIndex() => _mainWindow.ResetKeyframeIndex();

        public SKColor GetCurrentTargetColor() => _mainWindow.GetCurrentTargetColor();
    }
}
