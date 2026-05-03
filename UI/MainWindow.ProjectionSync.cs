using System.Windows.Controls;
using ImageColorChanger.Services.Ndi;
using ImageColorChanger.Services.Projection.Output;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 投影滚动同步
    /// </summary>
    public partial class MainWindow
    {
        // 标志：是否禁用自动投影滚动同步（用于关键帧跳转时避免中间状态同步）
        private volatile bool _disableAutoProjectionSync = false;

        /// <summary>
        /// 设置是否禁用自动投影滚动同步
        /// </summary>
        public void SetAutoProjectionSyncEnabled(bool enabled)
        {
            _disableAutoProjectionSync = !enabled;
        }

        /// <summary>
        /// 滚动事件处理 - 同步投影和更新预览线
        /// </summary>
        private void ImageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 如果正在执行关键帧跳转，跳过自动同步（避免中间状态导致投影位置错误）
            if (!_disableAutoProjectionSync)
            {
                _projectionManager?.SyncProjectionScroll();
            }

            // 更新关键帧预览线和指示块
            _keyframeManager?.UpdatePreviewLines();
        }

        /// <summary>
        /// 更新投影内容
        /// </summary>
        public void UpdateProjection()
        {
            if (_imageProcessor.CurrentImage != null)
            {
                if (_projectionManager != null && _projectionManager.IsProjectionActive)
                {
                    _projectionManager?.UpdateProjectionImage(
                        _imageProcessor.CurrentImage,
                        _isColorEffectEnabled,
                        _currentZoom,
                        _originalMode,
                        _originalDisplayMode,
                        _originalTopScalePercent
                    );

                    // 媒体 NDI 通道暂时下线，等待与主投影同源链路重构完成后再恢复。
                    if (_ndiRouter?.IsChannelEnabled(NdiChannel.Media) == true)
                    {
                        _projectionNdiOutputManager?.PublishFrame(
                            _imageProcessor.CurrentImage,
                            ProjectionNdiContentType.Image);
                    }
                }
            }
        }
    }
}
