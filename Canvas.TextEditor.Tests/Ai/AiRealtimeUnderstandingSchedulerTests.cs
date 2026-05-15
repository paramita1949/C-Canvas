using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiRealtimeUnderstandingSchedulerTests
    {
        [Fact]
        public async Task EnqueueAsync_WhenProcessorIsBusy_CoalescesPendingTurnsIntoLatestWindow()
        {
            var scheduler = new AiRealtimeUnderstandingScheduler(new AiAsrSemanticWindow(maxTurnCount: 10));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var processed = new List<AiAsrSemanticWindowSnapshot>();

            Task Processor(AiAsrSemanticWindowSnapshot snapshot, CancellationToken cancellationToken)
            {
                processed.Add(snapshot);
                if (processed.Count == 1)
                {
                    started.SetResult();
                    return releaseFirst.Task;
                }

                return Task.CompletedTask;
            }

            await scheduler.EnqueueAsync(CreateTurn("第一句，讲师开始讲罗马书八章。", 0), Processor, CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

            await scheduler.EnqueueAsync(CreateTurn("第二句，中间内容不应该单独排队。", 1), Processor, CancellationToken.None);
            await scheduler.EnqueueAsync(CreateTurn("第三句，最新内容应该合并进入下一次理解。", 2), Processor, CancellationToken.None);

            releaseFirst.SetResult();
            await scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(2, processed.Count);
            Assert.Equal(1, processed[0].Version);
            Assert.Equal(3, processed[1].Version);
            Assert.Contains("第二句", processed[1].WindowText, StringComparison.Ordinal);
            Assert.Contains("第三句", processed[1].WindowText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task EnqueueAsync_WhenProcessorFails_RecoversAndProcessesLaterWindow()
        {
            var scheduler = new AiRealtimeUnderstandingScheduler(new AiAsrSemanticWindow(maxTurnCount: 10));
            int attempts = 0;
            var processed = new List<string>();

            Task Processor(AiAsrSemanticWindowSnapshot snapshot, CancellationToken cancellationToken)
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                processed.Add(snapshot.WindowText);
                return Task.CompletedTask;
            }

            await scheduler.EnqueueAsync(CreateTurn("第一次请求会失败", 0), Processor, CancellationToken.None);
            await scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(3));

            await scheduler.EnqueueAsync(CreateTurn("失败后还能继续处理", 1), Processor, CancellationToken.None);
            await scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(3));

            Assert.Equal(2, attempts);
            Assert.Single(processed);
            Assert.Contains("失败后还能继续处理", processed[0], StringComparison.Ordinal);
        }

        private static AiAsrTurnEnvelope CreateTurn(string text, int seconds)
        {
            return new AiAsrTurnEnvelope
            {
                TurnId = $"turn-{seconds}",
                Text = text,
                IsFinal = true,
                CapturedAt = new DateTimeOffset(2026, 5, 14, 10, 0, seconds, TimeSpan.FromHours(8)),
                Source = "test"
            };
        }
    }
}
