using System;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiSermonSummaryServiceTests
    {
        [Fact]
        public void BuildSessionSummary_KeepsLatestAsrWindowInReadableSummary()
        {
            var service = new AiSermonSummaryService();
            var snapshot = new AiAsrSemanticWindowSnapshot(
                2,
                Array.Empty<AiAsrTurnEnvelope>(),
                "[10:00:01] 今天讲罗马书八章\n[10:00:09] 讲到万事互相效力",
                DateTimeOffset.Now);

            string summary = service.BuildSessionSummary("", snapshot);

            Assert.Contains("本场最近线索", summary, StringComparison.Ordinal);
            Assert.Contains("罗马书八章", summary, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildSpeakerStyleSummary_AddsConfirmedScriptureWithoutDuplicating()
        {
            var service = new AiSermonSummaryService();
            var candidate = new AiScriptureCandidate
            {
                BookName = "罗马书",
                Chapter = 8,
                StartVerse = 28,
                EndVerse = 28
            };

            string first = service.BuildSpeakerStyleSummary("", candidate);
            string second = service.BuildSpeakerStyleSummary(first, candidate);

            Assert.Contains("罗马书8章28节", first, StringComparison.Ordinal);
            Assert.Equal(first, second);
        }
    }
}
