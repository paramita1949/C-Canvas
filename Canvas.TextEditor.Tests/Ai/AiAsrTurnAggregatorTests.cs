using System;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiAsrTurnAggregatorTests
    {
        [Fact]
        public void TryAccept_FinalText_ReturnsTurn()
        {
            var aggregator = new AiAsrTurnAggregator();

            bool accepted = aggregator.TryAccept(
                "我们今天讲罗马书八章二十八节，万事互相效力。",
                isFinal: true,
                capturedAt: new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.FromHours(8)),
                out var turn);

            Assert.True(accepted);
            Assert.NotNull(turn);
            Assert.True(turn.IsFinal);
            Assert.Equal("我们今天讲罗马书八章二十八节，万事互相效力。", turn.Text);
            Assert.StartsWith("asr-", turn.TurnId, StringComparison.Ordinal);
        }

        [Fact]
        public void TryAccept_DuplicateFinalText_IsRejected()
        {
            var aggregator = new AiAsrTurnAggregator();
            var capturedAt = new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.FromHours(8));

            Assert.True(aggregator.TryAccept("罗马书八章二十八节", true, capturedAt, out _));
            bool accepted = aggregator.TryAccept(" 罗马书八章二十八节 ", true, capturedAt.AddSeconds(1), out var turn);

            Assert.False(accepted);
            Assert.Null(turn);
        }

        [Fact]
        public void TryAccept_ShortNonReferenceText_IsRejected()
        {
            var aggregator = new AiAsrTurnAggregator();

            bool accepted = aggregator.TryAccept(
                "好的",
                isFinal: true,
                capturedAt: DateTimeOffset.Now,
                out var turn);

            Assert.False(accepted);
            Assert.Null(turn);
        }

        [Fact]
        public void TryAccept_ShortExplicitReference_IsAccepted()
        {
            var aggregator = new AiAsrTurnAggregator();

            bool accepted = aggregator.TryAccept(
                "约3章",
                isFinal: true,
                capturedAt: DateTimeOffset.Now,
                out var turn);

            Assert.True(accepted);
            Assert.Equal("约3章", turn.Text);
        }

        [Fact]
        public void TryAccept_InterimOnly_DoesNotImmediatelyForward()
        {
            var aggregator = new AiAsrTurnAggregator();
            var t0 = new DateTimeOffset(2026, 5, 12, 0, 30, 0, TimeSpan.FromHours(8));

            bool accepted = aggregator.TryAccept(
                "我们的中国人跟犹太人有的习俗是很相信很相近的",
                isFinal: false,
                capturedAt: t0,
                out var turn);

            Assert.False(accepted);
            Assert.Null(turn);
        }

        [Fact]
        public void TryFlushPendingInterim_AfterSilence_IsAccepted()
        {
            var aggregator = new AiAsrTurnAggregator();
            var t0 = new DateTimeOffset(2026, 5, 12, 0, 30, 0, TimeSpan.FromHours(8));

            Assert.False(aggregator.TryAccept(
                "interim text for first aggregation baseline sample one and rich enough",
                isFinal: false,
                capturedAt: t0,
                out _));

            bool accepted = aggregator.TryFlushPendingInterim(
                t0.AddSeconds(3),
                out var turn);

            Assert.True(accepted);
            Assert.NotNull(turn);
            Assert.False(turn.IsFinal);
            Assert.Equal("realtime-asr-interim-silence", turn.Source);
        }

        [Fact]
        public void TryFlushPendingInterim_TooEarly_IsRejected()
        {
            var aggregator = new AiAsrTurnAggregator();
            var t0 = new DateTimeOffset(2026, 5, 12, 0, 30, 0, TimeSpan.FromHours(8));

            Assert.False(aggregator.TryAccept(
                "interim text for first aggregation baseline sample one and rich enough",
                isFinal: false,
                capturedAt: t0,
                out _));

            bool accepted = aggregator.TryFlushPendingInterim(
                t0.AddMilliseconds(800),
                out var turn);

            Assert.False(accepted);
            Assert.Null(turn);
        }

        [Fact]
        public void TryFlushPendingInterim_DefaultLatencyFlushesAfterOneSecond()
        {
            var aggregator = new AiAsrTurnAggregator();
            var t0 = new DateTimeOffset(2026, 5, 12, 0, 30, 0, TimeSpan.FromHours(8));

            Assert.False(aggregator.TryAccept(
                "实时ASR已经形成足够长的一段内容，需要尽快交给AI理解。",
                isFinal: false,
                capturedAt: t0,
                out _));

            bool accepted = aggregator.TryFlushPendingInterim(t0.AddSeconds(1), out var turn);

            Assert.True(accepted);
            Assert.NotNull(turn);
            Assert.False(turn.IsFinal);
        }

        [Fact]
        public void TryFlushPendingInterim_DefaultCadenceAllowsFourSecondUpdates()
        {
            var aggregator = new AiAsrTurnAggregator();
            var t0 = new DateTimeOffset(2026, 5, 12, 0, 30, 0, TimeSpan.FromHours(8));

            Assert.False(aggregator.TryAccept(
                "第一段实时ASR已经足够长，需要先形成一次AI理解。",
                isFinal: false,
                capturedAt: t0,
                out _));
            Assert.True(aggregator.TryFlushPendingInterim(t0.AddSeconds(1), out _));

            Assert.False(aggregator.TryAccept(
                "第二段实时ASR继续推进讲章内容，不能等到八秒以后才处理。",
                isFinal: false,
                capturedAt: t0.AddSeconds(4),
                out _));

            bool accepted = aggregator.TryFlushPendingInterim(t0.AddSeconds(5), out var turn);

            Assert.True(accepted);
            Assert.NotNull(turn);
        }
    }
}
