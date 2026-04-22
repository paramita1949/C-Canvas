using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ImageColorChanger.Services
{
    internal static class BibleSpeechTextNormalizer
    {
        private static readonly Regex ChapterTokenRegex =
            new(@"(?<num>[零〇一二两三四五六七八九十百千万0-9]+)张", RegexOptions.Compiled);

        private static readonly Regex VerseTokenRegex =
            new(@"(?<num>[零〇一二两三四五六七八九十百千万0-9]+)(姐|街)", RegexOptions.Compiled);

        public static IReadOnlyList<string> BuildReferenceCandidates(string text, bool aggressive, bool allowInferVerseUnit = true)
        {
            var ordered = new List<string>(6);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Add(string value)
            {
                string candidate = (value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return;
                }

                if (seen.Add(candidate))
                {
                    ordered.Add(candidate);
                }
            }

            Add(text);

            string normalized = NormalizeForReference(text, aggressive);
            Add(normalized);

            if (allowInferVerseUnit && TryInferVerseUnit(normalized, out string inferredFromNormalized))
            {
                Add(inferredFromNormalized);
            }

            if (allowInferVerseUnit &&
                !string.Equals(normalized, text, StringComparison.Ordinal) &&
                TryInferVerseUnit(text, out string inferredFromRaw))
            {
                Add(inferredFromRaw);
            }

            return ordered;
        }

        public static string NormalizeForReference(string text, bool aggressive)
        {
            string input = (text ?? string.Empty).Trim();
            if (input.Length == 0)
            {
                return string.Empty;
            }

            string output = input
                .Replace("：", ":")
                .Replace("。", string.Empty)
                .Replace("，", string.Empty)
                .Replace("、", string.Empty)
                .Replace(",", string.Empty)
                .Replace(" ", string.Empty);

            if (!aggressive)
            {
                return output;
            }

            output = output
                .Replace("福应", "福音", StringComparison.Ordinal)
                .Replace("福英", "福音", StringComparison.Ordinal)
                .Replace("伏音", "福音", StringComparison.Ordinal)
                .Replace("副音", "福音", StringComparison.Ordinal)
                .Replace("菲利比", "腓立比", StringComparison.Ordinal)
                .Replace("非利比", "腓立比", StringComparison.Ordinal)
                .Replace("飞利比", "腓立比", StringComparison.Ordinal)
                .Replace("飞利笔", "腓立比", StringComparison.Ordinal);

            output = ChapterTokenRegex.Replace(output, "${num}章");
            output = VerseTokenRegex.Replace(output, "${num}节");

            return output;
        }

        public static bool TryInferVerseUnit(string source, out string inferred)
        {
            inferred = string.Empty;
            string input = (source ?? string.Empty).Trim();
            if (input.Length == 0 || input.Contains("节", StringComparison.Ordinal))
            {
                return false;
            }

            int chapterMarkerIndex = input.LastIndexOf('章');
            if (chapterMarkerIndex < 0 || chapterMarkerIndex >= input.Length - 1)
            {
                return false;
            }

            string tail = input.Substring(chapterMarkerIndex + 1).Trim();
            if (!IsNumericLikeToken(tail))
            {
                return false;
            }

            inferred = input + "节";
            return true;
        }

        private static bool IsNumericLikeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (char.IsDigit(c))
                {
                    continue;
                }

                if ("零〇一二两三四五六七八九十百千万".IndexOf(c) >= 0)
                {
                    continue;
                }

                return false;
            }

            return true;
        }
    }
}
