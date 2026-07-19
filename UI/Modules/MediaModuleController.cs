using System;
using ImageColorChanger.Managers;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using LibVLCSharp.WPF;

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
        private readonly EventHandler<string> _playbackErrorHandler;
        private VideoView _mainVideoView;
        private System.Windows.Controls.Panel _hostContainer;
        private SizeChangedEventHandler _mainVideoViewSizeChangedHandler;
        private bool _attached;

        public MediaModuleController(
            VideoPlayerManager videoPlayerManager,
            EventHandler<bool> videoTrackDetectedHandler,
            EventHandler<bool> playStateChangedHandler,
            EventHandler<string> mediaChangedHandler,
            EventHandler mediaEndedHandler,
            EventHandler<(float position, long currentTime, long totalTime)> progressUpdatedHandler,
            EventHandler<string> playbackErrorHandler)
        {
            _videoPlayerManager = videoPlayerManager ?? throw new ArgumentNullException(nameof(videoPlayerManager));
            _videoTrackDetectedHandler = videoTrackDetectedHandler ?? throw new ArgumentNullException(nameof(videoTrackDetectedHandler));
            _playStateChangedHandler = playStateChangedHandler ?? throw new ArgumentNullException(nameof(playStateChangedHandler));
            _mediaChangedHandler = mediaChangedHandler ?? throw new ArgumentNullException(nameof(mediaChangedHandler));
            _mediaEndedHandler = mediaEndedHandler ?? throw new ArgumentNullException(nameof(mediaEndedHandler));
            _progressUpdatedHandler = progressUpdatedHandler ?? throw new ArgumentNullException(nameof(progressUpdatedHandler));
            _playbackErrorHandler = playbackErrorHandler ?? throw new ArgumentNullException(nameof(playbackErrorHandler));
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
            _videoPlayerManager.PlaybackError += _playbackErrorHandler;
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
            _videoPlayerManager.PlaybackError -= _playbackErrorHandler;
            _attached = false;
        }

        public void Dispose()
        {
            Detach();
        }

        public VideoView MainVideoView => _mainVideoView;

        /// <summary>
        /// 创建并挂载主屏 VideoView，首帧尺寸有效后初始化 MediaPlayer。
        /// </summary>
        public void InitializeMainVideoView(System.Windows.Controls.Panel hostContainer)
        {
            if (hostContainer == null)
            {
                throw new ArgumentNullException(nameof(hostContainer));
            }

            // 避免重复挂载，保持初始化幂等
            if (_mainVideoView != null)
            {
                return;
            }

            _mainVideoView = new VideoView
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Margin = new Thickness(0)
            };

            _hostContainer = hostContainer;
            bool mediaPlayerInitialized = false;
            bool mediaPlayerInitializationInProgress = false;

            void TryInitializeMainVideoView(string trigger)
            {
                if (mediaPlayerInitialized || mediaPlayerInitializationInProgress)
                {
                    return;
                }

                Debug.WriteLine(
                    $"[MediaPlayerInit] Main VideoView {trigger}: " +
                    $"Loaded={_mainVideoView.IsLoaded}, Visible={_mainVideoView.IsVisible}, " +
                    $"Size={_mainVideoView.ActualWidth:F0}x{_mainVideoView.ActualHeight:F0}, " +
                    $"HasMediaPlayer={_mainVideoView.MediaPlayer != null}");

                if (_mainVideoView.ActualWidth <= 0 || _mainVideoView.ActualHeight <= 0)
                {
                    return;
                }

                mediaPlayerInitializationInProgress = true;
                try
                {
                    _videoPlayerManager.InitializeMediaPlayer(_mainVideoView);
                    _videoPlayerManager.SetMainVideoView(_mainVideoView);
                    mediaPlayerInitialized = _mainVideoView.MediaPlayer != null;
                    Debug.WriteLine($"[MediaPlayerInit] Main VideoView 绑定完成: Success={mediaPlayerInitialized}");

                    if (mediaPlayerInitialized)
                    {
                        var handler = _mainVideoViewSizeChangedHandler;
                        if (handler != null)
                        {
                            _mainVideoView.SizeChanged -= handler;
                            _mainVideoViewSizeChangedHandler = null;
                        }
                    }
                }
                finally
                {
                    mediaPlayerInitializationInProgress = false;
                }
            }

            _mainVideoViewSizeChangedHandler = (s, e) =>
            {
                try
                {
                    TryInitializeMainVideoView("SizeChanged");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MediaPlayerInit] Main VideoView 初始化异常: {ex}");
                }
            };

            _mainVideoView.SizeChanged += _mainVideoViewSizeChangedHandler;
            _mainVideoView.Loaded += (_, _) => TryInitializeMainVideoView("Loaded");
            hostContainer.Children.Add(_mainVideoView);
            Debug.WriteLine($"[MediaPlayerInit] Main VideoView 已加入宿主: ChildCount={hostContainer.Children.Count}");
            TryInitializeMainVideoView("AfterAttach");
        }

        /// <summary>
        /// 停止并释放媒体模块资源。
        /// </summary>
        public void Shutdown()
        {
            Detach();

            try
            {
                _videoPlayerManager.Stop();
            }
            catch
            {
                // 媒体管理器关闭异常不应阻断主窗口退出
            }

            try
            {
                _videoPlayerManager.Dispose();
            }
            catch
            {
                // 媒体管理器关闭异常不应阻断主窗口退出
            }

            if (_mainVideoView != null && _mainVideoViewSizeChangedHandler != null)
            {
                _mainVideoView.SizeChanged -= _mainVideoViewSizeChangedHandler;
                _mainVideoViewSizeChangedHandler = null;
            }

            if (_hostContainer != null && _mainVideoView != null)
            {
                _hostContainer.Children.Remove(_mainVideoView);
            }

            _mainVideoView = null;
            _hostContainer = null;
        }
    }
}
