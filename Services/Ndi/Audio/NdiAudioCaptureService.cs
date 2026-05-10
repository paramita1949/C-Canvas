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
                                 && _configManager.ProjectionNdiSlideEnabled
                                 && _configManager.ProjectionNdiAudioEnabled
                                 && ProjectionNdiRuntimeProbe.IsRuntimeAvailable();
                NdiAudioSourceMode mode = ParseMode(_configManager.ProjectionNdiAudioSourceMode);
                if (!shouldRun || mode == NdiAudioSourceMode.None)
                {
                    StopCore();
                    return;
                }

                if (_capture != null)
                {
                    if (IsCaptureConfigurationStillValid(mode))
                    {
                        return;
                    }

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
            }
            catch (Exception ex)
            {
                LastError = BuildStartErrorMessage(ex, mode);
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
                    return;
                }

                _transportCoordinator.PublishAudio(frame);
            }
            catch
            {
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
    }
}
