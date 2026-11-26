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
    // 扩展谱文解析器：不执行站点代码、不依赖 YouPu 字体。每个音乐标记均进入模型，
    // 不认识的标记携带原行号拒绝；按声部/小节/有理时值布局，不把多声部串成单声部。
    internal static class ExtendedScoreNotationRenderer
    {
        private const int Width = 1800;
        private const float Left = 105, Right = 80, Size = 43, LyricSize = 31;
        private static readonly Regex Token = new(@"\G(?<open>(?:\(y\d*:|\(y|\()*)(?<note>:\|:|:\||\|:|\|\|\||\|\||\|/|\||[0-9X\-])(?<tail>(?:[^0-9:(|{\-X"" ]|\s|""[^""]*"")*)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly HashSet<string> Marks = new(StringComparer.Ordinal)
        { "yc", "bc", "zy", "dy", "hx", "shy", "xhy", "sby", "sby+", "xby", "xby+", "cy", "cy+", "pp", "p", "mp", "mf", "f", "ff", "fff", "ppp", "sf", "fp", "sfp", "sfz", "ds", "dc", "ty", "hs", "fine", "zkh", "ykh" };
        private readonly record struct Time(long N, long D) : IComparable<Time>
        {
            public static Time Zero => new(0, 1);
            public static Time Make(long n, long d) { long a = Math.Abs(n), b = d; while (b != 0) { long t = a % b; a = b; b = t; } return new(n / Math.Max(1, a), d / Math.Max(1, a)); }
            public static Time operator +(Time a, Time b) => Make(checked(a.N * b.D + b.N * a.D), checked(a.D * b.D));
            public static Time operator -(Time a, Time b) => Make(checked(a.N * b.D - b.N * a.D), checked(a.D * b.D));
            public static Time operator *(Time a, Time b) => Make(checked(a.N * b.N), checked(a.D * b.D));
            public int CompareTo(Time b) => checked(N * b.D).CompareTo(checked(b.N * D));
            public double Value => (double)N / D;
        }
        private sealed class Cell
        {
            public string Text = "", Head = "";
            public bool Underline; public int HoldCells;
            public float Scale = 1;
            public List<string> Lines => Text.Replace('/', ' ').Replace('@', ' ').Split('|').ToList();
        }
        private sealed class Verse { public string Label = ""; public List<Cell> Cells = new(); }
        private sealed class Note
        {
            public string Symbol, Accidental = "", Beat = "", Chord = "", Comment = "", Dynamics = "", Repeat = "", Key = "";
            public List<string> Marks = new(); public List<Note> Grace = new(); public bool GraceAfter;
            public int Octave, Dots, Beams, Height, Measure, Ordinal, Voice, BeatCount = 4, BeatUnit = 4, RowIndex = -1, LyricIndex = -1;
            public float X, Y;
            public bool NoSplit, StopHairpin, HideBeat;
            public char BeamTag, Hairpin;
            public Time Duration = Time.Zero, Start = Time.Zero;
            public bool Bar => Symbol.Contains('|');
            public bool Syllable => Symbol is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "X" || ((Symbol is "0" or "-") && LyricIndex >= 0);
            public bool Invisible => Symbol is "8" or "9";
        }
        private sealed class Voice { public int Id; public string Header = ""; public List<Note> Notes = new(); public List<Verse> Lyrics = new(); }
        private sealed class SystemRow { public List<Voice> Voices = new(); }
        private sealed class Section
        {
            public string Key = "", Beat = "", Tempo = ""; public int Beats = 4, BeatType = 4;
            public List<(string Tag, string Text)> Header = new(), Footer = new();
            public List<object> Blocks = new(); public bool HideChords, HidePunctuation;
        }
        private sealed record Arc(Note Start, Note End, int Depth, int Tuplet);
        private sealed record Ending(Note Start, Note End, string Label, bool Closed, int Height);
        private sealed class Score { public List<Section> Sections = new(); public List<Arc> Arcs = new(); public List<Ending> Endings = new(); }
        private sealed class Line { public Voice Voice; public Section Section; public List<Note> Notes; public float Baseline, Bottom; public int From, To; }
        private sealed class PositionedText { public string Tag, Text; public float Y; }
        private sealed class Layout { public List<Line> Lines = new(); public List<PositionedText> Texts = new(); public int Height; }
        private static ScoreIntegrationException Error(int line, string reason) => new($"结构化歌谱第 {line} 行：{reason}；未忽略音乐内容，将尝试原图。");
        public static bool CanRender(string source, out string reason)
        {
            try { Parse(source, CancellationToken.None); reason = null; return true; }
            catch (ScoreIntegrationException ex) { reason = ex.Message; return false; }
            catch (OverflowException) { reason = "谱文时值超出安全范围。"; return false; }
        }
        private static Score Parse(string source, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(source) || Encoding.UTF8.GetByteCount(source) > ScoreNotationRenderer.MaxSourceBytes) throw Error(1, "谱文为空或过大");
            var score = new Score(); var section = new Section(); score.Sections.Add(section);
            SystemRow system = null; Voice voice = null; int total = 0;
            var slurs = new Dictionary<int, Stack<(Note Note, int Depth, int Tuplet)>>();
            var endings = new Dictionary<int, (Note Note, string Label, int Height)>();
            void FinishSection(int line)
            {
                if (slurs.Values.Any(s => s.Count != 0)) throw Error(line, "分段前连音没有闭合");
                foreach (var end in endings.Values)
                { var last = section.Blocks.OfType<SystemRow>().SelectMany(r => r.Voices).Where(v => v.Id == end.Note.Voice).SelectMany(v => v.Notes).LastOrDefault(); score.Endings.Add(new(end.Note, last, end.Label, false, end.Height)); }
                slurs.Clear(); endings.Clear();
            }
            var rawLines = source.Replace("\r", "").Split('\n');
            for (int index = 0; index < rawLines.Length; index++)
            {
                ct.ThrowIfCancellationRequested(); int line = index + 1; string value = rawLines[index].Trim();
                if (value.Length == 0 || value.StartsWith('#') || value.StartsWith("//")) continue;
                if (value.StartsWith('-') || value.StartsWith('－') || value == "*")
                { FinishSection(line); section = new Section(); score.Sections.Add(section); system = null; voice = null; continue; }
                var signature = Regex.Match(value, @"^([16])=([#♯b♭]?[A-Ga-g][#♯b♭]?)(?:\s*(\d{1,2})/(\d{1,2}))?(.*)$");
                if (signature.Success)
                {
                    if (section.Key.Length != 0) throw Error(line, "重复调号应先分段");
                    section.Key = signature.Groups[1].Value + "=" + signature.Groups[2].Value.Replace("♭", "b").Replace("♯", "#");
                    if (signature.Groups[3].Success) { section.Beats = int.Parse(signature.Groups[3].Value); section.BeatType = int.Parse(signature.Groups[4].Value); ValidateBeat(section.Beats, section.BeatType, line); section.Beat = section.Beats + "/" + section.BeatType; }
                    section.Tempo = signature.Groups[5].Value.Trim(); continue;
                }
                int colon = value.IndexOfAny(new[] { ':', '：' }); if (colon < 0) throw Error(line, "无法识别的行");
                string tag = value[..colon].Trim(), content = value[(colon + 1)..].Trim();
                if (Regex.IsMatch(tag, @"^(T|Z|XL|XR|TL|TR)(?:<[^>]*>)?$"))
                { if (content.Length > 180) throw Error(line, "标题或说明过长"); section.Header.Add((tag.Split('<')[0], content)); continue; }
                if (Regex.IsMatch(tag, @"^(BL|BC|BR)(?:<[^>]*>)?$")) { section.Footer.Add((tag[..2], content)); continue; }
                if (Regex.IsMatch(tag, @"^J(?:16|[1248])?\.*$")) { section.Tempo = "速度 " + content; continue; }
                if (Regex.IsMatch(tag, @"^O\d*$")) { if (content != "auto" && !int.TryParse(content, out _)) throw Error(line, "无效音区定义"); continue; }
                if (tag is "FontSize" or "Margin" or "Space")
                { if (content.Length > 500 || !Regex.IsMatch(content, @"^[A-Za-z0-9\s=%;,.:+\-]*$")) throw Error(line, "无效排版设置"); continue; }
                if (tag == "Off")
                { foreach (var off in content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) { if (off == "hx") section.HideChords = true; else if (off == "bd") section.HidePunctuation = true; else throw Error(line, "未知隐藏项 " + off); } continue; }
                if (Regex.IsMatch(tag, @"^W(?:<[^>]*>)?$")) { section.Blocks.Add(content.Replace('/', ' ').Replace('@', ' ')); system = null; continue; }
                var q = Regex.Match(tag, @"^Q(\d*)(?:<([^>]*)>)?([!+\-]*)$");
                if (q.Success)
                {
                    int id = q.Groups[1].Value.Length == 0 ? 1 : int.Parse(q.Groups[1].Value); if (id < 1 || id > 16) throw Error(line, "声部编号超出范围");
                    if (id > 1 && system == null) throw Error(line, "附加声部没有对应的 Q1 主声部");
                    if (system == null || q.Groups[1].Value.Length == 0 || id == 1 || system.Voices.Any(v => v.Id == id)) { system = new SystemRow(); section.Blocks.Add(system); }
                    voice = new Voice { Id = id, Header = q.Groups[2].Value }; system.Voices.Add(voice);
                    if (!slurs.TryGetValue(id, out var stack)) slurs[id] = stack = new();
                    int offset = 0, lyricIndex = 0;
                    while (offset < content.Length)
                    {
                        while (offset < content.Length && char.IsWhiteSpace(content[offset])) offset++;
                        if (offset == content.Length) break;
                        var token = Token.Match(content, offset);
                        if (!token.Success || token.Index != offset || token.Length == 0) throw Error(line, "无法识别的音符附近：" + content.Substring(offset, Math.Min(24, content.Length - offset)));
                        offset += token.Length; var note = new Note { Symbol = token.Groups["note"].Value, Voice = id };
                        string tail = token.Groups["tail"].Value.Trim();
                        ParseTail(note, ref tail, line);
                        if (tail.Contains('@') || note.Symbol is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "X") note.LyricIndex = lyricIndex++;
                        foreach (char close in tail.Where(c => c == ')'))
                            if (stack.Count > 0) { var start = stack.Pop(); score.Arcs.Add(new(start.Note, note, start.Depth, start.Tuplet)); }
                        foreach (Match open in Regex.Matches(token.Groups["open"].Value, @"\(y(\d*):|\(y|\("))
                        { if (stack.Count >= 6) throw Error(line, "连音嵌套过多"); int tuplet = open.Value.Contains('y') ? (open.Groups[1].Value.Length > 0 ? int.Parse(open.Groups[1].Value) : 1) : 0; stack.Push((note, stack.Count, tuplet)); }
                        if (note.Bar)
                        {
                            if (tail.Contains(']') && endings.Remove(id, out var end)) score.Endings.Add(new(end.Note, note, end.Label, !tail.Contains("]/"), end.Height));
                            var bracket = Regex.Match(tail, @"\[(\+*)(?:«([^»]*)»)?");
                            if (bracket.Success)
                            { if (endings.Remove(id, out var previous)) score.Endings.Add(new(previous.Note, note, previous.Label, false, previous.Height)); endings[id] = (note, bracket.Groups[2].Value, bracket.Groups[1].Length); }
                        }
                        tail = Regex.Replace(tail, @"[\[\]]/?|«[^»]*»", "").Replace("+", "").Replace(")", "");
                        if (tail.Any(c => !char.IsWhiteSpace(c) && !"dg./^~#b♯♭♮@<>!𝄪𝄫".Contains(c))) throw Error(line, "未知音符修饰 " + tail.Trim());
                        note.Octave = tail.Count(c => c == 'g') - tail.Count(c => c == 'd'); note.Dots = tail.Count(c => c == '.'); note.Beams = tail.Count(c => c == '/');
                        note.BeamTag = tail.Contains('~') ? '~' : tail.Contains('^') ? '^' : '\0'; note.Hairpin = tail.Contains('<') ? '<' : tail.Contains('>') ? '>' : '\0'; note.StopHairpin |= tail.Contains('!');
                        if (note.Accidental.Length == 0) note.Accidental = Regex.Match(tail, @"[#b♯♭♮]|𝄪|𝄫").Value.Replace("#", "♯").Replace("b", "♭");
                        if (tail.Contains('g') && tail.Contains('d') || Regex.Matches(tail, @"[#b♯♭♮]|𝄪|𝄫").Count > 1) throw Error(line, "互相矛盾的音区或变音记号");
                        if (Math.Abs(note.Octave) > 6 || note.Dots > 3 || note.Beams > 5 || note.Accidental.Length > 2) throw Error(line, "音符修饰超出范围");
                        if (note.Bar && (note.Beams > 0 || note.Octave != 0 || note.Dots != 0)) throw Error(line, "小节线不应带音符时值");
                        note.Duration = note.Bar || note.Invisible ? Time.Zero : Time.Make((1L << (note.Dots + 1)) - 1, 1L << (note.Dots + note.Beams));
                        voice.Notes.Add(note); if (++total > 6000) throw Error(line, "音符总数过多");
                    }
                    if (voice.Notes.Count == 0) throw Error(line, "空声部"); continue;
                }
                if (Regex.IsMatch(tag, @"^C\d*(?:-\d+)?$"))
                {
                    if (voice == null || voice.Lyrics.Count >= 16) throw Error(line, "歌词没有对应声部或过多");
                    var verse = ParseLyrics(content, line, section.HidePunctuation); if (tag.Length > 1 && verse.Label.Length == 0) verse.Label = tag[1..] + ".";
                    int available = voice.Notes.Count(n => n.LyricIndex >= 0); if (verse.Cells.Count > available) throw Error(line, $"歌词 {verse.Cells.Count} 个音节超出声部 {available} 个音符");
                    voice.Lyrics.Add(verse); continue;
                }
                throw Error(line, "未知行类型 " + tag);
            }
            FinishSection(rawLines.Length);
            if (!score.Sections.Any(s => s.Blocks.OfType<SystemRow>().Any())) throw Error(1, "没有音符");
            foreach (var arc in score.Arcs.Where(a => a.Tuplet > 0))
            {
                var all = score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).SelectMany(s => s.Voices).Where(v => v.Id == arc.Start.Voice).SelectMany(v => v.Notes).ToList();
                int start = all.IndexOf(arc.Start), end = all.IndexOf(arc.End); var notes = all.Skip(start).Take(end - start + 1).Where(n => !n.Bar && !n.Invisible).ToList();
                int count = arc.Tuplet == 1 ? notes.Count(n => n.Symbol != "-") : arc.Tuplet;
                int numerator = count switch { 2 => 3, 3 => 2, 4 => 3, >= 5 and <= 7 => 4, >= 9 and <= 15 => 8, _ => 0 };
                if (numerator == 0) throw Error(1, "未知连音时值组 " + count);
                foreach (var note in notes) note.Duration *= Time.Make(numerator, count);
            }
            foreach (var part in score.Sections)
                foreach (var group in part.Blocks.OfType<SystemRow>().SelectMany(r => r.Voices).GroupBy(v => v.Id))
                {
                    int beats = part.Beats, type = part.BeatType;
                    foreach (var n in group.SelectMany(v => v.Notes))
                    { if (n.Beat.Length > 0) { var b = n.Beat.Split('/'); beats = int.Parse(b[0]); type = int.Parse(b[1]); } n.BeatCount = beats; n.BeatUnit = type; }
                }
            return score;
        }
        private static void ValidateBeat(int beats, int type, int line)
        { if (beats < 1 || beats > 32 || !new[] { 1, 2, 4, 8, 16, 32 }.Contains(type)) throw Error(line, "无效拍号"); }
        private static void ParseTail(Note n, ref string tail, int line)
        {
            if (n.Bar && tail.StartsWith('/')) { n.NoSplit = true; tail = tail[1..]; }
            if (n.Symbol == "|/") n.NoSplit = true;
            string remaining = tail;
            foreach (Match m in Regex.Matches(tail, "\"([^\"]*)\""))
            {
                string annotation = m.Groups[1].Value;
                if (n.Bar && remaining.IndexOf(m.Value, StringComparison.Ordinal) > 0 && remaining[..remaining.IndexOf(m.Value, StringComparison.Ordinal)].Contains('['))
                { remaining = remaining.Replace(m.Value, "«" + annotation + "»"); continue; }
                remaining = remaining.Replace(m.Value, "");
                var a = Regex.Match(annotation, @"^([a-zA-Z/]*)(\+*|\+\d+):(.*)$");
                if (!a.Success) { n.Comment = annotation; continue; }
                string tag = a.Groups[1].Value, text = a.Groups[3].Value; n.Height = Math.Max(n.Height, Math.Min(12, a.Groups[2].Length));
                switch (tag)
                {
                    case "hx": n.Chord = text; break;
                    case "d": n.Key = (text == text.ToLowerInvariant() ? "6=" : "1=") + text; break;
                    case "p": case "p/":
                        var beat = Regex.Match(text, @"^(\d{1,2})/(\d{1,2})$"); if (!beat.Success) throw Error(line, "无效变拍号"); ValidateBeat(int.Parse(beat.Groups[1].Value), int.Parse(beat.Groups[2].Value), line); n.Beat = text; n.HideBeat = tag == "p/"; break;
                    case "yy": case "hyy":
                        if (text.Length > 100) throw Error(line, "倚音过长"); n.GraceAfter = tag == "hyy"; n.Grace = ParseGrace(text, line); break;
                    case "ld": case "sd": n.Dynamics = text.TrimEnd('!'); n.StopHairpin |= text.Contains('!'); if (text.Contains('<')) n.Hairpin = '<'; else if (text.Contains('>')) n.Hairpin = '>'; break;
                    case "ff": case "ffs": case "ffcs": n.Repeat = text; break;
                    case "yc": n.Marks.Add("yc"); break;
                    case "bz": n.Comment = text.Length == 0 || text == "!" ? "伴奏" : text; break;
                    case "": n.Comment = text; break;
                    default: throw Error(line, "未实现的音乐注释 " + tag);
                }
            }
            foreach (Match m in Regex.Matches(remaining, @"&([a-z]+\+?|\++)"))
            { string mark = m.Groups[1].Value; if (mark.All(c => c == '+')) n.Height += Math.Min(12, mark.Length); else if (Marks.Contains(mark)) n.Marks.Add(mark); else throw Error(line, "未知音乐符号 &" + mark); }
            tail = Regex.Replace(remaining, @"&([a-z]+\+?|\++)", "");
        }
        private static List<Note> ParseGrace(string text, int line)
        {
            var notes = new List<Note>(); int offset = 0;
            while (offset < text.Length)
            {
                var m = Regex.Match(text[offset..], @"^\s*([0-7])([dg./#b♯♭♮]*)\s*"); if (!m.Success) throw Error(line, "无法识别的倚音");
                string tail = m.Groups[2].Value; notes.Add(new Note { Symbol = m.Groups[1].Value, Octave = tail.Count(c => c == 'g') - tail.Count(c => c == 'd'), Beams = tail.Count(c => c == '/'), Dots = tail.Count(c => c == '.'), Accidental = string.Concat(tail.Where(c => "#b♯♭♮".Contains(c))).Replace("b", "♭").Replace("#", "♯") }); offset += m.Length;
            }
            if (notes.Count == 0) throw Error(line, "空倚音"); return notes;
        }
        private static Verse ParseLyrics(string text, int line, bool noPunctuation)
        {
            var verse = new Verse(); string head = ""; bool underline = false; float scale = 1;
            for (int i = 0; i < text.Length;)
            {
                char ch = text[i]; if (char.IsWhiteSpace(ch)) { i++; continue; }
                if (ch == '<')
                { int end = text.IndexOf('>', i + 1); if (end < 0) throw Error(line, "歌词标签未闭合"); string h = text[(i + 1)..end]; if (verse.Cells.Count == 0) verse.Label += h; else head += h; i = end + 1; continue; }
                if (ch == '[')
                {
                    int end = text.IndexOf(']', i + 1); if (end < 0) throw Error(line, "歌词设置未闭合");
                    foreach (string tag in text[(i + 1)..end].Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (tag == "ul") underline = true; else if (tag == "ul!") underline = false;
                        else if (tag is "small" or "hy") scale = .8f; else if (tag is "small!" or "hy!") scale = 1;
                        else if (tag.StartsWith("small:") && float.TryParse(tag[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out float size)) scale = Math.Clamp(size, .2f, 1.5f);
                        else if (tag.Length > 0 && tag.All(c => c == '/') && verse.Cells.Count > 0) verse.Cells[^1].HoldCells += tag.Length;
                        else throw Error(line, "未知歌词设置 " + tag);
                    }
                    i = end + 1; continue;
                }
                string cell;
                if (ch == '{') { int end = text.IndexOf('}', i + 1); if (end < 0) throw Error(line, "歌词分组未闭合"); cell = text[(i + 1)..end]; if (cell.Contains('{')) throw Error(line, "歌词分组嵌套"); i = end + 1; }
                else if (char.IsAsciiLetterOrDigit(ch)) { int start = i; while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '\'')) i++; cell = text[start..i]; }
                else if (ch == '-' && text.Any(char.IsAsciiLetter)) { cell = "-"; i++; }
                else if (ch is '/' or '@' || ch >= '\u3400' && ch <= '\u9fff') { cell = ch.ToString(); i++; }
                else if (char.IsPunctuation(ch) || char.GetUnicodeCategory(ch) == UnicodeCategory.OtherNumber)
                { if (!noPunctuation) { if (verse.Cells.Count == 0 || "“‘《（【([".Contains(ch)) head += ch; else verse.Cells[^1].Text += ch; } i++; continue; }
                else throw Error(line, "未知歌词字符 " + ch);
                if (cell.Length > 500) throw Error(line, "歌词分组过长"); verse.Cells.Add(new Cell { Text = cell, Head = head, Underline = underline, Scale = scale }); head = "";
            }
            if (head.Length > 0 && verse.Cells.Count > 0) verse.Cells[^1].Text += head;
            return verse;
        }

        public static void RenderToPng(string source, string path, CancellationToken ct)
        {
            var score = Parse(source, ct);
            using var face = SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;
            using var numberFace = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
            using var words = new SKFont(face, LyricSize); using var numbers = new SKFont(numberFace, Size);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var stroke = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f };
            var layout = Position(score, words, ct);
            if ((long)layout.Height * Width > 40_000_000) throw Error(1, "谱面超出图片尺寸限制");
            using var bitmap = new SKBitmap(Width, layout.Height, SKColorType.Bgra8888, SKAlphaType.Opaque); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
            foreach (var item in layout.Texts)
            {
                ct.ThrowIfCancellationRequested(); using var font = new SKFont(face, item.Tag == "T" ? 63 : item.Tag == "W" ? 32 : 31);
                float x = item.Tag is "T" or "Z" or "BC" ? Width / 2f : item.Tag.EndsWith('R') ? Width - Right : Left;
                Text(canvas, item.Text, x, item.Y, font, paint, item.Tag is "T" or "Z" or "BC" ? 0 : item.Tag.EndsWith('R') ? 1 : -1);
            }
            foreach (var row in layout.Lines)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var n in row.Notes) { DrawNote(canvas, n, numbers, words, paint, stroke); DrawAnnotations(canvas, n, row.Section, words, paint, stroke); }
                Beams(canvas, row, stroke);
                float lyricY = row.Baseline + 72 + row.Notes.Max(n => n.Beams * 8 + Math.Max(0, -n.Octave) * 10);
                foreach (var verse in row.Voice.Lyrics)
                {
                    int maxLines = 1;
                    foreach (var n in row.Notes.Where(n => n.LyricIndex >= 0 && n.LyricIndex < verse.Cells.Count))
                    {
                        var cell = verse.Cells[n.LyricIndex]; if (cell.Text is "/" or "@" or "") continue;
                        using var font = new SKFont(face, LyricSize * cell.Scale);
                        var lines = WrapCell(cell, font, 360); maxLines = Math.Max(maxLines, lines.Count);
                        for (int i = 0; i < lines.Count; i++)
                        { float y = lyricY + i * 40; Text(canvas, lines[i], n.X, y, font, paint, 0); if (cell.Underline) canvas.DrawLine(n.X - font.MeasureText(lines[i]) / 2, y + 7, n.X + font.MeasureText(lines[i]) / 2, y + 7, stroke); }
                        if (cell.HoldCells > 0)
                        { var end = row.Notes.LastOrDefault(e => e.LyricIndex >= n.LyricIndex && e.LyricIndex <= n.LyricIndex + cell.HoldCells) ?? n; canvas.DrawLine(n.X + font.MeasureText(lines[^1]) / 2 + 5, lyricY + 7, end.X + 12, lyricY + 7, stroke); }
                        if (cell.Head.Length > 0) Text(canvas, cell.Head, n.X - 25, lyricY - 3, font, paint, 1);
                    }
                    Text(canvas, verse.Label, 22, lyricY, words, paint, -1); lyricY += maxLines * 40 + 8;
                }
            }
            foreach (var arc in score.Arcs)
            {
                var lines = layout.Lines.Where(l => l.Voice.Id == arc.Start.Voice && l.Notes.Any(n => n.RowIndex >= arc.Start.RowIndex && n.RowIndex <= arc.End.RowIndex)).ToList();
                foreach (var row in lines)
                {
                    float x1 = row.Notes.Contains(arc.Start) ? arc.Start.X : Left, x2 = row.Notes.Contains(arc.End) ? arc.End.X : Width - Right;
                    float y = row.Baseline - TopSpace(row.Notes, row.Section) - 8 - arc.Depth * 17;
                    using var curve = new SKPath(); curve.MoveTo(x1, y); curve.CubicTo(x1 + (x2 - x1) / 3, y - 25, x2 - (x2 - x1) / 3, y - 25, x2, y); canvas.DrawPath(curve, stroke);
                    if (arc.Tuplet > 0 && row.Notes.Contains(arc.Start))
                    { int count = arc.Tuplet == 1 ? lines.SelectMany(l => l.Notes).Count(n => !n.Bar && !n.Invisible && n.Symbol != "-" && n.RowIndex >= arc.Start.RowIndex && n.RowIndex <= arc.End.RowIndex && (n.RowIndex != arc.Start.RowIndex || n.X >= arc.Start.X) && (n.RowIndex != arc.End.RowIndex || n.X <= arc.End.X)) : arc.Tuplet; Text(canvas, count.ToString(), (x1 + x2) / 2, y - 29, words, paint, 0); }
                }
            }
            foreach (var end in score.Endings)
            {
                int last = end.End?.RowIndex ?? layout.Lines.FindLastIndex(l => l.Voice.Id == end.Start.Voice);
                foreach (var row in layout.Lines.Where(l => l.Voice.Id == end.Start.Voice && l.Notes.Any(n => n.RowIndex >= end.Start.RowIndex && n.RowIndex <= last)))
                {
                    float x1 = row.Notes.Contains(end.Start) ? end.Start.X : Left, x2 = end.End != null && row.Notes.Contains(end.End) ? end.End.X : Width - Right, y = row.Baseline - TopSpace(row.Notes, row.Section) - 65 - end.Height * 12;
                    canvas.DrawLine(x1, y, x2, y, stroke); if (row.Notes.Contains(end.Start)) { canvas.DrawLine(x1, y, x1, y + 24, stroke); Text(canvas, end.Label, x1 + 10, y + 33, words, paint, -1); }
                    if (end.Closed && end.End != null && row.Notes.Contains(end.End)) canvas.DrawLine(x2, y, x2, y + 24, stroke);
                }
            }
            // 每个真正的多声部系统只绘一次括号；各声部已按相同小节时间坐标定位。
            foreach (var group in layout.Lines.GroupBy(l => (l.Section, l.From, l.To, System: score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).First(s => s.Voices.Contains(l.Voice)))))
                if (group.Count() > 1)
                { var first = group.First(); var last = group.Last(); canvas.DrawLine(Left - 28, first.Baseline - 40, Left - 28, last.Baseline + 18, stroke); canvas.DrawLine(Left - 28, first.Baseline - 40, Left - 15, first.Baseline - 40, stroke); canvas.DrawLine(Left - 28, last.Baseline + 18, Left - 15, last.Baseline + 18, stroke); }
            using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded == null || encoded.Size > ShigebenClient.MaxImageBytes) throw Error(1, "渲染文件过大");
            ct.ThrowIfCancellationRequested(); bool created = false;
            try { using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { created = true; encoded.SaveTo(file); } ct.ThrowIfCancellationRequested(); }
            catch { if (created) File.Delete(path); throw; }
        }
        private static List<string> WrapCell(Cell cell, SKFont font, float maxWidth)
        {
            var output = new List<string>();
            foreach (string input in cell.Lines)
            { string line = ""; foreach (var rune in input.EnumerateRunes()) { string next = rune.ToString(); if (line.Length > 0 && font.MeasureText(line + next) > maxWidth) { output.Add(line); line = ""; } line += next; } output.Add(line); }
            return output;
        }
        private static Layout Position(Score score, SKFont words, CancellationToken ct)
        {
            var layout = new Layout(); float y = 80;
            foreach (var section in score.Sections)
            {
                foreach (var header in section.Header) { layout.Texts.Add(new() { Tag = header.Tag, Text = header.Text, Y = y + (header.Tag == "T" ? 60 : 32) }); y += header.Tag == "T" ? 82 : 43; }
                if (section.Key.Length > 0) { layout.Texts.Add(new() { Tag = "W", Text = section.Key + "    " + section.Beat + "    " + section.Tempo, Y = y + 32 }); y += 65; }
                foreach (var block in section.Blocks)
                {
                    ct.ThrowIfCancellationRequested();
                    if (block is string free)
                    { foreach (var line in WrapCell(new Cell { Text = free }, words, Width - Left - Right)) { layout.Texts.Add(new() { Tag = "W", Text = line, Y = y + 32 }); y += 45; } continue; }
                    var system = (SystemRow)block;
                    foreach (var voice in system.Voices)
                    {
                        int measure = 0, ordinal = 0; Time time = Time.Zero;
                        foreach (var n in voice.Notes)
                        { n.Measure = measure; n.Start = time; n.Ordinal = ordinal++; if (n.Bar && !n.NoSplit && (time.Value > 0 || ordinal > 1)) { measure++; time = Time.Zero; ordinal = 0; } else time += n.Duration; }
                        // 弱起小节靠齐完整小节尾部，各声部独立求缺拍；只用于本段第一小节。
                        if (ReferenceEquals(system, section.Blocks.OfType<SystemRow>().First()))
                        {
                            var first = voice.Notes.Where(n => n.Measure == 0).ToList(); var last = first.LastOrDefault();
                            if (last?.Bar == true && !last.NoSplit)
                            {
                                Time duration = first.Aggregate(Time.Zero, (sum, n) => sum + n.Duration);
                                Time full = Time.Make(first[0].BeatCount * 4, first[0].BeatUnit);
                                if (duration.CompareTo(Time.Zero) > 0 && duration.CompareTo(full) < 0)
                                    foreach (var n in first) n.Start += full - duration;
                            }
                        }
                    }
                    int measures = system.Voices.Max(v => v.Notes.Max(n => n.Measure)) + 1;
                    var widths = new float[measures]; var slots = new Dictionary<int, List<(Time Start, int Order, float X, float Width)>>();
                    for (int m = 0; m < measures; m++)
                    {
                        var notes = system.Voices.SelectMany(v => v.Notes).Where(n => n.Measure == m).ToList();
                        var keys = notes.Select(n => (n.Start, Order: SlotOrder(n))).Distinct().OrderBy(k => k.Start).ThenBy(k => k.Order).ToList();
                        var values = new List<(Time, int, float, float)>(); float x = 0;
                        foreach (var key in keys)
                        {
                            float w = 58;
                            foreach (var n in notes.Where(n => n.Start == key.Start && SlotOrder(n) == key.Order))
                            {
                                w = Math.Max(w, 58 + n.Dots * 10 + n.Accidental.Length * 15 + n.Grace.Count * 24);
                                if (n.Chord.Length > 0 && !section.HideChords) w = Math.Max(w, words.MeasureText(n.Chord) + 18);
                                if (n.LyricIndex >= 0)
                                    foreach (var verse in system.Voices.First(v => v.Id == n.Voice).Lyrics.Where(v => v.Cells.Count > n.LyricIndex))
                                        w = Math.Max(w, WrapCell(verse.Cells[n.LyricIndex], words, 360).Max(t => words.MeasureText(t)) + 18);
                            }
                            values.Add((key.Start, key.Order, x + w / 2, w)); x += w;
                        }
                        widths[m] = x + 20; slots[m] = values;
                        if (x > Width - Left - Right) throw Error(1, "单小节含过宽歌词或过多音符，不能无损排版");
                    }
                    for (int from = 0; from < measures;)
                    {
                        int to = from; float total = 0;
                        while (to < measures && total + widths[to] <= Width - Left - Right) total += widths[to++];
                        if (to == from) throw Error(1, "小节宽度超出限制");
                        float expand = (Width - Left - Right) / Math.Max(1, total);
                        foreach (var voice in system.Voices)
                        {
                            var notes = voice.Notes.Where(n => n.Measure >= from && n.Measure < to).ToList(); if (notes.Count == 0) continue;
                            y += TopSpace(notes, section) + 95;
                            float x = Left; int rowIndex = layout.Lines.Count;
                            for (int m = from; m < to; m++)
                            { foreach (var n in notes.Where(n => n.Measure == m)) { int order = SlotOrder(n); var slot = slots[m].First(s => s.Start == n.Start && s.Order == order); n.X = x + slot.X * expand; n.Y = y; n.RowIndex = rowIndex; } x += widths[m] * expand; }
                            float lyricHeight = voice.Lyrics.Sum(v => Math.Max(1, notes.Where(n => n.LyricIndex >= 0 && n.LyricIndex < v.Cells.Count).Select(n => WrapCell(v.Cells[n.LyricIndex], words, 360).Count).DefaultIfEmpty(1).Max()) * 40 + 8);
                            float bottom = y + 86 + notes.Max(n => n.Beams * 8 + Math.Max(0, -n.Octave) * 10) + lyricHeight;
                            layout.Lines.Add(new() { Voice = voice, Section = section, Notes = notes, Baseline = y, Bottom = bottom, From = from, To = to });
                            if (voice.Header.Length > 0) layout.Texts.Add(new() { Tag = "W", Text = voice.Header, Y = y - 48 });
                            y = bottom + 32;
                        }
                        y += 20; from = to;
                    }
                }
                foreach (var footer in section.Footer) { layout.Texts.Add(new() { Tag = footer.Tag, Text = footer.Text, Y = y + 35 }); y += 48; }
                y += 60;
            }
            layout.Height = checked((int)Math.Ceiling(y + 30)); return layout;
        }
        private static void Text(SKCanvas c, string value, float x, float y, SKFont font, SKPaint ink, int align)
        { float w = font.MeasureText(value); c.DrawText(value, x - (align == 0 ? w / 2 : align == 1 ? w : 0), y, font, ink); }
        private static int SlotOrder(Note n) => n.Bar ? n.Ordinal == 0 ? -10000 : 10000 : n.Invisible ? n.Ordinal + 1 : 0;
        private static float TopSpace(List<Note> notes, Section section) => notes.Max(n => 72 + Math.Max(0, n.Octave) * 10 + n.Height * 8 + n.Marks.Count * 40 +
            new[] { n.Dynamics, section.HideChords ? "" : n.Chord, n.Key, n.Beat, n.Comment }.Count(t => t.Length > 0) * 39);
        private static void DrawNote(SKCanvas c, Note n, SKFont font, SKFont text, SKPaint ink, SKPaint line)
        {
            if (n.Bar)
            {
                if (n.Symbol == "|/") return;
                c.DrawLine(n.X, n.Y - Size, n.X, n.Y + 20, line);
                if (n.Symbol != "|") c.DrawLine(n.X + 8, n.Y - Size, n.X + 8, n.Y + 20, line);
                if (n.Symbol == "|||") { float width = line.StrokeWidth; line.StrokeWidth = 6; c.DrawLine(n.X + 10, n.Y - Size, n.X + 10, n.Y + 20, line); line.StrokeWidth = width; }
                if (n.Symbol.StartsWith(':')) { c.DrawCircle(n.X - 12, n.Y - 25, 3.4f, ink); c.DrawCircle(n.X - 12, n.Y - 7, 3.4f, ink); }
                if (n.Symbol.EndsWith(':')) { c.DrawCircle(n.X + 20, n.Y - 25, 3.4f, ink); c.DrawCircle(n.X + 20, n.Y - 7, 3.4f, ink); }
                return;
            }
            if (n.Invisible) return;
            if (n.Symbol == "-") c.DrawLine(n.X - 12, n.Y - 16, n.X + 12, n.Y - 16, line); else Text(c, n.Symbol, n.X, n.Y, font, ink, 0);
            if (n.Accidental.Length > 0) Accidental(c, n.Accidental, n.X - 30, n.Y - 20, ink, line);
            for (int i = 0; i < n.Dots; i++) c.DrawCircle(n.X + 23 + i * 10, n.Y - 15, 3, ink);
            for (int i = 0; i < Math.Abs(n.Octave); i++) c.DrawCircle(n.X, n.Octave > 0 ? n.Y - 52 - i * 10 : n.Y + 16 + n.Beams * 8 + i * 10, 3, ink);
        }
        private static void Accidental(SKCanvas c, string value, float x, float y, SKPaint ink, SKPaint line)
        {
            // 音乐升降号不用普通中文字体，避免系统缺字显示方框。
            if (value == "𝄫") { Accidental(c, "♭", x - 6, y, ink, line); Accidental(c, "♭", x + 6, y, ink, line); }
            else if (value == "𝄪") { c.DrawLine(x - 9, y - 14, x + 9, y + 10, line); c.DrawLine(x + 9, y - 14, x - 9, y + 10, line); }
            else if (value.Contains('♭')) { c.DrawLine(x - 4, y - 25, x - 4, y + 12, line); using var p = new SKPath(); p.MoveTo(x - 4, y + 12); p.CubicTo(x + 19, y - 3, x + 8, y - 15, x - 4, y - 3); c.DrawPath(p, line); }
            else if (value.Contains('♮')) { c.DrawLine(x - 5, y - 23, x - 5, y + 6, line); c.DrawLine(x + 5, y - 9, x + 5, y + 22, line); c.DrawLine(x - 5, y - 7, x + 5, y - 11, line); c.DrawLine(x - 5, y + 8, x + 5, y + 4, line); }
            else { c.DrawLine(x - 6, y - 23, x - 8, y + 15, line); c.DrawLine(x + 6, y - 25, x + 4, y + 13, line); c.DrawLine(x - 13, y - 8, x + 13, y - 13, line); c.DrawLine(x - 13, y + 7, x + 13, y + 2, line); }
        }
        private static void DrawAnnotations(SKCanvas c, Note n, Section section, SKFont font, SKPaint ink, SKPaint line)
        {
            float y = n.Y - 72 - Math.Max(0, n.Octave) * 10 - n.Height * 8;
            foreach (var mark in n.Marks)
            {
                if (mark == "yc") { c.DrawArc(new SKRect(n.X - 20, y - 18, n.X + 20, y + 17), 180, 180, false, line); c.DrawCircle(n.X, y + 4, 3, ink); }
                else if (mark == "bc") c.DrawLine(n.X - 16, y, n.X + 16, y, line);
                else if (mark == "zy") { c.DrawLine(n.X - 16, y - 8, n.X + 16, y, line); c.DrawLine(n.X - 16, y + 8, n.X + 16, y, line); }
                else if (mark == "dy") { using var p = new SKPath(); p.MoveTo(n.X - 5, y - 10); p.LineTo(n.X + 5, y - 10); p.LineTo(n.X, y + 8); p.Close(); c.DrawPath(p, ink); }
                else if (mark is "shy" or "xhy") c.DrawLine(n.X - 18, mark == "shy" ? y + 10 : y - 10, n.X + 18, mark == "shy" ? y - 10 : y + 10, line);
                else if (mark is "sby" or "sby+" or "xby" or "xby+")
                { using var wave = new SKPath(); wave.MoveTo(n.X - 24, y); for (int i = 0; i < 4; i++) wave.LineTo(n.X - 16 + i * 12, y + (i % 2 == 0 ? -8 : 8)); c.DrawPath(wave, line); if (mark.StartsWith("x") || mark.EndsWith('+')) c.DrawLine(n.X, y - 17, n.X, y + 17, line); }
                else if (mark == "ty") { c.DrawCircle(n.X, y, 14, line); c.DrawLine(n.X - 22, y, n.X + 22, y, line); c.DrawLine(n.X, y - 22, n.X, y + 22, line); }
                else if (mark == "hs") { using var s = new SKPath(); s.MoveTo(n.X + 11, y - 14); s.CubicTo(n.X - 17, y - 24, n.X - 18, y - 2, n.X, y); s.CubicTo(n.X + 18, y + 2, n.X + 17, y + 24, n.X - 11, y + 14); c.DrawPath(s, line); c.DrawLine(n.X - 18, y + 19, n.X + 18, y - 19, line); c.DrawCircle(n.X - 17, y - 8, 2.5f, ink); c.DrawCircle(n.X + 17, y + 8, 2.5f, ink); }
                else Text(c, mark switch { "hx" => "’", "cy" or "cy+" => "tr" + (mark.EndsWith('+') ? "+" : ""), "ds" => "D.S.", "dc" => "D.C.", "fine" => "Fine", "zkh" => "(", "ykh" => ")", _ => mark }, n.X, y + 6, font, ink, 0);
                y -= 40;
            }
            foreach (var annotation in new[] { n.Dynamics, section.HideChords ? "" : n.Chord, n.Key, n.HideBeat ? "" : n.Beat, n.Comment })
                if (annotation.Length > 0) { Text(c, annotation, n.X, y, font, ink, 0); y -= 39; }
            if (n.Repeat.Length > 0) Text(c, n.Repeat, n.X, n.Y + 68, font, ink, 0);
            if (n.Grace.Count > 0)
            {
                using var small = new SKFont(font.Typeface, 25); float x = n.GraceAfter ? n.X + 32 : n.X - n.Grace.Count * 32;
                foreach (var grace in n.Grace) { grace.X = x; grace.Y = n.Y - 68; DrawNote(c, grace, small, small, ink, line); for (int i = 0; i < Math.Max(1, grace.Beams); i++) c.DrawLine(x - 9, grace.Y + 6 + i * 6, x + 9, grace.Y + 6 + i * 6, line); x += 30; }
            }
        }
        private static void Beams(SKCanvas c, Line row, SKPaint line)
        {
            int beats = row.Section.Beats, type = row.Section.BeatType; double elapsed = 0;
            for (int level = 1; level <= 5; level++)
            {
                Note start = null, previous = null; elapsed = 0;
                void Finish() { if (start != null) c.DrawLine(start.X - 12, start.Y + level * 8, previous.X + 12, previous.Y + level * 8, line); start = null; }
                foreach (var n in row.Notes)
                {
                    beats = n.BeatCount; type = n.BeatUnit;
                    double unit = type >= 8 && beats % 3 == 0 ? 12.0 / type : 4.0 / type;
                    if (n.Bar) { Finish(); if (!n.NoSplit) elapsed = 0; previous = null; continue; }
                    if (n.Beams < level || n.Invisible) { Finish(); previous = n; elapsed += n.Duration.Value; continue; }
                    if (start != null && previous?.BeamTag != '~' && (previous?.BeamTag == '^' || Math.Floor((elapsed + .000001) / unit) > Math.Floor((elapsed - previous.Duration.Value + .000001) / unit))) Finish();
                    start ??= n; previous = n; elapsed += n.Duration.Value; if (n.BeamTag == '^') Finish();
                }
                Finish();
            }
            Note hairpin = null;
            foreach (var n in row.Notes)
            {
                if ((n.StopHairpin || n.Bar || n == row.Notes[^1]) && hairpin != null)
                { float x1 = hairpin.X, x2 = Math.Max(x1 + 25, n.X), y = row.Baseline - 100; c.DrawLine(x1, y + (hairpin.Hairpin == '<' ? 0 : -8), x2, y + (hairpin.Hairpin == '<' ? -8 : 0), line); c.DrawLine(x1, y + (hairpin.Hairpin == '<' ? 0 : 8), x2, y + (hairpin.Hairpin == '<' ? 8 : 0), line); hairpin = null; }
                if (n.Hairpin != '\0') hairpin = n;
            }
        }
    }
}
