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
        private System.Windows.Threading.DispatcherTimer _projectionNdiMirrorTimer;
        private DateTime _projectionNdiLastMirrorAtUtc = DateTime.MinValue;
        private static readonly TimeSpan ProjectionNdiMirrorInterval = TimeSpan.FromMilliseconds(120);

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
                PublishMediaNdiFromCurrentProjectionViewport("scroll");
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

                    PublishMediaNdiFromCurrentProjectionViewport("update");
                }
            }
        }

        private void PublishMediaNdiFromCurrentProjectionViewport(string source)
        {
            if (!ShouldUseProjectionNdiMirrorForCurrentMode())
            {
                return;
            }

            if (_imageNdiModule?.IsEnabled() != true || _projectionManager?.IsProjectionActive != true)
            {
                return;
            }

            using var projectionViewportFrame = _projectionManager.CaptureProjectionViewportFrameForNdi();
            if (projectionViewportFrame == null)
            {
                ProjectionNdiDiagnostics.Log($"Media NDI publish skipped: source={source}, frame=null");
                return;
            }

            bool sent = _imageNdiModule.PublishImageFrame(projectionViewportFrame);
            if (!sent)
            {
                ProjectionNdiDiagnostics.Log(
                    $"Media NDI publish failed: source={source}, size={projectionViewportFrame.Width}x{projectionViewportFrame.Height}");
            }
        }

        private void StartProjectionNdiMirror()
        {
            if (_projectionNdiMirrorTimer == null)
            {
                _projectionNdiMirrorTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = ProjectionNdiMirrorInterval
                };
                _projectionNdiMirrorTimer.Tick += (_, _) =>
                {
                    if (!ShouldUseProjectionNdiMirrorForCurrentMode())
                    {
                        return;
                    }

                    if (_projectionManager?.IsProjectionActive != true || _imageNdiModule?.IsEnabled() != true)
                    {
                        return;
                    }

                    var now = DateTime.UtcNow;
                    if ((now - _projectionNdiLastMirrorAtUtc) < ProjectionNdiMirrorInterval)
                    {
                        return;
                    }

                    _projectionNdiLastMirrorAtUtc = now;
                    PublishMediaNdiFromCurrentProjectionViewport("mirror");
                };
            }

            _projectionNdiLastMirrorAtUtc = DateTime.MinValue;
            if (!_projectionNdiMirrorTimer.IsEnabled)
            {
                _projectionNdiMirrorTimer.Start();
            }
        }

        private void StopProjectionNdiMirror()
        {
            if (_projectionNdiMirrorTimer?.IsEnabled == true)
            {
                _projectionNdiMirrorTimer.Stop();
            }
        }

        private bool ShouldUseProjectionNdiMirrorForCurrentMode()
        {
            // 镜像推流只用于图片/媒体主投影链路。
            // 歌词与文本编辑器（幻灯片）有各自独立的 NDI 发布逻辑，避免同通道相互覆盖抖动。
            if (_isLyricsMode)
            {
                return false;
            }

            if (TextEditorPanel?.Visibility == System.Windows.Visibility.Visible)
            {
                return false;
            }

            return true;
        }
    }
}
