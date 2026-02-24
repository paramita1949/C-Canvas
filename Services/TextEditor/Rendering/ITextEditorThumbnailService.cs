using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public interface ITextEditorThumbnailService
    {
        BitmapSource GenerateThumbnail(Grid canvasContainer, IReadOnlyCollection<DraggableTextBox> textBoxes);

        string SaveSlideThumbnail(int slideId, Grid canvasContainer, IReadOnlyCollection<DraggableTextBox> textBoxes, string thumbnailDirectory);
    }
}
