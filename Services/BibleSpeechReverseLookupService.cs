using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Bible;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services
{
    public sealed class BibleSpeechReverseLookupService
    {
        public async Task<BibleSpeechReference?> TryResolveAsync(
            IBibleService bibleService,
            string recognizedText,
            CancellationToken cancellationToken)
        {
            if (bibleService == null || string.IsNullOrWhiteSpace(recognizedText))
            {
                return null;
            }

            string normalized = Normalize(recognizedText);
            if (normalized.Length < 4)
            {
                return null;
            }

            var keywords = BuildSearchKeywords(normalized);
            BibleSearchResult best = null;
            double bestScore = 0;

            foreach (string keyword in keywords)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                List<BibleSearchResult> hits = await bibleService.SearchVersesAsync(keyword);
                if (hits == null || hits.Count == 0)
                {
                    continue;
                }

                foreach (var hit in hits.Take(50))
                {
                    double score = Score(normalized, Normalize(hit.Scripture));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = hit;
                    }
                }
            }

            if (best == null || bestScore < 0.42)
            {
                return null;
            }

            return new BibleSpeechReference(
                best.Book,
                best.Chapter,
                best.Verse,
                best.Verse);
        }

        private static List<string> BuildSearchKeywords(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return list;
            }

            void AddIfValid(string value)
            {
                string v = Normalize(value);
                if (v.Length >= 4 && !list.Contains(v, StringComparer.Ordinal))
                {
                    list.Add(v);
                }
            }

            AddIfValid(text);
            int len = text.Length;
            if (len > 12)
            {
                AddIfValid(text.Substring(0, 12));
                AddIfValid(text.Substring(Math.Max(0, len - 12), 12));
                int midStart = Math.Max(0, (len / 2) - 6);
                if (midStart + 12 <= len)
                {
                    AddIfValid(text.Substring(midStart, 12));
                }
            }

            if (len > 8)
            {
                AddIfValid(text.Substring(0, 8));
                AddIfValid(text.Substring(Math.Max(0, len - 8), 8));
            }

            return list;
        }

        private static double Score(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return 0;
            }

            int lcs = LongestCommonSubstringLength(a, b);
            int baseLen = Math.Max(1, Math.Min(a.Length, b.Length));
            return (double)lcs / baseLen;
        }

        private static int LongestCommonSubstringLength(string a, string b)
        {
            int[,] dp = new int[a.Length + 1, b.Length + 1];
            int max = 0;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    if (a[i - 1] == b[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                        if (dp[i, j] > max)
                        {
                            max = dp[i, j];
                        }
                    }
                }
            }

            return max;
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("，", string.Empty, StringComparison.Ordinal)
                .Replace("。", string.Empty, StringComparison.Ordinal)
                .Replace("！", string.Empty, StringComparison.Ordinal)
                .Replace("？", string.Empty, StringComparison.Ordinal)
                .Replace("、", string.Empty, StringComparison.Ordinal)
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Trim();
        }
    }
}
