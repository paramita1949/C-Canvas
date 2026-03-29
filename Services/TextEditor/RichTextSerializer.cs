using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor.Models;

namespace ImageColorChanger.Services.TextEditor
{
    public sealed class RichTextSerializer : IRichTextSerializer
    {
        public RichTextDocumentV2 BuildDocument(string content, IReadOnlyList<RichTextSpan> spans)
        {
            var upgraded = UpgradeToV2(content, spans, spans?.FirstOrDefault()?.TextElementId ?? 0);

            var document = new RichTextDocumentV2();
            if (upgraded.Count == 0)
            {
                return document;
            }

            foreach (var paragraph in upgraded
                         .GroupBy(s => s.ParagraphIndex ?? 0)
                         .OrderBy(g => g.Key))
            {
                var p = new RichTextParagraphV2 { ParagraphIndex = paragraph.Key };
                foreach (var run in paragraph.OrderBy(s => s.RunIndex ?? s.SpanOrder))
                {
                    p.Runs.Add(new RichTextRunV2
                    {
                        RunIndex = run.RunIndex ?? run.SpanOrder,
                        Text = run.Text ?? string.Empty
                    });
                }

                document.Paragraphs.Add(p);
            }

            return document;
        }

        public IReadOnlyList<RichTextSpan> UpgradeToV2(string content, IReadOnlyList<RichTextSpan> spans, int textElementId)
        {
            if (spans == null || spans.Count == 0)
            {
                return Array.Empty<RichTextSpan>();
            }

            var ordered = spans
                .Where(s => s != null)
                .OrderBy(s => s.SpanOrder)
                .ToList();

            if (ordered.Count == 0)
            {
                return Array.Empty<RichTextSpan>();
            }

            bool isAlreadyV2 = ordered.All(s =>
                string.Equals(s.FormatVersion, RichTextDocumentV2.CurrentFormatVersion, StringComparison.OrdinalIgnoreCase) &&
                s.ParagraphIndex.HasValue &&
                s.RunIndex.HasValue);

            if (isAlreadyV2)
            {
                return ordered
                    .Select((s, index) => CloneWithV2Metadata(s, textElementId, s.Text, index, s.ParagraphIndex, s.RunIndex))
                    .ToList();
            }

            if (TryUpgradeByContent(content, ordered, textElementId, out var upgraded))
            {
                return upgraded;
            }

#if DEBUG
            // string safeContent = (content ?? string.Empty).Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\r");
            // if (safeContent.Length > 120)
            // {
            //     safeContent = safeContent.Substring(0, 120) + "...";
            // }
            // string joined = string.Concat(ordered.Select(s => s.Text ?? string.Empty));
            // int contentNoBreakLen = (content ?? string.Empty).Replace("\r\n", "").Replace("\n", "").Replace("\r", "").Length;
            // Debug.WriteLine(
            //     $"[换行诊断][RichTextSerializer][FallbackV1ToV2] textElementId={textElementId}, " +
            //     $"contentLen={(content ?? string.Empty).Length}, contentNoBreakLen={contentNoBreakLen}, " +
            //     $"joinedLen={joined.Length}, spanCount={ordered.Count}, contentPreview='{safeContent}'");
#endif

            Debug.WriteLine($"[RichTextSerializer] v1 -> v2 升级使用保守回退: textElementId={textElementId}");
            return ordered
                .Select((s, index) => CloneWithV2Metadata(s, textElementId, s.Text, index, 0, index))
                .ToList();
        }

        private static bool TryUpgradeByContent(string content, List<RichTextSpan> orderedSpans, int textElementId, out List<RichTextSpan> upgraded)
        {
            upgraded = new List<RichTextSpan>();
            content ??= string.Empty;

            string contentWithoutLineBreaks = content.Replace("\r\n", "").Replace("\n", "").Replace("\r", "");
            string spansJoined = string.Concat(orderedSpans.Select(s => s.Text ?? string.Empty));
            if (!string.Equals(contentWithoutLineBreaks, spansJoined, StringComparison.Ordinal))
            {
#if DEBUG
                // string contentPreview = content.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\r");
                // if (contentPreview.Length > 120)
                // {
                //     contentPreview = contentPreview.Substring(0, 120) + "...";
                // }
                // Debug.WriteLine(
                //     $"[换行诊断][RichTextSerializer][TryUpgradeByContentMismatch] textElementId={textElementId}, " +
                //     $"contentNoBreakLen={contentWithoutLineBreaks.Length}, spansJoinedLen={spansJoined.Length}, " +
                //     $"spanCount={orderedSpans.Count}, contentPreview='{contentPreview}'");
#endif
                return false;
            }

            var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            int spanIndex = 0;
            int offsetInSpan = 0;
            int order = 0;
            bool hasAnyTextContent = false;

            for (int paragraphIndex = 0; paragraphIndex < lines.Length; paragraphIndex++)
            {
                string line = lines[paragraphIndex] ?? string.Empty;
                int lineRemaining = line.Length;
                int runIndex = 0;

                while (lineRemaining > 0 && spanIndex < orderedSpans.Count)
                {
                    var source = orderedSpans[spanIndex];
                    string sourceText = source.Text ?? string.Empty;

                    if (sourceText.Length == 0 || offsetInSpan >= sourceText.Length)
                    {
                        spanIndex++;
                        offsetInSpan = 0;
                        continue;
                    }

                    int takeLength = Math.Min(lineRemaining, sourceText.Length - offsetInSpan);
                    string chunk = sourceText.Substring(offsetInSpan, takeLength);
                    upgraded.Add(CloneWithV2Metadata(source, textElementId, chunk, order++, paragraphIndex, runIndex++));

                    if (!string.IsNullOrEmpty(chunk))
                    {
                        hasAnyTextContent = true;
                    }

                    lineRemaining -= takeLength;
                    offsetInSpan += takeLength;
                    if (offsetInSpan >= sourceText.Length)
                    {
                        spanIndex++;
                        offsetInSpan = 0;
                    }
                }

                if (line.Length == 0)
                {
                    upgraded.Add(CreateEmptyParagraphSpan(textElementId, order++, paragraphIndex));
                }
            }

            if (spanIndex < orderedSpans.Count)
            {
                for (int i = spanIndex; i < orderedSpans.Count; i++)
                {
                    if (!string.IsNullOrEmpty(orderedSpans[i].Text))
                    {
                        return false;
                    }
                }
            }

            if (!hasAnyTextContent)
            {
                upgraded.Clear();
            }

            return true;
        }

        private static RichTextSpan CreateEmptyParagraphSpan(int textElementId, int spanOrder, int paragraphIndex)
        {
            return new RichTextSpan
            {
                TextElementId = textElementId,
                SpanOrder = spanOrder,
                Text = string.Empty,
                ParagraphIndex = paragraphIndex,
                RunIndex = 0,
                FormatVersion = RichTextDocumentV2.CurrentFormatVersion
            };
        }

        private static RichTextSpan CloneWithV2Metadata(
            RichTextSpan source,
            int textElementId,
            string text,
            int spanOrder,
            int? paragraphIndex,
            int? runIndex)
        {
            return new RichTextSpan
            {
                TextElementId = textElementId > 0 ? textElementId : source.TextElementId,
                SpanOrder = spanOrder,
                Text = text ?? string.Empty,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                FontColor = source.FontColor,
                IsBold = source.IsBold,
                IsItalic = source.IsItalic,
                IsUnderline = source.IsUnderline,
                BorderColor = source.BorderColor,
                BorderWidth = source.BorderWidth,
                BorderRadius = source.BorderRadius,
                BorderOpacity = source.BorderOpacity,
                BackgroundColor = source.BackgroundColor,
                BackgroundRadius = source.BackgroundRadius,
                BackgroundOpacity = source.BackgroundOpacity,
                ShadowColor = source.ShadowColor,
                ShadowOffsetX = source.ShadowOffsetX,
                ShadowOffsetY = source.ShadowOffsetY,
                ShadowBlur = source.ShadowBlur,
                ShadowOpacity = source.ShadowOpacity,
                ParagraphIndex = paragraphIndex,
                RunIndex = runIndex,
                FormatVersion = RichTextDocumentV2.CurrentFormatVersion
            };
        }
    }
}
