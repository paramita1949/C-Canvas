using System;
using ImageColorChanger.Managers;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 媒体模块控制器：统一管理 VideoPlayerManager 事件绑定/解绑。
    /// </summary>
    public sealed class MediaModuleController : IDisposable
    {
        private readonly VideoPlayerManager _videoPlayerManager;
        private readonly EventHandler<bool> _videoTrackDetectedHandler;
        private readonly EventHandler<bool> _playStateChangedHandler;
        private readonly EventHandler<string> _mediaChangedHandler;
        private readonly EventHandler _mediaEndedHandler;
        private readonly EventHandler<(float position, long currentTime, long totalTime)> _progressUpdatedHandler;
        private bool _attached;

        public MediaModuleController(
            VideoPlayerManager videoPlayerManager,
            EventHandler<bool> videoTrackDetectedHandler,
            EventHandler<bool> playStateChangedHandler,
            EventHandler<string> mediaChangedHandler,
            EventHandler mediaEndedHandler,
            EventHandler<(float position, long currentTime, long totalTime)> progressUpdatedHandler)
        {
            _videoPlayerManager = videoPlayerManager ?? throw new ArgumentNullException(nameof(videoPlayerManager));
            _videoTrackDetectedHandler = videoTrackDetectedHandler ?? throw new ArgumentNullException(nameof(videoTrackDetectedHandler));
            _playStateChangedHandler = playStateChangedHandler ?? throw new ArgumentNullException(nameof(playStateChangedHandler));
            _mediaChangedHandler = mediaChangedHandler ?? throw new ArgumentNullException(nameof(mediaChangedHandler));
            _mediaEndedHandler = mediaEndedHandler ?? throw new ArgumentNullException(nameof(mediaEndedHandler));
            _progressUpdatedHandler = progressUpdatedHandler ?? throw new ArgumentNullException(nameof(progressUpdatedHandler));
        }

        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            _videoPlayerManager.VideoTrackDetected += _videoTrackDetectedHandler;
            _videoPlayerManager.PlayStateChanged += _playStateChangedHandler;
            _videoPlayerManager.MediaChanged += _mediaChangedHandler;
            _videoPlayerManager.MediaEnded += _mediaEndedHandler;
            _videoPlayerManager.ProgressUpdated += _progressUpdatedHandler;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _videoPlayerManager.VideoTrackDetected -= _videoTrackDetectedHandler;
            _videoPlayerManager.PlayStateChanged -= _playStateChangedHandler;
            _videoPlayerManager.MediaChanged -= _mediaChangedHandler;
            _videoPlayerManager.MediaEnded -= _mediaEndedHandler;
            _videoPlayerManager.ProgressUpdated -= _progressUpdatedHandler;
            _attached = false;
        }

        public void Dispose()
        {
            Detach();
        }
    }
}
