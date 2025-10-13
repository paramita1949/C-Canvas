using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 播放模式枚举
    /// </summary>
    public enum PlayMode
    {
        /// <summary>顺序播放</summary>
        Sequential,
        /// <summary>随机播放</summary>
        Random,
        /// <summary>单曲循环</summary>
        LoopOne,
        /// <summary>列表循环</summary>
        LoopAll
    }

    /// <summary>
    /// 视频播放管理器
    /// 负责视频和音频文件的播放控制
    /// </summary>
    public class VideoPlayerManager : IDisposable
    {
        #region 字段

        private readonly Window _mainWindow;
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private VideoView _mainVideoView;
        private VideoView _projectionVideoView;
        
        // 播放列表
        private List<string> _playlist;
        private int _currentIndex;
        private string _currentMediaPath;
        
        // 播放状态
        private bool _isPlaying;
        private bool _isPaused;
        private PlayMode _playMode;
        
        // 投影相关
        private bool _isProjectionEnabled;
        
        // 更新定时器
        private DispatcherTimer _updateTimer;
        
        // 随机数生成器
        private Random _random;

        #endregion

        #region 事件

        /// <summary>
        /// 播放状态改变事件
        /// </summary>
        public event EventHandler<bool> PlayStateChanged;

        /// <summary>
        /// 媒体改变事件
        /// </summary>
        public event EventHandler<string> MediaChanged;

        /// <summary>
        /// 播放结束事件
        /// </summary>
        public event EventHandler MediaEnded;

        /// <summary>
        /// 播放进度更新事件 (position: 0.0-1.0, currentTime: 毫秒, totalTime: 毫秒)
        /// </summary>
        public event EventHandler<(float position, long currentTime, long totalTime)> ProgressUpdated;

        /// <summary>
        /// 视频轨道检测事件 (hasVideo: 是否有视频轨道)
        /// </summary>
        public event EventHandler<bool> VideoTrackDetected;

        #endregion

        #region 属性

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public bool IsPlaying => _isPlaying;

        /// <summary>
        /// 是否暂停
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// 当前播放模式
        /// </summary>
        public PlayMode CurrentPlayMode => _playMode;

        /// <summary>
        /// 当前媒体路径
        /// </summary>
        public string CurrentMediaPath => _currentMediaPath;

        /// <summary>
        /// 播放列表数量
        /// </summary>
        public int PlaylistCount => _playlist?.Count ?? 0;

        /// <summary>
        /// 当前播放索引
        /// </summary>
        public int CurrentIndex => _currentIndex;

        #endregion

        #region 构造函数

        public VideoPlayerManager(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _playlist = new List<string>();
            _currentIndex = -1;
            _playMode = PlayMode.Random; // 默认使用随机播放模式
            _random = new Random();

            InitializeLibVLC();
            InitializeUpdateTimer();
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化LibVLC
        /// </summary>
        private void InitializeLibVLC()
        {
            try
            {
                LibVLCSharp.Shared.Core.Initialize();
                
                // 创建LibVLC实例
                _libVLC = new LibVLC(
                    "--no-osd",                    // 不显示屏幕显示
                    "--no-video-title-show",       // 不显示视频标题
                    "--quiet"                      // 安静模式
                );

                // 创建媒体播放器
                _mediaPlayer = new MediaPlayer(_libVLC)
                {
                    // 禁用视频输出直到VideoView绑定
                    EnableHardwareDecoding = true,
                    EnableMouseInput = false,
                    EnableKeyInput = false
                };

                // 绑定事件
                _mediaPlayer.EndReached += OnMediaPlayerEndReached;
                _mediaPlayer.Playing += OnMediaPlayerPlaying;
                _mediaPlayer.Paused += OnMediaPlayerPaused;
                _mediaPlayer.Stopped += OnMediaPlayerStopped;
                _mediaPlayer.EncounteredError += OnMediaPlayerError;

                // System.Diagnostics.Debug.WriteLine("✅ LibVLC 初始化成功");
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ LibVLC 初始化失败: {ex.Message}");
                System.Windows.MessageBox.Show($"视频播放器初始化失败: {ex.Message}\n\n请确保已安装VLC播放器组件。", 
                    "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 初始化更新定时器
        /// </summary>
        private void InitializeUpdateTimer()
        {
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // 每500ms更新一次
            };
            _updateTimer.Tick += OnUpdateTimerTick;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置主窗口视频视图
        /// </summary>
        public void SetMainVideoView(VideoView videoView)
        {
            _mainVideoView = videoView;
            if (_mainVideoView != null && _mediaPlayer != null)
            {
                // 强制绑定MediaPlayer到VideoView
                _mainVideoView.MediaPlayer = _mediaPlayer;
                
                // 确保VideoView可见
                _mainVideoView.Visibility = System.Windows.Visibility.Visible;
                
                // System.Diagnostics.Debug.WriteLine("✅ 主窗口视频视图已设置并绑定到MediaPlayer");
            }
        }

        /// <summary>
        /// 设置投影窗口视频视图
        /// </summary>
        public void SetProjectionVideoView(VideoView videoView)
        {
            _projectionVideoView = videoView;
            // System.Diagnostics.Debug.WriteLine("✅ 投影窗口视频视图已设置");
        }

        /// <summary>
        /// 加载媒体文件
        /// </summary>
        public bool LoadMedia(string mediaPath)
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine($"📥 LoadMedia 开始: {System.IO.Path.GetFileName(mediaPath)}");
                
                if (string.IsNullOrEmpty(mediaPath) || !System.IO.File.Exists(mediaPath))
                {
                    // System.Diagnostics.Debug.WriteLine($"❌ 文件不存在");
                    return false;
                }

                // 停止当前播放
                if (_isPlaying)
                {
                    // System.Diagnostics.Debug.WriteLine("⏹ 停止当前播放");
                    Stop();
                }
                
                // System.Diagnostics.Debug.WriteLine($"🔍 当前模式: _isProjectionEnabled={_isProjectionEnabled}");
                // System.Diagnostics.Debug.WriteLine($"🔍 VideoView状态: Main={(_mainVideoView?.MediaPlayer != null ? "已绑定" : "未绑定")}, Projection={(_projectionVideoView?.MediaPlayer != null ? "已绑定" : "未绑定")}");

                // 创建媒体对象
                // System.Diagnostics.Debug.WriteLine("📦 创建Media对象");
                var media = new Media(_libVLC, new Uri(mediaPath));
                _mediaPlayer.Media = media;
                
                _currentMediaPath = mediaPath;
                
                // System.Diagnostics.Debug.WriteLine($"✅ 媒体已加载到MediaPlayer");
                
                // 触发媒体改变事件
                MediaChanged?.Invoke(this, mediaPath);
                
                return true;
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 加载媒体失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 播放
        /// </summary>
        public bool Play(string mediaPath = null)
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine($"▶ ===== Play 开始 =====");
                // System.Diagnostics.Debug.WriteLine($"▶ 参数: mediaPath={mediaPath ?? "null"}");
                // System.Diagnostics.Debug.WriteLine($"▶ 状态: _isProjectionEnabled={_isProjectionEnabled}, _isPaused={_isPaused}");
                
                // 确保VideoView已绑定
                if (_isProjectionEnabled)
                {
                    // System.Diagnostics.Debug.WriteLine($"🔍 投影模式 - 检查绑定");
                    if (_projectionVideoView != null)
                    {
                        if (_projectionVideoView.MediaPlayer == null)
                        {
                            // System.Diagnostics.Debug.WriteLine("🔧 绑定投影VideoView.MediaPlayer");
                            _projectionVideoView.MediaPlayer = _mediaPlayer;
                        }
                        else
                        {
                            // System.Diagnostics.Debug.WriteLine("✅ 投影VideoView已绑定");
                        }
                    }
                }
                else
                {
                    // System.Diagnostics.Debug.WriteLine($"🔍 主屏幕模式 - 检查绑定");
                    if (_mainVideoView != null)
                    {
                        if (_mainVideoView.MediaPlayer == null)
                        {
                            // System.Diagnostics.Debug.WriteLine("🔧 绑定主VideoView.MediaPlayer");
                            _mainVideoView.MediaPlayer = _mediaPlayer;
                        }
                        else
                        {
                            // System.Diagnostics.Debug.WriteLine("✅ 主VideoView已绑定");
                        }
                    }
                }
                
                // 如果提供了新的媒体路径，先加载
                if (!string.IsNullOrEmpty(mediaPath))
                {
                    // System.Diagnostics.Debug.WriteLine("📂 加载新媒体文件");
                    if (!LoadMedia(mediaPath))
                    {
                        return false;
                    }
                    
                    // 更新当前索引
                    if (_playlist != null && _playlist.Count > 0)
                    {
                        int index = _playlist.IndexOf(mediaPath);
                        if (index >= 0)
                        {
                            _currentIndex = index;
                            // System.Diagnostics.Debug.WriteLine($"📍 播放索引: {_currentIndex + 1}/{_playlist.Count}");
                        }
                    }
                }

                // 如果是暂停状态，恢复播放
                if (_isPaused)
                {
                    // System.Diagnostics.Debug.WriteLine("⏯ 从暂停恢复播放");
                    _mediaPlayer.SetPause(false);
                    _isPaused = false;
                }
                else
                {
                    // 延迟10ms确保VideoView完全就绪
                    // System.Diagnostics.Debug.WriteLine("⏳ 等待10ms确保VideoView就绪");
                    System.Threading.Thread.Sleep(10);
                    
                    // System.Diagnostics.Debug.WriteLine("▶ 调用 _mediaPlayer.Play()");
                    _mediaPlayer.Play();
                }

                _isPlaying = true;
                _updateTimer.Start();
                
                // System.Diagnostics.Debug.WriteLine($"✅ ===== Play 完成 =====");
                
                PlayStateChanged?.Invoke(this, true);

                return true;
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 播放失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 暂停
        /// </summary>
        public void Pause()
        {
            try
            {
                if (_isPlaying && !_isPaused)
                {
                    _mediaPlayer.SetPause(true);
                    _isPaused = true;
                    _updateTimer.Stop();

                    // System.Diagnostics.Debug.WriteLine("⏸ 播放已暂停");
                    
                    // 触发播放状态改变事件
                    PlayStateChanged?.Invoke(this, false);
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 暂停失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止
        /// </summary>
        public void Stop()
        {
            try
            {
                _mediaPlayer.Stop();
                _isPlaying = false;
                _isPaused = false;
                _updateTimer.Stop();

                // System.Diagnostics.Debug.WriteLine("⏹ 播放已停止");
                
                // 触发播放状态改变事件
                PlayStateChanged?.Invoke(this, false);
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 停止失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置音量 (0-100)
        /// </summary>
        public void SetVolume(int volume)
        {
            try
            {
                volume = Math.Clamp(volume, 0, 100);
                _mediaPlayer.Volume = volume;
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 设置音量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取音量 (0-100)
        /// </summary>
        public int GetVolume()
        {
            try
            {
                return _mediaPlayer.Volume;
            }
            catch
            {
                return 50; // 默认音量
            }
        }

        /// <summary>
        /// 设置播放进度 (0.0-1.0)
        /// </summary>
        public void SetPosition(float position)
        {
            try
            {
                position = Math.Clamp(position, 0.0f, 1.0f);
                _mediaPlayer.Position = position;
                // System.Diagnostics.Debug.WriteLine($"📍 播放进度已设置: {position:P1}");
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 设置播放进度失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取播放进度 (0.0-1.0)
        /// </summary>
        public float GetPosition()
        {
            try
            {
                return _mediaPlayer.Position;
            }
            catch
            {
                return 0.0f;
            }
        }

        /// <summary>
        /// 获取当前播放时间（毫秒）
        /// </summary>
        public long GetTime()
        {
            try
            {
                return _mediaPlayer.Time;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取媒体总时长（毫秒）
        /// </summary>
        public long GetLength()
        {
            try
            {
                return _mediaPlayer.Length;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 设置播放模式
        /// </summary>
        public void SetPlayMode(PlayMode mode)
        {
            _playMode = mode;
            // System.Diagnostics.Debug.WriteLine($"🔄 播放模式已设置: {mode}");
        }

        /// <summary>
        /// 设置播放列表
        /// </summary>
        public void SetPlaylist(List<string> mediaPaths)
        {
            _playlist = mediaPaths ?? new List<string>();
            _currentIndex = -1;
            // System.Diagnostics.Debug.WriteLine($"📋 播放列表已设置: {_playlist.Count} 个文件");
        }

        /// <summary>
        /// 播放下一个
        /// </summary>
        public bool PlayNext()
        {
            if (_playlist == null || _playlist.Count == 0)
            {
                // System.Diagnostics.Debug.WriteLine("❌ 播放列表为空");
                return false;
            }

            try
            {
                string nextMedia = null;

                switch (_playMode)
                {
                    case PlayMode.LoopOne:
                        // 单曲循环，重新播放当前曲目
                        if (!string.IsNullOrEmpty(_currentMediaPath))
                        {
                            return Play(_currentMediaPath);
                        }
                        return false;

                    case PlayMode.Random:
                        // 随机播放
                        _currentIndex = _random.Next(0, _playlist.Count);
                        nextMedia = _playlist[_currentIndex];
                        // System.Diagnostics.Debug.WriteLine($"🎲 随机播放: {System.IO.Path.GetFileName(nextMedia)} ({_currentIndex + 1}/{_playlist.Count})");
                        break;

                    case PlayMode.Sequential:
                    case PlayMode.LoopAll:
                        // 顺序播放或列表循环
                        _currentIndex++;
                        
                        if (_currentIndex >= _playlist.Count)
                        {
                            if (_playMode == PlayMode.LoopAll)
                            {
                                // 列表循环，回到开头
                                _currentIndex = 0;
                            }
                            else
                            {
                                // 顺序播放结束
                                // System.Diagnostics.Debug.WriteLine("📋 播放列表已结束");
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
                // System.Diagnostics.Debug.WriteLine($"❌ 播放下一个失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 播放上一个
        /// </summary>
        public bool PlayPrevious()
        {
            if (_playlist == null || _playlist.Count == 0)
            {
                // System.Diagnostics.Debug.WriteLine("❌ 播放列表为空");
                return false;
            }

            try
            {
                string prevMedia = null;

                if (_playMode == PlayMode.Random)
                {
                    // 随机模式下，随机选择
                    _currentIndex = _random.Next(0, _playlist.Count);
                }
                else
                {
                    // 其他模式，播放上一个
                    _currentIndex--;
                    
                    if (_currentIndex < 0)
                    {
                        if (_playMode == PlayMode.LoopAll)
                        {
                            // 列表循环，跳到最后
                            _currentIndex = _playlist.Count - 1;
                        }
                        else
                        {
                            // 已经是第一个
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
                // System.Diagnostics.Debug.WriteLine($"❌ 播放上一个失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切换到投影模式（不停止当前播放，只切换输出目标）
        /// </summary>
        public void SwitchToProjectionMode()
        {
            // System.Diagnostics.Debug.WriteLine("🔄 SwitchToProjectionMode 开始");
            if (_projectionVideoView != null)
            {
                // System.Diagnostics.Debug.WriteLine($"🔧 设置 _projectionVideoView.MediaPlayer = _mediaPlayer");
                _projectionVideoView.MediaPlayer = _mediaPlayer;
                _isProjectionEnabled = true;
                // System.Diagnostics.Debug.WriteLine($"✅ 投影模式已切换: _isProjectionEnabled={_isProjectionEnabled}");
            }
            else
            {
                // System.Diagnostics.Debug.WriteLine("❌ _projectionVideoView 为 null");
            }
        }
        
        /// <summary>
        /// 启用投影播放
        /// </summary>
        public void EnableProjection()
        {
            try
            {
                if (_projectionVideoView == null)
                {
                    // System.Diagnostics.Debug.WriteLine("❌ 投影视频视图未设置");
                    return;
                }

                // 保存当前播放状态
                bool wasPlaying = _isPlaying;
                float currentPosition = GetPosition();

                // 停止播放
                if (wasPlaying)
                {
                    Stop();
                }

                // 切换到投影视频视图
                _projectionVideoView.MediaPlayer = _mediaPlayer;
                _isProjectionEnabled = true;

                // System.Diagnostics.Debug.WriteLine("✅ 投影播放已启用");

                // 如果之前在播放，恢复播放
                if (wasPlaying && !string.IsNullOrEmpty(_currentMediaPath))
                {
                    _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Play(_currentMediaPath);
                        
                        // 恢复播放位置
                        if (currentPosition > 0)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                SetPosition(currentPosition);
                            }), DispatcherPriority.Background);
                        }
                    }), DispatcherPriority.Background);
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 启用投影播放失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 禁用投影播放
        /// </summary>
        public void DisableProjection()
        {
            try
            {
                if (_mainVideoView == null)
                {
                    // System.Diagnostics.Debug.WriteLine("❌ 主窗口视频视图未设置");
                    return;
                }

                // 保存当前播放状态
                bool wasPlaying = _isPlaying;
                float currentPosition = GetPosition();

                // 停止播放
                if (wasPlaying)
                {
                    Stop();
                }

                // 切换回主窗口视频视图
                _mainVideoView.MediaPlayer = _mediaPlayer;
                _isProjectionEnabled = false;

                // System.Diagnostics.Debug.WriteLine("✅ 投影播放已禁用");

                // 如果之前在播放，恢复播放
                if (wasPlaying && !string.IsNullOrEmpty(_currentMediaPath))
                {
                    _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Play(_currentMediaPath);
                        
                        // 恢复播放位置
                        if (currentPosition > 0)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                SetPosition(currentPosition);
                            }), DispatcherPriority.Background);
                        }
                    }), DispatcherPriority.Background);
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 禁用投影播放失败: {ex.Message}");
            }
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 媒体播放结束事件
        /// </summary>
        private void OnMediaPlayerEndReached(object sender, EventArgs e)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                // System.Diagnostics.Debug.WriteLine("🏁 媒体播放结束");
                
                _isPlaying = false;
                _isPaused = false;
                _updateTimer.Stop();

                // 触发播放结束事件
                MediaEnded?.Invoke(this, EventArgs.Empty);

                // 延迟播放下一个，避免在VLC回调中直接操作
                _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    PlayNext();
                }), DispatcherPriority.Background);
            });
        }

        /// <summary>
        /// 媒体开始播放事件
        /// </summary>
        private void OnMediaPlayerPlaying(object sender, EventArgs e)
        {
            // System.Diagnostics.Debug.WriteLine("▶ 媒体开始播放");
            
            // 检测是否有视频轨道
            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 等待一小段时间让媒体信息加载完成
                    System.Threading.Thread.Sleep(100);
                    
                    // 检查是否有视频轨道
                    bool hasVideo = _mediaPlayer.VideoTrackCount > 0;
                    
                    // System.Diagnostics.Debug.WriteLine($"🎬 视频轨道检测: VideoTrackCount={_mediaPlayer.VideoTrackCount}, HasVideo={hasVideo}");
                    
                    // 如果没有视频轨道，解绑 VideoView，让音频在后台播放
                    if (!hasVideo)
                    {
                        // System.Diagnostics.Debug.WriteLine("🎵 无视频轨道，解绑VideoView，后台播放音频");
                        
                        // 解绑主窗口 VideoView
                        if (_mainVideoView != null && _mainVideoView.MediaPlayer != null)
                        {
                            _mainVideoView.MediaPlayer = null;
                            // System.Diagnostics.Debug.WriteLine("✅ 已解绑主窗口VideoView");
                        }
                        
                        // 解绑投影窗口 VideoView
                        if (_projectionVideoView != null && _projectionVideoView.MediaPlayer != null)
                        {
                            _projectionVideoView.MediaPlayer = null;
                            // System.Diagnostics.Debug.WriteLine("✅ 已解绑投影窗口VideoView");
                        }
                    }
                    
                    // 触发事件通知主窗口
                    VideoTrackDetected?.Invoke(this, hasVideo);
                }
                catch (Exception)
                {
                    // System.Diagnostics.Debug.WriteLine($"❌ 视频轨道检测失败: {ex.Message}");
                    // 出错时假设有视频
                    VideoTrackDetected?.Invoke(this, true);
                }
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// 媒体暂停事件
        /// </summary>
        private void OnMediaPlayerPaused(object sender, EventArgs e)
        {
            // System.Diagnostics.Debug.WriteLine("⏸ 媒体已暂停");
        }

        /// <summary>
        /// 媒体停止事件
        /// </summary>
        private void OnMediaPlayerStopped(object sender, EventArgs e)
        {
            // System.Diagnostics.Debug.WriteLine("⏹ 媒体已停止");
        }

        /// <summary>
        /// 媒体播放错误事件
        /// </summary>
        private void OnMediaPlayerError(object sender, EventArgs e)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                // System.Diagnostics.Debug.WriteLine("❌ 媒体播放错误");
                System.Windows.MessageBox.Show("媒体播放出现错误，请检查文件格式是否支持。", "播放错误", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            });
        }

        /// <summary>
        /// 更新定时器事件
        /// </summary>
        private void OnUpdateTimerTick(object sender, EventArgs e)
        {
            if (_isPlaying && !_isPaused)
            {
                try
                {
                    float position = GetPosition();
                    long currentTime = GetTime();
                    long totalTime = GetLength();

                    // 触发进度更新事件
                    ProgressUpdated?.Invoke(this, (position, currentTime, totalTime));
                }
                catch (Exception)
                {
                    // System.Diagnostics.Debug.WriteLine($"❌ 更新播放进度失败: {ex.Message}");
                }
            }
        }

        #endregion

        #region IDisposable

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

                // System.Diagnostics.Debug.WriteLine("✅ VideoPlayerManager 资源已清理");
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 清理资源失败: {ex.Message}");
            }
        }

        #endregion
    }
}

