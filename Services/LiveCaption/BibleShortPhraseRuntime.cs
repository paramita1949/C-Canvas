using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.LiveCaption
{
    internal sealed class BibleShortPhraseRuntime : IDisposable
    {
        private readonly BibleShortPhraseConsumer _consumer;
        private readonly Action<BibleShortPhraseConsumer.Result> _onCompleted;
        private readonly object _gate = new();
        private MemoryStream _pcmBuffer;
        private IDisposable _subscription;
        private System.Threading.Timer _timer;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private bool _isProcessing;
        private bool _disposed;
        private readonly TimeSpan _segmentInterval = TimeSpan.FromSeconds(4.5);

        public BibleShortPhraseRuntime(
            BibleShortPhraseConsumer consumer,
            Action<BibleShortPhraseConsumer.Result> onCompleted = null)
        {
            _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
            _onCompleted = onCompleted ?? (_ => { });
        }

        public bool IsRunning => _isRunning;

        public void Start(SharedAudioCaptureSession session)
        {
            ThrowIfDisposed();
            if (_isRunning)
            {
                return;
            }

            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            _pcmBuffer?.Dispose();
            _pcmBuffer = new MemoryStream();
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _subscription = session.SubscribeChunk(OnAudioChunk);
            _timer = new System.Threading.Timer(OnTimerTick, null, _segmentInterval, _segmentInterval);
            _isRunning = true;
        }

        public async Task<BibleShortPhraseConsumer.Result> StopAsync(CancellationToken cancellationToken)
        {
            if (!_isRunning)
            {
                return new BibleShortPhraseConsumer.Result { FailureReason = "not-running" };
            }

            _isRunning = false;
            _subscription?.Dispose();
            _subscription = null;
            _timer?.Dispose();
            _timer = null;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            BibleShortPhraseConsumer.Result result = await FlushCurrentBufferAsync(cancellationToken);
            _onCompleted(result);
            return result;
        }

        private async void OnTimerTick(object state)
        {
            if (!_isRunning || _isProcessing)
            {
                return;
            }

            CancellationToken token = _cts?.Token ?? CancellationToken.None;
            BibleShortPhraseConsumer.Result result = await FlushCurrentBufferAsync(token);
            _onCompleted(result);
        }

        private async Task<BibleShortPhraseConsumer.Result> FlushCurrentBufferAsync(CancellationToken cancellationToken)
        {
            if (_isProcessing)
            {
                return new BibleShortPhraseConsumer.Result { FailureReason = "busy" };
            }

            byte[] pcmBytes;
            lock (_gate)
            {
                pcmBytes = _pcmBuffer?.ToArray() ?? Array.Empty<byte>();
                _pcmBuffer?.SetLength(0);
            }

            _isProcessing = true;
            try
            {
                return await _consumer.ProcessPcmAsync(pcmBytes, cancellationToken);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void OnAudioChunk(SharedAudioCaptureSession.AudioChunk chunk)
        {
            if (!_isRunning || chunk.BytesRecorded <= 0)
            {
                return;
            }

            lock (_gate)
            {
                _pcmBuffer?.Write(chunk.Buffer, 0, chunk.BytesRecorded);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscription?.Dispose();
            _subscription = null;
            _timer?.Dispose();
            _timer = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _pcmBuffer?.Dispose();
            _pcmBuffer = null;
            _isRunning = false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BibleShortPhraseRuntime));
            }
        }
    }
}
