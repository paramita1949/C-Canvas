using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Interfaces;
using TinyPinyin;

namespace ImageColorChanger.UI.Modules
{
    public sealed class BibleSearchCoordinator : IBibleSearchCoordinator
    {
        private readonly IBibleService _bibleService;
        private readonly IBibleSearchSummaryBuilder _summaryBuilder;
        private readonly object _syncRoot = new();
        private CancellationTokenSource _pendingSearchCts;
        private bool _disposed;

        public BibleSearchCoordinator(IBibleService bibleService, IBibleSearchSummaryBuilder summaryBuilder)
        {
            _bibleService = bibleService ?? throw new ArgumentNullException(nameof(bibleService));
            _summaryBuilder = summaryBuilder ?? throw new ArgumentNullException(nameof(summaryBuilder));
        }

        public async Task<IReadOnlyList<BibleSearchHit>> SearchAsync(string query, int debounceMs = 300)
        {
            if (_disposed)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                CancelPendingSearch();
                return Array.Empty<BibleSearchHit>();
            }

            var cts = ReplaceSearchToken();
            try
            {
                if (debounceMs > 0)
                {
                    await Task.Delay(debounceMs, cts.Token);
                }

                var keywords = ParseKeywords(query);
                if (keywords.Count == 0)
                {
                    return Array.Empty<BibleSearchHit>();
                }

                bool pinyinMode = IsLikelyPinyinQuery(keywords);
                string normalizedPinyinKeyword = pinyinMode
                    ? NormalizeAlphaNumeric(string.Concat(keywords))
                    : string.Empty;

                var rawResults = pinyinMode
                    ? await _bibleService.SearchVersesByPinyinAsync(string.Concat(keywords))
                    : await _bibleService.SearchVersesAsync(keywords[0]);
                cts.Token.ThrowIfCancellationRequested();

                IEnumerable<Database.Models.Bible.BibleSearchResult> filtered = rawResults ?? Enumerable.Empty<Database.Models.Bible.BibleSearchResult>();
                if (!pinyinMode && keywords.Count > 1)
                {
                    filtered = filtered.Where(r => ContainsAllKeywords(r?.Scripture, keywords));
                }

                var hits = filtered
                    .OrderBy(r => r.Book)
                    .ThenBy(r => r.Chapter)
                    .ThenBy(r => r.Verse)
                    .Select((r, index) =>
                    {
                        string scripture = r.Scripture ?? string.Empty;
                        string snippet = _summaryBuilder.BuildSnippet(scripture, keywords);
                        string prefix;
                        string match;
                        string suffix;
                        string fullPrefix;
                        string fullMatch;
                        string fullSuffix;

                        if (pinyinMode)
                        {
                            if (TryLocatePinyinMatchRange(scripture, normalizedPinyinKeyword, out int charStart, out int charEnd))
                            {
                                var pinyinHighlight = BuildSnippetFromMatchRange(
                                    scripture,
                                    charStart,
                                    charEnd,
                                    fallbackSnippet: snippet);
                                snippet = pinyinHighlight.snippet;
                                prefix = pinyinHighlight.prefix;
                                match = pinyinHighlight.match;
                                suffix = pinyinHighlight.suffix;
                                (fullPrefix, fullMatch, fullSuffix) = SplitByCharRange(scripture, charStart, charEnd);
                            }
                            else
                            {
                                prefix = snippet;
                                match = string.Empty;
                                suffix = string.Empty;
                                fullPrefix = scripture;
                                fullMatch = string.Empty;
                                fullSuffix = string.Empty;
                            }
                        }
                        else
                        {
                            (prefix, match, suffix) = SplitHighlight(snippet, keywords);
                            (fullPrefix, fullMatch, fullSuffix) = SplitHighlight(scripture, keywords);
                        }

                        return new BibleSearchHit
                        {
                            RowNumber = index + 1,
                            Book = r.Book,
                            Chapter = r.Chapter,
                            Verse = r.Verse,
                            Reference = string.IsNullOrWhiteSpace(r.Reference)
                                ? $"{BibleBookConfig.GetBook(r.Book)?.Name} {r.Chapter}:{r.Verse}"
                                : r.Reference,
                            Scripture = scripture,
                            Snippet = snippet,
                            SnippetPrefix = prefix,
                            SnippetMatch = match,
                            SnippetSuffix = suffix,
                            FullPrefix = fullPrefix,
                            FullMatch = fullMatch,
                            FullSuffix = fullSuffix
                        };
                    })
                    .ToList();

                cts.Token.ThrowIfCancellationRequested();
                return hits;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void CancelPendingSearch()
        {
            lock (_syncRoot)
            {
                try
                {
                    _pendingSearchCts?.Cancel();
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_syncRoot)
            {
                try
                {
                    _pendingSearchCts?.Cancel();
                }
                catch
                {
                }

                _pendingSearchCts?.Dispose();
                _pendingSearchCts = null;
            }
        }

        private CancellationTokenSource ReplaceSearchToken()
        {
            lock (_syncRoot)
            {
                _pendingSearchCts?.Cancel();
                _pendingSearchCts?.Dispose();
                _pendingSearchCts = new CancellationTokenSource();
                return _pendingSearchCts;
            }
        }

        private static List<string> ParseKeywords(string query)
        {
            return query
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ContainsAllKeywords(string scripture, IReadOnlyList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(scripture))
            {
                return false;
            }

            foreach (var keyword in keywords)
            {
                if (scripture.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsLikelyPinyinQuery(IReadOnlyList<string> keywords)
        {
            if (keywords == null || keywords.Count == 0)
            {
                return false;
            }

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    return false;
                }

                for (int i = 0; i < keyword.Length; i++)
                {
                    char c = keyword[i];
                    if (!IsAsciiLetter(c))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static (string prefix, string match, string suffix) SplitHighlight(string snippet, IReadOnlyList<string> keywords)
        {
            if (string.IsNullOrEmpty(snippet) || keywords == null || keywords.Count == 0)
            {
                return (snippet ?? string.Empty, string.Empty, string.Empty);
            }

            int bestIndex = -1;
            string bestKeyword = null;
            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                int index = snippet.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                if (bestIndex < 0 || index < bestIndex)
                {
                    bestIndex = index;
                    bestKeyword = snippet.Substring(index, keyword.Length);
                }
            }

            if (bestIndex < 0 || string.IsNullOrEmpty(bestKeyword))
            {
                return (snippet, string.Empty, string.Empty);
            }

            string prefix = snippet.Substring(0, bestIndex);
            string suffix = snippet.Substring(bestIndex + bestKeyword.Length);
            return (prefix, bestKeyword, suffix);
        }

        private static bool TryLocatePinyinMatchRange(
            string scripture,
            string normalizedPinyinKeyword,
            out int charStart,
            out int charEnd)
        {
            charStart = -1;
            charEnd = -1;

            if (string.IsNullOrWhiteSpace(scripture) || string.IsNullOrWhiteSpace(normalizedPinyinKeyword))
            {
                return false;
            }

            var pinyinBuilder = new StringBuilder(scripture.Length * 2);
            var segments = new List<PinyinSegment>(scripture.Length);

            for (int i = 0; i < scripture.Length; i++)
            {
                char current = scripture[i];
                string token = GetSearchTokenForChar(current);
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                int start = pinyinBuilder.Length;
                pinyinBuilder.Append(token);
                segments.Add(new PinyinSegment(i, start, token.Length));
            }

            if (segments.Count == 0)
            {
                return false;
            }

            string pinyinText = pinyinBuilder.ToString();
            int pinyinMatchStart = pinyinText.IndexOf(normalizedPinyinKeyword, StringComparison.OrdinalIgnoreCase);
            if (pinyinMatchStart < 0)
            {
                return false;
            }

            int pinyinMatchEndExclusive = pinyinMatchStart + normalizedPinyinKeyword.Length;
            foreach (var segment in segments)
            {
                bool intersects =
                    segment.Start < pinyinMatchEndExclusive &&
                    (segment.Start + segment.Length) > pinyinMatchStart;
                if (!intersects)
                {
                    continue;
                }

                if (charStart < 0)
                {
                    charStart = segment.CharIndex;
                }
                charEnd = segment.CharIndex;
            }

            if (charStart < 0 || charEnd < charStart)
            {
                return false;
            }

            return true;
        }

        private static (string snippet, string prefix, string match, string suffix) BuildSnippetFromMatchRange(
            string scripture,
            int charStart,
            int charEnd,
            string fallbackSnippet)
        {
            if (string.IsNullOrWhiteSpace(scripture) || charStart < 0 || charEnd < charStart || charEnd >= scripture.Length)
            {
                return (fallbackSnippet ?? string.Empty, fallbackSnippet ?? string.Empty, string.Empty, string.Empty);
            }

            const int maxLength = 48;
            const int contextBefore = 12;
            int windowStart = 0;
            int windowLength = scripture.Length;

            if (scripture.Length > maxLength)
            {
                windowStart = Math.Max(0, charStart - contextBefore);
                if (windowStart + maxLength < charEnd + 1)
                {
                    windowStart = (charEnd + 1) - maxLength;
                }

                if (windowStart + maxLength > scripture.Length)
                {
                    windowStart = scripture.Length - maxLength;
                }

                windowLength = Math.Min(maxLength, scripture.Length - windowStart);
            }

            string rawSnippet = scripture.Substring(windowStart, windowLength);
            int highlightStart = charStart - windowStart;
            int highlightLength = charEnd - charStart + 1;

            if (highlightStart < 0 || highlightStart >= rawSnippet.Length)
            {
                return (fallbackSnippet ?? string.Empty, fallbackSnippet ?? string.Empty, string.Empty, string.Empty);
            }

            if (highlightStart + highlightLength > rawSnippet.Length)
            {
                highlightLength = rawSnippet.Length - highlightStart;
            }

            string prefix = rawSnippet.Substring(0, highlightStart);
            string match = rawSnippet.Substring(highlightStart, highlightLength);
            string suffix = rawSnippet.Substring(highlightStart + highlightLength);

            if (windowStart > 0)
            {
                prefix = "..." + prefix;
            }

            if (windowStart + windowLength < scripture.Length)
            {
                suffix += "...";
            }

            string snippet = prefix + match + suffix;
            return (snippet, prefix, match, suffix);
        }

        private static (string prefix, string match, string suffix) SplitByCharRange(string source, int start, int endInclusive)
        {
            if (string.IsNullOrEmpty(source) || start < 0 || endInclusive < start || endInclusive >= source.Length)
            {
                return (source ?? string.Empty, string.Empty, string.Empty);
            }

            int length = endInclusive - start + 1;
            string prefix = source.Substring(0, start);
            string match = source.Substring(start, length);
            string suffix = source.Substring(start + length);
            return (prefix, match, suffix);
        }

        private static string GetSearchTokenForChar(char current)
        {
            if (PinyinHelper.IsChinese(current))
            {
                var py = PinyinHelper.GetPinyin(current.ToString());
                return NormalizeAlphaNumeric(py);
            }

            if (char.IsLetterOrDigit(current))
            {
                return char.ToLowerInvariant(current).ToString();
            }

            return string.Empty;
        }

        private static string NormalizeAlphaNumeric(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private readonly struct PinyinSegment
        {
            public PinyinSegment(int charIndex, int start, int length)
            {
                CharIndex = charIndex;
                Start = start;
                Length = length;
            }

            public int CharIndex { get; }
            public int Start { get; }
            public int Length { get; }
        }
    }
}
