using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public sealed class TextEditorThumbnailService : ITextEditorThumbnailService
    {
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
                        width = 1600;
                    }
                    if (height <= 0)
                    {
                        height = 900;
                    }

                    var renderBitmap = new RenderTargetBitmap(
                        width,
                        height,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    renderBitmap.Render(canvasContainer);
                    return new TransformedBitmap(renderBitmap, new ScaleTransform(0.1, 0.1));
                });
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
