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
    }
}
