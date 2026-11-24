using System;
using System.Text;

namespace ImageColorChanger.Services.LiveCaption
{
    /// <summary>识别文本的合并规则；不依赖控件、存档或投影排版。</summary>
    internal static class CaptionRecognitionTextPolicy
    {
        internal static bool ShouldRotateDraftToNextUtterance(string currentDraft, string incoming)
        {
            if (string.IsNullOrWhiteSpace(currentDraft) || string.IsNullOrWhiteSpace(incoming))
            {
                return false;
            }

            if (incoming.StartsWith(currentDraft, StringComparison.Ordinal) ||
                currentDraft.StartsWith(incoming, StringComparison.Ordinal))
            {
                return false;
            }

            int overlap = FindOverlap(currentDraft, incoming);
            if (overlap >= 3)
            {
                return false;
            }

            // 新片段明显更短，且与旧草稿无前缀/后缀关系，可判定为“下一句话开始”。
            if (incoming.Length + 3 < currentDraft.Length)
            {
                return true;
            }

            // 极短片段重启（如“在宁波”“他”）也视为新句启动，避免显示冻结。
            return incoming.Length <= 8 && currentDraft.Length >= 20;
        }

        internal static string MergeDraftStable(string currentDraft, string incoming)
        {
            if (string.IsNullOrWhiteSpace(incoming))
            {
                return currentDraft;
            }

            if (string.IsNullOrWhiteSpace(currentDraft))
            {
                return incoming;
            }

            if (incoming.StartsWith(currentDraft, StringComparison.Ordinal))
            {
                return incoming;
            }

            if (currentDraft.StartsWith(incoming, StringComparison.Ordinal))
            {
                // ASR中间结果经常回缩，直接保持较长内容，减少闪烁。
                return currentDraft;
            }

            int overlap = FindOverlap(currentDraft, incoming);
            if (overlap >= 3 && overlap < incoming.Length)
            {
                return currentDraft + incoming.Substring(overlap);
            }

            // 尽量保留已有草稿，只在明显新增时替换，避免一句话来回重写。
            if (incoming.Length + 3 < currentDraft.Length)
            {
                return currentDraft;
            }

            return incoming;
        }

        internal static int FindOverlap(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return 0;
            }

            int max = Math.Min(left.Length, right.Length);
            for (int len = max; len >= 1; len--)
            {
                if (string.Compare(left, left.Length - len, right, 0, len, StringComparison.Ordinal) == 0)
                {
                    if (CaptionTextElements.FloorBoundary(left, left.Length - len) != left.Length - len ||
                        CaptionTextElements.FloorBoundary(right, len) != len) continue;
                    return len;
                }
            }

            return 0;
        }

        internal static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(input.Length);
            bool previousSpace = false;
            foreach (char raw in input)
            {
                char c = raw;
                if (c == '\r' || c == '\n' || char.IsWhiteSpace(c))
                {
                    if (previousSpace)
                    {
                        continue;
                    }

                    sb.Append(' ');
                    previousSpace = true;
                    continue;
                }

                previousSpace = false;
                sb.Append(c);
            }

            return sb.ToString().Trim();
        }
    }
}
