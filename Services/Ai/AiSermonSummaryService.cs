using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiSermonSummaryService
    {
        private const int MaxSummaryLength = 1600;
        private const int MaxEvidenceCount = 12;
        private static readonly string[] StyleKeywords =
        {
            "结构", "层次", "递进", "主线", "对比", "比喻", "例子", "重复", "强调",
            "呼召", "应用", "落地", "祷告", "行动", "互动", "提问", "安慰", "劝勉", "提醒",
            "经历", "见证", "故事", "生活", "场景", "家庭", "关系", "服事", "信心", "悔改", "苦难",
            "逐节", "串联", "主题", "结论"
        };
        private static readonly string[] AccentCorrectionKeywords =
        {
            "口音", "方言", "发音", "同音", "近音", "连读", "误识别", "纠错", "听成"
        };

        public string BuildSessionSummary(
            string existingSummary,
            AiAsrSemanticWindowSnapshot snapshot,
            string assistantUnderstanding)
        {
            if ((snapshot == null || string.IsNullOrWhiteSpace(snapshot.WindowText)) &&
                string.IsNullOrWhiteSpace(assistantUnderstanding))
            {
                return existingSummary ?? string.Empty;
            }

            string latestSignal = ExtractSessionSignal(snapshot?.WindowText, assistantUnderstanding);
            if (string.IsNullOrWhiteSpace(latestSignal))
            {
                return existingSummary ?? string.Empty;
            }

            string current = string.IsNullOrWhiteSpace(existingSummary)
                ? "本场摘要：\n- 起始阶段，正在建立主题理解。"
                : Trim(existingSummary, 1200);

            if (current.Contains(latestSignal, StringComparison.Ordinal))
            {
                return current;
            }

            string merged = $"{current}\n- {latestSignal}";
            return Trim(merged, MaxSummaryLength);
        }

        public string BuildSpeakerStyleSummary(
            string existingSummary,
            string assistantUnderstanding,
            AiAsrSemanticWindowSnapshot snapshot,
            AiScriptureCandidate scriptureCandidate = null)
        {
            string understanding = (assistantUnderstanding ?? string.Empty).Trim();
            string asr = (snapshot?.WindowText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(understanding) &&
                string.IsNullOrWhiteSpace(asr) &&
                scriptureCandidate == null)
            {
                return existingSummary ?? string.Empty;
            }

            var scriptureEvidence = ExtractExistingLines(existingSummary, "经文证据：");
            string candidateLine = BuildScriptureEvidenceLine(scriptureCandidate);
            if (!string.IsNullOrWhiteSpace(candidateLine) &&
                !scriptureEvidence.Contains(candidateLine, StringComparer.Ordinal))
            {
                scriptureEvidence.Add(candidateLine);
            }

            string styleLine = BuildStyleLine(understanding, asr);
            var styleEvidence = ExtractExistingLines(existingSummary, "风格特征：");
            if (!string.IsNullOrWhiteSpace(styleLine) &&
                !styleEvidence.Contains(styleLine, StringComparer.Ordinal))
            {
                styleEvidence.Add(styleLine);
            }

            if (scriptureEvidence.Count == 0 && styleEvidence.Count == 0)
            {
                return existingSummary ?? string.Empty;
            }

            scriptureEvidence = scriptureEvidence.TakeLast(MaxEvidenceCount).ToList();
            styleEvidence = styleEvidence.TakeLast(MaxEvidenceCount).ToList();
            return Trim(BuildSpeakerProfile(scriptureEvidence, styleEvidence), MaxSummaryLength);
        }

        private static string ExtractSessionSignal(string asrWindow, string assistantUnderstanding)
        {
            string ai = NormalizeSentence(assistantUnderstanding, 120);
            if (!string.IsNullOrWhiteSpace(ai))
            {
                return $"AI判断：{ai}";
            }

            string asr = NormalizeSentence(asrWindow, 90);
            if (!string.IsNullOrWhiteSpace(asr))
            {
                return $"语义线索：{asr}";
            }

            return string.Empty;
        }

        private static string BuildStyleLine(string understanding, string asr)
        {
            var source = $"{understanding}\n{asr}";
            if (AccentCorrectionKeywords.Any(keyword => source.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                string accentSignal = NormalizeSentence(understanding, 130);
                if (string.IsNullOrWhiteSpace(accentSignal))
                {
                    accentSignal = NormalizeSentence(asr, 100);
                }

                return string.IsNullOrWhiteSpace(accentSignal)
                    ? string.Empty
                    : $"风格特征：口音纠错线索：{accentSignal}";
            }

            var hits = StyleKeywords
                .Where(keyword => source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToList();

            if (hits.Count > 0)
            {
                return $"风格特征：常见“{string.Join("、", hits)}”表达。";
            }

            string fallback = NormalizeSentence(understanding, 80);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return $"风格特征：{fallback}";
            }

            return string.Empty;
        }

        private static string BuildSpeakerProfile(
            IReadOnlyList<string> scriptureEvidence,
            IReadOnlyList<string> styleEvidence)
        {
            var builder = new StringBuilder();
            builder.AppendLine("传道人总结：");
            builder.AppendLine("- " + BuildMethodSummary(styleEvidence));
            builder.AppendLine("- " + BuildExpressionSummary(styleEvidence));
            builder.AppendLine("- " + BuildStylePreference(styleEvidence));
            builder.AppendLine("- " + BuildContentPreference(scriptureEvidence, styleEvidence));
            builder.AppendLine("- " + BuildScriptureUsageSummary(scriptureEvidence, styleEvidence));
            builder.AppendLine("- " + BuildAccentCorrectionSummary(styleEvidence));

            foreach (string line in scriptureEvidence)
            {
                builder.AppendLine(line);
            }

            foreach (string line in styleEvidence)
            {
                builder.AppendLine(line);
            }

            return builder.ToString().Trim();
        }

        private static string BuildMethodSummary(IReadOnlyList<string> evidence)
        {
            if (evidence == null || evidence.Count == 0)
            {
                return "讲道方法：正在累积讲道方法线索。";
            }

            var methods = new[]
                {
                    ("结构", "结构推进"),
                    ("层次", "分层展开"),
                    ("递进", "递进铺陈"),
                    ("对比", "对比说明"),
                    ("例子", "例证解释"),
                    ("比喻", "比喻解释"),
                    ("提问", "提问引导"),
                    ("应用", "应用落地"),
                    ("行动", "行动回应")
                }
                .Where(pair => evidence.Any(line => line.Contains(pair.Item1, StringComparison.Ordinal)))
                .Select(pair => pair.Item2)
                .Distinct(StringComparer.Ordinal)
                .Take(4)
                .ToList();

            return methods.Count == 0
                ? "讲道方法：需要继续累积结构、例证、互动和应用线索。"
                : $"讲道方法：常用{string.Join("、", methods)}。";
        }

        private static string BuildStylePreference(IReadOnlyList<string> evidence)
        {
            if (evidence == null || evidence.Count == 0)
            {
                return "讲章风格：暂未形成稳定风格。";
            }

            var keywords = evidence
                .SelectMany(line => StyleKeywords.Where(keyword => line.Contains(keyword, StringComparison.Ordinal)))
                .GroupBy(keyword => keyword)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Take(4)
                .Select(group => group.Key)
                .ToList();
            if (keywords.Count == 0)
            {
                return "讲章风格：需要继续累积表达证据。";
            }

            return $"讲章风格：常见{string.Join("、", keywords)}。";
        }

        private static string BuildAccentCorrectionSummary(IReadOnlyList<string> evidence)
        {
            if (evidence == null || evidence.Count == 0)
            {
                return "口音纠错：正在累积普通话口音、同音近音和ASR误识别线索。";
            }

            string latestSignal = evidence
                .LastOrDefault(line => AccentCorrectionKeywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
            if (string.IsNullOrWhiteSpace(latestSignal))
            {
                return "口音纠错：暂未形成稳定线索。";
            }

            string signal = latestSignal
                .Replace("风格特征：", string.Empty, StringComparison.Ordinal)
                .Replace("口音纠错线索：", string.Empty, StringComparison.Ordinal)
                .Trim()
                .TrimEnd('。');
            return $"口音纠错：{Trim(signal, 140)}。";
        }

        private static string BuildExpressionSummary(IReadOnlyList<string> evidence)
        {
            if (evidence == null || evidence.Count == 0)
            {
                return "表达手法：正在累积表达手法线索。";
            }

            var expressions = new[]
                {
                    ("例子", "常用例子"),
                    ("经历", "个人经历"),
                    ("见证", "见证故事"),
                    ("故事", "故事叙述"),
                    ("生活", "生活场景"),
                    ("场景", "现场场景"),
                    ("比喻", "比喻类比"),
                    ("重复", "反复强调"),
                    ("强调", "重点强调"),
                    ("提问", "现场提问"),
                    ("互动", "互动引导")
                }
                .Where(pair => evidence.Any(line => line.Contains(pair.Item1, StringComparison.Ordinal)))
                .Select(pair => pair.Item2)
                .Distinct(StringComparer.Ordinal)
                .Take(4)
                .ToList();

            return expressions.Count == 0
                ? "表达手法：需要继续累积例子、经历、见证、比喻和互动线索。"
                : $"表达手法：常见{string.Join("、", expressions)}。";
        }

        private static string BuildContentPreference(
            IReadOnlyList<string> scriptureEvidence,
            IReadOnlyList<string> styleEvidence)
        {
            var contentTags = StyleKeywords
                .Where(keyword => styleEvidence != null && styleEvidence.Any(line => line.Contains(keyword, StringComparison.Ordinal)))
                .Where(keyword => keyword is "呼召" or "应用" or "落地" or "行动" or "安慰" or "劝勉" or "提醒" or "祷告" or "家庭" or "关系" or "服事" or "信心" or "悔改" or "苦难")
                .Distinct(StringComparer.Ordinal)
                .Take(4)
                .ToList();

            if (contentTags.Count > 0)
            {
                return $"内容偏向：更常落在{string.Join("、", contentTags)}。";
            }

            if (scriptureEvidence != null && scriptureEvidence.Count > 0)
            {
                return "内容偏向：会结合已确认经文推进主题，但暂不总结高频经文。";
            }

            return "内容偏向：正在累积主题、应用和牧养重点。";
        }

        private static string BuildScriptureUsageSummary(
            IReadOnlyList<string> scriptureEvidence,
            IReadOnlyList<string> styleEvidence)
        {
            var usages = new[]
                {
                    ("逐节", "逐节讲解"),
                    ("串联", "串联多处经文"),
                    ("主题", "先讲主题再带经文"),
                    ("结论", "用经文作结论"),
                    ("结构", "用经文组织结构"),
                    ("应用", "经文后接应用")
                }
                .Where(pair => styleEvidence != null && styleEvidence.Any(line => line.Contains(pair.Item1, StringComparison.Ordinal)))
                .Select(pair => pair.Item2)
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToList();

            if (usages.Count > 0)
            {
                return $"经文使用方式：常见{string.Join("、", usages)}。";
            }

            if (scriptureEvidence != null && scriptureEvidence.Count > 0)
            {
                return "经文使用方式：已有确认经文线索，正在累积使用方式。";
            }

            return "经文使用方式：正在累积线索。";
        }

        private static string BuildScriptureEvidenceLine(AiScriptureCandidate candidate)
        {
            if (candidate == null || candidate.BookId <= 0)
            {
                return string.Empty;
            }

            var book = BibleBookConfig.GetBook(candidate.BookId);
            if (book == null)
            {
                return string.Empty;
            }

            var reference = new StringBuilder();
            reference.Append(book.Name);
            if (candidate.Chapter > 0)
            {
                reference.Append(candidate.Chapter).Append('章');
            }

            if (candidate.StartVerse > 0)
            {
                reference.Append(candidate.StartVerse);
                if (candidate.EndVerse > candidate.StartVerse)
                {
                    reference.Append('-').Append(candidate.EndVerse);
                }

                reference.Append('节');
            }

            return $"经文证据：{book.Testament}|{book.Name}|{candidate.Chapter}|{reference}";
        }

        private static List<string> ExtractExistingLines(string summary, string prefix)
        {
            return (summary ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string NormalizeSentence(string value, int maxLength)
        {
            string text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            text = Regex.Replace(text, "\\s+", " ");
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...";
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
