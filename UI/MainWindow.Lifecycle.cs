using System;
using System.ComponentModel;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 窗口生命周期事件

        /// <summary>
        /// 窗口关闭事件 - 清理资源
        /// </summary>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine("🔚 主窗口正在关闭,清理资源...");
                
                // 保存用户设置
                SaveSettings();
                
                // 取消订阅事件，防止内存泄漏
                if (_videoPlayerManager != null)
                {
                    _videoPlayerManager.VideoTrackDetected -= VideoPlayerManager_VideoTrackDetected;
                    _videoPlayerManager.PlayStateChanged -= OnVideoPlayStateChanged;
                    _videoPlayerManager.MediaChanged -= OnVideoMediaChanged;
                    _videoPlayerManager.MediaEnded -= OnVideoMediaEnded;
                    _videoPlayerManager.ProgressUpdated -= OnVideoProgressUpdated;
                }
                
                // 注意：PropertyChanged事件使用匿名方法订阅，无法直接取消订阅
                // ViewModel会随窗口关闭自动释放
                // 如果需要，应在订阅时保存匿名方法引用以便取消订阅
                
                // 停止并清理视频播放器
                if (_videoPlayerManager != null)
                {
                    _videoPlayerManager.Stop();
                    _videoPlayerManager.Dispose();
                }
                
                // 关闭投影窗口
                if (_projectionManager != null)
                {
                    _projectionManager.CloseProjection();
                    _projectionManager.Dispose();
                }
                
                // 释放全局热键
                if (_globalHotKeyManager != null)
                {
                    _globalHotKeyManager.Dispose();
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 资源清理失败: {ex.Message}");
            }
        }

        #endregion
    }
}

