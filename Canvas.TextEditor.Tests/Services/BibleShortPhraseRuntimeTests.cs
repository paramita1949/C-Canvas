using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BibleShortPhraseRuntimeTests
    {
        [Fact]
        public async Task StopAsync_ForwardsAccumulatedPcmToConsumer()
        {
            byte[] received = null;
            var consumer = new BibleShortPhraseConsumer(
                new BibleShortPhraseConsumerTests.FakeBibleServiceAccessor().Create(),
                (wav, ct) =>
                {
                    received = wav;
                    return Task.FromResult(string.Empty);
                });
            var session = new SharedAudioCaptureSession();
            var runtime = new BibleShortPhraseRuntime(consumer);

            runtime.Start(session);
            session.PublishForTest(new byte[3200]);
            await runtime.StopAsync(CancellationToken.None);

            Assert.NotNull(received);
            Assert.True(received.Length > 3200);
        }

        [Fact]
        public async Task StopAsync_InvokesCompletionCallback()
        {
            BibleShortPhraseConsumer.Result callbackResult = null;
            var consumer = new BibleShortPhraseConsumer(
                new BibleShortPhraseConsumerTests.FakeBibleServiceAccessor().Create(),
                (wav, ct) => Task.FromResult("约翰福音三章十六节"));
            var session = new SharedAudioCaptureSession();
            var runtime = new BibleShortPhraseRuntime(consumer, result => callbackResult = result);

            runtime.Start(session);
            session.PublishForTest(new byte[6400]);
            await runtime.StopAsync(CancellationToken.None);

            Assert.NotNull(callbackResult);
            Assert.Equal("约翰福音三章十六节", callbackResult.RecognizedText);
        }

        [Fact]
        public async Task TimerTick_InvokesCompletionCallback_WhenRecognitionFails()
        {
            BibleShortPhraseConsumer.Result callbackResult = null;
            var consumer = new BibleShortPhraseConsumer(
                new BibleShortPhraseConsumerTests.FakeBibleServiceAccessor().Create(),
                (wav, ct) => Task.FromResult(string.Empty));
            var session = new SharedAudioCaptureSession();
            var runtime = new BibleShortPhraseRuntime(consumer, result => callbackResult = result);

            runtime.Start(session);
            session.PublishForTest(new byte[6400]);

            MethodInfo tick = typeof(BibleShortPhraseRuntime).GetMethod(
                "OnTimerTick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(tick);
            tick.Invoke(runtime, new object[] { null });

            DateTime deadline = DateTime.UtcNow.AddSeconds(1);
            while (callbackResult == null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.NotNull(callbackResult);
            Assert.False(callbackResult.Success);
            Assert.Equal("empty-transcript", callbackResult.FailureReason);

            await runtime.StopAsync(CancellationToken.None);
        }
    }
}
