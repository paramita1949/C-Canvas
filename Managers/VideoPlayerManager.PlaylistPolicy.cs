using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// VideoPlayerManager 播放列表策略职责：模式、前后曲、自然结束衔接。
    /// </summary>
    public partial class VideoPlayerManager
    {
        public void SetPlayMode(PlayMode mode)
        {
            _playMode = mode;
        }

        public void SetPlaylist(List<string> mediaPaths)
        {
            _playlist = mediaPaths ?? new List<string>();
            _currentIndex = -1;
        }

        public bool PlayNext()
        {
            if (_playlist == null || _playlist.Count == 0)
            {
                return false;
            }

            try
            {
                string nextMedia = null;

                switch (_playMode)
                {
                    case PlayMode.LoopOne:
                        if (!string.IsNullOrEmpty(_currentMediaPath))
                        {
                            return Play(_currentMediaPath);
                        }
                        return false;

                    case PlayMode.Random:
                        _currentIndex = _random.Next(0, _playlist.Count);
                        nextMedia = _playlist[_currentIndex];
                        break;

                    case PlayMode.Sequential:
                    case PlayMode.LoopAll:
                        _currentIndex++;

                        if (_currentIndex >= _playlist.Count)
                        {
                            if (_playMode == PlayMode.LoopAll)
                            {
                                _currentIndex = 0;
                            }
                            else
                            {
                                return false;
                            }
                        }

                        nextMedia = _playlist[_currentIndex];
                        break;
                }

                if (!string.IsNullOrEmpty(nextMedia))
                {
                    return Play(nextMedia);
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool PlayPrevious()
        {
            if (_playlist == null || _playlist.Count == 0)
            {
                return false;
            }

            try
            {
                string prevMedia = null;

                if (_playMode == PlayMode.Random)
                {
                    _currentIndex = _random.Next(0, _playlist.Count);
                }
                else
                {
                    _currentIndex--;

                    if (_currentIndex < 0)
                    {
                        if (_playMode == PlayMode.LoopAll)
                        {
                            _currentIndex = _playlist.Count - 1;
                        }
                        else
                        {
                            _currentIndex = 0;
                        }
                    }
                }

                prevMedia = _playlist[_currentIndex];

                if (!string.IsNullOrEmpty(prevMedia))
                {
                    return Play(prevMedia);
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OnMediaPlayerEndReached(object sender, EventArgs e)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _isPlaying = false;
                _isPaused = false;
                _updateTimer.Stop();
                MediaEnded?.Invoke(this, EventArgs.Empty);

                _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    PlayNext();
                }), DispatcherPriority.Background);
            });
        }
    }
}
