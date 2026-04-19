using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ImageColorChanger.Services.LiveCaption
{
    internal sealed class SharedAudioCaptureSession : IDisposable
    {
        internal readonly struct AudioChunk
        {
            public AudioChunk(byte[] buffer, int bytesRecorded, WaveFormat format)
            {
                Buffer = buffer ?? Array.Empty<byte>();
                BytesRecorded = Math.Max(0, bytesRecorded);
                Format = format;
            }

            public byte[] Buffer { get; }
            public int BytesRecorded { get; }
            public WaveFormat Format { get; }
        }

        internal sealed class InputAudioDeviceInfo
        {
            public string Id { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public bool IsDefault { get; init; }
        }

        internal delegate IWaveIn CaptureFactory(LiveCaptionAudioSource source, string preferredInputDeviceId, string preferredSystemDeviceId, out string selectedName);

        private readonly object _gate = new();
        private readonly List<Action<AudioChunk>> _subscribers = new();
        private readonly CaptureFactory _captureFactory;
        private IWaveIn _capture;
        private bool _disposed;

        public SharedAudioCaptureSession()
            : this(CreateCapture)
        {
        }

        internal SharedAudioCaptureSession(CaptureFactory captureFactory)
        {
            _captureFactory = captureFactory ?? throw new ArgumentNullException(nameof(captureFactory));
        }

        public LiveCaptionAudioSource CurrentSource { get; private set; } = LiveCaptionAudioSource.SystemLoopback;
        public string CurrentInputDeviceId { get; private set; } = string.Empty;
        public string CurrentSystemDeviceId { get; private set; } = string.Empty;
        public string SelectedDeviceName { get; private set; } = string.Empty;
        public string LastStartError { get; private set; } = string.Empty;
        public bool LastStartFallbackApplied { get; private set; }
        public bool IsRunning { get; private set; }

        public IDisposable Subscribe(Action<byte[]> onPcmChunk)
        {
            if (onPcmChunk == null)
            {
                throw new ArgumentNullException(nameof(onPcmChunk));
            }

            return SubscribeChunk(chunk => onPcmChunk(chunk.Buffer));
        }

        public IDisposable SubscribeChunk(Action<AudioChunk> onAudioChunk)
        {
            if (onAudioChunk == null)
            {
                throw new ArgumentNullException(nameof(onAudioChunk));
            }

            lock (_gate)
            {
                _subscribers.Add(onAudioChunk);
            }

            return new Subscription(this, onAudioChunk);
        }

        public void SetSelection(LiveCaptionAudioSource source, string inputDeviceId, string systemDeviceId)
        {
            CurrentSource = source;
            CurrentInputDeviceId = inputDeviceId ?? string.Empty;
            CurrentSystemDeviceId = systemDeviceId ?? string.Empty;
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (IsRunning)
            {
                return;
            }

            LastStartError = string.Empty;
            LastStartFallbackApplied = false;
            try
            {
                _capture = CreateAndStartCapture(CurrentSource, CurrentInputDeviceId, CurrentSystemDeviceId, out string selectedName);
                SelectedDeviceName = selectedName ?? string.Empty;
                IsRunning = true;
            }
            catch (Exception ex)
            {
                if (CurrentSource == LiveCaptionAudioSource.Microphone)
                {
                    string micError = BuildCaptureStartErrorMessage(ex, LiveCaptionAudioSource.Microphone, SelectedDeviceName);
                    try
                    {
                        _capture = CreateAndStartCapture(
                            LiveCaptionAudioSource.SystemLoopback,
                            CurrentInputDeviceId,
                            CurrentSystemDeviceId,
                            out string fallbackSelectedName);
                        SelectedDeviceName = fallbackSelectedName ?? string.Empty;
                        CurrentSource = LiveCaptionAudioSource.SystemLoopback;
                        LastStartFallbackApplied = true;
                        LastStartError = $"{micError} 已自动切换到系统声音。";
                        IsRunning = true;
                        return;
                    }
                    catch (Exception fallbackEx)
                    {
                        string fallbackError = BuildCaptureStartErrorMessage(fallbackEx, LiveCaptionAudioSource.SystemLoopback, SelectedDeviceName);
                        LastStartError = $"{micError}；系统声音回退也失败：{fallbackError}";
                    }
                }

                if (string.IsNullOrWhiteSpace(LastStartError))
                {
                    LastStartError = BuildCaptureStartErrorMessage(ex, CurrentSource, SelectedDeviceName);
                }
                _capture = null;
                IsRunning = false;
            }
        }

        public void Stop()
        {
            if (_capture == null)
            {
                IsRunning = false;
                return;
            }

            var capture = _capture;
            _capture = null;
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
            catch
            {
            }

            try
            {
                capture.Dispose();
            }
            catch
            {
            }

            IsRunning = false;
        }

        internal void PublishForTest(byte[] pcmChunk)
        {
            PublishToSubscribers(new AudioChunk(
                pcmChunk ?? Array.Empty<byte>(),
                pcmChunk?.Length ?? 0,
                new WaveFormat(16000, 16, 1)));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (e == null || e.BytesRecorded <= 0)
            {
                return;
            }

            try
            {
                WaveFormat sourceFormat = _capture?.WaveFormat;
                byte[] normalized = NormalizeToPcm16Mono(e.Buffer, e.BytesRecorded, sourceFormat, out WaveFormat normalizedFormat);
                PublishToSubscribers(new AudioChunk(normalized, normalized.Length, normalizedFormat));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LiveCaption] Capture_DataAvailable normalize failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            IsRunning = false;
        }

        private void PublishToSubscribers(AudioChunk chunk)
        {
            Action<AudioChunk>[] snapshot;
            lock (_gate)
            {
                snapshot = _subscribers.ToArray();
            }

            foreach (Action<AudioChunk> subscriber in snapshot)
            {
                subscriber(chunk);
            }
        }

        private void Unsubscribe(Action<AudioChunk> onAudioChunk)
        {
            lock (_gate)
            {
                _subscribers.Remove(onAudioChunk);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SharedAudioCaptureSession));
            }
        }

        public static IReadOnlyList<InputAudioDeviceInfo> EnumerateInputDevices()
        {
            var result = new List<InputAudioDeviceInfo>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice defaultDevice = null;
                try
                {
                    defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                }
                catch
                {
                }

                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    result.Add(new InputAudioDeviceInfo
                    {
                        Id = endpoint.ID ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(endpoint.FriendlyName) ? "未命名输入设备" : endpoint.FriendlyName.Trim(),
                        IsDefault = defaultDevice != null && string.Equals(defaultDevice.ID, endpoint.ID, StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
            catch
            {
            }

            return result;
        }

        public static IReadOnlyList<InputAudioDeviceInfo> EnumerateSystemLoopbackDevices()
        {
            var result = new List<InputAudioDeviceInfo>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice defaultDevice = null;
                try
                {
                    defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
                catch
                {
                }

                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    result.Add(new InputAudioDeviceInfo
                    {
                        Id = endpoint.ID ?? string.Empty,
                        Name = string.IsNullOrWhiteSpace(endpoint.FriendlyName) ? "未命名系统设备" : endpoint.FriendlyName.Trim(),
                        IsDefault = defaultDevice != null && string.Equals(defaultDevice.ID, endpoint.ID, StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
            catch
            {
            }

            return result;
        }

        public static IWaveIn CreateCaptureForSelection(LiveCaptionAudioSource source, string preferredInputDeviceId, string preferredSystemDeviceId, out string selectedName)
        {
            return CreateCapture(source, preferredInputDeviceId, preferredSystemDeviceId, out selectedName);
        }

        private static IWaveIn CreateCapture(LiveCaptionAudioSource source, string preferredInputDeviceId, string preferredSystemDeviceId, out string selectedName)
        {
            return source == LiveCaptionAudioSource.SystemLoopback
                ? CreateSystemLoopbackCapture(preferredSystemDeviceId, out selectedName)
                : CreateInputCapture(preferredInputDeviceId, out selectedName);
        }

        private static IWaveIn CreateInputCapture(string preferredInputDeviceId, out string selectedName)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice device = null;

                if (!string.IsNullOrWhiteSpace(preferredInputDeviceId))
                {
                    try
                    {
                        var target = enumerator.GetDevice(preferredInputDeviceId.Trim());
                        if (target.DataFlow == DataFlow.Capture && target.State == DeviceState.Active)
                        {
                            device = target;
                        }
                    }
                    catch
                    {
                    }
                }

                device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                selectedName = string.IsNullOrWhiteSpace(device?.FriendlyName) ? "默认输入设备" : device.FriendlyName.Trim();
                if (device != null)
                {
                    return new WasapiCapture(device);
                }
            }
            catch
            {
            }

            selectedName = "系统默认输入设备(WaveIn)";
            return new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };
        }

        private static IWaveIn CreateSystemLoopbackCapture(string preferredSystemDeviceId, out string selectedName)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice device = null;

                if (!string.IsNullOrWhiteSpace(preferredSystemDeviceId))
                {
                    try
                    {
                        var target = enumerator.GetDevice(preferredSystemDeviceId.Trim());
                        if (target.DataFlow == DataFlow.Render && target.State == DeviceState.Active)
                        {
                            device = target;
                        }
                    }
                    catch
                    {
                    }
                }

                device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                selectedName = string.IsNullOrWhiteSpace(device?.FriendlyName) ? "默认系统输出设备" : device.FriendlyName.Trim();
                if (device != null)
                {
                    return new WasapiLoopbackCapture(device);
                }
            }
            catch
            {
            }

            selectedName = "默认系统输出设备";
            return new WasapiLoopbackCapture();
        }

        private static byte[] NormalizeToPcm16Mono(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat, out WaveFormat outputFormat)
        {
            outputFormat = new WaveFormat(16000, 16, 1);
            if (buffer == null || bytesRecorded <= 0)
            {
                return Array.Empty<byte>();
            }

            if (sourceFormat != null &&
                sourceFormat.Encoding == WaveFormatEncoding.Pcm &&
                sourceFormat.SampleRate == outputFormat.SampleRate &&
                sourceFormat.BitsPerSample == outputFormat.BitsPerSample &&
                sourceFormat.Channels == outputFormat.Channels)
            {
                byte[] passthrough = new byte[bytesRecorded];
                Buffer.BlockCopy(buffer, 0, passthrough, 0, bytesRecorded);
                return passthrough;
            }

            using var sourceStream = new RawSourceWaveStream(buffer, 0, bytesRecorded, sourceFormat ?? outputFormat);
            ISampleProvider sampleProvider = sourceStream.ToSampleProvider();
            if (sampleProvider.WaveFormat.Channels == 2)
            {
                sampleProvider = new StereoToMonoSampleProvider(sampleProvider)
                {
                    LeftVolume = 0.5f,
                    RightVolume = 0.5f
                };
            }
            else if (sampleProvider.WaveFormat.Channels > 2)
            {
                sampleProvider = new MultiChannelToMonoSampleProvider(sampleProvider);
            }

            if (sampleProvider.WaveFormat.SampleRate != outputFormat.SampleRate)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, outputFormat.SampleRate);
            }

            var pcmProvider = new SampleToWaveProvider16(sampleProvider);
            using var converted = new System.IO.MemoryStream();
            byte[] temp = new byte[4096];
            int read;
            while ((read = pcmProvider.Read(temp, 0, temp.Length)) > 0)
            {
                converted.Write(temp, 0, read);
            }

            return converted.ToArray();
        }

        private sealed class MultiChannelToMonoSampleProvider : ISampleProvider
        {
            private readonly ISampleProvider _source;
            private readonly int _sourceChannels;
            private float[] _sourceBuffer = Array.Empty<float>();

            public MultiChannelToMonoSampleProvider(ISampleProvider source)
            {
                _source = source ?? throw new ArgumentNullException(nameof(source));
                _sourceChannels = Math.Max(1, source.WaveFormat.Channels);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                if (buffer == null)
                {
                    throw new ArgumentNullException(nameof(buffer));
                }

                if (count <= 0)
                {
                    return 0;
                }

                int sourceSamplesRequested = count * _sourceChannels;
                if (_sourceBuffer.Length < sourceSamplesRequested)
                {
                    _sourceBuffer = new float[sourceSamplesRequested];
                }

                int sourceRead = _source.Read(_sourceBuffer, 0, sourceSamplesRequested);
                int framesRead = sourceRead / _sourceChannels;
                int sourceIndex = 0;
                for (int i = 0; i < framesRead; i++)
                {
                    float sum = 0f;
                    for (int c = 0; c < _sourceChannels; c++)
                    {
                        sum += _sourceBuffer[sourceIndex++];
                    }

                    buffer[offset + i] = sum / _sourceChannels;
                }

                return framesRead;
            }
        }

        private IWaveIn CreateAndStartCapture(
            LiveCaptionAudioSource source,
            string inputDeviceId,
            string systemDeviceId,
            out string selectedName)
        {
            IWaveIn capture = null;
            try
            {
                capture = _captureFactory(source, inputDeviceId, systemDeviceId, out selectedName);
                capture.DataAvailable += Capture_DataAvailable;
                capture.RecordingStopped += Capture_RecordingStopped;
                capture.StartRecording();
                return capture;
            }
            catch
            {
                try
                {
                    if (capture != null)
                    {
                        capture.DataAvailable -= Capture_DataAvailable;
                        capture.RecordingStopped -= Capture_RecordingStopped;
                    }
                }
                catch
                {
                }

                try
                {
                    capture?.Dispose();
                }
                catch
                {
                }

                throw;
            }
        }

        private static string BuildCaptureStartErrorMessage(Exception ex, LiveCaptionAudioSource source, string selectedDeviceName)
        {
            string deviceType = source == LiveCaptionAudioSource.SystemLoopback ? "系统声卡" : "输入设备";
            string deviceName = string.IsNullOrWhiteSpace(selectedDeviceName) ? "默认设备" : selectedDeviceName.Trim();

            bool isBusy = ex is COMException com &&
                          (com.ErrorCode == unchecked((int)0x8889000A) || com.ErrorCode == unchecked((int)0x80070020));
            if (!isBusy)
            {
                string message = ex?.Message ?? string.Empty;
                string lower = message.ToLowerInvariant();
                isBusy = lower.Contains("0x8889000a") ||
                         lower.Contains("device in use") ||
                         lower.Contains("used by another") ||
                         message.Contains("占用", StringComparison.Ordinal);
            }

            if (isBusy)
            {
                return $"音频采集启动失败：{deviceType}“{deviceName}”正被其他应用占用，请关闭独占模式或切换其他设备重试。";
            }

            return $"音频采集启动失败：{deviceType}“{deviceName}”不可用（{ex?.Message ?? "未知错误"}）。";
        }

        private sealed class Subscription : IDisposable
        {
            private SharedAudioCaptureSession _owner;
            private Action<AudioChunk> _callback;

            public Subscription(SharedAudioCaptureSession owner, Action<AudioChunk> callback)
            {
                _owner = owner;
                _callback = callback;
            }

            public void Dispose()
            {
                if (_owner == null || _callback == null)
                {
                    return;
                }

                _owner.Unsubscribe(_callback);
                _owner = null;
                _callback = null;
            }
        }
    }
}
