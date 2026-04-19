using System;
using System.Collections.Generic;
using ImageColorChanger.Services.LiveCaption;
using NAudio.Wave;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class SharedAudioCaptureSessionTests
    {
        [Fact]
        public void SubscribeAndPublish_ForwardsPcmToAllSubscribers()
        {
            var session = new SharedAudioCaptureSession();
            var received1 = new List<int>();
            var received2 = new List<int>();

            using var sub1 = session.Subscribe(bytes => received1.Add(bytes.Length));
            using var sub2 = session.Subscribe(bytes => received2.Add(bytes.Length));

            session.PublishForTest(new byte[3200]);
            session.PublishForTest(new byte[1600]);

            Assert.Equal(new[] { 3200, 1600 }, received1);
            Assert.Equal(new[] { 3200, 1600 }, received2);
        }

        [Fact]
        public void DisposeSubscriber_StopsFurtherNotifications()
        {
            var session = new SharedAudioCaptureSession();
            var received = new List<int>();

            var subscription = session.Subscribe(bytes => received.Add(bytes.Length));
            session.PublishForTest(new byte[3200]);
            subscription.Dispose();
            session.PublishForTest(new byte[1600]);

            Assert.Equal(new[] { 3200 }, received);
        }

        [Fact]
        public void SetSelection_StoresSourceAndDeviceIds()
        {
            var session = new SharedAudioCaptureSession();

            session.SetSelection(LiveCaptionAudioSource.Microphone, "mic-1", "sys-1");

            Assert.Equal(LiveCaptionAudioSource.Microphone, session.CurrentSource);
            Assert.Equal("mic-1", session.CurrentInputDeviceId);
            Assert.Equal("sys-1", session.CurrentSystemDeviceId);
        }

        [Fact]
        public void Start_UsesConfiguredSelectionAndSetsRunningState()
        {
            var fakeCapture = new FakeWaveIn();
            var session = new SharedAudioCaptureSession((LiveCaptionAudioSource source, string inputId, string systemId, out string selectedName) =>
            {
                Assert.Equal(LiveCaptionAudioSource.Microphone, source);
                Assert.Equal("mic-1", inputId);
                Assert.Equal("sys-1", systemId);
                selectedName = "Mic One";
                return fakeCapture;
            });

            session.SetSelection(LiveCaptionAudioSource.Microphone, "mic-1", "sys-1");
            session.Start();

            Assert.True(session.IsRunning);
            Assert.True(fakeCapture.Started);
            Assert.Equal("Mic One", session.SelectedDeviceName);
        }

        [Fact]
        public void StartAndPublish_FromCapture_ForwardsToSubscribers()
        {
            var fakeCapture = new FakeWaveIn();
            var received = new List<int>();
            var session = new SharedAudioCaptureSession((LiveCaptionAudioSource source, string inputId, string systemId, out string selectedName) =>
            {
                selectedName = "Mic One";
                return fakeCapture;
            });
            using var subscription = session.Subscribe(bytes => received.Add(bytes.Length));

            session.Start();
            fakeCapture.RaiseData(new byte[3200], 3200);

            Assert.Equal(new[] { 3200 }, received);
        }

        [Fact]
        public void Stop_StopsAndDisposesCapture()
        {
            var fakeCapture = new FakeWaveIn();
            var session = new SharedAudioCaptureSession((LiveCaptionAudioSource source, string inputId, string systemId, out string selectedName) =>
            {
                selectedName = "Mic One";
                return fakeCapture;
            });

            session.Start();
            session.Stop();

            Assert.False(session.IsRunning);
            Assert.True(fakeCapture.Stopped);
            Assert.True(fakeCapture.Disposed);
        }

        [Fact]
        public void Start_WhenCaptureStartThrows_DoesNotLeaveSessionRunning()
        {
            var fakeCapture = new FakeWaveIn
            {
                ThrowOnStart = new InvalidOperationException("device busy")
            };
            var session = new SharedAudioCaptureSession((LiveCaptionAudioSource source, string inputId, string systemId, out string selectedName) =>
            {
                selectedName = "Busy Mic";
                return fakeCapture;
            });

            var ex = Record.Exception(() => session.Start());

            Assert.Null(ex);
            Assert.False(session.IsRunning);
            Assert.True(fakeCapture.Disposed);
            Assert.False(string.IsNullOrWhiteSpace(session.LastStartError));
        }

        [Fact]
        public void Start_WhenMicrophoneFails_FallsBackToSystemLoopback()
        {
            var micCapture = new FakeWaveIn
            {
                ThrowOnStart = new InvalidOperationException("mic busy")
            };
            var systemCapture = new FakeWaveIn();
            var session = new SharedAudioCaptureSession((LiveCaptionAudioSource source, string inputId, string systemId, out string selectedName) =>
            {
                if (source == LiveCaptionAudioSource.Microphone)
                {
                    selectedName = "Busy Mic";
                    return micCapture;
                }

                selectedName = "System Output";
                return systemCapture;
            });

            session.SetSelection(LiveCaptionAudioSource.Microphone, "mic-1", "sys-1");
            var ex = Record.Exception(() => session.Start());

            Assert.Null(ex);
            Assert.True(session.IsRunning);
            Assert.Equal(LiveCaptionAudioSource.SystemLoopback, session.CurrentSource);
            Assert.Equal("System Output", session.SelectedDeviceName);
            Assert.True(session.LastStartFallbackApplied);
            Assert.True(systemCapture.Started);
        }

        [Fact]
        public void SubscribeChunk_ProvidesFormatMetadata()
        {
            var session = new SharedAudioCaptureSession();
            SharedAudioCaptureSession.AudioChunk? received = null;

            using var sub = session.SubscribeChunk(chunk => received = chunk);
            session.PublishForTest(new byte[3200]);

            Assert.True(received.HasValue);
            Assert.Equal(3200, received.Value.BytesRecorded);
            Assert.NotNull(received.Value.Format);
            Assert.Equal(16000, received.Value.Format.SampleRate);
        }

        [Fact]
        public void Subscribe_ConvertsFloatStereoCaptureToPcm16Mono()
        {
            var fakeCapture = new FakeWaveIn
            {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
            };
            byte[] received = null;
            var session = new SharedAudioCaptureSession((LiveCaptionAudioSource source, string inputId, string systemId, out string selectedName) =>
            {
                selectedName = "Loopback";
                return fakeCapture;
            });
            using var sub = session.Subscribe(bytes => received = bytes);

            session.Start();

            float[] samples = new float[4800 * 2];
            for (int i = 0; i < samples.Length; i += 2)
            {
                samples[i] = 0.5f;
                samples[i + 1] = 0.5f;
            }

            byte[] buffer = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, buffer, 0, buffer.Length);
            fakeCapture.RaiseData(buffer, buffer.Length);

            Assert.NotNull(received);
            Assert.Equal(3200, received.Length);
        }

        [Fact]
        public void Subscribe_ConvertsFloatMultiChannelCaptureToPcm16Mono()
        {
            var fakeCapture = new FakeWaveIn
            {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 6)
            };
            byte[] received = null;
            var session = new SharedAudioCaptureSession((LiveCaptionAudioSource source, string inputId, string systemId, out string selectedName) =>
            {
                selectedName = "ASIO";
                return fakeCapture;
            });
            using var sub = session.Subscribe(bytes => received = bytes);

            session.Start();

            float[] samples = new float[4800 * 6];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = 0.25f;
            }

            byte[] buffer = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, buffer, 0, buffer.Length);

            var ex = Record.Exception(() => fakeCapture.RaiseData(buffer, buffer.Length));

            Assert.Null(ex);
            Assert.NotNull(received);
            Assert.Equal(3200, received.Length);
        }

        private sealed class FakeWaveIn : IWaveIn
        {
            public WaveFormat WaveFormat { get; set; } = new WaveFormat(16000, 16, 1);
            public event EventHandler<WaveInEventArgs> DataAvailable;
            public event EventHandler<StoppedEventArgs> RecordingStopped;
            public bool Started { get; private set; }
            public bool Stopped { get; private set; }
            public bool Disposed { get; private set; }
            public Exception ThrowOnStart { get; set; }

            public void StartRecording()
            {
                if (ThrowOnStart != null)
                {
                    throw ThrowOnStart;
                }
                Started = true;
            }

            public void StopRecording()
            {
                Stopped = true;
                RecordingStopped?.Invoke(this, new StoppedEventArgs());
            }

            public void Dispose()
            {
                Disposed = true;
            }

            public void RaiseData(byte[] buffer, int bytesRecorded)
            {
                DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytesRecorded));
            }
        }
    }
}
