using System;
using System.Threading;
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
        private int _projectionScrollSyncRunning;

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
        public void SyncProjectionScroll(bool force = false)
        {
            if (!_syncEnabled || _projectionWindow == null || _currentImage == null)
            {
                return;
            }

            if (Interlocked.Exchange(ref _projectionScrollSyncRunning, 1) == 1)
            {
                return;
            }

            try
            {
                if (!force && ShouldThrottleSync())
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
                    double mainScrollableHeight = _mainScrollViewer.ScrollableHeight;
                    double projectionScrollableHeight = _projectionScrollViewer.ScrollableHeight;
                    double projScrollTop = ProjectionScrollPolicy.CalculateByScrollableHeights(
                        mainScrollTop,
                        mainScrollableHeight,
                        projectionScrollableHeight);
                    _projectionScrollViewer.ScrollToVerticalOffset(projScrollTop);
                    _host.RecordProjectionSync();
                });
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _projectionScrollSyncRunning, 0);
            }
        }

        private void SetProjectionVerticalOffsetWithStabilization(double offset)
        {
            if (_projectionScrollViewer == null)
            {
                return;
            }

            // 性能优化：移除 UpdateLayout() 调用，避免强制同步布局导致滚动卡顿
            // ScrollToVerticalOffset 本身会触发布局更新，无需额外强制同步
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

            if (_projectionScrollViewer == null)
            {
                return;
            }

            double titleInset = 0d;
            if (visible)
            {
                _projectionBibleTitleBorder.UpdateLayout();
                titleInset = _projectionBibleTitleBorder.ActualHeight;
                if (titleInset <= 0)
                {
                    // 兜底，避免布局尚未完成时顶部留白丢失导致遮挡经文。
                    titleInset = 62d;
                }
            }

            var currentMargin = _projectionScrollViewer.Margin;
            if (Math.Abs(currentMargin.Top - titleInset) > 0.5d)
            {
                _projectionScrollViewer.Margin = new Thickness(
                    currentMargin.Left,
                    titleInset,
                    currentMargin.Right,
                    currentMargin.Bottom);
            }
        }

        private void UpdateBibleTitleStyleOnUi(
            string fontFamily,
            double fontSize,
            string titleColorHex,
            string backgroundColorHex,
            double sourceDpiScaleY,
            double contentScaleRatio)
        {
            double projectionDpiScaleY = 1.0d;
            try
            {
                if (_projectionWindow != null)
                {
                    projectionDpiScaleY = System.Windows.Media.VisualTreeHelper.GetDpi(_projectionWindow).DpiScaleY;
                }
            }
            catch
            {
            }

            if (projectionDpiScaleY <= 0)
            {
                projectionDpiScaleY = 1.0d;
            }

            if (sourceDpiScaleY <= 0)
            {
                sourceDpiScaleY = 1.0d;
            }

            if (contentScaleRatio <= 0)
            {
                contentScaleRatio = 1.0d;
            }

            // 主屏与投影屏 DPI 不一致时，按像素等效换算，避免“同 DIP 字号在高 DPI 投影上变大”。
            // 另外固定标题是独立 Overlay，需叠加内容缩放比，才能与投影经文主体视觉一致。
            double normalizedFontSize = fontSize * (sourceDpiScaleY / projectionDpiScaleY) * contentScaleRatio;
            if (normalizedFontSize <= 0)
            {
                normalizedFontSize = fontSize;
            }

            if (_projectionBibleTitleText != null)
            {
                if (!string.IsNullOrWhiteSpace(fontFamily))
                {
                    try
                    {
                        _projectionBibleTitleText.FontFamily = new System.Windows.Media.FontFamily(fontFamily);
                    }
                    catch
                    {
                    }
                }

                if (normalizedFontSize > 0)
                {
                    _projectionBibleTitleText.FontSize = normalizedFontSize;
                }

                if (!string.IsNullOrWhiteSpace(titleColorHex))
                {
                    try
                    {
                        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(titleColorHex);
                        _projectionBibleTitleText.Foreground = new System.Windows.Media.SolidColorBrush(color);
                    }
                    catch
                    {
                    }
                }
            }

            if (_projectionBibleTitleBorder != null && !string.IsNullOrWhiteSpace(backgroundColorHex))
            {
                try
                {
                    var bg = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backgroundColorHex);
                    _projectionBibleTitleBorder.Background = new System.Windows.Media.SolidColorBrush(bg);
                }
                catch
                {
                }
            }

#if DEBUG
            try
            {
                string signature =
                    $"{_projectionBibleTitleText?.FontFamily?.Source}|{_projectionBibleTitleText?.FontSize:0.##}|" +
                    $"{sourceDpiScaleY:0.###}|{projectionDpiScaleY:0.###}|{contentScaleRatio:0.###}|" +
                    $"{titleColorHex}|{backgroundColorHex}";
                if (!string.Equals(signature, _lastBibleTitleDiagSignature, StringComparison.Ordinal))
                {
                    _lastBibleTitleDiagSignature = signature;
                    System.Diagnostics.Trace.WriteLine(
                        $"[BibleTitleDiag][Projection.UpdateBibleTitleStyleOnUi] " +
                        $"fontFamily={_projectionBibleTitleText?.FontFamily?.Source}, fontSize={_projectionBibleTitleText?.FontSize:0.##}, " +
                        $"sourceDpiScaleY={sourceDpiScaleY:0.###}, projectionDpiScaleY={projectionDpiScaleY:0.###}, " +
                        $"contentScaleRatio={contentScaleRatio:0.###}, requestedFontSize={fontSize:0.##}, normalizedFontSize={normalizedFontSize:0.##}, " +
                        $"titleColorHex={titleColorHex}, backgroundColorHex={backgroundColorHex}");
                }
            }
            catch
            {
            }
#endif
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
                _originalTopScalePercent,
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
                _originalTopScalePercent,
                _zoomRatio);
            return height;
        }
    }
}
