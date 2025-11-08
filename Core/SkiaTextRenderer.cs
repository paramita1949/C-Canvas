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
            
#if DEBUG
            var sw = System.Diagnostics.Stopwatch.StartNew();
#endif

            // 1. 检查缓存
            var cacheKey = context.GetCacheKey();
            if (_cache.TryGetValue(cacheKey, out SKBitmap cachedBitmap))
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ [SkiaTextRenderer] 缓存命中: {cacheKey.Substring(0, Math.Min(50, cacheKey.Length))}...");
#endif
                return cachedBitmap;
            }
            
            // 2. 创建Bitmap和Canvas
            int width = (int)Math.Ceiling(context.Size.Width);
            int height = (int)Math.Ceiling(context.Size.Height);
            
            if (width <= 0 || height <= 0)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [SkiaTextRenderer] 无效尺寸: {width}x{height}");
#endif
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
            
            // 6. 创建Paint
            using var paint = CreatePaint(context.Style);
            paint.TextAlign = context.Alignment;
            
            // 7. 逐行绘制
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
            }
            
            // 8. 缓存结果
            _cache.Set(cacheKey, bitmap, TimeSpan.FromMinutes(5));
            
#if DEBUG
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"🎨 [SkiaTextRenderer] 渲染完成: {sw.ElapsedMilliseconds}ms, 尺寸: {width}x{height}, 行数: {layout.Lines.Count}");
#endif
            
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
            
            foreach (var verse in context.Verses)
            {
                float verseStartY = currentY;
                
                if (verse.IsTitle)
                {
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
                    
                    currentY += titleHeight + context.VerseSpacing;
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
                    
                    currentY += verseHeight + context.VerseSpacing / 2;
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
                    // 渲染经文行
                    var verseColor = layout.Verse.IsHighlighted 
                        ? context.VerseStyle.TextColor 
                        : context.VerseStyle.TextColor;
                    
                    // 节号
                    using var numberPaint = CreatePaint(context.VerseNumberStyle);
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
            
#if DEBUG
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"📖 [SkiaTextRenderer-Bible] 完成: {context.Verses.Count}节, 尺寸: {width}×{actualHeight}, {sw.ElapsedMilliseconds}ms");
#endif
            
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
            
#if DEBUG
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"🎵 [SkiaTextRenderer-Lyrics] 完成: {lines.Count}行, 尺寸: {width}×{actualHeight}, {sw.ElapsedMilliseconds}ms");
#endif
            
            return bitmap;
        }
        
        /// <summary>
        /// 创建Paint对象
        /// </summary>
        private SKPaint CreatePaint(TextStyle style)
        {
            // ✅ 使用SkiaFontService加载字体（支持自定义字体文件）
            var typeface = SkiaFontService.Instance.GetTypeface(style.FontFamily, style.IsBold, style.IsItalic);
            
            return new SKPaint
            {
                Typeface = typeface,
                TextSize = style.FontSize,
                Color = style.TextColor,
                IsAntialias = true,
                SubpixelText = true
            };
        }
        
    }
}

