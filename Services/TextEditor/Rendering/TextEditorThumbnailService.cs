using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public sealed class TextEditorThumbnailService : ITextEditorThumbnailService
    {
        private const int FallbackCanvasWidth = 1600;
        private const int FallbackCanvasHeight = 900;
        private const double DefaultScaleRatio = 0.1;
        private const int MinThumbnailWidth = 320;
        private const int MinThumbnailHeight = 180;

        private readonly ITextEditorRenderSafetyService _renderSafetyService;

        public TextEditorThumbnailService()
            : this(new TextEditorRenderSafetyService())
        {
        }

        public TextEditorThumbnailService(ITextEditorRenderSafetyService renderSafetyService)
        {
            _renderSafetyService = renderSafetyService ?? throw new ArgumentNullException(nameof(renderSafetyService));
        }

        public BitmapSource GenerateThumbnail(Grid canvasContainer, IReadOnlyCollection<DraggableTextBox> textBoxes)
        {
            if (canvasContainer == null)
            {
                return null;
            }

            return _renderSafetyService.Execute(textBoxes, () =>
                {
                    canvasContainer.UpdateLayout();

                    int width = (int)canvasContainer.ActualWidth;
                    int height = (int)canvasContainer.ActualHeight;
                    if (width <= 0)
                    {
                        width = FallbackCanvasWidth;
                    }
                    if (height <= 0)
                    {
                        height = FallbackCanvasHeight;
                    }

                    var renderBitmap = new RenderTargetBitmap(
                        width,
                        height,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    renderBitmap.Render(canvasContainer);

                    var (targetWidth, targetHeight) = CalculateThumbnailSize(width, height);
                    if (targetWidth == width && targetHeight == height)
                    {
                        renderBitmap.Freeze();
                        return renderBitmap;
                    }

                    var scaledVisual = new DrawingVisual();
                    RenderOptions.SetBitmapScalingMode(scaledVisual, BitmapScalingMode.HighQuality);

                    using (var context = scaledVisual.RenderOpen())
                    {
                        context.DrawImage(renderBitmap, new Rect(0, 0, targetWidth, targetHeight));
                    }

                    var thumbnailBitmap = new RenderTargetBitmap(
                        targetWidth,
                        targetHeight,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    thumbnailBitmap.Render(scaledVisual);
                    thumbnailBitmap.Freeze();

                    return thumbnailBitmap;
                });
        }

        public static (int Width, int Height) CalculateThumbnailSize(int sourceWidth, int sourceHeight)
        {
            if (sourceWidth <= 0)
            {
                sourceWidth = FallbackCanvasWidth;
            }

            if (sourceHeight <= 0)
            {
                sourceHeight = FallbackCanvasHeight;
            }

            double scaledWidth = sourceWidth * DefaultScaleRatio;
            double scaledHeight = sourceHeight * DefaultScaleRatio;

            int targetWidth = (int)Math.Round(Math.Max(scaledWidth, MinThumbnailWidth));
            int targetHeight = (int)Math.Round(Math.Max(scaledHeight, MinThumbnailHeight));

            targetWidth = Math.Min(targetWidth, sourceWidth);
            targetHeight = Math.Min(targetHeight, sourceHeight);

            return (targetWidth, targetHeight);
        }

        public string SaveSlideThumbnail(int slideId, Grid canvasContainer, IReadOnlyCollection<DraggableTextBox> textBoxes, string thumbnailDirectory)
        {
            if (slideId <= 0 || string.IsNullOrWhiteSpace(thumbnailDirectory))
            {
                return null;
            }

            var thumbnail = GenerateThumbnail(canvasContainer, textBoxes);
            if (thumbnail == null)
            {
                return null;
            }

            if (!Directory.Exists(thumbnailDirectory))
            {
                Directory.CreateDirectory(thumbnailDirectory);
            }

            string thumbnailPath = Path.Combine(thumbnailDirectory, $"slide_{slideId}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));

            using (var fileStream = new FileStream(thumbnailPath, FileMode.Create))
            {
                encoder.Save(fileStream);
            }

            return thumbnailPath;
        }
    }
}
