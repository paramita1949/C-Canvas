using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor.Models;

namespace ImageColorChanger.Services.TextEditor
{
    public interface ITextLayoutService
    {
        void ApplyLayout(System.Windows.Controls.RichTextBox richTextBox, TextLayoutProfile profile, TextElement textElement);
    }
}
