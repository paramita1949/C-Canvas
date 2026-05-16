using System;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiSermonSummaryServiceTests
    {
        [Fact]
        public void BuildSessionSummary_UsesAssistantUnderstandingAsPrimarySignal()
        {
            var service = new AiSermonSummaryService();
            var snapshot = new AiAsrSemanticWindowSnapshot(
                2,
                Array.Empty<AiAsrTurnEnvelope>(),
                "[10:00:01] 今天继续在信心里往前走\n[10:00:09] 要落到行动",
                DateTimeOffset.Now);

            string summary = service.BuildSessionSummary(
                "",
                snapshot,
                "当前重点在信心与行动落地，不是停留在概念解释。");

            Assert.Contains("本场摘要", summary, StringComparison.Ordinal);
            Assert.Contains("AI判断", summary, StringComparison.Ordinal);
            Assert.Contains("信心与行动落地", summary, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildSpeakerStyleSummary_AddsStyleLineWithoutDuplicating()
        {
            var service = new AiSermonSummaryService();
            var snapshot = new AiAsrSemanticWindowSnapshot(
                3,
                Array.Empty<AiAsrTurnEnvelope>(),
                "这个地方继续强调行动和应用，不要只停留在理解。",
                DateTimeOffset.Now);

            string first = service.BuildSpeakerStyleSummary(
                "",
                "讲员结构很清晰，反复强调行动落地。",
                snapshot);
            string second = service.BuildSpeakerStyleSummary(
                first,
                "讲员结构很清晰，反复强调行动落地。",
                snapshot);

            Assert.Contains("风格特征", first, StringComparison.Ordinal);
            Assert.Equal(first, second);
        }

        [Fact]
        public void BuildSpeakerStyleSummary_AddsScripturePreferenceForPrediction()
        {
            var service = new AiSermonSummaryService();
            var romanCandidate = new AiScriptureCandidate
            {
                BookId = 45,
                BookName = "罗马书",
                Chapter = 8,
                StartVerse = 28,
                EndVerse = 28
            };
            var johnCandidate = new AiScriptureCandidate
            {
                BookId = 43,
                BookName = "约翰福音",
                Chapter = 15,
                StartVerse = 5,
                EndVerse = 5
            };

            string first = service.BuildSpeakerStyleSummary("", "", null, romanCandidate);
            string second = service.BuildSpeakerStyleSummary(first, "", null, johnCandidate);

            Assert.Contains("讲师画像", second, StringComparison.Ordinal);
            Assert.Contains("经文倾向", second, StringComparison.Ordinal);
            Assert.Contains("新约", second, StringComparison.Ordinal);
            Assert.Contains("高频书卷", second, StringComparison.Ordinal);
            Assert.Contains("高频章节", second, StringComparison.Ordinal);
            Assert.Contains("预测提示", second, StringComparison.Ordinal);
        }
    }
}
