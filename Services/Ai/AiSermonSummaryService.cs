using System;
using System.Linq;
using System.Text;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiSermonSummaryService
    {
        private const int MaxSummaryLength = 1600;

        public string BuildSessionSummary(string existingSummary, AiAsrSemanticWindowSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.WindowText))
            {
                return existingSummary ?? string.Empty;
            }

            string latest = Trim(snapshot.WindowText, 700);
            string combined = string.IsNullOrWhiteSpace(existingSummary)
                ? $"本场最近线索：\n{latest}"
                : $"{Trim(existingSummary, 850)}\n\n本场最近线索：\n{latest}";
            return Trim(combined, MaxSummaryLength);
        }

        public string BuildSpeakerStyleSummary(string existingSummary, AiScriptureCandidate candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.BookName))
            {
                return existingSummary ?? string.Empty;
            }

            string reference = FormatReference(candidate);
            string current = existingSummary ?? string.Empty;
            if (current.Contains(reference, StringComparison.Ordinal))
            {
                return current;
            }

            string line = $"常引用或本次明确提到：{reference}";
            if (string.IsNullOrWhiteSpace(current))
            {
                return line;
            }

            return Trim(current.TrimEnd() + "\n" + line, MaxSummaryLength);
        }

        private static string FormatReference(AiScriptureCandidate candidate)
        {
            var builder = new StringBuilder();
            builder.Append(candidate.BookName.Trim());
            if (candidate.Chapter > 0)
            {
                builder.Append(candidate.Chapter).Append('章');
            }

            if (candidate.StartVerse > 0)
            {
                builder.Append(candidate.StartVerse);
                if (candidate.EndVerse > candidate.StartVerse)
                {
                    builder.Append('-').Append(candidate.EndVerse);
                }
                builder.Append('节');
            }

            return builder.ToString();
        }

        private static string Trim(string text, int maxLength)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(Math.Max(0, value.Length - maxLength));
        }
    }
}
