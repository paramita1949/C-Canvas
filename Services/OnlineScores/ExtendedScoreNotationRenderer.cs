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
    // 已实测的上游旧谱非语义残留给出诊断；未知音乐标记仍拒绝，不把多声部串成单声部。
    internal static class ExtendedScoreNotationRenderer
    {
        private const int Width = 1800;
        private const float Left = 105, Right = 80, Size = 43, LyricSize = 31;
        // 与1920投影图片的86.4px标题、63.36px正文一致；旧单图入口保留原参数。
        internal const float ProjectionTitleSize = 81, ProjectionBodySize = 59.4f, ProjectionTextSize = 54;
        private const float ProjectionLyricStep = 72, ProjectionLyricOffset = 87;
        private static readonly Regex Token = new(@"\G(?<open>(?:\(y\d*:|\(y|\()*)(?<note>:\|:|:\||\|:|\|\|\||\|\||\|/|\||[0-9X\-])(?<tail>(?:[^0-9:(|{\-X"" ]|\s|""[^""]*"")*)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly HashSet<string> Marks = new(StringComparer.Ordinal)
        { "yc", "bc", "zy", "dy", "hx", "shy", "xhy", "sby", "sby+", "xby", "xby+", "cy", "cy+", "pp", "p", "mp", "mf", "f", "ff", "fff", "ppp", "sf", "fp", "sfp", "sfz", "ds", "dc", "ty", "hs", "fine", "zkh", "ykh" };
        private static readonly string[] MarkNames = Marks.OrderByDescending(m => m.Length).ThenBy(m => m, StringComparer.Ordinal).ToArray();
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
            public Cell Copy() => (Cell)MemberwiseClone();
            public string Text = "", Head = "";
            public bool Underline, LeftAligned; public int HoldCells;
            public float Scale = 1, HeadGap;
            public List<string> Lines => Text == "|" ? new List<string> { "|" } : Text.Replace('/', ' ').Replace('@', ' ').Split('|').ToList();
        }
        // 同一次排版/绘制共用字体和量字口径；不保存全局字体或谱文，避免切歌后累积缓存。
        private sealed class TextMetrics : IDisposable
        {
            private readonly SKTypeface face;
            private readonly Dictionary<float, SKFont> fonts = new();
            private readonly Dictionary<(string Text, float Size), float> widths = new();
            private readonly Dictionary<(Cell Cell, float Size, float Width), List<string>> wraps = new();
            public TextMetrics(SKTypeface face) => this.face = face;
            public SKFont Font(float size)
            {
                if (!fonts.TryGetValue(size, out var font)) fonts[size] = font = new SKFont(face, size);
                return font;
            }
            public float Measure(string text, float size)
            {
                var key = (text, size);
                if (!widths.TryGetValue(key, out float width)) widths[key] = width = Font(size).MeasureText(text);
                return width;
            }
            public List<string> Wrap(Cell cell, float size, float width)
            {
                var key = (cell, size, width);
                if (!wraps.TryGetValue(key, out var lines))
                    wraps[key] = lines = WrapCell(cell, Font(size), width, this);
                return lines;
            }
            public void Dispose() { foreach (var font in fonts.Values) font.Dispose(); }
        }
        private sealed class Verse { public string Label = ""; public float LabelGap; public int? No, ToNo; public List<Cell> Cells = new(); public Verse Copy() => (Verse)MemberwiseClone(); }
        private sealed class Note
        {
            public Note Copy() => (Note)MemberwiseClone();
            public string Symbol, Accidental = "", Beat = "", Chord = "", Comment = "", Dynamics = "", Repeat = "", Key = "";
            public List<string> Marks = new(); public List<Note> Grace = new(); public bool GraceAfter;
            public int Octave, Dots, Beams, Height, Measure, Ordinal, Voice, BeatCount = 4, BeatUnit = 4, RowIndex = -1, LyricIndex = -1;
            public float X, Y;
            public bool NoSplit, StopHairpin, HideBeat, PlayStart, Accompaniment;
            public char BeamTag, Hairpin;
            public Time Duration = Time.Zero, Start = Time.Zero;
            public bool Bar => Symbol.Contains('|');
            public bool Syllable => Symbol is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "X" || ((Symbol is "0" or "-") && LyricIndex >= 0);
            public bool Invisible => Symbol is "8" or "9";
        }
        private sealed class Voice { public int Id, SharedStart = -1; public string Header = "", WidthTag = ""; public List<Note> Notes = new(); public List<Verse> Lyrics = new(); public Note Anchor; public bool Hidden; public Voice Copy() => (Voice)MemberwiseClone(); }
        private sealed class PageBreak { }
        private sealed class FreeText { public string Text; public float Offset; }
        private sealed class TemporarySegment { public Voice Voice; public Note Anchor; public bool Below, Accompaniment; public char Bracket; public int Height; }
        private sealed class SystemRow { public List<Voice> Voices = new(); public List<TemporarySegment> Segments = new(); public Note VoiceStart; public bool IsChorus; }
        private sealed class Section
        {
            public Section Copy() => (Section)MemberwiseClone();
            public string Key = "", Beat = "", Tempo = ""; public int Beats = 4, BeatType = 4;
            public List<(string Tag, string Text)> Header = new(), Footer = new();
            public Dictionary<string, float> TextOffsets = new(StringComparer.Ordinal);
            public Dictionary<int, int?> Octaves = new();
            public List<object> Blocks = new(); public bool HideChords, HidePunctuation;
            public float AllScale = 1, QuSpace = 30, LeftMargin = Left, RightMargin = Left, TopMargin = 30, BottomMargin = 30;
            public Dictionary<string, float> FontRatios = new(StringComparer.OrdinalIgnoreCase);
            public float Ratio(string tag, float defaultRatio = 1) => AllScale * (FontRatios.TryGetValue(tag, out float ratio) ? ratio / defaultRatio : 1);
        }
        private sealed record Arc(Note Start, Note End, int Depth, int Tuplet);
        private sealed record Ending(Note Start, Note End, string Label, bool Closed, int Height);
        private sealed class Score { public List<Section> Sections = new(); public List<Arc> Arcs = new(); public List<Ending> Endings = new(); public List<string> Diagnostics = new(); }
        private sealed class Line { public Voice Voice; public Section Section; public List<Note> Notes; public float Baseline, Bottom, ExtraBelow, LabelRight, NoteScale = 1; public int From, To; public TemporarySegment Segment; }
        private sealed class PositionedText { public string Tag, Text; public float Y, XOffset; public Section Signature, Owner; }
        private sealed class Layout { public List<Line> Lines = new(); public List<PositionedText> Texts = new(); public List<(float Top, float Bottom)> Bands = new(); public List<int> PageBreaks = new(); public List<int> ChorusBands = new(); public int Height; }
        private static ScoreIntegrationException Error(int line, string reason) => new($"结构化歌谱第 {line} 行：{reason}；原始谱文保留，未忽略音乐内容。");
        // 只保留最近使用的解析模板，不把整本谱或Skia资源常驻；模板从不参与排版和逐节裁选。
        // 所有显示/编辑工作副本都深复制可变节点，并维持弧线/反复框/临时声部的音符身份关系。
        private sealed class Analysis
        {
            public string Source;
            public Score Template;
            public int[] VerseNumbers;
            public bool CanCombine;
        }
        private static readonly object AnalysisGate = new();
        private static Analysis latestAnalysis;
        private static Analysis GetAnalysis(string source, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (AnalysisGate)
            {
                ct.ThrowIfCancellationRequested();
                if (latestAnalysis != null && string.Equals(latestAnalysis.Source, source, StringComparison.Ordinal)) return latestAnalysis;
                var score = ParseCore(source, ct, 0);
                ct.ThrowIfCancellationRequested();
                return latestAnalysis = new Analysis { Source = source, Template = score, VerseNumbers = VerseNumbers(score), CanCombine = CanCombineScore(score) };
            }
        }
        private static Score CopyScore(Score template, CancellationToken ct)
        {
            var notes = new Dictionary<Note, Note>();
            Note CopyNote(Note source)
            {
                if (source == null) return null;
                if (notes.TryGetValue(source, out var existing)) return existing;
                ct.ThrowIfCancellationRequested();
                var result = source.Copy(); notes.Add(source, result);
                result.Marks = new(source.Marks); result.Grace = source.Grace.Select(CopyNote).ToList();
                return result;
            }
            Voice CopyVoice(Voice source)
            {
                var result = source.Copy(); result.Anchor = CopyNote(source.Anchor); result.Notes = source.Notes.Select(CopyNote).ToList();
                result.Lyrics = source.Lyrics.Select(v => { var copy = v.Copy(); copy.Cells = v.Cells.Select(c => c.Copy()).ToList(); return copy; }).ToList();
                return result;
            }
            var score = new Score { Diagnostics = new(template.Diagnostics) };
            foreach (var section in template.Sections)
            {
                ct.ThrowIfCancellationRequested();
                var copy = section.Copy();
                copy.Header = new(section.Header); copy.Footer = new(section.Footer);
                copy.TextOffsets = new(section.TextOffsets, section.TextOffsets.Comparer);
                copy.FontRatios = new(section.FontRatios, section.FontRatios.Comparer); copy.Octaves = new(section.Octaves);
                copy.Blocks = section.Blocks.Select(block => block is SystemRow row ? (object)new SystemRow
                {
                    VoiceStart = CopyNote(row.VoiceStart), Voices = row.Voices.Select(CopyVoice).ToList(),
                    Segments = row.Segments.Select(s => new TemporarySegment { Voice = CopyVoice(s.Voice), Anchor = CopyNote(s.Anchor),
                        Below = s.Below, Accompaniment = s.Accompaniment, Bracket = s.Bracket, Height = s.Height }).ToList()
                } : block is FreeText text ? new FreeText { Text = text.Text, Offset = text.Offset } : new PageBreak()).ToList();
                score.Sections.Add(copy);
            }
            score.Arcs = template.Arcs.Select(a => new Arc(CopyNote(a.Start), CopyNote(a.End), a.Depth, a.Tuplet)).ToList();
            score.Endings = template.Endings.Select(e => new Ending(CopyNote(e.Start), CopyNote(e.End), e.Label, e.Closed, e.Height)).ToList();
            return score;
        }
        public static bool CanRender(string source, out string reason)
        {
            try { GetAnalysis(source, CancellationToken.None); reason = null; return true; }
            catch (ScoreIntegrationException ex) { reason = ex.Message; return false; }
            catch (OverflowException) { reason = "谱文时值超出安全范围。"; return false; }
        }
        public static IReadOnlyList<string> GetDiagnostics(string source, CancellationToken ct) => GetAnalysis(source, ct).Template.Diagnostics.ToArray();
        private static Score Parse(string source, CancellationToken ct)
            => CopyScore(GetAnalysis(source, ct).Template, ct);
        private static Score ParseCore(string source, CancellationToken ct, int depth)
        {
            if (depth > 2) throw Error(1, "临时声部嵌套过多");
            if (string.IsNullOrWhiteSpace(source) || Encoding.UTF8.GetByteCount(source) > ScoreNotationRenderer.MaxSourceBytes) throw Error(1, "谱文为空或过大");
            var score = new Score(); var section = new Section(); score.Sections.Add(section);
            SystemRow system = null; Voice voice = null; int total = 0;
            var slurs = new Dictionary<int, Stack<(Note Note, int Depth, int Tuplet)>>();
            var endings = new Dictionary<int, (Note Note, string Label, int Height)>();
            void FinishSection(int line)
            {
                if (slurs.Values.Any(s => s.Any(a => a.Tuplet > 0))) throw Error(line, "多连音没有闭合，无法确定时值");
                if (slurs.Values.Any(s => s.Count != 0)) score.Diagnostics.Add($"第{line}行前有未闭合圆滑线：保留全部音符，仅绘制已有明确起止的连线，未补造终点。");
                foreach (var end in endings.Values)
                { var last = section.Blocks.OfType<SystemRow>().SelectMany(r => r.Voices).Where(v => v.Id == end.Note.Voice).SelectMany(v => v.Notes).LastOrDefault(); score.Endings.Add(new(end.Note, last, end.Label, false, end.Height)); }
                slurs.Clear(); endings.Clear();
            }
            var rawLines = source.Replace("\r", "").Split('\n');
            for (int index = 0; index < rawLines.Length; index++)
            {
                ct.ThrowIfCancellationRequested(); int line = index + 1; string value = rawLines[index].Trim();
                if (value.Length == 0 || value.StartsWith('#') || value.StartsWith("//")) continue;
                if (value.StartsWith('*'))
                {
                    // 上游标记放在下一条Q前才生效；放在C行之间不能拆开其所属旋律。
                    int next = index + 1; while (next < rawLines.Length && (string.IsNullOrWhiteSpace(rawLines[next]) || rawLines[next].TrimStart().StartsWith('#'))) next++;
                    if (next < rawLines.Length && Regex.IsMatch(rawLines[next].TrimStart(), @"^Q\d*(?:<[^>]*>)?[!+\-]*[:：]"))
                    { section.Blocks.Add(new PageBreak()); system = null; voice = null; }
                    continue;
                }
                if (value.StartsWith('-') || value.StartsWith('－') || value.StartsWith('—'))
                { FinishSection(line); section = new Section { Octaves = new Dictionary<int, int?>(section.Octaves) }; score.Sections.Add(section); system = null; voice = null; continue; }
                var signature = Regex.Match(value, @"^([16])=([#♯b♭]?[A-Ga-g][#♯b♭]?)(?:\s*(\d{1,2})/(\d{1,2}))?(.*)$");
                if (signature.Success)
                {
                    if (section.Key.Length != 0)
                    { score.Diagnostics.Add($"第{line}行重复调号 {value}：与诗歌本一致使用本曲段首个调号 {section.Key} {section.Beat}，原码保留。"); continue; }
                    section.Key = signature.Groups[1].Value + "=" + signature.Groups[2].Value.Replace("♭", "b").Replace("♯", "#");
                    if (signature.Groups[3].Success) { section.Beats = int.Parse(signature.Groups[3].Value); section.BeatType = int.Parse(signature.Groups[4].Value); ValidateBeat(section.Beats, section.BeatType, line); section.Beat = section.Beats + "/" + section.BeatType; }
                    section.Tempo = signature.Groups[5].Value.Trim(); continue;
                }
                int colon = value.IndexOfAny(new[] { ':', '：' });
                if (colon < 0)
                {
                    if (Regex.IsMatch(value, @"^[QC](?:\d|\s|<|$)")) throw Error(line, "音乐或歌词行缺少冒号");
                    score.Diagnostics.Add($"第{line}行谱外文字未进入谱面（诗歌本也不渲染）：{value}"); continue;
                }
                string tag = value[..colon].Trim(), content = value[(colon + 1)..].Trim();
                if (Regex.IsMatch(tag, @"^(T|Z|XL|XR|TL|TR)(?:<[^>]*>)?$"))
                { if (content.Length > 180) throw Error(line, "标题或说明过长"); var shift = Regex.Match(tag, @"<(-?\d+(?:\.\d+)?)>"); string baseTag = tag.Split('<')[0]; if (shift.Success) section.TextOffsets[baseTag + "|" + content] = float.Parse(shift.Groups[1].Value, CultureInfo.InvariantCulture); section.Header.Add((baseTag, content)); continue; }
                if (Regex.IsMatch(tag, @"^(BL|BC|BR)(?:<[^>]*>)?$")) { var shift = Regex.Match(tag, @"<(-?\d+(?:\.\d+)?)>"); if (shift.Success) section.TextOffsets[tag[..2] + "|" + content] = float.Parse(shift.Groups[1].Value, CultureInfo.InvariantCulture); section.Footer.Add((tag[..2], content)); continue; }
                if (Regex.IsMatch(tag, @"^J(?:16|[1248])?\.*$")) { section.Tempo = "速度 " + content; continue; }
                if (Regex.IsMatch(tag, @"^O\d*$")) { bool auto = content.Equals("auto", StringComparison.OrdinalIgnoreCase); if (!auto && !int.TryParse(content, out _)) throw Error(line, "无效音区定义"); int voiceId = tag.Length == 1 ? 0 : int.Parse(tag[1..]); if (auto && voiceId != 0) throw Error(line, "声部音区必须为整数"); section.Octaves.TryAdd(voiceId, auto ? null : int.Parse(content)); continue; }
                if (tag is "FontSize" or "Margin" or "Space")
                {
                    if (content.Length > 500) throw Error(line, "排版设置过长");
                    foreach (string setting in content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var item = Regex.Match(setting, tag == "FontSize" ? @"^([A-Za-z][A-Za-z0-9]*)\s*=\s*(\d+(?:\.\d+)?)%$" : @"^([A-Za-z]+)\s*=\s*(-?\d+(?:\.\d+)?)$", RegexOptions.CultureInvariant);
                        if (!item.Success || !float.TryParse(item.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float amount) || !float.IsFinite(amount)) throw Error(line, "无效排版设置 " + setting);
                        string key = item.Groups[1].Value.ToLowerInvariant();
                        if (tag == "FontSize")
                        {
                            if (!new[] { "all", "t", "t2", "x", "z", "c", "q", "w", "hx", "zs", "pd", "tl", "tr", "bl", "bc", "br" }.Contains(key)) throw Error(line, "未知字号项 " + key);
                            float ratio = amount / 100; if (ratio <= 0 || ratio > 10) throw Error(line, "字号比例超出范围");
                            if (key == "all") section.AllScale *= ratio; else section.FontRatios[key] = ratio;
                        }
                        else if (tag == "Space") { if (key != "qu") throw Error(line, "未知间距项 " + key); section.QuSpace = amount; }
                        else
                        {
                            if (amount < 0 || amount > 1000) throw Error(line, "边距超出范围");
                            switch (key) { case "left": section.LeftMargin = amount * 1.8f; break; case "right": section.RightMargin = amount * 1.8f; break; case "top": section.TopMargin = amount; break; case "bottom": section.BottomMargin = amount; break; default: throw Error(line, "未知边距项 " + key); }
                        }
                    }
                    if (section.LeftMargin + section.RightMargin > Width - 100 || section.AllScale > 10 || section.FontRatios.Values.Any(r => r * section.AllScale > 10) || Math.Abs(section.QuSpace) > 500) throw Error(line, "排版设置超出可显示范围");
                    continue;
                }
                if (tag == "Off")
                { foreach (var off in content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) { if (off == "hx") section.HideChords = true; else if (off == "bd") section.HidePunctuation = true; else throw Error(line, "未知隐藏项 " + off); } continue; }
                if (Regex.IsMatch(tag, @"^W(?:<[^>]*>)?$"))
                {
                    float shift = 0; if (tag.Contains('<') && !float.TryParse(tag[(tag.IndexOf('<') + 1)..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out shift)) throw Error(line, "无效文字行偏移");
                    section.Blocks.Add(new FreeText { Text = content.Replace('/', ' ').Replace('@', ' '), Offset = shift }); system = null; voice = null; continue;
                }
                var q = Regex.Match(tag, @"^Q(\d*)(?:<([^>]*)>)?([!+\-]*)$");
                if (q.Success)
                {
                    int id = q.Groups[1].Value.Length == 0 ? 1 : int.Parse(q.Groups[1].Value); if (id < 1 || id > 16) throw Error(line, "声部编号超出范围");
                    if (id > 1 && system == null) throw Error(line, "附加声部没有对应的 Q1 主声部");
                    if (system == null || q.Groups[1].Value.Length == 0 || id == 1 || system.Voices.Any(v => v.Id == id)) { system = new SystemRow(); section.Blocks.Add(system); }
                    voice = new Voice { Id = id, Header = q.Groups[2].Value, WidthTag = q.Groups[3].Value, Hidden = q.Groups[3].Value.Contains('+') }; system.Voices.Add(voice);
                    if (!slurs.TryGetValue(id, out var stack)) slurs[id] = stack = new();
                    int offset = 0, lyricIndex = 0; bool accompaniment = false, middleStart = false;
                    var pendingSegments = new List<TemporarySegment>();
                    while (offset < content.Length)
                    {
                        while (offset < content.Length && char.IsWhiteSpace(content[offset])) offset++;
                        if (offset == content.Length) break;
                        // 网站全局 qPattern 会跳过旧谱多写的行前 Q:、冒号、单引号。
                        // 仅兼容实测残留，不全局跳过未知音符，以免掩盖损坏的音乐内容。
                        if (voice.Notes.Count == 0)
                        {
                            var residue = Regex.Match(content[offset..], @"^(?:Q[:：]|[:：']+)\s*");
                            if (residue.Success)
                            { score.Diagnostics.Add($"第{line}行多写的行前标记 {residue.Value.Trim()}：按诗歌本提取后续音符，原码保留。"); offset += residue.Length; continue; }
                        }
                        if (content[offset] == '{')
                        {
                            var head = Regex.Match(content[offset..], @"^\{(bz\.?|dsb[<>.]?)(\+\d+:|\+*)");
                            if (!head.Success) { middleStart = true; offset++; continue; }
                            int close = content.IndexOf('}', offset + head.Length); if (close < 0) throw Error(line, "临时声部未闭合");
                            string fragment = content.Substring(offset + head.Length, close - offset - head.Length).Trim();
                            Score inner;
                            try { inner = ParseCore("Q:" + fragment, ct, depth + 1); } catch (ScoreIntegrationException ex) { throw Error(line, "临时声部：" + ex.Message); }
                            var voices = inner.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).SelectMany(s => s.Voices).ToList();
                            if (voices.Count != 1) throw Error(line, "临时声部不能包含独立曲段");
                            var tempVoice = voices[0]; int temporaryId = 1000 + system.Segments.Count + id * 100;
                            foreach (var n in tempVoice.Notes) n.Voice = temporaryId;
                            tempVoice.Id = temporaryId;
                            string kind = head.Groups[1].Value, spacing = head.Groups[2].Value;
                            var segment = new TemporarySegment { Voice = tempVoice, Below = kind == "bz.", Accompaniment = kind.StartsWith("bz"), Bracket = kind.EndsWith('<') ? '<' : kind.EndsWith('>') ? '>' : kind == "dsb" ? '=' : '\0', Height = spacing.EndsWith(':') ? int.Parse(spacing[1..^1]) : spacing.Length };
                            pendingSegments.Add(segment); system.Segments.Add(segment); score.Arcs.AddRange(inner.Arcs); score.Endings.AddRange(inner.Endings);
                            offset = close + 1; continue;
                        }
                        // 全局qPattern跳过行首孤立的强断线符；此处没有前音符可供连接。
                        if (voice.Notes.Count == 0 && content[offset] == '^')
                        { score.Diagnostics.Add($"第{line}行行首孤立强断线符：按诗歌本保留后续音符，原码保留。"); offset++; continue; }
                        // 实测旧谱把第二个hx写在引号外。网站只读取第一个有效注释；
                        // 不补造第二个和弦，不改原文，也不放宽对未知音乐语法的检查。
                        var brokenChord = Regex.Match(content[offset..], "^(?<before>[^\\\"\\r\\n]*\\\"hx:[^\\\"\\r\\n]*\\\")hx:[^\\\"\\r\\n]*\\\"\\\"");
                        if (brokenChord.Success)
                        {
                            content = content[..offset] + brokenChord.Groups["before"].Value + content[(offset + brokenChord.Length)..];
                            score.Diagnostics.Add($"第{line}行引号外重复和弦标签：按诗歌本仅使用有效引号内注释，完整原码保留。");
                        }
                        var token = Token.Match(content, offset);
                        if (!token.Success || token.Index != offset || token.Length == 0) throw Error(line, "无法识别的音符附近：" + content.Substring(offset, Math.Min(24, content.Length - offset)));
                        offset += token.Length; var note = new Note { Symbol = token.Groups["note"].Value, Voice = id };
                        string tail = token.Groups["tail"].Value.Trim().Replace('．', '.');
                        ParseTail(note, ref tail, line);
                        if (Regex.IsMatch(token.Groups["tail"].Value, "\"bz:\"")) accompaniment = true;
                        if (Regex.IsMatch(token.Groups["tail"].Value, "\"bz:!\"")) accompaniment = false;
                        note.Accompaniment = accompaniment;
                        foreach (var pending in pendingSegments) pending.Anchor = note;
                        pendingSegments.Clear();
                        if (middleStart) { system.VoiceStart = note; middleStart = false; }
                        if (id > 1 && voice.Anchor == null && system.VoiceStart != null) voice.Anchor = system.VoiceStart;
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
                        note.PlayStart = tail.Contains('*');
                        tail = Regex.Replace(tail, @"[\[\]]/?|«[^»]*»", "").Replace("+", "").Replace(")", "").Replace("*", "");
                        var legacyResidue = Regex.Matches(tail, "[ay·…]+|s(?=d)|(?<=#)w");
                        if (legacyResidue.Count > 0)
                        { score.Diagnostics.Add($"第{line}行音符 {note.Symbol} 的旧谱残留 {string.Join("、", legacyResidue.Select(m => m.Value))}：网站没有赋予音乐语义，保持已识别音区、减时线和附点，原码保留。"); tail = Regex.Replace(tail, "[ay·…]+|s(?=d)|(?<=#)w", ""); }
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
                    if (pendingSegments.Count > 0) throw Error(line, "临时声部后缺少主旋律锚点");
                    if (voice.Notes.Count == 0) throw Error(line, "空声部"); continue;
                }
                if (Regex.IsMatch(tag, @"^C\d*(?:-\d+)?$"))
                {
                    if (voice == null || voice.Lyrics.Count >= 16) throw Error(line, "歌词没有对应声部或过多");
                    var verse = ParseLyrics(content, line, section.HidePunctuation);
                    var range = Regex.Match(tag, @"^C(\d+)(?:-(\d+))?$");
                    if (range.Success)
                    {
                        verse.No = int.Parse(range.Groups[1].Value); verse.ToNo = range.Groups[2].Success ? int.Parse(range.Groups[2].Value) : verse.No;
                        if (verse.No < 1 || verse.ToNo < verse.No || verse.ToNo - verse.No > 128) throw Error(line, "歌词节号范围无效");
                        if (verse.Label.Length == 0) verse.Label = verse.No + ".";
                    }
                    int available = voice.Notes.Count(n => n.LyricIndex >= 0);
                    if (verse.Cells.Count > available)
                    {
                        // 网站按音符 indexInLine 对位，超出部分没有锚点，不画也不补造音符。
                        // Cells 完整保留；编辑器提示具体尾词，避免把“能渲染”误报成“谱文无误”。
                        string unmatched = string.Concat(verse.Cells.Skip(available).Select(c => c.Head + c.Text));
                        score.Diagnostics.Add($"第{line}行歌词 {verse.Cells.Count} 个音节，对应 {available} 个音符；尾部「{unmatched}」没有音符锚点，按诗歌本不绘制，完整原码及歌词单元保留供编辑。");
                    }
                    var chorus = voice.Notes.FirstOrDefault(n => n.Comment.Contains("副歌") && n.LyricIndex >= 0);
                    if (chorus != null && voice.Notes.IndexOf(chorus) > 0) voice.SharedStart = chorus.LyricIndex;
                    voice.Lyrics.Add(verse); continue;
                }
                if (Regex.IsMatch(tag, @"^[QC](?:\d|<|$)")) throw Error(line, "未知音乐行类型 " + tag);
                score.Diagnostics.Add($"第{line}行谱外标签 {tag} 未进入谱面（诗歌本也不渲染），原码保留。");
            }
            FinishSection(rawLines.Length);
            if (!score.Sections.Any(s => s.Blocks.OfType<SystemRow>().Any())) throw Error(1, "没有音符");
            foreach (var arc in score.Arcs.Where(a => a.Tuplet > 0))
            {
                var all = score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).SelectMany(s => s.Voices.Concat(s.Segments.Select(t => t.Voice))).Where(v => v.Id == arc.Start.Voice).SelectMany(v => v.Notes).ToList();
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
                string tag = a.Groups[1].Value, text = a.Groups[3].Value;
                string plus = a.Groups[2].Value; int height = plus.Length > 1 && char.IsDigit(plus[1]) ? int.Parse(plus[1..]) : plus.Length;
                n.Height = Math.Max(n.Height, Math.Min(24, height));
                switch (tag)
                {
                    case "hx": n.Chord = NormalizeChord(text); break;
                    case "d": n.Key = (text == text.ToLowerInvariant() ? "6=" : "1=") + text; break;
                    case "p": case "p/":
                        var beat = Regex.Match(text, @"^(\d{1,2})/(\d{1,2})$"); if (!beat.Success) throw Error(line, "无效变拍号"); ValidateBeat(int.Parse(beat.Groups[1].Value), int.Parse(beat.Groups[2].Value), line); n.Beat = text; n.HideBeat = tag == "p/"; break;
                    case "yy": case "hyy":
                        if (text.Length > 100) throw Error(line, "倚音过长"); n.GraceAfter = tag == "hyy"; n.Grace = ParseGrace(text, line); break;
                    case "ld": case "sd": n.Dynamics = text.TrimEnd('!'); n.StopHairpin |= text.Contains('!'); if (text.Contains('<')) n.Hairpin = '<'; else if (text.Contains('>')) n.Hairpin = '>'; break;
                    case "ff": case "ffs": case "ffcs": n.Repeat = text; break;
                    case "yc": n.Marks.Add("yc"); break;
                    case "bz": n.Accompaniment = text != "!"; break;
                    case "": n.Comment = text; break;
                    default: throw Error(line, "未实现的音乐注释 " + tag);
                }
            }
            // 必须按已知标记最长匹配，不能把&zyg的g吞入mark；+只有sby+/xby+/cy+属于名字。
            var suffix = new StringBuilder();
            for (int i = 0; i < remaining.Length;)
            {
                if (remaining[i] != '&') { suffix.Append(remaining[i++]); continue; }
                int at = ++i;
                if (i < remaining.Length && remaining[i] == '+')
                { while (i < remaining.Length && remaining[i] == '+') i++; n.Height += Math.Min(24, i - at); continue; }
                string mark = MarkNames.FirstOrDefault(m => remaining.AsSpan(i).StartsWith(m, StringComparison.Ordinal));
                if (mark == null) throw Error(line, "未知音乐符号 " + remaining[(at - 1)..]);
                n.Marks.Add(mark); i += mark.Length;
                while (i < remaining.Length && remaining[i] == '+') { n.Height = Math.Min(24, n.Height + 1); i++; }
            }
            tail = suffix.ToString();
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
            var verse = new Verse(); string head = ""; float headGap = 0; bool underline = false; float smallScale = 1; bool hy = false;
            for (int i = 0; i < text.Length;)
            {
                char ch = text[i]; if (char.IsWhiteSpace(ch)) { i++; continue; }
                if (ch == '<')
                { int end = text.IndexOf('>', i + 1); if (end < 0) throw Error(line, "歌词标签未闭合"); string h = text[(i + 1)..end]; var gap = Regex.Match(h, @"%(-?\d+(?:\.\d+)?)$"); float spacing = gap.Success ? float.Parse(gap.Groups[1].Value, CultureInfo.InvariantCulture) / 100 : 0; if (gap.Success) h = h[..gap.Index]; if (verse.Cells.Count == 0) { verse.Label += h; verse.LabelGap = spacing; } else { head += h; headGap = spacing; } i = end + 1; continue; }
                if (ch == '[')
                {
                    int end = text.IndexOf(']', i + 1); if (end < 0) throw Error(line, "歌词设置未闭合");
                    foreach (string tag in text[(i + 1)..end].Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (tag == "ul") underline = true; else if (tag == "ul!") underline = false;
                        else if (tag == "hy") hy = true; else if (tag == "hy!") hy = false;
                        else if (tag == "small") smallScale = .8f; else if (tag == "small!") smallScale = 1;
                        else if (tag.StartsWith("small:") && float.TryParse(tag[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out float size)) smallScale = Math.Clamp(size, .2f, 1.5f);
                        else if (tag.Length > 0 && tag.All(c => c == '/') && verse.Cells.Count > 0) verse.Cells[^1].HoldCells += tag.Length;
                        else throw Error(line, "未知歌词设置 " + tag);
                    }
                    i = end + 1; continue;
                }
                string cell; bool grouped = false;
                if (ch == '{') { grouped = true; int end = text.IndexOf('}', i + 1); if (end < 0) throw Error(line, "歌词分组未闭合"); cell = text[(i + 1)..end]; if (cell.Contains('{')) throw Error(line, "歌词分组嵌套"); i = end + 1; }
                else if (char.IsAsciiLetterOrDigit(ch)) { int start = i; while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '\'')) i++; cell = text[start..i]; }
                else if (ch == '-' && text.Any(char.IsAsciiLetter)) { cell = "-"; i++; }
                else if (ch is '/' or '@' or '|' || ch >= '\u3400' && ch <= '\u9fff' || ch >= '\uf900' && ch <= '\ufaff') { cell = ch.ToString(); i++; }
                else if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { cell = text.Substring(i, 2); i += 2; }
                else if (char.IsPunctuation(ch) || char.GetUnicodeCategory(ch) == UnicodeCategory.OtherNumber)
                { if (!noPunctuation) { if (verse.Cells.Count == 0 || "“‘《（【([".Contains(ch)) head += ch; else verse.Cells[^1].Text += ch; } i++; continue; }
                else throw Error(line, "未知歌词字符 " + ch);
                if (cell.Length > 500) throw Error(line, "歌词分组过长"); verse.Cells.Add(new Cell { Text = cell, Head = head, HeadGap = headGap, Underline = underline, Scale = smallScale * (hy ? .8f : 1), LeftAligned = grouped && cell.Split('|').Any(s => s.Length > (s.Any(char.IsAsciiLetter) ? 4 : 2)) }); head = ""; headGap = 0;
            }
            if (head.Length > 0 && verse.Cells.Count > 0) verse.Cells[^1].Text += head;
            return verse;
        }

        internal sealed class RecordedScore : IDisposable
        {
            internal readonly record struct VerseLabel(int Band, string Text, float Y, float Gap, float Size, float Right);
            public SKPicture Picture;
            public List<(float Top, float Bottom)> Bands;
            public List<VerseLabel> VerseLabels = new();
            public List<int> PageBreaks = new();
            public List<(int Start, int End)> ChorusRanges = new();
            public float HeaderHeight;
            public int Height;
            public void Dispose() => Picture?.Dispose();
        }
        public static bool CanCombine(string source, CancellationToken ct)
            => GetAnalysis(source, ct).CanCombine;
        private static bool CanCombineScore(Score score)
        {
            // C行必须挂在同一个Q声部下；分段的歌词、不同声部和节号本身不算同旋律多节歌词。
            foreach (var voice in score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).SelectMany(s => s.Voices))
            {
                int alignedCells = voice.Notes.Count(n => n.LyricIndex >= 0);
                if (voice.SharedStart >= 0) alignedCells = Math.Min(alignedCells, voice.SharedStart);
                var lyrics = voice.Lyrics.Select(v => string.Concat(v.Cells.Take(alignedCells).Select(c => c.Text))
                    .Replace("/", "").Replace("@", "").Trim()).Where(text => text.Length > 0);
                if (lyrics.Distinct(StringComparer.Ordinal).Take(2).Count() > 1) return true;
            }
            return false;
        }

        private static int[] VerseNumbers(Score score)
        {
            var voices = score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).SelectMany(s => s.Voices).ToList();
            var numbers = voices.SelectMany(v => v.Lyrics).Where(v => v.No.HasValue)
                .SelectMany(v => Enumerable.Range(v.No.Value, v.ToNo.GetValueOrDefault(v.No.Value) - v.No.Value + 1)).Distinct().OrderBy(n => n).ToArray();
            // 不带节号的多条C仍按本地旧文件逐条展示；单条C是所有节共享。
            int unnumbered = voices.Select(v => v.Lyrics.Count(l => !l.No.HasValue)).DefaultIfEmpty().Max();
            return numbers.Length > 0 ? numbers : Enumerable.Range(1, Math.Max(1, unnumbered)).ToArray();
        }
        internal static int VerseCount(string source, CancellationToken ct) => GetAnalysis(source, ct).VerseNumbers.Length;
        internal static string ExtractLyrics(string source)
        {
            var score = GetAnalysis(source, CancellationToken.None).Template;
            var rows = score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).ToArray();
            var numbers = VerseNumbers(score); var verses = new List<string>();
            for (int index = 0; index < numbers.Length; index++)
            {
                var lines = new List<string>(); string heading = null;
                foreach (var row in rows)
                {
                    var texts = new List<string>();
                    foreach (var voice in row.Voices)
                    {
                        var selected = voice.Lyrics.Any(v => v.No.HasValue)
                            ? voice.Lyrics.Where(v => !v.No.HasValue || numbers[index] >= v.No && numbers[index] <= v.ToNo)
                            : voice.Lyrics.Count > 1 ? voice.Lyrics.Skip(Math.Min(index, voice.Lyrics.Count - 1)).Take(1) : voice.Lyrics;
                        foreach (var verse in selected)
                        {
                            // Use parsed cells, including unanchored tail words; never extract via regex that drops grouping or English.
                            var body = new StringBuilder();
                            foreach (var cell in verse.Cells)
                            {
                                string word = cell.Head + (cell.Text is "/" or "@" or "|" ? "" : cell.Text.Replace("|", " "));
                                if (word.Length > 0 && body.Length > 0 && char.IsAsciiLetterOrDigit(body[^1]) && char.IsAsciiLetterOrDigit(word[0])) body.Append(' ');
                                body.Append(word);
                            }
                            string text = body.ToString();
                            if (string.IsNullOrWhiteSpace(text)) continue;
                            if (heading == null && verse.Label.Length > 0) heading = verse.Label;
                            if (!texts.Contains(text)) texts.Add(text); // Harmony voices may share the same lyric line.
                        }
                    }
                    if (texts.Count > 0) lines.Add(string.Join("\n", texts));
                }
                if (lines.Count > 0) verses.Add((heading ?? (numbers.Length > 1 ? numbers[index] + "." : "")) + string.Join("\n", lines));
            }
            return string.Join("\n\n", verses);
        }
        public static void RenderToPng(string source, string path, CancellationToken ct)
        {
            using var score = Record(source, null, ct, false);
            if ((long)score.Height * Width > 40_000_000) throw Error(1, "谱面超出图片尺寸限制");
            using var bitmap = new SKBitmap(Width, score.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White); canvas.DrawPicture(score.Picture);
            using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded == null) throw Error(1, "渲染文件失败");
            using var file = new FileStream(path, FileMode.CreateNew); encoded.SaveTo(file);
        }
        internal static RecordedScore Record(string source, int? verseIndex, CancellationToken ct, bool compact = true, SKColor? textColor = null, bool measureOnly = false)
        {
            var score = Parse(source, ct);
            // 在选节之前识别公共副歌，避免C1..C4被筛成单节后丢失段落身份。
            // 来源是解析节点而非原始字符串切割；副歌中的全部实际换行band共享保护范围。
            foreach (var section in score.Sections)
            {
                bool numberedSeen = false, chorus = false, explicitChorusActive = false;
                foreach (var row in section.Blocks.OfType<SystemRow>())
                {
                    ct.ThrowIfCancellationRequested();
                    bool numbered = row.Voices.Any(v => v.Lyrics.Any(l => l.No.HasValue));
                    bool common = row.Voices.Any(v => v.Lyrics.Any(l => !l.No.HasValue && l.Cells.Any(c => c.Text.Length > 0)));
                    bool explicitChorus = row.Voices.Any(v => v.Notes.Any(n => n.Comment.Contains("副歌")));
                    if (row.Voices.Any(v => v.Notes.Any(n => n.Comment.Contains("主歌"))))
                    { chorus = false; explicitChorusActive = false; numberedSeen = false; }
                    if (numbered && !explicitChorusActive) chorus = false;
                    if (explicitChorus) { chorus = true; explicitChorusActive = true; }
                    if (numberedSeen && common && !numbered) chorus = true;
                    row.IsChorus = chorus;
                    numberedSeen |= numbered;
                }
            }
            if (verseIndex.HasValue)
            {
                var verseNumbers = VerseNumbers(score); if (verseIndex.Value < 0 || verseIndex.Value >= verseNumbers.Length) throw Error(1, "歌词节索引无效");
                int number = verseNumbers[verseIndex.Value];
                // 仅在整行明确限定其它歌词节时省略（如C4专属的阿们）。
                // 无词间奏、无节号公共段、多声部公共旋律及临时声部不属于此条件。
                foreach (var section in score.Sections)
                    section.Blocks.RemoveAll(block => block is SystemRow row && row.Segments.Count == 0 && row.Voices.Count > 0 &&
                        row.Voices.All(v => v.SharedStart < 0 && v.Lyrics.Count > 0 &&
                            v.Lyrics.All(l => l.No.HasValue && !(number >= l.No && number <= l.ToNo))));
                var retainedNotes = score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>())
                    .SelectMany(r => r.Voices.Concat(r.Segments.Select(t => t.Voice))).SelectMany(v => v.Notes).ToHashSet();
                // 连音线和反复框不能再指向本节未演唱的段落，保留其余完整音乐标记。
                score.Arcs.RemoveAll(a => !retainedNotes.Contains(a.Start) || !retainedNotes.Contains(a.End));
                score.Endings.RemoveAll(e => !retainedNotes.Contains(e.Start) || e.End != null && !retainedNotes.Contains(e.End));
                foreach (var voice in score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).SelectMany(s => s.Voices))
                    if (voice.Lyrics.Any(v => v.No.HasValue))
                    {
                        var original = voice.Lyrics;
                        var selected = voice.Lyrics.Where(v => !v.No.HasValue || number >= v.No && number <= v.ToNo).ToList();
                        if (voice.SharedStart >= 0 && original.Count > 0)
                        {
                            if (selected.Count == 0) selected.Add(new Verse { Cells = Enumerable.Range(0, voice.SharedStart).Select(_ => new Cell()).ToList() });
                            foreach (var verse in selected)
                            {
                                while (verse.Cells.Count < voice.SharedStart) verse.Cells.Add(new Cell());
                                verse.Cells = verse.Cells.Take(voice.SharedStart).Concat(original[0].Cells.Skip(voice.SharedStart)).ToList();
                            }
                        }
                        foreach (var verse in selected.Where(v => v.ToNo > v.No && v.Label == v.No + ".")) verse.Label = number + ".";
                        voice.Lyrics = selected; // 该节无词保留空旋律，不能套用别节的词。
                    }
                    else if (voice.Lyrics.Count > 1)
                        voice.Lyrics = new() { voice.Lyrics[Math.Min(verseIndex.Value, voice.Lyrics.Count - 1)] };
            }
            else
                foreach (var voice in score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).SelectMany(s => s.Voices).Where(v => v.SharedStart >= 0))
                    foreach (var verse in voice.Lyrics.Skip(1)) verse.Cells = verse.Cells.Take(voice.SharedStart).ToList();
            using var face = SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;
            using var titleFace = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyle.Bold) ?? SKTypeface.Default;
            using var numberFace = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
            using var words = new SKFont(face, compact ? ProjectionBodySize : LyricSize); using var numbers = new SKFont(numberFace, Size);
            using var annotations = new SKFont(face, compact ? 43.2f : LyricSize);
            using var metrics = new TextMetrics(face);
            var verseLabels = new List<RecordedScore.VerseLabel>();
            using var paint = new SKPaint { Color = textColor ?? SKColors.Black, IsAntialias = true };
            using var stroke = new SKPaint { Color = textColor ?? SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.4f };
            var layout = PositionCore(score, words, ct, compact, metrics);
            // 分页分析仅需要完整谱行边界；复用相同量字/排版，不绘制不会显示的音符和歌词。
            if (measureOnly) return CompleteRecording(layout, null, verseLabels, ct);
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0, 0, Width, layout.Height));
            foreach (var item in layout.Texts)
            {
                ct.ThrowIfCancellationRequested();
                using var font = new SKFont(compact && item.Tag == "T" ? titleFace : face,
                    compact ? item.Tag is "T" or "XL" or "XR" ? ProjectionTitleSize : item.Tag is "BL" or "BC" or "BR" ? 43.2f : ProjectionTextSize
                        : item.Tag == "T" ? 63 : item.Tag == "W" ? 32 : 31);
                if (item.Owner != null)
                {
                    string sizeTag = item.Signature != null ? "pd" : item.Tag is "XL" or "XR" ? "x" : item.Tag.ToLowerInvariant();
                    font.Size *= item.Owner.Ratio(sizeTag, item.Tag is "T" or "XL" or "XR" ? 1.5f : item.Tag is "Z" or "BL" or "BC" or "BR" ? .8f : 1);
                }
                if (item.Signature != null) { DrawSignature(canvas, item.Signature, item.Y, font, paint, stroke); continue; }
                float x = item.Tag is "T" or "T2" or "Z" or "BC" ? Width / 2f : item.Tag.EndsWith('R') ? Width - (item.Owner?.RightMargin ?? (compact ? Left : Right)) : item.Owner?.LeftMargin ?? Left;
                if (item.Owner != null && item.Owner.TextOffsets.TryGetValue(item.Tag + "|" + item.Text, out float offset)) x += (item.Tag.EndsWith('R') ? -1 : 1) * offset * font.Size;
                Text(canvas, item.Text, x + item.XOffset, item.Y, font, paint, item.Tag is "T" or "T2" or "Z" or "BC" ? 0 : item.Tag.EndsWith('R') ? 1 : -1);
            }
            foreach (var row in layout.Lines)
            {
                ct.ThrowIfCancellationRequested();
                float noteFactor = (compact ? ProjectionBodySize / Size : 1) * row.Section.Ratio("q", 1.1f) * (row.Segment?.Accompaniment == true ? .8f : 1);
                row.NoteScale = noteFactor;
                words.Size = (compact ? ProjectionBodySize : LyricSize) * row.Section.Ratio("c", 1.1f);
                annotations.Size = (compact ? 43.2f : LyricSize) * row.Section.Ratio("zs");
                foreach (var n in row.Notes)
                {
                    if (noteFactor != 1 || n.Accompaniment)
                    {
                        canvas.Save(); canvas.Translate(n.X, n.Y); canvas.Scale(noteFactor * (n.Accompaniment ? .8f : 1)); canvas.Translate(-n.X, -n.Y);
                        DrawNote(canvas, n, numbers, words, paint, stroke); canvas.Restore();
                    }
                    else DrawNote(canvas, n, numbers, words, paint, stroke);
                    DrawAnnotations(canvas, n, row.Section, annotations, paint, stroke);
                }
                Beams(canvas, row, stroke);
                float baseLyricStep = (compact ? ProjectionLyricStep : 40) * row.Section.Ratio("c", 1.1f);
                float lyricY = row.Baseline + (compact ? ProjectionLyricOffset : 72) + row.ExtraBelow + row.Notes.Max(n => n.Beams * 8 + Math.Max(0, -n.Octave) * 10) * Math.Max(1, noteFactor);
                foreach (var verse in row.Voice.Lyrics)
                {
                    float lyricStep = baseLyricStep * LyricLineScale(verse, row.Notes);
                    int maxLines = 1;
                    foreach (var n in row.Notes.Where(n => n.LyricIndex >= 0 && n.LyricIndex < verse.Cells.Count))
                    {
                        var cell = verse.Cells[n.LyricIndex]; if (cell.Text is "/" or "@" or "") continue;
                        var font = metrics.Font(words.Size * cell.Scale);
                        var lines = metrics.Wrap(cell, font.Size, 360); maxLines = Math.Max(maxLines, lines.Count);
                        for (int i = 0; i < lines.Count; i++)
                        { float y = lyricY + i * lyricStep; if (cell.LeftAligned) Text(canvas, lines[i], n.X - font.Size / 2, y, font, paint, -1); else if (compact) DrawProjectionLyric(canvas, lines[i], n.X, y, font, paint); else Text(canvas, lines[i], n.X, y, font, paint, 0); if (cell.Underline) { float start = cell.LeftAligned ? n.X - font.Size / 2 : n.X - font.MeasureText(lines[i]) / 2; canvas.DrawLine(start, y + 7, start + font.MeasureText(lines[i]), y + 7, stroke); } }
                        if (cell.HoldCells > 0)
                        { var end = row.Notes.LastOrDefault(e => e.LyricIndex >= n.LyricIndex && e.LyricIndex <= n.LyricIndex + cell.HoldCells) ?? n; canvas.DrawLine(n.X + font.MeasureText(lines[^1]) / 2 + 5, lyricY + 7, end.X + 12, lyricY + 7, stroke); }
                        if (cell.Head.Length > 0) Text(canvas, cell.Head, n.X - 25 - cell.HeadGap * font.Size, lyricY - 3, font, paint, 1);
                    }
                    if (compact)
                        verseLabels.Add(new(layout.Bands.FindIndex(b => row.Baseline >= b.Top && row.Baseline < b.Bottom), verse.Label, lyricY, verse.LabelGap, words.Size, row.LabelRight));
                    else Text(canvas, verse.Label, 22, lyricY, words, paint, -1);
                    lyricY += maxLines * lyricStep + (compact ? 0 : 8);
                }
                if (row.Segment != null && row.Segment.Bracket != '\0')
                {
                    float y1 = row.Baseline - 42 * noteFactor, y2 = row.Baseline + 18 * noteFactor;
                    foreach (float x in row.Segment.Bracket == '<' ? new[] { row.Notes[0].X - 30 } : row.Segment.Bracket == '>' ? new[] { row.Notes[^1].X + 30 } : new[] { row.Notes[0].X - 30, row.Notes[^1].X + 30 })
                    { using var bracket = new SKPath(); bracket.MoveTo(x + 10, y1); bracket.CubicTo(x - 12, y1, x + 10, (y1 + y2) / 2, x - 10, (y1 + y2) / 2); bracket.CubicTo(x + 10, (y1 + y2) / 2, x - 12, y2, x + 10, y2); canvas.DrawPath(bracket, stroke); }
                }
            }
            foreach (var arc in score.Arcs)
            {
                var lines = layout.Lines.Where(l => l.Voice.Id == arc.Start.Voice && l.Notes.Any(n => n.RowIndex >= arc.Start.RowIndex && n.RowIndex <= arc.End.RowIndex)).ToList();
                foreach (var row in lines)
                {
                    annotations.Size = (compact ? 43.2f : LyricSize) * row.Section.Ratio("zs");
                    float x1 = row.Notes.Contains(arc.Start) ? arc.Start.X : Left, x2 = row.Notes.Contains(arc.End) ? arc.End.X : Width - Right;
                    float y = row.Baseline - TopSpace(row.Notes, row.Section) - 8 - arc.Depth * 17;
                    using var curve = new SKPath(); curve.MoveTo(x1, y); curve.CubicTo(x1 + (x2 - x1) / 3, y - 25, x2 - (x2 - x1) / 3, y - 25, x2, y); canvas.DrawPath(curve, stroke);
                    if (arc.Tuplet > 0 && row.Notes.Contains(arc.Start))
                    { int count = arc.Tuplet == 1 ? lines.SelectMany(l => l.Notes).Count(n => !n.Bar && !n.Invisible && n.Symbol != "-" && n.RowIndex >= arc.Start.RowIndex && n.RowIndex <= arc.End.RowIndex && (n.RowIndex != arc.Start.RowIndex || n.X >= arc.Start.X) && (n.RowIndex != arc.End.RowIndex || n.X <= arc.End.X)) : arc.Tuplet; Text(canvas, count.ToString(), (x1 + x2) / 2, y - 29, annotations, paint, 0); }
                }
            }
            foreach (var end in score.Endings)
            {
                int last = end.End?.RowIndex ?? layout.Lines.FindLastIndex(l => l.Voice.Id == end.Start.Voice);
                foreach (var row in layout.Lines.Where(l => l.Voice.Id == end.Start.Voice && l.Notes.Any(n => n.RowIndex >= end.Start.RowIndex && n.RowIndex <= last)))
                {
                    words.Size = (compact ? ProjectionBodySize : LyricSize) * row.Section.Ratio("c", 1.1f);
                    float x1 = row.Notes.Contains(end.Start) ? end.Start.X : Left, x2 = end.End != null && row.Notes.Contains(end.End) ? end.End.X : Width - Right, y = row.Baseline - TopSpace(row.Notes, row.Section) - 65 - end.Height * 12;
                    canvas.DrawLine(x1, y, x2, y, stroke); if (row.Notes.Contains(end.Start)) { canvas.DrawLine(x1, y, x1, y + 24, stroke); Text(canvas, end.Label, x1 + 10, y + 33, words, paint, -1); }
                    if (end.Closed && end.End != null && row.Notes.Contains(end.End)) canvas.DrawLine(x2, y, x2, y + 24, stroke);
                }
            }
            // 每个真正的多声部系统只绘一次括号；各声部已按相同小节时间坐标定位。
            foreach (var group in layout.Lines.Where(l => l.Segment == null).GroupBy(l => (l.Section, l.From, l.To, System: score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).First(s => s.Voices.Contains(l.Voice)))))
                if (group.Count() > 1)
                {
                    var first = group.First(); var last = group.Last(); float x = Left - 28;
                    foreach (var row in group)
                    {
                        words.Size = (compact ? ProjectionBodySize : LyricSize) * row.Section.Ratio("c", 1.1f);
                        foreach (var verse in row.Voice.Lyrics.Where(v => v.Label.Length > 0))
                            x = Math.Min(x, row.LabelRight - words.MeasureText(verse.Label) - verse.LabelGap * words.Size - 14);
                    }
                    x = Math.Max(8, x);
                    canvas.DrawLine(x, first.Baseline - 40, x, last.Baseline + 18, stroke); canvas.DrawLine(x, first.Baseline - 40, x + 13, first.Baseline - 40, stroke); canvas.DrawLine(x, last.Baseline + 18, x + 13, last.Baseline + 18, stroke);
                }
            return CompleteRecording(layout, recorder.EndRecording(), verseLabels, ct);
        }

        private static RecordedScore CompleteRecording(Layout layout, SKPicture picture, List<RecordedScore.VerseLabel> verseLabels, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var bands = layout.Bands;
                if (bands.Count == 0) throw Error(1, "没有可排版的谱行");
                for (int i = 0; i < bands.Count; i++)
                    bands[i] = (bands[i].Top, i + 1 < bands.Count ? bands[i + 1].Top : layout.Height);
                var chorusRanges = new List<(int Start, int End)>();
                foreach (int band in layout.ChorusBands)
                {
                    if (chorusRanges.Count > 0 && chorusRanges[^1].End == band)
                        chorusRanges[^1] = (chorusRanges[^1].Start, band + 1);
                    else chorusRanges.Add((band, band + 1));
                }
                return new RecordedScore { Picture = picture, Bands = bands, VerseLabels = verseLabels, PageBreaks = layout.PageBreaks.Where(i => i > 0 && i < bands.Count).Distinct().ToList(), ChorusRanges = chorusRanges, HeaderHeight = bands[0].Top, Height = layout.Height };
            }
            catch { picture?.Dispose(); throw; }
        }
        private static void DrawSignature(SKCanvas canvas, Section section, float baseline, SKFont font, SKPaint ink, SKPaint stroke)
        {
            Text(canvas, section.Key, section.LeftMargin, baseline, font, ink, -1);
            float x = section.LeftMargin + font.MeasureText(section.Key) + 18;
            if (section.Beat.Length > 0)
            {
                using var face = SKTypeface.FromFamilyName("Times New Roman", SKFontStyle.Bold) ?? SKTypeface.Default;
                using var beat = new SKFont(face, 40.5f * section.Ratio("pd"));
                float center = x + Math.Max(beat.MeasureText(section.Beats.ToString()), beat.MeasureText(section.BeatType.ToString())) / 2;
                Text(canvas, section.Beats.ToString(), center, baseline - 20, beat, ink, 0);
                canvas.DrawLine(x - 2, baseline - 12, center * 2 - x + 2, baseline - 12, stroke);
                Text(canvas, section.BeatType.ToString(), center, baseline + 23, beat, ink, 0);
                x = center * 2 - x + 30;
            }
            if (section.Tempo.Length > 0) Text(canvas, section.Tempo, x, baseline, font, ink, -1);
        }
        private static string LyricCore(string text) => text.TrimEnd('，', '。', '！', '？', '、', '；', '：', ',', '.', '!', '?', ';', ':');
        private static void DrawProjectionLyric(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint ink)
        {
            string core = LyricCore(text);
            if (core.Length == 0) { Text(canvas, text, x, y, font, ink, 0); return; }
            // 字对音符居中，尾随标点挂在字右侧，不让“哉，”的整组宽度把“哉”挤偏。
            Text(canvas, core, x, y, font, ink, 0);
            string punctuation = text[core.Length..].Replace('，', ',').Replace('。', '.').Replace('！', '!').Replace('？', '?');
            if (punctuation.Length > 0) Text(canvas, punctuation, x + font.MeasureText(core) / 2, y, font, ink, -1);
        }
        private static List<string> WrapCell(Cell cell, SKFont font, float maxWidth, TextMetrics metrics = null)
        {
            var output = new List<string>();
            foreach (string input in cell.Lines)
            { string line = ""; foreach (var rune in input.EnumerateRunes()) { string next = rune.ToString(); if (line.Length > 0 && (metrics?.Measure(line + next, font.Size) ?? font.MeasureText(line + next)) > maxWidth) { output.Add(line); line = ""; } line += next; } output.Add(line); }
            return output;
        }
        // 和弦不是歌词单元：/是转位低音，不能按歌词空位处理；兼容bE与Eb两种写法。
        private static string NormalizeChord(string text) => Regex.Replace(text,
            @"(?<edge>^|[/|])(?<before>[b#♭♯]?)(?<root>[A-G])(?<after>[b#♭♯]?)",
            m => m.Groups["edge"].Value + m.Groups["root"].Value +
                (m.Groups["before"].Value + m.Groups["after"].Value).Replace("b", "♭").Replace("#", "♯"),
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static IEnumerable<string> ChordRuns(string text)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
                if (text[i] is '♭' or '♯' or '♮')
                {
                    if (i > start) yield return text[start..i];
                    yield return text[i].ToString(); start = i + 1;
                }
            if (start < text.Length) yield return text[start..];
        }
        private static bool ChordAccidental(string run) => run is "♭" or "♯" or "♮";
        private static float AnnotationMeasure(string text, SKFont font, bool chord) => chord
            ? ChordRuns(text).Sum(run => ChordAccidental(run) ? font.Size * .45f : font.MeasureText(run))
            : font.MeasureText(text);
        private static List<string> WrapAnnotation(string text, SKFont font, float maxWidth, bool chord)
        {
            var output = new List<string>();
            foreach (string input in text.Split('|'))
            {
                string line = "";
                foreach (var rune in input.EnumerateRunes())
                {
                    string next = rune.ToString();
                    if (line.Length > 0 && AnnotationMeasure(line + next, font, chord) > maxWidth)
                    { output.Add(line); line = ""; }
                    line += next;
                }
                output.Add(line);
            }
            return output;
        }
        private static void DrawChord(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint ink, SKPaint line)
        {
            x -= AnnotationMeasure(text, font, true) / 2;
            foreach (string run in ChordRuns(text))
            {
                if (ChordAccidental(run))
                {
                    float width = font.Size * .45f;
                    canvas.Save(); canvas.Translate(x + width / 2, y - font.Size * .18f); canvas.Scale(font.Size / 43 * .8f);
                    Accidental(canvas, run, 0, 0, ink, line); canvas.Restore(); x += width;
                }
                else { canvas.DrawText(run, x, y, font, ink); x += font.MeasureText(run); }
            }
        }
        private static float LyricLineScale(Verse verse, List<Note> notes) => Math.Max(1, notes
            .Where(n => n.LyricIndex >= 0 && n.LyricIndex < verse.Cells.Count)
            .Select(n => verse.Cells[n.LyricIndex]).Where(c => c.Text is not "/" and not "@" and not "")
            .Select(c => c.Scale).DefaultIfEmpty(1).Max());
        private static Layout Position(Score score, SKFont words, CancellationToken ct, bool compact = false)
        {
            using var metrics = new TextMetrics(words.Typeface);
            return PositionCore(score, words, ct, compact, metrics);
        }
        private static Layout PositionCore(Score score, SKFont words, CancellationToken ct, bool compact, TextMetrics metrics)
        {
            // 投影版仅压缩页头空白，谱字/歌词字号和完整谱行内部间距保持不变。
            var layout = new Layout(); float y = compact ? 32 : 80; float right = compact ? Left : Right;
            var noteOrder = score.Sections.SelectMany(s => s.Blocks.OfType<SystemRow>()).SelectMany(s => s.Voices.Concat(s.Segments.Select(t => t.Voice))).SelectMany(v => v.Notes)
                .Select((n, index) => (n, index)).ToDictionary(p => p.n, p => p.index);
            foreach (var section in score.Sections)
            {
                bool firstSection = ReferenceEquals(section, score.Sections[0]);
                float sectionStart = y;
                int firstText = layout.Texts.Count;
                float left = section.LeftMargin; right = compact ? section.RightMargin : Right;
                words.Size = (compact ? ProjectionBodySize : LyricSize) * section.Ratio("c", 1.1f);
                float labelWidth = section.Blocks.OfType<SystemRow>().SelectMany(s => s.Voices).SelectMany(v => v.Lyrics)
                    .Select(v => metrics.Measure(v.Label, words.Size) + Math.Max(0, v.LabelGap) * words.Size).DefaultIfEmpty().Max();
                left = Math.Max(left, labelWidth + 32);
                if (left + right > Width - 100) throw Error(1, "歌词节号或注释过宽，无法完整显示");
                y += (section.TopMargin - 30) * 1.8f;
                if (compact)
                {
                    float titleBaseline = y + 55, subtitleY = titleBaseline; bool hasTitle = false;
                    var headerRows = new Dictionary<string, int>(); float leftBottom = titleBaseline, rightBottom = titleBaseline;
                    foreach (var header in section.Header)
                    {
                        string tag = header.Tag;
                        if (tag == "T") { if (hasTitle) tag = "T2"; hasTitle = true; }
                        if (tag is "Z" or "T2") subtitleY += 65 * section.Ratio(tag.ToLowerInvariant(), tag == "Z" ? .8f : 1);
                        float baseline = tag is "Z" or "T2" ? subtitleY : titleBaseline;
                        if (tag is "TL" or "TR")
                        {
                            int row = headerRows.GetValueOrDefault(tag); headerRows[tag] = row + 1;
                            baseline += row * ProjectionTextSize * 1.2f * section.Ratio(tag.ToLowerInvariant());
                            if (tag == "TL") leftBottom = baseline; else rightBottom = baseline;
                        }
                        layout.Texts.Add(new() { Tag = tag, Text = header.Text, Y = baseline });
                    }
                    // 副标题居中，调拍号在左；两者可处于同一水平区域，不把整个页头串行加高。
                    y = titleBaseline + 56 * Math.Max(1, section.Ratio("t", 1.5f));
                    if (section.Key.Length > 0)
                    {
                        // 左侧分类末行与拍号之间仍留足字形下沿/分数上沿间距。
                        y = Math.Max(y, leftBottom + ProjectionTextSize * .8f * section.Ratio("tl") + 6);
                        layout.Texts.Add(new() { Tag = "W", Text = section.Key, Y = y + 24, Signature = section }); y += 65;
                    }
                    y = Math.Max(y, Math.Max(subtitleY + 54, rightBottom + ProjectionTextSize * section.Ratio("tr") + 24));
                }
                else
                {
                    foreach (var header in section.Header) { layout.Texts.Add(new() { Tag = header.Tag, Text = header.Text, Y = y + (header.Tag == "T" ? 60 : 32) }); y += header.Tag == "T" ? 82 : 43; }
                    if (section.Key.Length > 0) { layout.Texts.Add(new() { Tag = "W", Text = section.Key + "    " + section.Beat + "    " + section.Tempo, Y = y + 32 }); y += 65; }
                }
                bool firstBand = true;
                foreach (var block in section.Blocks)
                {
                    ct.ThrowIfCancellationRequested();
                    if (block is PageBreak) { layout.PageBreaks.Add(layout.Bands.Count); continue; }
                    if (block is FreeText free)
                    {
                        using var freeFont = new SKFont(words.Typeface, (compact ? ProjectionTextSize : 32) * section.Ratio("w"));
                        float offset = free.Offset * freeFont.Size;
                        if (!float.IsFinite(offset) || Math.Abs(offset) > Width / 2) throw Error(1, "文字行偏移超出可显示范围");
                        foreach (var line in WrapCell(new Cell { Text = free.Text }, freeFont, Width - left - right - Math.Max(0, offset)))
                        {
                            float top = firstBand && !firstSection ? sectionStart : y;
                            layout.Texts.Add(new() { Tag = "W", Text = line, Y = y + freeFont.Size, XOffset = offset });
                            y += freeFont.Size * 1.35f;
                            layout.Bands.Add((top, y)); firstBand = false;
                        }
                        continue;
                    }
                    var system = (SystemRow)block;
                    if (system.Voices.All(v => v.Hidden)) continue;
                    foreach (var voice in system.Voices)
                    {
                        int measure = voice.Anchor?.Measure ?? 0, ordinal = 0; Time time = voice.Anchor?.Start ?? Time.Zero;
                        foreach (var n in voice.Notes)
                        { n.Measure = measure; n.Start = time; n.Ordinal = ordinal++; if (n.Bar && !n.NoSplit && (time.Value > 0 || ordinal > 1)) { measure++; time = Time.Zero; ordinal = 0; } else time += n.Duration; }
                        // 弱起小节靠齐完整小节尾部，各声部独立求缺拍；只用于本段第一小节。
                        if (voice.Anchor == null && ReferenceEquals(system, section.Blocks.OfType<SystemRow>().First()))
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
                    foreach (var segment in system.Segments)
                    {
                        int measure = segment.Anchor.Measure, ordinal = 0; Time time = segment.Anchor.Start;
                        foreach (var n in segment.Voice.Notes) { n.Measure = measure; n.Start = time; n.Ordinal = ordinal++; if (n.Bar && !n.NoSplit) { measure++; time = Time.Zero; ordinal = 0; } else time += n.Duration; }
                    }
                    var allVoices = system.Voices.Concat(system.Segments.Select(t => t.Voice)).ToList();
                    var voicesById = allVoices.ToDictionary(v => v.Id);
                    var notesByMeasure = allVoices.SelectMany(v => v.Notes).ToLookup(n => n.Measure);
                    int measures = allVoices.Max(v => v.Notes.Max(n => n.Measure)) + 1;
                    var widths = new float[measures]; var slots = new Dictionary<int, List<(Time Start, int Order, float X, float Width)>>();
                    for (int m = 0; m < measures; m++)
                    {
                        var notes = notesByMeasure[m].ToList();
                        var notesBySlot = notes.ToLookup(n => (n.Start, Order: SlotOrder(n)));
                        var keys = notes.Select(n => (n.Start, Order: SlotOrder(n))).Distinct().OrderBy(k => k.Start).ThenBy(k => k.Order).ToList();
                        var values = new List<(Time, int, float, float)>(); float x = 0;
                        foreach (var key in keys)
                        {
                            float w = compact ? 12 : 58;
                            float lyricLeft = 0, lyricRight = 0;
                            foreach (var n in notesBySlot[key])
                            {
                                w = Math.Max(w, (compact ? n.Bar ? 12 : n.Beams > 0 || n.Symbol == "-" ? 34 : 40 : 58) + n.Dots * (compact ? 8 : 10) + n.Accidental.Length * 15 + n.Grace.Count * 24);
                                float glyphScale = (compact ? ProjectionBodySize / Size : 1) * section.Ratio("q", 1.1f);
                                if (glyphScale > (compact ? ProjectionBodySize / Size : 1))
                                    w = Math.Max(w, (n.Bar ? 18 : 40 + n.Dots * 10 + n.Accidental.Length * 20) * glyphScale);
                                if (n.Chord.Length > 0 && !section.HideChords)
                                    w = Math.Max(w, AnnotationMeasure(n.Chord, metrics.Font((compact ? 43.2f : LyricSize) * section.Ratio("hx")), true) + 18);
                                if (n.LyricIndex >= 0)
                                    foreach (var verse in voicesById[n.Voice].Lyrics.Where(v => v.Cells.Count > n.LyricIndex))
                                    {
                                        var cell = verse.Cells[n.LyricIndex];
                                        float size = words.Size * cell.Scale;
                                        foreach (string text in metrics.Wrap(cell, size, 360))
                                        {
                                            float width = metrics.Measure(compact && !cell.LeftAligned ? LyricCore(text) : text, size);
                                            float padding = compact ? 4 : 18;
                                            // 长歌词组从音符左半字处起画，不是以整个组居中；左右占位必须跟绘制一致。
                                            if (cell.LeftAligned)
                                            {
                                                lyricLeft = Math.Max(lyricLeft, size / 2 + padding / 2);
                                                lyricRight = Math.Max(lyricRight, width - size / 2 + padding / 2);
                                            }
                                            else
                                            {
                                                lyricLeft = Math.Max(lyricLeft, (width + padding) / 2);
                                                lyricRight = Math.Max(lyricRight, (width + padding) / 2);
                                            }
                                        }
                                    }
                            }
                            float slotLeft = Math.Max(w / 2, lyricLeft), slotRight = Math.Max(w / 2, lyricRight);
                            w = slotLeft + slotRight;
                            values.Add((key.Start, key.Order, x + slotLeft, w)); x += w;
                        }
                        widths[m] = x + (compact ? 12 : 20); slots[m] = values;
                        if (x > Width - left - right) throw Error(1, "单小节含过宽歌词或过多音符，不能无损排版");
                    }
                    bool naturalWidth = system.Voices[0].WidthTag.Contains('!');
                    var balancedEnds = compact && !naturalWidth ? BalanceSystemMeasures(widths, Width - left - right) : null;
                    for (int from = 0; from < measures;)
                    {
                        int to = from; float total = 0;
                        while (to < measures && total + widths[to] <= Width - left - right &&
                            (balancedEnds == null || to < balancedEnds[from])) total += widths[to++];
                        if (to == from) throw Error(1, "小节宽度超出限制");
                        float expand = naturalWidth ? 1 : (Width - left - right) / Math.Max(1, total);
                        float origin = left;
                        if (compact && !naturalWidth)
                        {
                            // 行首不能随整行伸展比例移动：不同和弦/附点曾把第二行推到右边。
                            // 仅固定第一个时间槽；弱起、多声部和临时声部仍共用完整时间线。
                            float firstSlot = slots[from][0].X;
                            float inset = Math.Max(words.Size, ProjectionBodySize * section.Ratio("q", 1.1f)) / 2;
                            foreach (var n in notesByMeasure[from].Where(n => n.Start == slots[from][0].Start && SlotOrder(n) == slots[from][0].Order))
                            {
                                if (!n.GraceAfter && n.Grace.Count > 0) inset = Math.Max(inset, n.Grace.Count * 32 + 16);
                                if (n.Accidental.Length > 0) inset = Math.Max(inset, 55 * ProjectionBodySize / Size * section.Ratio("q", 1.1f));
                            }
                            foreach (var n in notesByMeasure[from].Where(n => n.Start == slots[from][0].Start && SlotOrder(n) == slots[from][0].Order && n.LyricIndex >= 0))
                                foreach (var verse in voicesById[n.Voice].Lyrics.Where(v => v.Cells.Count > n.LyricIndex))
                                {
                                    var cell = verse.Cells[n.LyricIndex];
                                    float size = words.Size * cell.Scale;
                                    if (cell.LeftAligned) inset = Math.Max(inset, size / 2 + 2);
                                    else foreach (string text in metrics.Wrap(cell, size, 360))
                                        inset = Math.Max(inset, metrics.Measure(LyricCore(text), size) / 2 + 2);
                                }
                            expand = (Width - left - right - inset) / Math.Max(1, total - firstSlot);
                            origin = left + inset - firstSlot * expand;
                        }
                        float systemTop = firstBand && !firstSection ? sectionStart : y; firstBand = false;
                        foreach (var voice in system.Voices)
                        {
                            if (voice.Hidden) continue;
                            var notes = voice.Notes.Where(n => n.Measure >= from && n.Measure < to).ToList(); if (notes.Count == 0) continue;
                            var segments = system.Segments.Where(t => t.Anchor.Voice == voice.Id && t.Voice.Notes.Any(n => n.Measure >= from && n.Measure < to)).ToList();
                            float above = segments.Where(t => !t.Below).Sum(t => 135 + Math.Max(0, t.Height) * 8 + TopSpace(t.Voice.Notes, section) * .3f);
                            float below = segments.Where(t => t.Below).Sum(t => 135 + Math.Max(0, t.Height) * 8);
                            int firstNote = noteOrder[notes[0]], lastNote = noteOrder[notes[^1]];
                            var rowArcs = compact ? score.Arcs.Where(a => a.Start.Voice == voice.Id && noteOrder[a.Start] <= lastNote && noteOrder[a.End] >= firstNote).ToList() : score.Arcs;
                            float arcSpace = rowArcs.Select(a => 8 + a.Depth * 17 + (a.Tuplet > 0 ? compact ? 72 : 29 : 0)).DefaultIfEmpty(5).Max();
                            if (compact && rowArcs.Count > 0) arcSpace += 12; // 曲线的实际顶点也位于本系统band内。
                            var rowEndings = compact ? score.Endings.Where(e => e.Start.Voice == voice.Id && noteOrder[e.Start] <= lastNote && (e.End == null || noteOrder[e.End] >= firstNote)).ToList() : score.Endings;
                            if (rowEndings.Count > 0) arcSpace = Math.Max(arcSpace, 65 + rowEndings.Max(e => e.Height) * 12);
                            y += TopSpace(notes, section) + (compact ? arcSpace + 8 : 95) + above;
                            float x = origin; int rowIndex = layout.Lines.Count;
                            for (int m = from; m < to; m++)
                            { foreach (var n in notes.Where(n => n.Measure == m)) { int order = SlotOrder(n); var slot = slots[m].First(s => s.Start == n.Start && s.Order == order); n.X = x + slot.X * expand; n.Y = y; n.RowIndex = rowIndex; } x += widths[m] * expand; }
                            float lyricHeight = voice.Lyrics.Sum(v => Math.Max(1, notes.Where(n => n.LyricIndex >= 0 && n.LyricIndex < v.Cells.Count).Select(n => metrics.Wrap(v.Cells[n.LyricIndex], words.Size * v.Cells[n.LyricIndex].Scale, 360).Count).DefaultIfEmpty(1).Max()) * (compact ? ProjectionLyricStep : 40) * section.Ratio("c", 1.1f) * LyricLineScale(v, notes) + (compact ? 0 : 8));
                            float bottom = y + (compact ? ProjectionLyricOffset - ProjectionLyricStep + 22 : 86) + notes.Max(n => n.Beams * 8 + Math.Max(0, -n.Octave) * 10) * Math.Max(1, ProjectionBodySize / Size * section.Ratio("q", 1.1f)) + lyricHeight + below;
                            layout.Lines.Add(new() { Voice = voice, Section = section, Notes = notes, Baseline = y, Bottom = bottom, From = from, To = to, ExtraBelow = below, LabelRight = left - 7 });
                            float upperY = y - TopSpace(notes, section) - 35, lowerY = y + 115;
                            foreach (var segment in segments)
                            {
                                var segmentNotes = segment.Voice.Notes.Where(n => n.Measure >= from && n.Measure < to).ToList();
                                float sy = segment.Below ? lowerY + segment.Height * 8 : upperY - segment.Height * 8;
                                float sx = origin; int segmentRowIndex = layout.Lines.Count;
                                for (int m = from; m < to; m++) { foreach (var n in segmentNotes.Where(n => n.Measure == m)) { var slot = slots[m].First(s => s.Start == n.Start && s.Order == SlotOrder(n)); n.X = sx + slot.X * expand; n.Y = sy; n.RowIndex = segmentRowIndex; } sx += widths[m] * expand; }
                                layout.Lines.Add(new() { Voice = segment.Voice, Section = section, Notes = segmentNotes, Baseline = sy, Bottom = sy + 50, From = from, To = to, Segment = segment });
                                if (segment.Below) lowerY += 135 + segment.Height * 8; else upperY -= 135 + segment.Height * 8;
                            }
                            if (voice.Header.Length > 0) layout.Texts.Add(new() { Tag = "W", Text = voice.Header, Y = y - 48 });
                            y = bottom + (compact ? 12 : 32);
                        }
                        y += (compact ? 10 : 20) + Math.Max(-10, section.QuSpace - 30);
                        if (system.IsChorus) layout.ChorusBands.Add(layout.Bands.Count);
                        layout.Bands.Add((systemTop, y)); from = to;
                    }
                }
                var footerRows = new Dictionary<string, int>(); float footerBottom = y;
                foreach (var footer in section.Footer)
                {
                    int row = footerRows.GetValueOrDefault(footer.Tag); footerRows[footer.Tag] = row + 1;
                    float size = (compact ? 43.2f : 31) * section.Ratio(footer.Tag.ToLowerInvariant(), .8f);
                    float baseline = y + size * (1 + row * 1.2f);
                    layout.Texts.Add(new() { Tag = footer.Tag, Text = footer.Text, Y = baseline }); footerBottom = Math.Max(footerBottom, baseline + size * .5f);
                }
                y = footerBottom;
                foreach (var text in layout.Texts.Skip(firstText)) text.Owner = section;
                y += (compact ? 20 : 60) + (section.BottomMargin - 30) * 1.8f;
            }
            layout.Height = checked((int)Math.Ceiling(y + (compact ? 10 : 30))); return layout;
        }
        // 最少谱行优先，其次均衡各行自然宽度。小节不可拆，不改变Q!或显式分页。
        // 贪心会将四小节拆成3+1，最后一个小节的音符被横向拉开数倍。
        private static int[] BalanceSystemMeasures(float[] widths, float capacity)
        {
            int count = widths.Length;
            var ends = new int[count]; var rows = new int[count + 1]; var costs = new double[count + 1];
            for (int from = count - 1; from >= 0; from--)
            {
                rows[from] = int.MaxValue; costs[from] = double.PositiveInfinity;
                float total = 0;
                for (int to = from + 1; to <= count; to++)
                {
                    total += widths[to - 1];
                    if (total > capacity) break;
                    if (rows[to] == int.MaxValue) continue;
                    int candidateRows = rows[to] + 1;
                    double slack = capacity - total, cost = costs[to] + slack * slack;
                    if (candidateRows < rows[from] || (candidateRows == rows[from] && cost < costs[from]))
                    { rows[from] = candidateRows; costs[from] = cost; ends[from] = to; }
                }
                if (ends[from] == 0) throw Error(1, "小节宽度超出限制");
            }
            return ends;
        }
        private static void Text(SKCanvas c, string value, float x, float y, SKFont font, SKPaint ink, int align)
        { float w = font.MeasureText(value); c.DrawText(value, x - (align == 0 ? w / 2 : align == 1 ? w : 0), y, font, ink); }
        private static int SlotOrder(Note n) => n.Bar ? n.Ordinal == 0 ? -10000 : 10000 : n.Invisible ? n.Ordinal + 1 : 0;
        private static IEnumerable<(string Text, string Tag)> AnnotationTexts(Note n, Section section)
        {
            if (n.Dynamics.Length > 0) yield return (n.Dynamics, "zs");
            if (!section.HideChords && n.Chord.Length > 0) yield return (n.Chord, "hx");
            if (n.Key.Length > 0) yield return (n.Key, "pd");
            if (!n.HideBeat && n.Beat.Length > 0) yield return (n.Beat, "pd");
            if (n.Comment.Length > 0) yield return (n.Comment, "zs");
        }
        private static float AnnotationWidth(Section section) => Math.Min(520, Width - section.LeftMargin - section.RightMargin);
        private static float TopSpace(List<Note> notes, Section section)
        {
            using var face = SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;
            using var font = new SKFont(face, 43.2f);
            return notes.Max(n =>
            {
                float height = 72 + Math.Max(0, n.Octave) * 10 + n.Height * 8 + n.Marks.Count(m => m is not ("zkh" or "ykh")) * 40;
                foreach (var annotation in AnnotationTexts(n, section))
                { font.Size = 43.2f * section.Ratio(annotation.Tag); height += WrapAnnotation(annotation.Text, font, AnnotationWidth(section), annotation.Tag == "hx").Count * Math.Max(39, font.Size * 1.2f); }
                return height;
            });
        }
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
            // &zkh/&ykh是音符两侧的括弧，不是音符上方的装饰音。
            foreach (bool left in new[] { true, false })
                if (n.Marks.Contains(left ? "zkh" : "ykh"))
                {
                    float x = n.X + (left ? -31 : 31), sign = left ? -1 : 1;
                    using var bracket = new SKPath(); bracket.MoveTo(x, n.Y - 45); bracket.CubicTo(x + sign * 12, n.Y - 32, x + sign * 12, n.Y + 2, x, n.Y + 12); c.DrawPath(bracket, line);
                }
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
                if (mark is "zkh" or "ykh") continue;
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
            float baseSize = font.Size / section.Ratio("zs");
            foreach (var annotation in AnnotationTexts(n, section))
            {
                using var annotationFont = new SKFont(font.Typeface, baseSize * section.Ratio(annotation.Tag));
                bool chord = annotation.Tag == "hx";
                var lines = WrapAnnotation(annotation.Text, annotationFont, AnnotationWidth(section), chord);
                float step = Math.Max(39, annotationFont.Size * 1.2f);
                for (int i = 0; i < lines.Count; i++)
                {
                    float half = AnnotationMeasure(lines[i], annotationFont, chord) / 2;
                    float x = Math.Clamp(n.X, section.LeftMargin + half, Width - section.RightMargin - half);
                    float baseline = y - (lines.Count - 1 - i) * step;
                    if (chord) DrawChord(c, lines[i], x, baseline, annotationFont, ink, line);
                    else Text(c, lines[i], x, baseline, annotationFont, ink, 0);
                }
                y -= lines.Count * step;
            }
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
                void Finish() { if (start != null) c.DrawLine(start.X - 12 * row.NoteScale, start.Y + level * 8 * row.NoteScale, previous.X + 12 * row.NoteScale, previous.Y + level * 8 * row.NoteScale, line); start = null; }
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
