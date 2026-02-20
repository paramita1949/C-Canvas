using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 实时帧率监控器 - 基于帧时间戳的精确FPS测量
    /// FPS = 每秒帧数 = 1秒内的帧数
    /// </summary>
    public class RealTimeFpsMonitor : IDisposable
    {
        private readonly Window _mainWindow;
        
        // 主窗口帧时间戳队列（保留最近1秒的数据）
        private readonly Queue<long> _mainFrameTimestamps = new Queue<long>();
        private double _mainCurrentFps;
        
        // 投影窗口同步时间戳队列（保留最近1秒的数据）
        private readonly Queue<long> _projectionSyncTimestamps = new Queue<long>();
        private double _projectionCurrentFps;
        
        // 高精度计时器
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        
        // 标题更新定时器
        private DispatcherTimer _titleUpdateTimer;
        
        // 是否正在监控
        private bool _isMonitoring;
        public bool IsMonitoring => _isMonitoring;
        
        // 原始标题
        private string _originalTitle;
        
        // 时间窗口（毫秒）- 保留最近1秒的帧数据
        private const long TimeWindowMs = 1000;

        public RealTimeFpsMonitor(Window mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _originalTitle = _mainWindow.Title;
            
            InitializeTitleUpdateTimer();
        }

        /// <summary>
        /// 开始监控
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;
            
            // 清空时间戳队列
            _mainFrameTimestamps.Clear();
            _projectionSyncTimestamps.Clear();
            _mainCurrentFps = 0;
            _projectionCurrentFps = 0;
            
            // 重置计时器
            _stopwatch.Restart();
            
            // 启动标题更新定时器
            _titleUpdateTimer.Start();
            
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"✅ [FPS监控] 监控已启动");
            //#endif
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isMonitoring)
                return;

            _isMonitoring = false;
            
            // 停止标题更新
            _titleUpdateTimer.Stop();
            
            // 恢复原始标题
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.Title = _originalTitle;
            });
            
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"⏹️ [FPS监控] 监控已停止");
            //#endif
        }

        /// <summary>
        /// 初始化标题更新定时器
        /// </summary>
        private void InitializeTitleUpdateTimer()
        {
            _titleUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200) // 每200ms更新一次标题
            };
            
            _titleUpdateTimer.Tick += (s, e) => UpdateWindowTitle();
        }

        /// <summary>
        /// 记录主窗口动画渲染帧（由AnimationHelper调用）
        /// </summary>
        public void RecordMainFrame()
        {
            if (!_isMonitoring)
                return;
            
            long currentTime = _stopwatch.ElapsedMilliseconds;
            
            // 添加当前帧时间戳
            _mainFrameTimestamps.Enqueue(currentTime);
            
            // 移除1秒之前的旧时间戳
            while (_mainFrameTimestamps.Count > 0 && 
                   currentTime - _mainFrameTimestamps.Peek() > TimeWindowMs)
            {
                _mainFrameTimestamps.Dequeue();
            }
            
            // FPS = 最近1秒内的帧数
            _mainCurrentFps = _mainFrameTimestamps.Count;
            
            //#if DEBUG
            //// 每10帧输出一次调试信息
            //if (_mainFrameTimestamps.Count % 10 == 0)
            //{
            //    System.Diagnostics.Debug.WriteLine($"🎬 [主屏渲染] 最近1秒帧数: {_mainFrameTimestamps.Count}, FPS: {_mainCurrentFps:F1}");
            //}
            //#endif
        }

        /// <summary>
        /// 记录投影窗口同步（由ProjectionManager调用）
        /// </summary>
        public void RecordProjectionSync()
        {
            if (!_isMonitoring)
                return;
            
            long currentTime = _stopwatch.ElapsedMilliseconds;
            
            // 添加当前同步时间戳
            _projectionSyncTimestamps.Enqueue(currentTime);
            
            // 移除1秒之前的旧时间戳
            while (_projectionSyncTimestamps.Count > 0 && 
                   currentTime - _projectionSyncTimestamps.Peek() > TimeWindowMs)
            {
                _projectionSyncTimestamps.Dequeue();
            }
            
            // FPS = 最近1秒内的同步次数
            _projectionCurrentFps = _projectionSyncTimestamps.Count;
            
            //#if DEBUG
            //// 只在启动后的前几次，或每秒输出一次（降低日志频率）
            //if (_projectionSyncTimestamps.Count <= 5 || _projectionSyncTimestamps.Count % 60 == 0)
            //{
            //    double avgInterval = 0;
            //    if (_projectionSyncTimestamps.Count > 1)
            //    {
            //        var timestamps = _projectionSyncTimestamps.ToArray();
            //        double totalInterval = timestamps[timestamps.Length - 1] - timestamps[0];
            //        avgInterval = totalInterval / (_projectionSyncTimestamps.Count - 1);
            //    }
            //    
            //    System.Diagnostics.Debug.WriteLine($"📺 [投影同步] 最近1秒次数: {_projectionSyncTimestamps.Count}, FPS: {_projectionCurrentFps:F1}, 平均间隔: {avgInterval:F2}ms");
            //}
            //#endif
        }

        /// <summary>
        /// 更新窗口标题
        /// </summary>
        private void UpdateWindowTitle()
        {
            if (!_isMonitoring)
                return;

            string title;
            
            // 显示主屏幕和投影屏的FPS
            title = $"{_originalTitle} | 主屏FPS: {_mainCurrentFps:F1}  投影FPS: {_projectionCurrentFps:F1}";

            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.Title = title;
            });
        }

        /// <summary>
        /// 获取主窗口当前FPS
        /// </summary>
        public double GetMainFps() => _mainCurrentFps;

        /// <summary>
        /// 获取投影窗口当前FPS
        /// </summary>
        public double GetProjectionFps() => _projectionCurrentFps;

        public void Dispose()
        {
            StopMonitoring();
            _titleUpdateTimer?.Stop();
        }
    }
}

