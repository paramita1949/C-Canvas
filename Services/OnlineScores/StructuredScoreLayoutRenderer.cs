using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SkiaSharp;

namespace ImageColorChanger.Services.OnlineScores
{
    public enum StructuredScoreLayout { Paginated, Combined, LongPage }

    /// <summary>使用解析器的完整音乐系统作为分页单位，不扫描像素猜测切割位置。</summary>
    public static class StructuredScoreLayoutRenderer
    {
        public const int Width = 1920, PageHeight = 1440;
        private const float Scale = 1920f / 1800f, BottomMargin = 42;

        public static (int Height, float[] Bottoms) GetCutPreview(string source, StructuredScoreLayout layout, int verse, string path, CancellationToken ct)
        {
            using var score = ExtendedScoreNotationRenderer.Record(source, layout == StructuredScoreLayout.Combined ? null : verse, ct);
            using var bitmap = NewBitmap(checked((int)Math.Ceiling((score.Height + BottomMargin) * Scale)));
            using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White); canvas.Scale(Scale);
            DrawBand(canvas, score, 0, score.Height, 0); DrawVerseLabels(canvas, score, 0, score.HeaderHeight, score.HeaderHeight);
            Save(bitmap, path, ct, new List<string>());
            return (bitmap.Height, score.Bands.Select(b => b.Bottom * Scale).ToArray());
        }

        public static int[][] CreatePageLines(string source, StructuredScoreLayout layout, CancellationToken ct)
        {
            if (layout == StructuredScoreLayout.LongPage) throw new ScoreIntegrationException("长页不需要分页数据。");
            int count = layout == StructuredScoreLayout.Combined ? 1 : ExtendedScoreNotationRenderer.VerseCount(source, ct);
            var plan = new int[count][];
            for (int verse = 0; verse < count; verse++)
            {
                using var score = ExtendedScoreNotationRenderer.Record(source, layout == StructuredScoreLayout.Combined ? null : verse, ct);
                float capacity = PageHeight / Scale - score.HeaderHeight - BottomMargin;
                // 谱文显式分页优先，不再在标记之间额外拆页；高页由原图完整显示。
                plan[verse] = (score.PageBreaks.Count > 0 ? SplitAtBreaks(new[] { (Start: 0, End: score.Bands.Count) }, score.PageBreaks) :
                    Balance(score.Bands.Select(b => b.Bottom - b.Top).ToArray(), capacity)).Select(p => p.End - p.Start).ToArray();
            }
            return plan;
        }

        /// <summary>按实际合页切分计数，不生成位图；与渲染共用同一分页实现。</summary>
        internal static int GetCombinedPageCount(string source, int[] pageLines, bool ignoreSourcePageBreaks, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var score = ExtendedScoreNotationRenderer.Record(source, null, ct);
            return SelectPages(score, pageLines, ignoreSourcePageBreaks).Count;
        }

        private static IReadOnlyList<(int Start, int End)> SelectPages(ExtendedScoreNotationRenderer.RecordedScore score, int[] pageLines, bool ignoreSourcePageBreaks)
        {
            float capacity = PageHeight / Scale - score.HeaderHeight - BottomMargin;
            var heights = score.Bands.Select(b => b.Bottom - b.Top).ToArray();
            return pageLines == null ? (score.PageBreaks.Count > 0 ? SplitAtBreaks(new[] { (Start: 0, End: heights.Length) }, score.PageBreaks) : Balance(heights, capacity)) :
                ignoreSourcePageBreaks ? ReadPageLines(pageLines, heights, capacity) : SplitAtBreaks(ReadPageLines(pageLines, heights, capacity), score.PageBreaks);
        }

        public static IReadOnlyList<string> Render(string source, StructuredScoreLayout layout, string outputDirectory, CancellationToken ct, int[][] pageLines = null,
            SKColor? textColor = null, SKColor? backgroundColor = null, bool ignoreSourcePageBreaks = false)
        {
            if (!Enum.IsDefined(layout)) throw new ScoreIntegrationException("未知歌谱版式。");
            ct.ThrowIfCancellationRequested(); Directory.CreateDirectory(outputDirectory);
            int count = layout == StructuredScoreLayout.Combined ? 1 : ExtendedScoreNotationRenderer.VerseCount(source, ct);
            var recordings = new List<ExtendedScoreNotationRenderer.RecordedScore>();
            var output = new List<string>();
            try
            {
                for (int verse = 0; verse < count; verse++)
                {
                    ct.ThrowIfCancellationRequested();
                    recordings.Add(ExtendedScoreNotationRenderer.Record(source, layout == StructuredScoreLayout.Combined ? null : verse, ct, textColor: textColor));
                }
                if (layout == StructuredScoreLayout.LongPage)
                {
                    float height = recordings[0].HeaderHeight;
                    foreach (var score in recordings) height += score.Height - score.HeaderHeight;
                    height += (count - 1) * 40 + BottomMargin;
                    using var bitmap = NewBitmap(checked((int)Math.Ceiling(height * Scale)));
                    using var canvas = new SKCanvas(bitmap); canvas.Clear(backgroundColor ?? SKColors.White); canvas.Scale(Scale);
                    DrawBand(canvas, recordings[0], 0, recordings[0].HeaderHeight, 0);
                    float y = recordings[0].HeaderHeight;
                    foreach (var score in recordings)
                    {
                        DrawBand(canvas, score, score.HeaderHeight, score.Height, y);
                        DrawVerseLabels(canvas, score, 0, score.HeaderHeight, y, textColor);
                        y += score.Height - score.HeaderHeight + 40;
                    }
                    Save(bitmap, Path.Combine(outputDirectory, "page-0001.png"), ct, output);
                }
                else
                {
                    if (layout == StructuredScoreLayout.Paginated && pageLines?.Length == 1 && recordings.Count > 1)
                    {
                        // 数字编辑器的单行方案覆盖全曲，不强迫用户按歌词节分别填写。
                        var rows = recordings.SelectMany((score, verse) => score.Bands.Select((band, index) => (Score: score, Band: band, Verse: verse, Index: index))).ToArray();
                        var heights = rows.Select(row => row.Band.Bottom - row.Band.Top).ToArray();
                        var boundaries = new List<int>(); int start = 0;
                        foreach (var score in recordings) { boundaries.AddRange(score.PageBreaks.Select(i => i + start)); start += score.Bands.Count; }
                        var pages = ignoreSourcePageBreaks ? ReadPageLines(pageLines[0], heights, 0) : SplitAtBreaks(ReadPageLines(pageLines[0], heights, 0), boundaries);
                        foreach (var page in pages)
                        {
                            ct.ThrowIfCancellationRequested();
                            var header = rows[page.Start].Score;
                            float height = header.HeaderHeight + BottomMargin;
                            for (int i = page.Start; i < page.End; i++) height += heights[i];
                            using var bitmap = NewBitmap(Math.Max(PageHeight, checked((int)Math.Ceiling(height * Scale))));
                            using var canvas = new SKCanvas(bitmap); canvas.Clear(backgroundColor ?? SKColors.White); canvas.Scale(Scale);
                            DrawBand(canvas, header, 0, header.HeaderHeight, 0);
                            float y = header.HeaderHeight;
                            for (int i = page.Start; i < page.End; i++)
                            {
                                var row = rows[i]; DrawBand(canvas, row.Score, row.Band.Top, row.Band.Bottom, y);
                                if (i == page.Start || rows[i - 1].Verse != row.Verse)
                                    DrawVerseLabels(canvas, row.Score, row.Index, row.Band.Top, y, textColor,
                                        i == page.Start && pages.Count > 1 ? (output.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." : null);
                                y += heights[i];
                            }
                            Save(bitmap, Path.Combine(outputDirectory, $"page-{output.Count + 1:0000}.png"), ct, output);
                        }
                        return output;
                    }
                    if (pageLines != null && pageLines.Length != recordings.Count)
                        throw new ScoreIntegrationException("分页数据的歌词节数与谱文不符，请修改此文件的PageLines。");
                    for (int verse = 0; verse < recordings.Count; verse++)
                    {
                        var score = recordings[verse];
                        var heights = score.Bands.Select(b => b.Bottom - b.Top).ToArray();
                        var pages = SelectPages(score, pageLines?[verse], ignoreSourcePageBreaks);
                        foreach (var page in pages)
                        {
                            ct.ThrowIfCancellationRequested();
                            float contentHeight = score.HeaderHeight + BottomMargin;
                            for (int i = page.Start; i < page.End; i++) contentHeight += heights[i];
                            // 文件指定切分优先；较高页由原图完整显示，不裁断或强行再分页。
                            using var bitmap = NewBitmap(Math.Max(PageHeight, checked((int)Math.Ceiling(contentHeight * Scale))));
                            using var canvas = new SKCanvas(bitmap); canvas.Clear(backgroundColor ?? SKColors.White); canvas.Scale(Scale);
                            DrawBand(canvas, score, 0, score.HeaderHeight, 0);
                            float y = score.HeaderHeight;
                            for (int i = page.Start; i < page.End; i++)
                            {
                                var band = score.Bands[i]; DrawBand(canvas, score, band.Top, band.Bottom, y);
                                if (i == page.Start) DrawVerseLabels(canvas, score, i, band.Top, y, textColor,
                                    layout == StructuredScoreLayout.Paginated && (pages.Count > 1 || recordings.Count > 1) ?
                                        (output.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." : null);
                                y += band.Bottom - band.Top;
                            }
                            Save(bitmap, Path.Combine(outputDirectory, $"page-{output.Count + 1:0000}.png"), ct, output);
                        }
                    }
                }
                return output;
            }
            catch
            {
                foreach (string path in output) if (File.Exists(path)) File.Delete(path);
                throw;
            }
            finally { foreach (var score in recordings) score.Dispose(); }
        }

        private static IReadOnlyList<(int Start, int End)> ReadPageLines(int[] counts, float[] heights, float capacity)
        {
            if (counts == null || counts.Length == 0 || counts.Any(n => n <= 0) || counts.Sum(n => (long)n) != heights.Length)
                throw new ScoreIntegrationException("文件分页数据必须恰好覆盖全部完整谱行，不能漏行或重复；请修改PageLines/CombinedPageLines。");
            var pages = new List<(int, int)>(); int at = 0;
            foreach (int count in counts)
            {
                pages.Add((at, at + count)); at += count;
            }
            return pages;
        }

        // 显式*是硬边界；数字方案仍保留每个原切点，只拆跨越*的页，不漏行、不重复。
        private static IReadOnlyList<(int Start, int End)> SplitAtBreaks(IReadOnlyList<(int Start, int End)> pages, IEnumerable<int> breaks)
        {
            var result = new List<(int, int)>(); var points = breaks.Distinct().OrderBy(i => i).ToArray();
            foreach (var page in pages)
            {
                int start = page.Start;
                foreach (int point in points.Where(i => i > page.Start && i < page.End)) { result.Add((start, point)); start = point; }
                result.Add((start, page.End));
            }
            return result;
        }

        private static IReadOnlyList<(int Start, int End)> BalanceWithBreaks(float[] heights, float capacity, IEnumerable<int> breaks)
        {
            var result = new List<(int, int)>(); int start = 0;
            foreach (int end in breaks.Where(i => i > 0 && i < heights.Length).Append(heights.Length).Distinct().OrderBy(i => i))
            {
                result.AddRange(Balance(heights.Skip(start).Take(end - start).ToArray(), capacity).Select(p => (p.Start + start, p.End + start)));
                start = end;
            }
            return result;
        }

        private static SKBitmap NewBitmap(int height)
        {
            if (height <= 0) throw new ScoreIntegrationException("歌谱高度无效。");
            var bitmap = new SKBitmap(Width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            if (bitmap.GetPixels() == IntPtr.Zero) { bitmap.Dispose(); throw new ScoreIntegrationException("无法分配歌谱图像，请使用分页版。"); }
            return bitmap;
        }
        private static void DrawBand(SKCanvas canvas, ExtendedScoreNotationRenderer.RecordedScore score, float top, float bottom, float y)
        {
            canvas.Save(); canvas.ClipRect(new SKRect(0, y, 1800, y + bottom - top));
            canvas.Translate(0, y - top); canvas.DrawPicture(score.Picture); canvas.Restore();
        }
        private static void DrawVerseLabels(SKCanvas canvas, ExtendedScoreNotationRenderer.RecordedScore score, int band, float top, float y, SKColor? textColor = null, string fallbackLabel = null)
        {
            using var face = SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;
            using var font = new SKFont(face, ExtendedScoreNotationRenderer.ProjectionBodySize);
            using var ink = new SKPaint { Color = textColor ?? SKColors.Black, IsAntialias = true };
            // 一页仅在首个完整谱行标一次节号；后续页也能辨认本节，原始C行不改。
            var labels = score.VerseLabels.Where(l => l.Band == band).ToArray();
            bool fallback = fallbackLabel != null && !score.VerseLabels.Any(l => l.Text.Length > 0);
            foreach (var label in labels)
            {
                string text = label.Text;
                if (text.Length == 0 && fallback) { text = fallbackLabel; fallback = false; }
                if (text.Length == 0) continue;
                font.Size = label.Size; canvas.DrawText(text, label.Right - font.MeasureText(text) - label.Gap * font.Size, y + label.Y - top, font, ink);
            }
        }
        private static void Save(SKBitmap bitmap, string path, CancellationToken ct, List<string> output)
        {
            using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data == null) throw new ScoreIntegrationException("歌谱图片编码失败。");
            ct.ThrowIfCancellationRequested();
            bool created = false;
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew)) { created = true; data.SaveTo(stream); }
                ct.ThrowIfCancellationRequested(); output.Add(path);
            }
            catch { if (created) File.Delete(path); throw; }
        }

        // 先求最少页数，再在相同页数内平衡谱行数与占用高度。
        private static IReadOnlyList<(int Start, int End)> Balance(float[] heights, float capacity)
        {
            if (capacity <= 0 || heights.Any(h => h > capacity))
                return Enumerable.Range(0, heights.Length).Select(i => (i, i + 1)).ToArray();
            int n = heights.Length; var sum = new float[n + 1]; var minimum = Enumerable.Repeat(int.MaxValue, n + 1).ToArray(); minimum[0] = 0;
            for (int i = 0; i < n; i++) sum[i + 1] = sum[i] + heights[i];
            for (int end = 1; end <= n; end++)
                for (int start = end - 1; start >= 0 && sum[end] - sum[start] <= capacity + .01f; start--)
                    minimum[end] = Math.Min(minimum[end], minimum[start] + 1);
            int pages = minimum[n]; double targetCount = (double)n / pages, targetHeight = sum[n] / pages;
            // 能保持最少页数且每页放得下时，前面使用同一行数，余数只放末页。
            var uniform = new List<(int Start, int End)>(); double uniformCost = double.PositiveInfinity;
            foreach (int count in new[] { (int)Math.Floor(targetCount), (int)Math.Ceiling(targetCount) }.Distinct())
            {
                int tail = n - count * (pages - 1);
                if (count <= 0 || tail <= 0 || tail == 1 && n >= pages * 2) continue;
                var candidate = new List<(int Start, int End)>(); int start = 0; bool fits = true;
                for (int p = 0; p < pages; p++)
                {
                    int end = start + (p == pages - 1 ? tail : count);
                    if (sum[end] - sum[start] > capacity + .01f) { fits = false; break; }
                    candidate.Add((start, end)); start = end;
                }
                double cost = (pages - 1) * Math.Pow(count - targetCount, 2) + Math.Pow(tail - targetCount, 2);
                if (fits && cost < uniformCost) { uniform = candidate; uniformCost = cost; }
            }
            if (uniform.Count > 0) return uniform;
            var costs = new double[pages + 1, n + 1]; var previous = new int[pages + 1, n + 1];
            for (int p = 0; p <= pages; p++) for (int end = 0; end <= n; end++) costs[p, end] = double.PositiveInfinity;
            costs[0, 0] = 0;
            for (int p = 1; p <= pages; p++)
                for (int end = p; end <= n; end++)
                    for (int start = end - 1; start >= p - 1 && sum[end] - sum[start] <= capacity + .01f; start--)
                    {
                        int count = end - start;
                        double cost = costs[p - 1, start] + Math.Pow(count - targetCount, 2) * 100 + Math.Pow((sum[end] - sum[start] - targetHeight) / capacity, 2);
                        if (count == 1 && n >= pages * 2) cost += 10000;
                        if (cost < costs[p, end]) { costs[p, end] = cost; previous[p, end] = start; }
                    }
            var result = new List<(int, int)>(); int at = n;
            for (int p = pages; p > 0; p--) { int start = previous[p, at]; result.Add((start, at)); at = start; }
            result.Reverse(); return result;
        }
    }
}
