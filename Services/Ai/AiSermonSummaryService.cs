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
            "呼召", "应用", "落地", "祷告", "行动", "互动", "提问", "安慰", "劝勉", "提醒"
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
            builder.AppendLine("讲师画像：");
            builder.AppendLine("- " + BuildScripturePreference(scriptureEvidence));
            builder.AppendLine("- " + BuildStylePreference(styleEvidence));
            builder.AppendLine("- " + BuildPredictionHint(scriptureEvidence, styleEvidence));

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

        private static string BuildScripturePreference(IReadOnlyList<string> evidence)
        {
            if (evidence == null || evidence.Count == 0)
            {
                return "经文倾向：暂未形成稳定偏好。";
            }

            var parsed = evidence
                .Select(ParseScriptureEvidence)
                .Where(item => item != null)
                .ToList();
            if (parsed.Count == 0)
            {
                return "经文倾向：已有经文线索，但尚不足以判断新约/旧约偏好。";
            }

            string testament = parsed
                .GroupBy(item => item.Testament)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .First().Key;
            string book = parsed
                .GroupBy(item => item.Book)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .First().Key;
            string chapter = parsed
                .Where(item => item.Chapter > 0)
                .GroupBy(item => $"{item.Book}{item.Chapter}章")
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.Key)
                .FirstOrDefault() ?? "暂无稳定章节";

            return $"经文倾向：更常触及{testament}；高频书卷：{book}；高频章节：{chapter}。";
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

        private static string BuildPredictionHint(
            IReadOnlyList<string> scriptureEvidence,
            IReadOnlyList<string> styleEvidence)
        {
            string scripture = BuildScripturePreference(scriptureEvidence)
                .Replace("经文倾向：", string.Empty)
                .TrimEnd('。');
            string style = BuildStylePreference(styleEvidence)
                .Replace("讲章风格：", string.Empty)
                .TrimEnd('。');
            return $"预测提示：触发该讲师时，优先结合{scripture}，并留意其{style}的表达习惯。";
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

        private static ScriptureEvidence ParseScriptureEvidence(string line)
        {
            string value = (line ?? string.Empty).Trim();
            if (!value.StartsWith("经文证据：", StringComparison.Ordinal))
            {
                return null;
            }

            string[] parts = value.Substring("经文证据：".Length).Split('|');
            if (parts.Length < 4)
            {
                return null;
            }

            int.TryParse(parts[2], out int chapter);
            return new ScriptureEvidence(parts[0], parts[1], chapter);
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

        private sealed record ScriptureEvidence(string Testament, string Book, int Chapter);
    }
}
