using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor.Models;
using System;
using System.Diagnostics;
using System.Linq;

namespace ImageColorChanger.Services.TextEditor
{
    public sealed class TextLayoutService : ITextLayoutService
    {
        private const bool EnableNoticeVerticalAlignDebugTrace = false;

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
            foreach (var block in richTextBox.Document.Blocks)
            {
                if (block is System.Windows.Documents.Paragraph paragraph)
                {
                    paragraph.Margin = profile.ParagraphMargin;

                    // 对富文本使用段落内实际最大字号计算行高，避免加载/改色后出现“文字漂移错觉”。
                    double paragraphFontSize = fontSize;
                    if (paragraph.Inlines != null && paragraph.Inlines.Count > 0)
                    {
                        var runMax = paragraph.Inlines
                            .OfType<System.Windows.Documents.Run>()
                            .Select(r => r.FontSize > 0 ? r.FontSize : fontSize)
                            .DefaultIfEmpty(fontSize)
                            .Max();
                        paragraphFontSize = runMax > 0 ? runMax : fontSize;
                    }

                    double lineHeight = paragraphFontSize * lineSpacing;
                    paragraph.LineHeight = lineHeight;
                    paragraph.LineStackingStrategy = System.Windows.LineStackingStrategy.BlockLineHeight;
                }
            }

            ApplyVerticalTextAlignment(richTextBox, profile, textElement);
        }

        private static bool ShouldTraceNoticeVerticalAlign(TextElement textElement)
        {
#if !DEBUG
            _ = textElement;
            return false;
#else
            if (!EnableNoticeVerticalAlignDebugTrace || textElement == null)
            {
                return false;
            }

            return string.Equals(textElement.ComponentType, "Notice", StringComparison.OrdinalIgnoreCase);
#endif
        }

        private static void TraceNoticeVerticalAlign(
            string stage,
            System.Windows.Controls.RichTextBox richTextBox,
            TextLayoutProfile profile,
            TextElement textElement,
            string vAlign,
            double contentHeight,
            double availableHeight,
            double extra,
            double topPad,
            double bottomPad)
        {
#if !DEBUG
            _ = stage;
            _ = richTextBox;
            _ = profile;
            _ = textElement;
            _ = vAlign;
            _ = contentHeight;
            _ = availableHeight;
            _ = extra;
            _ = topPad;
            _ = bottomPad;
            return;
#else
            if (!ShouldTraceNoticeVerticalAlign(textElement) || richTextBox?.Document == null || profile == null)
            {
                return;
            }

            var rtbPadding = richTextBox.Padding;
            var docPadding = richTextBox.Document.PagePadding;
            Debug.WriteLine(
                $"[NoticeVAlignDebug] {stage} Id={textElement.Id} VAlign={vAlign} " +
                $"ElementH={textElement.Height:F1} RtbH={richTextBox.ActualHeight:F1} " +
                $"Avail={availableHeight:F1} Content={contentHeight:F1} Extra={extra:F1} " +
                $"TopPad={topPad:F1} BottomPad={bottomPad:F1} " +
                $"BasePad=({profile.DocumentPagePadding.Left:F1},{profile.DocumentPagePadding.Top:F1},{profile.DocumentPagePadding.Right:F1},{profile.DocumentPagePadding.Bottom:F1}) " +
                $"RtbPad=({rtbPadding.Left:F1},{rtbPadding.Top:F1},{rtbPadding.Right:F1},{rtbPadding.Bottom:F1}) " +
                $"DocPadNow=({docPadding.Left:F1},{docPadding.Top:F1},{docPadding.Right:F1},{docPadding.Bottom:F1})");
#endif
        }

        private static void ApplyVerticalTextAlignment(
            System.Windows.Controls.RichTextBox richTextBox,
            TextLayoutProfile profile,
            TextElement textElement)
        {
            if (richTextBox?.Document == null || profile == null)
            {
                return;
            }

            string vAlign = (textElement?.TextVerticalAlign ?? "Top").Trim();
            if (!string.Equals(vAlign, "Middle", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(vAlign, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                richTextBox.Document.PagePadding = profile.DocumentPagePadding;
                TraceNoticeVerticalAlign(
                    "ApplyVerticalAlign-TopPath",
                    richTextBox,
                    profile,
                    textElement,
                    vAlign,
                    contentHeight: 0,
                    availableHeight: 0,
                    extra: 0,
                    topPad: profile.DocumentPagePadding.Top,
                    bottomPad: profile.DocumentPagePadding.Bottom);
                return;
            }

            if (richTextBox.ActualHeight <= 0)
            {
                richTextBox.Document.PagePadding = profile.DocumentPagePadding;
                TraceNoticeVerticalAlign(
                    "ApplyVerticalAlign-NoActualHeight",
                    richTextBox,
                    profile,
                    textElement,
                    vAlign,
                    contentHeight: 0,
                    availableHeight: 0,
                    extra: 0,
                    topPad: profile.DocumentPagePadding.Top,
                    bottomPad: profile.DocumentPagePadding.Bottom);
                return;
            }

            // 先回到基础文档内边距并强制刷新布局，再测量内容高度，
            // 避免读取到“上一轮居中 padding”造成的污染高度。
            richTextBox.Document.PagePadding = profile.DocumentPagePadding;
            richTextBox.UpdateLayout();

            double contentHeight = richTextBox.ExtentHeight;
            var currentDocPadding = richTextBox.Document.PagePadding;
            if (contentHeight > 0.1 && !double.IsNaN(contentHeight) && !double.IsInfinity(contentHeight))
            {
                contentHeight = Math.Max(0, contentHeight - currentDocPadding.Top - currentDocPadding.Bottom);
            }
            if (contentHeight <= 0.1 || double.IsNaN(contentHeight) || double.IsInfinity(contentHeight))
            {
                var start = richTextBox.Document.ContentStart;
                var end = richTextBox.Document.ContentEnd;
                var startRect = start.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
                var endRect = end.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
                contentHeight = Math.Max(0, endRect.Bottom - startRect.Top);
            }

            // 空文档或字符矩形不可用时，保留基础内边距，避免跳变。
            if (contentHeight <= 0.1 || double.IsNaN(contentHeight) || double.IsInfinity(contentHeight))
            {
                richTextBox.Document.PagePadding = profile.DocumentPagePadding;
                TraceNoticeVerticalAlign(
                    "ApplyVerticalAlign-InvalidContentHeight",
                    richTextBox,
                    profile,
                    textElement,
                    vAlign,
                    contentHeight,
                    availableHeight: 0,
                    extra: 0,
                    topPad: profile.DocumentPagePadding.Top,
                    bottomPad: profile.DocumentPagePadding.Bottom);
                return;
            }

            double availableHeight = richTextBox.ActualHeight - profile.RichTextBoxPadding.Top - profile.RichTextBoxPadding.Bottom;
            double baseTop = profile.DocumentPagePadding.Top;
            double baseBottom = profile.DocumentPagePadding.Bottom;
            double extra = Math.Max(0, availableHeight - contentHeight - baseTop - baseBottom);

            double topPad = baseTop;
            double bottomPad = baseBottom;

            if (string.Equals(vAlign, "Middle", StringComparison.OrdinalIgnoreCase))
            {
                topPad = baseTop + (extra / 2.0);
                bottomPad = baseBottom + (extra / 2.0);
            }
            else if (string.Equals(vAlign, "Bottom", StringComparison.OrdinalIgnoreCase))
            {
                topPad = baseTop + extra;
                bottomPad = baseBottom;
            }

            richTextBox.Document.PagePadding = new System.Windows.Thickness(
                profile.DocumentPagePadding.Left,
                topPad,
                profile.DocumentPagePadding.Right,
                bottomPad);

            TraceNoticeVerticalAlign(
                "ApplyVerticalAlign-Computed",
                richTextBox,
                profile,
                textElement,
                vAlign,
                contentHeight,
                availableHeight,
                extra,
                topPad,
                bottomPad);
        }
    }
}
