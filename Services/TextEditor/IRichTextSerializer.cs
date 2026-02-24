using System.Collections.Generic;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor.Models;

namespace ImageColorChanger.Services.TextEditor
{
    public interface IRichTextSerializer
    {
        RichTextDocumentV2 BuildDocument(string content, IReadOnlyList<RichTextSpan> spans);

        IReadOnlyList<RichTextSpan> UpgradeToV2(string content, IReadOnlyList<RichTextSpan> spans, int textElementId);
    }
}
