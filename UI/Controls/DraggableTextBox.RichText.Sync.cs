using System;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Core;
using ImageColorChanger.Utils;
using WpfBorder = System.Windows.Controls.Border;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfGrid = System.Windows.Controls.Grid;
using WpfImage = System.Windows.Controls.Image;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfThumb = System.Windows.Controls.Primitives.Thumb;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKey = System.Windows.Input.Key;
using WpfMouseButton = System.Windows.Input.MouseButton;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfRect = System.Windows.Rect;
using ImageColorChanger.UI.Controls.Common;

namespace ImageColorChanger.UI.Controls
{
    public partial class DraggableTextBox
    {
        #region RichText 同步

        private void RenderRichTextSpansV2(IReadOnlyList<Database.Models.RichTextSpan> spans)
        {
            var grouped = spans
                .GroupBy(s => s.ParagraphIndex ?? 0)
                .OrderBy(g => g.Key)
                .ToList();

            if (grouped.Count == 0)
            {
                _richTextBox.Document.Blocks.Add(new System.Windows.Documents.Paragraph());
                return;
            }

            int expectedParagraphIndex = 0;
            foreach (var paragraphGroup in grouped)
            {
                while (expectedParagraphIndex < paragraphGroup.Key)
                {
                    _richTextBox.Document.Blocks.Add(new System.Windows.Documents.Paragraph());
                    expectedParagraphIndex++;
                }

                var paragraph = new System.Windows.Documents.Paragraph();

                foreach (var span in paragraphGroup.OrderBy(s => s.RunIndex ?? s.SpanOrder))
                {
                    var run = new System.Windows.Documents.Run(span.Text ?? string.Empty);
                    ApplySpanStyleToRun(run, span);
                    paragraph.Inlines.Add(run);
                }

                _richTextBox.Document.Blocks.Add(paragraph);
                expectedParagraphIndex = paragraphGroup.Key + 1;
            }
        }

        public void SyncTextFromRichTextBox()

        {

            if (_richTextBox == null || _richTextBox.Document == null)

                return;



            try

            {

                //  遍历所有段落，保留段落之间的换行符

                // 使用 textRange.Text 会丢失换行符，导致文本顺序错乱

                var contentBuilder = new System.Text.StringBuilder();

                bool isFirstBlock = true;

                

                foreach (var block in _richTextBox.Document.Blocks)

                {

                    if (block is System.Windows.Documents.Paragraph paragraph)

                    {

                        // 获取段落内的文本（不包括段落结束符）

                        var paragraphRange = new System.Windows.Documents.TextRange(

                            paragraph.ContentStart,

                            paragraph.ContentEnd);

                        string paragraphText = paragraphRange.Text;

                        

                        // 移除末尾的换行符（段落结束符，WPF会自动添加）

                        // paragraphRange.Text 通常以 \r\n 结尾（段落分隔符）

                        if (paragraphText.EndsWith("\r\n"))

                            paragraphText = paragraphText.Substring(0, paragraphText.Length - 2);

                        else if (paragraphText.EndsWith("\n") || paragraphText.EndsWith("\r"))

                            paragraphText = paragraphText.Substring(0, paragraphText.Length - 1);

                        

                        // 在段落之间添加换行符（第一个段落前不加）

                        if (!isFirstBlock)

                        {

                            contentBuilder.Append("\r\n");

                        }

                        

                        contentBuilder.Append(paragraphText);

                        isFirstBlock = false;

                    }

                    else if (block is System.Windows.Documents.Section section)

                    {

                        // 处理 Section 块

                        if (!isFirstBlock)

                        {

                            contentBuilder.Append("\r\n");

                        }

                        

                        var sectionRange = new System.Windows.Documents.TextRange(

                            section.ContentStart,

                            section.ContentEnd);

                        string sectionText = sectionRange.Text;

                        

                        // 移除末尾的换行符

                        if (sectionText.EndsWith("\r\n"))

                            sectionText = sectionText.Substring(0, sectionText.Length - 2);

                        else if (sectionText.EndsWith("\n") || sectionText.EndsWith("\r"))

                            sectionText = sectionText.Substring(0, sectionText.Length - 1);

                        

                        contentBuilder.Append(sectionText);

                        isFirstBlock = false;

                    }

                }

                

                Data.Content = contentBuilder.ToString();

                

#if DEBUG

                // 调试信息：显示同步后的文本内容（仅前100个字符）

                //string preview = Data.Content.Length > 100 ? Data.Content.Substring(0, 100) + "..." : Data.Content;

                //System.Diagnostics.Debug.WriteLine($" [SyncTextFromRichTextBox] 同步完成，段落数={_richTextBox.Document.Blocks.Count}, 文本长度={Data.Content.Length}, 预览={preview.Replace("\r\n", "\\n")}");

#endif

            }

            catch

            {

#if DEBUG

                //System.Diagnostics.Debug.WriteLine($" [SyncTextFromRichTextBox] 失败");

#endif

            }

        }

        private void SyncTextToRichTextBox()

        {

            if (_richTextBox == null)

                return;



            try

            {

                // 设置同步标志，防止 TextChanged 事件循环

                _isSyncing = true;



                _richTextBox.Document.Blocks.Clear();



                // 如果有 RichTextSpans，渲染富文本片段

                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)

                {

#if DEBUG

                    //System.Diagnostics.Debug.WriteLine($"[加载RichTextSpans] 文本框 ID={Data.Id} 开始加载 {Data.RichTextSpans.Count} 个片段");

#endif

                    //  关键修复：根据 Data.Content 中的换行符来分割段落

                    // 这样可以保留段落结构，即使 RichTextSpans 中没有段落分隔信息

                    string content = Data.Content ?? "";

                    string[] contentLines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

                    

                    // 按 SpanOrder 排序后渲染

                    var sortedSpans = Data.RichTextSpans.OrderBy(s => s.SpanOrder).ToList();

                    bool hasV2Spans = sortedSpans.Any(s =>
                        string.Equals(
                            s.FormatVersion,
                            Services.TextEditor.Models.RichTextDocumentV2.CurrentFormatVersion,
                            StringComparison.OrdinalIgnoreCase) &&
                        s.ParagraphIndex.HasValue);
                    int distinctParagraphCount = sortedSpans
                        .Select(s => s.ParagraphIndex ?? 0)
                        .Distinct()
                        .Count();
                    bool contentLooksMultiline = contentLines.Length > 1;
                    bool paragraphMetadataCollapsed = contentLooksMultiline && distinctParagraphCount <= 1;

#if DEBUG
                    // System.Diagnostics.Debug.WriteLine(
                    //     $"[换行诊断][SyncTextToRichTextBox][LoadMeta] textElementId={Data?.Id}, " +
                    //     $"contentLines={contentLines.Length}, spanCount={sortedSpans.Count}, " +
                    //     $"hasV2Spans={hasV2Spans}, distinctParagraphCount={distinctParagraphCount}, " +
                    //     $"paragraphMetadataCollapsed={paragraphMetadataCollapsed}");
#endif

                    if (hasV2Spans && !paragraphMetadataCollapsed)
                    {
                        RenderRichTextSpansV2(sortedSpans);
                    }
                    else
                    {
                        // 将所有 RichTextSpans 的文本按顺序拼接（去掉换行符）

                    string allSpansText = string.Join("", sortedSpans.Select(s => s.Text ?? ""));

                    string contentWithoutLineBreaks = content.Replace("\r\n", "").Replace("\n", "").Replace("\r", "");

                    

                    // 如果 RichTextSpans 的文本总和与 Data.Content（去掉换行符）一致，按段落长度分割

                    if (allSpansText == contentWithoutLineBreaks)

                    {

                        int spanIndex = 0;

                        int spanTextPosition = 0;

                        

                        foreach (string line in contentLines)

                        {

                            var paragraph = new System.Windows.Documents.Paragraph();

                            

                            int lineLength = line.Length;

                            int lineSpanPosition = 0;

                            

                            // 将这一行的文本与 RichTextSpans 匹配

                            while (lineSpanPosition < lineLength && spanIndex < sortedSpans.Count)

                            {

                                var span = sortedSpans[spanIndex];

                                string spanText = span.Text ?? "";

                                int spanLength = spanText.Length;

                                

                                // 计算这个 span 在当前行中的位置

                                int remainingInLine = lineLength - lineSpanPosition;

                                

                                if (spanLength <= remainingInLine)

                                {

                                    // 整个 span 都属于当前行

                                    var run = new System.Windows.Documents.Run(spanText);

                                    ApplySpanStyleToRun(run, span);

                                    paragraph.Inlines.Add(run);

                                    lineSpanPosition += spanLength;

                                    spanTextPosition += spanLength;

                                    spanIndex++;

                                }

                                else

                                {

                                    // span 跨越了行边界，只取当前行的部分

                                    string linePart = spanText.Substring(0, remainingInLine);

                                    var run = new System.Windows.Documents.Run(linePart);

                                    ApplySpanStyleToRun(run, span);

                                    paragraph.Inlines.Add(run);

                                    lineSpanPosition = lineLength;

                                    // 更新 span 的剩余部分（修改列表中的元素）

                                    var remainingSpan = new Database.Models.RichTextSpan

                                    {

                                        TextElementId = span.TextElementId,

                                        SpanOrder = span.SpanOrder,

                                        Text = spanText.Substring(remainingInLine),

                                        FontFamily = span.FontFamily,

                                        FontSize = span.FontSize,

                                        FontColor = span.FontColor,

                                        BackgroundColor = span.BackgroundColor,

                                        IsBold = span.IsBold,

                                        IsItalic = span.IsItalic,

                                        IsUnderline = span.IsUnderline,

                                        ParagraphIndex = span.ParagraphIndex,

                                        RunIndex = span.RunIndex,

                                        FormatVersion = span.FormatVersion

                                    };

                                    sortedSpans[spanIndex] = remainingSpan;

                                    spanTextPosition += remainingInLine;

                                    break; // 当前行已满，继续下一行

                                }

                            }

                            

                            _richTextBox.Document.Blocks.Add(paragraph);

                        }

                    }

                    else

                    {
#if DEBUG
                        // int contentLineCount = contentLines.Length;
                        // int spanCount = sortedSpans.Count;
                        // int nonEmptySpanCount = sortedSpans.Count(s => !string.IsNullOrEmpty(s.Text));
                        // int paragraphSpanCount = sortedSpans.Count(s => s.ParagraphIndex.HasValue);
                        // string contentPreview = (content ?? string.Empty).Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\r");
                        // if (contentPreview.Length > 120)
                        // {
                        //     contentPreview = contentPreview.Substring(0, 120) + "...";
                        // }
                        // System.Diagnostics.Debug.WriteLine(
                        //     $"[换行诊断][SyncTextToRichTextBox][Mismatch] textElementId={Data?.Id}, " +
                        //     $"contentLen={(content ?? string.Empty).Length}, contentLines={contentLineCount}, " +
                        //     $"spanCount={spanCount}, nonEmptySpanCount={nonEmptySpanCount}, paragraphSpanCount={paragraphSpanCount}, " +
                        //     $"spansJoinedLen={allSpansText.Length}, contentNoBreakLen={contentWithoutLineBreaks.Length}, " +
                        //     $"contentPreview='{contentPreview}'");
#endif

                        // 如果 spans 文本与 Content（去换行）不一致，优先保留 Content 的段落结构，
                        // 避免“重进后所有换行被合并成单段落”。
                        int spanIndex = 0;
                        int offsetInSpan = 0;

                        foreach (string line in contentLines)
                        {
                            var paragraph = new System.Windows.Documents.Paragraph();
                            int lineOffset = 0;

                            while (lineOffset < line.Length)
                            {
                                if (spanIndex >= sortedSpans.Count)
                                {
                                    paragraph.Inlines.Add(new System.Windows.Documents.Run(line.Substring(lineOffset)));
                                    lineOffset = line.Length;
                                    break;
                                }

                                var span = sortedSpans[spanIndex];
                                string spanText = span.Text ?? string.Empty;

                                if (spanText.Length == 0 || offsetInSpan >= spanText.Length)
                                {
                                    spanIndex++;
                                    offsetInSpan = 0;
                                    continue;
                                }

                                int takeLength = Math.Min(line.Length - lineOffset, spanText.Length - offsetInSpan);
                                if (takeLength <= 0)
                                {
                                    break;
                                }

                                string chunk = line.Substring(lineOffset, takeLength);
                                var run = new System.Windows.Documents.Run(chunk);
                                ApplySpanStyleToRun(run, span);
                                paragraph.Inlines.Add(run);

                                lineOffset += takeLength;
                                offsetInSpan += takeLength;

                                if (offsetInSpan >= spanText.Length)
                                {
                                    spanIndex++;
                                    offsetInSpan = 0;
                                }
                            }

                            if (line.Length == 0)
                            {
                                paragraph.Inlines.Add(new System.Windows.Documents.Run(string.Empty));
                            }

                            _richTextBox.Document.Blocks.Add(paragraph);
                        }

                    }

#if DEBUG
                    // System.Diagnostics.Debug.WriteLine(
                    //     $"[换行诊断][SyncTextToRichTextBox][RenderDone] textElementId={Data?.Id}, blocks={_richTextBox.Document.Blocks.Count}");
#endif

                    }

                    

#if DEBUG

                    //System.Diagnostics.Debug.WriteLine($" [加载RichTextSpans] 加载完成，段落数={_richTextBox.Document.Blocks.Count}");

#endif

                }

                else

                {

                    //  普通文本：按换行符分割为多个段落，保留文本顺序

                    string content = Data.Content ?? "";



                    // 按换行符分割文本（支持 \r\n、\n、\r）

                    string[] lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

                    

                    foreach (string line in lines)

                    {

                        var paragraph = new System.Windows.Documents.Paragraph();

                        var run = new System.Windows.Documents.Run(line);



                        // 应用全局样式到 Run

                        if (Data.IsBold == 1)

                            run.FontWeight = System.Windows.FontWeights.Bold;

                        if (Data.IsItalic == 1)

                            run.FontStyle = System.Windows.FontStyles.Italic;

                        if (Data.IsUnderline == 1)

                            run.TextDecorations = System.Windows.TextDecorations.Underline;



                        // 应用颜色

                        if (!string.IsNullOrEmpty(Data.FontColor) &&
                            SharedColorModule.TryCreateBrush(Data.FontColor, out var fontBrush))

                        {

                            run.Foreground = fontBrush;

                        }



                        paragraph.Inlines.Add(run);

                        _richTextBox.Document.Blocks.Add(paragraph);

                    }



                    // 如果内容为空，至少创建一个空段落

                    if (lines.Length == 0)

                    {

                        var paragraph = new System.Windows.Documents.Paragraph();

                        _richTextBox.Document.Blocks.Add(paragraph);

                    }

                }



                // 应用样式（包括 RichTextSpans）

                ApplyStylesToRichTextBox();

            }

            catch (Exception)

            {

#if DEBUG

                System.Diagnostics.Debug.WriteLine($" [SyncTextToRichTextBox] 失败");

#endif

            }

            finally

            {

                // 清除同步标志

                _isSyncing = false;

            }

        }

        #endregion
    }
}


