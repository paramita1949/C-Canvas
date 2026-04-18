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
        // 定时器周期：3.0s（原4.5s）—— 缩短最大等待时间，让识别结果更快送达
        private readonly TimeSpan _segmentInterval = TimeSpan.FromSeconds(3.0);
        // 滑动窗口：识别失败时保留末尾 1.5s PCM 作为下一窗口的前缀上下文，
        // 防止说话内容恰好跨越窗口边界时被两个窗口各截一半而无法识别。
        // 16kHz / 16-bit / mono → 32 000 bytes/s × 1.5s = 48 000 bytes
        private const int OverlapPcmBytes = 48_000;
        private const int OverlapPcmBytesMedium = 80_000;
        private const int OverlapPcmBytesMax = 112_000;
        private int _consecutiveFailures;

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
            _consecutiveFailures = 0;
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
                var result = await _consumer.ProcessPcmAsync(pcmBytes, cancellationToken);

                // 滑动窗口：识别失败且本次有足够音频时，将末尾 1.5s 写回缓冲区头部
                // 作为下一窗口的前缀，防止说话内容恰好被窗口边界截断而丢失上下文。
                // 取消/停止场景通过 _isRunning 守卫排除。
                if (result.Success)
                {
                    _consecutiveFailures = 0;
                }
                else if (result.FailureReason != "canceled" && result.FailureReason != "audio-too-short")
                {
                    _consecutiveFailures++;
                }

                int overlapSize = _consecutiveFailures switch
                {
                    <= 1 => OverlapPcmBytes,
                    <= 3 => OverlapPcmBytesMedium,
                    _    => OverlapPcmBytesMax
                };

                if (!result.Success
                    && result.FailureReason != "canceled"
                    && pcmBytes.Length > overlapSize)
                {
                    int overlapStart = pcmBytes.Length - overlapSize;
                    var overlapBytes = new byte[overlapSize];
                    Array.Copy(pcmBytes, overlapStart, overlapBytes, 0, overlapSize);

                    lock (_gate)
                    {
                        if (_pcmBuffer != null && _isRunning)
                        {
                            // 在 API 调用期间已追加的新音频
                            byte[] newAudio = _pcmBuffer.ToArray();
                            _pcmBuffer.SetLength(0);
                            // overlap 前缀 + 新音频
                            _pcmBuffer.Write(overlapBytes, 0, overlapBytes.Length);
                            _pcmBuffer.Write(newAudio, 0, newAudio.Length);
                        }
                    }
                }

                return result;
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
