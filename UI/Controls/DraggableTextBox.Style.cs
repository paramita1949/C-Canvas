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
        #region 文本样式

        public void ApplyFontFamily(System.Windows.Media.FontFamily fontFamily)

        {

            if (fontFamily != null)

            {

                Data.FontFamily = fontFamily.Source;

                ApplyStylesToRichTextBox();

            }

        }

        public void ApplyStyle(string fontFamily = null, double? fontSize = null,

                               string color = null, bool? isBold = null, string textAlign = null, string textVerticalAlign = null,

                               bool? isUnderline = null, bool? isItalic = null,

                               string borderColor = null, double? borderWidth = null,

                               double? borderRadius = null, int? borderOpacity = null,

                               string backgroundColor = null, double? backgroundRadius = null,

                               int? backgroundOpacity = null,

                               string shadowColor = null, double? shadowOffsetX = null,

                               double? shadowOffsetY = null, double? shadowBlur = null,

                               int? shadowOpacity = null,

                               double? lineSpacing = null, double? letterSpacing = null)

        {

            if (fontFamily != null)

            {

                Data.FontFamily = fontFamily;

            }

            bool hasTypographyStyleParams = fontFamily != null || fontSize.HasValue ||
                                            color != null || isBold.HasValue || textAlign != null || textVerticalAlign != null ||
                                            isUnderline.HasValue || isItalic.HasValue ||
                                            lineSpacing.HasValue || letterSpacing.HasValue;
            bool hasBorderStyleParams = borderColor != null || borderWidth.HasValue ||
                                        borderRadius.HasValue || borderOpacity.HasValue;
            bool hasBackgroundStyleParams = backgroundColor != null || backgroundRadius.HasValue ||
                                            backgroundOpacity.HasValue;
            bool hasShadowStyleParams = shadowColor != null || shadowOffsetX.HasValue ||
                                        shadowOffsetY.HasValue || shadowBlur.HasValue ||
                                        shadowOpacity.HasValue;


            bool needsRichTextResync = false;



            if (fontSize.HasValue)

            {

                Data.FontSize = fontSize.Value;



                // 如果有 RichTextSpans，同步更新所有片段的字体大小

                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)

                {

                    double scaleFactor = fontSize.Value / Data.RichTextSpans.First().FontSize.GetValueOrDefault(40);

                    foreach (var span in Data.RichTextSpans)

                    {

                        if (span.FontSize.HasValue)

                        {

                            span.FontSize = span.FontSize.Value * scaleFactor;

                        }

                    }

                    needsRichTextResync = true;

                }

            }



            if (color != null)

            {

                Data.FontColor = color;

                // 应用全局颜色时，清除局部样式，重新渲染

                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)

                {

                    Data.RichTextSpans.Clear();

                }

                // 无论是否有 RichTextSpans，都需要重新渲染以应用颜色到 Run 对象

                needsRichTextResync = true;

            }



            if (isBold.HasValue)

            {

                Data.IsBoldBool = isBold.Value;

                // 应用全局加粗时，清除局部样式，重新渲染

                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)

                {

                    Data.RichTextSpans.Clear();

                    needsRichTextResync = true;

//#if DEBUG

//                    System.Diagnostics.Debug.WriteLine($" [ApplyStyle] 应用全局加粗，清除局部样式");

//#endif

                }

            }



            if (textAlign != null)

            {

                Data.TextAlign = textAlign;

            }

            if (textVerticalAlign != null)
            {
                Data.TextVerticalAlign = textVerticalAlign;
            }



            if (isUnderline.HasValue)

            {

                Data.IsUnderlineBool = isUnderline.Value;

                // 应用全局下划线时，清除局部样式，重新渲染

                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)

                {

                    Data.RichTextSpans.Clear();

                    needsRichTextResync = true;

//#if DEBUG

//                    System.Diagnostics.Debug.WriteLine($" [ApplyStyle] 应用全局下划线，清除局部样式");

//#endif

                }

            }



            if (isItalic.HasValue)

            {

                Data.IsItalicBool = isItalic.Value;

                // 应用全局斜体时，清除局部样式，重新渲染

                if (Data.RichTextSpans != null && Data.RichTextSpans.Count > 0)

                {

                    Data.RichTextSpans.Clear();

                    needsRichTextResync = true;

//#if DEBUG

//                    System.Diagnostics.Debug.WriteLine($" [ApplyStyle] 应用全局斜体，清除局部样式");

//#endif

                }

            }



            // 边框样式

            if (borderColor != null)

            {

                Data.BorderColor = borderColor;

            }



            if (borderWidth.HasValue)

            {

                Data.BorderWidth = borderWidth.Value;

            }



            if (borderRadius.HasValue)

            {

                Data.BorderRadius = borderRadius.Value;

            }



            if (borderOpacity.HasValue)

            {

                Data.BorderOpacity = borderOpacity.Value;

            }



            // 背景样式

            if (backgroundColor != null)

            {

                Data.BackgroundColor = backgroundColor;
                _useBackgroundGradient = false;
                _backgroundGradientStartColor = null;
                _backgroundGradientEndColor = null;

            }



            if (backgroundRadius.HasValue)

            {

                Data.BackgroundRadius = backgroundRadius.Value;

            }



            if (backgroundOpacity.HasValue)

            {

                Data.BackgroundOpacity = backgroundOpacity.Value;

            }



            // 阴影样式

            if (shadowColor != null)

            {

                Data.ShadowColor = shadowColor;

            }



            if (shadowOffsetX.HasValue)

            {

                Data.ShadowOffsetX = shadowOffsetX.Value;

            }



            if (shadowOffsetY.HasValue)

            {

                Data.ShadowOffsetY = shadowOffsetY.Value;

            }



            if (shadowBlur.HasValue)

            {

                Data.ShadowBlur = shadowBlur.Value;

            }



            if (shadowOpacity.HasValue)

            {

                Data.ShadowOpacity = shadowOpacity.Value;

            }



            // 间距样式

            if (lineSpacing.HasValue)

            {

                Data.LineSpacing = lineSpacing.Value;

            }



            if (letterSpacing.HasValue)

            {

                Data.LetterSpacing = letterSpacing.Value;

            }



            // 仅文字排版相关参数才触发 RichTextBox 排版链路；
            // 填充/边框/阴影走容器样式更新，避免触发布局重排造成“漂移错觉”。
            if (needsRichTextResync)
            {
                SyncTextToRichTextBox();
            }
            else if (hasTypographyStyleParams)
            {
                ApplyStylesToRichTextBox();
            }
            else
            {
                if (hasBorderStyleParams)
                {
                    ApplyBorderStyle();
                }

                if (hasBackgroundStyleParams)
                {
                    ApplyBackgroundStyle();
                }

                if (hasShadowStyleParams)
                {
                    ApplyShadowStyle();
                }
            }



            // 触发内容改变事件，通知主窗口保存样式到数据库

            ContentChanged?.Invoke(this, Data.Content);

        }

        public void SetTextVerticalAlign(string textVerticalAlign)
        {
            if (string.IsNullOrWhiteSpace(textVerticalAlign))
            {
                return;
            }

            if (string.Equals(Data.TextVerticalAlign, textVerticalAlign, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Data.TextVerticalAlign = textVerticalAlign;
            ApplyTextLayoutProfile();
            ContentChanged?.Invoke(this, Data.Content);
        }

        public void ApplyBackgroundGradient(string startColor, string endColor, BackgroundGradientDirection direction = BackgroundGradientDirection.LeftToRight, double? backgroundRadius = null, int? backgroundOpacity = null)
        {
            if (string.IsNullOrWhiteSpace(startColor) || string.IsNullOrWhiteSpace(endColor))
            {
                return;
            }

            _useBackgroundGradient = true;
            _backgroundGradientStartColor = startColor;
            _backgroundGradientEndColor = endColor;
            _backgroundGradientDirection = direction;

            if (backgroundRadius.HasValue)
            {
                Data.BackgroundRadius = backgroundRadius.Value;
            }

            if (backgroundOpacity.HasValue)
            {
                Data.BackgroundOpacity = backgroundOpacity.Value;
            }

            // 仍写入一个基色，保证旧链路存储/回放至少有可见背景。
            Data.BackgroundColor = startColor;

            ApplyBackgroundStyle();
            ContentChanged?.Invoke(this, Data.Content);
        }

        public void ClearBackgroundGradient()
        {
            _useBackgroundGradient = false;
            _backgroundGradientStartColor = null;
            _backgroundGradientEndColor = null;
            _backgroundGradientDirection = BackgroundGradientDirection.LeftToRight;
        }

        public void ApplyBorderLineStyle(BorderLineStyle style)
        {
            _borderLineStyle = style;
            ApplyBorderStyle();
            ContentChanged?.Invoke(this, Data.Content);
        }

        public void ApplyStyleToSelection(System.Windows.Media.FontFamily fontFamilyObj = null,

                                          string fontFamily = null, double? fontSize = null,

                                          string color = null, bool? isBold = null,

                                          bool? isUnderline = null, bool? isItalic = null,

                                          string borderColor = null, double? borderWidth = null,

                                          double? borderRadius = null, int? borderOpacity = null,

                                          string backgroundColor = null, double? backgroundRadius = null,

                                          int? backgroundOpacity = null,

                                          string shadowColor = null, double? shadowOffsetX = null,

                                          double? shadowOffsetY = null, double? shadowBlur = null,

                                          int? shadowOpacity = null)

        {

            // 检查是否有字体样式参数（字体、字号、加粗、斜体、下划线、颜色）

            bool hasFontStyleParams = fontFamilyObj != null || fontFamily != null || fontSize.HasValue ||

                                      color != null || isBold.HasValue || isUnderline.HasValue || isItalic.HasValue;



            // 检查是否有容器样式参数（边框、背景、阴影）

            bool hasContainerStyleParams = borderColor != null || borderWidth.HasValue || borderRadius.HasValue || borderOpacity.HasValue ||

                                           backgroundColor != null || backgroundRadius.HasValue || backgroundOpacity.HasValue ||

                                           shadowColor != null || shadowOffsetX.HasValue || shadowOffsetY.HasValue ||

                                           shadowBlur.HasValue || shadowOpacity.HasValue;



            // 如果没有选中文本

            if (_richTextBox == null || _richTextBox.Selection.IsEmpty)

            {

                // 字体样式：必须选中文字才能修改

                if (hasFontStyleParams && !hasContainerStyleParams)

                {

                    return;

                }



                // 容器样式：无选中时应用到整个文本框

                if (hasContainerStyleParams)

                {

                    ApplyStyle(null, null, null, null, null, null, null, null,

                              borderColor, borderWidth, borderRadius, borderOpacity,

                              backgroundColor, backgroundRadius, backgroundOpacity,

                              shadowColor, shadowOffsetX, shadowOffsetY, shadowBlur, shadowOpacity);

                    return;

                }

            }



            //  使用 WPF 原生 TextRange API

            var selection = _richTextBox.Selection;



            //  应用加粗样式（WPF 原生 API）

            if (isBold.HasValue)

            {

                selection.ApplyPropertyValue(

                    System.Windows.Documents.TextElement.FontWeightProperty,

                    isBold.Value ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal);

                // 同时更新 Data 对象，确保保存到数据库

                Data.IsBoldBool = isBold.Value;

            }



            //  应用斜体样式（WPF 原生 API）

            if (isItalic.HasValue)

            {

                selection.ApplyPropertyValue(

                    System.Windows.Documents.TextElement.FontStyleProperty,

                    isItalic.Value ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal);

                // 同时更新 Data 对象，确保保存到数据库

                Data.IsItalicBool = isItalic.Value;

#if DEBUG

                System.Diagnostics.Debug.WriteLine($"   应用斜体: {isItalic.Value}, Data.IsItalic={Data.IsItalic}");

#endif

            }



            //  应用下划线样式（WPF 原生 API）

            if (isUnderline.HasValue)

            {

                selection.ApplyPropertyValue(

                    System.Windows.Documents.Inline.TextDecorationsProperty,

                    isUnderline.Value ? System.Windows.TextDecorations.Underline : null);

                // 同时更新 Data 对象，确保保存到数据库

                Data.IsUnderlineBool = isUnderline.Value;

#if DEBUG

                System.Diagnostics.Debug.WriteLine($"   应用下划线: {isUnderline.Value}, Data.IsUnderline={Data.IsUnderline}");

#endif

            }



            //  应用文字颜色（WPF 原生 API）

            if (color != null)

            {

                try

                {

                    var wpfColor = (WpfColor)WpfColorConverter.ConvertFromString(color);

                    selection.ApplyPropertyValue(

                        System.Windows.Documents.TextElement.ForegroundProperty,

                        new WpfSolidColorBrush(wpfColor));

#if DEBUG

                    //System.Diagnostics.Debug.WriteLine($"   应用颜色: {color}");

#endif

                }

                catch (Exception)

                {

#if DEBUG

                    System.Diagnostics.Debug.WriteLine($"   颜色转换失败");

#endif

                }

            }



            //  应用字体（WPF 原生 API）- 优先使用 FontFamily 对象

            if (fontFamilyObj != null)

            {

                selection.ApplyPropertyValue(

                    System.Windows.Documents.TextElement.FontFamilyProperty,

                    fontFamilyObj);

              // System.Diagnostics.Debug.WriteLine($"   应用字体对象: {fontFamilyObj.Source}");

            }

            else if (fontFamily != null)

            {

                selection.ApplyPropertyValue(

                    System.Windows.Documents.TextElement.FontFamilyProperty,

                    new System.Windows.Media.FontFamily(fontFamily));

            }



            //  应用字号（WPF 原生 API）

            if (fontSize.HasValue)

            {

                selection.ApplyPropertyValue(

                    System.Windows.Documents.TextElement.FontSizeProperty,

                    fontSize.Value);

                // 同时更新 Data 对象

                Data.FontSize = fontSize.Value;



                // 强制刷新 RichTextBox 布局，防止文本被边框遮挡

                _richTextBox.InvalidateVisual();

                _richTextBox.UpdateLayout();



                //// 检测文本是否被边框遮挡（调试用，已验证修复）

                //try

                //{

                //    var document = _richTextBox.Document;

                //    var contentStart = document.ContentStart;

                //    var contentEnd = document.ContentEnd;

                //

                //    // 获取文本内容的实际渲染边界

                //    var startRect = contentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);

                //    var endRect = contentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);

                //

                //    // 获取 RichTextBox 的可视区域

                //    var richTextBoxWidth = _richTextBox.ActualWidth;

                //    var richTextBoxHeight = _richTextBox.ActualHeight;

                //    var padding = _richTextBox.Padding;

                //

                //    // 计算可视区域（减去 Padding）

                //    var visibleLeft = padding.Left;

                //    var visibleRight = richTextBoxWidth - padding.Right;

                //    var visibleTop = padding.Top;

                //    var visibleBottom = richTextBoxHeight - padding.Bottom;

                //

                //    // 检测左右遮挡

                //    bool leftClipped = startRect.Left < visibleLeft;

                //    bool rightClipped = endRect.Right > visibleRight;

                //    bool topClipped = startRect.Top < visibleTop;

                //    bool bottomClipped = endRect.Bottom > visibleBottom;

                //

                //    System.Diagnostics.Debug.WriteLine($"[字号={fontSize.Value}] 文本边界检测:");

                //    System.Diagnostics.Debug.WriteLine($"   RichTextBox: Width={richTextBoxWidth:F1}, Height={richTextBoxHeight:F1}");

                //    System.Diagnostics.Debug.WriteLine($"   Padding: L={padding.Left}, R={padding.Right}, T={padding.Top}, B={padding.Bottom}");

                //    System.Diagnostics.Debug.WriteLine($"   可视区域: [{visibleLeft:F1}, {visibleRight:F1}] x [{visibleTop:F1}, {visibleBottom:F1}]");

                //    System.Diagnostics.Debug.WriteLine($"   文本边界: [{startRect.Left:F1}, {endRect.Right:F1}] x [{startRect.Top:F1}, {endRect.Bottom:F1}]");

                //

                //    if (leftClipped || rightClipped || topClipped || bottomClipped)

                //    {

                //        System.Diagnostics.Debug.WriteLine($" 检测到遮挡: 左={leftClipped}, 右={rightClipped}, 上={topClipped}, 下={bottomClipped}");

                //    }

                //    else

                //    {

                //        System.Diagnostics.Debug.WriteLine($" 文本未被遮挡");

                //    }

                //}

                //catch (Exception ex)

                //{

                //    System.Diagnostics.Debug.WriteLine($" 边界检测失败: {ex.Message}");

                //}

            }



            // 更新 Data 对象的边框样式（确保保存到数据库）

            if (borderColor != null)

                Data.BorderColor = borderColor;

            if (borderWidth.HasValue)

                Data.BorderWidth = borderWidth.Value;

            if (borderRadius.HasValue)

                Data.BorderRadius = borderRadius.Value;

            if (borderOpacity.HasValue)

                Data.BorderOpacity = borderOpacity.Value;



            // 更新 Data 对象的背景样式（确保保存到数据库）

            if (backgroundColor != null)

                Data.BackgroundColor = backgroundColor;

            if (backgroundRadius.HasValue)

                Data.BackgroundRadius = backgroundRadius.Value;

            if (backgroundOpacity.HasValue)

                Data.BackgroundOpacity = backgroundOpacity.Value;



            // 更新 Data 对象的文字颜色（确保保存到数据库）

            if (color != null)

                Data.FontColor = color;



            // 应用边框和背景样式到 UI

            ApplyBorderStyle();

            ApplyBackgroundStyle();



            // 应用阴影样式到选中文本（修复阴影在有选中文本时无效的问题）

            if (hasContainerStyleParams && (shadowColor != null || shadowOffsetX.HasValue ||

                shadowOffsetY.HasValue || shadowBlur.HasValue || shadowOpacity.HasValue))

            {

                // 更新阴影数据到 Data 对象

                if (shadowColor != null)

                    Data.ShadowColor = shadowColor;

                if (shadowOffsetX.HasValue)

                    Data.ShadowOffsetX = shadowOffsetX.Value;

                if (shadowOffsetY.HasValue)

                    Data.ShadowOffsetY = shadowOffsetY.Value;

                if (shadowBlur.HasValue)

                    Data.ShadowBlur = shadowBlur.Value;

                if (shadowOpacity.HasValue)

                    Data.ShadowOpacity = shadowOpacity.Value;



                // 应用阴影样式到 UI

                ApplyShadowStyle();

            }



            // 触发内容改变事件，通知主窗口保存样式到数据库

            ContentChanged?.Invoke(this, Data.Content);

        }

        public void ApplyHighlightToSelection(string highlightColor)
        {
            if (_richTextBox == null || _richTextBox.Selection.IsEmpty)
            {
                return;
            }

            var selection = _richTextBox.Selection;

            if (string.IsNullOrWhiteSpace(highlightColor) ||
                string.Equals(highlightColor, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                selection.ApplyPropertyValue(System.Windows.Documents.TextElement.BackgroundProperty, null);
                ApplyTextLayoutProfile();
                ContentChanged?.Invoke(this, Data.Content);
                return;
            }

            try
            {
                var color = (WpfColor)WpfColorConverter.ConvertFromString(highlightColor);
                selection.ApplyPropertyValue(
                    System.Windows.Documents.TextElement.BackgroundProperty,
                    new WpfSolidColorBrush(color));
                ApplyTextLayoutProfile();
                ContentChanged?.Invoke(this, Data.Content);
            }
            catch
            {
                // ignore invalid highlight color input
            }
        }

        private void ApplyLineHeightToAllParagraphs()

        {

            ApplyTextLayoutProfile();

        }

        private void ApplyLetterSpacingToAllParagraphs()

        {

            // WPF RichTextBox 不支持字间距，暂不实现

            // 字间距数据仅保存到 Data.LetterSpacing，不应用到 UI

        }

        private void ConvertToRichTextMode()

        {

            // 保留空实现以兼容旧代码

        }

        private void SplitSpansAtSelection(int selStart, int selEnd)

        {

            // 保留空实现以兼容旧代码

        }

        private Database.Models.RichTextSpan CloneSpan(Database.Models.RichTextSpan source)

        {

            return new Database.Models.RichTextSpan

            {

                TextElementId = source.TextElementId,

                SpanOrder = source.SpanOrder,

                Text = source.Text,

                FontFamily = source.FontFamily,

                FontSize = source.FontSize,

                FontColor = source.FontColor,

                IsBold = source.IsBold,

                IsUnderline = source.IsUnderline,

                IsItalic = source.IsItalic,

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

                ParagraphIndex = source.ParagraphIndex,

                RunIndex = source.RunIndex,

                FormatVersion = source.FormatVersion

            };

        }

        private void MergeAdjacentSpans()

        {

            // 保留空实现以兼容旧代码

        }

        private bool SpansHaveSameStyle(Database.Models.RichTextSpan a, Database.Models.RichTextSpan b)

        {

            return a.FontFamily == b.FontFamily &&

                   a.FontSize == b.FontSize &&

                   a.FontColor == b.FontColor &&

                   a.IsBold == b.IsBold &&

                   a.IsUnderline == b.IsUnderline &&

                   a.IsItalic == b.IsItalic &&

                   a.BorderColor == b.BorderColor &&

                   a.BorderWidth == b.BorderWidth &&

                   a.BorderRadius == b.BorderRadius &&

                   a.BorderOpacity == b.BorderOpacity &&

                   a.BackgroundColor == b.BackgroundColor &&

                   a.BackgroundRadius == b.BackgroundRadius &&

                   a.BackgroundOpacity == b.BackgroundOpacity &&

                   a.ShadowColor == b.ShadowColor &&

                   a.ShadowOffsetX == b.ShadowOffsetX &&

                   a.ShadowOffsetY == b.ShadowOffsetY &&

                   a.ShadowBlur == b.ShadowBlur &&

                   a.ShadowOpacity == b.ShadowOpacity;

        }

        #endregion
    }
}


