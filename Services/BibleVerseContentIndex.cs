using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Interfaces;
using TinyPinyin;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 经文内容内存倒排索引（Bigram Inverted Index）。
    /// 启动时通过 <see cref="LoadAsync"/> 从 IBibleService 预热；
    /// 此后 <see cref="FindByContent"/> 在 &lt;1 ms 内完成经文内容反查，无任何 DB I/O。
    ///
    /// 双索引策略：
    ///   ① 字符 bigram（汉字二元组）—— 精确匹配
    ///   ② 拼音 bigram（双音节串）  —— ASR 错字容错
    /// </summary>
    public sealed class BibleVerseContentIndex
    {
        // ─── 查询结果 ──────────────────────────────────────────────────────────
        public sealed class VerseMatch
        {
            public int Book    { get; init; }
            public int Chapter { get; init; }
            public int Verse   { get; init; }
            public double Score           { get; init; }
            public double CharScore       { get; init; }
            public double PinyinScore     { get; init; }
            public double FuzzyPinyinScore { get; init; }
            public double RrfScore        { get; init; }
            public int CharHits { get; init; }
        }

        // ─── 配置常量 ───────────────────────────────────────────────────────────
        /// <summary>
        /// 最小唯一字符 bigram 数量门槛。
        /// 短于此的经节不进入索引（帖前5:16"要常常喜乐"仅4个bigram，
        /// 会被极低成本覆盖，引发大量误识）。
        /// 数据：全库仅115节(0.37%)低于此值，排除后无实际影响。
        /// </summary>
        private const int MinVerseBigrams = 6;
        private const int FingerprintMaxDf = 10;

        // ─── 内部 ──────────────────────────────────────────────────────────────
        private readonly struct VerseKey : IEquatable<VerseKey>
        {
            public readonly int Book, Chapter, Verse;
            public VerseKey(int b, int c, int v) { Book = b; Chapter = c; Verse = v; }
            public bool Equals(VerseKey o) => Book == o.Book && Chapter == o.Chapter && Verse == o.Verse;
            public override bool Equals(object obj) => obj is VerseKey k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(Book, Chapter, Verse);
        }

        // bigram → 命中节列表
        private Dictionary<string, List<VerseKey>> _charIndex       = new(StringComparer.Ordinal);
        private Dictionary<string, List<VerseKey>> _pinyinIndex     = new(StringComparer.Ordinal);
        private Dictionary<string, List<VerseKey>> _fuzzyPinyinIndex = new(StringComparer.Ordinal);
        // 指纹短语（3/4-gram, df≤10）→ 经节列表
        private Dictionary<string, List<VerseKey>> _fingerprintIndex     = new(StringComparer.Ordinal);
        private Dictionary<string, List<VerseKey>> _fingerprintPyIndex   = new(StringComparer.Ordinal);

        private Dictionary<string, double> _charBigramIdf        = new(StringComparer.Ordinal);
        private Dictionary<string, double> _pinyinBigramIdf      = new(StringComparer.Ordinal);
        private Dictionary<string, double> _fuzzyPinyinBigramIdf = new(StringComparer.Ordinal);
        private Dictionary<string, double> _fingerprintIdf       = new(StringComparer.Ordinal);
        private Dictionary<string, double> _fingerprintPyIdf     = new(StringComparer.Ordinal);

        private Dictionary<VerseKey, double> _verseCharIdfTotal        = new();
        private Dictionary<VerseKey, double> _versePinyinIdfTotal      = new();
        private Dictionary<VerseKey, double> _verseFuzzyPinyinIdfTotal = new();
        private Dictionary<VerseKey, double> _verseFingerprintIdfTotal = new();
        private Dictionary<VerseKey, double> _verseFingerprintPyIdfTotal = new();

        // 每节唯一 bigram 原始数量（仅用于日志）
        private Dictionary<VerseKey, int> _verseCharBigramCount = new();

        private volatile bool _isReady;
        public bool IsReady => _isReady;

        // ─── 构建 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 从缓存文件或 IBibleService 加载全部经文并构建索引。
        /// 缓存文件路径与 DB 同目录（如 bible.db → bible.verses.cache），
        /// 首次从 DB 加载约 1s，后续从缓存文件加载约 50ms。
        /// </summary>
        public async Task LoadAsync(IBibleService service, CancellationToken ct = default,
            string dbFilePath = null)
        {
            if (service == null) return;

            string cachePath = ResolveCachePath(dbFilePath);

            // ① 尝试从缓存文件快速加载
            if (cachePath != null)
            {
                var cached = await TryLoadFromCacheAsync(cachePath, ct).ConfigureAwait(false);
                if (cached != null)
                {
                    Build(cached);
                    return;
                }
            }

            // ② 从 DB 加载
            var allVerses = await LoadFromServiceAsync(service, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested || allVerses.Count == 0) return;

            Build(allVerses);

            // ③ 异步写缓存（不阻塞调用方）
            if (cachePath != null)
                _ = Task.Run(() => WriteCacheFile(cachePath, allVerses));
        }

        private static async Task<List<(int book, int chapter, int verse, string scripture)>> LoadFromServiceAsync(
            IBibleService service, CancellationToken ct)
        {
            var allVerses = new List<(int book, int chapter, int verse, string scripture)>(32_000);
            foreach (var book in BibleBookConfig.Books)
            {
                if (ct.IsCancellationRequested) return allVerses;
                var tasks = new Task<List<Database.Models.Bible.BibleVerse>>[book.ChapterCount];
                for (int ch = 1; ch <= book.ChapterCount; ch++)
                    tasks[ch - 1] = service.GetChapterVersesAsync(book.BookId, ch);
                var chapters = await Task.WhenAll(tasks).ConfigureAwait(false);
                foreach (var verseList in chapters)
                {
                    if (verseList == null) continue;
                    foreach (var v in verseList)
                    {
                        if (!string.IsNullOrWhiteSpace(v.Scripture))
                            allVerses.Add((v.Book, v.Chapter, v.Verse, v.Scripture));
                    }
                }
            }
            return allVerses;
        }

        // ─── 缓存文件 I/O ─────────────────────────────────────────────────────

        private const byte CacheVersion = 2;

        private static string ResolveCachePath(string dbFilePath)
        {
            if (string.IsNullOrEmpty(dbFilePath)) return null;
            try
            {
                string dir = Path.GetDirectoryName(dbFilePath);
                string name = Path.GetFileNameWithoutExtension(dbFilePath);
                return Path.Combine(dir ?? ".", name + ".verses.cache");
            }
            catch { return null; }
        }

        private static async Task<List<(int, int, int, string)>> TryLoadFromCacheAsync(
            string path, CancellationToken ct)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                return DeserializeCache(bytes);
            }
            catch { return null; }
        }

        private static List<(int, int, int, string)> DeserializeCache(byte[] data)
        {
            if (data == null || data.Length < 5) return null;
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            if (r.ReadByte() != CacheVersion) return null;
            int count = r.ReadInt32();
            if (count <= 0 || count > 100_000) return null;
            var list = new List<(int, int, int, string)>(count);
            for (int i = 0; i < count; i++)
            {
                int book = r.ReadInt16();
                int chapter = r.ReadInt16();
                int verse = r.ReadInt16();
                string scripture = r.ReadString();
                list.Add((book, chapter, verse, scripture));
            }
            return list;
        }

        private static void WriteCacheFile(string path,
            IReadOnlyList<(int book, int chapter, int verse, string scripture)> verses)
        {
            try
            {
                using var ms = new MemoryStream();
                using var w = new BinaryWriter(ms, Encoding.UTF8);
                w.Write(CacheVersion);
                w.Write(verses.Count);
                foreach (var (book, chapter, verse, scripture) in verses)
                {
                    w.Write((short)book);
                    w.Write((short)chapter);
                    w.Write((short)verse);
                    w.Write(scripture ?? string.Empty);
                }
                w.Flush();
                File.WriteAllBytes(path, ms.ToArray());
            }
            catch { /* 缓存写失败不影响功能 */ }
        }

        /// <summary>
        /// 直接从预加载数据构建（主要用于单元测试）。
        ///
        /// 两阶段构建：
        ///   阶段1 — 过滤极短节，统计每个 bigram 的文档频率(df)，计算 IDF = log((N+1)/(df+1))
        ///   阶段2 — 建立倒排索引，同时为每节存储 IDF 权重总和（评分分母）
        /// </summary>
        public void Build(IReadOnlyList<(int book, int chapter, int verse, string scripture)> verses)
        {
            // ── 阶段1：提取所有 n-gram 集合，统计文档频率 ───────────────
            var verseData    = new List<(VerseKey key, HashSet<string> charBgs, HashSet<string> pyBgs,
                HashSet<string> fuzzyPyBgs, HashSet<string> charTrigrams, HashSet<string> pyTrigrams)>(verses.Count);
            var charDocFreq      = new Dictionary<string, int>(StringComparer.Ordinal);
            var pinyinDocFreq    = new Dictionary<string, int>(StringComparer.Ordinal);
            var fuzzyPyDocFreq   = new Dictionary<string, int>(StringComparer.Ordinal);
            var trigramDocFreq   = new Dictionary<string, int>(StringComparer.Ordinal);
            var pyTrigramDocFreq = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var (book, chapter, verse, scripture) in verses)
            {
                var charBgs = new HashSet<string>(ExtractCharBigrams(scripture), StringComparer.Ordinal);
                if (charBgs.Count < MinVerseBigrams) continue;

                var pyBgs       = new HashSet<string>(ExtractPinyinBigrams(scripture), StringComparer.Ordinal);
                var fuzzyPyBgs  = new HashSet<string>(ExtractFuzzyPinyinBigrams(scripture), StringComparer.Ordinal);
                var charTrigrams = new HashSet<string>(ExtractCharNgrams(scripture, 3, 4), StringComparer.Ordinal);
                var pyTrigrams   = new HashSet<string>(ExtractPinyinNgrams(scripture, 3, 4), StringComparer.Ordinal);
                var key = new VerseKey(book, chapter, verse);
                verseData.Add((key, charBgs, pyBgs, fuzzyPyBgs, charTrigrams, pyTrigrams));

                foreach (var bg in charBgs)       { charDocFreq.TryGetValue(bg, out int v);      charDocFreq[bg]      = v + 1; }
                foreach (var bg in pyBgs)         { pinyinDocFreq.TryGetValue(bg, out int v);    pinyinDocFreq[bg]    = v + 1; }
                foreach (var bg in fuzzyPyBgs)    { fuzzyPyDocFreq.TryGetValue(bg, out int v);   fuzzyPyDocFreq[bg]   = v + 1; }
                foreach (var ng in charTrigrams)  { trigramDocFreq.TryGetValue(ng, out int v);   trigramDocFreq[ng]   = v + 1; }
                foreach (var ng in pyTrigrams)    { pyTrigramDocFreq.TryGetValue(ng, out int v); pyTrigramDocFreq[ng] = v + 1; }
            }

            // ── 计算 IDF ──────────────────────────────────────────────────
            int N = verseData.Count;
            var charIdf    = new Dictionary<string, double>(StringComparer.Ordinal);
            var pinyinIdf  = new Dictionary<string, double>(StringComparer.Ordinal);
            var fuzzyPyIdf = new Dictionary<string, double>(StringComparer.Ordinal);
            var fpIdf      = new Dictionary<string, double>(StringComparer.Ordinal);
            var fpPyIdf    = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (bg, df) in charDocFreq)      charIdf[bg]    = Math.Log((double)(N + 1) / (df + 1));
            foreach (var (bg, df) in pinyinDocFreq)    pinyinIdf[bg]  = Math.Log((double)(N + 1) / (df + 1));
            foreach (var (bg, df) in fuzzyPyDocFreq)   fuzzyPyIdf[bg] = Math.Log((double)(N + 1) / (df + 1));
            foreach (var (ng, df) in trigramDocFreq)   if (df <= FingerprintMaxDf) fpIdf[ng]   = Math.Log((double)(N + 1) / (df + 1));
            foreach (var (ng, df) in pyTrigramDocFreq) if (df <= FingerprintMaxDf) fpPyIdf[ng] = Math.Log((double)(N + 1) / (df + 1));

            // ── 阶段2：建倒排索引 ────────────────────────────────────────
            var charIdx    = new Dictionary<string, List<VerseKey>>(StringComparer.Ordinal);
            var pinyinIdx  = new Dictionary<string, List<VerseKey>>(StringComparer.Ordinal);
            var fuzzyPyIdx = new Dictionary<string, List<VerseKey>>(StringComparer.Ordinal);
            var fpIdx      = new Dictionary<string, List<VerseKey>>(StringComparer.Ordinal);
            var fpPyIdx    = new Dictionary<string, List<VerseKey>>(StringComparer.Ordinal);
            var verseCharIdf     = new Dictionary<VerseKey, double>();
            var versePinyinIdf   = new Dictionary<VerseKey, double>();
            var verseFuzzyPyIdf  = new Dictionary<VerseKey, double>();
            var verseFpIdf       = new Dictionary<VerseKey, double>();
            var verseFpPyIdf     = new Dictionary<VerseKey, double>();
            var verseCharCount   = new Dictionary<VerseKey, int>();

            foreach (var (key, charBgs, pyBgs, fuzzyPyBgs, charTrigrams, pyTrigrams) in verseData)
            {
                double charIdfSum = 0, pyIdfSum = 0, fuzzyPyIdfSum = 0, fpIdfSum = 0, fpPyIdfSum = 0;

                foreach (var bg in charBgs)
                {
                    if (!charIdx.TryGetValue(bg, out var list)) charIdx[bg] = list = new List<VerseKey>();
                    list.Add(key);
                    charIdfSum += charIdf.TryGetValue(bg, out double w) ? w : 0;
                }
                foreach (var bg in pyBgs)
                {
                    if (!pinyinIdx.TryGetValue(bg, out var list)) pinyinIdx[bg] = list = new List<VerseKey>();
                    list.Add(key);
                    pyIdfSum += pinyinIdf.TryGetValue(bg, out double w) ? w : 0;
                }
                foreach (var bg in fuzzyPyBgs)
                {
                    if (!fuzzyPyIdx.TryGetValue(bg, out var list)) fuzzyPyIdx[bg] = list = new List<VerseKey>();
                    list.Add(key);
                    fuzzyPyIdfSum += fuzzyPyIdf.TryGetValue(bg, out double w) ? w : 0;
                }
                foreach (var ng in charTrigrams)
                {
                    if (!fpIdf.ContainsKey(ng)) continue;
                    if (!fpIdx.TryGetValue(ng, out var list)) fpIdx[ng] = list = new List<VerseKey>();
                    list.Add(key);
                    fpIdfSum += fpIdf[ng];
                }
                foreach (var ng in pyTrigrams)
                {
                    if (!fpPyIdf.ContainsKey(ng)) continue;
                    if (!fpPyIdx.TryGetValue(ng, out var list)) fpPyIdx[ng] = list = new List<VerseKey>();
                    list.Add(key);
                    fpPyIdfSum += fpPyIdf[ng];
                }

                verseCharIdf[key]    = charIdfSum;
                versePinyinIdf[key]  = pyIdfSum;
                verseFuzzyPyIdf[key] = fuzzyPyIdfSum;
                verseFpIdf[key]      = fpIdfSum;
                verseFpPyIdf[key]    = fpPyIdfSum;
                verseCharCount[key]  = charBgs.Count;
            }

            _charIndex                  = charIdx;
            _pinyinIndex                = pinyinIdx;
            _fuzzyPinyinIndex           = fuzzyPyIdx;
            _fingerprintIndex           = fpIdx;
            _fingerprintPyIndex         = fpPyIdx;
            _charBigramIdf              = charIdf;
            _pinyinBigramIdf            = pinyinIdf;
            _fuzzyPinyinBigramIdf       = fuzzyPyIdf;
            _fingerprintIdf             = fpIdf;
            _fingerprintPyIdf           = fpPyIdf;
            _verseCharIdfTotal          = verseCharIdf;
            _versePinyinIdfTotal        = versePinyinIdf;
            _verseFuzzyPinyinIdfTotal   = verseFuzzyPyIdf;
            _verseFingerprintIdfTotal   = verseFpIdf;
            _verseFingerprintPyIdfTotal = verseFpPyIdf;
            _verseCharBigramCount       = verseCharCount;
            _isReady = true;
        }

        // ─── 查询 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 根据文本（ASR 识别片段）查找最匹配的经节。
        ///
        /// 评分公式：IDF 加权余弦相似度 × 查询覆盖率 × 指纹加权
        ///   score = (0.5*charCosine + 0.3*pinyinCosine + 0.2*fuzzyPyCosine) * queryCov * fpBoost
        ///   charCosine = Σ(matched_idf) / Σ(verse_idf)  — 经文被覆盖比例
        ///   queryCov   = matchedQueryBigrams / totalQueryBigrams — 查询被匹配比例
        ///   fpBoost    = 有指纹命中时 1.5，否则 1.0
        /// </summary>
        public IReadOnlyList<VerseMatch> FindByContent(string text, int topN = 5)
        {
            if (!_isReady || string.IsNullOrWhiteSpace(text))
                return Array.Empty<VerseMatch>();

            var queryCharSet      = new HashSet<string>(ExtractCharBigrams(text),        StringComparer.Ordinal);
            var queryPinyinSet    = new HashSet<string>(ExtractPinyinBigrams(text),      StringComparer.Ordinal);
            var queryFuzzyPySet   = new HashSet<string>(ExtractFuzzyPinyinBigrams(text), StringComparer.Ordinal);
            var queryFpSet        = new HashSet<string>(ExtractCharNgrams(text, 3, 4),   StringComparer.Ordinal);
            var queryFpPySet      = new HashSet<string>(ExtractPinyinNgrams(text, 3, 4), StringComparer.Ordinal);

            if (queryCharSet.Count == 0 && queryPinyinSet.Count == 0 && queryFuzzyPySet.Count == 0)
                return Array.Empty<VerseMatch>();

            // IDF 命中计算（经文覆盖率分子）
            var charIdfHits    = CountIdfHits(_charIndex,        queryCharSet,    _charBigramIdf);
            var pinyinIdfHits  = CountIdfHits(_pinyinIndex,      queryPinyinSet,  _pinyinBigramIdf);
            var fuzzyPyIdfHits = CountIdfHits(_fuzzyPinyinIndex,  queryFuzzyPySet, _fuzzyPinyinBigramIdf);
            var fpIdfHits      = CountIdfHits(_fingerprintIndex,  queryFpSet,      _fingerprintIdf);
            var fpPyIdfHits    = CountIdfHits(_fingerprintPyIndex, queryFpPySet,   _fingerprintPyIdf);
            var charCountHits  = CountHitsInt(_charIndex, queryCharSet);

            // 查询覆盖率：查询中有多少 bigram 命中了某节经文
            var queryCharHitCount = CountQueryHitsPerVerse(_charIndex, queryCharSet);

            var allKeys = new HashSet<VerseKey>(charIdfHits.Keys);
            allKeys.UnionWith(pinyinIdfHits.Keys);
            allKeys.UnionWith(fuzzyPyIdfHits.Keys);

            int totalQueryBigrams = queryCharSet.Count;
            var results = new List<VerseMatch>(Math.Min(allKeys.Count, 200));

            foreach (var vk in allKeys)
            {
                // 经文覆盖率（IDF 加权）
                charIdfHits.TryGetValue(vk, out double chIdf);
                pinyinIdfHits.TryGetValue(vk, out double pyIdf);
                fuzzyPyIdfHits.TryGetValue(vk, out double fpyIdf);
                _verseCharIdfTotal.TryGetValue(vk, out double vCharIdf);
                _versePinyinIdfTotal.TryGetValue(vk, out double vPyIdf);
                _verseFuzzyPinyinIdfTotal.TryGetValue(vk, out double vFpyIdf);

                double charCosine    = vCharIdf > 0 ? chIdf   / vCharIdf : 0;
                double pinyinCosine  = vPyIdf   > 0 ? pyIdf   / vPyIdf   : 0;
                double fuzzyPyCosine = vFpyIdf  > 0 ? fpyIdf  / vFpyIdf  : 0;

                double blendedScore = 0.5 * charCosine + 0.3 * pinyinCosine + 0.2 * fuzzyPyCosine;

                // 查询覆盖率：查询 bigram 中有多少命中该经文
                queryCharHitCount.TryGetValue(vk, out int qHits);
                double queryCov = totalQueryBigrams > 0 ? (double)qHits / totalQueryBigrams : 0;

                // 指纹加权：命中低频 3/4-gram 时提升置信度
                fpIdfHits.TryGetValue(vk, out double fIdf);
                fpPyIdfHits.TryGetValue(vk, out double fpyIdf2);
                bool hasFingerprintHit = fIdf > 0 || fpyIdf2 > 0;
                double fpBoost = hasFingerprintHit ? 1.5 : 1.0;

                double finalScore = blendedScore * queryCov * fpBoost;

                if (finalScore > 0)
                {
                    charCountHits.TryGetValue(vk, out int rawHits);
                    results.Add(new VerseMatch
                    {
                        Book = vk.Book, Chapter = vk.Chapter, Verse = vk.Verse,
                        Score = finalScore, RrfScore = finalScore,
                        CharScore = charCosine, PinyinScore = pinyinCosine, FuzzyPinyinScore = fuzzyPyCosine,
                        CharHits = rawHits
                    });
                }
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            return results.Count <= topN ? results : results.GetRange(0, topN);
        }

        /// <summary>统计查询中每个 bigram 命中的经文，返回每节被命中的查询 bigram 数量。</summary>
        private static Dictionary<VerseKey, int> CountQueryHitsPerVerse(
            Dictionary<string, List<VerseKey>> index, HashSet<string> querySet)
        {
            var result = new Dictionary<VerseKey, int>();
            foreach (var bg in querySet)
            {
                if (!index.TryGetValue(bg, out var list)) continue;
                foreach (var vk in list)
                {
                    result.TryGetValue(vk, out int c);
                    result[vk] = c + 1;
                }
            }
            return result;
        }

        // ─── Bigram 提取 ───────────────────────────────────────────────────────

        private static List<string> ExtractCharBigrams(string text)
        {
            var bigrams = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return bigrams;
            // 只取汉字字符，过滤标点空格
            var chars = new List<char>(text.Length);
            foreach (char c in text)
                if (c > 127 && PinyinHelper.IsChinese(c)) chars.Add(c);
            for (int i = 0; i < chars.Count - 1; i++)
                bigrams.Add(new string(new[] { chars[i], chars[i + 1] }));
            return bigrams;
        }

        private static List<string> ExtractPinyinBigrams(string text)
        {
            var bigrams = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return bigrams;
            var syllables = new List<string>();
            foreach (char c in text)
            {
                if (c <= 127 || !PinyinHelper.IsChinese(c)) continue;
                string py = PinyinHelper.GetPinyin(c.ToString());
                if (!string.IsNullOrWhiteSpace(py)) syllables.Add(py.ToLowerInvariant());
            }
            for (int i = 0; i < syllables.Count - 1; i++)
                bigrams.Add(syllables[i] + syllables[i + 1]);
            return bigrams;
        }

        private static List<string> ExtractFuzzyPinyinBigrams(string text)
        {
            var bigrams = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return bigrams;
            var syllables = new List<string>();
            foreach (char c in text)
            {
                if (c <= 127 || !PinyinHelper.IsChinese(c)) continue;
                string py = PinyinHelper.GetPinyin(c.ToString());
                if (!string.IsNullOrWhiteSpace(py))
                    syllables.Add(NormalizePinyinForFuzzy(py.ToLowerInvariant()));
            }
            for (int i = 0; i < syllables.Count - 1; i++)
                bigrams.Add(syllables[i] + syllables[i + 1]);
            return bigrams;
        }

        private static List<string> ExtractCharNgrams(string text, int minN, int maxN)
        {
            var ngrams = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return ngrams;
            var chars = new List<char>(text.Length);
            foreach (char c in text)
                if (c > 127 && PinyinHelper.IsChinese(c)) chars.Add(c);
            for (int n = minN; n <= maxN; n++)
                for (int i = 0; i <= chars.Count - n; i++)
                    ngrams.Add(new string(chars.GetRange(i, n).ToArray()));
            return ngrams;
        }

        private static List<string> ExtractPinyinNgrams(string text, int minN, int maxN)
        {
            var ngrams = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return ngrams;
            var syllables = new List<string>();
            foreach (char c in text)
            {
                if (c <= 127 || !PinyinHelper.IsChinese(c)) continue;
                string py = PinyinHelper.GetPinyin(c.ToString());
                if (!string.IsNullOrWhiteSpace(py))
                    syllables.Add(NormalizePinyinForFuzzy(py.ToLowerInvariant()));
            }
            for (int n = minN; n <= maxN; n++)
            {
                for (int i = 0; i <= syllables.Count - n; i++)
                {
                    var sb = new StringBuilder();
                    for (int j = i; j < i + n; j++) sb.Append(syllables[j]);
                    ngrams.Add(sb.ToString());
                }
            }
            return ngrams;
        }

        private static string NormalizePinyinForFuzzy(string pinyin)
        {
            if (string.IsNullOrEmpty(pinyin)) return pinyin;
            string p = pinyin;
            if (p.StartsWith("zh")) p = "z" + p.Substring(2);
            else if (p.StartsWith("ch")) p = "c" + p.Substring(2);
            else if (p.StartsWith("sh")) p = "s" + p.Substring(2);
            if (p.EndsWith("ng") && p.Length > 2) p = p.Substring(0, p.Length - 1);
            if (p.StartsWith("n") && p.Length > 1) p = "l" + p.Substring(1);
            if (p.StartsWith("r") && p.Length > 1) p = "l" + p.Substring(1);
            return p;
        }

        // ─── 辅助 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// IDF 加权命中统计：对每个查询 bigram，将其 IDF 权重累加到所有包含该 bigram 的节上。
        /// 同一 bigram 对同一节只累加一次（去重），确保经文中重复用词不重复计分。
        /// </summary>
        private static Dictionary<VerseKey, double> CountIdfHits(
            Dictionary<string, List<VerseKey>> idx,
            HashSet<string> queryBigrams,
            Dictionary<string, double> idf)
        {
            var hits = new Dictionary<VerseKey, double>();
            foreach (var bg in queryBigrams)
            {
                if (!idx.TryGetValue(bg, out var keys)) continue;
                if (!idf.TryGetValue(bg, out double w) || w <= 0) continue;
                var seen = new HashSet<VerseKey>();
                foreach (var k in keys)
                    if (seen.Add(k)) { hits.TryGetValue(k, out double prev); hits[k] = prev + w; }
            }
            return hits;
        }

        /// <summary>原始命中计数（不加权），用于填充 VerseMatch.CharHits 日志字段。</summary>
        private static Dictionary<VerseKey, int> CountHitsInt(
            Dictionary<string, List<VerseKey>> idx,
            HashSet<string> queryBigrams)
        {
            var hits = new Dictionary<VerseKey, int>();
            foreach (var bg in queryBigrams)
            {
                if (!idx.TryGetValue(bg, out var keys)) continue;
                var seen = new HashSet<VerseKey>();
                foreach (var k in keys)
                    if (seen.Add(k)) { hits.TryGetValue(k, out int prev); hits[k] = prev + 1; }
            }
            return hits;
        }

        private static Dictionary<VerseKey, int> BuildRankMap(Dictionary<VerseKey, double> scores)
        {
            var sorted = new List<KeyValuePair<VerseKey, double>>(scores);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            var ranks = new Dictionary<VerseKey, int>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
                ranks[sorted[i].Key] = i + 1;
            return ranks;
        }
    }
}
