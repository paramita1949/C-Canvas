using System;
using System.Collections.Generic;
using SkiaSharp;

namespace ImageColorChanger.Services.LiveCaption.Rendering
{
    internal readonly struct LiveCaptionNdiRenderOptions
    {
        public LiveCaptionNdiRenderOptions(int width, int height, string fontFamily, float fontSize,
            float padding, float letterSpacing, float lineGap, SKColor textColor, SKColor latestColor,
            string alignment = "center", int maxLines = 2)
        {
            Width = Math.Clamp(width, 16, 8192);
            Height = Math.Clamp(height, 16, 8192);
            FontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Microsoft YaHei UI" : fontFamily.Trim();
            FontSize = Math.Clamp(float.IsFinite(fontSize) ? fontSize : 52, 20, 112);
            Padding = Math.Clamp(float.IsFinite(padding) ? padding : 56, 0, 160);
            LetterSpacing = Math.Clamp(float.IsFinite(letterSpacing) ? letterSpacing : 0, 0, 10);
            LineGap = Math.Clamp(float.IsFinite(lineGap) ? lineGap : 30, 0, 60);
            TextColor = textColor;
            LatestColor = latestColor;
            Alignment = alignment?.ToLowerInvariant() ?? "center";
            MaxLines = Math.Clamp(maxLines, 1, 2);
        }

        public int Width { get; }
        public int Height { get; }
        public string FontFamily { get; }
        public float FontSize { get; }
        public float Padding { get; }
        public float LetterSpacing { get; }
        public float LineGap { get; }
        public SKColor TextColor { get; }
        public SKColor LatestColor { get; }
        public string Alignment { get; }
        public int MaxLines { get; }
    }

    internal readonly struct LiveCaptionNdiGlyph
    {
        public LiveCaptionNdiGlyph(CaptionTextElement element, float advance, SKRect bounds, float x)
        {
            Element = element;
            Advance = advance;
            Bounds = bounds;
            X = x;
        }
        public CaptionTextElement Element { get; }
        public float Advance { get; }
        public SKRect Bounds { get; }
        public float X { get; }
    }

    internal sealed class LiveCaptionNdiLine
    {
        public LiveCaptionNdiLine(LiveCaptionNdiGlyph[] glyphs, float leftBearing, float width,
            float x = 0, float baseline = 0, float scaleX = 1, SKRect bounds = default)
        {
            Glyphs = Array.AsReadOnly(glyphs);
            LeftBearing = leftBearing;
            Width = width;
            X = x;
            Baseline = baseline;
            ScaleX = scaleX;
            Bounds = bounds;
        }
        public IReadOnlyList<LiveCaptionNdiGlyph> Glyphs { get; }
        public float LeftBearing { get; }
        public float Width { get; }
        public float X { get; }
        public float Baseline { get; }
        public float ScaleX { get; }
        public SKRect Bounds { get; }
    }

    internal sealed class LiveCaptionNdiLayout
    {
        public LiveCaptionNdiLayout(float fontSize, LiveCaptionNdiLine[] lines)
        {
            FontSize = fontSize;
            Lines = Array.AsReadOnly(lines);
        }
        public float FontSize { get; }
        public IReadOnlyList<LiveCaptionNdiLine> Lines { get; }
    }

    /// <summary>纯 Skia 排版和绘制。输入不含控件/配置/传输对象，返回的 Bitmap 归调用方释放。</summary>
    internal sealed class LiveCaptionNdiRenderer
    {
        private const float ReadableMinimumFontSize = 18;

        public SKBitmap Render(LiveCaptionRenderFrame frame, LiveCaptionNdiRenderOptions options)
        {
            var bitmap = new SKBitmap(options.Width, options.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            try
            {
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(LiveCaptionNdiFrameDefaults.TransparentBackground);
                using var typeface = ResolveTypeface(options.FontFamily);
                using var font = new SKFont(typeface, options.FontSize);
                LiveCaptionNdiLayout layout = BuildLayout(frame, options, font);
                font.Size = layout.FontSize;
                using var basePaint = new SKPaint { IsAntialias = true, Color = options.TextColor };
                using var latestPaint = new SKPaint { IsAntialias = true, Color = options.LatestColor };
                int highlight = CaptionTextElements.FloorBoundary(frame.Display, frame.HighlightStart);
                foreach (LiveCaptionNdiLine line in layout.Lines)
                {
                    canvas.Save();
                    canvas.Translate(line.X, line.Baseline);
                    canvas.Scale(line.ScaleX, 1);
                    foreach (LiveCaptionNdiGlyph glyph in line.Glyphs)
                    {
                        SKPaint paint = glyph.Element.SourceStart >= highlight ? latestPaint : basePaint;
                        canvas.DrawText(glyph.Element.Text, glyph.X, 0, SKTextAlign.Left, font, paint);
                    }
                    canvas.Restore();
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        public LiveCaptionNdiLayout CreateLayout(LiveCaptionRenderFrame frame, LiveCaptionNdiRenderOptions options)
        {
            using var typeface = ResolveTypeface(options.FontFamily);
            using var font = new SKFont(typeface, options.FontSize);
            return BuildLayout(frame, options, font);
        }

        private static SKTypeface ResolveTypeface(string family) =>
            SKTypeface.FromFamilyName(family) ?? SKTypeface.FromFamilyName("Microsoft YaHei UI");

        private static LiveCaptionNdiLayout BuildLayout(LiveCaptionRenderFrame frame,
            LiveCaptionNdiRenderOptions options, SKFont font)
        {
            if (string.IsNullOrWhiteSpace(frame.Display))
                return new LiveCaptionNdiLayout(options.FontSize, Array.Empty<LiveCaptionNdiLine>());

            var rawLines = new List<CaptionTextLine>(CaptionTextElements.ReadLines(frame.Display));
            if (rawLines.Count > options.MaxLines)
                rawLines.RemoveRange(0, rawLines.Count - options.MaxLines);

            // 小画面不能让设置的内边距吃掉正文，另留 1px 供字形边缘抗锯齿。
            float padding = Math.Min(options.Padding, Math.Min(options.Width, options.Height) * 0.1f) + 1;
            float availableWidth = Math.Max(1, options.Width - padding * 2);
            float availableHeight = Math.Max(1, options.Height - padding * 2);
            float gap = options.LineGap;
            List<LiveCaptionNdiLine> measured = MeasureLines(rawLines, font, options.LetterSpacing);

            if (!Fits(measured, font, availableWidth, availableHeight, gap))
            {
                font.Size = ReadableMinimumFontSize;
                List<LiveCaptionNdiLine> minimum = MeasureLines(rawLines, font, options.LetterSpacing);
                if (Fits(minimum, font, availableWidth, availableHeight, gap))
                {
                    // 采用实际宽高二分，字距不跟着字号缩放，因此不能只用一次比例估算。
                    float low = ReadableMinimumFontSize, high = options.FontSize;
                    for (int i = 0; i < 12; i++)
                    {
                        font.Size = (low + high) * 0.5f;
                        if (Fits(MeasureLines(rawLines, font, options.LetterSpacing), font,
                            availableWidth, availableHeight, gap)) low = font.Size;
                        else high = font.Size;
                    }
                    font.Size = low;
                }
                measured = MeasureLines(rawLines, font, options.LetterSpacing);
            }

            GetVerticalMetrics(measured, font, out float top, out float bottom);
            float rowHeight = bottom - top;
            // 极小画面一行也容不下时，降低字号保留完整元素；普通画面保持可读下限。
            for (int i = 0; rowHeight > availableHeight && font.Size > 1 && i < 12; i++)
            {
                font.Size = Math.Max(1, font.Size * availableHeight / rowHeight * 0.98f);
                measured = MeasureLines(rawLines, font, options.LetterSpacing);
                GetVerticalMetrics(measured, font, out top, out bottom);
                rowHeight = bottom - top;
            }

            List<LiveCaptionNdiLine> wrapped = WrapToPixelWidth(measured, availableWidth, options.LetterSpacing);
            int desiredRows = Math.Min(options.MaxLines, wrapped.Count);
            if (desiredRows > 1)
                gap = Math.Min(gap, Math.Max(0, (availableHeight - rowHeight * desiredRows) / (desiredRows - 1)));
            int capacity = Math.Max(1, (int)Math.Floor((availableHeight + gap) / (rowHeight + gap)));
            int visibleCount = Math.Min(wrapped.Count, Math.Min(options.MaxLines, capacity));
            int start = wrapped.Count - visibleCount;
            float blockHeight = rowHeight * visibleCount + gap * Math.Max(0, visibleCount - 1);
            float blockTop = options.Height - padding - blockHeight;
            var positioned = new LiveCaptionNdiLine[visibleCount];
            for (int i = 0; i < visibleCount; i++)
            {
                LiveCaptionNdiLine line = wrapped[start + i];
                // 单个组合元素可能比画面还宽；只压缩这个完整元素所在行，不从元素中间截断。
                float scaleX = line.Width > availableWidth ? availableWidth / line.Width : 1;
                float visibleWidth = line.Width * scaleX;
                float left = options.Alignment switch
                {
                    "left" => padding,
                    "right" => options.Width - padding - visibleWidth,
                    _ => padding + (availableWidth - visibleWidth) * 0.5f
                };
                float baseline = blockTop - top + i * (rowHeight + gap);
                positioned[i] = new LiveCaptionNdiLine(ToArray(line.Glyphs), line.LeftBearing, line.Width,
                    left - line.LeftBearing * scaleX, baseline, scaleX,
                    new SKRect(left, baseline + top, left + visibleWidth, baseline + bottom));
            }
            return new LiveCaptionNdiLayout(font.Size, positioned);
        }

        private static List<LiveCaptionNdiLine> MeasureLines(List<CaptionTextLine> lines, SKFont font, float spacing)
        {
            var result = new List<LiveCaptionNdiLine>(lines.Count);
            foreach (CaptionTextLine line in lines)
            {
                var glyphs = new List<LiveCaptionNdiGlyph>();
                foreach (CaptionTextElement element in CaptionTextElements.Enumerate(line.Text, line.SourceStart))
                {
                    float advance = Math.Max(0, font.MeasureText(element.Text, out SKRect bounds));
                    glyphs.Add(new LiveCaptionNdiGlyph(element, advance, bounds, 0));
                }
                result.Add(PositionGlyphs(glyphs, spacing));
            }
            return result;
        }

        private static LiveCaptionNdiLine PositionGlyphs(IReadOnlyList<LiveCaptionNdiGlyph> glyphs, float spacing)
        {
            float cursor = 0, left = 0, right = 0;
            var positioned = new LiveCaptionNdiGlyph[glyphs.Count];
            for (int i = 0; i < glyphs.Count; i++)
            {
                LiveCaptionNdiGlyph glyph = glyphs[i];
                if (i > 0) cursor += spacing;
                positioned[i] = new LiveCaptionNdiGlyph(glyph.Element, glyph.Advance, glyph.Bounds, cursor);
                left = Math.Min(left, cursor + glyph.Bounds.Left);
                right = Math.Max(right, cursor + glyph.Bounds.Right);
                cursor += glyph.Advance;
                right = Math.Max(right, cursor);
            }
            return new LiveCaptionNdiLine(positioned, left, right - left);
        }

        private static bool Fits(List<LiveCaptionNdiLine> lines, SKFont font,
            float width, float height, float gap)
        {
            foreach (LiveCaptionNdiLine line in lines) if (line.Width > width) return false;
            GetVerticalMetrics(lines, font, out float top, out float bottom);
            return (bottom - top) * lines.Count + gap * Math.Max(0, lines.Count - 1) <= height;
        }

        private static void GetVerticalMetrics(List<LiveCaptionNdiLine> lines, SKFont font,
            out float top, out float bottom)
        {
            SKFontMetrics metrics = font.Metrics;
            top = metrics.Ascent;
            bottom = metrics.Descent;
            foreach (LiveCaptionNdiLine line in lines)
                foreach (LiveCaptionNdiGlyph glyph in line.Glyphs)
                {
                    top = Math.Min(top, glyph.Bounds.Top);
                    bottom = Math.Max(bottom, glyph.Bounds.Bottom);
                }
            bottom = Math.Max(top + 1, bottom);
        }

        private static List<LiveCaptionNdiLine> WrapToPixelWidth(List<LiveCaptionNdiLine> source,
            float availableWidth, float spacing)
        {
            var result = new List<LiveCaptionNdiLine>();
            foreach (LiveCaptionNdiLine line in source)
            {
                if (line.Width <= availableWidth)
                {
                    result.Add(line);
                    continue;
                }
                var current = new List<LiveCaptionNdiGlyph>();
                float cursor = 0, left = 0, right = 0;
                foreach (LiveCaptionNdiGlyph glyph in line.Glyphs)
                {
                    float x = cursor + (current.Count > 0 ? spacing : 0);
                    float nextLeft = Math.Min(left, x + glyph.Bounds.Left);
                    float nextRight = Math.Max(right, x + Math.Max(glyph.Advance, glyph.Bounds.Right));
                    if (current.Count > 0 && nextRight - nextLeft > availableWidth)
                    {
                        result.Add(PositionGlyphs(current, spacing));
                        current.Clear();
                        x = 0;
                        nextLeft = Math.Min(0, glyph.Bounds.Left);
                        nextRight = Math.Max(glyph.Advance, glyph.Bounds.Right);
                    }
                    current.Add(glyph);
                    cursor = x + glyph.Advance;
                    left = nextLeft;
                    right = nextRight;
                }
                result.Add(PositionGlyphs(current, spacing));
            }
            return result;
        }

        private static LiveCaptionNdiGlyph[] ToArray(IReadOnlyList<LiveCaptionNdiGlyph> source)
        {
            var result = new LiveCaptionNdiGlyph[source.Count];
            for (int i = 0; i < source.Count; i++) result[i] = source[i];
            return result;
        }
    }
}
