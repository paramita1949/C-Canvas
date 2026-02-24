using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor.Models;

namespace ImageColorChanger.Services.TextEditor
{
    public sealed class TextLayoutService : ITextLayoutService
    {
        public void ApplyLayout(System.Windows.Controls.RichTextBox richTextBox, TextLayoutProfile profile, TextElement textElement)
        {
            if (richTextBox == null)
            {
                return;
            }

            profile ??= TextLayoutProfile.Default;

            richTextBox.Padding = profile.RichTextBoxPadding;

            if (richTextBox.Document == null)
            {
                return;
            }

            richTextBox.Document.PagePadding = profile.DocumentPagePadding;

            double defaultLineSpacing = profile.DefaultLineSpacing > 0 ? profile.DefaultLineSpacing : 1.2;
            double lineSpacing = textElement != null && textElement.LineSpacing > 0
                ? textElement.LineSpacing
                : defaultLineSpacing;
            double fontSize = textElement != null && textElement.FontSize > 0
                ? textElement.FontSize
                : 24;
            double lineHeight = fontSize * lineSpacing;

            foreach (var block in richTextBox.Document.Blocks)
            {
                if (block is System.Windows.Documents.Paragraph paragraph)
                {
                    paragraph.Margin = profile.ParagraphMargin;
                    paragraph.LineHeight = lineHeight;
                    paragraph.LineStackingStrategy = System.Windows.LineStackingStrategy.BlockLineHeight;
                }
            }
        }
    }
}
