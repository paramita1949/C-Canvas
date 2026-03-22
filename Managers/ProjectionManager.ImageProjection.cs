using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 图片投影更新链路逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 更新投影内容
        /// </summary>
        private void UpdateProjection()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!CanUpdateProjectionImage())
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(UpdateProjectionOnUiThread);
                sw.Stop();
            }
            catch (InvalidOperationException)
            {
                sw.Stop();
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                sw.Stop();
            }
            catch (Exception ex)
            {
                sw.Stop();
                _ = ex;
            }
        }

        private bool CanUpdateProjectionImage()
        {
            if (_projectionWindow == null)
            {
                return false;
            }

            // 没有图片时通常处于视频投影路径，这里无需刷新图片层。
            return _currentImage != null;
        }

        private void UpdateProjectionOnUiThread()
        {
            var renderContext = GetProjectionRenderContext();
            string cacheKey = GenerateProjectionCacheKey(renderContext.ImageWidth, renderContext.ImageHeight);
            _projectionImage = GetOrCreateProjectionBitmap(cacheKey, renderContext.ImageWidth, renderContext.ImageHeight);
            if (_projectionImage == null)
            {
                return;
            }

            ApplyProjectionBitmapToImageControl(_projectionImage, renderContext.ImageWidth, renderContext.ImageHeight);
            ApplyDefaultProjectionImageLayout(
                renderContext.ImageWidth,
                renderContext.ImageHeight,
                renderContext.ScreenWidth,
                renderContext.ScreenHeight);
        }

        private ProjectionRenderContext GetProjectionRenderContext()
        {
            var screen = GetCurrentProjectionScreenOrNull();
            int screenWidth = screen?.PhysicalWidth ?? DefaultProjectionWidth;
            int screenHeight = screen?.PhysicalHeight ?? DefaultProjectionHeight;
            var (newWidth, newHeight) = CalculateImageSize(screenWidth, screenHeight);
            return new ProjectionRenderContext(screenWidth, screenHeight, newWidth, newHeight);
        }

        private void ApplyProjectionBitmapToImageControl(BitmapSource bitmap, int width, int height)
        {
            if (_projectionImageControl == null || bitmap == null)
            {
                return;
            }

            _projectionImageControl.Source = bitmap;
            _projectionImageControl.Width = width;
            _projectionImageControl.Height = height;
        }

        private void ApplyDefaultProjectionImageLayout(int newWidth, int newHeight, int screenWidth, int screenHeight)
        {
            var (containerWidth, containerHeight) = GetProjectionContainerSize(screenWidth, screenHeight);
            ApplyProjectionImageAlignment(newWidth, newHeight, containerWidth, containerHeight);
            UpdateProjectionContainerHeight(newHeight, containerHeight);
        }

        private void ApplySharedProjectionImageLayout(int newHeight, int screenWidth, int screenHeight)
        {
            if (_projectionScrollViewer == null || _projectionContainer == null || _projectionImageControl == null)
            {
                return;
            }

            var (_, containerHeight) = GetProjectionContainerSize(screenWidth, screenHeight);
            bool shouldTopAlign = _isOriginalMode && _originalDisplayMode == OriginalDisplayMode.FitTop;
            double y = _isOriginalMode && !shouldTopAlign ? Math.Max(0, (containerHeight - newHeight) / 2.0) : 0;
            _projectionImageControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            _projectionImageControl.VerticalAlignment = VerticalAlignment.Top;
            _projectionImageControl.Margin = new Thickness(0, y, 0, 0);
            _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            _projectionContainer.Height = ProjectionLayoutPolicy.CalculateScrollContainerHeight(newHeight, containerHeight, _isOriginalMode);
        }

        private void ConfigureSharedProjectionImageStretch()
        {
            if (_projectionImageControl == null)
            {
                return;
            }

            _projectionImageControl.Stretch =
                _isOriginalMode && _originalDisplayMode == OriginalDisplayMode.Stretch
                    ? Stretch.Fill
                    : Stretch.Uniform;
        }

        private (double width, double height) GetProjectionContainerSize(int screenWidth, int screenHeight)
        {
            double containerWidth = _projectionScrollViewer?.ActualWidth ?? screenWidth;
            double containerHeight = _projectionScrollViewer?.ActualHeight ?? screenHeight;
            if (containerWidth <= 0)
            {
                containerWidth = screenWidth;
            }

            if (containerHeight <= 0)
            {
                containerHeight = screenHeight;
            }

            return (containerWidth, containerHeight);
        }

        private void ApplyProjectionImageAlignment(int newWidth, int newHeight, double containerWidth, double containerHeight)
        {
            _projectionImageControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            _projectionImageControl.VerticalAlignment = VerticalAlignment.Top;
            _projectionImageControl.Margin = ProjectionLayoutPolicy.CalculateImageMargin(
                newWidth,
                newHeight,
                containerWidth,
                containerHeight,
                _isOriginalMode,
                _originalDisplayMode);
        }

        private void UpdateProjectionContainerHeight(int newHeight, double containerHeight)
        {
            if (_projectionContainer == null)
            {
                return;
            }

            double scrollHeight = ProjectionLayoutPolicy.CalculateScrollContainerHeight(newHeight, containerHeight, _isOriginalMode);
            _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            _projectionContainer.Height = scrollHeight;
        }
    }
}
