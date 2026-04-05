using System;
using System.Text;
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
        public void SetProjectionCaptionLayout(
            ProjectionCaptionOrientation orientation,
            ProjectionCaptionHorizontalAnchor horizontalAnchor,
            ProjectionCaptionVerticalAnchor verticalAnchor)
        {
            _projectionCaptionOrientation = orientation;
            _projectionCaptionHorizontalAnchor = horizontalAnchor;
            _projectionCaptionVerticalAnchor = verticalAnchor;

            if (_projectionWindow == null)
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(ApplyProjectionCaptionOverlayLayoutOnUi);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 更新投影字幕覆盖层（独立于本机字幕窗）。
        /// </summary>
        public void UpdateProjectionCaptionOverlay(string captionText)
        {
            if (_projectionWindow == null)
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(() =>
                {
                    if (_projectionCaptionOverlayContainer == null || _projectionCaptionOverlayText == null)
                    {
                        return;
                    }

                    string next = (captionText ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(next))
                    {
                        HideProjectionCaptionOverlayOnUi();
                        return;
                    }

                    ApplyProjectionCaptionOverlayLayoutOnUi();
                    string formatted = FormatProjectionCaptionText(next);

                    _projectionCaptionOverlayText.Text = formatted;
                    _projectionCaptionOverlayContainer.Visibility = Visibility.Visible;
                });
            }
            catch
            {
            }
        }

        /// <summary>
        /// 隐藏投影字幕覆盖层。
        /// </summary>
        public void HideProjectionCaptionOverlay()
        {
            if (_projectionWindow == null)
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(HideProjectionCaptionOverlayOnUi);
            }
            catch
            {
            }
        }

        private void HideProjectionCaptionOverlayOnUi()
        {
            if (_projectionCaptionOverlayText != null)
            {
                _projectionCaptionOverlayText.Text = string.Empty;
            }

            if (_projectionCaptionOverlayContainer != null)
            {
                _projectionCaptionOverlayContainer.Visibility = Visibility.Collapsed;
            }
        }

        private string FormatProjectionCaptionText(string text)
        {
            if (_projectionCaptionOrientation != ProjectionCaptionOrientation.Vertical)
            {
                return text;
            }

            var sb = new StringBuilder();
            string normalized = (text ?? string.Empty).Replace("\r", string.Empty);
            string[] rawLines = normalized.Split('\n');
            string line1 = rawLines.Length > 0 ? rawLines[0] : string.Empty;
            string line2 = rawLines.Length > 1 ? rawLines[1] : string.Empty;
            if (string.IsNullOrEmpty(line1) && string.IsNullOrEmpty(line2))
            {
                return text;
            }

            int rows = Math.Max(line1.Length, line2.Length);
            int maxVisibleRows = GetVerticalVisibleRowCapacity();
            int startRow = Math.Max(0, rows - maxVisibleRows);

            for (int i = startRow; i < rows; i++)
            {
                char c1 = i < line1.Length ? line1[i] : '　';
                char c2 = i < line2.Length ? line2[i] : '　';
                // 竖排按右到左阅读：第二行在左，第一行在右。
                sb.Append(c2);
                sb.Append('　');
                sb.Append(c1);

                if (i < rows - 1)
                {
                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        private void ApplyProjectionCaptionOverlayLayoutOnUi()
        {
            if (_projectionCaptionOverlayBorder == null || _projectionCaptionOverlayText == null)
            {
                return;
            }

            ProjectionCaptionHorizontalAnchor effectiveHorizontalAnchor = _projectionCaptionOrientation == ProjectionCaptionOrientation.Horizontal
                ? ProjectionCaptionHorizontalAnchor.Center
                : _projectionCaptionHorizontalAnchor;
            ProjectionCaptionVerticalAnchor effectiveVerticalAnchor = _projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical
                ? ProjectionCaptionVerticalAnchor.Center
                : _projectionCaptionVerticalAnchor;

            _projectionCaptionOverlayBorder.Margin = BuildProjectionCaptionMargin(effectiveHorizontalAnchor);
            _projectionCaptionOverlayBorder.MaxWidth = double.PositiveInfinity;
            _projectionCaptionOverlayBorder.MaxHeight = double.PositiveInfinity;

            _projectionCaptionOverlayBorder.HorizontalAlignment = ResolveHorizontalAlignment(effectiveHorizontalAnchor);
            _projectionCaptionOverlayBorder.VerticalAlignment = ResolveVerticalAlignment(effectiveVerticalAnchor);

            if (_projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical)
            {
                _projectionCaptionOverlayBorder.MinWidth = 96;
                _projectionCaptionOverlayBorder.Padding = new Thickness(14, 18, 14, 18);
                _projectionCaptionOverlayText.TextAlignment = TextAlignment.Center;
                _projectionCaptionOverlayText.TextWrapping = TextWrapping.NoWrap;
                _projectionCaptionOverlayText.FontSize = 34;
                _projectionCaptionOverlayText.LineHeight = 44;
                return;
            }

            _projectionCaptionOverlayBorder.MinWidth = 0;
            _projectionCaptionOverlayBorder.Padding = new Thickness(28, 16, 28, 16);
            _projectionCaptionOverlayText.TextAlignment = TextAlignment.Center;
            _projectionCaptionOverlayText.TextWrapping = TextWrapping.Wrap;
            _projectionCaptionOverlayText.FontSize = 46;
            _projectionCaptionOverlayText.LineHeight = 62;
        }

        private Thickness BuildProjectionCaptionMargin(ProjectionCaptionHorizontalAnchor horizontalAnchor)
        {
            const double baseInset = 18;
            if (_projectionCaptionOrientation == ProjectionCaptionOrientation.Horizontal)
            {
                return new Thickness(baseInset);
            }

            double viewportWidth = _projectionWindow?.ActualWidth > 0
                ? _projectionWindow.ActualWidth
                : DefaultProjectionWidth;

            // “靠左/靠右”不贴边：保留更大的观看安全区。
            double edgeInset = Math.Clamp(viewportWidth * 0.12, 120, 260);
            return horizontalAnchor switch
            {
                ProjectionCaptionHorizontalAnchor.Left => new Thickness(edgeInset, baseInset, baseInset, baseInset),
                ProjectionCaptionHorizontalAnchor.Right => new Thickness(baseInset, baseInset, edgeInset, baseInset),
                _ => new Thickness(baseInset)
            };
        }

        private int GetVerticalVisibleRowCapacity()
        {
            if (_projectionCaptionOverlayText == null)
            {
                return 1;
            }

            double viewportHeight = _projectionWindow?.ActualHeight > 0
                ? _projectionWindow.ActualHeight
                : DefaultProjectionHeight;
            if (viewportHeight <= 0)
            {
                viewportHeight = DefaultProjectionHeight;
            }

            double marginTop = _projectionCaptionOverlayBorder?.Margin.Top ?? 18;
            double marginBottom = _projectionCaptionOverlayBorder?.Margin.Bottom ?? 18;
            double paddingTop = _projectionCaptionOverlayBorder?.Padding.Top ?? 18;
            double paddingBottom = _projectionCaptionOverlayBorder?.Padding.Bottom ?? 18;
            double available = viewportHeight - marginTop - marginBottom - paddingTop - paddingBottom - 8;
            if (available <= 0)
            {
                available = 300;
            }

            double lineHeight = _projectionCaptionOverlayText.LineHeight;
            if (lineHeight <= 0)
            {
                double fontSize = _projectionCaptionOverlayText.FontSize > 0
                    ? _projectionCaptionOverlayText.FontSize
                    : 34;
                lineHeight = Math.Max(44, fontSize * 1.2);
            }

            int maxRows = (int)Math.Floor(available / lineHeight);
            return Math.Max(1, maxRows);
        }

        private static System.Windows.HorizontalAlignment ResolveHorizontalAlignment(ProjectionCaptionHorizontalAnchor anchor)
        {
            return anchor switch
            {
                ProjectionCaptionHorizontalAnchor.Left => System.Windows.HorizontalAlignment.Left,
                ProjectionCaptionHorizontalAnchor.Right => System.Windows.HorizontalAlignment.Right,
                _ => System.Windows.HorizontalAlignment.Center
            };
        }

        private static VerticalAlignment ResolveVerticalAlignment(ProjectionCaptionVerticalAnchor anchor)
        {
            return anchor switch
            {
                ProjectionCaptionVerticalAnchor.Top => VerticalAlignment.Top,
                ProjectionCaptionVerticalAnchor.Bottom => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Center
            };
        }

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
