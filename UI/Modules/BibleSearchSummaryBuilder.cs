using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageColorChanger.UI.Modules
{
    public sealed class BibleSearchSummaryBuilder : IBibleSearchSummaryBuilder
    {
        public string BuildSnippet(string scripture, IReadOnlyList<string> keywords, int maxLength = 48, int contextBefore = 12)
        {
            if (string.IsNullOrWhiteSpace(scripture))
            {
                return string.Empty;
            }

            if (maxLength <= 0)
            {
                return string.Empty;
            }

            var normalizedKeywords = (keywords ?? Array.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .ToList();

            int hitIndex = FindFirstMatchIndex(scripture, normalizedKeywords);
            int startIndex = 0;

            if (hitIndex >= 0)
            {
                startIndex = Math.Max(0, hitIndex - Math.Max(0, contextBefore));
            }

            int availableLength = scripture.Length - startIndex;
            int takeLength = Math.Min(maxLength, Math.Max(0, availableLength));
            string snippet = scripture.Substring(startIndex, takeLength);

            if (startIndex > 0)
            {
                snippet = "..." + snippet;
            }

            if (startIndex + takeLength < scripture.Length)
            {
                snippet += "...";
            }

            return snippet;
        }

        private static int FindFirstMatchIndex(string text, IReadOnlyList<string> keywords)
        {
            if (keywords == null || keywords.Count == 0)
            {
                return -1;
            }

            int earliest = -1;
            foreach (var keyword in keywords)
            {
                int index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                if (earliest < 0 || index < earliest)
                {
                    earliest = index;
                }
            }

            return earliest;
        }
    }
}
