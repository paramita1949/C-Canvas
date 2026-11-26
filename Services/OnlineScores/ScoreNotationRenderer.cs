using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SkiaSharp;

namespace ImageColorChanger.Services.OnlineScores
{
    // 独立的单声部简谱排版：系统字体 + 几何符号，不依赖站点字体、SVG 或网页引擎。
    // 未实现的音乐语法必须拒绝，交给原始图片路径，不能把源文本或错误谱面当成歌谱。
    public static class ScoreNotationRenderer
    {
        public const int MaxSourceBytes = 200 * 1024;
        private const int Width = 1800;
        private const float Margin = 80, NumberSize = 44, LyricSize = 33;
        private static readonly Regex NotePattern = new(
            @"\G(?<open>\(*)(?<symbol>:\|:|:\||\|:|\|\|\||\|\||\||[0-7\-])(?<suffix>[dg./^~)#b♯♭♮\s]*)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private sealed class Note
        {
            public string Symbol, Accidental = "";
            public int Octave, Dots, Beams;
            public char BeamTag;
            public bool NonBreakingBar;
            public float X, Y;
            public int VisualRow;
            public bool Bar => Symbol.Contains('|');
            public bool Syllable => Symbol.Length == 1 && Symbol[0] >= '1' && Symbol[0] <= '7';
            public double Duration => Bar ? 0 : (2 - Math.Pow(0.5, Dots)) * Math.Pow(0.5, Beams);
        }
        private sealed class Lyric { public string Text = "", Punctuation = ""; }
        private sealed class Verse { public string Label = ""; public List<Lyric> Cells = new(); }
        private sealed class Row { public List<Note> Notes = new(); public List<Verse> Verses = new(); }
        private sealed record Slur(Note Start, Note End, int Depth);
        private sealed class Document
        {
            public List<string> Titles = new(), Credits = new(), Footer = new();
            public List<(string Position, string Text)> Remarks = new();
            public string Key = "", Beat = "", Tempo = "";
            public int BeatType = 4, Beats = 4;
            public List<Row> Rows = new();
            public List<Slur> Slurs = new();
        }
        private sealed class LayoutRow
        {
            public Row Source;
            public List<Note> Notes;
            public float Baseline, LyricsY;
        }

        public static bool CanRender(string source, out string reason)
        {
            if (TryParse(source, CancellationToken.None, out reason) != null) return true;
            return ExtendedScoreNotationRenderer.CanRender(source, out reason);
        }

        public static void RenderToPng(string source, string outputPath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Document document = TryParse(source, ct, out _);
            if (document == null)
            {
                try { ExtendedScoreNotationRenderer.RenderToPng(source, outputPath, ct); }
                catch (OverflowException) { throw new ScoreIntegrationException("结构化歌谱时值或尺寸超出安全范围；将尝试原图。"); }
                return;
            }
            using var textFace = SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;
            using var numberFace = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
            using var numbers = new SKFont(numberFace, NumberSize);
            using var lyrics = new SKFont(textFace, LyricSize);
            using var text = new SKFont(textFace, 34);
            using var title = new SKFont(textFace, 68);
            using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var line = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.6f };
            var rows = Layout(document, lyrics, ct, out int height);
            if ((long)Width * height > 40_000_000)
                throw Unsupported("谱面尺寸过大");
            using var bitmap = new SKBitmap(Width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            float headingY = 105;
            foreach (var heading in document.Titles)
            {
                Center(canvas, heading, Width / 2f, headingY, title, ink, Width - Margin * 2);
                headingY += 82;
            }
            foreach (var credit in document.Credits)
            {
                Center(canvas, credit, Width / 2f, headingY, text, ink, Width - Margin * 2);
                headingY += 45;
            }
            canvas.DrawText(document.Key, Margin, headingY + 14, text, ink);
            canvas.DrawText(document.Beat, Margin + 170, headingY + 14, text, ink);
            if (document.Tempo.Length > 0) Center(canvas, document.Tempo, Width / 2f, headingY + 14, text, ink, 450);
            float remarkY = headingY + 64;
            foreach (var remark in document.Remarks.Where(r => r.Position.StartsWith("T") || r.Position.StartsWith("X")))
            {
                float x = remark.Position.EndsWith("R") ? Width - Margin - text.MeasureText(remark.Text) : Margin;
                canvas.DrawText(remark.Text, x, remarkY, text, ink);
                remarkY += 42;
            }
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var note in row.Notes) DrawNote(canvas, note, numbers, text, ink, line);
                DrawBeams(canvas, row.Notes, document, line);
                int verseIndex = 0;
                foreach (var verse in row.Source.Verses)
                {
                    float y = row.LyricsY + verseIndex++ * 47;
                    if (verse.Label.Length > 0) canvas.DrawText(verse.Label, 20, y, lyrics, ink);
                    var syllables = row.Source.Notes.Where(n => n.Syllable).ToList();
                    foreach (var note in row.Notes.Where(n => n.Syllable))
                    {
                        int index = syllables.IndexOf(note);
                        if (index >= verse.Cells.Count) continue;
                        var cell = verse.Cells[index];
                        if (cell.Text == "" || cell.Text == "@" || cell.Text == "/") continue;
                        float w = lyrics.MeasureText(cell.Text);
                        canvas.DrawText(cell.Text, note.X - w / 2, y, lyrics, ink);
                        if (cell.Punctuation.Length > 0)
                            canvas.DrawText(cell.Punctuation, note.X + w / 2, y, lyrics, ink);
                    }
                }
            }
            foreach (var slur in document.Slurs)
            {
                ct.ThrowIfCancellationRequested();
                // 换行连音从当前行左边续接，上一行右边结束；不丢弃跨行连音。
                for (int r = slur.Start.VisualRow; r <= slur.End.VisualRow; r++)
                {
                    var row = rows[r];
                    float x1 = r == slur.Start.VisualRow ? slur.Start.X : Margin;
                    float x2 = r == slur.End.VisualRow ? slur.End.X : Width - Margin;
                    float y = row.Baseline - 59 - slur.Depth * 12;
                    using var path = new SKPath();
                    path.MoveTo(x1, y);
                    path.CubicTo(x1 + (x2 - x1) / 3, y - 24, x2 - (x2 - x1) / 3, y - 24, x2, y);
                    canvas.DrawPath(path, line);
                }
            }
            float footerY = rows[^1].LyricsY + Math.Max(1, rows[^1].Source.Verses.Count) * 47 + 50;
            foreach (var foot in document.Footer.Concat(document.Remarks.Where(r => r.Position.StartsWith("B")).Select(r => r.Text)))
            {
                ct.ThrowIfCancellationRequested();
                canvas.DrawText(foot, Margin, footerY, text, ink); footerY += 46;
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data == null || data.Size > ShigebenClient.MaxImageBytes) throw Unsupported("渲染图片超过大小限制");
            ct.ThrowIfCancellationRequested();
            // 仅写新会话文件，失败/取消时不留下不完整图片，不覆盖既有文件。
            bool created = false;
            try
            {
                using (var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { created = true; data.SaveTo(stream); }
                ct.ThrowIfCancellationRequested();
            }
            catch { if (created) File.Delete(outputPath); throw; }
        }

        private static Document TryParse(string source, CancellationToken ct, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(source) || Encoding.UTF8.GetByteCount(source) > MaxSourceBytes)
                return Reject<Document>("谱面为空或超过大小限制", out reason);
            var doc = new Document(); var openSlurs = new Stack<(Note Note, int Depth)>();
            Row row = null; int count = 0;
            foreach (string raw in source.Replace("\r", "").Split('\n'))
            {
                ct.ThrowIfCancellationRequested();
                string value = raw.Trim();
                if (value.Length == 0 || value.StartsWith("//")) continue;
                var key = Regex.Match(value, @"^([16])=([#b♯♭]?)([A-Ga-g])([#b♯♭]?)\s*(\d{1,2})/(\d{1,2})$", RegexOptions.CultureInvariant);
                if (key.Success)
                {
                    if (doc.Key.Length > 0) return Reject<Document>("多段调号尚未支持", out reason);
                    string accidental = key.Groups[2].Value + key.Groups[4].Value;
                    if (accidental.Length > 1) return Reject<Document>("无效调号", out reason);
                    doc.Key = key.Groups[1].Value + "=" + accidental.Replace("b", "♭").Replace("#", "♯") + key.Groups[3].Value.ToUpperInvariant();
                    doc.Beats = int.Parse(key.Groups[5].Value, CultureInfo.InvariantCulture);
                    doc.BeatType = int.Parse(key.Groups[6].Value, CultureInfo.InvariantCulture);
                    if (doc.Beats < 1 || doc.Beats > 16 || !new[] { 1, 2, 4, 8, 16 }.Contains(doc.BeatType)) return Reject<Document>("无效拍号", out reason);
                    doc.Beat = doc.Beats + "/" + doc.BeatType; continue;
                }
                int colon = value.IndexOfAny(new[] { ':', '：' });
                if (colon < 0) return Reject<Document>("无法识别的谱面行", out reason);
                string tag = value[..colon].Trim(), content = value[(colon + 1)..].Trim();
                switch (tag)
                {
                    case "T": if (doc.Titles.Count >= 4 || content.Length > 60) return Reject<Document>("标题过长", out reason); doc.Titles.Add(content); break;
                    case "Z": if (content.Length > 60) return Reject<Document>("作者信息过长", out reason); doc.Credits.Add(content); break;
                    case "XL": case "XR": case "TL": case "TR": case "BL": case "BC": case "BR":
                        if (content.Length > 70) return Reject<Document>("说明文字过长", out reason); doc.Remarks.Add((tag, content)); break;
                    case "J": if (content.Length > 30) return Reject<Document>("速度说明过长", out reason); doc.Tempo = "速度：" + content; break;
                    case "W": if (content.Length > 44) return Reject<Document>("自由文字过长", out reason); doc.Footer.Add(content.Replace('/', ' ').Replace('@', ' ')); break;
                    // 排版比例由 Canvas 自身控制；这些源格式设置不改变音高/时值/歌词。
                    case "FontSize": case "Margin": case "Space":
                        if (content.Length > 200 || !Regex.IsMatch(content, @"^[A-Za-z0-9\s=%;,.:+\-]*$")) return Reject<Document>("未支持的排版设置", out reason);
                        break;
                    case "Off": if (content.Trim(';', ' ') != "hx") return Reject<Document>("未支持的隐藏标记", out reason); break;
                    case "Q":
                        row = new Row(); doc.Rows.Add(row);
                        if (doc.Rows.Count > 100) return Reject<Document>("谱面行数过多", out reason);
                        int offset = 0;
                        while (offset < content.Length)
                        {
                            var token = NotePattern.Match(content, offset);
                            if (!token.Success || token.Index != offset) return Reject<Document>("未支持的音符、装饰音或多声部语法", out reason);
                            offset += token.Length;
                            var note = new Note { Symbol = token.Groups["symbol"].Value };
                            string tail = token.Groups["suffix"].Value;
                            if (note.Bar && tail.StartsWith('/'))
                            { note.NonBreakingBar = true; tail = tail[1..]; }
                            note.Octave = tail.Count(c => c == 'g') - tail.Count(c => c == 'd');
                            note.Dots = tail.Count(c => c == '.'); note.Beams = tail.Count(c => c == '/');
                            note.BeamTag = tail.Contains('~') ? '~' : tail.Contains('^') ? '^' : '\0';
                            note.Accidental = string.Concat(tail.Where(c => "#b♯♭♮".Contains(c))).Replace("#", "♯").Replace("b", "♭");
                            if (Math.Abs(note.Octave) > 3 || note.Dots > 2 || note.Beams > 4 || note.Accidental.Length > 1 ||
                                (tail.Contains('g') && tail.Contains('d')) || (note.Bar && (note.Octave != 0 || note.Dots != 0 || note.Beams != 0)))
                                return Reject<Document>("未支持的音符修饰", out reason);
                            row.Notes.Add(note); if (++count > 4000) return Reject<Document>("音符过多", out reason);
                            // 上游解析先闭合、再开启；旧谱多余 ')' 在没有开启连音时不产生弧线。
                            foreach (char close in tail.Where(c => c == ')'))
                                if (openSlurs.Count > 0)
                                { var start = openSlurs.Pop(); doc.Slurs.Add(new Slur(start.Note, note, start.Depth)); }
                            foreach (char start in token.Groups["open"].Value)
                            { if (openSlurs.Count >= 6) return Reject<Document>("连音嵌套过多", out reason); openSlurs.Push((note, openSlurs.Count)); }
                        }
                        if (row.Notes.Count == 0) return Reject<Document>("空音符行", out reason);
                        break;
                    default:
                        if (tag == "C" || Regex.IsMatch(tag, @"^C\d{1,2}$"))
                        {
                            if (row == null || row.Verses.Count >= 8) return Reject<Document>("歌词行无法关联音符", out reason);
                            var verse = TryParseLyrics(content, out reason); if (verse == null) return null;
                            if (tag.Length > 1) verse.Label = tag[1..] + ".";
                            if (verse.Cells.Count > row.Notes.Count(n => n.Syllable)) return Reject<Document>("歌词字数超过可对齐音符数", out reason);
                            row.Verses.Add(verse);
                        }
                        else return Reject<Document>("未支持的行类型 " + tag, out reason);
                        break;
                }
            }
            if (doc.Rows.Count == 0 || doc.Key.Length == 0 || doc.Titles.Count == 0 || openSlurs.Count > 0)
                return Reject<Document>("谱面缺少标题、调号、音符或连音未闭合", out reason);
            return doc;
        }

        private static Verse TryParseLyrics(string content, out string reason)
        {
            reason = null;
            var verse = new Verse(); string front = "";
            var label = Regex.Match(content, @"^<(\d{1,2}\.?)>");
            if (label.Success) { verse.Label = label.Groups[1].Value; content = content[label.Length..]; }
            for (int i = 0; i < content.Length; i++)
            {
                char ch = content[i]; if (char.IsWhiteSpace(ch)) continue;
                if (ch == '{')
                {
                    int end = content.IndexOf('}', i + 1);
                    if (end < 0 || end - i > 12) return Reject<Verse>("未支持的歌词分组", out reason);
                    verse.Cells.Add(new Lyric { Text = front + content[(i + 1)..end].Replace('/', ' ').Replace('@', ' ') });
                    front = ""; i = end; continue;
                }
                if (ch == '/' || ch == '@' || (ch >= '\u3400' && ch <= '\u9fff'))
                { verse.Cells.Add(new Lyric { Text = front + ch }); front = ""; }
                else if (char.IsPunctuation(ch))
                {
                    if ("“‘《（【([".Contains(ch) || verse.Cells.Count == 0) front += ch;
                    else verse.Cells[^1].Punctuation += ch;
                }
                else return Reject<Verse>("未支持的歌词文字或标记", out reason);
            }
            if (front.Length > 0) return Reject<Verse>("未闭合的歌词标点", out reason);
            return verse;
        }

        private static List<LayoutRow> Layout(Document doc, SKFont lyrics, CancellationToken ct, out int height)
        {
            var output = new List<LayoutRow>();
            float y = 105 + doc.Titles.Count * 82 + doc.Credits.Count * 45 + 65 +
                doc.Remarks.Count(r => r.Position.StartsWith("T") || r.Position.StartsWith("X")) * 42 + 92;
            foreach (var row in doc.Rows)
            {
                ct.ThrowIfCancellationRequested();
                var syllables = row.Notes.Where(n => n.Syllable).ToList();
                float CellWidth(Note n)
                {
                    float minimum = n.Bar ? 32 : n.Symbol == "-" ? 36 : 49 + n.Dots * 12 + (n.Accidental.Length > 0 ? 20 : 0);
                    int index = syllables.IndexOf(n);
                    if (index >= 0)
                        foreach (var v in row.Verses)
                            if (index < v.Cells.Count && v.Cells[index].Text != "/" && v.Cells[index].Text != "@")
                                minimum = Math.Max(minimum, lyrics.MeasureText(v.Cells[index].Text + v.Cells[index].Punctuation) + 14);
                    return minimum;
                }
                int from = 0;
                while (from < row.Notes.Count)
                {
                    int to = from, lastBar = -1; float used = 0;
                    while (to < row.Notes.Count && used + CellWidth(row.Notes[to]) < Width - Margin * 2)
                    { used += CellWidth(row.Notes[to]); if (row.Notes[to].Bar && to > from) lastBar = to; to++; }
                    if (to < row.Notes.Count)
                    {
                        if (lastBar <= from) throw Unsupported("单个小节过宽，不能安全换行");
                        to = lastBar + 1;
                    }
                    if (to == from) throw Unsupported("单个歌词分组过宽");
                    var notes = row.Notes.GetRange(from, to - from);
                    float total = notes.Sum(CellWidth), extra = Width - Margin * 2 - total;
                    double weight = notes.Sum(n => n.Bar ? .4 : Math.Sqrt(n.Duration));
                    float x = Margin;
                    int bottom = notes.Max(n => n.Beams * 10 + Math.Max(0, -n.Octave) * 11);
                    var layout = new LayoutRow { Source = row, Notes = notes, Baseline = y, LyricsY = y + 58 + bottom };
                    foreach (var n in notes)
                    {
                        float w = CellWidth(n) + extra * (float)((n.Bar ? .4 : Math.Sqrt(n.Duration)) / weight);
                        n.X = x + w / 2; n.Y = y; n.VisualRow = output.Count; x += w;
                    }
                    output.Add(layout); y = layout.LyricsY + Math.Max(1, row.Verses.Count) * 47 + 100;
                    from = to;
                }
            }
            y += (doc.Footer.Count + doc.Remarks.Count(r => r.Position.StartsWith("B"))) * 46 + 50;
            height = checked((int)Math.Ceiling(y));
            return output;
        }

        private static void DrawNote(SKCanvas canvas, Note n, SKFont font, SKFont small, SKPaint ink, SKPaint line)
        {
            if (n.Bar)
            {
                float x = n.X, y1 = n.Y - 40, y2 = n.Y + 22;
                canvas.DrawLine(x, y1, x, y2, line);
                if (n.Symbol is "||" or "|||" or ":|" or "|:" or ":|:") canvas.DrawLine(x + 7, y1, x + 7, y2, line);
                if (n.Symbol == "|||") { float w = line.StrokeWidth; line.StrokeWidth = 7; canvas.DrawLine(x + 9, y1, x + 9, y2, line); line.StrokeWidth = w; }
                if (n.Symbol.StartsWith(':')) { canvas.DrawCircle(x - 11, n.Y - 22, 3, ink); canvas.DrawCircle(x - 11, n.Y - 4, 3, ink); }
                if (n.Symbol.EndsWith(':')) { canvas.DrawCircle(x + 18, n.Y - 22, 3, ink); canvas.DrawCircle(x + 18, n.Y - 4, 3, ink); }
                return;
            }
            if (n.Symbol == "-") canvas.DrawLine(n.X - 12, n.Y - 16, n.X + 12, n.Y - 16, line);
            else Center(canvas, n.Symbol, n.X, n.Y, font, ink, 60);
            if (n.Accidental.Length > 0) canvas.DrawText(n.Accidental, n.X - 38, n.Y - 15, small, ink);
            for (int d = 0; d < n.Dots; d++) canvas.DrawCircle(n.X + 20 + d * 11, n.Y - 15, 3.2f, ink);
            for (int o = 0; o < Math.Abs(n.Octave); o++)
                canvas.DrawCircle(n.X, n.Octave > 0 ? n.Y - 50 - o * 11 : n.Y + n.Beams * 10 + 14 + o * 11, 3.2f, ink);
        }

        private static void DrawBeams(SKCanvas canvas, List<Note> notes, Document doc, SKPaint line)
        {
            double unit = doc.BeatType >= 8 && doc.Beats % 3 == 0 ? 12.0 / doc.BeatType : 4.0 / doc.BeatType;
            for (int level = 1; level <= 4; level++)
            {
                Note start = null, previous = null; double elapsed = 0;
                void Finish() { if (start != null) canvas.DrawLine(start.X - 13, start.Y + level * 10 - 3, previous.X + 13, previous.Y + level * 10 - 3, line); start = null; }
                foreach (var n in notes)
                {
                    if (n.Bar) { Finish(); previous = null; if (!n.NonBreakingBar) elapsed = 0; continue; }
                    if (n.Beams < level) { Finish(); previous = n; elapsed += n.Duration; continue; }
                    if (start != null && previous?.BeamTag != '~' &&
                        (previous?.BeamTag == '^' || Math.Floor((elapsed + .00001) / unit) > Math.Floor((elapsed - previous.Duration + .00001) / unit))) Finish();
                    start ??= n; previous = n; elapsed += n.Duration;
                    if (n.BeamTag == '^') Finish();
                }
                Finish();
            }
        }
        private static void Center(SKCanvas canvas, string value, float x, float y, SKFont font, SKPaint ink, float maxWidth)
        {
            float width = font.MeasureText(value);
            if (width <= maxWidth) { canvas.DrawText(value, x - width / 2, y, font, ink); return; }
            canvas.Save(); canvas.Translate(x, y); float scale = maxWidth / width; canvas.Scale(scale); canvas.DrawText(value, -width / 2, 0, font, ink); canvas.Restore();
        }
        // 简单语法不匹配是正常的引擎选择结果，不抛异常再捕获。
        private static T Reject<T>(string detail, out string reason) where T : class
        {
            reason = "结构化简谱暂不能安全排版：" + detail + "；将尝试原始歌谱图片。";
            return null;
        }
        private static ScoreIntegrationException Unsupported(string reason) => new("结构化简谱暂不能安全排版：" + reason + "；将尝试原始歌谱图片。");
    }
}
