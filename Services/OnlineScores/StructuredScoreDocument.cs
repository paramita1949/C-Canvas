using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using SkiaSharp;

namespace ImageColorChanger.Services.OnlineScores
{
    /// <summary>原始诗歌本谱文是数据源；PNG 内的私有辅助块只是便携副本，不依赖旁文件。</summary>
    public sealed class StructuredScoreDocument
    {
        public const string Extension = ".cscore";
        public string Format { get; set; } = "CanvasCast.StructuredScore";
        public int Version { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "新歌谱";
        public string NotationFormat { get; set; } = "shigeben";
        public string Notation { get; set; } = "";
        /// <summary>随实体文件携带的完整歌词编辑文档；null兼容旧谱，空正文代表用户主动清空。</summary>
        public Services.Lyrics.LyricsDocument Lyrics { get; set; }
        /// <summary>自动从谱文提取的初始歌词可由在线全文补齐；用户编辑后的正文不自动覆盖。</summary>
        public bool LyricsFromNotation { get; set; }
        public int? ShigebenId { get; set; }
        public int NotationType { get; set; }
        // 旧PNG中没有此字段，导入时必须刷新像素，不能沿用旧解析器生成的预览。
        public string RenderRevision { get; set; }
        internal const string CurrentRenderRevision = "native-score-v2.6-page-labels";
        [System.Text.Json.Serialization.JsonIgnore]
        public bool PaginationWasUpdated { get; private set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string PaginationNotice { get; private set; }
        /// <summary>原生谱字颜色；null沿用主显示颜色。旧文件无需迁移。</summary>
        public string TextColor { get; set; }
        public static SKColor ParseTextColor(string value)
        {
            if (value == null || value.Length != 7 || value[0] != '#' ||
                !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
                throw new ScoreIntegrationException("文字颜色必须为#RRGGBB。");
            return new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        public static SKColor TextBackground(SKColor color) => color.Red == 0 && color.Green == 0 && color.Blue == 0 ? SKColors.White : SKColors.Black;
        public StructuredScoreLayout Layout { get; set; } = StructuredScoreLayout.Paginated;
        public int Page { get; set; }
        /// <summary>每节各页的完整谱行数；如[[4],[4],[4],[4]]、单节长谱[[3,3,4,3]]。</summary>
        public int[][] PageLines { get; set; }
        /// <summary>同旋律合谱各页完整谱行数；长页不需要切分。</summary>
        public int[] CombinedPageLines { get; set; }
        /// <summary>true为手动数字覆盖，false为原谱/自动分页，null兼容未记录来源的旧文件。</summary>
        public bool? PageLinesManual { get; set; }
        public bool? CombinedPageLinesManual { get; set; }
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        // ancillary/private/reserved-correct/safe-to-copy PNG chunk, CRC verified on read.
        private const string ChunkType = "csCo";
        private static readonly ConcurrentDictionary<string, object> CacheGates = new();
        private static readonly ConcurrentDictionary<string, object> FileGates = new(StringComparer.OrdinalIgnoreCase);

        public Services.Lyrics.LyricsDocument GetLyrics()
        {
            if (Lyrics != null) return CopyLyrics(Lyrics);
            try { return new Services.Lyrics.LyricsDocument { Content = ExtendedScoreNotationRenderer.ExtractLyrics(Notation) }; }
            catch (ScoreIntegrationException) { return new Services.Lyrics.LyricsDocument(); } // Preserve unsupported notation for editing/image fallback.
        }
        private static Services.Lyrics.LyricsDocument CopyLyrics(Services.Lyrics.LyricsDocument value) => new()
        { Content = value.Content, FontSize = value.FontSize, TextAlign = value.TextAlign, ViewMode = value.ViewMode, ProjectionWatermarkPath = value.ProjectionWatermarkPath };
        private void EnsureLyrics()
        {
            if (Lyrics != null) return;
            var extracted = GetLyrics();
            if (!string.IsNullOrWhiteSpace(extracted.Content)) { Lyrics = extracted; LyricsFromNotation = true; }
        }

        public void Validate()
        {
            if (Lyrics != null && (Lyrics.Content == null || Encoding.UTF8.GetByteCount(Lyrics.Content) > ScoreNotationRenderer.MaxSourceBytes * 4L ||
                !double.IsFinite(Lyrics.FontSize) || Lyrics.FontSize <= 0))
                throw new ScoreIntegrationException("内嵌歌词内容或字号无效，未保存。");
            if (TextColor != null) ParseTextColor(TextColor);
            if (Format != "CanvasCast.StructuredScore" || Version != 1 || NotationFormat != "shigeben")
                throw new ScoreIntegrationException("不支持的结构化歌谱格式或版本。");
            if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 || string.IsNullOrWhiteSpace(Title) || Title.Length > 180 ||
                string.IsNullOrWhiteSpace(Notation) || Encoding.UTF8.GetByteCount(Notation) > ScoreNotationRenderer.MaxSourceBytes ||
                !Enum.IsDefined(Layout) || Page < 0 || NotationType is not (0 or 1))
                throw new ScoreIntegrationException("结构化歌谱字段无效，未保存。");
            if (PageLines != null && (PageLines.Length == 0 || PageLines.Length > 16 || PageLines.Any(p => p == null || p.Length == 0 || p.Length > 6000 || p.Any(n => n <= 0 || n > 6000))) ||
                CombinedPageLines != null && (CombinedPageLines.Length == 0 || CombinedPageLines.Length > 6000 || CombinedPageLines.Any(n => n <= 0 || n > 6000)))
                throw new ScoreIntegrationException("分页数据无效：每页谱行数必须是正整数。");
            if (PageLinesManual == true && PageLines == null || CombinedPageLinesManual == true && CombinedPageLines == null)
                throw new ScoreIntegrationException("手动分页缺少行数，请调整脚本或清空恢复默认分页。");
        }

        public static StructuredScoreDocument Read(string path)
        {
            if (new FileInfo(path).Length > ScoreNotationRenderer.MaxSourceBytes * 8L)
                throw new ScoreIntegrationException("结构化歌谱文件过大。");
            return Decode(File.ReadAllBytes(path));
        }

        public static bool IsScoreFile(string path) => string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);

        public static StructuredScoreDocument ReadFromFile(string path) => IsScoreFile(path) ? Read(path) : TryReadPng(path);

        /// <summary>文件有结构数据就优先本地合成；正式文件路径不变，渲染只写应用缓存。</summary>
        public static string ResolveImagePath(string path, CancellationToken ct = default)
        {
            var score = ReadFromFile(path);
            if (score == null) return path;
            bool needsPlan = score.Layout == StructuredScoreLayout.Paginated ? score.PageLines == null : score.Layout == StructuredScoreLayout.Combined && score.CombinedPageLines == null;
            var pages = score.Render(ct);
            // 旧文件首次成功排版后记录方案；之后只执行文件数据，不再逐次猜分页。
            if ((needsPlan || score.PaginationWasUpdated) && IsScoreFile(path)) score.Save(path);
            return pages[Math.Min(score.Page, pages.Count - 1)];
        }

        private static StructuredScoreDocument Decode(byte[] bytes)
        {
            try
            {
                var document = JsonSerializer.Deserialize<StructuredScoreDocument>(bytes, JsonOptions);
                if (document == null) throw new ScoreIntegrationException("结构化歌谱为空。");
                document.Validate(); return document;
            }
            catch (JsonException) { throw new ScoreIntegrationException("结构化歌谱 JSON 无效。"); }
        }

        public void Save(string path)
        {
            lock (FileGates.GetOrAdd(Path.GetFullPath(path), _ => new object()))
            {
                // Navigation/layout snapshots can predate a lyrics edit; only explicit SaveLyrics may replace carried lyrics.
                if (File.Exists(path))
                {
                    var current = Read(path);
                    if (current.Id == Id && current.Lyrics != null) { Lyrics = CopyLyrics(current.Lyrics); LyricsFromNotation = current.LyricsFromNotation; }
                }
                EnsureLyrics(); Validate(); AtomicWrite(path, stream => stream.Write(JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions)));
            }
        }

        public void SaveLyrics(string path, Services.Lyrics.LyricsDocument lyrics)
        {
            ArgumentNullException.ThrowIfNull(lyrics);
            lock (FileGates.GetOrAdd(Path.GetFullPath(path), _ => new object()))
            {
                var current = ReadFromFile(path) ?? throw new ScoreIntegrationException("文件没有结构化歌谱数据。");
                if (current.Id != Id) throw new ScoreIntegrationException("歌谱文件已被替换，未写入歌词。");
                current.Lyrics = CopyLyrics(lyrics); current.LyricsFromNotation = false; current.Validate();
                if (IsScoreFile(path)) AtomicWrite(path, stream => stream.Write(JsonSerializer.SerializeToUtf8Bytes(current, JsonOptions)));
                else current.WritePngCore(path, path, CancellationToken.None);
                Lyrics = CopyLyrics(lyrics); LyricsFromNotation = false;
            }
        }

        public string CreateInFolder(string folderPath)
        {
            EnsureLyrics(); Validate();
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                throw new ScoreIntegrationException("文件夹不存在，请先导入一个本地文件夹。");
            folderPath = Path.GetFullPath(folderPath);
            string name = new string((Title ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
            if (name.Length == 0) name = "新歌谱";
            if (name.Length > 120) name = name[..120];
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem) ||
                stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9') name = "_" + name;
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
            for (int number = 1; number <= 9999; number++)
            {
                string path = Path.Combine(folderPath, name + (number == 1 ? "" : $" ({number})") + Extension);
                try { AtomicWrite(path, stream => stream.Write(bytes), false); return path; }
                catch (IOException) when (File.Exists(path)) { } // 同名不覆盖，包含并发创建。
            }
            throw new ScoreIntegrationException("同名歌谱过多，请换一个标题。");
        }

        public string CacheKey() => CacheKey(TextColor == null ? SKColors.Black : ParseTextColor(TextColor),
            TextColor == null ? SKColors.White : TextBackground(ParseTextColor(TextColor)));

        private string CacheKey(SKColor text, SKColor background)
        {
            Validate();
            // 页面不参与排版缓存；字体/排版实现变更必须提升此版本。
            var plan = Layout == StructuredScoreLayout.Paginated ? PageLines : Layout == StructuredScoreLayout.Combined && CombinedPageLines != null ? new[] { CombinedPageLines } : null;
            bool manual = Layout == StructuredScoreLayout.Paginated ? PageLinesManual == true : Layout == StructuredScoreLayout.Combined && CombinedPageLinesManual == true;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CurrentRenderRevision + "|Microsoft YaHei|Arial|" + text + "|" + background + "|" + Layout + "|" + manual + "|" + JsonSerializer.Serialize(plan) + "|" + Notation)));
        }

        public IReadOnlyList<string> Render(CancellationToken ct) => RenderWithColors(
            TextColor == null ? SKColors.Black : ParseTextColor(TextColor),
            TextColor == null ? SKColors.White : TextBackground(ParseTextColor(TextColor)), ct);

        public IReadOnlyList<string> RenderWithColors(SKColor text, SKColor background, CancellationToken ct)
        {
            Validate();
            PreparePageLines(ct);
            string key = CacheKey(text, background);
            lock (CacheGates.GetOrAdd(key, _ => new object())) return RenderCore(ct, key, text, background);
        }

        /// <summary>只迁移旧渲染版本的过期切分；当前版本无效方案仍拒绝，不掩盖数字编辑错误。</summary>
        public void PreparePageLines(CancellationToken ct)
        {
            Validate(); PaginationWasUpdated = false; PaginationNotice = null;
            if (Layout == StructuredScoreLayout.Paginated && PageLines == null)
            {
                PageLines = StructuredScoreLayoutRenderer.CreatePageLines(Notation, Layout, ct);
                PageLinesManual = false;
            }
            else if (Layout == StructuredScoreLayout.Combined && CombinedPageLines == null)
            {
                CombinedPageLines = StructuredScoreLayoutRenderer.CreatePageLines(Notation, Layout, ct)[0];
                CombinedPageLinesManual = false;
            }
            else if (Layout != StructuredScoreLayout.LongPage && RenderRevision != CurrentRenderRevision)
            {
                var canonical = StructuredScoreLayoutRenderer.CreatePageLines(Notation, Layout, ct);
                var stored = Layout == StructuredScoreLayout.Paginated ? PageLines : new[] { CombinedPageLines };
                bool covers = stored.Length == 1 && stored[0].Sum(n => (long)n) == canonical.Sum(p => p.Sum(n => (long)n)) ||
                    stored.Length == canonical.Length && stored.Select((p, i) => p.Sum(n => (long)n) == canonical[i].Sum(n => (long)n)).All(ok => ok);
                bool defaultMode = Layout == StructuredScoreLayout.Paginated ? PageLinesManual == false : CombinedPageLinesManual == false;
                if (defaultMode && JsonSerializer.Serialize(stored) != JsonSerializer.Serialize(canonical))
                {
                    if (Layout == StructuredScoreLayout.Paginated) PageLines = canonical; else CombinedPageLines = canonical[0];
                    PaginationWasUpdated = true; PaginationNotice = "默认分页已按谱文标记与当前排版更新。";
                }
                else if (!covers)
                {
                    if (Layout == StructuredScoreLayout.Paginated ? PageLinesManual == true : CombinedPageLinesManual == true)
                        throw new ScoreIntegrationException("谱文行数已变化，请调整手动分页脚本或清空恢复默认分页。");
                    var migrated = canonical.Select((p, i) => i < stored.Length && stored[i].Sum(n => (long)n) == p.Sum(n => (long)n) ? stored[i] : p).ToArray();
                    if (Layout == StructuredScoreLayout.Paginated) PageLines = migrated; else CombinedPageLines = migrated[0];
                    PaginationWasUpdated = true; PaginationNotice = "旧分割脚本已按歌词节号与文字段落兼容，仍完整覆盖谱行。";
                }
            }
            RenderRevision = CurrentRenderRevision;
        }

        private IReadOnlyList<string> RenderCore(CancellationToken ct, string key, SKColor text, SKColor background)
        {
            Validate(); ct.ThrowIfCancellationRequested();
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CanvasCast", "StructuredScores", key);
            string index = Path.Combine(directory, "pages.json");
            // 成功标记最后提交；取消/失败不能留下可接受的半套分页。
            if (File.Exists(index))
            {
                try
                {
                    var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(index));
                    if (names?.Length > 0 && names.All(n => Path.GetFileName(n) == n && File.Exists(Path.Combine(directory, n))))
                        return names.Select(n => Path.Combine(directory, n)).ToArray();
                }
                catch (JsonException) { }
            }
            string stage = directory + "." + Guid.NewGuid().ToString("N");
            try
            {
                var plan = Layout == StructuredScoreLayout.Paginated ? PageLines : Layout == StructuredScoreLayout.Combined ? new[] { CombinedPageLines } : null;
                bool manual = Layout == StructuredScoreLayout.Paginated ? PageLinesManual == true : Layout == StructuredScoreLayout.Combined && CombinedPageLinesManual == true;
                var paths = StructuredScoreLayoutRenderer.Render(Notation, Layout, stage, ct, plan, text, background, ignoreSourcePageBreaks: manual);
                ct.ThrowIfCancellationRequested(); Directory.CreateDirectory(directory);
                foreach (string path in paths) File.Move(path, Path.Combine(directory, Path.GetFileName(path)), true);
                AtomicWrite(index, s => s.Write(JsonSerializer.SerializeToUtf8Bytes(paths.Select(Path.GetFileName).ToArray())));
                return paths.Select(p => Path.Combine(directory, Path.GetFileName(p))).ToArray();
            }
            finally
            {
                // stage 是本次创建的完整绝对路径，且只枚举本层生成文件，不递归清理用户目录。
                if (Directory.Exists(stage))
                {
                    foreach (string file in Directory.EnumerateFiles(stage)) File.Delete(file);
                    Directory.Delete(stage);
                }
            }
        }

        public string Materialize(string target, CancellationToken ct)
        {
            var pages = Render(ct); Page = Math.Min(Page, pages.Count - 1);
            WritePng(pages[Page], target, ct); return target;
        }

        public static string ImportPreview(string source, CancellationToken ct)
        {
            var document = Read(source); string legacyPreview = Path.ChangeExtension(source, ".png");
            // 已有在线结构预览沿用同一图片ID；绝不覆盖同名的普通用户图片。
            string target = File.Exists(legacyPreview) && TryReadPng(legacyPreview)?.Id == document.Id
                ? legacyPreview : source + ".png";
            if (File.Exists(target))
            {
                var preview = TryReadPng(target);
                if (preview?.Id == document.Id && preview.RenderRevision == CurrentRenderRevision && preview.Notation == document.Notation && preview.TextColor == document.TextColor && preview.Layout == document.Layout && preview.Page == document.Page && preview.Title == document.Title &&
                    JsonSerializer.Serialize(preview.PageLines) == JsonSerializer.Serialize(document.PageLines) && JsonSerializer.Serialize(preview.CombinedPageLines) == JsonSerializer.Serialize(document.CombinedPageLines) &&
                    preview.PageLinesManual == document.PageLinesManual && preview.CombinedPageLinesManual == document.CombinedPageLinesManual)
                {
                    if (JsonSerializer.Serialize(preview.Lyrics) == JsonSerializer.Serialize(document.Lyrics) && preview.LyricsFromNotation == document.LyricsFromNotation)
                        return target;
                    // Lyric-only changes update portable metadata without rerendering score pixels.
                    lock (FileGates.GetOrAdd(Path.GetFullPath(target), _ => new object()))
                    { document.EnsureLyrics(); document.WritePngCore(target, target, ct); }
                    return target;
                }
                if (preview?.Id != document.Id)
                    throw new ScoreIntegrationException("结构谱预览路径已有其他文件，未覆盖：" + target);
            }
            return document.Materialize(target, ct);
        }

        public void WritePng(string raster, string target, CancellationToken ct)
        {
            lock (FileGates.GetOrAdd(Path.GetFullPath(target), _ => new object()))
            {
                var current = File.Exists(target) ? TryReadPng(target) : null;
                if (current?.Id == Id && current.Lyrics != null) { Lyrics = CopyLyrics(current.Lyrics); LyricsFromNotation = current.LyricsFromNotation; }
                EnsureLyrics(); RenderRevision = CurrentRenderRevision; WritePngCore(raster, target, ct);
            }
        }
        private void WritePngCore(string raster, string target, CancellationToken ct)
        {
            Validate(); byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
            AtomicWrite(target, output =>
            {
                using var input = File.OpenRead(raster);
                byte[] signature = new byte[8]; input.ReadExactly(signature);
                if (!signature.SequenceEqual(Signature)) throw new ScoreIntegrationException("预览不是有效 PNG。");
                output.Write(signature);
                Span<byte> header = stackalloc byte[8];
                while (ReadChunk(input, out string type, out byte[] data, out byte[] crc))
                {
                    ct.ThrowIfCancellationRequested();
                    if (type == ChunkType) continue;
                    if (type == "IEND") WriteChunk(output, ChunkType, metadata);
                    BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
                    Encoding.ASCII.GetBytes(type, header[4..]); output.Write(header); output.Write(data); output.Write(crc);
                    if (type == "IEND") { ct.ThrowIfCancellationRequested(); return; }
                }
                throw new ScoreIntegrationException("预览 PNG 不完整。");
            });
        }

        public static StructuredScoreDocument TryReadPng(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                using var input = File.OpenRead(path); byte[] signature = new byte[8];
                input.ReadExactly(signature); if (!signature.SequenceEqual(Signature)) return null;
                while (ReadChunk(input, out string type, out byte[] data, out byte[] crc, metadataOnly: true))
                {
                    if (type == ChunkType)
                    {
                        if (BinaryPrimitives.ReadUInt32BigEndian(crc) != Crc(type, data))
                            throw new ScoreIntegrationException("图片内嵌谱文校验失败；原画面不变。");
                        return Decode(data);
                    }
                    if (type == "IEND") return null;
                }
                return null;
            }
            catch (EndOfStreamException) { return null; }
        }

        private static bool ReadChunk(Stream input, out string type, out byte[] data, out byte[] crc, bool metadataOnly = false)
        {
            type = null; data = crc = null;
            Span<byte> header = stackalloc byte[8]; int first = input.ReadByte(); if (first < 0) return false;
            header[0] = (byte)first; input.ReadExactly(header[1..]);
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header));
            type = Encoding.ASCII.GetString(header[4..]);
            if (length < 0 || length > ShigebenClient.MaxImageBytes) throw new ScoreIntegrationException("PNG 数据块无效。");
            if (type == ChunkType && length > ScoreNotationRenderer.MaxSourceBytes * 8)
                throw new ScoreIntegrationException("图片内嵌谱文过大。");
            // 读结构数据时跳过压缩像素；复制PNG时仍保留原块及CRC，不重算全部像素的CRC。
            if (metadataOnly && type != ChunkType)
            {
                if (input.Position + length + 4 > input.Length) throw new EndOfStreamException();
                input.Seek(length, SeekOrigin.Current); data = Array.Empty<byte>();
            }
            else { data = new byte[length]; input.ReadExactly(data); }
            crc = new byte[4]; input.ReadExactly(crc); return true;
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            Span<byte> header = stackalloc byte[8]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, header[4..]); output.Write(header); output.Write(data);
            Span<byte> crc = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(type, data)); output.Write(crc);
        }
        private static uint Crc(string type, byte[] data)
        {
            uint crc = 0xffffffff;
            foreach (byte b in Encoding.ASCII.GetBytes(type).Concat(data))
            { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            return crc ^ 0xffffffff;
        }
        private static void AtomicWrite(string path, Action<Stream> write, bool overwrite = true)
        {
            path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try { using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) write(output); File.Move(pending, path, overwrite); }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }
}
