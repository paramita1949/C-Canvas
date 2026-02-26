using System;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;
using LibVLCSharp.WPF;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 渲染流程编排（共享渲染/预渲染）逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 更新投影图片 - 使用共享渲染模式
        /// </summary>
        public void UpdateProjectionImage(SKBitmap image, bool applyColorEffect, double zoomRatio, bool isOriginalMode, OriginalDisplayMode originalDisplayMode = OriginalDisplayMode.Fit, bool bypassCache = false)
        {
            // 只有在实际使用 VisualBrush 时才重置（避免每次都 ScrollToTop）
            if (_projectionWindow != null && _currentBibleScrollViewer != null)
            {
                RunOnMainDispatcher(() => { ResetVisualBrushProjection(); });
            }

            _currentImage = image;
            _isColorEffectEnabled = applyColorEffect;
            _isOriginalMode = isOriginalMode;
            _originalDisplayMode = originalDisplayMode;
            _currentImagePath = _imageProcessor?.CurrentImagePath;

            if (bypassCache)
            {
                _currentImagePath = $"texteditor_{Guid.NewGuid()}";
            }

            if (_projectionWindow != null && image != null)
            {
                _zoomRatio = zoomRatio;
                var mainScreenBitmap = _imageProcessor?.CurrentPhoto;
                if (mainScreenBitmap != null && !bypassCache)
                {
                    _ = UseSharedRenderingAsync(mainScreenBitmap);
                }
                else
                {
                    _ = PreRenderProjectionAsync();
                }
            }
        }

        /// <summary>
        /// 使用共享渲染模式 - 直接复用主屏 BitmapSource
        /// </summary>
        private System.Threading.Tasks.Task UseSharedRenderingAsync(BitmapSource mainScreenBitmap)
        {
            if (_projectionWindow == null || mainScreenBitmap == null)
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }

            try
            {
                RunOnMainDispatcher(() =>
                {
                    var renderContext = GetProjectionRenderContext();
                    _projectionImage = mainScreenBitmap;
                    if (_projectionImageControl != null)
                    {
                        ApplyProjectionBitmapToImageControl(_projectionImage, renderContext.ImageWidth, renderContext.ImageHeight);
                        ConfigureSharedProjectionImageStretch();
                        ApplySharedProjectionImageLayout(renderContext.ImageHeight, renderContext.ScreenWidth, renderContext.ScreenHeight);
                    }
                });
            }
            catch (Exception ex)
            {
                _ = ex;
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// 同步渲染投影（主线程）- 独立渲染模式（降级方案）
        /// </summary>
        public System.Threading.Tasks.Task PreRenderProjectionAsync()
        {
            if (_projectionWindow == null || _currentImage == null)
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }

            lock (_preRenderLock)
            {
                if (_isPreRendering)
                {
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                _isPreRendering = true;
            }

            try
            {
                RunOnMainDispatcher(() =>
                {
                    var renderContext = GetProjectionRenderContext();
                    string cacheKey = GenerateProjectionCacheKey(renderContext.ImageWidth, renderContext.ImageHeight);

                    if (_projectionCache.TryGetValue(cacheKey, out BitmapSource cachedImage))
                    {
                        _projectionImage = cachedImage;
                        ApplyProjectionBitmapToImageControl(_projectionImage, renderContext.ImageWidth, renderContext.ImageHeight);
                        return;
                    }

                    BitmapSource projectionImage = GetOrCreateProjectionBitmap(cacheKey, renderContext.ImageWidth, renderContext.ImageHeight);
                    if (projectionImage == null)
                    {
                        return;
                    }

                    _projectionImage = projectionImage;
                    ApplyProjectionBitmapToImageControl(_projectionImage, renderContext.ImageWidth, renderContext.ImageHeight);
                    ApplyDefaultProjectionImageLayout(
                        renderContext.ImageWidth,
                        renderContext.ImageHeight,
                        renderContext.ScreenWidth,
                        renderContext.ScreenHeight);
                });
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                _ = ex;
            }
            finally
            {
                lock (_preRenderLock)
                {
                    _isPreRendering = false;
                }
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// 获取投影窗口的 VideoView（用于视频播放）
        /// </summary>
        public VideoView GetProjectionVideoView()
        {
            return _projectionVideoView;
        }

        /// <summary>
        /// 获取投影窗口（用于 FPS 监控）
        /// </summary>
        public System.Windows.Window GetProjectionWindow()
        {
            return _projectionWindow;
        }
    }
}
