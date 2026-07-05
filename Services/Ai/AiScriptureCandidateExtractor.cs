using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models.Bible;
using ImageColorChanger.Services;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services.Ai
{
    public static class AiScriptureCandidateExtractor
    {
        private const int MinQuoteMatchLength = 6;
        private const double MinQuoteMatchScore = 0.45;

        public static Task<IReadOnlyList<AiScriptureCandidate>> ExtractFromAssistantTextAsync(
            string assistantText,
            IBibleService bibleService,
            CancellationToken cancellationToken = default)
        {
            string text = (assistantText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return Task.FromResult<IReadOnlyList<AiScriptureCandidate>>(Array.Empty<AiScriptureCandidate>());
            }

            if (!TryParseReferenceFromAssistantText(text, out var reference))
            {
                return Task.FromResult<IReadOnlyList<AiScriptureCandidate>>(Array.Empty<AiScriptureCandidate>());
            }

            var book = BibleBookConfig.GetBook(reference.BookId);
            if (book == null)
            {
                return Task.FromResult<IReadOnlyList<AiScriptureCandidate>>(Array.Empty<AiScriptureCandidate>());
            }

            if (reference.IsChapterOnly)
            {
                return ResolveChapterQuoteAsync(text, book.BookId, book.Name, reference.Chapter, bibleService, cancellationToken);
            }

            return Task.FromResult<IReadOnlyList<AiScriptureCandidate>>(new[]
            {
                new AiScriptureCandidate
                {
                    BookId = book.BookId,
                    BookName = book.Name,
                    Chapter = reference.Chapter,
                    StartVerse = reference.StartVerse,
                    EndVerse = reference.EndVerse,
                    Confidence = 0.88,
                    Reason = "AI摘要正文包含可解析经文引用，作为工具候选缺失时的本地补充。",
                    EvidenceText = text,
                    SourceTurnId = "assistant_summary"
                }
            });
        }

        public static async Task<AiScriptureCandidate> RefineChapterOnlyCandidateAsync(
            AiScriptureCandidate candidate,
            string assistantText,
            IBibleService bibleService,
            CancellationToken cancellationToken = default)
        {
            if (candidate == null || candidate.BookId <= 0 || candidate.Chapter <= 0 || candidate.StartVerse > 0)
            {
                return candidate;
            }

            var book = BibleBookConfig.GetBook(candidate.BookId);
            if (book == null)
            {
                return null;
            }

            var resolved = await ResolveChapterQuoteAsync(
                assistantText,
                book.BookId,
                book.Name,
                candidate.Chapter,
                bibleService,
                cancellationToken).ConfigureAwait(false);
            return resolved.Count == 0 ? null : resolved[0];
        }

        private static bool TryParseReferenceFromAssistantText(string text, out BibleSpeechReference reference)
        {
            if (BibleSpeechReferenceParser.TryParse(text, out reference))
            {
                return true;
            }

            int chapterMarker = text.IndexOf('章');
            if (chapterMarker < 0)
            {
                reference = default;
                return false;
            }

            string prefix = text.Substring(0, chapterMarker + 1);
            return BibleSpeechReferenceParser.TryParse(prefix, out reference);
        }

        private static async Task<IReadOnlyList<AiScriptureCandidate>> ResolveChapterQuoteAsync(
            string assistantText,
            int bookId,
            string bookName,
            int chapter,
            IBibleService bibleService,
            CancellationToken cancellationToken)
        {
            if (bibleService == null || bookId <= 0 || chapter <= 0)
            {
                return Array.Empty<AiScriptureCandidate>();
            }

            List<BibleVerse> verses;
            try
            {
                verses = await bibleService.GetChapterVersesAsync(bookId, chapter).ConfigureAwait(false);
            }
            catch
            {
                return Array.Empty<AiScriptureCandidate>();
            }

            if (verses == null || verses.Count == 0)
            {
                return Array.Empty<AiScriptureCandidate>();
            }

            string normalizedText = NormalizeForQuoteMatch(assistantText);
            if (normalizedText.Length < MinQuoteMatchLength)
            {
                return Array.Empty<AiScriptureCandidate>();
            }

            var best = verses
                .Where(v => v != null && v.Verse > 0 && !string.IsNullOrWhiteSpace(v.Scripture))
                .Select(v =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string scripture = NormalizeForQuoteMatch(v.Scripture);
                    int lcs = LongestCommonSubstringLength(normalizedText, scripture);
                    double score = scripture.Length == 0 ? 0 : (double)lcs / scripture.Length;
                    return new { Verse = v, MatchLength = lcs, Score = score };
                })
                .Where(x => x.MatchLength >= MinQuoteMatchLength && x.Score >= MinQuoteMatchScore)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.MatchLength)
                .FirstOrDefault();

            if (best == null)
            {
                return Array.Empty<AiScriptureCandidate>();
            }

            return new[]
            {
                new AiScriptureCandidate
                {
                    BookId = bookId,
                    BookName = bookName,
                    Chapter = chapter,
                    StartVerse = best.Verse.Verse,
                    EndVerse = best.Verse.Verse,
                    Confidence = Math.Min(0.98, Math.Max(0.70, best.Score)),
                    Reason = "AI摘要只给出章节，但正文经句与本章具体经节匹配。",
                    EvidenceText = assistantText ?? string.Empty,
                    SourceTurnId = "assistant_summary"
                }
            };
        }

        private static string NormalizeForQuoteMatch(string value)
        {
            return (value ?? string.Empty)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("　", string.Empty, StringComparison.Ordinal)
                .Replace("，", string.Empty, StringComparison.Ordinal)
                .Replace("。", string.Empty, StringComparison.Ordinal)
                .Replace("！", string.Empty, StringComparison.Ordinal)
                .Replace("？", string.Empty, StringComparison.Ordinal)
                .Replace("、", string.Empty, StringComparison.Ordinal)
                .Replace("：", string.Empty, StringComparison.Ordinal)
                .Replace(":", string.Empty, StringComparison.Ordinal)
                .Replace("“", string.Empty, StringComparison.Ordinal)
                .Replace("”", string.Empty, StringComparison.Ordinal)
                .Replace("\"", string.Empty, StringComparison.Ordinal)
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        private static int LongestCommonSubstringLength(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return 0;
            }

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
    }
}
