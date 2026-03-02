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
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKey = System.Windows.Input.Key;
using WpfMouseButton = System.Windows.Input.MouseButton;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfRect = System.Windows.Rect;

namespace ImageColorChanger.UI.Controls
{
    public partial class DraggableTextBox
    {
        #region RichText 片段

        private void ApplySpanStyleToRun(System.Windows.Documents.Run run, Database.Models.RichTextSpan span)

        {

            // 应用字体

            // 修复：如果 span.FontFamily 为空，使用 Data.FontFamily 作为默认值

            string fontFamilyToApply = span.FontFamily;

            if (string.IsNullOrEmpty(fontFamilyToApply))

            {

                fontFamilyToApply = Data.FontFamily;

            }

            

            if (!string.IsNullOrEmpty(fontFamilyToApply))

            {

                var fontFamily = FontService.Instance.GetFontFamilyByFamily(fontFamilyToApply);

                if (fontFamily != null)

                    run.FontFamily = fontFamily;

                else

                {

                    // 如果 FontService 加载失败，尝试使用 GetFontFamily（支持字体显示名称）

                    fontFamily = FontService.Instance.GetFontFamily(fontFamilyToApply);

                    if (fontFamily != null)

                        run.FontFamily = fontFamily;

                    else

                    {

                        // 降级：直接使用字体名称（可能是系统字体）

                        try

                        {

                            run.FontFamily = new System.Windows.Media.FontFamily(fontFamilyToApply);

                        }

                        catch { }

                    }

                }

            }

            

            // 应用字体大小

            if (span.FontSize.HasValue && span.FontSize.Value > 0)

                run.FontSize = span.FontSize.Value;

            

            // 应用颜色

            if (!string.IsNullOrEmpty(span.FontColor))

            {

                try

                {

                    var color = (WpfColor)WpfColorConverter.ConvertFromString(span.FontColor);

                    run.Foreground = new WpfSolidColorBrush(color);

                }

                catch { }

            }

            // 应用文字高亮背景（选区背景色，区别于文本框容器背景）
            if (!string.IsNullOrEmpty(span.BackgroundColor) &&
                !string.Equals(span.BackgroundColor, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var backgroundColor = (WpfColor)WpfColorConverter.ConvertFromString(span.BackgroundColor);
                    run.Background = new WpfSolidColorBrush(backgroundColor);
                }
                catch
                {
                    run.Background = null;
                }
            }
            else
            {
                run.Background = null;
            }

            

            // 应用粗体

            run.FontWeight = span.IsBold == 1

                ? System.Windows.FontWeights.Bold

                : System.Windows.FontWeights.Normal;

            

            // 应用斜体

            run.FontStyle = span.IsItalic == 1

                ? System.Windows.FontStyles.Italic

                : System.Windows.FontStyles.Normal;

            

            // 应用下划线

            if (span.IsUnderline == 1)

                run.TextDecorations = System.Windows.TextDecorations.Underline;

        }

        public List<Database.Models.RichTextSpan> ExtractRichTextSpansFromFlowDocument()

        {

            var spans = new List<Database.Models.RichTextSpan>();



            if (_richTextBox == null || _richTextBox.Document == null)

                return spans;



            int spanOrder = 0;

            try

            {
                var fullRange = new System.Windows.Documents.TextRange(
                    _richTextBox.Document.ContentStart,
                    _richTextBox.Document.ContentEnd);
                var fullText = fullRange.Text ?? string.Empty;
                if (fullText.Replace("\r", string.Empty).Replace("\n", string.Empty).Length == 0)
                {
                    return spans;
                }

                int paragraphIndex = 0;
                bool hasAnyTextContent = false;

                foreach (var block in _richTextBox.Document.Blocks)

                {

                    if (block is not System.Windows.Documents.Paragraph paragraph)

                    {
                        continue;
                    }

                    int runIndex = 0;
                    bool paragraphHasTextRun = false;

                    foreach (var inline in paragraph.Inlines)

                    {

                        if (inline is not System.Windows.Documents.Run run)

                        {
                            continue;
                        }

                        string text = run.Text ?? string.Empty;
                        if (string.IsNullOrEmpty(text))
                        {
                            continue;
                        }

                        var span = new Database.Models.RichTextSpan

                        {

                            TextElementId = Data.Id,

                            SpanOrder = spanOrder++,

                            Text = text,

                            ParagraphIndex = paragraphIndex,

                            RunIndex = runIndex++,

                            FormatVersion = Services.TextEditor.Models.RichTextDocumentV2.CurrentFormatVersion

                        };

                        if (run.FontFamily != null)

                        {

                            span.FontFamily = run.FontFamily.Source;

                        }

                        else if (_richTextBox?.FontFamily != null)

                        {

                            span.FontFamily = _richTextBox.FontFamily.Source;

                        }

                        else if (!string.IsNullOrEmpty(Data.FontFamily))

                        {

                            span.FontFamily = Data.FontFamily;

                        }

                        if (!double.IsNaN(run.FontSize) && run.FontSize > 0)

                        {

                            span.FontSize = run.FontSize;

                        }

                        if (run.Foreground is WpfSolidColorBrush brush)

                        {

                            var color = brush.Color;

                            span.FontColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

                        }

                        if (run.Background is WpfSolidColorBrush backgroundBrush)
                        {
                            var bgColor = backgroundBrush.Color;
                            span.BackgroundColor = bgColor.A < 255
                                ? $"#{bgColor.A:X2}{bgColor.R:X2}{bgColor.G:X2}{bgColor.B:X2}"
                                : $"#{bgColor.R:X2}{bgColor.G:X2}{bgColor.B:X2}";
                        }

                        span.IsBold = (run.FontWeight == System.Windows.FontWeights.Bold) ? 1 : 0;
                        span.IsItalic = (run.FontStyle == System.Windows.FontStyles.Italic) ? 1 : 0;
                        span.IsUnderline = (run.TextDecorations == System.Windows.TextDecorations.Underline) ? 1 : 0;

                        spans.Add(span);
                        paragraphHasTextRun = true;
                        hasAnyTextContent = true;
                    }

                    if (!paragraphHasTextRun)

                    {

                        spans.Add(new Database.Models.RichTextSpan

                        {

                            TextElementId = Data.Id,

                            SpanOrder = spanOrder++,

                            Text = string.Empty,

                            ParagraphIndex = paragraphIndex,

                            RunIndex = 0,

                            FormatVersion = Services.TextEditor.Models.RichTextDocumentV2.CurrentFormatVersion

                        });

                    }

                    paragraphIndex++;

                }

                if (!hasAnyTextContent)
                {
                    spans.Clear();
                }

            }

            catch (Exception ex)

            {

                _ = ex;

            }



            return spans;

        }

        #endregion
    }
}


