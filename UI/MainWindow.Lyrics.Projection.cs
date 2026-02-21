using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Database.Models.Enums;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSize = System.Windows.Size;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfMessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Lyrics Projection
    /// </summary>
    public partial class MainWindow
    {
        private void RenderLyricsToProjection()
        {
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

                var canvas = new Canvas
                {
                    Width = physicalWidth,
                    Height = physicalHeight,
                    Background = WpfBrushes.Black
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
                Canvas.SetTop(textBlock, 0);
                canvas.Children.Add(textBlock);
                AddSongNameWatermarkToProjection(canvas, physicalWidth, actualHeight, fontScale);

                canvas.Measure(new WpfSize(physicalWidth, actualHeight));
                canvas.Arrange(new Rect(0, 0, physicalWidth, actualHeight));
                canvas.UpdateLayout();

                var renderBitmap = new RenderTargetBitmap(
                    (int)physicalWidth, (int)Math.Ceiling(actualHeight), 96, 96, PixelFormats.Pbgra32);
                renderBitmap.Render(canvas);
                renderBitmap.Freeze();

                var skBitmap = ConvertToSKBitmap(renderBitmap);
                if (skBitmap != null)
                {
                    _projectionManager?.UpdateProjectionText(skBitmap);
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
            if (_lyricsPagingMode)
            {
                RenderPagingRegionToProjection(physicalWidth, physicalHeight);
                return;
            }

            if (LyricsSplitGrid == null || LyricsSplitGrid.ActualWidth <= 0 || LyricsSplitGrid.ActualHeight <= 0)
            {
                return;
            }

            var canvas = new Canvas
            {
                Width = physicalWidth,
                Height = physicalHeight,
                Background = WpfBrushes.Black
            };

            var (wpfWidth, _) = _projectionManager.GetProjectionScreenSize();
            double fontScale = physicalWidth / wpfWidth;
            foreach (var region in GetSplitRenderRegions(physicalWidth, physicalHeight))
            {
                AddSplitRegionTextElement(canvas, region.Rect, region.Editor, region.Text, fontScale);
            }

            AddSplitOverlayForProjection(canvas, physicalWidth, physicalHeight);
            AddSongNameWatermarkToProjection(canvas, physicalWidth, physicalHeight, fontScale);

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
                Background = WpfBrushes.Black
            };

            var (wpfWidth, _) = _projectionManager.GetProjectionScreenSize();
            double fontScale = physicalWidth / wpfWidth;
            AddSplitRegionTextElement(canvas, new Rect(0, 0, physicalWidth, physicalHeight), current.Editor, current.Text, fontScale);
            AddSongNameWatermarkToProjection(canvas, physicalWidth, physicalHeight, fontScale);

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
                skBitmap.Dispose();
            }
        }

        private void AddSplitRegionTextElement(Canvas canvas, Rect rect, WpfTextBox editor, string text, double fontScale)
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
                FontSize = _lyricsProjectionFontSize * fontScale,
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

        private void AddSongNameWatermarkToProjection(Canvas canvas, double width, double height, double fontScale)
        {
            string watermark = GetCurrentLyricsSongWatermarkText();
            if (string.IsNullOrWhiteSpace(watermark))
            {
                return;
            }

            double padX = Math.Max(18, 16 * fontScale);
            double padY = Math.Max(10, 8 * fontScale);
            double marginX = Math.Max(22, 18 * fontScale);
            double marginY = Math.Max(18, 14 * fontScale);
            double fontSize = Math.Max(32, 28 * fontScale);

            var border = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromArgb(130, 0, 0, 0)),
                CornerRadius = new CornerRadius(Math.Max(8, 6 * fontScale)),
                Padding = new Thickness(padX, padY, padX, padY),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = watermark,
                    FontSize = fontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(WpfColor.FromArgb(180, 255, 255, 255))
                }
            };

            border.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            var size = border.DesiredSize;
            Canvas.SetLeft(border, marginX);
            Canvas.SetTop(border, Math.Max(0, height - size.Height - marginY));
            Canvas.SetZIndex(border, 1200);
            canvas.Children.Add(border);
        }

        private IEnumerable<(Rect Rect, WpfTextBox Editor, string Text)> GetSplitRenderRegions(double width, double height)
        {
            double halfWidth = width / 2.0;
            double halfHeight = height / 2.0;

            switch ((ViewSplitMode)_lyricsSplitMode)
            {
                case ViewSplitMode.Horizontal:
                    yield return (new Rect(0, 0, halfWidth, height), LyricsSplitTextBox1, LyricsSplitTextBox1.Text ?? "");
                    yield return (new Rect(halfWidth, 0, width - halfWidth, height), LyricsSplitTextBox2, LyricsSplitTextBox2.Text ?? "");
                    break;
                case ViewSplitMode.Vertical:
                    yield return (new Rect(0, 0, width, halfHeight), LyricsSplitTextBox1, LyricsSplitTextBox1.Text ?? "");
                    yield return (new Rect(0, halfHeight, width, height - halfHeight), LyricsSplitTextBox2, LyricsSplitTextBox2.Text ?? "");
                    break;
                case ViewSplitMode.TripleSplit:
                    yield return (new Rect(0, 0, halfWidth, halfHeight), LyricsSplitTextBox1, LyricsSplitTextBox1.Text ?? "");
                    yield return (new Rect(0, halfHeight, halfWidth, height - halfHeight), LyricsSplitTextBox2, LyricsSplitTextBox2.Text ?? "");
                    yield return (new Rect(halfWidth, 0, width - halfWidth, height), LyricsSplitTextBox3, LyricsSplitTextBox3.Text ?? "");
                    break;
                case ViewSplitMode.Quad:
                    yield return (new Rect(0, 0, halfWidth, halfHeight), LyricsSplitTextBox1, LyricsSplitTextBox1.Text ?? "");
                    yield return (new Rect(halfWidth, 0, width - halfWidth, halfHeight), LyricsSplitTextBox2, LyricsSplitTextBox2.Text ?? "");
                    yield return (new Rect(0, halfHeight, halfWidth, height - halfHeight), LyricsSplitTextBox3, LyricsSplitTextBox3.Text ?? "");
                    yield return (new Rect(halfWidth, halfHeight, width - halfWidth, height - halfHeight), LyricsSplitTextBox4, LyricsSplitTextBox4.Text ?? "");
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
