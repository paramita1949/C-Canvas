using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// SkiaSharp文本渲染引擎
    /// </summary>
    public class SkiaTextRenderer
    {
        private readonly IMemoryCache _cache;
        private readonly TextLayoutEngine _layoutEngine;
        
        public SkiaTextRenderer(IMemoryCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _layoutEngine = new TextLayoutEngine();
        }
        
        /// <summary>
        /// 渲染单个文本框
        /// </summary>
        public SKBitmap RenderTextBox(TextBoxRenderContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            
//#if DEBUG
//            var sw = System.Diagnostics.Stopwatch.StartNew();
//#endif

            // 1. 检查缓存
            var cacheKey = context.GetCacheKey();
            if (_cache.TryGetValue(cacheKey, out SKBitmap cachedBitmap))
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"✅ [SkiaTextRenderer] 缓存命中: {cacheKey.Substring(0, Math.Min(50, cacheKey.Length))}...");
//#endif
                return cachedBitmap;
            }
            
            // 2. 创建Bitmap和Canvas
            int width = (int)Math.Ceiling(context.Size.Width);
            int height = (int)Math.Ceiling(context.Size.Height);
            
            if (width <= 0 || height <= 0)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"⚠️ [SkiaTextRenderer] 无效尺寸: {width}x{height}");
//#endif
                return new SKBitmap(1, 1);
            }
            
            var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            
            // 3. 绘制背景
            if (context.BackgroundColor.HasValue)
            {
                canvas.Clear(context.BackgroundColor.Value);
            }
            else
            {
                canvas.Clear(SKColors.Transparent);
            }
            
            // 4. 计算有效渲染区域（减去Padding）
            float contentWidth = context.Size.Width - context.Padding.Left - context.Padding.Right;
            float contentHeight = context.Size.Height - context.Padding.Top - context.Padding.Bottom;
            
            if (contentWidth <= 0 || string.IsNullOrEmpty(context.Text))
            {
                // 没有有效内容，返回背景
                return bitmap;
            }
            
            // 5. 计算文本布局
            var layout = _layoutEngine.CalculateLayout(context.Text, context.Style, contentWidth);

            // 6. 计算文本区域边界（用于背景和边框）
            var textBounds = CalculateTextBounds(layout, context);

            // 7. 绘制阴影（如果启用）
            if (context.Style.ShadowOpacity > 0 && context.Style.ShadowBlur > 0)
            {
                DrawShadow(canvas, textBounds, context.Style);
            }

            // 8. 绘制背景（如果启用）
            if (context.Style.BackgroundOpacity > 0)
            {
                DrawBackground(canvas, textBounds, context.Style);
            }

            // 9. 绘制边框（如果启用）
            if (context.Style.BorderOpacity > 0 && context.Style.BorderWidth > 0)
            {
                DrawBorder(canvas, textBounds, context.Style);
            }

            // 10. 创建Paint
            using var paint = CreatePaint(context.Style);
            paint.TextAlign = context.Alignment;

            // 11. 绘制选择区域（编辑模式下，在文本之前绘制）
            if (context.IsEditing && context.SelectionStart.HasValue && context.SelectionEnd.HasValue)
            {
                DrawSelection(canvas, layout, context, paint);
            }

            // 12. 逐行绘制文本
            foreach (var line in layout.Lines)
            {
                float x = context.Padding.Left;
                float y = context.Padding.Top + line.Position.Y;

                // 根据对齐方式调整X坐标
                if (context.Alignment == SKTextAlign.Center)
                {
                    x = context.Padding.Left + contentWidth / 2;
                }
                else if (context.Alignment == SKTextAlign.Right)
                {
                    x = context.Padding.Left + contentWidth;
                }

                canvas.DrawText(line.Text, x, y, paint);

                // 🆕 绘制下划线
                if (context.Style.IsUnderline)
                {
                    DrawUnderline(canvas, line.Text, x, y, paint, context.Alignment);
                }
            }

            // 13. 绘制光标（编辑模式下，在文本之后绘制）
            if (context.IsEditing && context.CursorVisible)
            {
                DrawCursor(canvas, layout, context, paint);
            }

            // 14. 缓存结果（编辑模式下不缓存）
            if (!context.IsEditing)
            {
                _cache.Set(cacheKey, bitmap, TimeSpan.FromMinutes(5));
            }

            return bitmap;
        }
        
        /// <summary>
        /// 渲染圣经经文（支持标题+经文布局，自动计算内容高度）
        /// </summary>
        public SKBitmap RenderBibleText(BibleRenderContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            
#if DEBUG
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif

            int width = (int)Math.Ceiling(context.Size.Width);
            float screenHeight = context.Size.Height;
            
            if (width <= 0 || screenHeight <= 0)
            {
                return new SKBitmap(1, 1);
            }
            
            float contentWidth = width - context.Padding.Left - context.Padding.Right;
            
            // ========================================
            // 第一步：预计算所有内容的总高度
            // ========================================
            float currentY = context.Padding.Top;
            var verseLayouts = new List<VerseLayout>();
            
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"📏 [投影节距] VerseSpacing配置: {context.VerseSpacing}");
            //#endif
            
            // 用于跟踪是否是第一个标题
            bool isFirstTitle = true;
            
            for (int i = 0; i < context.Verses.Count; i++)
            {
                var verse = context.Verses[i];
                float verseStartY = currentY;
                
                if (verse.IsTitle)
                {
                    // 如果不是第一个标题，添加额外的记录分隔间距（固定60像素）
                    if (!isFirstTitle)
                    {
                        currentY += 60;
                        verseStartY = currentY;
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📌 [记录分隔] 添加固定间距60，currentY: {currentY - 60} -> {currentY}");
                        //#endif
                    }
                    isFirstTitle = false;
                    
                    // 计算标题行高度
                    using var titlePaint = CreatePaint(context.TitleStyle);
                    var titleLayout = _layoutEngine.CalculateLayout(verse.Text, context.TitleStyle, contentWidth);
                    float titleHeight = titleLayout.TotalSize.Height;
                    
                    verseLayouts.Add(new VerseLayout
                    {
                        Verse = verse,
                        StartY = verseStartY,
                        Lines = new List<string> { verse.Text },
                        Height = titleHeight,
                        NumberWidth = 0
                    });
                    
                    // 标题后的间距固定为15像素
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📌 [标题间距] 标题高度: {titleHeight}, 固定间距: 15, currentY变化: {currentY} -> {currentY + titleHeight + 15}");
                    //#endif
                    currentY += titleHeight + 15;
                }
                else
                {
                    // 计算经文行高度
                    using var numberPaint = CreatePaint(context.VerseNumberStyle);
                    using var versePaint = CreatePaint(context.VerseStyle);
                    
                    string verseNumberText = $"{verse.VerseNumber} ";
                    float numberWidth = numberPaint.MeasureText(verseNumberText);
                    
                    // 第一行经文紧跟节号
                    float firstLineWidth = contentWidth - numberWidth;
                    var lines = _layoutEngine.WrapText(verse.Text, versePaint, firstLineWidth);
                    
                    float verseHeight = lines.Count * context.VerseStyle.FontSize * context.VerseStyle.LineSpacing;
                    
                    verseLayouts.Add(new VerseLayout
                    {
                        Verse = verse,
                        StartY = verseStartY,
                        Lines = lines,
                        Height = verseHeight,
                        NumberWidth = numberWidth
                    });
                    
                    // 经文后的间距：如果不是最后一节，使用配置的节距
                    if (i < context.Verses.Count - 1)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📌 [经文间距] 第{verse.VerseNumber}节, 高度: {verseHeight}, 节距: {context.VerseSpacing}, currentY变化: {currentY} -> {currentY + verseHeight + context.VerseSpacing}");
                        //#endif
                        currentY += verseHeight + context.VerseSpacing;
                    }
                    else
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📌 [最后一节] 第{verse.VerseNumber}节, 高度: {verseHeight}, 无额外间距");
                        //#endif
                        currentY += verseHeight;
                    }
                }
            }
            
            // 添加底部扩展空间（与主屏幕一致，支持底部内容向上拉）
            float contentHeight = currentY + screenHeight + context.Padding.Bottom;
            
            // ========================================
            // 第二步：根据实际内容高度创建Bitmap并渲染
            // ========================================
            int actualHeight = (int)Math.Ceiling(contentHeight);
            var bitmap = new SKBitmap(width, actualHeight);
            using var canvas = new SKCanvas(bitmap);
            
            // 绘制背景
            canvas.Clear(context.BackgroundColor);
            
            // 渲染所有经文
            foreach (var layout in verseLayouts)
            {
                if (layout.Verse.IsTitle)
                {
                    // 渲染标题行
                    using var titlePaint = CreatePaint(context.TitleStyle);
                    titlePaint.TextAlign = SKTextAlign.Left;
                    
                    float x = context.Padding.Left;
                    float y = layout.StartY + context.TitleStyle.FontSize;
                    canvas.DrawText(layout.Verse.Text, x, y, titlePaint);
                }
                else
                {
                    // 渲染经文行（🔧 高亮时使用高亮颜色）
                    var verseColor = layout.Verse.IsHighlighted 
                        ? context.HighlightColor 
                        : context.VerseStyle.TextColor;
                    
                    // 节号（🔧 高亮时也使用高亮颜色）
                    using var numberPaint = CreatePaint(context.VerseNumberStyle);
                    if (layout.Verse.IsHighlighted)
                    {
                        numberPaint.Color = context.HighlightColor;
                    }
                    string verseNumberText = $"{layout.Verse.VerseNumber} ";
                    canvas.DrawText(verseNumberText, context.Padding.Left, layout.StartY + context.VerseStyle.FontSize, numberPaint);
                    
                    // 经文内容
                    using var versePaint = CreatePaint(context.VerseStyle);
                    versePaint.Color = verseColor;
                    
                    float lineY = layout.StartY;
                    for (int i = 0; i < layout.Lines.Count; i++)
                    {
                        float x = (i == 0) ? context.Padding.Left + layout.NumberWidth : context.Padding.Left;
                        float y = lineY + context.VerseStyle.FontSize;
                        canvas.DrawText(layout.Lines[i], x, y, versePaint);
                        lineY += context.VerseStyle.FontSize * context.VerseStyle.LineSpacing;
                    }
                }
            }
            
//#if DEBUG
//            sw.Stop();
//            System.Diagnostics.Debug.WriteLine($"📖 [SkiaTextRenderer-Bible] 完成: {context.Verses.Count}节, 尺寸: {width}×{actualHeight}, {sw.ElapsedMilliseconds}ms");
//#endif
            
            return bitmap;
        }
        
        /// <summary>
        /// 经文布局信息（用于两步渲染）
        /// </summary>
        private class VerseLayout
        {
            public BibleVerseItem Verse { get; set; }
            public float StartY { get; set; }
            public List<string> Lines { get; set; }
            public float Height { get; set; }
            public float NumberWidth { get; set; }
        }
        
        /// <summary>
        /// 渲染歌词（支持自动计算内容高度）
        /// </summary>
        public SKBitmap RenderLyrics(LyricsRenderContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            
#if DEBUG
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif

            int width = (int)Math.Ceiling(context.Size.Width);
            float screenHeight = context.Size.Height;
            
            if (width <= 0 || screenHeight <= 0)
            {
                return new SKBitmap(1, 1);
            }
            
            if (string.IsNullOrEmpty(context.Text))
            {
                var emptyBitmap = new SKBitmap(width, (int)screenHeight);
                using var emptyCanvas = new SKCanvas(emptyBitmap);
                emptyCanvas.Clear(context.BackgroundColor);
                return emptyBitmap;
            }
            
            // ========================================
            // 第一步：预计算内容高度
            // ========================================
            // 创建Paint
            using var paint = CreatePaint(context.Style);
            paint.TextAlign = context.Alignment;
            
            // 计算有效宽度
            float contentWidth = width - context.Padding.Left - context.Padding.Right;
            
            // 自动换行
            var lines = _layoutEngine.WrapText(context.Text, paint, contentWidth);
            
            // 计算内容实际高度
            float totalHeight = lines.Count * context.Style.FontSize * context.Style.LineSpacing;
            float startY = context.Padding.Top;
            
            // 计算实际所需的Bitmap高度（内容高度 vs 屏幕高度）
            float contentHeight = startY + totalHeight + context.Padding.Bottom;
            int actualHeight = (int)Math.Ceiling(Math.Max(contentHeight, screenHeight));
            
            // ========================================
            // 第二步：创建Bitmap并渲染
            // ========================================
            var bitmap = new SKBitmap(width, actualHeight);
            using var canvas = new SKCanvas(bitmap);
            
            // 绘制背景
            canvas.Clear(context.BackgroundColor);
            
            // 逐行绘制
            float currentY = startY + context.Style.FontSize;
            float centerX = width / 2f;
            float rightX = width - context.Padding.Right;
            
            foreach (var line in lines)
            {
                float x;
                switch (context.Alignment)
                {
                    case SKTextAlign.Center:
                        x = centerX;
                        break;
                    case SKTextAlign.Right:
                        x = rightX;
                        break;
                    default: // Left
                        x = context.Padding.Left;
                        break;
                }
                
                canvas.DrawText(line, x, currentY, paint);
                currentY += context.Style.FontSize * context.Style.LineSpacing;
            }
            
//#if DEBUG
//            sw.Stop();
//            System.Diagnostics.Debug.WriteLine($"🎵 [SkiaTextRenderer-Lyrics] 完成: {lines.Count}行, 尺寸: {width}×{actualHeight}, {sw.ElapsedMilliseconds}ms");
//#endif
            
            return bitmap;
        }
        
        /// <summary>
        /// 创建Paint对象
        /// </summary>
        private SKPaint CreatePaint(TextStyle style)
        {
            // ✅ 使用SkiaFontService加载字体（支持自定义字体文件）
            var typeface = SkiaFontService.Instance.GetTypeface(style.FontFamily, style.IsBold, style.IsItalic);
            
            var paint = new SKPaint
            {
                Typeface = typeface,
                TextSize = style.FontSize,
                Color = style.TextColor,
                IsAntialias = true,
                SubpixelText = true
            };
            
            // 🔧 如果需要加粗，启用伪加粗（对于不支持加粗的自定义字体）
            if (style.IsBold)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"    🎨 [CreatePaint] 启用加粗: 字体={style.FontFamily}, FakeBoldText=true");
//#endif
                paint.FakeBoldText = true;
            }

            // ✅ 应用字间距（使用 TextScaleX 实现水平拉伸）
            if (style.LetterSpacing > 0)
            {
                paint.TextScaleX = 1.0f + style.LetterSpacing;
            }

            return paint;
        }

        /// <summary>
        /// 绘制下划线
        /// </summary>
        /// <param name="canvas">画布</param>
        /// <param name="text">文本内容</param>
        /// <param name="x">文本X坐标</param>
        /// <param name="y">文本Y坐标（基线位置）</param>
        /// <param name="textPaint">文本Paint对象（用于测量和获取颜色）</param>
        /// <param name="alignment">文本对齐方式</param>
        private void DrawUnderline(SKCanvas canvas, string text, float x, float y, SKPaint textPaint, SKTextAlign alignment)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // 1. 测量文本宽度
            float textWidth = textPaint.MeasureText(text);

            // 2. 计算下划线起点X坐标（根据对齐方式）
            float underlineStartX;
            switch (alignment)
            {
                case SKTextAlign.Center:
                    underlineStartX = x - textWidth / 2;
                    break;
                case SKTextAlign.Right:
                    underlineStartX = x - textWidth;
                    break;
                default: // Left
                    underlineStartX = x;
                    break;
            }

            // 3. 计算下划线Y坐标（基线下方，距离约为字体大小的10%）
            float underlineY = y + textPaint.TextSize * 0.1f;

            // 4. 创建下划线Paint
            using var underlinePaint = new SKPaint
            {
                Color = textPaint.Color,
                StrokeWidth = Math.Max(1f, textPaint.TextSize * 0.05f), // 粗细为字体大小的5%，最小1像素
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            // 5. 绘制下划线
            canvas.DrawLine(underlineStartX, underlineY, underlineStartX + textWidth, underlineY, underlinePaint);
        }

        /// <summary>
        /// 计算文本区域边界（用于背景和边框）
        /// </summary>
        private SKRect CalculateTextBounds(TextLayout layout, TextBoxRenderContext context)
        {
            if (layout.Lines.Count == 0)
            {
                return SKRect.Empty;
            }

            // 计算文本实际占用的矩形区域
            float left = context.Padding.Left;
            float top = context.Padding.Top;
            float right = context.Padding.Left + layout.TotalSize.Width;
            float bottom = context.Padding.Top + layout.TotalSize.Height;

            // 添加一些内边距（让背景和边框不要紧贴文字）
            float padding = context.Style.FontSize * 0.1f;
            return new SKRect(
                left - padding,
                top - padding,
                right + padding,
                bottom + padding
            );
        }

        /// <summary>
        /// 绘制阴影
        /// </summary>
        private void DrawShadow(SKCanvas canvas, SKRect bounds, TextStyle style)
        {
            if (style.ShadowOpacity <= 0 || style.ShadowBlur <= 0)
                return;

            // 计算阴影颜色（应用不透明度）
            byte alpha = (byte)(style.ShadowColor.Alpha * style.ShadowOpacity / 100f);
            var shadowColor = new SKColor(
                style.ShadowColor.Red,
                style.ShadowColor.Green,
                style.ShadowColor.Blue,
                alpha
            );

            // 创建阴影Paint（带模糊效果）
            using var shadowPaint = new SKPaint
            {
                Color = shadowColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, style.ShadowBlur)
            };

            // 计算阴影矩形位置（偏移）
            var shadowBounds = new SKRect(
                bounds.Left + style.ShadowOffsetX,
                bounds.Top + style.ShadowOffsetY,
                bounds.Right + style.ShadowOffsetX,
                bounds.Bottom + style.ShadowOffsetY
            );

            // 绘制圆角矩形阴影
            if (style.BackgroundRadius > 0)
            {
                canvas.DrawRoundRect(shadowBounds, style.BackgroundRadius, style.BackgroundRadius, shadowPaint);
            }
            else
            {
                canvas.DrawRect(shadowBounds, shadowPaint);
            }
        }

        /// <summary>
        /// 绘制背景
        /// </summary>
        private void DrawBackground(SKCanvas canvas, SKRect bounds, TextStyle style)
        {
            if (style.BackgroundOpacity >= 100)
                return;

            // ✅ 计算背景颜色（应用透明度：0% = 完全不透明，100% = 完全透明）
            byte alpha = (byte)(255 * (100 - style.BackgroundOpacity) / 100f);
            var backgroundColor = new SKColor(
                style.BackgroundColor.Red,
                style.BackgroundColor.Green,
                style.BackgroundColor.Blue,
                alpha
            );

            // 创建背景Paint
            using var backgroundPaint = new SKPaint
            {
                Color = backgroundColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            // 绘制圆角矩形背景
            if (style.BackgroundRadius > 0)
            {
                canvas.DrawRoundRect(bounds, style.BackgroundRadius, style.BackgroundRadius, backgroundPaint);
            }
            else
            {
                canvas.DrawRect(bounds, backgroundPaint);
            }
        }

        /// <summary>
        /// 绘制边框
        /// </summary>
        private void DrawBorder(SKCanvas canvas, SKRect bounds, TextStyle style)
        {
            if (style.BorderOpacity >= 100 || style.BorderWidth <= 0)
                return;

            // ✅ 计算边框颜色（应用透明度：0% = 完全不透明，100% = 完全透明）
            byte alpha = (byte)(255 * (100 - style.BorderOpacity) / 100f);
            var borderColor = new SKColor(
                style.BorderColor.Red,
                style.BorderColor.Green,
                style.BorderColor.Blue,
                alpha
            );

            // 创建边框Paint
            using var borderPaint = new SKPaint
            {
                Color = borderColor,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = style.BorderWidth
            };

            // 绘制圆角矩形边框
            if (style.BorderRadius > 0)
            {
                canvas.DrawRoundRect(bounds, style.BorderRadius, style.BorderRadius, borderPaint);
            }
            else
            {
                canvas.DrawRect(bounds, borderPaint);
            }
        }

        /// <summary>
        /// 绘制光标（垂直线）
        /// </summary>
        private void DrawCursor(SKCanvas canvas, TextLayout layout, TextBoxRenderContext context, SKPaint textPaint)
        {
            if (layout.Lines.Count == 0)
                return;

            // 计算光标在文本中的位置
            int currentPos = 0;
            float cursorX = context.Padding.Left;
            float cursorY = context.Padding.Top;
            float cursorHeight = context.Style.FontSize;

            // 遍历每一行，找到光标所在位置
            foreach (var line in layout.Lines)
            {
                int lineLength = line.Text.Length;
                int lineEnd = currentPos + lineLength;

                if (context.CursorPosition >= currentPos && context.CursorPosition <= lineEnd)
                {
                    // 光标在当前行
                    int posInLine = context.CursorPosition - currentPos;
                    string textBeforeCursor = line.Text.Substring(0, Math.Min(posInLine, line.Text.Length));

                    // 计算光标X坐标
                    float textWidth = textPaint.MeasureText(textBeforeCursor);
                    cursorX = context.Padding.Left;

                    // 根据对齐方式调整X坐标
                    float contentWidth = context.Size.Width - context.Padding.Left - context.Padding.Right;
                    if (context.Alignment == SKTextAlign.Center)
                    {
                        float lineWidth = textPaint.MeasureText(line.Text);
                        cursorX = context.Padding.Left + (contentWidth - lineWidth) / 2 + textWidth;
                    }
                    else if (context.Alignment == SKTextAlign.Right)
                    {
                        float lineWidth = textPaint.MeasureText(line.Text);
                        cursorX = context.Padding.Left + contentWidth - lineWidth + textWidth;
                    }
                    else
                    {
                        cursorX += textWidth;
                    }

                    cursorY = context.Padding.Top + line.Position.Y - context.Style.FontSize * 0.8f;
                    cursorHeight = context.Style.FontSize;
                    break;
                }

                currentPos = lineEnd;
            }

            // 绘制光标（亮蓝色垂直线）
            using var cursorPaint = new SKPaint
            {
                Color = new SKColor(0, 150, 255), // 亮蓝色 #0096FF
                StrokeWidth = 2f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            canvas.DrawLine(cursorX, cursorY, cursorX, cursorY + cursorHeight, cursorPaint);
        }

        /// <summary>
        /// 绘制选择区域（蓝色高亮背景）
        /// </summary>
        private void DrawSelection(SKCanvas canvas, TextLayout layout, TextBoxRenderContext context, SKPaint textPaint)
        {
            if (!context.SelectionStart.HasValue || !context.SelectionEnd.HasValue)
                return;

            int selStart = Math.Min(context.SelectionStart.Value, context.SelectionEnd.Value);
            int selEnd = Math.Max(context.SelectionStart.Value, context.SelectionEnd.Value);

            if (selStart == selEnd || layout.Lines.Count == 0)
                return;

            // 创建选择区域Paint（半透明蓝色）
            using var selectionPaint = new SKPaint
            {
                Color = new SKColor(0, 120, 215, 80), // 半透明蓝色 #0078D7 with 30% opacity
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            int currentPos = 0;
            float contentWidth = context.Size.Width - context.Padding.Left - context.Padding.Right;

            // 遍历每一行，绘制选择区域
            foreach (var line in layout.Lines)
            {
                int lineLength = line.Text.Length;
                int lineEnd = currentPos + lineLength;

                // 检查当前行是否包含选择区域
                if (lineEnd > selStart && currentPos < selEnd)
                {
                    // 计算选择区域在当前行的起始和结束位置
                    int selStartInLine = Math.Max(0, selStart - currentPos);
                    int selEndInLine = Math.Min(lineLength, selEnd - currentPos);

                    string textBeforeSelection = line.Text.Substring(0, selStartInLine);
                    string selectedText = line.Text.Substring(selStartInLine, selEndInLine - selStartInLine);

                    float selectionStartX = context.Padding.Left;
                    float selectionWidth = textPaint.MeasureText(selectedText);
                    float textBeforeWidth = textPaint.MeasureText(textBeforeSelection);

                    // 根据对齐方式调整X坐标
                    if (context.Alignment == SKTextAlign.Center)
                    {
                        float lineWidth = textPaint.MeasureText(line.Text);
                        selectionStartX = context.Padding.Left + (contentWidth - lineWidth) / 2 + textBeforeWidth;
                    }
                    else if (context.Alignment == SKTextAlign.Right)
                    {
                        float lineWidth = textPaint.MeasureText(line.Text);
                        selectionStartX = context.Padding.Left + contentWidth - lineWidth + textBeforeWidth;
                    }
                    else
                    {
                        selectionStartX += textBeforeWidth;
                    }

                    float selectionY = context.Padding.Top + line.Position.Y - context.Style.FontSize * 0.8f;
                    float selectionHeight = context.Style.FontSize;

                    // 绘制选择区域矩形
                    var selectionRect = new SKRect(
                        selectionStartX,
                        selectionY,
                        selectionStartX + selectionWidth,
                        selectionY + selectionHeight
                    );
                    canvas.DrawRect(selectionRect, selectionPaint);
                }

                currentPos = lineEnd;
            }
        }

    }
}

