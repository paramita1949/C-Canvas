using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ImageColorChanger.Core;
using TinyPinyin;

namespace ImageColorChanger.Services
{
    public readonly struct BibleSpeechReference
    {
        public BibleSpeechReference(int bookId, int chapter, int startVerse, int endVerse)
        {
            BookId = bookId;
            Chapter = chapter;
            StartVerse = startVerse;
            EndVerse = endVerse;
        }

        public int BookId { get; }
        public int Chapter { get; }
        public int StartVerse { get; }
        public int EndVerse { get; }
        public bool IsChapterOnly => StartVerse <= 0 || EndVerse <= 0;
    }

    public static class BibleSpeechReferenceParser
    {
        private enum ParseDecisionKind
        {
            Accept = 0,
            Abstain = 1
        }

        private enum AbstainReasonCode
        {
            None = 0,
            EmptyInput = 1,
            InvalidStructure = 2,
            BookNotFound = 3,
            InvalidChapterVerse = 4,
            ChapterOverflow = 5
        }

        private readonly struct ParseDecision
        {
            public ParseDecision(
                ParseDecisionKind kind,
                BibleSpeechReference reference,
                AbstainReasonCode reason,
                BookCandidateScore topCandidate,
                BookCandidateScore secondCandidate)
            {
                Kind = kind;
                Reference = reference;
                Reason = reason;
                TopCandidate = topCandidate;
                SecondCandidate = secondCandidate;
            }

            public ParseDecisionKind Kind { get; }
            public BibleSpeechReference Reference { get; }
            public AbstainReasonCode Reason { get; }
            public BookCandidateScore TopCandidate { get; }
            public BookCandidateScore SecondCandidate { get; }

            public static ParseDecision Accept(BibleSpeechReference reference, BookCandidateScore topCandidate, BookCandidateScore secondCandidate)
            {
                return new ParseDecision(ParseDecisionKind.Accept, reference, AbstainReasonCode.None, topCandidate, secondCandidate);
            }

            public static ParseDecision Abstain(AbstainReasonCode reason, BookCandidateScore topCandidate, BookCandidateScore secondCandidate)
            {
                return new ParseDecision(ParseDecisionKind.Abstain, default, reason, topCandidate, secondCandidate);
            }
        }

        private readonly struct BookCandidateScore
        {
            public BookCandidateScore(int bookId, string alias, double score)
            {
                BookId = bookId;
                Alias = alias ?? string.Empty;
                Score = score;
            }

            public int BookId { get; }
            public string Alias { get; }
            public double Score { get; }

            public static BookCandidateScore None => new BookCandidateScore(0, string.Empty, 0);
        }

        private static readonly Regex NumberTokenRegex = new Regex(
            @"[0-9]+|[零〇一二两三四五六七八九十百千]+",
            RegexOptions.Compiled);
        private static readonly Regex ChapterMarkerRegex = new Regex(
            @"(?<num>[0-9]+|[零〇一二两三四五六七八九十百千]+)\s*(?<mark>章|:)",
            RegexOptions.Compiled);
        private static readonly string[] BookSegmentNoisePrefixes =
        {
            "新约的",
            "旧约的",
            "新约",
            "旧约",
            "圣经的",
            "圣经",
            "英文哦在",
            "英文哦",
            "英文在",
            "翻到以后呢",
            "翻到以后",
            "我们来读",
            "我们来看",
            "我们看",
            "一同来看",
            "一同来读",
            "请看",
            "请读",
            "打开",
            "在"
        };

        private sealed class BookAlias
        {
            public int BookId { get; init; }
            public string Alias { get; init; } = string.Empty;
            public string Initials { get; init; } = string.Empty;
            /// <summary>完整拼音串（无声调），例如"腓利比" → "feilibili"</summary>
            public string FullPinyin { get; init; } = string.Empty;
        }

        private readonly struct AmbiguousOrdinalProfile
        {
            public AmbiguousOrdinalProfile(string segment, string leadingDigits, int maxTokenLength)
            {
                Segment = segment ?? string.Empty;
                LeadingDigits = leadingDigits ?? string.Empty;
                MaxTokenLength = Math.Max(1, maxTokenLength);
            }

            public string Segment { get; }
            public string LeadingDigits { get; }
            public int MaxTokenLength { get; }
        }

        private static readonly List<BookAlias> Aliases = BuildAliases();
        private static readonly Dictionary<int, int> BookChapterCounts = BuildBookChapterCounts();
        private static readonly AmbiguousOrdinalProfile[] AmbiguousOrdinalProfiles =
        {
            // `约一/约二/约三` can map to Johannine epistles, so `约 + [一/二/三]章` is intentionally abstained.
            new AmbiguousOrdinalProfile("约", "一二三", 2)
        };

        public static bool TryParse(string text, out BibleSpeechReference result)
        {
            ParseDecision decision = Parse(text);
            if (decision.Kind != ParseDecisionKind.Accept)
            {
                result = default;
                return false;
            }

            result = decision.Reference;
            return true;
        }

        private static ParseDecision Parse(string text)
        {
            BookCandidateScore noCandidate = BookCandidateScore.None;
            if (string.IsNullOrWhiteSpace(text))
            {
                return ParseDecision.Abstain(AbstainReasonCode.EmptyInput, noCandidate, noCandidate);
            }

            string input = NormalizeInput(text);
            if (!TryExtractBookAndTail(input, out string bookSegment, out string tail))
            {
                return ParseDecision.Abstain(AbstainReasonCode.InvalidStructure, noCandidate, noCandidate);
            }

            var bookMatch = FindBook(bookSegment, out BookCandidateScore topCandidate, out BookCandidateScore secondCandidate);
            if (bookMatch == null)
            {
                return ParseDecision.Abstain(AbstainReasonCode.BookNotFound, topCandidate, secondCandidate);
            }

            if (!TryParseChapterAndVerses(tail, out int chapter, out int startVerse, out int endVerse))
            {
                return ParseDecision.Abstain(AbstainReasonCode.InvalidChapterVerse, topCandidate, secondCandidate);
            }

            if (BookChapterCounts.TryGetValue(bookMatch.BookId, out int chapterCount) &&
                chapterCount > 0 &&
                chapter > chapterCount)
            {
                return ParseDecision.Abstain(AbstainReasonCode.ChapterOverflow, topCandidate, secondCandidate);
            }

            var reference = new BibleSpeechReference(
                bookMatch.BookId,
                chapter,
                startVerse,
                endVerse);
            return ParseDecision.Accept(reference, topCandidate, secondCandidate);
        }

        private static bool TryExtractBookAndTail(string input, out string bookSegment, out string tail)
        {
            bookSegment = string.Empty;
            tail = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            Match marker = ChapterMarkerRegex.Match(input);
            if (!marker.Success || marker.Index <= 0)
            {
                return false;
            }

            bookSegment = NormalizeBookSegment(input.Substring(0, marker.Index));
            if (string.IsNullOrWhiteSpace(bookSegment))
            {
                return false;
            }

            if (IsAmbiguousJohannineOrdinal(bookSegment, marker.Groups["num"].Value))
            {
                return false;
            }

            tail = input.Substring(marker.Index);
            return !string.IsNullOrWhiteSpace(tail);
        }

        private static bool IsAmbiguousJohannineOrdinal(string bookSegment, string chapterToken)
        {
            if (string.IsNullOrWhiteSpace(bookSegment) || string.IsNullOrWhiteSpace(chapterToken))
            {
                return false;
            }

            if (chapterToken.Contains('十') || chapterToken.Contains('百') || chapterToken.Contains('千'))
            {
                return false;
            }

            if (chapterToken.Length > 2)
            {
                return false;
            }

            char first = chapterToken[0];
            foreach (var profile in AmbiguousOrdinalProfiles)
            {
                if (!string.Equals(bookSegment, profile.Segment, StringComparison.Ordinal))
                {
                    continue;
                }

                if (chapterToken.Length > profile.MaxTokenLength)
                {
                    continue;
                }

                if (profile.LeadingDigits.IndexOf(first) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseChapterAndVerses(string tail, out int chapter, out int startVerse, out int endVerse)
        {
            chapter = 0;
            startVerse = 0;
            endVerse = 0;
            if (string.IsNullOrWhiteSpace(tail))
            {
                return false;
            }

            Match marker = ChapterMarkerRegex.Match(tail);
            if (!marker.Success || marker.Index != 0)
            {
                return false;
            }

            if (!TryParseNumber(marker.Groups["num"].Value, out int parsedChapter) || parsedChapter <= 0)
            {
                return false;
            }

            chapter = parsedChapter;
            string mark = marker.Groups["mark"].Value;
            string rest = tail.Substring(marker.Length).Trim();

            if (string.Equals(mark, ":", StringComparison.Ordinal))
            {
                return TryParseColonVerse(rest, out startVerse, out endVerse);
            }

            if (string.IsNullOrEmpty(rest))
            {
                startVerse = 1;
                endVerse = 0;
                return true;
            }

            return TryParseChapterVerseRest(rest, out startVerse, out endVerse);
        }

        private static bool TryParseColonVerse(string rest, out int startVerse, out int endVerse)
        {
            startVerse = 0;
            endVerse = 0;
            if (string.IsNullOrWhiteSpace(rest))
            {
                return false;
            }

            if (ContainsNonVerseUnit(rest))
            {
                return false;
            }

            var numbers = ExtractNumbers(rest);
            if (numbers.Count == 0)
            {
                return false;
            }

            bool hasRange = ContainsRangeToken(rest);
            if (hasRange && numbers.Count < 2)
            {
                return false;
            }

            startVerse = Math.Max(1, numbers[0]);
            endVerse = startVerse;
            if (numbers.Count >= 2 && hasRange)
            {
                endVerse = Math.Max(1, numbers[1]);
                if (endVerse < startVerse)
                {
                    (startVerse, endVerse) = (endVerse, startVerse);
                }
            }

            return true;
        }

        private static bool TryParseChapterVerseRest(string rest, out int startVerse, out int endVerse)
        {
            startVerse = 0;
            endVerse = 0;

            bool hasVerseKeyword = rest.Contains("节", StringComparison.Ordinal);
            var numbers = ExtractNumbers(rest);
            if (!hasVerseKeyword)
            {
                return false;
            }

            if (numbers.Count == 0)
            {
                return false;
            }

            bool hasRange = ContainsRangeToken(rest);
            if (hasRange && numbers.Count < 2)
            {
                return false;
            }

            startVerse = Math.Max(1, numbers[0]);
            endVerse = startVerse;
            if (numbers.Count >= 2 && hasRange)
            {
                endVerse = Math.Max(1, numbers[1]);
                if (endVerse < startVerse)
                {
                    (startVerse, endVerse) = (endVerse, startVerse);
                }
            }

            return true;
        }

        private static bool ContainsRangeToken(string text)
        {
            return text.Contains("到", StringComparison.Ordinal) ||
                   text.Contains("至", StringComparison.Ordinal) ||
                   text.Contains("-", StringComparison.Ordinal) ||
                   text.Contains("—", StringComparison.Ordinal) ||
                   text.Contains("~", StringComparison.Ordinal) ||
                   text.Contains("～", StringComparison.Ordinal);
        }

        private static bool ContainsNonVerseUnit(string text)
        {
            return text.Contains("分", StringComparison.Ordinal) ||
                   text.Contains("秒", StringComparison.Ordinal) ||
                   text.Contains("点", StringComparison.Ordinal) ||
                   text.Contains("页", StringComparison.Ordinal);
        }

        private static List<int> ExtractNumbers(string text)
        {
            var values = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return values;
            }

            foreach (Match match in NumberTokenRegex.Matches(text))
            {
                if (match == null || string.IsNullOrWhiteSpace(match.Value))
                {
                    continue;
                }

                if (TryParseNumber(match.Value, out int value) && value > 0)
                {
                    values.Add(value);
                }
            }

            return values;
        }

        private static bool TryParseNumber(string token, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (int.TryParse(token, out value))
            {
                return value > 0;
            }

            if (!token.Contains('十') && !token.Contains('百') && !token.Contains('千') && token.Length > 1)
            {
                int digitsValue = 0;
                foreach (char ch in token)
                {
                    if (!TryGetChineseDigitValue(ch, out int digit))
                    {
                        digitsValue = 0;
                        break;
                    }

                    digitsValue = (digitsValue * 10) + digit;
                }

                if (digitsValue > 0)
                {
                    value = digitsValue;
                    return true;
                }
            }

            int total = 0;
            int section = 0;
            int current = 0;

            foreach (char ch in token)
            {
                switch (ch)
                {
                    case '零':
                    case '〇':
                        current = 0;
                        break;
                    case '一': current = 1; break;
                    case '二':
                    case '两': current = 2; break;
                    case '三': current = 3; break;
                    case '四': current = 4; break;
                    case '五': current = 5; break;
                    case '六': current = 6; break;
                    case '七': current = 7; break;
                    case '八': current = 8; break;
                    case '九': current = 9; break;
                    case '十':
                        section += (current == 0 ? 1 : current) * 10;
                        current = 0;
                        break;
                    case '百':
                        section += (current == 0 ? 1 : current) * 100;
                        current = 0;
                        break;
                    case '千':
                        section += (current == 0 ? 1 : current) * 1000;
                        current = 0;
                        break;
                    default:
                        return false;
                }
            }

            total += section + current;
            value = total;
            return value > 0;
        }

        private static bool TryGetChineseDigitValue(char ch, out int value)
        {
            value = ch switch
            {
                '零' => 0,
                '〇' => 0,
                '一' => 1,
                '二' => 2,
                '两' => 2,
                '三' => 3,
                '四' => 4,
                '五' => 5,
                '六' => 6,
                '七' => 7,
                '八' => 8,
                '九' => 9,
                _ => -1
            };

            return value >= 0;
        }

        private static string NormalizeInput(string text)
        {
            return (text ?? string.Empty)
                .Trim()
                .Replace("：", ":")
                .Replace("，", "")
                .Replace("。", "")
                .Replace("、", "")
                .Replace(",", "")
                .Replace("  ", " ")
                .Replace(" ", "");
        }

        private static string NormalizeBookSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return string.Empty;
            }

            string normalized = segment
                .Replace("请打开", string.Empty, StringComparison.Ordinal)
                .Replace("打开", string.Empty, StringComparison.Ordinal)
                .Replace("请看", string.Empty, StringComparison.Ordinal)
                .Replace("我们看", string.Empty, StringComparison.Ordinal)
                .Replace("来看", string.Empty, StringComparison.Ordinal)
                .Replace("请读", string.Empty, StringComparison.Ordinal)
                .Replace("读", string.Empty, StringComparison.Ordinal)
                .Replace("第", string.Empty, StringComparison.Ordinal)
                .Replace("的", string.Empty, StringComparison.Ordinal)
                .Replace("菲利比", "腓立比", StringComparison.Ordinal)
                .Replace("非利比", "腓立比", StringComparison.Ordinal)
                .Replace("飞利比", "腓立比", StringComparison.Ordinal)
                .Replace("飞利笔", "腓立比", StringComparison.Ordinal)
                .Trim();

            bool removed;
            do
            {
                removed = false;
                foreach (string prefix in BookSegmentNoisePrefixes)
                {
                    if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        normalized = normalized.Substring(prefix.Length).Trim();
                        removed = true;
                    }
                }
            }
            while (removed && normalized.Length > 0);

            return normalized;
        }

        private static BookAlias FindBook(string segment, out BookCandidateScore topCandidate, out BookCandidateScore secondCandidate)
        {
            topCandidate = BookCandidateScore.None;
            secondCandidate = BookCandidateScore.None;
            if (string.IsNullOrWhiteSpace(segment))
            {
                return null;
            }

            var directMultiChar = Aliases
                .Where(a => a.Alias.Length >= 2 && string.Equals(a.Alias, segment, StringComparison.Ordinal))
                .OrderByDescending(a => a.Alias.Length)
                .ThenBy(a => a.BookId)
                .FirstOrDefault();
            if (directMultiChar != null)
            {
                topCandidate = new BookCandidateScore(directMultiChar.BookId, directMultiChar.Alias, 1);
                return directMultiChar;
            }

            var direct = Aliases
                .Where(a => a.Alias.Length >= 2 && segment.EndsWith(a.Alias, StringComparison.Ordinal))
                .OrderByDescending(a => a.Alias.Length)
                .ThenBy(a => a.BookId)
                .FirstOrDefault();
            if (direct != null)
            {
                topCandidate = new BookCandidateScore(direct.BookId, direct.Alias, 0.99);
                return direct;
            }

            var directSingle = Aliases
                .Where(a => a.Alias.Length == 1 && string.Equals(a.Alias, segment, StringComparison.Ordinal))
                .OrderBy(a => a.BookId)
                .FirstOrDefault();
            if (directSingle != null)
            {
                topCandidate = new BookCandidateScore(directSingle.BookId, directSingle.Alias, 0.98);
                return directSingle;
            }

            if (segment.Length < 2)
            {
                return null;
            }

            var candidates = Aliases
                .Where(a => !string.IsNullOrWhiteSpace(a.Alias) && a.Alias.Length >= 2)
                .Select(a => new { Alias = a, Score = ComputeAliasScore(segment, a) })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Alias.Alias.Length)
                .ThenBy(x => x.Alias.BookId)
                .Take(2)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var top = candidates[0];
            double second = candidates.Count > 1 ? candidates[1].Score : 0;
            topCandidate = new BookCandidateScore(top.Alias.BookId, top.Alias.Alias, top.Score);
            if (candidates.Count > 1)
            {
                var secondEntry = candidates[1];
                secondCandidate = new BookCandidateScore(secondEntry.Alias.BookId, secondEntry.Alias.Alias, secondEntry.Score);
            }

            if (top.Score >= 0.62 && (top.Score - second) >= 0.06)
            {
                return top.Alias;
            }

            return null;
        }

        private static List<BookAlias> BuildAliases()
        {
            var aliases = new List<BookAlias>();
            foreach (var book in BibleBookConfig.Books)
            {
                if (!string.IsNullOrWhiteSpace(book?.Name))
                {
                    string alias = book.Name.Trim();
                    aliases.Add(new BookAlias
                    {
                        BookId = book.BookId,
                        Alias = alias,
                        Initials = ToInitials(alias),
                        FullPinyin = ToFullPinyin(alias)
                    });
                }

                if (!string.IsNullOrWhiteSpace(book?.ShortName))
                {
                    string alias = book.ShortName.Trim();
                    aliases.Add(new BookAlias
                    {
                        BookId = book.BookId,
                        Alias = alias,
                        Initials = ToInitials(alias),
                        FullPinyin = ToFullPinyin(alias)
                    });
                }
            }

            var dedup = new Dictionary<string, BookAlias>(StringComparer.Ordinal);
            foreach (var alias in aliases.Where(a => !string.IsNullOrWhiteSpace(a.Alias)))
            {
                string key = $"{alias.BookId}:{alias.Alias}";
                if (!dedup.ContainsKey(key))
                {
                    dedup[key] = alias;
                }
            }

            return dedup.Values.ToList();
        }

        private static Dictionary<int, int> BuildBookChapterCounts()
        {
            var map = new Dictionary<int, int>();
            foreach (var book in BibleBookConfig.Books)
            {
                if (book != null && book.BookId > 0 && book.ChapterCount > 0)
                {
                    map[book.BookId] = book.ChapterCount;
                }
            }

            return map;
        }

        private static double ComputeAliasScore(string segment, BookAlias alias)
        {
            if (string.IsNullOrWhiteSpace(segment) || alias == null || string.IsNullOrWhiteSpace(alias.Alias))
            {
                return 0;
            }

            // ① 汉字字符级 Levenshtein
            int dist = LevenshteinDistance(segment, alias.Alias);
            int maxLen = Math.Max(segment.Length, alias.Alias.Length);
            if (maxLen <= 0) return 0;
            double charScore = 1.0 - (double)dist / maxLen;

            // ② 拼音首字母级（原有）
            string segInitials = ToInitials(segment);
            double initialsScore = 0;
            if (!string.IsNullOrWhiteSpace(segInitials) && !string.IsNullOrWhiteSpace(alias.Initials))
            {
                int pyDist = LevenshteinDistance(segInitials, alias.Initials);
                int pyMax = Math.Max(segInitials.Length, alias.Initials.Length);
                if (pyMax > 0)
                    initialsScore = 1.0 - (double)pyDist / pyMax;
            }

            // ③ 完整拼音音节级（新增）
            // 例："可林多前书" fullPinyin="kelinduoqianshu" vs "哥林多前书" fullPinyin="gelinduoqianshu"
            // Levenshtein距离=1，得分≈0.93，显著高于首字母对比的误差容忍上限
            string segFullPinyin = ToFullPinyin(segment);
            double fullPinyinScore = 0;
            if (!string.IsNullOrWhiteSpace(segFullPinyin) && !string.IsNullOrWhiteSpace(alias.FullPinyin))
            {
                int fpDist = LevenshteinDistance(segFullPinyin, alias.FullPinyin);
                int fpMax = Math.Max(segFullPinyin.Length, alias.FullPinyin.Length);
                if (fpMax > 0)
                    fullPinyinScore = 1.0 - (double)fpDist / fpMax;
            }

            // 综合取最优：字符匹配最权威，完整拼音次之，首字母最低
            double score = Math.Max(Math.Max(charScore, initialsScore * 0.95), fullPinyinScore * 0.97);

            if (segment.Length > 0 && alias.Alias.Length > 0 && segment[0] == alias.Alias[0])
                score += 0.03;

            if (alias.Alias.StartsWith(segment, StringComparison.Ordinal) ||
                segment.StartsWith(alias.Alias, StringComparison.Ordinal))
                score += 0.12;

            return Math.Clamp(score, 0, 1);
        }

        private static string ToInitials(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var chars = new List<char>(text.Length);
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                if (ch <= 127)
                {
                    if (char.IsLetterOrDigit(ch))
                    {
                        chars.Add(char.ToLowerInvariant(ch));
                    }
                    continue;
                }

                if (!PinyinHelper.IsChinese(ch))
                {
                    continue;
                }

                string pinyin = PinyinHelper.GetPinyin(ch.ToString());
                if (!string.IsNullOrWhiteSpace(pinyin))
                {
                    chars.Add(char.ToLowerInvariant(pinyin[0]));
                }
            }

            return new string(chars.ToArray());
        }

        /// <summary>
        /// 将汉字转为完整拼音音节串（无声调），非汉字字符原样保留小写字母/数字。
        /// 例："腓利比" → "feilibili"，"哥林多前书" → "gelinduoqianshu"
        /// 用途：在字符 Levenshtein 和首字母匹配均不足时，提供第三维度的相似度。
        /// </summary>
        private static string ToFullPinyin(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var sb = new StringBuilder(text.Length * 4);
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                    continue;

                if (ch <= 127)
                {
                    if (char.IsLetterOrDigit(ch))
                        sb.Append(char.ToLowerInvariant(ch));
                    continue;
                }

                if (!PinyinHelper.IsChinese(ch))
                    continue;

                string syllable = PinyinHelper.GetPinyin(ch.ToString());
                if (!string.IsNullOrWhiteSpace(syllable))
                    sb.Append(syllable.ToLowerInvariant());
            }

            return sb.ToString();
        }

        private static int LevenshteinDistance(string a, string b)
        {
            a ??= string.Empty;
            b ??= string.Empty;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[a.Length, b.Length];
        }
    }
}
