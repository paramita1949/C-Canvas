using System;
using System.Collections.Generic;

namespace ImageColorChanger.Services.Ndi.Audio
{
    public interface INdiAudioCaptureService : IDisposable
    {
        bool IsRunning { get; }
        string CurrentDeviceName { get; }
        string LastError { get; }
        IReadOnlyList<NdiAudioDeviceInfo> EnumerateDevices(NdiAudioSourceMode mode);
        void ApplyConfiguration();
        void Stop();
    }
}
