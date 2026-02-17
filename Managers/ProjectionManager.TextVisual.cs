using System;
using System.Windows;
using System.Windows.Controls;
using SkiaSharp;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 文本投影与 VisualBrush 状态重置逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
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

            _currentBibleScrollViewer = null;
        }
    }
}
