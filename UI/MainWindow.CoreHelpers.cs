using System;
using System.Windows.Input;
using SkiaSharp;
using ImageColorChanger.Utils;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 核心辅助方法
    /// </summary>
    public partial class MainWindow
    {
        private void ResetView()
        {
            ResetZoom();
            ShowStatus("视图已重置");
        }

        public void ShowStatus(string message)
        {
            // 保持固定标题，不显示状态信息
            // Title = $"Canvas Cast V2.5.5 - {message}";
            //System.Diagnostics.Debug.WriteLine($"状态: {message}");
        }

        public SKColor GetCurrentTargetColor()
        {
            return _currentTargetColor;
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                DisposeLiveCaption();
            }
            catch
            {
                // ignored
            }
            _imageProcessor?.Dispose();
            base.OnClosed(e);
        }

        /// <summary>
        /// 窗口激活事件 - 确保能接收键盘事件
        /// </summary>
        private void Window_Activated(object sender, EventArgs e)
        {
            if (!_startupFirstActivationLogged)
            {
                _startupFirstActivationLogged = true;
                StartupPerfLogger.Mark("MainWindow.WindowActivated.First");
            }

            Focus();
            SyncLiveCaptionVisibilityWithMainWindowContext("window-activated");
        }

        /// <summary>
        /// 图片区域点击事件 - 恢复主窗口焦点
        /// </summary>
        private void ImageArea_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
        }
    }
}


