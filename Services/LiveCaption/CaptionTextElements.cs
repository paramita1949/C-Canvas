using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ImageColorChanger.Services.LiveCaption
{
    internal readonly struct CaptionTextElement
    {
        public CaptionTextElement(string text, int sourceStart)
        {
            Text = text;
            SourceStart = sourceStart;
        }
        public string Text { get; }
        public int SourceStart { get; }
    }

    internal readonly struct CaptionTextLine
    {
        public CaptionTextLine(string text, int sourceStart)
        {
            Text = text;
            SourceStart = sourceStart;
        }
        public string Text { get; }
        public int SourceStart { get; }
    }

    /// <summary>行容量按文本元素计数；所有来源位置仍为原字符串的 UTF-16 下标。</summary>
    internal static class CaptionTextElements
    {
        public static IEnumerable<CaptionTextElement> Enumerate(string text, int sourceStart = 0)
        {
            var iterator = StringInfo.GetTextElementEnumerator(text ?? string.Empty);
            while (iterator.MoveNext())
                yield return new CaptionTextElement(iterator.GetTextElement(), sourceStart + iterator.ElementIndex);
        }

        public static IEnumerable<CaptionTextLine> ReadLines(string text)
        {
            string source = text ?? string.Empty;
            int start = 0;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != '\r' && source[i] != '\n') continue;
                yield return new CaptionTextLine(source.Substring(start, i - start), start);
                if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n') i++;
                start = i + 1;
            }
            yield return new CaptionTextLine(source.Substring(start), start);
        }

        public static string TakeLastWrappedLines(string text, int lineLimit, int maxLines)
            => TakeLastWrappedLines(text, lineLimit, maxLines, 0);

        public static string TakeLastWrappedLines(string text, int lineLimit, int maxLines, long firstLineOffset)
        {
            int limit = Math.Max(1, lineLimit);
            int count = Math.Max(1, maxLines);
            var visible = new List<string>(count);
            // 从尾部读取所需段落，不为早已不可见的每一行和每一个字分配字符串。
            foreach (CaptionTextLine rawLine in ReadLinesBackwards(text))
            {
                int[] starts = StringInfo.ParseCombiningCharacters(rawLine.Text);
                int firstCapacity = limit - (rawLine.SourceStart == 0 ? (int)(Math.Max(0, firstLineOffset) % limit) : 0);
                int rows = starts.Length <= firstCapacity ? 1 : 1 + (starts.Length - firstCapacity + limit - 1) / limit;
                for (int row = rows - 1; row >= 0 && visible.Count < count; row--)
                {
                    int elementStart = row == 0 ? 0 : firstCapacity + (row - 1) * limit;
                    int elementEnd = Math.Min(starts.Length, elementStart + (row == 0 ? firstCapacity : limit));
                    int start = elementStart < starts.Length ? starts[elementStart] : rawLine.Text.Length;
                    int end = elementEnd < starts.Length ? starts[elementEnd] : rawLine.Text.Length;
                    visible.Add(rawLine.Text.Substring(start, end - start));
                }
                if (visible.Count == count) break;
            }
            visible.Reverse();
            return string.Join(Environment.NewLine, visible);
        }

        private static IEnumerable<CaptionTextLine> ReadLinesBackwards(string text)
        {
            string source = text ?? string.Empty;
            int end = source.Length;
            for (int i = source.Length - 1; i >= 0; i--)
            {
                if (source[i] != '\r' && source[i] != '\n') continue;
                yield return new CaptionTextLine(source.Substring(i + 1, end - i - 1), i + 1);
                if (source[i] == '\n' && i > 0 && source[i - 1] == '\r') i--;
                end = i;
            }
            yield return new CaptionTextLine(source.Substring(0, end), 0);
        }

        public static int FloorBoundary(string text, int offset)
        {
            string source = text ?? string.Empty;
            int safe = Math.Clamp(offset, 0, source.Length);
            if (safe == 0 || safe == source.Length) return safe;
            int[] starts = StringInfo.ParseCombiningCharacters(source);
            int position = Array.BinarySearch(starts, safe);
            return position >= 0 ? safe : starts[Math.Max(0, ~position - 1)];
        }

        public static int CeilingBoundary(string text, int offset)
        {
            string source = text ?? string.Empty;
            int safe = Math.Clamp(offset, 0, source.Length);
            if (safe == 0 || safe == source.Length) return safe;
            int[] starts = StringInfo.ParseCombiningCharacters(source);
            int position = Array.BinarySearch(starts, safe);
            return position >= 0 ? safe : (~position < starts.Length ? starts[~position] : source.Length);
        }

        public static string ApplyLetterSpacing(string text, double spacing, out int[] indexMap)
        {
            string source = text ?? string.Empty;
            indexMap = new int[source.Length + 1];
            if (source.Length == 0 || spacing <= 0.01)
            {
                for (int i = 0; i <= source.Length; i++) indexMap[i] = i;
                return source;
            }

            string spacer = new string('\u200A', Math.Clamp((int)Math.Round(spacing), 1, 10));
            var output = new StringBuilder(source.Length);
            bool previousWasText = false;
            foreach (CaptionTextElement element in Enumerate(source))
            {
                bool isLineBreak = element.Text.IndexOfAny(new[] { '\r', '\n' }) >= 0;
                if (previousWasText && !isLineBreak) output.Append(spacer);
                for (int i = element.SourceStart; i < element.SourceStart + element.Text.Length; i++)
                    indexMap[i] = output.Length;
                output.Append(element.Text);
                previousWasText = !isLineBreak;
            }
            indexMap[source.Length] = output.Length;
            return output.ToString();
        }
    }
}
