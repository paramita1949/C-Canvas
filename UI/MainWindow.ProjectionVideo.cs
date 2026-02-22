using System;
using System.Windows;
using LibVLCSharp.WPF;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 投影视频初始化与延迟播放
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 投影VideoView加载完成事件处理
        /// </summary>
        private void OnProjectionVideoViewLoaded(object sender, VideoView projectionVideoView)
        {
            try
            {
                // 如果VideoView尺寸为0，等待SizeChanged事件
                if (projectionVideoView != null && (projectionVideoView.ActualWidth == 0 || projectionVideoView.ActualHeight == 0))
                {
                    bool initialized = false;
                    SizeChangedEventHandler sizeChangedHandler = null;

                    sizeChangedHandler = (s, e) =>
                    {
                        if (!initialized && projectionVideoView.ActualWidth > 0 && projectionVideoView.ActualHeight > 0)
                        {
                            if (_videoPlayerManager != null)
                            {
                                _videoPlayerManager.InitializeMediaPlayer(projectionVideoView);
                                _videoPlayerManager.SetProjectionVideoView(projectionVideoView);

                                if (_videoPlayerManager.IsPlaying)
                                {
                                    EnableVideoProjection();
                                }
                            }

                            initialized = true;
                            projectionVideoView.SizeChanged -= sizeChangedHandler;

                            if (!string.IsNullOrEmpty(_pendingProjectionVideoPath))
                            {
                                PlayPendingProjectionVideo();
                            }
                        }
                    };

                    projectionVideoView.SizeChanged += sizeChangedHandler;

                    _projectionTimeoutTimer = new System.Windows.Threading.DispatcherTimer();
                    _projectionTimeoutTimer.Interval = TimeSpan.FromSeconds(3);
                    _projectionTimeoutTimer.Tick += (s, e) =>
                    {
                        _projectionTimeoutTimer.Stop();
                        _projectionTimeoutTimer = null;
                        if (!initialized)
                        {
                            if (_videoPlayerManager != null)
                            {
                                _videoPlayerManager.InitializeMediaPlayer(projectionVideoView);
                                _videoPlayerManager.SetProjectionVideoView(projectionVideoView);

                                if (_videoPlayerManager.IsPlaying)
                                {
                                    EnableVideoProjection();
                                }
                            }

                            initialized = true;
                            projectionVideoView.SizeChanged -= sizeChangedHandler;
                        }
                    };
                    _projectionTimeoutTimer.Start();
                }
                else if (projectionVideoView != null)
                {
                    if (_videoPlayerManager != null)
                    {
                        _videoPlayerManager.InitializeMediaPlayer(projectionVideoView);
                        _videoPlayerManager.SetProjectionVideoView(projectionVideoView);

                        if (_videoPlayerManager.IsPlaying)
                        {
                            EnableVideoProjection();
                        }

                        if (!string.IsNullOrEmpty(_pendingProjectionVideoPath))
                        {
                            PlayPendingProjectionVideo();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 播放待投影的视频
        /// </summary>
        private void PlayPendingProjectionVideo()
        {
            try
            {
                if (string.IsNullOrEmpty(_pendingProjectionVideoPath))
                    return;

                if (!EnsureVideoPlayerInitialized("PlayPendingProjectionVideo"))
                {
                    ShowStatus("❌ 媒体播放器初始化失败");
                    return;
                }

                string videoPath = _pendingProjectionVideoPath;
                _pendingProjectionVideoPath = null;

                _videoPlayerManager.SwitchToProjectionMode();
                BuildVideoPlaylist(videoPath);
                _videoPlayerManager.Play(videoPath);

                ShowStatus($"🎬 正在投影播放: {System.IO.Path.GetFileName(videoPath)}");
            }
            catch (Exception)
            {
            }
        }
    }
}
