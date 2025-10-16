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
        
        /// <summary>
        /// 获取是否已启用投影模式
        /// </summary>
        public bool IsProjectionEnabled => _isProjectionEnabled;
        
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
                System.Diagnostics.Debug.WriteLine("🔧 ===== LibVLC 初始化开始 =====");
                LibVLCSharp.Shared.Core.Initialize();
                System.Diagnostics.Debug.WriteLine("✅ LibVLCSharp.Core 初始化完成");
                
                // 只创建LibVLC实例，MediaPlayer将在VideoView加载后创建
                _libVLC = new LibVLC(
                    "--no-osd",                    // 禁用屏显信息
                    "--no-video-title-show",       // 禁用视频标题显示
                    //"--quiet",                     // 静默模式
                    "--verbose=2",                 // 详细日志级别 
                    "--no-video-deco",             // 禁用视频窗口装饰
                    //"--no-embedded-video",         // 🔥 关键：禁用嵌入式视频窗口
                    "--vout=directdraw",           // 视频输出方式
                    "--aspect-ratio=",             // 🔥 空字符串 = 自动拉伸
                    "--autoscale",                 // 🔥 自动缩放
                    "--no-video-title"             // 不显示视频标题
                );
                
                System.Diagnostics.Debug.WriteLine($"✅ LibVLC实例创建成功，版本: {_libVLC.Version}");
                System.Diagnostics.Debug.WriteLine("🔧 ===== LibVLC 初始化完成 =====");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ LibVLC初始化失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 堆栈: {ex.StackTrace}");
                System.Windows.MessageBox.Show($"视频播放器初始化失败: {ex.Message}\n\n请确保已安装VLC播放器组件。", 
                    "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 初始化MediaPlayer并立即绑定到VideoView（在VideoView加载后调用）
        /// </summary>
        public void InitializeMediaPlayer(VideoView videoView)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔵 ===== InitializeMediaPlayer 开始 =====");
                System.Diagnostics.Debug.WriteLine($"🔵 参数 VideoView: {(videoView != null ? "存在" : "null")}");
                System.Diagnostics.Debug.WriteLine($"🔵 当前 _mediaPlayer: {(_mediaPlayer != null ? "已存在" : "null")}");
                
                // 检查是否已经为这个VideoView创建了MediaPlayer
                if (videoView.MediaPlayer != null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ VideoView已有MediaPlayer，跳过重复创建");
                    System.Diagnostics.Debug.WriteLine("🔵 ===== InitializeMediaPlayer 结束（跳过） =====");
                    return;
                }

                if (videoView == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ VideoView为null，无法创建MediaPlayer");
                    System.Diagnostics.Debug.WriteLine("🔵 ===== InitializeMediaPlayer 结束（失败） =====");
                    return;
                }

                // 如果主MediaPlayer不存在，创建它
                if (_mediaPlayer == null)
                {
                    System.Diagnostics.Debug.WriteLine("🟢 开始创建主MediaPlayer...");
                    
                    // 创建媒体播放器
                    _mediaPlayer = new MediaPlayer(_libVLC)
                    {
                        EnableHardwareDecoding = true,
                        EnableMouseInput = false,
                        EnableKeyInput = false
                    };

                    System.Diagnostics.Debug.WriteLine($"🟢 主MediaPlayer已创建，HashCode: {_mediaPlayer.GetHashCode()}");
                    
                    // 🔥 关键设置：视频缩放模式
                    // 注意：AspectRatio会在OnMediaPlayerPlaying事件中动态设置为容器的实际宽高比
                    // 这样可以确保视频强制拉伸填充整个VideoView，无论视频本身的宽高比
                    _mediaPlayer.AspectRatio = null;  // 初始设置为null，播放时动态设置
                    _mediaPlayer.Scale = 0;           // 0 = 自适应填充（FitScreen）
                    System.Diagnostics.Debug.WriteLine("🎬 初始化视频缩放: AspectRatio=null (播放时动态设置), Scale=0");
                    
                    // 绑定事件
                    _mediaPlayer.EndReached += OnMediaPlayerEndReached;
                    _mediaPlayer.Playing += OnMediaPlayerPlaying;
                    _mediaPlayer.Paused += OnMediaPlayerPaused;
                    _mediaPlayer.Stopped += OnMediaPlayerStopped;
                    _mediaPlayer.EncounteredError += OnMediaPlayerError;

                    System.Diagnostics.Debug.WriteLine("✅ 主MediaPlayer事件已绑定");
                }

                System.Diagnostics.Debug.WriteLine("🟢 立即绑定到VideoView...");
                // System.Diagnostics.Debug.WriteLine($"🔍 绑定前检查 - VideoView: {(videoView != null ? "存在" : "null")}");
                // System.Diagnostics.Debug.WriteLine($"🔍 绑定前检查 - MediaPlayer: {(_mediaPlayer != null ? $"存在 (HashCode:{_mediaPlayer.GetHashCode()})" : "null")}");
                // System.Diagnostics.Debug.WriteLine($"🔍 绑定前检查 - VideoView.IsLoaded: {videoView.IsLoaded}");
                // System.Diagnostics.Debug.WriteLine($"🔍 绑定前检查 - VideoView.ActualWidth: {videoView.ActualWidth}");
                // System.Diagnostics.Debug.WriteLine($"🔍 绑定前检查 - VideoView.ActualHeight: {videoView.ActualHeight}");
                
                // 立即绑定到VideoView，避免小窗口闪现
                videoView.MediaPlayer = _mediaPlayer;
                
                // 🔥 验证绑定是否成功
                System.Threading.Thread.Sleep(50); // 给WPF时间完成绑定
                bool bindingSuccess = videoView.MediaPlayer != null && 
                                    videoView.MediaPlayer.GetHashCode() == _mediaPlayer.GetHashCode();
                
                System.Diagnostics.Debug.WriteLine($"✅ MediaPlayer绑定{(bindingSuccess ? "成功" : "失败")}");
                // System.Diagnostics.Debug.WriteLine($"🔍 绑定后验证 - VideoView.MediaPlayer: {(videoView.MediaPlayer != null ? $"已绑定 (HashCode:{videoView.MediaPlayer.GetHashCode()})" : "null")}");
                // System.Diagnostics.Debug.WriteLine($"🔍 绑定后验证 - 是否同一实例: {bindingSuccess}");
                System.Diagnostics.Debug.WriteLine("🔵 ===== InitializeMediaPlayer 结束（成功） =====");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ MediaPlayer创建失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 堆栈: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine("🔵 ===== InitializeMediaPlayer 结束（异常） =====");
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
                System.Diagnostics.Debug.WriteLine("🔧 强制重新绑定MediaPlayer到主窗口VideoView");
                
                // 🔥 关键修复：从投影切回主窗口时，必须重新绑定MediaPlayer
                // 否则MediaPlayer可能记住投影窗口句柄，导致创建独立小窗口
                _mainVideoView.MediaPlayer = null;  // 先解除绑定
                System.Threading.Thread.Sleep(50);   // 等待解除生效
                _mainVideoView.MediaPlayer = _mediaPlayer;  // 重新绑定
                
                _mainVideoView.Visibility = System.Windows.Visibility.Visible;
                _isProjectionEnabled = false;
                
                System.Diagnostics.Debug.WriteLine($"✅ MediaPlayer已重新绑定到主窗口 (HashCode:{_mediaPlayer.GetHashCode()})");
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
                if (_mediaPlayer == null) return false;
                if (string.IsNullOrEmpty(mediaPath) || !System.IO.File.Exists(mediaPath)) return false;

                // 确保VideoView已绑定
                VideoView targetVideoView = _isProjectionEnabled ? _projectionVideoView : _mainVideoView;
                
                if (targetVideoView != null && targetVideoView.MediaPlayer == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ VideoView绑定丢失，重新绑定");
                    targetVideoView.MediaPlayer = _mediaPlayer;
                    System.Threading.Thread.Sleep(50);
                }
                else if (targetVideoView != null)
                {
                    // 验证当前绑定
                    bool currentBindingValid = targetVideoView.MediaPlayer != null && 
                                             targetVideoView.MediaPlayer.GetHashCode() == _mediaPlayer.GetHashCode();
                    
                    if (!currentBindingValid)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ 绑定异常，重新绑定");
                        targetVideoView.MediaPlayer = _mediaPlayer;
                        System.Threading.Thread.Sleep(50);
                    }
                }

                // 创建并加载媒体（不调用Stop，直接切换）
                var oldMedia = _mediaPlayer.Media;
                var media = new Media(_libVLC, new Uri(mediaPath));
                _mediaPlayer.Media = media;
                
                // 清理播放状态标志
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 加载媒体失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 堆栈: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"🟣 ===== LoadMedia 结束（异常） =====");
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
                if (_mediaPlayer == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ MediaPlayer未创建");
                    return false;
                }
                
                string fileName = System.IO.Path.GetFileName(mediaPath);
                System.Diagnostics.Debug.WriteLine($"▶ 播放: {fileName}");
                
                VideoView targetVideoView = null;
                
                if (_isProjectionEnabled)
                {
                    // System.Diagnostics.Debug.WriteLine($"🔍 投影模式 - 检查绑定");
                    
                    if (_projectionVideoView != null)
                    {
                        targetVideoView = _projectionVideoView;
                        
                        if (_projectionVideoView.MediaPlayer == null)
                        {
                            _projectionVideoView.MediaPlayer = _mediaPlayer;
                            System.Threading.Thread.Sleep(30);
                        }
                        
                        // 如果VideoView尺寸为0，强制刷新布局
                        if (_projectionVideoView.ActualWidth == 0 || _projectionVideoView.ActualHeight == 0)
                        {
                            _mainWindow.Dispatcher.Invoke(() =>
                            {
                                // _projectionVideoView.UpdateLayout();
                            }, System.Windows.Threading.DispatcherPriority.Render);
                            System.Threading.Thread.Sleep(100);
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
                            System.Diagnostics.Debug.WriteLine("⚠️ 重新绑定MediaPlayer");
                            _mainVideoView.MediaPlayer = _mediaPlayer;
                            System.Threading.Thread.Sleep(30);
                        }
                        
                        // 如果VideoView尺寸为0，强制刷新布局
                        if (_mainVideoView.ActualWidth == 0 || _mainVideoView.ActualHeight == 0)
                        {
                            System.Diagnostics.Debug.WriteLine("⚠️ VideoView尺寸为0，强制刷新布局");
                            _mainWindow.Dispatcher.Invoke(() =>
                            {
                                // _mainVideoView.UpdateLayout();
                            }, System.Windows.Threading.DispatcherPriority.Render);
                            System.Threading.Thread.Sleep(100);
                            System.Diagnostics.Debug.WriteLine($"🔴 刷新后尺寸: {_mainVideoView.ActualWidth}x{_mainVideoView.ActualHeight}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("❌ _mainVideoView为null");
                    }
                }
                
                // 检查VideoView是否可见
                if (targetVideoView != null)
                {
                    bool isVisible = targetVideoView.IsVisible;
                    
                    // 如果是投影模式但投影窗口不可见，强制切换到主VideoView
                    if (_isProjectionEnabled && !isVisible && _mainVideoView != null)
                    {
                        _projectionVideoView.MediaPlayer = null;
                        _mainVideoView.MediaPlayer = null;
                        System.Threading.Thread.Sleep(50);
                        _mainVideoView.MediaPlayer = _mediaPlayer;
                        _isProjectionEnabled = false;
                        targetVideoView = _mainVideoView;
                        System.Threading.Thread.Sleep(50);
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
                    System.Diagnostics.Debug.WriteLine("⏯ 从暂停恢复播放");
                    _mediaPlayer.SetPause(false);
                    _isPaused = false;
                }
                else
                {
                    // 播放前进行小窗检测和修复
                    DetectAndFixSmallWindow();
                    
                    // 延迟确保VideoView完全就绪
                    System.Threading.Thread.Sleep(50);
                    
                    _mediaPlayer.Play();
                }

                _isPlaying = true;
                _updateTimer.Start();
                
                PlayStateChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 播放失败: {ex.Message}");
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
                if (_mediaPlayer == null) return;
                
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
                if (_mediaPlayer == null) return;
                
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
                if (_mediaPlayer == null)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ MediaPlayer未创建，音量设置延迟到创建后");
                    return;
                }
                
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
                if (_mediaPlayer == null) return 90;
                return _mediaPlayer.Volume;
            }
            catch
            {
                return 90; // 默认音量
            }
        }

        /// <summary>
        /// 设置播放进度 (0.0-1.0)
        /// </summary>
        public void SetPosition(float position)
        {
            try
            {
                if (_mediaPlayer == null) return;
                
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
                if (_mediaPlayer == null) return 0.0f;
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
                if (_mediaPlayer == null) return 0;
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
                if (_mediaPlayer == null) return 0;
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
            if (_mediaPlayer == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ MediaPlayer未创建，无法切换到投影模式");
                return;
            }
            
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
                if (_mediaPlayer == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ MediaPlayer未创建，无法启用投影");
                    return;
                }
                
                if (_projectionVideoView == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 投影视频视图未设置");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("🔄 EnableProjection 开始执行");
                System.Diagnostics.Debug.WriteLine($"当前状态: _isPlaying={_isPlaying}, _isPaused={_isPaused}");

                // 保存当前播放状态和媒体信息
                bool wasPlaying = _isPlaying;
                bool wasPaused = _isPaused;
                float currentPosition = GetPosition();
                string currentMedia = _currentMediaPath;

                System.Diagnostics.Debug.WriteLine($"保存状态: wasPlaying={wasPlaying}, wasPaused={wasPaused}, position={currentPosition:F2}, media={System.IO.Path.GetFileName(currentMedia)}");

                // 完全停止当前播放
                if (_isPlaying || _isPaused)
                {
                    System.Diagnostics.Debug.WriteLine("⏹ 停止当前播放");
                    _mediaPlayer.Stop();
                    _isPlaying = false;
                    _isPaused = false;
                    _updateTimer.Stop();
                }

                // 解绑主窗口的 VideoView
                if (_mainVideoView != null && _mainVideoView.MediaPlayer != null)
                {
                    System.Diagnostics.Debug.WriteLine("🔧 解绑主VideoView");
                    _mainVideoView.MediaPlayer = null;
                }

                // 等待一小段时间确保解绑完成
                System.Threading.Thread.Sleep(50);

                // 重新绑定投影窗口视频视图
                System.Diagnostics.Debug.WriteLine("🔧 重新绑定投影VideoView.MediaPlayer");
                _projectionVideoView.MediaPlayer = null;  // 先解绑
                System.Threading.Thread.Sleep(50);         // 等待
                _projectionVideoView.MediaPlayer = _mediaPlayer;  // 再绑定
                
                _isProjectionEnabled = true;

                System.Diagnostics.Debug.WriteLine("✅ 投影播放已启用，VideoView已重新绑定");

                // 如果之前在播放或暂停，恢复播放
                if ((wasPlaying || wasPaused) && !string.IsNullOrEmpty(currentMedia))
                {
                    System.Diagnostics.Debug.WriteLine($"准备恢复播放: media={System.IO.Path.GetFileName(currentMedia)}");
                    
                    // 使用较长延迟确保 VideoView 完全就绪
                    _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        System.Diagnostics.Debug.WriteLine("🔄 重新加载媒体");
                        
                        // 重新加载媒体（这很关键，确保视频轨道重新初始化）
                        var media = new Media(_libVLC, new Uri(currentMedia));
                        _mediaPlayer.Media?.Dispose();  // 释放旧媒体
                        _mediaPlayer.Media = media;
                        
                        // 等待媒体加载完成
                        System.Threading.Thread.Sleep(100);
                        
                        System.Diagnostics.Debug.WriteLine("▶ 开始播放");
                        _mediaPlayer.Play();
                        _isPlaying = true;
                        _updateTimer.Start();
                        
                        // 如果是暂停状态，等待播放开始后再暂停
                        if (wasPaused)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                System.Threading.Thread.Sleep(200);  // 等待播放开始
                                System.Diagnostics.Debug.WriteLine("⏸ 恢复暂停状态");
                                _mediaPlayer.SetPause(true);
                                _isPaused = true;
                                _updateTimer.Stop();
                            }), DispatcherPriority.Background);
                        }
                        
                        // 恢复播放位置
                        if (currentPosition > 0.01f)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                System.Threading.Thread.Sleep(300);  // 等待视频轨道初始化
                                System.Diagnostics.Debug.WriteLine($"⏩ 恢复播放位置: {currentPosition:F2}");
                                SetPosition(currentPosition);
                            }), DispatcherPriority.Background);
                        }
                        
                        System.Diagnostics.Debug.WriteLine("✅ EnableProjection 恢复播放完成");
                    }), DispatcherPriority.Normal);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ 之前未在播放，无需恢复");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 启用投影播放失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"堆栈: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 禁用投影播放
        /// </summary>
        public void DisableProjection()
        {
            try
            {
                if (_mediaPlayer == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ MediaPlayer未创建，无法禁用投影");
                    return;
                }
                
                if (_mainVideoView == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 主窗口视频视图未设置");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("🔄 DisableProjection 开始执行");
                System.Diagnostics.Debug.WriteLine($"当前状态: _isPlaying={_isPlaying}, _isPaused={_isPaused}");

                // 保存当前播放状态和媒体信息
                bool wasPlaying = _isPlaying;
                bool wasPaused = _isPaused;
                float currentPosition = GetPosition();
                string currentMedia = _currentMediaPath;

                System.Diagnostics.Debug.WriteLine($"保存状态: wasPlaying={wasPlaying}, wasPaused={wasPaused}, position={currentPosition:F2}, media={System.IO.Path.GetFileName(currentMedia)}");

                // 完全停止当前播放并清空媒体
                if (_isPlaying || _isPaused)
                {
                    System.Diagnostics.Debug.WriteLine("⏹ 停止当前播放");
                    _mediaPlayer.Stop();
                    _isPlaying = false;
                    _isPaused = false;
                    _updateTimer.Stop();
                }

                // 解绑投影窗口的 VideoView
                if (_projectionVideoView != null && _projectionVideoView.MediaPlayer != null)
                {
                    System.Diagnostics.Debug.WriteLine("🔧 解绑投影VideoView");
                    _projectionVideoView.MediaPlayer = null;
                }

                // 等待一小段时间确保解绑完成
                System.Threading.Thread.Sleep(50);

                // 重新绑定主窗口视频视图
                System.Diagnostics.Debug.WriteLine("🔧 重新绑定主VideoView.MediaPlayer");
                _mainVideoView.MediaPlayer = null;  // 先解绑
                System.Threading.Thread.Sleep(50);   // 等待
                _mainVideoView.MediaPlayer = _mediaPlayer;  // 再绑定
                
                _isProjectionEnabled = false;

                System.Diagnostics.Debug.WriteLine("✅ 投影播放已禁用，VideoView已重新绑定");

                // 如果之前在播放或暂停，恢复播放
                if ((wasPlaying || wasPaused) && !string.IsNullOrEmpty(currentMedia))
                {
                    System.Diagnostics.Debug.WriteLine($"准备恢复播放: media={System.IO.Path.GetFileName(currentMedia)}");
                    
                    // 使用较长延迟确保 VideoView 完全就绪
                    _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        System.Diagnostics.Debug.WriteLine("🔄 重新加载媒体");
                        
                        // 重新加载媒体（这很关键，确保视频轨道重新初始化）
                        var media = new Media(_libVLC, new Uri(currentMedia));
                        _mediaPlayer.Media?.Dispose();  // 释放旧媒体
                        _mediaPlayer.Media = media;
                        
                        // 等待媒体加载完成
                        System.Threading.Thread.Sleep(100);
                        
                        System.Diagnostics.Debug.WriteLine("▶ 开始播放");
                        _mediaPlayer.Play();
                        _isPlaying = true;
                        _updateTimer.Start();
                        
                        // 如果是暂停状态，等待播放开始后再暂停
                        if (wasPaused)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                System.Threading.Thread.Sleep(200);  // 等待播放开始
                                System.Diagnostics.Debug.WriteLine("⏸ 恢复暂停状态");
                                _mediaPlayer.SetPause(true);
                                _isPaused = true;
                                _updateTimer.Stop();
                            }), DispatcherPriority.Background);
                        }
                        
                        // 恢复播放位置
                        if (currentPosition > 0.01f)
                        {
                            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                System.Threading.Thread.Sleep(300);  // 等待视频轨道初始化
                                System.Diagnostics.Debug.WriteLine($"⏩ 恢复播放位置: {currentPosition:F2}");
                                SetPosition(currentPosition);
                            }), DispatcherPriority.Background);
                        }
                        
                        System.Diagnostics.Debug.WriteLine("✅ DisableProjection 恢复播放完成");
                    }), DispatcherPriority.Normal);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ 之前未在播放，无需恢复");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 禁用投影播放失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"堆栈: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 重置投影模式标志（在投影窗口关闭时调用，不恢复播放）
        /// </summary>
        public void ResetProjectionMode()
        {
            try
            {
                // 解绑投影VideoView
                if (_projectionVideoView != null && _projectionVideoView.MediaPlayer != null)
                {
                    _projectionVideoView.MediaPlayer = null;
                }
                
                // 重置标志
                _isProjectionEnabled = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 重置投影模式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 检测并修复VLC小窗口问题
        /// </summary>
        private void DetectAndFixSmallWindow()
        {
            try
            {
                if (_mediaPlayer == null) return;
                
                VideoView expectedVideoView = _isProjectionEnabled ? _projectionVideoView : _mainVideoView;
                if (expectedVideoView == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ 目标VideoView为null");
                    return;
                }
                
                // 检查绑定状态
                bool isCorrectlyBound = expectedVideoView.MediaPlayer != null && 
                                       expectedVideoView.MediaPlayer.GetHashCode() == _mediaPlayer.GetHashCode();
                
                if (!isCorrectlyBound)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 检测到绑定异常，重新绑定");
                    
                    // 解绑其他VideoView
                    if (_mainVideoView != null && _mainVideoView.MediaPlayer != null && !_isProjectionEnabled)
                    {
                        _mainVideoView.MediaPlayer = null;
                    }
                    
                    if (_projectionVideoView != null && _projectionVideoView.MediaPlayer != null && _isProjectionEnabled)
                    {
                        _projectionVideoView.MediaPlayer = null;
                    }
                    
                    // 重新绑定
                    System.Threading.Thread.Sleep(30);
                    expectedVideoView.MediaPlayer = _mediaPlayer;
                    System.Threading.Thread.Sleep(50);
                    
                    System.Diagnostics.Debug.WriteLine("✅ 已重新绑定");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 小窗检测异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取当前MediaPlayer绑定状态的诊断信息
        /// </summary>
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
            System.Diagnostics.Debug.WriteLine("▶ 播放开始");
            
            // 检测是否有视频轨道
            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_mediaPlayer == null)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ MediaPlayer为null");
                        return;
                    }
                    
                    // 等待一小段时间让媒体信息加载完成
                    System.Threading.Thread.Sleep(100);
                    
                    // 🔥 在播放开始后强制设置视频缩放模式（确保生效）
                    try
                    {
                        VideoView currentView = _isProjectionEnabled ? _projectionVideoView : _mainVideoView;
                        
                        // 设置AspectRatio为VideoView容器的实际宽高比，强制视频拉伸填充
                        if (currentView != null && currentView.ActualWidth > 0 && currentView.ActualHeight > 0)
                        {
                            string containerRatio = $"{(int)currentView.ActualWidth}:{(int)currentView.ActualHeight}";
                            _mediaPlayer.AspectRatio = containerRatio;
                            _mediaPlayer.Scale = 0;  // 自适应填充
                            _mediaPlayer.CropGeometry = null;  // 不裁剪
                            System.Diagnostics.Debug.WriteLine($"✅ 视频缩放: {containerRatio}, Scale=0");
                        }
                        else
                        {
                            _mediaPlayer.AspectRatio = null;
                            _mediaPlayer.Scale = 0;
                            _mediaPlayer.CropGeometry = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ 设置视频缩放失败: {ex.Message}");
                    }
                    
                    // 检查是否有视频轨道
                    bool hasVideo = _mediaPlayer.VideoTrackCount > 0;
                    
                    // System.Diagnostics.Debug.WriteLine($"🎬 视频轨道检测: VideoTrackCount={_mediaPlayer.VideoTrackCount}, HasVideo={hasVideo}");
                    // System.Diagnostics.Debug.WriteLine($"🔍 当前模式: _isProjectionEnabled={_isProjectionEnabled}");
                    // System.Diagnostics.Debug.WriteLine($"🔍 MediaPlayer.State={_mediaPlayer.State}");
                    
                    if (hasVideo)
                    {
                        // 有视频轨道，确保VideoView已绑定
                        // System.Diagnostics.Debug.WriteLine("📹 检测到视频轨道，确认VideoView绑定状态");
                        
                        if (_isProjectionEnabled)
                        {
                            if (_projectionVideoView != null)
                            {
                                bool isBound = _projectionVideoView.MediaPlayer != null;
                                // System.Diagnostics.Debug.WriteLine($"🔍 投影VideoView绑定状态: {(isBound ? "已绑定" : "未绑定")}");
                                if (!isBound)
                                {
                                    // System.Diagnostics.Debug.WriteLine("⚠️ 警告: 有视频但投影VideoView未绑定!");
                                }
                            }
                        }
                        else
                        {
                            if (_mainVideoView != null)
                            {
                                bool isBound = _mainVideoView.MediaPlayer != null;
                                // System.Diagnostics.Debug.WriteLine($"🔍 主VideoView绑定状态: {(isBound ? "已绑定" : "未绑定")}");
                                if (!isBound)
                                {
                                    // System.Diagnostics.Debug.WriteLine("⚠️ 警告: 有视频但主VideoView未绑定!");
                                }
                            }
                        }
                    }
                    else
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
                    // System.Diagnostics.Debug.WriteLine($"❌ 视频轨道检测失败");
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

