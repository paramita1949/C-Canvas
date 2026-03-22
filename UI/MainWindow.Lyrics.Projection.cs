using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Services.Projection.Output;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSize = System.Windows.Size;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfMessageBox = System.Windows.MessageBox;
using WpfImage = System.Windows.Controls.Image;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Lyrics Projection
    /// </summary>
    public partial class MainWindow
    {
        private bool _lyricsTransparentIdleFramePushed;

        private void RenderLyricsToProjection()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RenderLyricsToProjection), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            Action caretRestore = () => { };
            try
            {
                caretRestore = HideAllLyricsCaretsForProjection();
                var (physicalWidth, physicalHeight) = _projectionManager.GetCurrentProjectionPhysicalSize();

                if (_lyricsSplitMode != (int)ViewSplitMode.Single)
                {
                    RenderSplitLyricsToProjection(physicalWidth, physicalHeight);
                    return;
                }

                var skBitmap = BuildSingleLyricsProjectionBitmap(physicalWidth, physicalHeight, ndiInvertedLayout: false);
                if (skBitmap == null)
                {
                    return;
                }

                SKBitmap ndiBitmap = skBitmap;
                SKBitmap ndiOwnedBitmap = null;
                try
                {
                    _projectionManager?.UpdateProjectionText(skBitmap);

                    if (ShouldUseNdiInvertedLyricsLayout())
                    {
                        ndiOwnedBitmap = BuildSingleLyricsProjectionBitmap(physicalWidth, physicalHeight, ndiInvertedLayout: true);
                        if (ndiOwnedBitmap != null)
                        {
                            ndiBitmap = ndiOwnedBitmap;
                        }
                    }

                    TryPublishLyricsFrameToNdi(ndiBitmap);
                }
                finally
                {
                    ndiOwnedBitmap?.Dispose();
                    skBitmap.Dispose();
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"投影失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                caretRestore();
            }
        }

        private Action HideAllLyricsCaretsForProjection()
        {
            var allEditors = new[] { LyricsTextBox, LyricsSplitTextBox1, LyricsSplitTextBox2, LyricsSplitTextBox3, LyricsSplitTextBox4 };
            var backups = new List<(WpfTextBox Tb, System.Windows.Media.Brush Brush)>(allEditors.Length);
            foreach (var tb in allEditors)
            {
                if (tb == null)
                {
                    continue;
                }

                backups.Add((tb, tb.CaretBrush));
                tb.CaretBrush = WpfBrushes.Transparent;
            }

            return () =>
            {
                foreach (var (tb, brush) in backups)
                {
                    tb.CaretBrush = brush;
                }
            };
        }

        private void RenderSplitLyricsToProjection(double physicalWidth, double physicalHeight)
        {
            if (LyricsSplitGrid == null || LyricsSplitGrid.ActualWidth <= 0 || LyricsSplitGrid.ActualHeight <= 0)
            {
                return;
            }

            var canvas = new Canvas
            {
                Width = physicalWidth,
                Height = physicalHeight,
                Background = new SolidColorBrush(GetCurrentLyricsThemeBackgroundColor())
            };

            var (wpfWidth, _) = _projectionManager.GetProjectionScreenSize();
            double fontScale = physicalWidth / wpfWidth;
            foreach (var region in GetSplitRenderRegions(physicalWidth, physicalHeight))
            {
                double regionFontSize = GetSplitProjectionFontSizeForRegion(region.RegionIndex);
                AddSplitRegionTextElement(canvas, region.Rect, region.Editor, region.Text, fontScale, regionFontSize);
            }

            AddSplitOverlayForProjection(canvas, physicalWidth, physicalHeight);

            canvas.Measure(new WpfSize(physicalWidth, physicalHeight));
            canvas.Arrange(new Rect(0, 0, physicalWidth, physicalHeight));
            canvas.UpdateLayout();

            var renderBitmap = new RenderTargetBitmap((int)physicalWidth, (int)physicalHeight, 96, 96, PixelFormats.Pbgra32);
            renderBitmap.Render(canvas);
            renderBitmap.Freeze();

            var skBitmap = ConvertToSKBitmap(renderBitmap);
            if (skBitmap != null)
            {
                _projectionManager?.UpdateProjectionText(skBitmap);
                TryPublishLyricsFrameToNdi(skBitmap);
                skBitmap.Dispose();
            }
        }

        private void RenderPagingRegionToProjection(double physicalWidth, double physicalHeight)
        {
            int regionIndex = ClampPagingRegionIndex(_lyricsCurrentPageIndex);
            var regions = GetSplitRenderRegions(physicalWidth, physicalHeight).ToList();
            if (regions.Count == 0)
            {
                return;
            }

            var current = regions[Math.Min(regionIndex, regions.Count - 1)];
            var canvas = new Canvas
            {
                Width = physicalWidth,
                Height = physicalHeight,
                Background = new SolidColorBrush(GetCurrentLyricsThemeBackgroundColor())
            };

            var (wpfWidth, _) = _projectionManager.GetProjectionScreenSize();
            double fontScale = physicalWidth / wpfWidth;
            double regionFontSize = GetSplitProjectionFontSizeForRegion(current.RegionIndex);
            AddSplitRegionTextElement(canvas, new Rect(0, 0, physicalWidth, physicalHeight), current.Editor, current.Text, fontScale, regionFontSize);

            canvas.Measure(new WpfSize(physicalWidth, physicalHeight));
            canvas.Arrange(new Rect(0, 0, physicalWidth, physicalHeight));
            canvas.UpdateLayout();

            var renderBitmap = new RenderTargetBitmap((int)physicalWidth, (int)physicalHeight, 96, 96, PixelFormats.Pbgra32);
            renderBitmap.Render(canvas);
            renderBitmap.Freeze();

            var skBitmap = ConvertToSKBitmap(renderBitmap);
            if (skBitmap != null)
            {
                _projectionManager?.UpdateProjectionText(skBitmap);
                TryPublishLyricsFrameToNdi(skBitmap);
                skBitmap.Dispose();
            }
        }

        private void TryPublishLyricsFrameToNdi(SKBitmap frame)
        {
            try
            {
                if (_projectionNdiOutputManager == null || frame == null)
                {
                    return;
                }

                bool transparentEnabled = _configManager?.ProjectionNdiEnabled == true
                    && _configManager.ProjectionNdiLyricsTransparentEnabled;

                bool transparentLyricsMode = IsLyricsTransparentNdiMode();

                // 透明已开启但未进入“歌词切片透明模式”时，不发送歌词内容，仅保持透明空帧通道。
                if (transparentEnabled && !transparentLyricsMode)
                {
                    PushTransparentIdleFrameToNdi(frame);
                    return;
                }

                if (transparentLyricsMode)
                {
                    var bg = GetCurrentLyricsThemeBackgroundColor();
                    _projectionNdiOutputManager.PublishFrame(
                        frame,
                        ProjectionNdiContentType.Lyrics,
                        new SKColor(bg.R, bg.G, bg.B, bg.A));
                    _lyricsTransparentIdleFramePushed = false;
                }
                else
                {
                    _projectionNdiOutputManager.PublishFrame(frame, ProjectionNdiContentType.Slide);
                    _lyricsTransparentIdleFramePushed = false;
                }
            }
            catch
            {
                // NDI 输出异常不影响本地投影显示。
            }
        }

        private void PushTransparentIdleFrameToNdi(SKBitmap referenceFrame)
        {
            if (_lyricsTransparentIdleFramePushed || referenceFrame == null || _projectionNdiOutputManager == null)
            {
                return;
            }

            using var transparentFrame = new SKBitmap(
                referenceFrame.Width,
                referenceFrame.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);
            transparentFrame.Erase(new SKColor(0, 0, 0, 0));
            _projectionNdiOutputManager.PublishFrame(transparentFrame, ProjectionNdiContentType.Lyrics);
            _lyricsTransparentIdleFramePushed = true;
        }

        private SKBitmap BuildSingleLyricsProjectionBitmap(double physicalWidth, double physicalHeight, bool ndiInvertedLayout)
        {
            var canvas = new Canvas
            {
                Width = physicalWidth,
                Height = physicalHeight,
                Background = new SolidColorBrush(GetCurrentLyricsThemeBackgroundColor())
            };

            double actualHeight = physicalHeight;
            var (wpfWidth, _) = _projectionManager.GetProjectionScreenSize();
            double fontScale = physicalWidth / wpfWidth;

            var textBlock = new TextBlock
            {
                Text = GetCurrentSingleLyricsProjectionText(),
                FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                FontSize = _lyricsProjectionFontSize * fontScale,
                Foreground = LyricsTextBox.Foreground,
                TextAlignment = LyricsTextBox.TextAlignment,
                TextWrapping = TextWrapping.Wrap,
                Width = physicalWidth,
                Padding = new Thickness(60 * fontScale, 40 * fontScale, 60 * fontScale, 40 * fontScale),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            };

            textBlock.Measure(new WpfSize(physicalWidth, double.PositiveInfinity));
            double textBlockHeight = textBlock.DesiredSize.Height;
            if (textBlockHeight > physicalHeight)
            {
                actualHeight = textBlockHeight;
                canvas.Height = actualHeight;
            }

            Canvas.SetLeft(textBlock, 0);
            Canvas.SetTop(textBlock, ndiInvertedLayout ? Math.Max(0, actualHeight - textBlockHeight) : 0);
            canvas.Children.Add(textBlock);
            AddImageWatermarkToProjection(canvas, physicalWidth, actualHeight, fontScale, placeTop: ndiInvertedLayout);
            AddSongNameWatermarkToProjection(canvas, physicalWidth, actualHeight, fontScale, placeTop: ndiInvertedLayout);

            canvas.Measure(new WpfSize(physicalWidth, actualHeight));
            canvas.Arrange(new Rect(0, 0, physicalWidth, actualHeight));
            canvas.UpdateLayout();

            var renderBitmap = new RenderTargetBitmap(
                (int)physicalWidth, (int)Math.Ceiling(actualHeight), 96, 96, PixelFormats.Pbgra32);
            renderBitmap.Render(canvas);
            renderBitmap.Freeze();

            return ConvertToSKBitmap(renderBitmap);
        }

        private bool ShouldUseNdiInvertedLyricsLayout()
        {
            return _configManager?.ProjectionNdiEnabled == true
                && _configManager.ProjectionNdiLyricsTransparentEnabled;
        }

        private void AddSplitRegionTextElement(Canvas canvas, Rect rect, WpfTextBox editor, string text, double fontScale, double regionFontSize)
        {
            var host = new Grid
            {
                Width = Math.Max(0, rect.Width),
                Height = Math.Max(0, rect.Height),
                Background = WpfBrushes.Transparent
            };

            var textBlock = new TextBlock
            {
                Text = text ?? "",
                FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                FontSize = regionFontSize * fontScale,
                Foreground = editor?.Foreground ?? LyricsTextBox.Foreground ?? WpfBrushes.White,
                TextAlignment = editor?.TextAlignment ?? TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Padding = new Thickness(10 * fontScale, 30 * fontScale, 10 * fontScale, 10 * fontScale)
            };

            host.Children.Add(textBlock);
            Canvas.SetLeft(host, rect.X);
            Canvas.SetTop(host, rect.Y);
            canvas.Children.Add(host);
        }

        private void AddSplitOverlayForProjection(Canvas canvas, double width, double height)
        {
            double halfWidth = width / 2.0;
            double halfHeight = height / 2.0;

            var lineBrush = new SolidColorBrush(WpfColor.FromRgb(255, 59, 48));
            double lineThickness = 2;
            double labelScale = Math.Max(1.0, Math.Min(width, height) / 1080.0);

            switch ((ViewSplitMode)_lyricsSplitMode)
            {
                case ViewSplitMode.Horizontal:
                    canvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = halfWidth, Y1 = 0, X2 = halfWidth, Y2 = height,
                        Stroke = lineBrush, StrokeThickness = lineThickness
                    });
                    AddProjectionRegionLabel(canvas, "1", 0, 0, labelScale);
                    AddProjectionRegionLabel(canvas, "2", halfWidth, 0, labelScale);
                    break;
                case ViewSplitMode.Vertical:
                    canvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = 0, Y1 = halfHeight, X2 = width, Y2 = halfHeight,
                        Stroke = lineBrush, StrokeThickness = lineThickness
                    });
                    AddProjectionRegionLabel(canvas, "1", 0, 0, labelScale);
                    AddProjectionRegionLabel(canvas, "2", 0, halfHeight, labelScale);
                    break;
                case ViewSplitMode.TripleSplit:
                    canvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = halfWidth, Y1 = 0, X2 = halfWidth, Y2 = height,
                        Stroke = lineBrush, StrokeThickness = lineThickness
                    });
                    canvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = 0, Y1 = halfHeight, X2 = halfWidth, Y2 = halfHeight,
                        Stroke = lineBrush, StrokeThickness = lineThickness
                    });
                    AddProjectionRegionLabel(canvas, "1", 0, 0, labelScale);
                    AddProjectionRegionLabel(canvas, "2", 0, halfHeight, labelScale);
                    AddProjectionRegionLabel(canvas, "3", halfWidth, 0, labelScale);
                    break;
                case ViewSplitMode.Quad:
                    canvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = halfWidth, Y1 = 0, X2 = halfWidth, Y2 = height,
                        Stroke = lineBrush, StrokeThickness = lineThickness
                    });
                    canvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = 0, Y1 = halfHeight, X2 = width, Y2 = halfHeight,
                        Stroke = lineBrush, StrokeThickness = lineThickness
                    });
                    AddProjectionRegionLabel(canvas, "1", 0, 0, labelScale);
                    AddProjectionRegionLabel(canvas, "2", halfWidth, 0, labelScale);
                    AddProjectionRegionLabel(canvas, "3", 0, halfHeight, labelScale);
                    AddProjectionRegionLabel(canvas, "4", halfWidth, halfHeight, labelScale);
                    break;
            }
        }

        private void AddProjectionRegionLabel(Canvas canvas, string text, double x, double y, double scale)
        {
            double fontSize = Math.Max(28, 24 * scale);
            double padX = Math.Max(12, 10 * scale);
            double padY = Math.Max(5, 4 * scale);
            double radius = Math.Max(10, 8 * scale);

            var label = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromArgb(200, 255, 102, 0)),
                CornerRadius = new CornerRadius(0, 0, radius, 0),
                Padding = new Thickness(padX, padY, padX, padY),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = WpfBrushes.White
                }
            };

            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            Canvas.SetZIndex(label, 1000);
            canvas.Children.Add(label);
        }

        private void AddSongNameWatermarkToProjection(Canvas canvas, double width, double height, double fontScale, bool placeTop = false)
        {
            string watermark = GetCurrentLyricsSongWatermarkText();
            if (string.IsNullOrWhiteSpace(watermark))
            {
                return;
            }

            double marginX = Math.Max(22, 18 * fontScale);
            double marginY = Math.Max(18, 14 * fontScale);
            double fontSize = Math.Max(MinLyricsTextWatermarkFontSize, _lyricsTextWatermarkFontSize * fontScale);
            var watermarkBrush = ResolveCurrentLyricsProjectionTextBrush();

            var text = new TextBlock
            {
                Text = watermark,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = watermarkBrush,
                IsHitTestVisible = false
            };

            text.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            var size = text.DesiredSize;
            Canvas.SetLeft(text, marginX);
            Canvas.SetTop(text, placeTop ? marginY : Math.Max(0, height - size.Height - marginY));
            Canvas.SetZIndex(text, 1200);
            canvas.Children.Add(text);
        }

        private WpfBrush ResolveCurrentLyricsProjectionTextBrush()
        {
            string configuredHex = NormalizeLyricsTextWatermarkColorHex(_lyricsTextWatermarkColorHex);
            if (!string.IsNullOrWhiteSpace(configuredHex))
            {
                var configured = HexToColor(configuredHex);
                var configuredBrush = new SolidColorBrush(WpfColor.FromArgb(128, configured.R, configured.G, configured.B));
                if (configuredBrush.CanFreeze)
                {
                    configuredBrush.Freeze();
                }

                return configuredBrush;
            }

            if (LyricsTextBox?.Foreground is WpfBrush source)
            {
                var cloned = source.CloneCurrentValue();
                cloned.Opacity = Math.Clamp(source.Opacity * 0.5, 0.0, 1.0);
                if (cloned.CanFreeze)
                {
                    cloned.Freeze();
                }

                return cloned;
            }

            return new SolidColorBrush(WpfColor.FromArgb(128, 255, 255, 255));
        }

        private void AddImageWatermarkToProjection(Canvas canvas, double width, double height, double fontScale, bool placeTop = false)
        {
            string imagePath = GetCurrentLyricsWatermarkImagePath();
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                {
                    return;
                }

                double maxWidth = Math.Max(180, width * 0.16);
                double maxHeight = Math.Max(90, height * 0.12);
                double scale = Math.Min(maxWidth / bitmap.PixelWidth, maxHeight / bitmap.PixelHeight);
                scale = Math.Min(1.0, Math.Max(0.05, scale));

                double drawWidth = bitmap.PixelWidth * scale;
                double drawHeight = bitmap.PixelHeight * scale;
                double marginX = Math.Max(24, 20 * fontScale);
                double marginY = Math.Max(18, 14 * fontScale);

                var image = new WpfImage
                {
                    Source = bitmap,
                    Width = drawWidth,
                    Height = drawHeight,
                    Stretch = Stretch.Fill,
                    Opacity = 0.95,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(image, Math.Max(0, width - drawWidth - marginX));
                Canvas.SetTop(image, placeTop ? marginY : Math.Max(0, height - drawHeight - marginY));
                Canvas.SetZIndex(image, 1300);
                canvas.Children.Add(image);
            }
            catch
            {
                // 水印加载失败不影响歌词投影
            }
        }

        private bool IsLyricsTransparentNdiMode()
        {
            return _configManager?.ProjectionNdiEnabled == true
                && _configManager.ProjectionNdiLyricsTransparentEnabled
                && _lyricsSliceModeEnabled
                && _lyricsSplitMode == (int)ViewSplitMode.Single;
        }

        private IEnumerable<(Rect Rect, WpfTextBox Editor, string Text, int RegionIndex)> GetSplitRenderRegions(double width, double height)
        {
            double halfWidth = width / 2.0;
            double halfHeight = height / 2.0;

            switch ((ViewSplitMode)_lyricsSplitMode)
            {
                case ViewSplitMode.Horizontal:
                    yield return (new Rect(0, 0, halfWidth, height), LyricsSplitTextBox1, LyricsSplitTextBox1.Text ?? "", 0);
                    yield return (new Rect(halfWidth, 0, width - halfWidth, height), LyricsSplitTextBox2, LyricsSplitTextBox2.Text ?? "", 1);
                    break;
                case ViewSplitMode.Vertical:
                    yield return (new Rect(0, 0, width, halfHeight), LyricsSplitTextBox1, LyricsSplitTextBox1.Text ?? "", 0);
                    yield return (new Rect(0, halfHeight, width, height - halfHeight), LyricsSplitTextBox2, LyricsSplitTextBox2.Text ?? "", 1);
                    break;
                case ViewSplitMode.TripleSplit:
                    yield return (new Rect(0, 0, halfWidth, halfHeight), LyricsSplitTextBox1, LyricsSplitTextBox1.Text ?? "", 0);
                    yield return (new Rect(0, halfHeight, halfWidth, height - halfHeight), LyricsSplitTextBox2, LyricsSplitTextBox2.Text ?? "", 1);
                    yield return (new Rect(halfWidth, 0, width - halfWidth, height), LyricsSplitTextBox3, LyricsSplitTextBox3.Text ?? "", 2);
                    break;
                case ViewSplitMode.Quad:
                    yield return (new Rect(0, 0, halfWidth, halfHeight), LyricsSplitTextBox1, LyricsSplitTextBox1.Text ?? "", 0);
                    yield return (new Rect(halfWidth, 0, width - halfWidth, halfHeight), LyricsSplitTextBox2, LyricsSplitTextBox2.Text ?? "", 1);
                    yield return (new Rect(0, halfHeight, halfWidth, height - halfHeight), LyricsSplitTextBox3, LyricsSplitTextBox3.Text ?? "", 2);
                    yield return (new Rect(halfWidth, halfHeight, width - halfWidth, height - halfHeight), LyricsSplitTextBox4, LyricsSplitTextBox4.Text ?? "", 3);
                    break;
            }
        }

        /// <summary>
        /// 将WPF BitmapSource转换为SKBitmap
        /// </summary>
        private SKBitmap ConvertToSKBitmap(BitmapSource bitmapSource)
        {
            try
            {
                int width = bitmapSource.PixelWidth;
                int height = bitmapSource.PixelHeight;

                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = bitmapSource;
                converted.DestinationFormat = PixelFormats.Bgra32;
                converted.EndInit();

                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                IntPtr pixelsPtr = skBitmap.GetPixels();
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, pixelsPtr, pixels.Length);
                return skBitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
