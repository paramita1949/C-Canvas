using System.Collections.Generic;

namespace ImageColorChanger.Services.TextEditor.Models
{
    /// <summary>
    /// v2 富文本文档模型：显式段落与 run 边界。
    /// </summary>
    public sealed class RichTextDocumentV2
    {
        public const string CurrentFormatVersion = "v2";

        public string FormatVersion { get; set; } = CurrentFormatVersion;

        public List<RichTextParagraphV2> Paragraphs { get; set; } = new List<RichTextParagraphV2>();
    }

    public sealed class RichTextParagraphV2
    {
        public int ParagraphIndex { get; set; }

        public List<RichTextRunV2> Runs { get; set; } = new List<RichTextRunV2>();
    }

    public sealed class RichTextRunV2
    {
        public int RunIndex { get; set; }

        public string Text { get; set; } = string.Empty;
    }
}
