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

//#if DEBUG

//                System.Diagnostics.Debug.WriteLine($"[提取RichTextSpans] 文本框 ID={Data.Id} 开始提取");

//#endif

                // 遍历所有段落

                foreach (var block in _richTextBox.Document.Blocks)

                {

                    if (block is System.Windows.Documents.Paragraph paragraph)

                    {

                        // 遍历段落中的所有 Inline 元素

                        foreach (var inline in paragraph.Inlines)

                        {

                            if (inline is System.Windows.Documents.Run run)

                            {

                                // 提取文本

                                string text = run.Text;

                                if (string.IsNullOrEmpty(text))

                                    continue;



                                // 提取样式

                                var span = new Database.Models.RichTextSpan

                                {

                                    TextElementId = Data.Id,

                                    SpanOrder = spanOrder++,

                                    Text = text

                                };



                                // 字体

                                // 修复：如果 Run 没有显式设置 FontFamily，使用 RichTextBox 的 FontFamily 或 Data.FontFamily

                                if (run.FontFamily != null)

                                {

                                    span.FontFamily = run.FontFamily.Source;

                                }

                                else

                                {

                                    // 使用 RichTextBox 的 FontFamily 或 Data.FontFamily 作为默认值

                                    if (_richTextBox != null && _richTextBox.FontFamily != null)

                                    {

                                        span.FontFamily = _richTextBox.FontFamily.Source;

                                    }

                                    else if (!string.IsNullOrEmpty(Data.FontFamily))

                                    {

                                        span.FontFamily = Data.FontFamily;

                                    }

                                }



                                // 字号

                                if (!double.IsNaN(run.FontSize) && run.FontSize > 0)

                                {

                                    span.FontSize = run.FontSize;

                                }



                                // 颜色

                                if (run.Foreground is WpfSolidColorBrush brush)

                                {

                                    var color = brush.Color;

                                    span.FontColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

                                }



                                // 加粗

                                span.IsBold = (run.FontWeight == System.Windows.FontWeights.Bold) ? 1 : 0;



                                // 斜体

                                span.IsItalic = (run.FontStyle == System.Windows.FontStyles.Italic) ? 1 : 0;



                                // 下划线

                                span.IsUnderline = (run.TextDecorations == System.Windows.TextDecorations.Underline) ? 1 : 0;



//#if DEBUG

//                                System.Diagnostics.Debug.WriteLine($"  片段 {spanOrder - 1}: 文本='{text}', 字体={span.FontFamily}, 字号={span.FontSize}, 颜色={span.FontColor}, 加粗={span.IsBold}, 斜体={span.IsItalic}");

//#endif

                                spans.Add(span);

                            }

                        }

                    }

                }

//#if DEBUG

//                System.Diagnostics.Debug.WriteLine($"[提取RichTextSpans] 文本框 ID={Data.Id} 提取完成，共 {spans.Count} 个片段");

//#endif

            }

            catch (Exception ex)

            {

//#if DEBUG

//                System.Diagnostics.Debug.WriteLine($" [ExtractRichTextSpans] 提取失败: {ex.Message}");

//#endif

                _ = ex;

            }



            return spans;

        }

        #endregion
    }
}


