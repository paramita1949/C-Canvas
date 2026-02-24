using System.Windows;

namespace ImageColorChanger.Services.TextEditor.Models
{
    /// <summary>
    /// 文本框内部布局配置（不可变）。
    /// </summary>
    public sealed class TextLayoutProfile
    {
        public static TextLayoutProfile Default { get; } = new TextLayoutProfile(
            richTextBoxPadding: new Thickness(10, 10, 10, 10),
            documentPagePadding: new Thickness(10, 15, 10, 15),
            paragraphMargin: new Thickness(0),
            defaultLineSpacing: 1.2);

        public TextLayoutProfile(
            Thickness richTextBoxPadding,
            Thickness documentPagePadding,
            Thickness paragraphMargin,
            double defaultLineSpacing)
        {
            RichTextBoxPadding = richTextBoxPadding;
            DocumentPagePadding = documentPagePadding;
            ParagraphMargin = paragraphMargin;
            DefaultLineSpacing = defaultLineSpacing;
        }

        public Thickness RichTextBoxPadding { get; }

        public Thickness DocumentPagePadding { get; }

        public Thickness ParagraphMargin { get; }

        public double DefaultLineSpacing { get; }
    }
}
