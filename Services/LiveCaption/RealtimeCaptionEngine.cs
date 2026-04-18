using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using ImageColorChanger.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ImageColorChanger.Services.LiveCaption
{
    internal enum LiveCaptionAudioSource
    {
        SystemLoopback = 0,
        Microphone = 1
    }

    internal sealed class RealtimeCaptionEngine : IDisposable
    {
        private readonly object _bufferGate = new();
        private readonly CliProxyApiClient _client;
        private readonly MemoryStream _pcmBuffer = new();
        private readonly MemoryStream _realtimePcmBuffer = new();
        private readonly object _subtitleGate = new();
        private readonly System.Timers.Timer _flushTimer;
        private readonly ElapsedEventHandler _flushTimerHandler;
        private static readonly WaveFormat RealtimeTargetFormat = new WaveFormat(16000, 16, 1);
        private int _realtimePacketBytes = 3200; // default 100ms @ 16k/16bit/mono
        private int _realtimeTickMs = 100;
        private int _realtimeMaxQueueBytes = 3200 * 40; // up to 4s
        private const double SilenceDbThreshold = -47.0;
        private const double MinChunkMs = 1700.0;
        private const double SentenceSilenceMs = 420.0;
        private const double MaxChunkMs = 5200.0;
        private const double MinSpeechMs = 260.0;
        private IWaveIn _capture;
        private IDisposable _sharedAudioSubscription;
        private WaveFormat _captureFormat;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private bool _isInFlight;
        private string _lastSubtitle = string.Empty;
        private DateTime _lastAudioUtc = DateTime.MinValue;
        private DateTime _lastDebugUtc = DateTime.MinValue;
        private bool _useRealtimeSession;
        private int _chunkCount;
        private int _chunkSentCount;
        private int _chunkTextCount;
        private int _dataEventCount;
        private float _lastPeakDb = -90f;
        private float _lastRmsDb = -90f;
        private double _bufferMs;
        private double _speechMs;
        private double _tailSilenceMs;
        private string _captureInfo = "capture=none";
        private DateTime _realtimeLastKeepAliveUtc = DateTime.MinValue;
        private DateTime _realtimeNextReconnectUtc = DateTime.MinValue;
        private bool _realtimeReconnectInFlight;
        private int _realtimeReconnectAttempt;
        private bool _realtimeSessionStartInFlight;
        private DateTime _realtimeConnectGraceUntilUtc = DateTime.MinValue;
        private volatile bool _isDisposing;
        private volatile bool _isDisposed;
        private const int AliyunFinalMergeWindowMs = 1300;
        private readonly List<string> _aliyunPendingFinalSegments = new();
        private DateTime _aliyunLastFinalUtc = DateTime.MinValue;

        public event Action<LiveCaptionAsrText> SubtitleUpdated;
        public event Action<string> StatusChanged;
        public event Action<string> DebugInfoUpdated;

        public RealtimeCaptionEngine(ConfigManager config)
        {
            _client = new CliProxyApiClient(config, useRealtimeSettings: true);
            // 高频轮询：实时链路按 200ms 包（6400 bytes）发送。
            _flushTimer = new System.Timers.Timer(200);
            _flushTimer.AutoReset = true;
            _flushTimerHandler = async (_, _) => await FlushChunkSafeAsync();
            _flushTimer.Elapsed += _flushTimerHandler;
        }

        private void ConfigureRealtimeProfile()
        {
            // 极速优先：
            // - 百度：100ms 包（3200 bytes），降低首字延迟
            // - 腾讯：40ms 包（1280 bytes，官方实时建议）
            if (string.Equals(_client.AsrProvider, "baidu", StringComparison.OrdinalIgnoreCase))
            {
                _realtimeTickMs = 100;
                _realtimePacketBytes = 3200;
                _realtimeMaxQueueBytes = _realtimePacketBytes * 40;
            }
            else if (string.Equals(_client.AsrProvider, "aliyun", StringComparison.OrdinalIgnoreCase))
            {
                _realtimeTickMs = 100;
                _realtimePacketBytes = 3200;
                _realtimeMaxQueueBytes = _realtimePacketBytes * 40;
            }
            else if (string.Equals(_client.AsrProvider, "doubao", StringComparison.OrdinalIgnoreCase))
            {
                // 豆包官方建议分包 100~200ms；默认使用 200ms，优先稳定性。
                _realtimeTickMs = 200;
                _realtimePacketBytes = 6400;
                _realtimeMaxQueueBytes = _realtimePacketBytes * 30;
            }
            else if (string.Equals(_client.AsrProvider, "funasr", StringComparison.OrdinalIgnoreCase))
            {
                // FunASR 本地 websocket 推荐较稳妥分包，兼顾延迟与稳定。
                _realtimeTickMs = 100;
                _realtimePacketBytes = 3200;
                _realtimeMaxQueueBytes = _realtimePacketBytes * 40;
            }
            else
            {
                _realtimeTickMs = 40;
                _realtimePacketBytes = 1280;
                _realtimeMaxQueueBytes = _realtimePacketBytes * 100;
            }

            _flushTimer.Interval = _realtimeTickMs;
        }

        public bool IsRunning => _isRunning;

        public bool IsConfigured => _client.IsReady;

        public static IReadOnlyList<SharedAudioCaptureSession.InputAudioDeviceInfo> EnumerateInputDevices()
        {
            return SharedAudioCaptureSession.EnumerateInputDevices();
        }

        public static IReadOnlyList<SharedAudioCaptureSession.InputAudioDeviceInfo> EnumerateSystemLoopbackDevices()
        {
            return SharedAudioCaptureSession.EnumerateSystemLoopbackDevices();
        }

        public void Start(LiveCaptionAudioSource source, string preferredInputDeviceId = null, string preferredSystemDeviceId = null)
        {
            if (_isDisposed || _isDisposing)
            {
                StatusChanged?.Invoke("实时字幕引擎正在释放，请稍后重试");
                return;
            }

            if (_isRunning)
            {
                return;
            }

            if (!_client.IsReady)
            {
                StatusChanged?.Invoke("实时字幕未启动：请先在 AI 配置中完成服务商凭据");
                return;
            }

            try
            {
                string selectedName;
                _capture = SharedAudioCaptureSession.CreateCaptureForSelection(
                    source,
                    preferredInputDeviceId,
                    preferredSystemDeviceId,
                    out selectedName);
                _captureInfo = source == LiveCaptionAudioSource.SystemLoopback
                    ? $"capture=system:{selectedName}"
                    : $"capture=input:{selectedName}";
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"音频采集初始化失败: {ex.Message}");
                return;
            }
            _captureFormat = _capture.WaveFormat;
            _capture.DataAvailable += Capture_DataAvailable;
            _capture.RecordingStopped += Capture_RecordingStopped;
            _captureInfo = $"{_captureInfo}, fmt={_captureFormat.SampleRate}Hz/{_captureFormat.BitsPerSample}bit/{_captureFormat.Channels}ch/{_captureFormat.Encoding}";

            InitializeRunningState(source);

            _capture.StartRecording();
            PublishStartedStatus(source);
        }

        internal void StartWithSharedCapture(SharedAudioCaptureSession session)
        {
            if (_isDisposed || _isDisposing)
            {
                StatusChanged?.Invoke("实时字幕引擎正在释放，请稍后重试");
                return;
            }

            if (_isRunning)
            {
                return;
            }

            if (session == null)
            {
                StatusChanged?.Invoke("实时字幕未启动：共享采集会话不存在");
                return;
            }

            if (!_client.IsReady)
            {
                StatusChanged?.Invoke("实时字幕未启动：请先在 AI 配置中完成服务商凭据");
                return;
            }

            _capture = null;
            _captureFormat = new WaveFormat(16000, 16, 1);
            _captureInfo = session.CurrentSource == LiveCaptionAudioSource.SystemLoopback
                ? $"capture=shared-system:{session.SelectedDeviceName}"
                : $"capture=shared-input:{session.SelectedDeviceName}";
            _captureInfo = $"{_captureInfo}, fmt={_captureFormat.SampleRate}Hz/{_captureFormat.BitsPerSample}bit/{_captureFormat.Channels}ch/{_captureFormat.Encoding}";

            InitializeRunningState(session.CurrentSource);
            _sharedAudioSubscription = session.SubscribeChunk(ProcessSharedAudioChunk);
            PublishStartedStatus(session.CurrentSource);
        }

        private void InitializeRunningState(LiveCaptionAudioSource source)
        {
            _cts = new CancellationTokenSource();
            _isInFlight = false;
            _useRealtimeSession = _client.SupportsRealtimeSession;
            ConfigureRealtimeProfile();
            _lastSubtitle = string.Empty;
            lock (_subtitleGate)
            {
                _aliyunPendingFinalSegments.Clear();
                _aliyunLastFinalUtc = DateTime.MinValue;
            }
            _chunkCount = 0;
            _chunkSentCount = 0;
            _chunkTextCount = 0;
            _dataEventCount = 0;
            _lastPeakDb = -90f;
            _lastRmsDb = -90f;
            _bufferMs = 0;
            _speechMs = 0;
            _tailSilenceMs = 0;
            _lastAudioUtc = DateTime.MinValue;
            _lastDebugUtc = DateTime.MinValue;
            _realtimeLastKeepAliveUtc = DateTime.MinValue;
            _realtimeNextReconnectUtc = DateTime.MinValue;
            _realtimeReconnectInFlight = false;
            _realtimeReconnectAttempt = 0;
            _realtimeSessionStartInFlight = false;
            _realtimeConnectGraceUntilUtc = DateTime.MinValue;
            lock (_bufferGate)
            {
                _pcmBuffer.SetLength(0);
                _realtimePcmBuffer.SetLength(0);
            }

            _flushTimer.Start();
            _isRunning = true;
            if (_useRealtimeSession)
            {
                _realtimeSessionStartInFlight = true;
                _realtimeConnectGraceUntilUtc = DateTime.UtcNow.AddSeconds(2);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool ok = await _client.StartRealtimeSessionAsync(
                            HandleRealtimeText,
                            msg => StatusChanged?.Invoke(msg),
                            _cts.Token);
                        if (!ok && !string.IsNullOrWhiteSpace(_client.LastError))
                        {
                            _useRealtimeSession = false;
                            lock (_bufferGate)
                            {
                                _realtimePcmBuffer.SetLength(0);
                            }
                            StatusChanged?.Invoke("实时链路不可用，已回退分片识别模式");
                        }
                    }
                    finally
                    {
                        _realtimeSessionStartInFlight = false;
                    }
                });
            }
            PublishDebugInfo("started");
        }

        private void PublishStartedStatus(LiveCaptionAudioSource source)
        {
            if (_useRealtimeSession)
            {
                StatusChanged?.Invoke(source == LiveCaptionAudioSource.SystemLoopback
                    ? "实时字幕已启动（系统声卡-实时）"
                    : "实时字幕已启动（输入设备-实时）");
            }
            else
            {
                StatusChanged?.Invoke(source == LiveCaptionAudioSource.SystemLoopback
                    ? "实时字幕已启动（系统声卡）"
                    : "实时字幕已启动（输入设备）");
            }
        }

        public void Stop()
        {
            if (_isDisposed || _isDisposing)
            {
                return;
            }

            if (!_isRunning)
            {
                return;
            }

            TryFlushAliyunMergedFinal(force: true);
            _isRunning = false;
            _flushTimer.Stop();
            _cts?.Cancel();
            if (_useRealtimeSession)
            {
                try
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _client.StopRealtimeSessionAsync(CancellationToken.None);
                        }
                        catch
                        {
                            // ignore
                        }
                    }).Wait(1200);
                }
                catch
                {
                    // ignore
                }
            }

            if (_capture != null)
            {
                var capture = _capture;
                _capture = null!;
                try
                {
                    capture.DataAvailable -= Capture_DataAvailable;
                    capture.RecordingStopped -= Capture_RecordingStopped;
                }
                catch
                {
                }

                try
                {
                    capture.StopRecording();
                }
                catch (Exception ex)
                {
                    RaiseStatusSafe($"实时字幕采集停止异常: {ex.Message}");
                }

                try
                {
                    capture.Dispose();
                }
                catch (Exception ex)
                {
                    RaiseStatusSafe($"实时字幕采集释放异常: {ex.Message}");
                    RaiseDebugSafe($"[stop] capture dispose exception: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (_sharedAudioSubscription != null)
            {
                try
                {
                    _sharedAudioSubscription.Dispose();
                }
                catch
                {
                }
                _sharedAudioSubscription = null;
            }

            lock (_bufferGate)
            {
                _pcmBuffer.SetLength(0);
                _realtimePcmBuffer.SetLength(0);
                _bufferMs = 0;
                _speechMs = 0;
                _tailSilenceMs = 0;
            }

            RaiseStatusSafe("实时字幕已停止");
            PublishDebugInfo("stopped");
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            ProcessAudioChunk(e?.Buffer, e?.BytesRecorded ?? 0, _captureFormat);
        }

        internal void ProcessSharedAudioChunk(SharedAudioCaptureSession.AudioChunk chunk)
        {
            ProcessAudioChunk(chunk.Buffer, chunk.BytesRecorded, chunk.Format);
        }

        private void ProcessAudioChunk(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            if (_isDisposed || _isDisposing || !_isRunning || bytesRecorded <= 0 || buffer == null || format == null)
            {
                return;
            }

            try
            {
                _dataEventCount++;
                _lastAudioUtc = DateTime.UtcNow;
                CalculateLevels(buffer, bytesRecorded, format, out var peakDb, out var rmsDb);
                _lastPeakDb = peakDb;
                _lastRmsDb = rmsDb;
                double chunkMs = CalculateChunkMs(bytesRecorded, format);
                bool isSpeech = rmsDb > SilenceDbThreshold;

                if (_useRealtimeSession)
                {
                    byte[] normalizedPcm;
                    try
                    {
                        normalizedPcm = ConvertToPcm16Mono16k(buffer, format, bytesRecorded);
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"实时字幕音频转换异常: {ex.Message}");
                        return;
                    }

                    lock (_bufferGate)
                    {
                        _realtimePcmBuffer.Write(normalizedPcm, 0, normalizedPcm.Length);
                        if (_realtimePcmBuffer.Length > _realtimeMaxQueueBytes)
                        {
                            var data = _realtimePcmBuffer.ToArray();
                            int keep = Math.Min(data.Length, _realtimePacketBytes * 10);
                            _realtimePcmBuffer.SetLength(0);
                            _realtimePcmBuffer.Write(data, data.Length - keep, keep);
                        }
                    }

                    if ((DateTime.UtcNow - _lastDebugUtc).TotalMilliseconds >= 500)
                    {
                        PublishDebugInfo("capturing");
                    }

                    return;
                }

                lock (_bufferGate)
                {
                    _pcmBuffer.Write(buffer, 0, bytesRecorded);
                    _bufferMs += chunkMs;
                    if (isSpeech)
                    {
                        _speechMs += chunkMs;
                        _tailSilenceMs = 0;
                    }
                    else
                    {
                        _tailSilenceMs += chunkMs;
                    }

                    const int maxBufferBytes = 2 * 1024 * 1024;
                    if (_pcmBuffer.Length > maxBufferBytes)
                    {
                        // 防止代理不可用时堆积过大：仅保留最近半段，元数据按比例缩放。
                        var trimmed = _pcmBuffer.ToArray();
                        int oldLen = trimmed.Length;
                        int keep = Math.Min(trimmed.Length, maxBufferBytes / 2);
                        _pcmBuffer.SetLength(0);
                        _pcmBuffer.Write(trimmed, trimmed.Length - keep, keep);
                        if (oldLen > 0)
                        {
                            double ratio = (double)keep / oldLen;
                            _bufferMs *= ratio;
                            _speechMs *= ratio;
                            _tailSilenceMs = Math.Min(_tailSilenceMs, _bufferMs);
                        }
                    }
                }

                if ((DateTime.UtcNow - _lastDebugUtc).TotalMilliseconds >= 500)
                {
                    PublishDebugInfo("capturing");
                }
            }
            catch (ObjectDisposedException)
            {
                // dispose 竞争窗口，安全忽略，避免线程异常冒泡导致进程退出。
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"实时字幕采集回调异常: {ex.Message}");
            }
        }

        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            try
            {
                if (e?.Exception != null)
                {
                    RaiseStatusSafe($"实时字幕采集已停止: {e.Exception.Message}");
                    PublishDebugInfo($"capture-stop-ex: {e.Exception.Message}");
                }
                else
                {
                    PublishDebugInfo("capture-stopped");
                }
            }
            catch (Exception ex)
            {
                RaiseDebugSafe($"[capture-stopped-handler] {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task FlushChunkAsync()
        {
            if (_isDisposed || _isDisposing || !_isRunning || _cts == null || _cts.IsCancellationRequested)
            {
                return;
            }

            if (_useRealtimeSession)
            {
                await FlushRealtimeChunkAsync();
                return;
            }

            if (_isInFlight)
            {
                return;
            }

            byte[] pcmChunk;
            long queuedBytes = 0;
            double queuedMs;
            double speechMs;
            double tailSilenceMs;
            lock (_bufferGate)
            {
                queuedBytes = _pcmBuffer.Length;
                queuedMs = _bufferMs;
                speechMs = _speechMs;
                tailSilenceMs = _tailSilenceMs;
                if (queuedMs < MinChunkMs)
                {
                    if ((DateTime.UtcNow - _lastDebugUtc).TotalMilliseconds >= 1200)
                    {
                        PublishDebugInfo("waiting-audio");
                    }
                    return;
                }

                bool shouldCutBySilence = tailSilenceMs >= SentenceSilenceMs;
                bool shouldCutByMax = queuedMs >= MaxChunkMs;
                if (!shouldCutBySilence && !shouldCutByMax)
                {
                    return;
                }

                if (speechMs < MinSpeechMs)
                {
                    _pcmBuffer.SetLength(0);
                    _bufferMs = 0;
                    _speechMs = 0;
                    _tailSilenceMs = 0;
                    PublishDebugInfo("drop-silence", queuedBytes, 0);
                    return;
                }

                pcmChunk = _pcmBuffer.ToArray();
                _pcmBuffer.SetLength(0);
                _bufferMs = 0;
                _speechMs = 0;
                _tailSilenceMs = 0;
            }

            _isInFlight = true;
            _chunkCount++;
            try
            {
                var normalizedPcm = ConvertToPcm16Mono16k(pcmChunk, _captureFormat);
                var wav = BuildWav(normalizedPcm, new WaveFormat(16000, 16, 1));
                string rawText = await _client.TranscribeAudioAsync(wav, _cts.Token);
                _chunkSentCount++;
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    if (!string.IsNullOrWhiteSpace(_client.LastError))
                    {
                        StatusChanged?.Invoke(_client.LastError);
                    }
                    PublishDebugInfo("asr-empty", queuedBytes, pcmChunk.Length);
                    return;
                }

                string zhText = rawText;
                _chunkTextCount++;

                if (string.Equals(_lastSubtitle, zhText, StringComparison.Ordinal))
                {
                    PublishDebugInfo("same-subtitle", queuedBytes, pcmChunk.Length);
                    return;
                }

                _lastSubtitle = zhText;
                SubtitleUpdated?.Invoke(new LiveCaptionAsrText(zhText, isFinal: true));
                PublishDebugInfo("subtitle-updated", queuedBytes, pcmChunk.Length);
            }
            catch (OperationCanceledException)
            {
                PublishDebugInfo("canceled", queuedBytes, pcmChunk.Length);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"实时字幕处理异常: {ex.Message}");
                PublishDebugInfo($"exception: {ex.Message}", queuedBytes, pcmChunk.Length);
            }
            finally
            {
                _isInFlight = false;
            }
        }

        private async Task FlushRealtimeChunkAsync()
        {
            if (!_isRunning || _cts == null || _cts.IsCancellationRequested)
            {
                TryFlushAliyunMergedFinal(force: false);
                return;
            }

            TryFlushAliyunMergedFinal(force: false);
            if (!_client.IsRealtimeConnected)
            {
                if (_realtimeSessionStartInFlight || DateTime.UtcNow < _realtimeConnectGraceUntilUtc)
                {
                    if ((DateTime.UtcNow - _lastDebugUtc).TotalMilliseconds >= 1200)
                    {
                        PublishDebugInfo("waiting-realtime-boot");
                    }

                    return;
                }

                await TryRecoverRealtimeConnectionAsync();
                if ((DateTime.UtcNow - _lastDebugUtc).TotalMilliseconds >= 1200)
                {
                    PublishDebugInfo("waiting-realtime-connection");
                }

                return;
            }

            byte[] packet;
            long queuedBytes = 0;
            int sentThisTick = 0;

            while (sentThisTick < 4)
            {
                lock (_bufferGate)
                {
                    queuedBytes = _realtimePcmBuffer.Length;
                    if (_realtimePcmBuffer.Length < _realtimePacketBytes)
                    {
                        break;
                    }

                    byte[] all = _realtimePcmBuffer.ToArray();
                    packet = new byte[_realtimePacketBytes];
                    Buffer.BlockCopy(all, 0, packet, 0, _realtimePacketBytes);
                    _realtimePcmBuffer.SetLength(0);
                    if (all.Length > _realtimePacketBytes)
                    {
                        _realtimePcmBuffer.Write(all, _realtimePacketBytes, all.Length - _realtimePacketBytes);
                    }
                }

                _isInFlight = true;
                _chunkCount++;
                try
                {
                    bool sent = await _client.SendRealtimeAudioAsync(packet, _cts.Token);
                    if (sent)
                    {
                        _chunkSentCount++;
                        sentThisTick++;
                        _realtimeLastKeepAliveUtc = DateTime.UtcNow;
                        PublishDebugInfo("realtime-sent", queuedBytes, packet.Length);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(_client.LastError))
                        {
                            StatusChanged?.Invoke(_client.LastError);
                        }
                        PublishDebugInfo("realtime-send-failed", queuedBytes, packet.Length);
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    PublishDebugInfo("canceled", queuedBytes, packet.Length);
                    break;
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"实时字幕发送异常: {ex.Message}");
                    PublishDebugInfo($"exception: {ex.Message}", queuedBytes, packet.Length);
                    break;
                }
                finally
                {
                    _isInFlight = false;
                }
            }

            int keepAliveIntervalMs = GetRealtimeKeepAliveIntervalMs();
            if (sentThisTick == 0
                && keepAliveIntervalMs > 0
                && _client.IsRealtimeConnected
                && (DateTime.UtcNow - _realtimeLastKeepAliveUtc).TotalMilliseconds >= keepAliveIntervalMs)
            {
                try
                {
                    byte[] keepAlive = new byte[_realtimePacketBytes];
                    bool sent = await _client.SendRealtimeAudioAsync(keepAlive, _cts.Token);
                    if (sent)
                    {
                        _chunkSentCount++;
                        _realtimeLastKeepAliveUtc = DateTime.UtcNow;
                        PublishDebugInfo("realtime-keepalive", queuedBytes, keepAlive.Length);
                    }
                }
                catch
                {
                    // ignore and let reconnect path handle
                }
            }
        }

        private int GetRealtimeKeepAliveIntervalMs()
        {
            // 豆包在静音阶段如果长时间不发包会报 45000081（等包超时），保活要更积极。
            if (string.Equals(_client.AsrProvider, "doubao", StringComparison.OrdinalIgnoreCase))
            {
                return 300;
            }

            if (string.Equals(_client.AsrProvider, "baidu", StringComparison.OrdinalIgnoreCase))
            {
                return 800;
            }

            return 0;
        }

        private async Task TryRecoverRealtimeConnectionAsync()
        {
            if (!_useRealtimeSession || _cts == null || _cts.IsCancellationRequested)
            {
                return;
            }

            if (_realtimeReconnectInFlight || _realtimeSessionStartInFlight)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now < _realtimeNextReconnectUtc)
            {
                return;
            }

            _realtimeReconnectInFlight = true;
            _realtimeReconnectAttempt++;
            try
            {
                StatusChanged?.Invoke($"实时链路断开，正在自动重连（{_realtimeReconnectAttempt}）");
                bool ok = await _client.StartRealtimeSessionAsync(
                    HandleRealtimeText,
                    msg => StatusChanged?.Invoke(msg),
                    _cts.Token);
                if (ok)
                {
                    _realtimeReconnectAttempt = 0;
                    _realtimeNextReconnectUtc = DateTime.MinValue;
                    _realtimeLastKeepAliveUtc = DateTime.UtcNow;
                    StatusChanged?.Invoke("实时链路已自动恢复");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_client.LastError))
                {
                    StatusChanged?.Invoke(_client.LastError);
                }

                _realtimeNextReconnectUtc = now.AddSeconds(2);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"实时链路重连异常: {ex.Message}");
                _realtimeNextReconnectUtc = now.AddSeconds(2);
            }
            finally
            {
                _realtimeReconnectInFlight = false;
            }
        }

        private void HandleRealtimeText(LiveCaptionAsrText update)
        {
            if (!_isRunning || string.IsNullOrWhiteSpace(update.Text))
            {
                return;
            }

            string normalized = update.Text.Trim();
            if (string.Equals(_client.AsrProvider, "aliyun", StringComparison.OrdinalIgnoreCase))
            {
                HandleAliyunRealtimeText(normalized, update.IsFinal);
                return;
            }

            lock (_subtitleGate)
            {
                if (update.IsFinal && string.Equals(_lastSubtitle, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                if (update.IsFinal)
                {
                    _lastSubtitle = normalized;
                }
            }

            _chunkTextCount++;
            SubtitleUpdated?.Invoke(new LiveCaptionAsrText(normalized, update.IsFinal));
            PublishDebugInfo("realtime-subtitle");
        }

        private void HandleAliyunRealtimeText(string normalized, bool isFinal)
        {
            if (isFinal)
            {
                string mergedPreview;
                lock (_subtitleGate)
                {
                    if (_aliyunPendingFinalSegments.Count == 0
                        || !string.Equals(_aliyunPendingFinalSegments[^1], normalized, StringComparison.Ordinal))
                    {
                        _aliyunPendingFinalSegments.Add(normalized);
                    }

                    _aliyunLastFinalUtc = DateTime.UtcNow;
                    mergedPreview = MergeAliyunSegments(_aliyunPendingFinalSegments);
                }

                if (!string.IsNullOrWhiteSpace(mergedPreview))
                {
                    _chunkTextCount++;
                    SubtitleUpdated?.Invoke(new LiveCaptionAsrText(mergedPreview, isFinal: false));
                    PublishDebugInfo("realtime-subtitle-aliyun-merge");
                }

                return;
            }

            SubtitleUpdated?.Invoke(new LiveCaptionAsrText(normalized, isFinal: false));
            PublishDebugInfo("realtime-subtitle");
        }

        private void TryFlushAliyunMergedFinal(bool force)
        {
            if (!string.Equals(_client.AsrProvider, "aliyun", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string mergedFinal = string.Empty;
            lock (_subtitleGate)
            {
                if (_aliyunPendingFinalSegments.Count == 0)
                {
                    return;
                }

                bool timeoutReached = _aliyunLastFinalUtc != DateTime.MinValue
                    && (DateTime.UtcNow - _aliyunLastFinalUtc).TotalMilliseconds >= AliyunFinalMergeWindowMs;
                if (!force && !timeoutReached)
                {
                    return;
                }

                mergedFinal = MergeAliyunSegments(_aliyunPendingFinalSegments);
                _aliyunPendingFinalSegments.Clear();
                _aliyunLastFinalUtc = DateTime.MinValue;
                if (!string.IsNullOrWhiteSpace(mergedFinal))
                {
                    _lastSubtitle = mergedFinal;
                }
            }

            if (string.IsNullOrWhiteSpace(mergedFinal))
            {
                return;
            }

            _chunkTextCount++;
            SubtitleUpdated?.Invoke(new LiveCaptionAsrText(mergedFinal, isFinal: true));
            PublishDebugInfo("realtime-subtitle-aliyun-final");
        }

        private static string MergeAliyunSegments(IReadOnlyList<string> segments)
        {
            if (segments == null || segments.Count == 0)
            {
                return string.Empty;
            }

            string merged = string.Empty;
            foreach (var raw in segments)
            {
                string current = raw?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(current))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(merged))
                {
                    merged = current;
                    continue;
                }

                if (current.StartsWith(merged, StringComparison.Ordinal))
                {
                    merged = current;
                    continue;
                }

                if (merged.EndsWith(current, StringComparison.Ordinal))
                {
                    continue;
                }

                merged += current;
            }

            return merged.Trim();
        }

        private void PublishDebugInfo(string stage, long queuedBytes = -1, long sentBytes = -1)
        {
            if (!_isRunning && !string.Equals(stage, "stopped", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                _lastDebugUtc = DateTime.UtcNow;
                long bufferBytes;
                lock (_bufferGate)
                {
                    bufferBytes = _useRealtimeSession ? _realtimePcmBuffer.Length : _pcmBuffer.Length;
                }

                double idleMs = _lastAudioUtc == DateTime.MinValue ? -1 : (DateTime.UtcNow - _lastAudioUtc).TotalMilliseconds;
                string idleText = idleMs < 0 ? "n/a" : $"{idleMs:0}ms";
                string queuedText = queuedBytes < 0 ? "n/a" : $"{queuedBytes}";
                string sentText = sentBytes < 0 ? "n/a" : $"{sentBytes}";
                string debug = $"[{stage}] {_captureInfo} | dataEvt={_dataEventCount} idle={idleText} " +
                               $"peak={_lastPeakDb:0.0}dB rms={_lastRmsDb:0.0}dB buf={bufferBytes}B " +
                               $"bufMs={_bufferMs:0} speechMs={_speechMs:0} tailSil={_tailSilenceMs:0} " +
                               $"chunk={_chunkCount}/{_chunkSentCount}/{_chunkTextCount} q/s={queuedText}/{sentText}B " +
                               $"provider/model={_client.AsrProvider}/{_client.AsrModel} " +
                               $"asr={_client.LastTranscribeStatusCode}@{_client.LastTranscribeElapsedMs}ms " +
                               $"url(asr)={_client.LastTranscribeUrl} " +
                               $"{(string.IsNullOrWhiteSpace(_client.LastError) ? string.Empty : $"err={_client.LastError}")}";
                RaiseDebugSafe(debug.Trim());
            }
            catch (Exception ex)
            {
                RaiseDebugSafe($"[debug-info-failed] stage={stage}, ex={ex.GetType().Name}: {ex.Message}");
            }
        }

        private void RaiseStatusSafe(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                StatusChanged?.Invoke(message);
            }
            catch
            {
                // 避免事件订阅方异常反向杀死采集线程。
            }
        }

        private void RaiseDebugSafe(string debug)
        {
            if (string.IsNullOrWhiteSpace(debug))
            {
                return;
            }

            try
            {
                DebugInfoUpdated?.Invoke(debug);
            }
            catch
            {
                // 调试输出异常不应影响主流程。
            }
        }

        private static void CalculateLevels(byte[] buffer, int bytesRecorded, WaveFormat format, out float peakDb, out float rmsDb)
        {
            peakDb = -90f;
            rmsDb = -90f;
            if (buffer == null || bytesRecorded <= 0 || format == null)
            {
                return;
            }

            double peak = 0.0;
            double sumSquares = 0.0;
            int sampleCount = 0;

            try
            {
                if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
                {
                    for (int i = 0; i + 3 < bytesRecorded; i += 4)
                    {
                        float sample = BitConverter.ToSingle(buffer, i);
                        double abs = Math.Abs(sample);
                        if (abs > peak) peak = abs;
                        sumSquares += sample * sample;
                        sampleCount++;
                    }
                }
                else if (format.BitsPerSample == 16)
                {
                    for (int i = 0; i + 1 < bytesRecorded; i += 2)
                    {
                        short sampleInt = BitConverter.ToInt16(buffer, i);
                        double sample = sampleInt / 32768.0;
                        double abs = Math.Abs(sample);
                        if (abs > peak) peak = abs;
                        sumSquares += sample * sample;
                        sampleCount++;
                    }
                }
                else if (format.BitsPerSample == 32)
                {
                    for (int i = 0; i + 3 < bytesRecorded; i += 4)
                    {
                        int sampleInt = BitConverter.ToInt32(buffer, i);
                        double sample = sampleInt / 2147483648.0;
                        double abs = Math.Abs(sample);
                        if (abs > peak) peak = abs;
                        sumSquares += sample * sample;
                        sampleCount++;
                    }
                }

                if (sampleCount <= 0)
                {
                    return;
                }

                double rms = Math.Sqrt(sumSquares / sampleCount);
                peak = Math.Clamp(peak, 1e-9, 1.0);
                rms = Math.Clamp(rms, 1e-9, 1.0);
                peakDb = (float)(20.0 * Math.Log10(peak));
                rmsDb = (float)(20.0 * Math.Log10(rms));
            }
            catch
            {
                peakDb = -90f;
                rmsDb = -90f;
            }
        }

        private static byte[] BuildWav(byte[] pcmBytes, WaveFormat format)
        {
            using var ms = new MemoryStream();
            using (var writer = new WaveFileWriter(ms, format))
            {
                writer.Write(pcmBytes, 0, pcmBytes.Length);
            }
            return ms.ToArray();
        }

        private static double CalculateChunkMs(int bytesRecorded, WaveFormat format)
        {
            if (bytesRecorded <= 0 || format == null || format.AverageBytesPerSecond <= 0)
            {
                return 0;
            }

            return (bytesRecorded * 1000.0) / format.AverageBytesPerSecond;
        }

        private static byte[] ConvertToPcm16Mono16k(byte[] rawPcmBytes, WaveFormat inputFormat)
            => ConvertToPcm16Mono16k(rawPcmBytes, inputFormat, rawPcmBytes?.Length ?? 0);

        private static byte[] ConvertToPcm16Mono16k(byte[] rawPcmBytes, WaveFormat inputFormat, int bytesRecorded)
        {
            if (rawPcmBytes == null || bytesRecorded <= 0)
            {
                return Array.Empty<byte>();
            }

            using var inputMs = new MemoryStream(rawPcmBytes, 0, bytesRecorded, writable: false, publiclyVisible: true);
            using var rawSource = new RawSourceWaveStream(inputMs, inputFormat);

            ISampleProvider sampleProvider = rawSource.ToSampleProvider();
            if (sampleProvider.WaveFormat.Channels == 2)
            {
                sampleProvider = new StereoToMonoSampleProvider(sampleProvider)
                {
                    LeftVolume = 0.5f,
                    RightVolume = 0.5f
                };
            }

            if (sampleProvider.WaveFormat.SampleRate != 16000)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16000);
            }

            var wave16 = new SampleToWaveProvider16(sampleProvider);
            byte[] buffer = new byte[16000 * 2];
            using var output = new MemoryStream();
            int read;
            while ((read = wave16.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposing = true;
            Stop();
            try
            {
                _flushTimer.Elapsed -= _flushTimerHandler;
                _flushTimer.Dispose();
            }
            catch
            {
            }

            try
            {
                _cts?.Dispose();
            }
            catch
            {
            }

            try
            {
                _client.Dispose();
            }
            catch
            {
            }

            try
            {
                lock (_bufferGate)
                {
                    _pcmBuffer.Dispose();
                    _realtimePcmBuffer.Dispose();
                }
            }
            catch
            {
            }

            _isDisposed = true;
            _isDisposing = false;
        }

        private async Task FlushChunkSafeAsync()
        {
            try
            {
                await FlushChunkAsync();
            }
            catch (ObjectDisposedException)
            {
                // 引擎释放过程中回调晚到，忽略。
            }
            catch (OperationCanceledException)
            {
                // 停止流程中的正常取消，忽略。
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"实时字幕调度异常: {ex.Message}");
            }
        }
    }
}
