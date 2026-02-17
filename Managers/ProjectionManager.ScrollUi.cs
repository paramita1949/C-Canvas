using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Core;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 滚动同步与 UI 状态辅助逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 重置投影滚动位置到顶部
        /// </summary>
        public void ResetProjectionScroll()
        {
            if (_projectionWindow != null && _projectionScrollViewer != null)
            {
                TryRunOnProjectionDispatcher(() =>
                {
                    SetProjectionVerticalOffsetWithStabilization(0);
                });
            }
        }

        /// <summary>
        /// 同步共享渲染 - 每一帧调用，使用主屏 BitmapSource 更新投影窗口
        /// </summary>
        public void SyncSharedRendering()
        {
            if (!_syncEnabled || _projectionWindow == null)
            {
                return;
            }

            var mainScreenBitmap = _imageProcessor?.CurrentPhoto;
            if (mainScreenBitmap != null && mainScreenBitmap != _lastSharedBitmap)
            {
                _lastSharedBitmap = mainScreenBitmap;
                _ = UseSharedRenderingAsync(mainScreenBitmap);
            }
        }

        /// <summary>
        /// 同步投影滚动位置 - 使用图片绝对像素高度同步
        /// </summary>
        public void SyncProjectionScroll()
        {
            if (!_syncEnabled || _projectionWindow == null || _currentImage == null)
            {
                return;
            }

            try
            {
                if (ShouldThrottleSync())
                {
                    return;
                }

                RunOnMainDispatcher(() =>
                {
                    if (_projectionScrollViewer == null || _mainScrollViewer == null || _mainImageControl == null)
                    {
                        return;
                    }

                    double mainScrollTop = _mainScrollViewer.VerticalOffset;
                    double mainCanvasWidth = _mainScrollViewer.ActualWidth;
                    double mainCanvasHeight = _mainScrollViewer.ActualHeight;

                    var screen = GetCurrentProjectionScreenOrNull();
                    if (screen == null)
                    {
                        return;
                    }

                    int projScreenWidth = screen.PhysicalWidth;
                    int projScreenHeight = screen.PhysicalHeight;
                    double mainImgHeight = CalculateDisplayedImageHeight(mainCanvasWidth, mainCanvasHeight);
                    var (projCanvasWidth, projCanvasHeight) = GetProjectionCanvasSize(projScreenWidth, projScreenHeight);
                    double projImgHeight = CalculateDisplayedImageHeight(projCanvasWidth, projCanvasHeight);
                    if (mainImgHeight == 0 || projImgHeight == 0)
                    {
                        return;
                    }

                    double projScrollTop = ProjectionScrollPolicy.CalculateByImageHeights(
                        mainScrollTop,
                        mainImgHeight,
                        projImgHeight);
                    _projectionScrollViewer.ScrollToVerticalOffset(projScrollTop);
                    _host.RecordProjectionSync();
                });
            }
            catch
            {
            }
        }

        private void SetProjectionVerticalOffsetWithStabilization(double offset)
        {
            if (_projectionScrollViewer == null)
            {
                return;
            }

            _projectionScrollViewer.ScrollToVerticalOffset(offset);
            _projectionScrollViewer.InvalidateScrollInfo();
            _projectionScrollViewer.UpdateLayout();
            _projectionScrollViewer.ScrollToVerticalOffset(offset);
        }

        private void UpdateProjectionMediaFileNameOnUi(string fileName, bool isAudioOnly)
        {
            if (isAudioOnly)
            {
                _projectionMediaFileNameText.Text = fileName;
                _projectionMediaFileNameBorder.Visibility = Visibility.Visible;
                return;
            }

            _projectionMediaFileNameBorder.Visibility = Visibility.Collapsed;
        }

        private void UpdateBibleTitleOnUi(string title, bool visible)
        {
            _projectionBibleTitleText.Text = title;
            _projectionBibleTitleBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetProjectionMode(bool showVideo)
        {
            ResetVisualBrushProjection();
            SetElementVisibility(_projectionVideoContainer, showVideo ? Visibility.Visible : Visibility.Collapsed);
            SetElementVisibility(_projectionScrollViewer, showVideo ? Visibility.Collapsed : Visibility.Visible);
        }

        /// <summary>
        /// 当前是否处于视频投影模式
        /// </summary>
        public bool IsInVideoProjectionMode()
        {
            return _projectionVideoContainer != null &&
                   _projectionVideoContainer.Visibility == Visibility.Visible;
        }

        private static void SetElementVisibility(UIElement element, Visibility visibility)
        {
            if (element != null)
            {
                element.Visibility = visibility;
            }
        }

        private (double width, double height) GetProjectionCanvasSize(double fallbackWidth, double fallbackHeight)
        {
            double width = _projectionScrollViewer?.ActualWidth ?? fallbackWidth;
            double height = _projectionScrollViewer?.ActualHeight ?? fallbackHeight;
            if (width <= 0)
            {
                width = fallbackWidth;
            }

            if (height <= 0)
            {
                height = fallbackHeight;
            }

            return (width, height);
        }

        /// <summary>
        /// 计算图片在投影屏幕上的尺寸
        /// </summary>
        private (int width, int height) CalculateImageSize(int screenWidth, int screenHeight)
        {
            double canvasWidth = _projectionScrollViewer?.ActualWidth ?? screenWidth;
            double canvasHeight = _projectionScrollViewer?.ActualHeight ?? screenHeight;
            if (canvasWidth <= 0)
            {
                canvasWidth = screenWidth;
            }

            if (canvasHeight <= 0)
            {
                canvasHeight = screenHeight;
            }

            return ProjectionSizingPolicy.Calculate(
                _currentImage.Width,
                _currentImage.Height,
                canvasWidth,
                canvasHeight,
                _isOriginalMode,
                _originalDisplayMode,
                _zoomRatio);
        }

        private double CalculateDisplayedImageHeight(double canvasWidth, double canvasHeight)
        {
            if (_currentImage == null)
            {
                return 0;
            }

            var (_, height) = ProjectionSizingPolicy.Calculate(
                _currentImage.Width,
                _currentImage.Height,
                canvasWidth,
                canvasHeight,
                _isOriginalMode,
                _originalDisplayMode,
                _zoomRatio);
            return height;
        }
    }
}
