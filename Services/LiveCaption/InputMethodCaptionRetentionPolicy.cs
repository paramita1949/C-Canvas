using System;
using System.Globalization;

namespace ImageColorChanger.Services.LiveCaption
{
    /// <summary>只决定稳定工作区的收缩边界，不操作控件或文件。</summary>
    internal static class InputMethodCaptionRetentionPolicy
    {
        public const int MaxWorkingChars = 16384;
        public const int RetainedChars = 8192;
        public const int UndoLimit = 50;

        public static int GetTrimLength(string text, int selectionStart)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= MaxWorkingChars) return 0;
            int trim = CaptionTextElements.FloorBoundary(text, text.Length - RetainedChars);
            // 优先在附近的段落边界收缩。无换行时保留文本元素边界和原始行进度。
            for (int i = trim - 1; i >= Math.Max(0, trim - 1024); i--)
            {
                if (text[i] != '\r' && text[i] != '\n') continue;
                trim = i + 1;
                if (text[i] == '\r' && trim < text.Length && text[trim] == '\n') trim++;
                break;
            }
            return trim > 0 && selectionStart >= trim ? trim : 0;
        }

        public static long GetLeadingTextElementsAfterTrim(string prefix, long previousLeadingElements)
        {
            int lineStart = 0;
            for (int i = prefix.Length - 1; i >= 0; i--)
            {
                if (prefix[i] != '\r' && prefix[i] != '\n') continue;
                lineStart = i + 1;
                break;
            }
            int count = StringInfo.ParseCombiningCharacters(prefix.Substring(lineStart)).Length;
            return lineStart > 0 ? count : previousLeadingElements + count;
        }
    }
}
