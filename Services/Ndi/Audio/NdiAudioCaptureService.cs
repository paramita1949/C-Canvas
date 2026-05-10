using System;
using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Core;
using ImageColorChanger.Services.LiveCaption;
using ImageColorChanger.Services.Projection.Output;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ImageColorChanger.Services.Ndi.Audio
{
    public sealed class NdiAudioCaptureService : INdiAudioCaptureService
    {
        private readonly ConfigManager _configManager;
        private readonly INdiTransportCoordinator _transportCoordinator;
        private readonly object _sync = new();
        private IWaveIn _capture;
        private bool _disposed;
        private NdiAudioSourceMode _currentMode = NdiAudioSourceMode.None;
        private string _currentInputDeviceId = string.Empty;
        private string _currentSystemDeviceId = string.Empty;
        private long _audioFrameCount;
        private long _audioPublishFailCount;
        private long _lastAudioStatsLogTick;
        private long _lastPublishFailLogTick;
        private long _audioWindowStartTick;
        private long _windowFrameCount;
        private long _windowBytes;
        private long _lastDataAvailableTick;
        private double _windowPeak;
        private double _windowSquareSum;
        private long _windowSampleCount;

        public NdiAudioCaptureService(
            ConfigManager configManager,
            INdiTransportCoordinator transportCoordinator)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _transportCoordinator = transportCoordinator ?? throw new ArgumentNullException(nameof(transportCoordinator));
        }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _capture != null;
                }
            }
        }

        public string CurrentDeviceName { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;

        public IReadOnlyList<NdiAudioDeviceInfo> EnumerateDevices(NdiAudioSourceMode mode)
        {
            var sourceList = mode == NdiAudioSourceMode.System
                ? SharedAudioCaptureSession.EnumerateSystemLoopbackDevices()
                : SharedAudioCaptureSession.EnumerateInputDevices();

            return sourceList.Select(device => new NdiAudioDeviceInfo
            {
                Id = device.Id,
                Name = device.Name,
                IsDefault = device.IsDefault
            }).ToArray();
        }

        public void ApplyConfiguration()
        {
            ThrowIfDisposed();
            lock (_sync)
            {
                bool shouldRun = _configManager.ProjectionNdiEnabled
                                 && _configManager.ProjectionNdiAudioEnabled
                                 && ProjectionNdiRuntimeProbe.IsRuntimeAvailable();
                NdiAudioSourceMode mode = ParseMode(_configManager.ProjectionNdiAudioSourceMode);
                ProjectionNdiDiagnostics.Log(
                    $"NDI audio config: shouldRun={shouldRun}, mode={mode}, master={_configManager.ProjectionNdiEnabled}, slide={_configManager.ProjectionNdiSlideEnabled}, audioEnabled={_configManager.ProjectionNdiAudioEnabled}");
                if (!shouldRun || mode == NdiAudioSourceMode.None)
                {
                    ProjectionNdiDiagnostics.Log("NDI audio stop: disabled by config or mode=None.");
                    StopCore();
                    return;
                }

                if (_capture != null)
                {
                    if (IsCaptureConfigurationStillValid(mode))
                    {
                        ProjectionNdiDiagnostics.Log("NDI audio keep running: capture config unchanged.");
                        return;
                    }

                    ProjectionNdiDiagnostics.Log("NDI audio restart: capture config changed.");
                    StopCore();
                }

                StartCore(mode);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                StopCore();
            }
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

        private void StartCore(NdiAudioSourceMode mode)
        {
            LastError = string.Empty;
            try
            {
                string preferredInputId = (_configManager.ProjectionNdiAudioInputDeviceId ?? string.Empty).Trim();
                string preferredSystemId = (_configManager.ProjectionNdiAudioSystemDeviceId ?? string.Empty).Trim();
                LiveCaptionAudioSource source = mode == NdiAudioSourceMode.System
                    ? LiveCaptionAudioSource.SystemLoopback
                    : LiveCaptionAudioSource.Microphone;

                _capture = SharedAudioCaptureSession.CreateCaptureForSelection(
                    source,
                    preferredInputId,
                    preferredSystemId,
                    out string selectedName);
                CurrentDeviceName = selectedName ?? string.Empty;
                _currentMode = mode;
                _currentInputDeviceId = preferredInputId;
                _currentSystemDeviceId = preferredSystemId;
                _capture.DataAvailable += Capture_DataAvailable;
                _capture.RecordingStopped += Capture_RecordingStopped;
                _capture.StartRecording();
                _audioFrameCount = 0;
                _audioPublishFailCount = 0;
                _lastAudioStatsLogTick = 0;
                _lastPublishFailLogTick = 0;
                _audioWindowStartTick = Environment.TickCount64;
                _windowFrameCount = 0;
                _windowBytes = 0;
                _lastDataAvailableTick = 0;
                _windowPeak = 0d;
                _windowSquareSum = 0d;
                _windowSampleCount = 0;
                ProjectionNdiDiagnostics.Log(
                    $"NDI audio capture started: mode={mode}, device=\"{CurrentDeviceName}\", sampleRate={_capture.WaveFormat.SampleRate}, channels={_capture.WaveFormat.Channels}, bits={_capture.WaveFormat.BitsPerSample}");
            }
            catch (Exception ex)
            {
                LastError = BuildStartErrorMessage(ex, mode);
                ProjectionNdiDiagnostics.Log($"NDI audio capture start failed: {LastError}");
                StopCore();
            }
        }

        private void StopCore()
        {
            if (_capture == null)
            {
                CurrentDeviceName = string.Empty;
                _currentMode = NdiAudioSourceMode.None;
                _currentInputDeviceId = string.Empty;
                _currentSystemDeviceId = string.Empty;
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

            CurrentDeviceName = string.Empty;
            _currentMode = NdiAudioSourceMode.None;
            _currentInputDeviceId = string.Empty;
            _currentSystemDeviceId = string.Empty;
            ProjectionNdiDiagnostics.Log("NDI audio capture stopped.");
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            var capture = _capture;
            if (e == null || e.BytesRecorded <= 0 || capture == null)
            {
                return;
            }

            try
            {
                var sourceFormat = capture.WaveFormat;
                if (!NdiAudioSampleConverter.TryConvertToPlanarFloat(e.Buffer, e.BytesRecorded, sourceFormat, out ProjectionNdiAudioFrame frame))
                {
                    ThrottledLog(ref _lastPublishFailLogTick, "NDI audio convert failed: unsupported source format.");
                    return;
                }

                bool sent = _transportCoordinator.PublishAudio(frame);
                _audioFrameCount++;
                _windowFrameCount++;
                _windowBytes += e.BytesRecorded;
                AnalyzeAudioLevels(frame);
                if (!sent)
                {
                    _audioPublishFailCount++;
                    ThrottledLog(
                        ref _lastPublishFailLogTick,
                        $"NDI audio publish failed: sampleRate={frame.SampleRate}, channels={frame.ChannelCount}, samplesPerChannel={frame.SamplesPerChannel}");
                }

                LogAudioStatsIfDue();
            }
            catch
            {
                ThrottledLog(ref _lastPublishFailLogTick, "NDI audio data handler failed: unknown exception.");
            }
        }

        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            _ = sender;
            _ = e;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _capture = null;
                CurrentDeviceName = string.Empty;
                ProjectionNdiDiagnostics.Log("NDI audio capture recording stopped callback received.");
            }
        }

        private bool IsCaptureConfigurationStillValid(NdiAudioSourceMode mode)
        {
            if (_capture == null)
            {
                return false;
            }

            return mode == _currentMode &&
                   string.Equals((_configManager.ProjectionNdiAudioInputDeviceId ?? string.Empty).Trim(), _currentInputDeviceId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals((_configManager.ProjectionNdiAudioSystemDeviceId ?? string.Empty).Trim(), _currentSystemDeviceId, StringComparison.OrdinalIgnoreCase);
        }

        private static NdiAudioSourceMode ParseMode(string value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "system" => NdiAudioSourceMode.System,
                "input" => NdiAudioSourceMode.Input,
                _ => NdiAudioSourceMode.None
            };
        }

        private static string BuildStartErrorMessage(Exception ex, NdiAudioSourceMode mode)
        {
            string sourceLabel = mode == NdiAudioSourceMode.System ? "系统声音" : "输入设备";
            string message = ex?.Message ?? "未知错误";
            return $"NDI音频采集启动失败：{sourceLabel}（{message}）";
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NdiAudioCaptureService));
            }
        }

        private void LogAudioStatsIfDue()
        {
            long now = Environment.TickCount64;
            if (now - _lastAudioStatsLogTick < 5000)
            {
                return;
            }

            long elapsedMs = Math.Max(1, now - _audioWindowStartTick);
            double elapsedSec = elapsedMs / 1000d;
            double callbackFps = _windowFrameCount / elapsedSec;
            double kbps = (_windowBytes * 8d / 1000d) / elapsedSec;
            double rms = _windowSampleCount > 0 ? Math.Sqrt(_windowSquareSum / _windowSampleCount) : 0d;
            double rmsDb = rms > 1e-9 ? 20d * Math.Log10(rms) : -120d;
            long lastGapMs = _lastDataAvailableTick > 0 ? now - _lastDataAvailableTick : -1;

            _lastAudioStatsLogTick = now;
            ProjectionNdiDiagnostics.Log(
                $"NDI audio stats: frames={_audioFrameCount}, publishFail={_audioPublishFailCount}, windowFrames={_windowFrameCount}, callbackFps={callbackFps:0.0}, kbps={kbps:0.0}, peak={_windowPeak:0.000}, rmsDb={rmsDb:0.0}, lastGapMs={lastGapMs}, device=\"{CurrentDeviceName}\"");

            _audioWindowStartTick = now;
            _windowFrameCount = 0;
            _windowBytes = 0;
            _windowPeak = 0d;
            _windowSquareSum = 0d;
            _windowSampleCount = 0;
        }

        private static void ThrottledLog(ref long lastTick, string message, int intervalMs = 5000)
        {
            long now = Environment.TickCount64;
            if (now - lastTick < intervalMs)
            {
                return;
            }

            lastTick = now;
            ProjectionNdiDiagnostics.Log(message);
        }

        private void AnalyzeAudioLevels(ProjectionNdiAudioFrame frame)
        {
            if (frame?.PlanarSamples == null || frame.PlanarSamples.Length == 0)
            {
                return;
            }

            _lastDataAvailableTick = Environment.TickCount64;
            int count = Math.Min(frame.PlanarSamples.Length, Math.Max(0, frame.ChannelCount * frame.SamplesPerChannel));
            if (count <= 0)
            {
                return;
            }

            float[] samples = frame.PlanarSamples;
            for (int i = 0; i < count; i++)
            {
                double v = samples[i];
                double abs = Math.Abs(v);
                if (abs > _windowPeak)
                {
                    _windowPeak = abs;
                }

                _windowSquareSum += v * v;
            }

            _windowSampleCount += count;
        }
    }
}
