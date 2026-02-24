using System.Collections.Generic;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Services.TextEditor.Models
{
    /// <summary>
    /// 文本框保存快照（无 UI 副作用采样结果）。
    /// </summary>
    public sealed class TextBoxSnapshot
    {
        public TextBoxSnapshot(
            TextElement element,
            string content,
            IReadOnlyList<RichTextSpan> richTextSpans,
            TextLayoutProfile layoutProfile,
            bool wasInEditMode)
        {
            Element = element;
            Content = content ?? string.Empty;
            RichTextSpans = richTextSpans ?? new List<RichTextSpan>();
            LayoutProfile = layoutProfile ?? TextLayoutProfile.Default;
            WasInEditMode = wasInEditMode;
        }

        public TextElement Element { get; }

        public int TextElementId => Element?.Id ?? 0;

        public string Content { get; }

        public IReadOnlyList<RichTextSpan> RichTextSpans { get; }

        public TextLayoutProfile LayoutProfile { get; }

        public bool WasInEditMode { get; }
    }
}
