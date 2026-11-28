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
        internal const int AutomaticRowsPerPage = 5;
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
                using var score = ExtendedScoreNotationRenderer.Record(source, layout == StructuredScoreLayout.Combined ? null : verse, ct, measureOnly: true);
                plan[verse] = SelectAutomaticPages(score).Select(p => p.End - p.Start).ToArray();
            }
            return plan;
        }

        /// <summary>按实际合页切分计数，不生成位图；与渲染共用同一分页实现。</summary>
        internal static int GetCombinedPageCount(string source, int[] pageLines, bool ignoreSourcePageBreaks, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var score = ExtendedScoreNotationRenderer.Record(source, null, ct, measureOnly: true);
            return SelectPages(score, pageLines, ignoreSourcePageBreaks).Count;
        }

        private static IReadOnlyList<(int Start, int End)> SelectPages(ExtendedScoreNotationRenderer.RecordedScore score, int[] pageLines, bool ignoreSourcePageBreaks)
        {
            if (pageLines != null)
            {
                var pages = ReadPageLines(pageLines, score.Bands.Count); // 无效方案不能被自动优化掩盖。
                if (ignoreSourcePageBreaks) return pages; // 人工数字完整优先，不额外切断。
            }
            return SelectAutomaticPages(score);
        }

        private static IReadOnlyList<(int Start, int End)> SelectAutomaticPages(ExtendedScoreNotationRenderer.RecordedScore score)
        {
            int count = score.Bands.Count;
            if (count == 0) return Array.Empty<(int Start, int End)>();
            if (count <= AutomaticRowsPerPage) return new[] { (Start: 0, End: count) };
            var forbidden = new bool[count + 1];
            var required = new bool[count + 1];
            foreach (var chorus in score.ChorusRanges)
            {
                if (chorus.End - chorus.Start <= AutomaticRowsPerPage)
                    for (int i = chorus.Start + 1; i < chorus.End; i++) forbidden[i] = true;
                else
                {
                    // 超长副歌只在自身内均分，不把它的开头强塞进主歌页。
                    required[chorus.Start] = required[chorus.End] = true;
                }
            }
            var requiredPrefix = new int[count + 1];
            for (int i = 1; i <= count; i++) requiredPrefix[i] = requiredPrefix[i - 1] + (required[i] ? 1 : 0);
            bool Allowed(int start, int end) => !forbidden[end] && requiredPrefix[end - 1] == requiredPrefix[start];
            var minimum = Enumerable.Repeat(int.MaxValue, count + 1).ToArray(); minimum[0] = 0;
            for (int end = 1; end <= count; end++)
                for (int start = Math.Max(0, end - AutomaticRowsPerPage); start < end; start++)
                    if (Allowed(start, end) && minimum[start] != int.MaxValue)
                        minimum[end] = Math.Min(minimum[end], minimum[start] + 1);
            int pageCount = minimum[count];
            // 总行数 -> 能完整容纳的最少页数 -> 前页同量、末页收余。
            // 单行尾页也允许；不能为了避短尾页把少行页移到前面。
            int frontRows = (int)Math.Ceiling((double)count / pageCount);
            int tailRows = count - frontRows * (pageCount - 1);
            var uniform = new List<(int Start, int End)>();
            if (tailRows > 0 && tailRows <= frontRows)
            {
                int start = 0;
                for (int page = 0; page < pageCount; page++)
                {
                    int end = start + (page == pageCount - 1 ? tailRows : frontRows);
                    if (!Allowed(start, end)) { uniform.Clear(); break; }
                    uniform.Add((start, end)); start = end;
                }
                if (uniform.Count == pageCount) return uniform;
            }
            // 仅完整副歌等语义边界使统一方案不可行时，在相同最少页数内回退。
            double target = (double)count / pageCount;
            var costs = Enumerable.Repeat(double.PositiveInfinity, count + 1).ToArray(); costs[0] = 0;
            var previous = new int[count + 1];
            var references = score.PageBreaks.ToHashSet();
            for (int end = 1; end <= count; end++)
                for (int start = Math.Max(0, end - AutomaticRowsPerPage); start < end; start++)
                {
                    if (!Allowed(start, end) || minimum[start] == int.MaxValue || minimum[end] != minimum[start] + 1) continue;
                    int rows = end - start;
                    double cost = costs[start] + Math.Pow(rows - target, 2) * 100
                        - (references.Contains(end) ? 1 : 0);
                    // 同等均衡时前页稍满；原谱切点只是低权重参考，不能拆开完整副歌。
                    if (cost < costs[end] - .001 || Math.Abs(cost - costs[end]) <= .001 && rows < end - previous[end])
                    { costs[end] = cost; previous[end] = start; }
                }
            var result = new List<(int Start, int End)>();
            for (int end = count; end > 0;) { int start = previous[end]; result.Add((start, end)); end = start; }
            result.Reverse(); return result;
        }

        public static IReadOnlyList<string> Render(string source, StructuredScoreLayout layout, string outputDirectory, CancellationToken ct, int[][] pageLines = null,
            SKColor? textColor = null, SKColor? backgroundColor = null, bool ignoreSourcePageBreaks = false)
            => RenderCore(source, layout, outputDirectory, ct, pageLines, textColor, backgroundColor, ignoreSourcePageBreaks, null);

        internal static IReadOnlyList<string> RenderProgressively(string source, StructuredScoreLayout layout, string outputDirectory, CancellationToken ct,
            int[][] pageLines, SKColor textColor, SKColor backgroundColor, bool ignoreSourcePageBreaks, Action<IReadOnlyList<string>, int> pageReady,
            Action<IReadOnlyList<string>, int, SKBitmap> firstBitmapReady = null)
            => RenderCore(source, layout, outputDirectory, ct, pageLines, textColor, backgroundColor, ignoreSourcePageBreaks, pageReady, firstBitmapReady);

        private static IReadOnlyList<string> RenderCore(string source, StructuredScoreLayout layout, string outputDirectory, CancellationToken ct, int[][] pageLines,
            SKColor? textColor, SKColor? backgroundColor, bool ignoreSourcePageBreaks, Action<IReadOnlyList<string>, int> pageReady,
            Action<IReadOnlyList<string>, int, SKBitmap> firstBitmapReady = null)
        {
            if (!Enum.IsDefined(layout)) throw new ScoreIntegrationException("未知歌谱版式。");
            ct.ThrowIfCancellationRequested(); Directory.CreateDirectory(outputDirectory);
            int count = layout == StructuredScoreLayout.Combined ? 1 : ExtendedScoreNotationRenderer.VerseCount(source, ct);
            // 普通逐节分页只先量全部节的边界；后续节到实际输出时才绘制，不阻塞首页。
            // 跨节手动分页和长页需要联合绘制，仍保留完整记录路径。
            bool lazyVerses = pageReady != null && layout == StructuredScoreLayout.Paginated && count > 1 && (pageLines == null || pageLines.Length == count);
            var recordings = new List<ExtendedScoreNotationRenderer.RecordedScore>();
            var output = new List<string>();
            IReadOnlyList<string> CanceledOutput()
            {
                foreach (string path in output) if (File.Exists(path)) File.Delete(path);
                return Array.Empty<string>();
            }
            bool StopRequested()
            {
                if (!ct.IsCancellationRequested) return false;
                // 导出API仍保留标准取消异常；交互式后台生成用空结果表示停止，
                // 不在已经交付首帧之后为一次正常切歌抛出保存异常。
                if (pageReady == null) ct.ThrowIfCancellationRequested();
                return true;
            }
            try
            {
                for (int verse = 0; verse < count; verse++)
                {
                    if (StopRequested()) return CanceledOutput();
                    recordings.Add(ExtendedScoreNotationRenderer.Record(source, layout == StructuredScoreLayout.Combined ? null : verse, ct, textColor: textColor, measureOnly: lazyVerses));
                }
                if (layout != StructuredScoreLayout.LongPage && pageLines != null && !ignoreSourcePageBreaks)
                {
                    // 老自动数字可覆盖全曲或分节；先校验覆盖，再按统一段落策略重算。
                    if (pageLines.Length == 1 && recordings.Count > 1)
                        ReadPageLines(pageLines[0], recordings.Sum(s => s.Bands.Count));
                    else
                    {
                        if (pageLines.Length != recordings.Count) throw new ScoreIntegrationException("分页数据的歌词节数与谱文不符。");
                        for (int i = 0; i < recordings.Count; i++)
                            ReadPageLines(pageLines[i], recordings[i].Bands.Count);
                    }
                    pageLines = recordings.Select(s => SelectAutomaticPages(s).Select(p => p.End - p.Start).ToArray()).ToArray();
                }
                IReadOnlyList<string> expected = null;
                if (pageReady != null || firstBitmapReady != null)
                {
                    int pages;
                    if (layout == StructuredScoreLayout.LongPage) pages = 1;
                    else if (layout == StructuredScoreLayout.Paginated && pageLines?.Length == 1 && recordings.Count > 1)
                    {
                        var cuts = ReadPageLines(pageLines[0], recordings.Sum(s => s.Bands.Count));
                        pages = cuts.Count; // 只有人工全曲方案会进入此分支，不再按原谱*拆开。
                    }
                    else
                    {
                        if (pageLines != null && pageLines.Length != recordings.Count)
                            throw new ScoreIntegrationException("分页数据的歌词节数与谱文不符，请修改此文件的PageLines。");
                        pages = recordings.Select((s, i) => SelectPages(s, pageLines?[i], ignoreSourcePageBreaks).Count).Sum();
                    }
                    expected = Enumerable.Range(1, pages).Select(n => Path.Combine(outputDirectory, $"page-{n:0000}.png")).ToArray();
                }
                bool SavePage(SKBitmap bitmap, string path)
                {
                    if (StopRequested()) return false;
                    if (output.Count == 0) firstBitmapReady?.Invoke(expected, 0, bitmap);
                    if (!Save(bitmap, path, ct, output)) { StopRequested(); return false; }
                    if (StopRequested()) return false;
                    pageReady?.Invoke(expected, output.Count - 1);
                    return !StopRequested();
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
                    if (!SavePage(bitmap, Path.Combine(outputDirectory, "page-0001.png"))) return CanceledOutput();
                }
                else
                {
                    if (layout == StructuredScoreLayout.Paginated && pageLines?.Length == 1 && recordings.Count > 1)
                    {
                        // 数字编辑器的单行方案覆盖全曲，不强迫用户按歌词节分别填写。
                        var rows = recordings.SelectMany((score, verse) => score.Bands.Select((band, index) => (Score: score, Band: band, Verse: verse, Index: index))).ToArray();
                        var heights = rows.Select(row => row.Band.Bottom - row.Band.Top).ToArray();
                        var pages = ReadPageLines(pageLines[0], heights.Length);
                        foreach (var page in pages)
                        {
                            if (StopRequested()) return CanceledOutput();
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
                            if (!SavePage(bitmap, Path.Combine(outputDirectory, $"page-{output.Count + 1:0000}.png"))) return CanceledOutput();
                        }
                        return output;
                    }
                    if (pageLines != null && pageLines.Length != recordings.Count)
                        throw new ScoreIntegrationException("分页数据的歌词节数与谱文不符，请修改此文件的PageLines。");
                    for (int verse = 0; verse < recordings.Count; verse++)
                    {
                        if (StopRequested()) return CanceledOutput();
                        if (lazyVerses)
                        {
                            var measured = recordings[verse];
                            recordings[verse] = ExtendedScoreNotationRenderer.Record(source, verse, ct, textColor: textColor);
                            measured.Dispose();
                        }
                        var score = recordings[verse];
                        var heights = score.Bands.Select(b => b.Bottom - b.Top).ToArray();
                        var pages = SelectPages(score, pageLines?[verse], ignoreSourcePageBreaks);
                        foreach (var page in pages)
                        {
                            if (StopRequested()) return CanceledOutput();
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
                            if (!SavePage(bitmap, Path.Combine(outputDirectory, $"page-{output.Count + 1:0000}.png"))) return CanceledOutput();
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

        private static IReadOnlyList<(int Start, int End)> ReadPageLines(int[] counts, int totalLines)
        {
            if (counts == null || counts.Length == 0 || counts.Any(n => n <= 0) || counts.Sum(n => (long)n) != totalLines)
                throw new ScoreIntegrationException("文件分页数据必须恰好覆盖全部完整谱行，不能漏行或重复；请修改PageLines/CombinedPageLines。");
            var pages = new List<(int, int)>(); int at = 0;
            foreach (int count in counts)
            {
                pages.Add((at, at + count)); at += count;
            }
            return pages;
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
        private static bool Save(SKBitmap bitmap, string path, CancellationToken ct, List<string> output)
        {
            if (ct.IsCancellationRequested) return false;
            // 谱面缓存仍是无损PNG；低压缩级别避免交互路径花时间反复尝试每行滤波。
            using var pixels = bitmap.PeekPixels();
            using var data = pixels.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.None, 1));
            if (data == null) throw new ScoreIntegrationException("歌谱图片编码失败。");
            if (ct.IsCancellationRequested) return false;
            bool created = false;
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew)) { created = true; data.SaveTo(stream); }
                if (ct.IsCancellationRequested) { File.Delete(path); return false; }
                output.Add(path);
                return true;
            }
            catch { if (created) File.Delete(path); throw; }
        }

    }
}
