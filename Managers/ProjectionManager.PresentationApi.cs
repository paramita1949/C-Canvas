using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using ImageColorChanger.Core;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 展示 API 与交互入口逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        public bool HasBibleVersePopup =>
            _projectionBiblePopupBorder != null &&
            _projectionBiblePopupBorder.Visibility == Visibility.Visible;

        /// <summary>
        /// 设置投影窗口媒体文件名显示
        /// </summary>
        public void SetProjectionMediaFileName(string fileName, bool isAudioOnly)
        {
            if (_projectionWindow == null || _projectionMediaFileNameBorder == null || _projectionMediaFileNameText == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { UpdateProjectionMediaFileNameOnUi(fileName, isAudioOnly); });
        }

        /// <summary>
        /// 设置圣经标题（固定在顶部）
        /// </summary>
        public void SetBibleTitle(string title, bool visible)
        {
            if (_projectionWindow == null || _projectionBibleTitleBorder == null || _projectionBibleTitleText == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { UpdateBibleTitleOnUi(title, visible); });
        }

        /// <summary>
        /// 设置投影端圣经固定标题样式（字体/字号/颜色/背景）。
        /// </summary>
        public void SetBibleTitleStyle(
            string fontFamily,
            double fontSize,
            string titleColorHex,
            string backgroundColorHex,
            double sourceDpiScaleY = 1.0d,
            double contentScaleRatio = 1.0d)
        {
            if (_projectionWindow == null || _projectionBibleTitleBorder == null || _projectionBibleTitleText == null)
            {
                return;
            }

            RunOnMainDispatcher(() =>
            {
                UpdateBibleTitleStyleOnUi(
                    fontFamily,
                    fontSize,
                    titleColorHex,
                    backgroundColorHex,
                    sourceDpiScaleY,
                    contentScaleRatio);
            });
        }

        /// <summary>
        /// 显示圣经弹窗（叠加在投影层，不切换模式）。
        /// </summary>
        public void ShowBibleVersePopup(string reference, string content, BibleTextInsertConfig config, int autoHideSeconds = 10)
        {
            if (_projectionWindow == null ||
                _projectionBiblePopupBorder == null ||
                _projectionBiblePopupReferenceText == null ||
                _projectionBiblePopupContentText == null)
            {
                return;
            }

            RunOnMainDispatcher(() =>
            {
                ApplyBibleVersePopupStyle(config);
                _projectionBiblePopupReferenceText.Text = reference ?? string.Empty;
                _projectionBiblePopupContentText.Text = content ?? string.Empty;
                _projectionBiblePopupBorder.Visibility = Visibility.Visible;
                StartBibleVersePopupAutoHide(autoHideSeconds);
            });
        }

        /// <summary>
        /// 手动隐藏圣经弹窗。
        /// </summary>
        public void HideBibleVersePopup()
        {
            if (_projectionWindow == null || _projectionBiblePopupBorder == null)
            {
                return;
            }

            RunOnMainDispatcher(HideBibleVersePopupOnUi);
        }

        /// <summary>
        /// 直接设置投影滚动位置（用于圣经同步）
        /// </summary>
        public void SetProjectionScrollPosition(double offset, bool shouldDebug = false)
        {
            _ = shouldDebug;
            if (!_syncEnabled || _projectionWindow == null || _projectionScrollViewer == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { SetProjectionVerticalOffsetWithStabilization(offset); });
        }

        /// <summary>
        /// 按比例设置投影滚动位置（用于圣经同步，确保像素级对齐）
        /// </summary>
        public void SetProjectionScrollPositionByRatio(double scrollRatio, bool shouldDebug = false)
        {
            _ = shouldDebug;
            if (!_syncEnabled || _projectionWindow == null || _projectionScrollViewer == null)
            {
                return;
            }

            RunOnMainDispatcher(() =>
            {
                double projScrollableHeight = _projectionScrollViewer.ScrollableHeight;
                double projScrollOffset = ProjectionScrollPolicy.CalculateByScrollableHeightRatio(scrollRatio, projScrollableHeight);
                SetProjectionVerticalOffsetWithStabilization(projScrollOffset);
            });
        }

        /// <summary>
        /// 显示视频投影（隐藏图片，显示视频）
        /// </summary>
        public void ShowVideoProjection()
        {
            if (_projectionWindow == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { SetProjectionMode(showVideo: true); });
        }

        /// <summary>
        /// 显示图片投影（隐藏视频，显示图片）
        /// </summary>
        public void ShowImageProjection()
        {
            if (_projectionWindow == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { SetProjectionMode(showVideo: false); });
        }

        /// <summary>
        /// 投影窗口键盘事件处理
        /// </summary>
        private void ProjectionWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            _ = sender;
            try
            {
                _host.ForwardProjectionKeyDown(e);
                e.Handled = true;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 导航到上一张图片
        /// </summary>
        private void NavigateToPreviousImage()
        {
            RunOnMainDispatcher(() => { _host.SwitchToPreviousSimilarImage(); });
        }

        /// <summary>
        /// 导航到下一张图片
        /// </summary>
        private void NavigateToNextImage()
        {
            RunOnMainDispatcher(() => { _host.SwitchToNextSimilarImage(); });
        }

        /// <summary>
        /// 捕获投影窗口当前最终可见帧（用于 NDI，确保与投影窗口显示一致）。
        /// </summary>
        public SKBitmap CaptureProjectionViewportFrameForNdi()
        {
            if (_projectionWindow == null)
            {
                return null;
            }

            return RunOnMainDispatcher(CaptureProjectionViewportFrameForNdiOnUiThread);
        }

        private SKBitmap CaptureProjectionViewportFrameForNdiOnUiThread()
        {
            if (_projectionWindow == null)
            {
                return null;
            }

            FrameworkElement visual = _projectionWindow.Content as FrameworkElement
                ?? _projectionScrollViewer as FrameworkElement
                ?? _projectionContainer as FrameworkElement;

            if (visual == null)
            {
                return null;
            }

            visual.UpdateLayout();

            double dipWidth = _projectionWindow.ActualWidth > 1 ? _projectionWindow.ActualWidth : visual.ActualWidth;
            double dipHeight = _projectionWindow.ActualHeight > 1 ? _projectionWindow.ActualHeight : visual.ActualHeight;
            if (dipWidth <= 1 || dipHeight <= 1)
            {
                return null;
            }

            var dpi = VisualTreeHelper.GetDpi(_projectionWindow);
            int pixelWidth = Math.Max(1, (int)Math.Round(dipWidth * dpi.DpiScaleX));
            int pixelHeight = Math.Max(1, (int)Math.Round(dipHeight * dpi.DpiScaleY));

            var renderBitmap = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            renderBitmap.Render(visual);
            renderBitmap.Freeze();

            return ConvertBitmapSourceToSkBitmap(renderBitmap);
        }

        private static SKBitmap ConvertBitmapSourceToSkBitmap(BitmapSource bitmapSource)
        {
            int width = bitmapSource.PixelWidth;
            int height = bitmapSource.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            bitmapSource.CopyPixels(pixels, stride, 0);

            var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            IntPtr ptr = skBitmap.GetPixels();
            if (ptr == IntPtr.Zero)
            {
                skBitmap.Dispose();
                return null;
            }

            Marshal.Copy(pixels, 0, ptr, pixels.Length);
            return skBitmap;
        }

        private void ProjectionBiblePopupCloseButton_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            HideBibleVersePopup();
        }

        private void ApplyBibleVersePopupStyle(BibleTextInsertConfig config)
        {
            config ??= new BibleTextInsertConfig();

            if (_projectionBiblePopupReferenceText != null)
            {
                _projectionBiblePopupReferenceText.FontFamily = new WpfFontFamily(config.FontFamily);
                _projectionBiblePopupReferenceText.FontSize = config.TitleStyle.FontSize;
                _projectionBiblePopupReferenceText.FontWeight = config.TitleStyle.IsBold ? FontWeights.Bold : FontWeights.Normal;
                _projectionBiblePopupReferenceText.Foreground = BuildSolidBrush(config.TitleStyle.ColorHex, 100);
            }

            if (_projectionBiblePopupContentText != null)
            {
                _projectionBiblePopupContentText.FontFamily = new WpfFontFamily(config.FontFamily);
                _projectionBiblePopupContentText.FontSize = config.VerseStyle.FontSize;
                _projectionBiblePopupContentText.FontWeight = config.VerseStyle.IsBold ? FontWeights.Bold : FontWeights.Normal;
                _projectionBiblePopupContentText.Foreground = BuildSolidBrush(config.VerseStyle.ColorHex, 100);

                double lineHeight = config.VerseStyle.FontSize * Math.Max(1.0, config.VerseStyle.VerseSpacing);
                _projectionBiblePopupContentText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                _projectionBiblePopupContentText.LineHeight = lineHeight;
            }

            if (_projectionBiblePopupBorder != null)
            {
                _projectionBiblePopupBorder.Background = BuildSolidBrush(
                    config.PopupBackgroundColorHex,
                    Math.Clamp(config.PopupBackgroundOpacity, 0, 100));
                _projectionBiblePopupBorder.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(60, 255, 255, 255));
                ApplyBibleVersePopupPosition(config.PopupPosition, config.PopupVerseCount);
            }
        }

        private void ApplyBibleVersePopupPosition(BiblePopupPosition position, int popupVerseCount)
        {
            if (_projectionBiblePopupBorder == null)
            {
                return;
            }

            switch (position)
            {
                case BiblePopupPosition.Top:
                    _projectionBiblePopupBorder.VerticalAlignment = VerticalAlignment.Top;
                    _projectionBiblePopupBorder.Margin = new Thickness(30, 0, 30, 0);
                    break;
                case BiblePopupPosition.Center:
                    _projectionBiblePopupBorder.VerticalAlignment = VerticalAlignment.Center;
                    _projectionBiblePopupBorder.Margin = new Thickness(30, 0, 30, 0);
                    break;
                default:
                    _projectionBiblePopupBorder.VerticalAlignment = VerticalAlignment.Bottom;
                    _projectionBiblePopupBorder.Margin = new Thickness(30, 0, 30, 40);
                    break;
            }

            if (_projectionBiblePopupContentScrollViewer != null && _projectionBiblePopupContentText != null)
            {
                int visibleVerseCount = Math.Clamp(popupVerseCount, 1, 10);
                _projectionBiblePopupContentScrollViewer.MaxHeight = _projectionBiblePopupContentText.LineHeight * visibleVerseCount;
                _projectionBiblePopupContentScrollViewer.ScrollToVerticalOffset(0);
            }
        }

        private void StartBibleVersePopupAutoHide(int autoHideSeconds)
        {
            int safeSeconds = Math.Max(1, autoHideSeconds);
            if (_projectionBiblePopupTimer == null)
            {
                _projectionBiblePopupTimer = new System.Windows.Threading.DispatcherTimer();
                _projectionBiblePopupTimer.Tick += (_, __) => HideBibleVersePopupOnUi();
            }

            _projectionBiblePopupTimer.Stop();
            _projectionBiblePopupTimer.Interval = TimeSpan.FromSeconds(safeSeconds);
            _projectionBiblePopupTimer.Start();
        }

        private void HideBibleVersePopupOnUi()
        {
            _projectionBiblePopupTimer?.Stop();
            if (_projectionBiblePopupBorder != null)
            {
                _projectionBiblePopupBorder.Visibility = Visibility.Collapsed;
            }
        }

        private static SolidColorBrush BuildSolidBrush(string hexColor, int opacityPercent)
        {
            WpfColor baseColor;
            try
            {
                baseColor = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hexColor ?? "#000000");
            }
            catch
            {
                baseColor = WpfColor.FromRgb(0, 0, 0);
            }

            byte alpha = (byte)Math.Clamp((int)Math.Round(opacityPercent * 2.55), 0, 255);
            var color = WpfColor.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
            return new SolidColorBrush(color);
        }
    }
}
