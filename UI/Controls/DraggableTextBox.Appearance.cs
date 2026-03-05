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
        #region 外观样式

        private void ApplyStylesToRichTextBox()

        {

            if (_richTextBox == null)

                return;



            try

            {

                // 如果有 RichTextSpans，不应用全局字体样式（保持每个片段的独立样式）

                bool hasRichTextSpans = Data.RichTextSpans != null && Data.RichTextSpans.Count > 0;



                // 解析文本颜色（用于光标颜色）

                var color = (WpfColor)WpfColorConverter.ConvertFromString(Data.FontColor);



                if (!hasRichTextSpans)

                {

                    // 使用 FontService 加载字体（支持自定义字体文件）

                    // 优先使用 GetFontFamilyByFamily（支持字体族名称和完整路径）

                    var fontFamily = FontService.Instance.GetFontFamilyByFamily(Data.FontFamily);



                    // 如果失败，尝试使用 GetFontFamily（支持字体显示名称）

                    if (fontFamily == null)

                    {

                        fontFamily = FontService.Instance.GetFontFamily(Data.FontFamily);

                    }



                    if (fontFamily != null)

                    {

                        _richTextBox.FontFamily = fontFamily;

                    }

                    else

                    {

                        // 降级：直接使用字体名称（可能是系统字体）

                        _richTextBox.FontFamily = new WpfFontFamily(Data.FontFamily);

                    }



                    _richTextBox.FontSize = Data.FontSize;



                    // 设置加粗

                    _richTextBox.FontWeight = Data.IsBoldBool

                        ? System.Windows.FontWeights.Bold

                        : System.Windows.FontWeights.Normal;



                    // 设置斜体

                    _richTextBox.FontStyle = Data.IsItalicBool

                        ? System.Windows.FontStyles.Italic

                        : System.Windows.FontStyles.Normal;



                    // 设置文本颜色

                    _richTextBox.Foreground = new WpfSolidColorBrush(color);

                }

                else

                {

                }



                // 设置光标颜色为文本颜色（确保可见）

                _richTextBox.CaretBrush = new WpfSolidColorBrush(color);



                //  应用行高到所有段落

                ApplyLineHeightToAllParagraphs();



                //  字间距功能暂不支持（WPF 限制）

                // ApplyLetterSpacingToAllParagraphs();



                // 设置文本对齐

                switch (Data.TextAlign)

                {

                    case "Left":

                        _richTextBox.Document.TextAlignment = System.Windows.TextAlignment.Left;

                        break;

                    case "Center":

                        _richTextBox.Document.TextAlignment = System.Windows.TextAlignment.Center;

                        break;

                    case "Right":

                        _richTextBox.Document.TextAlignment = System.Windows.TextAlignment.Right;

                        break;

                    case "Justify":

                        _richTextBox.Document.TextAlignment = System.Windows.TextAlignment.Justify;

                        break;

                }



                //  WPF 原生 FlowDocument 已包含所有样式信息

                // 不再需要从 RichTextSpans 表重新构建样式

                // 样式通过 TextRange.ApplyPropertyValue 直接应用到 FlowDocument



                //  应用边框样式到 Border 容器

                ApplyBorderStyle();



                //  应用背景样式到 RichTextBox

                ApplyBackgroundStyle();



                //  应用阴影样式到 RichTextBox

                ApplyShadowStyle();

            }

            catch (Exception)

            {

// #if DEBUG

//                 System.Diagnostics.Debug.WriteLine($" [ApplyStylesToRichTextBox] 失败: {ex.Message}");

// #endif

            }

        }

        private void ApplyBorderStyle()

        {

            if (_border == null)

                return;



            try

            {

                // 边框透明度为 100% 或宽度为 0 时，隐藏边框

                if (Data.BorderOpacity >= 100 || Data.BorderWidth <= 0)

                {

                    _border.BorderThickness = new System.Windows.Thickness(0);
                    _border.BorderBrush = WpfBrushes.Transparent;
                    if (_borderStrokeRect != null)
                    {
                        _borderStrokeRect.StrokeThickness = 0;
                        _borderStrokeRect.Stroke = WpfBrushes.Transparent;
                        _borderStrokeRect.StrokeDashArray = null;
                        _borderStrokeRect.Margin = new System.Windows.Thickness(0);
                    }

                    return;

                }



                // 解析边框颜色

                var borderColor = (WpfColor)WpfColorConverter.ConvertFromString(Data.BorderColor);



                //  应用透明度（反转逻辑：0% = 完全不透明，100% = 完全透明）

                byte alpha = (byte)(255 * (100 - Data.BorderOpacity) / 100.0);

                var borderColorWithAlpha = WpfColor.FromArgb(alpha, borderColor.R, borderColor.G, borderColor.B);



                // 使用描边层统一渲染边框，支持虚线/点线。
                _border.BorderThickness = new System.Windows.Thickness(0);
                _border.BorderBrush = WpfBrushes.Transparent;
                _border.CornerRadius = new System.Windows.CornerRadius(Data.BorderRadius);

                if (_borderStrokeRect != null)
                {
                    _borderStrokeRect.Stroke = new WpfSolidColorBrush(borderColorWithAlpha);
                    _borderStrokeRect.StrokeThickness = Data.BorderWidth;
                    _borderStrokeRect.RadiusX = Data.BorderRadius;
                    _borderStrokeRect.RadiusY = Data.BorderRadius;
                    _borderStrokeRect.Margin = new System.Windows.Thickness(Data.BorderWidth / 2.0);
                    _borderStrokeRect.StrokeDashCap = System.Windows.Media.PenLineCap.Round;
                    _borderStrokeRect.StrokeStartLineCap = System.Windows.Media.PenLineCap.Round;
                    _borderStrokeRect.StrokeEndLineCap = System.Windows.Media.PenLineCap.Round;

                    switch (_borderLineStyle)
                    {
                        case BorderLineStyle.Dashed:
                            _borderStrokeRect.StrokeDashArray = new System.Windows.Media.DoubleCollection { 6, 3 };
                            break;
                        case BorderLineStyle.Dotted:
                            _borderStrokeRect.StrokeDashArray = new System.Windows.Media.DoubleCollection { 1, 2.2 };
                            break;
                        case BorderLineStyle.DashDot:
                            _borderStrokeRect.StrokeDashArray = new System.Windows.Media.DoubleCollection { 6, 2.6, 1, 2.6 };
                            break;
                        case BorderLineStyle.LongDash:
                            _borderStrokeRect.StrokeDashArray = new System.Windows.Media.DoubleCollection { 10, 4 };
                            break;
                        case BorderLineStyle.Solid:
                        default:
                            _borderStrokeRect.StrokeDashArray = null;
                            break;
                    }
                }

            }

            catch (Exception)

            {

            }

        }

        private void ApplyBackgroundStyle()

        {

            if (_richTextBox == null || _border == null)

                return;



            try

            {
                bool isNoticeComponent = string.Equals(Data?.ComponentType, "Notice", StringComparison.OrdinalIgnoreCase);
                if (isNoticeComponent)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[NoticeColorTrace][Render] ApplyBackgroundStyle: id={Data?.Id}, bg={Data?.BackgroundColor}, opacity={Data?.BackgroundOpacity}, gradient={_useBackgroundGradient}");
                }

                // 背景透明度为 100% 时，使用透明背景

                if (Data.BackgroundOpacity >= 100)

                {

                    _border.Background = WpfBrushes.Transparent;

                    _richTextBox.Background = WpfBrushes.Transparent;

                    if (isNoticeComponent)
                    {
                        System.Diagnostics.Debug.WriteLine("[NoticeColorTrace][Render][WARN] 背景透明度=100，视觉不可见");
                    }

                    // 圆角仍然应用到 Border（与边框圆角共享）

                    ApplyBackgroundCornerRadius();

                    return;

                }

                // 渐变背景：优先于纯色背景渲染。
                if (_useBackgroundGradient &&
                    !string.IsNullOrWhiteSpace(_backgroundGradientStartColor) &&
                    !string.IsNullOrWhiteSpace(_backgroundGradientEndColor))
                {
                    var startColorRaw = (WpfColor)WpfColorConverter.ConvertFromString(_backgroundGradientStartColor);
                    var endColorRaw = (WpfColor)WpfColorConverter.ConvertFromString(_backgroundGradientEndColor);

                    byte gradientAlpha = (byte)(255 * (100 - Data.BackgroundOpacity) / 100.0);
                    var startColor = WpfColor.FromArgb(gradientAlpha, startColorRaw.R, startColorRaw.G, startColorRaw.B);
                    var endColor = WpfColor.FromArgb(gradientAlpha, endColorRaw.R, endColorRaw.G, endColorRaw.B);

                    System.Windows.Media.Brush gradientBrush;
                    switch (_backgroundGradientDirection)
                    {
                        case BackgroundGradientDirection.TopToBottom:
                            gradientBrush = new System.Windows.Media.LinearGradientBrush(
                                startColor, endColor, new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));
                            break;
                        case BackgroundGradientDirection.BottomToTop:
                            gradientBrush = new System.Windows.Media.LinearGradientBrush(
                                startColor, endColor, new System.Windows.Point(0, 1), new System.Windows.Point(0, 0));
                            break;
                        case BackgroundGradientDirection.RightToLeft:
                            gradientBrush = new System.Windows.Media.LinearGradientBrush(
                                startColor, endColor, new System.Windows.Point(1, 0), new System.Windows.Point(0, 0));
                            break;
                        case BackgroundGradientDirection.RadialCenter:
                            gradientBrush = new System.Windows.Media.RadialGradientBrush
                            {
                                GradientOrigin = new System.Windows.Point(0.5, 0.5),
                                Center = new System.Windows.Point(0.5, 0.5),
                                RadiusX = 0.68,
                                RadiusY = 0.68,
                                GradientStops = new System.Windows.Media.GradientStopCollection
                                {
                                    new System.Windows.Media.GradientStop(startColor, 0),
                                    new System.Windows.Media.GradientStop(endColor, 1)
                                }
                            };
                            break;
                        case BackgroundGradientDirection.LeftToRight:
                        default:
                            gradientBrush = new System.Windows.Media.LinearGradientBrush(
                                startColor, endColor, new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
                            break;
                    }

                    _border.Background = gradientBrush;
                    _richTextBox.Background = WpfBrushes.Transparent;
                    ApplyBackgroundCornerRadius();
                    return;
                }



                // 解析背景颜色

                WpfColor backgroundColor;

                if (Data.BackgroundColor == "Transparent" || string.IsNullOrEmpty(Data.BackgroundColor))

                {

                    _border.Background = WpfBrushes.Transparent;

                    _richTextBox.Background = WpfBrushes.Transparent;

                    ApplyBackgroundCornerRadius();

                    return;

                }

                else

                {

                    backgroundColor = (WpfColor)WpfColorConverter.ConvertFromString(Data.BackgroundColor);

                }



                //  应用透明度（反转逻辑：0% = 完全不透明，100% = 完全透明）

                byte alpha = (byte)(255 * (100 - Data.BackgroundOpacity) / 100.0);

                var backgroundColorWithAlpha = WpfColor.FromArgb(alpha, backgroundColor.R, backgroundColor.G, backgroundColor.B);



                //  设置背景到 Border 容器（支持圆角）

                _border.Background = new WpfSolidColorBrush(backgroundColorWithAlpha);

                // RichTextBox 保持透明，让 Border 的背景透过

                _richTextBox.Background = WpfBrushes.Transparent;



                //  应用背景圆角到 Border 容器

                ApplyBackgroundCornerRadius();

            }

            catch (Exception ex)

            {
                if (string.Equals(Data?.ComponentType, "Notice", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[NoticeColorTrace][Render][ERROR] 背景应用失败: id={Data?.Id}, bg={Data?.BackgroundColor}, opacity={Data?.BackgroundOpacity}, ex={ex.Message}");
                }

            }

        }

        private void ApplyBackgroundCornerRadius()

        {

            if (_border == null)

                return;



            try

            {

                // 背景圆角和边框圆角取最大值（确保圆角效果正确显示）

                double maxRadius = Math.Max(Data.BackgroundRadius, Data.BorderRadius);

                _border.CornerRadius = new System.Windows.CornerRadius(maxRadius);

            }

            catch (Exception)

            {

            }

        }

        private void ApplyShadowStyle()

        {

            if (_richTextBox == null)

                return;



            try

            {

                // 阴影透明度为 0 或偏移/模糊都为 0 时，移除阴影效果

                if (Data.ShadowOpacity <= 0 ||

                    (Math.Abs(Data.ShadowOffsetX) < 0.1 && Math.Abs(Data.ShadowOffsetY) < 0.1 && Data.ShadowBlur < 0.1))

                {

                    _richTextBox.Effect = null;

                    return;

                }



                // 解析阴影颜色

                var shadowColor = (WpfColor)WpfColorConverter.ConvertFromString(Data.ShadowColor);



                // 创建 DropShadowEffect

                var shadowEffect = new System.Windows.Media.Effects.DropShadowEffect

                {

                    Color = shadowColor,

                    BlurRadius = Data.ShadowBlur,

                    ShadowDepth = Math.Sqrt(Data.ShadowOffsetX * Data.ShadowOffsetX + Data.ShadowOffsetY * Data.ShadowOffsetY),

                    Direction = Math.Atan2(Data.ShadowOffsetY, Data.ShadowOffsetX) * 180 / Math.PI,

                    Opacity = Data.ShadowOpacity / 100.0  //  直接使用透明度百分比（0-100 → 0.0-1.0）

                };



                _richTextBox.Effect = shadowEffect;



// #if DEBUG

//                 System.Diagnostics.Debug.WriteLine($" [ApplyShadowStyle] 颜色={Data.ShadowColor}, 偏移=({Data.ShadowOffsetX:F2}, {Data.ShadowOffsetY:F2}), 模糊={Data.ShadowBlur}, 透明度={Data.ShadowOpacity}%");

// #endif

            }

            catch (Exception ex)

            {

// #if DEBUG

//                 System.Diagnostics.Debug.WriteLine($" [ApplyShadowStyle] 失败: {ex.Message}");

// #else

                _ = ex;  // 防止未使用变量警告

// #endif

            }

        }

        #endregion
    }
}


