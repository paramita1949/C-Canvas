using System.Collections.Generic;
using ImageColorChanger.Services.TextEditor.Rendering;
using ImageColorChanger.UI.Controls;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Rendering
{
    public sealed class TextEditorThumbnailServiceTests
    {
        [Fact]
        public void GenerateThumbnail_ReturnsNull_WhenCanvasIsNull()
        {
            var service = new TextEditorThumbnailService();
            var result = service.GenerateThumbnail(null, new List<DraggableTextBox>());
            Assert.Null(result);
        }

        [Fact]
        public void SaveSlideThumbnail_ReturnsNull_WhenInputIsInvalid()
        {
            var service = new TextEditorThumbnailService();
            var result = service.SaveSlideThumbnail(
                slideId: 0,
                canvasContainer: null,
                textBoxes: new List<DraggableTextBox>(),
                thumbnailDirectory: "");
            Assert.Null(result);
        }

        [Fact]
        public void CalculateThumbnailSize_UsesMinimumDisplayResolution_ForLargeCanvas()
        {
            var (width, height) = TextEditorThumbnailService.CalculateThumbnailSize(1600, 900);

            Assert.Equal(320, width);
            Assert.Equal(180, height);
        }

        [Fact]
        public void CalculateThumbnailSize_DoesNotUpscale_WhenCanvasAlreadySmall()
        {
            var (width, height) = TextEditorThumbnailService.CalculateThumbnailSize(220, 120);

            Assert.Equal(220, width);
            Assert.Equal(120, height);
        }
    }
}
