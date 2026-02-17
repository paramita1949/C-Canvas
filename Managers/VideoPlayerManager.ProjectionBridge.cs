using System;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// VideoPlayerManager 投影桥接职责：主屏/投影输出切换与绑定诊断。
    /// </summary>
    public partial class VideoPlayerManager
    {
        public void SetMainVideoView(VideoView videoView)
        {
            _mainVideoView = videoView;
            if (_mainVideoView != null && _mediaPlayer != null)
            {
                _mainVideoView.MediaPlayer = null;
                DrainUi(DispatcherPriority.Render);
                _mainVideoView.MediaPlayer = _mediaPlayer;

                _mainVideoView.Visibility = System.Windows.Visibility.Visible;
                _isProjectionEnabled = false;
            }
        }

        public void SetProjectionVideoView(VideoView videoView)
        {
            _projectionVideoView = videoView;
        }

        public void SwitchToProjectionMode()
        {
            if (_mediaPlayer == null)
            {
                return;
            }

            if (_projectionVideoView != null)
            {
                _projectionVideoView.MediaPlayer = _mediaPlayer;
                _isProjectionEnabled = true;
            }
        }

        public void EnableProjection()
        {
            try
            {
                if (_mediaPlayer == null)
                {
                    return;
                }

                if (_projectionVideoView == null)
                {
                    return;
                }

                bool wasPlaying = _isPlaying;
                bool wasPaused = _isPaused;
                float currentPosition = GetPosition();
                string currentMedia = _currentMediaPath;

                if (_isPlaying || _isPaused)
                {
                    _mediaPlayer.Stop();
                    _isPlaying = false;
                    _isPaused = false;
                    _updateTimer.Stop();
                }

                if (_mainVideoView != null && _mainVideoView.MediaPlayer != null)
                {
                    _mainVideoView.MediaPlayer = null;
                }

                DrainUi(DispatcherPriority.Render);
                _projectionVideoView.MediaPlayer = null;
                DrainUi(DispatcherPriority.Render);
                _projectionVideoView.MediaPlayer = _mediaPlayer;
                _isProjectionEnabled = true;

                if ((wasPlaying || wasPaused) && !string.IsNullOrEmpty(currentMedia))
                {
                    _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var media = new Media(_libVLC, new Uri(currentMedia));
                        _mediaPlayer.Media?.Dispose();
                        _mediaPlayer.Media = media;

                        DrainUi(DispatcherPriority.Background);
                        _mediaPlayer.Play();
                        _isPlaying = true;
                        _updateTimer.Start();

                        if (wasPaused)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                _mediaPlayer.SetPause(true);
                                _isPaused = true;
                                _updateTimer.Stop();
                            }), DispatcherPriority.Background);
                        }

                        if (currentPosition > 0.01f)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                SetPosition(currentPosition);
                            }), DispatcherPriority.ContextIdle);
                        }
                    }), DispatcherPriority.Normal);
                }
            }
            catch (Exception)
            {
            }
        }

        public void DisableProjection()
        {
            try
            {
                if (_mediaPlayer == null)
                {
                    return;
                }

                if (_mainVideoView == null)
                {
                    return;
                }

                bool wasPlaying = _isPlaying;
                bool wasPaused = _isPaused;
                float currentPosition = GetPosition();
                string currentMedia = _currentMediaPath;

                if (_isPlaying || _isPaused)
                {
                    _mediaPlayer.Stop();
                    _isPlaying = false;
                    _isPaused = false;
                    _updateTimer.Stop();
                }

                if (_projectionVideoView != null && _projectionVideoView.MediaPlayer != null)
                {
                    _projectionVideoView.MediaPlayer = null;
                }

                DrainUi(DispatcherPriority.Render);
                _mainVideoView.MediaPlayer = null;
                DrainUi(DispatcherPriority.Render);
                _mainVideoView.MediaPlayer = _mediaPlayer;
                _isProjectionEnabled = false;

                if ((wasPlaying || wasPaused) && !string.IsNullOrEmpty(currentMedia))
                {
                    _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var media = new Media(_libVLC, new Uri(currentMedia));
                        _mediaPlayer.Media?.Dispose();
                        _mediaPlayer.Media = media;

                        DrainUi(DispatcherPriority.Background);
                        _mediaPlayer.Play();
                        _isPlaying = true;
                        _updateTimer.Start();

                        if (wasPaused)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                _mediaPlayer.SetPause(true);
                                _isPaused = true;
                                _updateTimer.Stop();
                            }), DispatcherPriority.Background);
                        }

                        if (currentPosition > 0.01f)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                SetPosition(currentPosition);
                            }), DispatcherPriority.ContextIdle);
                        }
                    }), DispatcherPriority.Normal);
                }
            }
            catch (Exception)
            {
            }
        }

        public void ResetProjectionMode()
        {
            try
            {
                if (_projectionVideoView != null && _projectionVideoView.MediaPlayer != null)
                {
                    _projectionVideoView.MediaPlayer = null;
                }

                _isProjectionEnabled = false;
            }
            catch (Exception)
            {
            }
        }

        private void DetectAndFixSmallWindow()
        {
            try
            {
                if (_mediaPlayer == null) return;

                VideoView expectedVideoView = _isProjectionEnabled ? _projectionVideoView : _mainVideoView;
                if (expectedVideoView == null)
                {
                    return;
                }

                bool isCorrectlyBound = expectedVideoView.MediaPlayer != null &&
                                       expectedVideoView.MediaPlayer.GetHashCode() == _mediaPlayer.GetHashCode();

                if (!isCorrectlyBound)
                {
                    if (_mainVideoView != null && _mainVideoView.MediaPlayer != null && !_isProjectionEnabled)
                    {
                        _mainVideoView.MediaPlayer = null;
                    }

                    if (_projectionVideoView != null && _projectionVideoView.MediaPlayer != null && _isProjectionEnabled)
                    {
                        _projectionVideoView.MediaPlayer = null;
                    }

                    DrainUi(DispatcherPriority.Background);
                    expectedVideoView.MediaPlayer = _mediaPlayer;
                    DrainUi(DispatcherPriority.Render);
                }
            }
            catch (Exception)
            {
            }
        }

        public string GetBindingDiagnostics()
        {
            var diagnostics = new System.Text.StringBuilder();
            diagnostics.AppendLine("=== MediaPlayer绑定诊断 ===");
            diagnostics.AppendLine($"MediaPlayer: {(_mediaPlayer != null ? $"存在 (HashCode:{_mediaPlayer.GetHashCode()})" : "null")}");
            diagnostics.AppendLine($"投影模式: {_isProjectionEnabled}");
            diagnostics.AppendLine($"主窗口VideoView: {(_mainVideoView != null ? "存在" : "null")}");
            diagnostics.AppendLine($"主WindowVideoView绑定: {(_mainVideoView?.MediaPlayer != null ? $"已绑定 (HashCode:{_mainVideoView.MediaPlayer.GetHashCode()})" : "未绑定")}");
            diagnostics.AppendLine($"投影VideoView: {(_projectionVideoView != null ? "存在" : "null")}");
            diagnostics.AppendLine($"投影VideoView绑定: {(_projectionVideoView?.MediaPlayer != null ? $"已绑定 (HashCode:{_projectionVideoView.MediaPlayer.GetHashCode()})" : "未绑定")}");

            VideoView expectedView = _isProjectionEnabled ? _projectionVideoView : _mainVideoView;
            bool correctBinding = expectedView?.MediaPlayer != null &&
                                expectedView.MediaPlayer.GetHashCode() == _mediaPlayer?.GetHashCode();
            diagnostics.AppendLine($"预期绑定正确性: {correctBinding}");

            return diagnostics.ToString();
        }
    }
}
