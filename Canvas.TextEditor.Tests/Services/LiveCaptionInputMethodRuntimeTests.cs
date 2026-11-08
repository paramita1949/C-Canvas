using System.Collections.Concurrent;
using System.Reflection;
using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionInputMethodRuntimeTests
    {
        [Fact]
        public async Task Abort_WithPendingPcm_DropsAudioWithoutTranscriptionOrCallback()
        {
            int transcriptions = 0;
            int callbacks = 0;
            var consumer = CreateConsumer((wav, token) =>
            {
                Interlocked.Increment(ref transcriptions);
                return Task.FromResult(string.Empty);
            });
            using var session = new SharedAudioCaptureSession();
            using var runtime = new BibleShortPhraseRuntime(consumer, _ => Interlocked.Increment(ref callbacks));
            runtime.Start(session);
            session.PublishForTest(new byte[6400]);

            runtime.Abort();
            session.PublishForTest(new byte[6400]);
            await TickAsync(runtime);

            Assert.False(runtime.IsRunning);
            Assert.Equal(0, transcriptions);
            Assert.Equal(0, callbacks);
        }

        [Fact]
        public async Task AbortAndRestart_DelayedOldFailure_DoesNotEmitOrRestoreOldOverlap()
        {
            var delayed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var received = new ConcurrentQueue<byte[]>();
            var callbacks = new ConcurrentQueue<BibleShortPhraseConsumer.Result>();
            CancellationToken oldToken = default;
            var consumer = CreateConsumer((wav, token) =>
            {
                received.Enqueue(wav);
                if (received.Count == 1)
                {
                    oldToken = token;
                    return delayed.Task;
                }

                return Task.FromResult(string.Empty);
            });
            using var session = new SharedAudioCaptureSession();
            using var runtime = new BibleShortPhraseRuntime(consumer, callbacks.Enqueue);
            runtime.Start(session);
            session.PublishForTest(Enumerable.Repeat((byte)1, 64_000).ToArray());
            Task oldTick = TickAsync(runtime);
            Assert.Single(received);

            runtime.Abort();
            Assert.True(oldToken.IsCancellationRequested);
            runtime.Start(session);
            byte[] freshAudio = Enumerable.Repeat((byte)2, 6400).ToArray();
            session.PublishForTest(freshAudio);
            delayed.SetResult(string.Empty);
            await oldTick.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Empty(callbacks);
            await TickAsync(runtime);
            Assert.Single(callbacks);
            Assert.Equal(2, received.Count);
            Assert.Equal(BibleShortPhraseConsumer.BuildWavFromPcm16kMono(freshAudio), received.Last());
        }

        [Fact]
        public async Task AbortAndRestart_OldCompletion_DoesNotReleaseNewProcessingSlot()
        {
            var oldResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var newResult = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            int transcriptions = 0;
            var callbacks = new ConcurrentQueue<BibleShortPhraseConsumer.Result>();
            var consumer = CreateConsumer((wav, token) =>
            {
                return Interlocked.Increment(ref transcriptions) switch
                {
                    1 => oldResult.Task,
                    2 => newResult.Task,
                    _ => Task.FromResult(string.Empty)
                };
            });
            using var session = new SharedAudioCaptureSession();
            using var runtime = new BibleShortPhraseRuntime(consumer, callbacks.Enqueue);
            runtime.Start(session);
            session.PublishForTest(new byte[6400]);
            Task oldTick = TickAsync(runtime);

            runtime.Abort();
            runtime.Start(session);
            session.PublishForTest(new byte[6400]);
            Task newTick = TickAsync(runtime);
            Assert.Equal(2, transcriptions);

            oldResult.SetResult(string.Empty);
            await oldTick.WaitAsync(TimeSpan.FromSeconds(2));
            session.PublishForTest(new byte[6400]);
            await TickAsync(runtime);
            Assert.Equal(2, transcriptions);
            Assert.Empty(callbacks);

            newResult.SetResult(string.Empty);
            await newTick.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(callbacks);
            await runtime.StopAsync(CancellationToken.None);
            Assert.Equal(3, transcriptions);
            Assert.Equal(2, callbacks.Count);
        }

        private static BibleShortPhraseConsumer CreateConsumer(Func<byte[], CancellationToken, Task<string>> transcribe)
        {
            return new BibleShortPhraseConsumer(
                new BibleShortPhraseConsumerTests.FakeBibleServiceAccessor().Create(),
                transcribe);
        }

        private static Task TickAsync(BibleShortPhraseRuntime runtime)
        {
            MethodInfo tick = typeof(BibleShortPhraseRuntime).GetMethod("OnTimerTick", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(tick);
            var context = new TimerCompletionContext();
            SynchronizationContext previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                tick.Invoke(runtime, new object[] { null });
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            return context.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        private sealed class TimerCompletionContext : SynchronizationContext
        {
            public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override void OperationCompleted() => Completed.TrySetResult();
        }
    }
}
