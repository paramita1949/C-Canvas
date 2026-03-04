using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SkiaSharp;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 文本投影与 VisualBrush 状态重置逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 更新通知覆盖层（透明叠加，不改动底图）。
        /// </summary>
        public void UpdateProjectionNoticeOverlay(SKBitmap noticeOverlayFrame)
        {
            if (_projectionWindow == null || noticeOverlayFrame == null)
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(() =>
                {
                    if (_projectionNoticeOverlayImage == null || _projectionNoticeOverlayContainer == null)
                    {
                        return;
                    }

                    var bitmapSource = ConvertToBitmapSource(noticeOverlayFrame);
                    if (bitmapSource == null)
                    {
                        HideProjectionNoticeOverlayOnUi();
                        return;
                    }

                    _projectionNoticeOverlayImage.Source = bitmapSource;
                    _projectionNoticeOverlayImage.Visibility = Visibility.Visible;
                    _projectionNoticeOverlayContainer.Visibility = Visibility.Visible;
                });
            }
            catch
            {
            }
        }

        /// <summary>
        /// 隐藏通知覆盖层。
        /// </summary>
        public void HideProjectionNoticeOverlay()
        {
            if (_projectionWindow == null)
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(HideProjectionNoticeOverlayOnUi);
            }
            catch
            {
            }
        }

        private void HideProjectionNoticeOverlayOnUi()
        {
            if (_projectionNoticeOverlayImage != null)
            {
                _projectionNoticeOverlayImage.Source = null;
                _projectionNoticeOverlayImage.Visibility = Visibility.Collapsed;
            }

            if (_projectionNoticeOverlayContainer != null)
            {
                _projectionNoticeOverlayContainer.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 更新投影文字内容（专门用于歌词/文本编辑器）
        /// </summary>
        public void UpdateProjectionText(SKBitmap renderedTextImage)
        {
            if (_projectionWindow == null || renderedTextImage == null)
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(() =>
                {
                    ResetVisualBrushProjection();
                    var bitmapSource = ConvertToBitmapSource(renderedTextImage);
                    if (bitmapSource == null)
                    {
                        return;
                    }

                    ApplyProjectionBitmapToImageControl(bitmapSource, renderedTextImage.Width, renderedTextImage.Height);
                    var screen = GetCurrentProjectionScreenOrNull();
                    double screenWidth = screen?.PhysicalBounds.Width ?? DefaultProjectionWidth;
                    double screenHeight = screen?.PhysicalBounds.Height ?? DefaultProjectionHeight;
                    var (containerWidth, _) = GetProjectionCanvasSize(screenWidth, screenHeight);

                    _projectionImageControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    _projectionImageControl.VerticalAlignment = VerticalAlignment.Top;
                    _projectionImageControl.Stretch = System.Windows.Media.Stretch.Fill;
                    _projectionImageControl.Width = containerWidth;
                    _projectionImageControl.Height = renderedTextImage.Height * (containerWidth / renderedTextImage.Width);
                    _projectionImageControl.Margin = new Thickness(0, 0, 0, 0);

                    if (_projectionContainer != null)
                    {
                        _projectionContainer.Height = _projectionImageControl.Height;
                        _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    }
                });
            }
            catch
            {
            }
        }

        /// <summary>
        /// 更新投影全帧内容（用于幻灯片/经文叠加同源渲染）。
        /// 与 UpdateProjectionText 不同：不按“文本长图”逻辑扩展滚动高度，直接按视口整帧显示。
        /// </summary>
        public void UpdateProjectionTextFullFrame(SKBitmap renderedFrame)
        {
            if (_projectionWindow == null || renderedFrame == null)
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(() =>
                {
                    ResetVisualBrushProjection();
                    var bitmapSource = ConvertToBitmapSource(renderedFrame);
                    if (bitmapSource == null || _projectionImageControl == null || _projectionScrollViewer == null || _projectionContainer == null)
                    {
                        return;
                    }

                    _projectionImageControl.Source = bitmapSource;
                    _projectionImageControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                    _projectionImageControl.VerticalAlignment = VerticalAlignment.Stretch;
                    _projectionImageControl.Stretch = System.Windows.Media.Stretch.Fill;
                    _projectionImageControl.Width = double.NaN;
                    _projectionImageControl.Height = double.NaN;
                    _projectionImageControl.Margin = new Thickness(0);
                    RenderOptions.SetBitmapScalingMode(_projectionImageControl, BitmapScalingMode.Fant);
                    RenderOptions.SetCachingHint(_projectionImageControl, CachingHint.Cache);

                    double viewportHeight = _projectionScrollViewer.ActualHeight > 0
                        ? _projectionScrollViewer.ActualHeight
                        : (_projectionWindow.ActualHeight > 0 ? _projectionWindow.ActualHeight : DefaultProjectionHeight);
                    _projectionContainer.Height = viewportHeight;
                    _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    _projectionScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    _projectionScrollViewer.ScrollToVerticalOffset(0);
                });
            }
            catch
            {
            }
        }

        /// <summary>
        /// 禁用 VisualBrush 投影，恢复图片投影模式（需在 Dispatcher 中调用）。
        /// </summary>
        private void ResetVisualBrushProjection()
        {
            if (_currentBibleScrollViewer != null)
            {
                _currentBibleScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }

            if (_projectionVisualBrushRect != null)
            {
                _projectionVisualBrushRect.Fill = null;
                _projectionVisualBrushRect.Visibility = Visibility.Collapsed;
            }

            if (_projectionContainer != null)
            {
                _projectionContainer.Height = double.NaN;
                _projectionContainer.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            }

            if (_projectionScrollViewer != null)
            {
                _projectionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                _projectionScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
                _projectionScrollViewer.ScrollToTop();
                _projectionScrollViewer.ScrollToLeftEnd();
            }

            if (_projectionImageControl != null)
            {
                _projectionImageControl.Visibility = Visibility.Visible;
                _projectionImageControl.Source = null;
            }

            HideProjectionNoticeOverlayOnUi();

            _currentBibleScrollViewer = null;
        }
    }
}
