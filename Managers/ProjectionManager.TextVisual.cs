using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ImageColorChanger.Services.LiveCaption;
using SkiaSharp;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 文本投影与 VisualBrush 状态重置逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        private DateTime _projectionShiftProbeLastLogUtc = DateTime.MinValue;
        private DateTime _projectionVerticalProbeLastLogUtc = DateTime.MinValue;

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
            UpdateProjectionCaptionOverlay(captionText, null);
        }

        /// <summary>
        /// 更新投影字幕覆盖层（独立于本机字幕窗），支持最新字高亮起点。
        /// </summary>
        public void UpdateProjectionCaptionOverlay(string captionText, int? highlightStart)
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

                    // 帧中的缩进/换行及高亮来源下标必须一起保留，不能在投影入口再裁剪。
                    string next = captionText ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(next))
                    {
                        HideProjectionCaptionOverlayOnUi();
                        return;
                    }

                    _projectionCaptionLastRawText = next;
                    _projectionCaptionLastHighlightStart = highlightStart;
                    ApplyProjectionCaptionOverlayLayoutOnUi();
                    // 先应用稳定排版参数（字号/行高/边距），再做竖排可见行裁剪与着色，
                    // 避免使用旧行高计算可见行导致底部字符被截断。
                    ApplyProjectionCaptionStableTypographyOnUi();
                    if (_projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical)
                    {
                        ApplyProjectionCaptionRunsVertical(next, highlightStart);
                    }
                    else
                    {
                        ApplyProjectionCaptionRuns(next, highlightStart);
                        ProbeProjectionHorizontalShiftAnomaly(next, highlightStart);
                    }
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
                _projectionCaptionOverlayText.Inlines.Clear();
                _projectionCaptionOverlayText.Text = string.Empty;
            }

            if (_projectionCaptionOverlayContainer != null)
            {
                _projectionCaptionOverlayContainer.Visibility = Visibility.Collapsed;
            }

            _projectionCaptionLastRawText = string.Empty;
            _projectionCaptionLastHighlightStart = null;
        }

        public void SetProjectionCaptionTypography(
            string fontFamily,
            double fontSize,
            double margin,
            double lineHeight,
            double letterSpacing,
            string textColorHex,
            string latestTextColorHex)
        {
            string nextFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Microsoft YaHei UI" : fontFamily.Trim();
            double nextSize = Math.Clamp(fontSize, 16, 120);
            double nextMargin = Math.Clamp(margin, 6, 160);
            double nextLineHeight = Math.Max(nextSize, lineHeight);

            _projectionCaptionPreferredFontFamily = nextFamily;
            _projectionCaptionPreferredFontSize = nextSize;
            _projectionCaptionPreferredPadding = nextMargin;
            _projectionCaptionPreferredLineGap = Math.Clamp(nextLineHeight - nextSize, 0, 60);
            _projectionCaptionLetterSpacing = Math.Clamp(letterSpacing, 0, 10);
            if (TryParseProjectionCaptionColor(textColorHex, out System.Windows.Media.Color baseColor))
            {
                _projectionCaptionBaseBrush = new SolidColorBrush(baseColor);
            }
            else
            {
                _projectionCaptionBaseBrush = System.Windows.Media.Brushes.White;
            }

            if (TryParseProjectionCaptionColor(latestTextColorHex, out System.Windows.Media.Color parsed))
            {
                _projectionCaptionLatestBrush = new SolidColorBrush(parsed);
            }
            else
            {
                _projectionCaptionLatestBrush = System.Windows.Media.Brushes.Gold;
            }

            if (_projectionWindow == null)
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(() =>
                {
                    if (_projectionCaptionOverlayBorder == null || _projectionCaptionOverlayText == null)
                    {
                        return;
                    }

                    _projectionCaptionOverlayText.FontFamily = new System.Windows.Media.FontFamily(_projectionCaptionPreferredFontFamily);
                    _projectionCaptionOverlayText.Foreground = _projectionCaptionBaseBrush;

                    ApplyProjectionCaptionStableTypographyOnUi();
                    if (!string.IsNullOrWhiteSpace(_projectionCaptionLastRawText))
                    {
                        if (_projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical)
                        {
                            ApplyProjectionCaptionRunsVertical(_projectionCaptionLastRawText, _projectionCaptionLastHighlightStart);
                        }
                        else
                        {
                            ApplyProjectionCaptionRuns(_projectionCaptionLastRawText, _projectionCaptionLastHighlightStart);
                        }
                    }
                });
            }
            catch
            {
            }
        }

        private void ApplyProjectionCaptionTypographyFromCacheOnUi()
        {
            if (_projectionCaptionOverlayBorder == null || _projectionCaptionOverlayText == null)
            {
                return;
            }

            _projectionCaptionOverlayText.FontFamily = new System.Windows.Media.FontFamily(_projectionCaptionPreferredFontFamily);
            _projectionCaptionOverlayText.Foreground = _projectionCaptionBaseBrush;
            ApplyProjectionCaptionStableTypographyOnUi();
            FitProjectionCaptionHorizontalTypographyOnUi();
        }

        private void ApplyProjectionCaptionStableTypographyOnUi()
        {
            if (_projectionCaptionOverlayBorder == null || _projectionCaptionOverlayText == null)
            {
                return;
            }

            double size = Math.Clamp(_projectionCaptionPreferredFontSize, 20, 120);
            bool hasSecondLine = _projectionCaptionOrientation == ProjectionCaptionOrientation.Horizontal
                && !string.IsNullOrWhiteSpace(_projectionCaptionLastRawText)
                && _projectionCaptionLastRawText.IndexOf('\n') >= 0;
            double gap = hasSecondLine ? Math.Clamp(_projectionCaptionPreferredLineGap, 0, 60) : 0;
            double padding = Math.Clamp(_projectionCaptionPreferredPadding, 8, 80);
            _projectionCaptionOverlayText.FontSize = size;
            _projectionCaptionOverlayText.LayoutTransform = Transform.Identity;
            _projectionCaptionOverlayText.LineHeight = _projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical
                ? Math.Max(size * 1.08, size + 2) // 竖排独立紧凑行高，不使用横排“段间距”
                : size + gap;
            if (_projectionCaptionOrientation == ProjectionCaptionOrientation.Horizontal)
            {
                double horizontalInset = Math.Max(24, padding);
                _projectionCaptionOverlayBorder.Padding = new Thickness(horizontalInset, padding, horizontalInset, padding);
                _projectionCaptionOverlayText.Height = _projectionCaptionOverlayText.LineHeight * 2;
            }
            else
            {
                double verticalInset = Math.Clamp(padding * 0.5, 12, 28);
                _projectionCaptionOverlayBorder.Padding = new Thickness(verticalInset);
                _projectionCaptionOverlayText.Height = double.NaN;
            }
        }

        private void FitProjectionCaptionHorizontalTypographyOnUi()
        {
            double width = _projectionWindow?.ActualWidth > 0 ? _projectionWindow.ActualWidth : DefaultProjectionWidth;
            double height = _projectionWindow?.ActualHeight > 0 ? _projectionWindow.ActualHeight : DefaultProjectionHeight;
            FitProjectionCaptionHorizontalTypographyToViewportOnUi(width, height);
        }

        private void FitProjectionCaptionHorizontalTypographyToViewportOnUi(double viewportWidth, double viewportHeight)
        {
            if (_projectionCaptionOrientation != ProjectionCaptionOrientation.Horizontal ||
                _projectionCaptionOverlayText == null || _projectionCaptionOverlayBorder == null) return;

            var text = _projectionCaptionOverlayText;
            var border = _projectionCaptionOverlayBorder;
            // 每次以未缩放内容为基准，不能把上次的结果当作新的绝对缩放比例。
            text.LayoutTransform = Transform.Identity;
            double borderWidth = Math.Max(1, Math.Min(border.MaxWidth,
                viewportWidth - border.Margin.Left - border.Margin.Right));
            double borderHeight = Math.Max(1, Math.Min(border.MaxHeight,
                viewportHeight - border.Margin.Top - border.Margin.Bottom));
            double AvailableWidth() => Math.Max(1, borderWidth - border.Padding.Left - border.Padding.Right
                - text.Margin.Left - text.Margin.Right - 2);
            double AvailableHeight() => Math.Max(1, borderHeight - border.Padding.Top - border.Padding.Bottom
                - text.Margin.Top - text.Margin.Bottom - 2);
            // FrameworkElement.DesiredSize 包含 Margin；正文测量不能重复扣减或缩放外边距。
            double ContentWidth() => Math.Max(0, text.DesiredSize.Width - text.Margin.Left - text.Margin.Right);
            double ContentHeight() => Math.Max(0, text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom);
            void Measure() => text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

            Measure();
            if (ContentWidth() <= AvailableWidth() && ContentHeight() <= AvailableHeight()) return;
            border.Padding = new Thickness(Math.Min(border.Padding.Left, borderWidth * 0.2),
                Math.Min(border.Padding.Top, borderHeight * 0.2), Math.Min(border.Padding.Right, borderWidth * 0.2),
                Math.Min(border.Padding.Bottom, borderHeight * 0.2));
            // 本机行容量不能决定另一块屏幕的像素容量，按实际 WPF Run 字形及插距测量。
            for (int i = 0; i < 8; i++)
            {
                Measure();
                double scale = Math.Min(AvailableWidth() / Math.Max(1, ContentWidth()),
                    AvailableHeight() / Math.Max(1, ContentHeight()));
                if (scale >= 1) return;
                double nextSize = Math.Max(18, text.FontSize * scale * 0.98);
                if (Math.Abs(nextSize - text.FontSize) < 0.01) break;
                double gap = Math.Max(0, text.LineHeight - text.FontSize) * nextSize / text.FontSize;
                text.FontSize = nextSize;
                text.LineHeight = nextSize + gap;
                text.Height = text.LineHeight * 2;
            }
            Measure();
            double finalScale = Math.Min(1, Math.Min(AvailableWidth() / Math.Max(1, ContentWidth()),
                AvailableHeight() / Math.Max(1, ContentHeight())));
            if (finalScale < 1) text.LayoutTransform = new ScaleTransform(finalScale * 0.98, finalScale * 0.98);
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
            double viewportWidth = _projectionWindow?.ActualWidth > 0 ? _projectionWindow.ActualWidth : DefaultProjectionWidth;
            double viewportHeight = _projectionWindow?.ActualHeight > 0 ? _projectionWindow.ActualHeight : DefaultProjectionHeight;
            _projectionCaptionOverlayBorder.MaxWidth = Math.Max(360, viewportWidth * 0.94);
            _projectionCaptionOverlayBorder.MaxHeight = _projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical
                ? Math.Max(360, viewportHeight * 0.90)
                : Math.Max(160, viewportHeight * 0.46);

            _projectionCaptionOverlayBorder.HorizontalAlignment = ResolveHorizontalAlignment(effectiveHorizontalAnchor);
            _projectionCaptionOverlayBorder.VerticalAlignment = ResolveVerticalAlignment(effectiveVerticalAnchor);

            if (_projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical)
            {
                _projectionCaptionOverlayBorder.MinWidth = 96;
                _projectionCaptionOverlayBorder.Width = double.NaN;
                // 竖排采用“上到下 + 右到左列序”，文本右对齐能稳定把主列固定在右侧。
                _projectionCaptionOverlayText.TextAlignment = TextAlignment.Right;
                _projectionCaptionOverlayText.TextWrapping = TextWrapping.NoWrap;
                _projectionCaptionOverlayText.Height = double.NaN;
                _projectionCaptionOverlayText.Margin = new Thickness(0);
                LiveCaptionDebugLogger.Log(
                    $"[CaptionLayout:Vertical] viewport={viewportWidth:0.#}x{viewportHeight:0.#}, " +
                    $"max={_projectionCaptionOverlayBorder.MaxWidth:0.#}x{_projectionCaptionOverlayBorder.MaxHeight:0.#}, " +
                    $"margin={_projectionCaptionOverlayBorder.Margin.Left:0.#},{_projectionCaptionOverlayBorder.Margin.Top:0.#},{_projectionCaptionOverlayBorder.Margin.Right:0.#},{_projectionCaptionOverlayBorder.Margin.Bottom:0.#}, " +
                    $"padding={_projectionCaptionOverlayBorder.Padding.Left:0.#},{_projectionCaptionOverlayBorder.Padding.Top:0.#}");
                return;
            }

            _projectionCaptionOverlayBorder.MinWidth = 0;
            _projectionCaptionOverlayBorder.Width = double.NaN;
            _projectionCaptionOverlayBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            _projectionCaptionOverlayText.TextAlignment = TextAlignment.Left;
            // 水平字幕严格由 Composer 控制为 2 行，禁用自动换行避免样式变更触发重排漂移。
            _projectionCaptionOverlayText.TextWrapping = TextWrapping.NoWrap;
            _projectionCaptionOverlayText.ClipToBounds = true;
            _projectionCaptionOverlayText.Margin = new Thickness(12, 0, 12, 0);
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
            double maxHeight = _projectionCaptionOverlayBorder?.MaxHeight ?? double.PositiveInfinity;
            double effectiveHeight = Math.Min(viewportHeight - marginTop - marginBottom, maxHeight);
            double available = effectiveHeight - paddingTop - paddingBottom - 8;
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
            int result = Math.Max(1, maxRows);
            LiveCaptionDebugLogger.Log(
                $"[VerticalCapacity] viewportH={viewportHeight:0.#}, maxH={maxHeight:0.#}, effectiveH={effectiveHeight:0.#}, " +
                $"available={available:0.#}, lineHeight={lineHeight:0.##}, rows={result}");
            return result;
        }

        private void ApplyProjectionCaptionAdaptiveTypographyOnUi(string text)
        {
            if (_projectionCaptionOverlayBorder == null || _projectionCaptionOverlayText == null)
            {
                return;
            }

            double viewportWidth = _projectionWindow?.ActualWidth > 0 ? _projectionWindow.ActualWidth : DefaultProjectionWidth;
            double viewportHeight = _projectionWindow?.ActualHeight > 0 ? _projectionWindow.ActualHeight : DefaultProjectionHeight;
            double marginLeft = _projectionCaptionOverlayBorder.Margin.Left;
            double marginRight = _projectionCaptionOverlayBorder.Margin.Right;
            double marginTop = _projectionCaptionOverlayBorder.Margin.Top;
            double marginBottom = _projectionCaptionOverlayBorder.Margin.Bottom;

            double preferredPadding = Math.Clamp(_projectionCaptionPreferredPadding, 10, 72);
            double computedFontSize;
            double computedPadding;
            double computedLineHeight;

            if (_projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical)
            {
                int rows = Math.Max(2, GetVerticalRowCount(text));
                double safeWidth = Math.Max(220, viewportWidth - marginLeft - marginRight - 48);
                double safeHeight = Math.Max(260, viewportHeight - marginTop - marginBottom - 56);
                double widthLimited = safeWidth / 3.6;
                double heightLimited = safeHeight / Math.Min(rows, 16);
                computedFontSize = Math.Clamp(Math.Min(widthLimited, heightLimited), 24, 96);
                computedPadding = Math.Clamp(preferredPadding * (computedFontSize / 56.0), 10, 48);
                computedLineHeight = Math.Clamp(computedFontSize + Math.Clamp(_projectionCaptionPreferredLineGap, 4, 16), computedFontSize * 1.08, computedFontSize * 1.45);
            }
            else
            {
                int longestLineLength = Math.Max(4, GetLongestLineLength(text));
                double safeWidth = Math.Max(420, viewportWidth - marginLeft - marginRight - 64);
                double safeHeight = Math.Max(180, viewportHeight * 0.34 - marginTop - marginBottom - 28);
                double spacingScale = 1.12 + (_projectionCaptionLetterSpacing * 0.11);
                double widthLimited = safeWidth / (Math.Max(1, longestLineLength) * spacingScale);
                double heightLimited = (safeHeight - (preferredPadding * 2) - 24) / 2.35;
                computedFontSize = Math.Clamp(Math.Min(widthLimited, heightLimited), 26, 110);
                computedPadding = Math.Clamp(preferredPadding * (computedFontSize / 62.0), 10, 52);
                computedLineHeight = Math.Clamp(computedFontSize + Math.Clamp(_projectionCaptionPreferredLineGap, 3, 18), computedFontSize * 1.06, computedFontSize * 1.5);
            }

            _projectionCaptionOverlayText.FontSize = computedFontSize;
            _projectionCaptionOverlayText.LineHeight = computedLineHeight;
            _projectionCaptionOverlayBorder.Padding = new Thickness(computedPadding);
            ShrinkProjectionCaptionTypographyToFitOnUi(viewportWidth, viewportHeight);
        }

        private void ShrinkProjectionCaptionTypographyToFitOnUi(double viewportWidth, double viewportHeight)
        {
            if (_projectionCaptionOverlayBorder == null || _projectionCaptionOverlayText == null)
            {
                return;
            }

            double marginLeft = _projectionCaptionOverlayBorder.Margin.Left;
            double marginRight = _projectionCaptionOverlayBorder.Margin.Right;
            double marginTop = _projectionCaptionOverlayBorder.Margin.Top;
            double marginBottom = _projectionCaptionOverlayBorder.Margin.Bottom;
            double maxBorderWidth = Math.Max(300, viewportWidth - marginLeft - marginRight - 12);
            double maxBorderHeight = _projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical
                ? Math.Max(240, viewportHeight - marginTop - marginBottom - 12)
                : Math.Max(120, viewportHeight * 0.34 - marginTop - marginBottom);
            double minFont = _projectionCaptionOrientation == ProjectionCaptionOrientation.Vertical ? 22 : 24;

            for (int i = 0; i < 10; i++)
            {
                double textMaxWidth = Math.Max(120, maxBorderWidth - _projectionCaptionOverlayBorder.Padding.Left - _projectionCaptionOverlayBorder.Padding.Right);
                _projectionCaptionOverlayText.Measure(new System.Windows.Size(textMaxWidth, double.PositiveInfinity));
                double desiredTextHeight = _projectionCaptionOverlayText.DesiredSize.Height;
                double desiredBorderHeight = desiredTextHeight + _projectionCaptionOverlayBorder.Padding.Top + _projectionCaptionOverlayBorder.Padding.Bottom;
                bool overflow = desiredBorderHeight > maxBorderHeight + 0.5;
                if (!overflow)
                {
                    break;
                }

                double nextSize = Math.Max(minFont, _projectionCaptionOverlayText.FontSize * 0.92);
                if (Math.Abs(nextSize - _projectionCaptionOverlayText.FontSize) < 0.1)
                {
                    break;
                }

                _projectionCaptionOverlayText.FontSize = nextSize;
                _projectionCaptionOverlayText.LineHeight = Math.Clamp(_projectionCaptionOverlayText.LineHeight * 0.92, nextSize * 1.05, nextSize * 1.45);
                double nextPadding = Math.Max(8, _projectionCaptionOverlayBorder.Padding.Top * 0.92);
                _projectionCaptionOverlayBorder.Padding = new Thickness(nextPadding);
            }
        }

        private static int GetLongestLineLength(string text)
        {
            string normalized = (text ?? string.Empty).Replace("\r", string.Empty);
            if (normalized.Length == 0)
            {
                return 0;
            }

            int maxLen = 0;
            int current = 0;
            for (int i = 0; i < normalized.Length; i++)
            {
                char ch = normalized[i];
                if (ch == '\n')
                {
                    if (current > maxLen)
                    {
                        maxLen = current;
                    }

                    current = 0;
                    continue;
                }

                current++;
            }

            return current > maxLen ? current : maxLen;
        }

        private static int GetVerticalRowCount(string text)
        {
            string compact = ((text ?? string.Empty).Replace("\r", string.Empty)).Replace("\n", string.Empty);
            if (compact.Length <= 0)
            {
                return 0;
            }

            return (int)Math.Ceiling(compact.Length / 2.0);
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

        private void ApplyProjectionCaptionRuns(string text, int? highlightStart)
        {
            if (_projectionCaptionOverlayText == null)
            {
                return;
            }

            _projectionCaptionOverlayText.Inlines.Clear();
            string safe = text ?? string.Empty;
            if (safe.Length == 0)
            {
                _projectionCaptionOverlayText.Text = string.Empty;
                return;
            }

            int split = CaptionTextElements.FloorBoundary(safe, highlightStart ?? safe.Length);
            string spaced = ApplyProjectionLetterSpacing(safe, out int[] indexMap);
            int splitSpaced = indexMap[Math.Clamp(split, 0, indexMap.Length - 1)];
            if (splitSpaced > 0)
            {
                _projectionCaptionOverlayText.Inlines.Add(new Run(spaced.Substring(0, splitSpaced))
                {
                    Foreground = _projectionCaptionBaseBrush
                });
            }

            if (splitSpaced < spaced.Length)
            {
                _projectionCaptionOverlayText.Inlines.Add(new Run(spaced.Substring(splitSpaced))
                {
                    Foreground = _projectionCaptionLatestBrush
                });
            }
            FitProjectionCaptionHorizontalTypographyOnUi();
        }

        private void ProbeProjectionHorizontalShiftAnomaly(string text, int? highlightStart)
        {
            if (_projectionCaptionOverlayText == null || _projectionCaptionOrientation != ProjectionCaptionOrientation.Horizontal)
            {
                return;
            }

            string safe = text ?? string.Empty;
            if (safe.Length == 0)
            {
                return;
            }

            bool hasWrap = safe.IndexOf('\n') >= 0;
            if (hasWrap)
            {
                return;
            }

            int length = safe.Length;
            bool tooLongSingleLine = length > 31;
            if (!tooLongSingleLine)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _projectionShiftProbeLastLogUtc).TotalMilliseconds < 350)
            {
                return;
            }

            _projectionShiftProbeLastLogUtc = now;
            string msg =
                $"[CaptionShiftProbe:Projection] anomaly singleLineTooLong=true, len={length}, highlight={highlightStart}, " +
                $"font={_projectionCaptionOverlayText.FontSize:0.##}, lineHeight={_projectionCaptionOverlayText.LineHeight:0.##}, " +
                $"wrap={_projectionCaptionOverlayText.TextWrapping}, align={_projectionCaptionOverlayText.TextAlignment}, " +
                $"margin={_projectionCaptionOverlayText.Margin.Left:0.##},{_projectionCaptionOverlayText.Margin.Top:0.##},{_projectionCaptionOverlayText.Margin.Right:0.##},{_projectionCaptionOverlayText.Margin.Bottom:0.##}, " +
                $"borderPadding={_projectionCaptionOverlayBorder?.Padding.Left:0.##}/{_projectionCaptionOverlayBorder?.Padding.Top:0.##}, " +
                $"text='{TrimProjectionCaptionForLog(safe)}'";
            System.Diagnostics.Debug.WriteLine(msg);
            LiveCaptionDebugLogger.Log(msg);
        }

        private static string TrimProjectionCaptionForLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string single = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return single.Length <= 140 ? single : single.Substring(0, 140) + "...";
        }

        private void ApplyProjectionCaptionRunsVertical(string text, int? highlightStart)
        {
            if (_projectionCaptionOverlayText == null)
            {
                return;
            }

            _projectionCaptionOverlayText.Inlines.Clear();
            string source = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(source))
            {
                _projectionCaptionOverlayText.Text = string.Empty;
                return;
            }

            var elements = new List<CaptionTextElement>();
            var compactBuilder = new StringBuilder();
            foreach (CaptionTextElement element in CaptionTextElements.Enumerate(source))
            {
                if (element.Text.IndexOf('\r') >= 0 || element.Text.IndexOf('\n') >= 0) continue;
                elements.Add(element);
                compactBuilder.Append(element.Text);
            }
            string compact = compactBuilder.ToString();
            if (elements.Count == 0)
            {
                _projectionCaptionOverlayText.Text = string.Empty;
                return;
            }

            int splitInSource = CaptionTextElements.FloorBoundary(source, highlightStart ?? source.Length);
            int highlightElementCount = 0;
            foreach (CaptionTextElement element in elements)
                if (element.SourceStart < splitInSource) highlightElementCount++;

            int maxVisibleRows = GetVerticalVisibleRowCapacity();
            int maxVisibleElements = Math.Max(1, maxVisibleRows * 2);
            int overflowElements = Math.Max(0, elements.Count - maxVisibleElements);
            // 竖排按“整列”推进：右列满后，下一字立即切到左列顶部（向上取整）。
            int columnStep = Math.Max(1, maxVisibleRows);
            int startElement = overflowElements > 0
                ? ((overflowElements + columnStep - 1) / columnStep) * columnStep
                : 0;
            int visibleElementCount = Math.Min(maxVisibleElements, Math.Max(0, elements.Count - startElement));
            if (visibleElementCount == 0)
            {
                _projectionCaptionOverlayText.Text = string.Empty;
                return;
            }
            LiveCaptionDebugLogger.Log(
                $"[VerticalPaging] elements={elements.Count}, rows={maxVisibleRows}, cap={maxVisibleElements}, " +
                $"overflow={overflowElements}, step={columnStep}, startElement={startElement}, visibleElements={visibleElementCount}");

            // 竖排输入顺序：先左列（上到下），满后再换到右列（上到下）。
            int leftColumnCount = Math.Min(maxVisibleRows, visibleElementCount);
            int rightColumnCount = Math.Max(0, visibleElementCount - leftColumnCount);
            int rows = Math.Max(rightColumnCount, leftColumnCount);
            ProbeProjectionVerticalAnomaly(
                compact,
                highlightElementCount,
                rows,
                maxVisibleRows,
                startElement,
                rightColumnCount,
                leftColumnCount);

            var runBuffer = new StringBuilder();
            System.Windows.Media.Brush currentBrush = null;

            void Flush()
            {
                if (runBuffer.Length == 0)
                {
                    return;
                }

                _projectionCaptionOverlayText.Inlines.Add(new Run(runBuffer.ToString())
                {
                    Foreground = currentBrush ?? _projectionCaptionBaseBrush
                });
                runBuffer.Clear();
            }

            void Append(string value, int sourceIndex)
            {
                System.Windows.Media.Brush brush = sourceIndex >= splitInSource && sourceIndex >= 0
                    ? _projectionCaptionLatestBrush
                    : _projectionCaptionBaseBrush;
                if (!ReferenceEquals(currentBrush, brush))
                {
                    Flush();
                    currentBrush = brush;
                }

                runBuffer.Append(value);
            }

            for (int i = 0; i < rows; i++)
            {
                int leftIndex = i;
                int rightIndex = leftColumnCount + i;
                int sourceIndexLeft = leftIndex < leftColumnCount ? elements[startElement + leftIndex].SourceStart : -1;
                int sourceIndexRight = rightIndex < visibleElementCount ? elements[startElement + rightIndex].SourceStart : -1;
                string leftChar = leftIndex < leftColumnCount ? elements[startElement + leftIndex].Text : "　";
                string rightChar = rightIndex < visibleElementCount ? elements[startElement + rightIndex].Text : "　";

                // 显示顺序：左列先显示，右列后显示；并加宽列间距。
                Append(leftChar, sourceIndexLeft);
                Append("　", -1);
                Append(rightChar, sourceIndexRight);

                if (i < rows - 1)
                {
                    Flush();
                    currentBrush = null;
                    _projectionCaptionOverlayText.Inlines.Add(new LineBreak());
                }
            }

            Flush();
        }

        private void ProbeProjectionVerticalAnomaly(
            string compact,
            int highlightElementCount,
            int rows,
            int maxVisibleRows,
            int startElement,
            int rightColumnCount,
            int leftColumnCount)
        {
            if (_projectionCaptionOverlayText == null || _projectionCaptionOrientation != ProjectionCaptionOrientation.Vertical)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _projectionVerticalProbeLastLogUtc).TotalMilliseconds < 350)
            {
                return;
            }
            _projectionVerticalProbeLastLogUtc = now;

            int elementCount = System.Globalization.StringInfo.ParseCombiningCharacters(compact).Length;
            bool truncated = elementCount > (maxVisibleRows * 2);
            bool secondLineSparse = leftColumnCount > 0 && leftColumnCount <= 2 && rightColumnCount >= 8;
            string msg =
                $"[CaptionShiftProbe:Vertical] truncated={truncated}, secondLineSparse={secondLineSparse}, " +
                $"rows={rows}, visibleRows={maxVisibleRows}, startElement={startElement}, highlightElements={highlightElementCount}, " +
                $"elements={elementCount}, rightElements={rightColumnCount}, leftElements={leftColumnCount}, " +
                $"font={_projectionCaptionOverlayText.FontSize:0.##}, lineHeight={_projectionCaptionOverlayText.LineHeight:0.##}, " +
                $"padding={_projectionCaptionOverlayBorder?.Padding.Left:0.##}/{_projectionCaptionOverlayBorder?.Padding.Top:0.##}, " +
                $"text='{TrimProjectionCaptionForLog(compact)}'";
            System.Diagnostics.Debug.WriteLine(msg);
            LiveCaptionDebugLogger.Log(msg);
        }

        private string ApplyProjectionLetterSpacing(string text, out int[] indexMap)
        {
            return CaptionTextElements.ApplyLetterSpacing(text, _projectionCaptionLetterSpacing, out indexMap);
        }

        private static bool TryParseProjectionCaptionColor(string value, out System.Windows.Media.Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                object converted = System.Windows.Media.ColorConverter.ConvertFromString(value.Trim());
                if (converted is System.Windows.Media.Color parsed)
                {
                    color = parsed;
                    return true;
                }
            }
            catch
            {
            }

            return false;
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
