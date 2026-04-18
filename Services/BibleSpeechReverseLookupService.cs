using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Bible;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 经文内容反查服务（时间窗口证据累积版）。
    ///
    /// 核心思路：不再对单次 ASR 片段做 accept/reject 硬判定，
    /// 而是将每次匹配结果作为"投票"累积到候选经文上。
    /// 同一经文在时间窗口内被多次命中（即使单次 score 较低）才触发；
    /// 单次极高分可直接触发（快速路径）。
    ///
    /// 双路策略不变：
    ///   ① 索引路径（快）：BibleVerseContentIndex 已就绪时走内存索引。
    ///   ② DB 降级路径（慢）：索引未就绪时回退到关键词搜索 + LCS 评分。
    /// </summary>
    public sealed class BibleSpeechReverseLookupService
    {
        // ─── 快速路径：单次极高置信度直接触发 ─────────────────────────────────
        private const double SingleShotMinScore = 0.30;
        private const double SingleShotMinCharScore = 0.35;

        // ─── 累积路径：多次弱证据叠加触发 ─────────────────────────────────────
        private const int AccumMinHits = 2;
        private const double AccumMinTotalScore = 0.15;
        private const double AccumMinBestCharScore = 0.20;

        // ─── 投票最低门槛（过滤纯噪声） ───────────────────────────────────────
        private const double VoteFloorScore = 0.02;
        private const int MinTextLength = 10;

        // ─── 时间窗口与冷却 ───────────────────────────────────────────────────
        private static readonly TimeSpan EvidenceWindow = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromSeconds(15);

        // ─── 直接解析跨路径加成 ───────────────────────────────────────────────
        private const double DirectParseBoostScore = 0.50;
        private const double DirectParseBoostCharScore = 1.0;

        // DB 降级路径最低接受阈值（LCS 相似度）
        private const double DbMinScore = 0.42;

        private readonly BibleVerseContentIndex _index;
        private readonly Action<string> _log;

        // ─── 证据累积状态 ─────────────────────────────────────────────────────
        private readonly object _evidenceLock = new();
        private readonly Dictionary<long, List<VerseVote>> _evidence = new();
        private readonly Dictionary<long, DateTime> _cooldowns = new();

        /// <summary>无索引构造：仅走 DB 路径（向下兼容）。</summary>
        public BibleSpeechReverseLookupService() { }

        /// <summary>带索引构造：优先走内存索引路径。</summary>
        public BibleSpeechReverseLookupService(BibleVerseContentIndex index, Action<string> log = null)
        {
            _index = index;
            _log   = log;
        }

        /// <summary>
        /// 当直接解析（Path1）成功时，由调用方通知本服务。
        /// 在证据池中为该经文注入一个强投票，使后续内容反查更容易确认。
        /// </summary>
        public void NotifyDirectParse(int book, int chapter, int verse)
        {
            DateTime now = DateTime.UtcNow;
            long key = VerseKey(book, chapter, verse);
            lock (_evidenceLock)
            {
                PruneUnsafe(now);
                AddVoteUnsafe(key, DirectParseBoostScore, DirectParseBoostCharScore, now);
            }
        }

        public async Task<BibleSpeechReference?> TryResolveAsync(
            IBibleService bibleService,
            string recognizedText,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(recognizedText))
                return null;

            string normalized = Normalize(recognizedText);
            if (normalized.Length < MinTextLength)
                return null;

            // ① 索引路径
            if (_index?.IsReady == true)
            {
                var matches = _index.FindByContent(normalized, topN: 5);
                DateTime now = DateTime.UtcNow;

                lock (_evidenceLock)
                {
                    PruneUnsafe(now);

                    // 将 top 候选作为投票提交
                    foreach (var m in matches)
                    {
                        if (m.Score < VoteFloorScore)
                            continue;
                        long vk = VerseKey(m.Book, m.Chapter, m.Verse);
                        AddVoteUnsafe(vk, m.Score, m.CharScore, now);
                    }

                    // 日志：当前最佳匹配
                    if (_log != null)
                    {
                        string textSnippet = normalized.Length > 20 ? normalized.Substring(0, 20) + "…" : normalized;
                        var top = matches.Count > 0 ? matches[0] : null;
                        if (top == null)
                            _log($"[RL] no-match | '{textSnippet}'");
                        else
                        {
                            long topKey = VerseKey(top.Book, top.Chapter, top.Verse);
                            int hitCount = _evidence.TryGetValue(topKey, out var votes) ? votes.Count : 0;
                            double totalScore = votes?.Sum(v => v.Score) ?? 0;
                            _log($"[RL] top=b{top.Book}c{top.Chapter}v{top.Verse}({top.Score:F3}/{top.CharScore:F2}) hits={hitCount} accum={totalScore:F3} | '{textSnippet}'");
                        }
                    }

                    // 检查是否有经文达到触发条件
                    var triggered = TryGetTriggeredUnsafe(now);
                    if (triggered.HasValue)
                    {
                        var (book, chapter, verse) = triggered.Value;
                        // 清除该经文证据 + 设置冷却期
                        long triggeredKey = VerseKey(book, chapter, verse);
                        _evidence.Remove(triggeredKey);
                        _cooldowns[triggeredKey] = now;

                        _log?.Invoke($"[RL] ✅ triggered b{book}c{chapter}v{verse}");
                        return new BibleSpeechReference(book, chapter, verse, verse);
                    }
                }

                return null;
            }

            // ② DB 降级路径（索引未就绪时）
            _log?.Invoke($"[ReverseLookup] index not ready, falling back to DB. normalized='{normalized}'");
            if (bibleService == null)
                return null;

            return await TryResolveFromDbAsync(bibleService, normalized, cancellationToken)
                .ConfigureAwait(false);
        }

        // ─── 证据累积内部方法（调用方必须持有 _evidenceLock）────────────────

        private void AddVoteUnsafe(long key, double score, double charScore, DateTime utcNow)
        {
            if (!_evidence.TryGetValue(key, out var list))
            {
                list = new List<VerseVote>(8);
                _evidence[key] = list;
            }
            list.Add(new VerseVote { Score = score, CharScore = charScore, UtcTime = utcNow });
        }

        private (int book, int chapter, int verse)? TryGetTriggeredUnsafe(DateTime utcNow)
        {
            // 在所有有投票的经文中，找到第一个满足触发条件的
            // 优先检查快速路径（单次高分），然后检查累积路径
            (int, int, int)? bestAccum = null;
            double bestAccumTotal = 0;

            foreach (var (key, votes) in _evidence)
            {
                if (votes.Count == 0)
                    continue;

                // 冷却期检查
                if (_cooldowns.TryGetValue(key, out var cooldownStart)
                    && (utcNow - cooldownStart) < CooldownDuration)
                    continue;

                var (book, chapter, verse) = ParseKey(key);

                // 快速路径：最新一次投票是极高置信度
                var latest = votes[votes.Count - 1];
                if (latest.Score >= SingleShotMinScore && latest.CharScore >= SingleShotMinCharScore)
                    return (book, chapter, verse);

                // 累积路径
                if (votes.Count >= AccumMinHits)
                {
                    double totalScore = 0;
                    double bestCharScore = 0;
                    for (int i = 0; i < votes.Count; i++)
                    {
                        totalScore += votes[i].Score;
                        if (votes[i].CharScore > bestCharScore)
                            bestCharScore = votes[i].CharScore;
                    }

                    if (totalScore >= AccumMinTotalScore && bestCharScore >= AccumMinBestCharScore)
                    {
                        if (totalScore > bestAccumTotal)
                        {
                            bestAccumTotal = totalScore;
                            bestAccum = (book, chapter, verse);
                        }
                    }
                }
            }

            return bestAccum;
        }

        private void PruneUnsafe(DateTime utcNow)
        {
            DateTime cutoff = utcNow - EvidenceWindow;
            List<long> emptyKeys = null;

            foreach (var (key, votes) in _evidence)
            {
                votes.RemoveAll(v => v.UtcTime < cutoff);
                if (votes.Count == 0)
                {
                    emptyKeys ??= new List<long>();
                    emptyKeys.Add(key);
                }
            }

            if (emptyKeys != null)
            {
                foreach (long key in emptyKeys)
                    _evidence.Remove(key);
            }

            // 清理过期冷却
            List<long> expiredCooldowns = null;
            foreach (var (key, start) in _cooldowns)
            {
                if ((utcNow - start) >= CooldownDuration)
                {
                    expiredCooldowns ??= new List<long>();
                    expiredCooldowns.Add(key);
                }
            }
            if (expiredCooldowns != null)
            {
                foreach (long key in expiredCooldowns)
                    _cooldowns.Remove(key);
            }
        }

        private struct VerseVote
        {
            public double Score;
            public double CharScore;
            public DateTime UtcTime;
        }

        private static long VerseKey(int book, int chapter, int verse)
            => (long)book * 1_000_000 + chapter * 1_000 + verse;

        private static (int book, int chapter, int verse) ParseKey(long key)
            => ((int)(key / 1_000_000), (int)(key / 1_000 % 1_000), (int)(key % 1_000));

        // ─── DB 降级路径（保留原逻辑）────────────────────────────────────────

        private static async Task<BibleSpeechReference?> TryResolveFromDbAsync(
            IBibleService bibleService,
            string normalized,
            CancellationToken cancellationToken)
        {
            var keywords = BuildSearchKeywords(normalized);
            BibleSearchResult best = null;
            double bestScore = 0;

            foreach (string keyword in keywords)
            {
                if (cancellationToken.IsCancellationRequested)
                    return null;

                List<BibleSearchResult> hits = await bibleService.SearchVersesAsync(keyword)
                    .ConfigureAwait(false);
                if (hits == null || hits.Count == 0)
                    continue;

                foreach (var hit in hits.Take(50))
                {
                    double score = LcsScore(normalized, Normalize(hit.Scripture));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = hit;
                    }
                }
            }

            if (best == null || bestScore < DbMinScore)
                return null;

            return new BibleSpeechReference(best.Book, best.Chapter, best.Verse, best.Verse);
        }

        private static List<string> BuildSearchKeywords(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            void AddIfValid(string value)
            {
                string v = Normalize(value);
                if (v.Length >= 4 && !list.Contains(v, StringComparer.Ordinal))
                    list.Add(v);
            }

            AddIfValid(text);
            int len = text.Length;
            if (len > 12)
            {
                AddIfValid(text.Substring(0, 12));
                AddIfValid(text.Substring(Math.Max(0, len - 12), 12));
                int midStart = Math.Max(0, (len / 2) - 6);
                if (midStart + 12 <= len)
                    AddIfValid(text.Substring(midStart, 12));
            }
            if (len > 8)
            {
                AddIfValid(text.Substring(0, 8));
                AddIfValid(text.Substring(Math.Max(0, len - 8), 8));
            }
            return list;
        }

        private static double LcsScore(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
            int lcs = LongestCommonSubstringLength(a, b);
            return (double)lcs / Math.Max(1, Math.Min(a.Length, b.Length));
        }

        private static int LongestCommonSubstringLength(string a, string b)
        {
            int[,] dp = new int[a.Length + 1, b.Length + 1];
            int max = 0;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    if (a[i - 1] == b[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                        if (dp[i, j] > max) max = dp[i, j];
                    }
                }
            return max;
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty)
                .Replace(" ",  string.Empty, StringComparison.Ordinal)
                .Replace("，", string.Empty, StringComparison.Ordinal)
                .Replace("。", string.Empty, StringComparison.Ordinal)
                .Replace("！", string.Empty, StringComparison.Ordinal)
                .Replace("？", string.Empty, StringComparison.Ordinal)
                .Replace("、", string.Empty, StringComparison.Ordinal)
                .Replace(",",  string.Empty, StringComparison.Ordinal)
                .Replace(".",  string.Empty, StringComparison.Ordinal)
                .Trim();
        }
    }
}
