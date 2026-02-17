using System;
using System.Windows.Controls;

namespace ImageColorChanger.Managers
{
    public interface IVideoBackgroundManager : IDisposable
    {
        MediaElement GetOrCreateMediaElement(string videoPath, bool loopEnabled = true);
        void Play(string videoPath);
        void Pause();
        void Stop();
        void SetVolume(double volume);
        void RemoveFromCache(string videoPath);
        void ClearCache();
        string GetCacheInfo();
    }
}
