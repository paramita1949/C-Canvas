using System;
using System.Collections.Generic;
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
    /// 视频播放管理器（组合根）
    /// 实际行为按职责拆分在:
    /// - VideoPlayerManager.PlaybackCore.cs
    /// - VideoPlayerManager.PlaylistPolicy.cs
    /// - VideoPlayerManager.ProjectionBridge.cs
    /// </summary>
    public partial class VideoPlayerManager : IDisposable
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

        /// <summary>
        /// 播放器错误事件（由 UI 决定提示方式）
        /// </summary>
        public event EventHandler<string> PlaybackError;

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

        /// <summary>
        /// 获取 LibVLC 实例（供自定义渲染器使用）
        /// </summary>
        public LibVLC GetLibVLC() => _libVLC;

        #endregion

        #region 构造函数

        public VideoPlayerManager(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _playlist = new List<string>();
            _currentIndex = -1;
            _playMode = PlayMode.Random;
            _random = new Random();

            InitializeLibVLC();
            InitializeUpdateTimer();
        }

        #endregion

        private void DrainUi(DispatcherPriority priority = DispatcherPriority.Background)
        {
            try
            {
                _mainWindow?.Dispatcher?.Invoke(() => { }, priority);
            }
            catch
            {
            }
        }

        private void RaisePlaybackError(string message)
        {
            try
            {
                PlaybackError?.Invoke(this, message);
            }
            catch
            {
            }
        }
    }
}
