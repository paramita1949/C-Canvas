using System;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// VideoPlayerManager 播放核心职责：初始化、播放控制、进度与底层回调。
    /// </summary>
    public partial class VideoPlayerManager
    {
        private void InitializeLibVLC()
        {
            try
            {
                LibVLCSharp.Shared.Core.Initialize();
                _libVLC = new LibVLC(
                    "--no-osd",
                    "--no-video-title-show",
                    "--verbose=2",
                    "--no-video-deco",
                    "--vout=direct3d11",
                    "--aspect-ratio=",
                    "--autoscale",
                    "--no-video-title",
                    "--embedded-video"
                );
            }
            catch (Exception ex)
            {
                RaisePlaybackError($"视频播放器初始化失败: {ex.Message}");
            }
        }

        public void InitializeMediaPlayer(VideoView videoView)
        {
            try
            {
                if (videoView == null)
                {
                    return;
                }

                if (videoView.MediaPlayer != null)
                {
                    return;
                }

                if (_mediaPlayer == null)
                {
                    _mediaPlayer = new MediaPlayer(_libVLC)
                    {
                        EnableHardwareDecoding = true,
                        EnableMouseInput = false,
                        EnableKeyInput = false
                    };

                    _mediaPlayer.AspectRatio = null;
                    _mediaPlayer.Scale = 0;

                    _mediaPlayer.EndReached += OnMediaPlayerEndReached;
                    _mediaPlayer.Playing += OnMediaPlayerPlaying;
                    _mediaPlayer.Paused += OnMediaPlayerPaused;
                    _mediaPlayer.Stopped += OnMediaPlayerStopped;
                    _mediaPlayer.EncounteredError += OnMediaPlayerError;
                }

                videoView.MediaPlayer = _mediaPlayer;
                DrainUi(DispatcherPriority.Render);
            }
            catch (Exception)
            {
            }
        }

        private void InitializeUpdateTimer()
        {
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _updateTimer.Tick += OnUpdateTimerTick;
        }

        public bool LoadMedia(string mediaPath)
        {
            try
            {
                if (_mediaPlayer == null) return false;
                if (string.IsNullOrEmpty(mediaPath) || !System.IO.File.Exists(mediaPath)) return false;

                VideoView targetVideoView = _isProjectionEnabled ? _projectionVideoView : _mainVideoView;
                if (targetVideoView != null && targetVideoView.MediaPlayer == null)
                {
                    targetVideoView.MediaPlayer = _mediaPlayer;
                    DrainUi(DispatcherPriority.Render);
                }
                else if (targetVideoView != null)
                {
                    bool currentBindingValid = targetVideoView.MediaPlayer != null &&
                                             targetVideoView.MediaPlayer.GetHashCode() == _mediaPlayer.GetHashCode();

                    if (!currentBindingValid)
                    {
                        targetVideoView.MediaPlayer = _mediaPlayer;
                        DrainUi(DispatcherPriority.Render);
                    }
                }

                var oldMedia = _mediaPlayer.Media;
                var media = new Media(_libVLC, new Uri(mediaPath));
                _mediaPlayer.Media = media;

                if (_isPlaying || _isPaused)
                {
                    _isPlaying = false;
                    _isPaused = false;
                    _updateTimer.Stop();
                }

                oldMedia?.Dispose();
                _currentMediaPath = mediaPath;
                MediaChanged?.Invoke(this, mediaPath);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool Play(string mediaPath = null)
        {
            try
            {
                if (_mediaPlayer == null)
                {
                    return false;
                }

                VideoView targetVideoView = null;
                if (_isProjectionEnabled)
                {
                    if (_projectionVideoView != null)
                    {
                        targetVideoView = _projectionVideoView;
                        if (_projectionVideoView.MediaPlayer == null)
                        {
                            _projectionVideoView.MediaPlayer = _mediaPlayer;
                            DrainUi(DispatcherPriority.Background);
                        }

                        if (_projectionVideoView.ActualWidth == 0 || _projectionVideoView.ActualHeight == 0)
                        {
                            DrainUi(DispatcherPriority.Render);
                        }
                    }
                }
                else
                {
                    if (_mainVideoView != null)
                    {
                        targetVideoView = _mainVideoView;
                        if (_mainVideoView.MediaPlayer == null)
                        {
                            _mainVideoView.MediaPlayer = _mediaPlayer;
                            DrainUi(DispatcherPriority.Background);
                        }

                        if (_mainVideoView.ActualWidth == 0 || _mainVideoView.ActualHeight == 0)
                        {
                            DrainUi(DispatcherPriority.Render);
                        }
                    }
                }

                if (targetVideoView != null)
                {
                    bool isVisible = targetVideoView.IsVisible;
                    if (_isProjectionEnabled && !isVisible && _mainVideoView != null)
                    {
                        // 仅在投影视图确实不可用时回退主屏，避免切换瞬间误判导致弹小窗
                        bool projectionUnavailable = _projectionVideoView == null ||
                                                     (_projectionVideoView.ActualWidth <= 0 &&
                                                      _projectionVideoView.ActualHeight <= 0);
                        if (projectionUnavailable)
                        {
                            if (_projectionVideoView != null)
                            {
                                _projectionVideoView.MediaPlayer = null;
                            }
                            _mainVideoView.MediaPlayer = null;
                            DrainUi(DispatcherPriority.Render);
                            _mainVideoView.MediaPlayer = _mediaPlayer;
                            _isProjectionEnabled = false;
                            targetVideoView = _mainVideoView;
                            DrainUi(DispatcherPriority.Render);
                        }
                    }
                }

                // 没有有效宿主时禁止直接播放，避免弹出独立小黑窗
                if (targetVideoView == null)
                {
                    return false;
                }

                bool hostBound = targetVideoView.MediaPlayer != null &&
                                 targetVideoView.MediaPlayer.GetHashCode() == _mediaPlayer.GetHashCode();
                if (!hostBound)
                {
                    targetVideoView.MediaPlayer = _mediaPlayer;
                    DrainUi(DispatcherPriority.Render);
                    hostBound = targetVideoView.MediaPlayer != null &&
                                targetVideoView.MediaPlayer.GetHashCode() == _mediaPlayer.GetHashCode();
                    if (!hostBound)
                    {
                        return false;
                    }
                }

                if (!string.IsNullOrEmpty(mediaPath))
                {
                    if (!LoadMedia(mediaPath))
                    {
                        return false;
                    }

                    if (_playlist != null && _playlist.Count > 0)
                    {
                        int index = _playlist.IndexOf(mediaPath);
                        if (index >= 0)
                        {
                            _currentIndex = index;
                        }
                    }
                }

                if (_isPaused)
                {
                    _mediaPlayer.SetPause(false);
                    _isPaused = false;
                }
                else
                {
                    DetectAndFixSmallWindow();
                    DrainUi(DispatcherPriority.Background);
                    _mediaPlayer.Volume = 70;
                    _mediaPlayer.Mute = false;
                    _mediaPlayer.Play();
                }

                _isPlaying = true;
                _updateTimer.Start();
                PlayStateChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Pause()
        {
            try
            {
                if (_mediaPlayer == null) return;
                if (_isPlaying && !_isPaused)
                {
                    _mediaPlayer.SetPause(true);
                    _isPaused = true;
                    _updateTimer.Stop();
                    PlayStateChanged?.Invoke(this, false);
                }
            }
            catch (Exception)
            {
            }
        }

        public void Stop()
        {
            try
            {
                if (_mediaPlayer == null) return;
                _mediaPlayer.Stop();
                _isPlaying = false;
                _isPaused = false;
                _updateTimer.Stop();
                PlayStateChanged?.Invoke(this, false);
            }
            catch (Exception)
            {
            }
        }

        public void SetVolume(int volume)
        {
            try
            {
                if (_mediaPlayer == null)
                {
                    return;
                }

                volume = Math.Clamp(volume, 0, 100);
                _mediaPlayer.Volume = volume;
            }
            catch (Exception)
            {
            }
        }

        public int GetVolume()
        {
            try
            {
                if (_mediaPlayer == null) return 90;
                return _mediaPlayer.Volume;
            }
            catch
            {
                return 90;
            }
        }

        public void SetPosition(float position)
        {
            try
            {
                if (_mediaPlayer == null) return;
                position = Math.Clamp(position, 0.0f, 1.0f);
                _mediaPlayer.Position = position;
            }
            catch (Exception)
            {
            }
        }

        public float GetPosition()
        {
            try
            {
                if (_mediaPlayer == null) return 0.0f;
                return _mediaPlayer.Position;
            }
            catch
            {
                return 0.0f;
            }
        }

        public long GetTime()
        {
            try
            {
                if (_mediaPlayer == null) return 0;
                return _mediaPlayer.Time;
            }
            catch
            {
                return 0;
            }
        }

        public long GetLength()
        {
            try
            {
                if (_mediaPlayer == null) return 0;
                return _mediaPlayer.Length;
            }
            catch
            {
                return 0;
            }
        }

        private void OnMediaPlayerPlaying(object sender, EventArgs e)
        {
            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_mediaPlayer == null)
                    {
                        return;
                    }
                    DrainUi(DispatcherPriority.Background);

                    try
                    {
                        VideoView currentView = _isProjectionEnabled ? _projectionVideoView : _mainVideoView;
                        if (currentView != null && currentView.ActualWidth > 0 && currentView.ActualHeight > 0)
                        {
                            string containerRatio = $"{(int)currentView.ActualWidth}:{(int)currentView.ActualHeight}";
                            _mediaPlayer.AspectRatio = containerRatio;
                            _mediaPlayer.Scale = 0;
                            _mediaPlayer.CropGeometry = null;
                        }
                        else
                        {
                            _mediaPlayer.AspectRatio = null;
                            _mediaPlayer.Scale = 0;
                            _mediaPlayer.CropGeometry = null;
                        }
                    }
                    catch (Exception)
                    {
                    }

                    bool hasVideo = _mediaPlayer.VideoTrackCount > 0;
                    VideoTrackDetected?.Invoke(this, hasVideo);
                }
                catch (Exception)
                {
                    VideoTrackDetected?.Invoke(this, true);
                }
            }), DispatcherPriority.Background);
        }

        private void OnMediaPlayerPaused(object sender, EventArgs e)
        {
        }

        private void OnMediaPlayerStopped(object sender, EventArgs e)
        {
        }

        private void OnMediaPlayerError(object sender, EventArgs e)
        {
            RaisePlaybackError("媒体播放出现错误，请检查文件格式是否支持");
        }

        private void OnUpdateTimerTick(object sender, EventArgs e)
        {
            if (_isPlaying && !_isPaused)
            {
                try
                {
                    float position = GetPosition();
                    long currentTime = GetTime();
                    long totalTime = GetLength();
                    ProgressUpdated?.Invoke(this, (position, currentTime, totalTime));
                }
                catch (Exception)
                {
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _updateTimer?.Stop();
                _updateTimer = null;

                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Stop();
                    _mediaPlayer.Dispose();
                    _mediaPlayer = null;
                }

                if (_libVLC != null)
                {
                    _libVLC.Dispose();
                    _libVLC = null;
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
