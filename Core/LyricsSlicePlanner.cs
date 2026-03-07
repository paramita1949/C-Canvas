using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageColorChanger.Core
{
    public static class LyricsSlicePlanner
    {
        public sealed class Segment
        {
            public int StartLine { get; init; }
            public int EndLine { get; init; }
            public string Text { get; init; } = string.Empty;
        }

        public static IReadOnlyList<string> NormalizeContentLines(string rawText)
        {
            return (rawText ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(StripDisplayPrefix)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        public static IReadOnlyList<int> NormalizeCutPoints(IEnumerable<int> cutPoints, int lineCount)
        {
            if (lineCount <= 1 || cutPoints == null)
            {
                return Array.Empty<int>();
            }

            var normalized = new SortedSet<int>();
            foreach (var point in cutPoints)
            {
                // Free cut-points are "end line of current slice".
                // Valid range: 1..lineCount-1 (last line cannot be a cut end).
                if (point >= 1 && point < lineCount)
                {
                    normalized.Add(point);
                }
            }

            return normalized.ToList();
        }

        public static IReadOnlyList<Segment> BuildSegments(
            IReadOnlyList<string> lines,
            int linesPerSlice,
            bool useFreeCutPoints,
            IEnumerable<int> cutPoints)
        {
            var segments = new List<Segment>();
            if (lines == null || lines.Count == 0)
            {
                return segments;
            }

            if (!useFreeCutPoints)
            {
                int group = Math.Max(1, linesPerSlice);
                for (int i = 0, idx = 0; i < lines.Count; i += group, idx++)
                {
                    var segmentLines = lines.Skip(i).Take(group).ToList();
                    segments.Add(new Segment
                    {
                        StartLine = i + 1,
                        EndLine = i + segmentLines.Count,
                        Text = string.Join(Environment.NewLine, segmentLines)
                    });
                }

                return segments;
            }

            var points = NormalizeCutPoints(cutPoints, lines.Count);
            int start = 1;
            foreach (var point in points)
            {
                int end = point;
                if (end >= start)
                {
                    var segmentLines = lines.Skip(start - 1).Take(end - start + 1).ToList();
                    segments.Add(new Segment
                    {
                        StartLine = start,
                        EndLine = end,
                        Text = string.Join(Environment.NewLine, segmentLines)
                    });
                }

                start = point + 1;
            }

            if (start <= lines.Count)
            {
                var tailLines = lines.Skip(start - 1).Take(lines.Count - start + 1).ToList();
                segments.Add(new Segment
                {
                    StartLine = start,
                    EndLine = lines.Count,
                    Text = string.Join(Environment.NewLine, tailLines)
                });
            }

            return segments;
        }

        public static string BuildMarkedText(
            IReadOnlyList<string> lines,
            IEnumerable<int> cutPoints,
            string markerPrefix = "🟡 ")
        {
            if (lines == null || lines.Count == 0)
            {
                return string.Empty;
            }

            var points = NormalizeCutPoints(cutPoints, lines.Count);
            var pointSet = new HashSet<int>(points);
            var rendered = lines.Select((line, index) =>
            {
                int lineNumber = index + 1;
                return pointSet.Contains(lineNumber)
                    ? markerPrefix + (line ?? string.Empty)
                    : (line ?? string.Empty);
            });

            return string.Join(Environment.NewLine, rendered);
        }

        public static string StripDisplayPrefix(string line)
        {
            string value = (line ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return string.Empty;
            }

            if (value.StartsWith("🟡 ", StringComparison.Ordinal))
            {
                value = value.Substring("🟡 ".Length).TrimStart();
            }
            else if (value.StartsWith("🟡", StringComparison.Ordinal))
            {
                value = value.Substring("🟡".Length).TrimStart();
            }
            else if (value.StartsWith("● ", StringComparison.Ordinal))
            {
                value = value.Substring("● ".Length).TrimStart();
            }
            else if (value.StartsWith("●", StringComparison.Ordinal))
            {
                value = value.Substring("●".Length).TrimStart();
            }
            else if (value.StartsWith("✂", StringComparison.Ordinal))
            {
                value = value.Substring(1).TrimStart();
            }

            int i = 0;
            while (i < value.Length && char.IsDigit(value[i]))
            {
                i++;
            }

            if (i > 0 && i < value.Length && value[i] == '.')
            {
                int next = i + 1;
                while (next < value.Length && char.IsWhiteSpace(value[next]))
                {
                    next++;
                }

                value = next < value.Length ? value.Substring(next) : string.Empty;
            }

            return value.Trim();
        }
    }
}
