using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop; // For RenderMode

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 性能监控工具 - 用于分析滚动性能和优化链路验证
    /// </summary>
    public class PerformanceMonitor
    {
        private static PerformanceMonitor _instance;
        public static PerformanceMonitor Instance => _instance ??= new PerformanceMonitor();

        private Stopwatch _scrollStopwatch;
        private DispatcherTimer _frameTimer;
        private int _frameCount;
        private DateTime _lastFrameTime;
        private double _totalFrameTime;
        private string _currentEasingType;
        private int _dropFrameCount;

        private PerformanceMonitor()
        {
        }

        /// <summary>
        /// 开始监控滚动性能
        /// </summary>
        public void StartScrollMonitoring(string easingType)
        {
            _currentEasingType = easingType;
            _frameCount = 0;
            _dropFrameCount = 0;
            _totalFrameTime = 0;
            _lastFrameTime = DateTime.Now;

            _scrollStopwatch = Stopwatch.StartNew();

            // 创建高频定时器监控帧率
            _frameTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(1) // 1ms检查一次
            };

            _frameTimer.Tick += (s, e) =>
            {
                var now = DateTime.Now;
                var frameTime = (now - _lastFrameTime).TotalMilliseconds;
                
                _frameCount++;
                _totalFrameTime += frameTime;
                
                // 检测掉帧 (>16.67ms = 低于60FPS)
                if (frameTime > 16.67)
                {
                    _dropFrameCount++;
                    // 注释掉详细的掉帧检测日志，只保留统计
                    // #if DEBUG
                    // System.Diagnostics.Debug.WriteLine($"⚠️ [性能监控] 掉帧检测: 帧#{_frameCount}, 耗时={frameTime:F2}ms (目标≤16.67ms)");
                    // #endif
                }

                _lastFrameTime = now;
            };

            _frameTimer.Start();

            #if DEBUG
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($"📊 [性能监控] 开始监控滚动性能");
            System.Diagnostics.Debug.WriteLine($"   缓动函数: {easingType}");
            System.Diagnostics.Debug.WriteLine($"   目标帧率: 60 FPS");
            System.Diagnostics.Debug.WriteLine("========================================");
            #endif
        }

        /// <summary>
        /// 停止监控并输出报告
        /// </summary>
        public void StopScrollMonitoring()
        {
            if (_frameTimer == null || _scrollStopwatch == null)
                return;

            _frameTimer.Stop();
            _scrollStopwatch.Stop();

            var totalSeconds = _scrollStopwatch.Elapsed.TotalSeconds;
            var avgFps = _frameCount / totalSeconds;
            var avgFrameTime = _totalFrameTime / _frameCount;
            var dropRate = (_dropFrameCount / (double)_frameCount) * 100;

            #if DEBUG
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($"📊 [性能监控] 滚动性能报告");
            System.Diagnostics.Debug.WriteLine($"   总帧数: {_frameCount}");
            System.Diagnostics.Debug.WriteLine($"   持续时间: {totalSeconds:F2}秒");
            System.Diagnostics.Debug.WriteLine($"   平均帧率: {avgFps:F1} FPS");
            System.Diagnostics.Debug.WriteLine($"   平均帧时间: {avgFrameTime:F2}ms");
            System.Diagnostics.Debug.WriteLine($"   掉帧率: {dropRate:F1}%");
            
            // 性能评级
            string rating;
            if (avgFps >= 55)
                rating = "⭐⭐⭐⭐⭐ 完美 (极致流畅)";
            else if (avgFps >= 45)
                rating = "⭐⭐⭐⭐☆ 优秀 (流畅)";
            else if (avgFps >= 35)
                rating = "⭐⭐⭐☆☆ 良好 (轻微卡顿)";
            else if (avgFps >= 25)
                rating = "⭐⭐☆☆☆ 一般 (明显卡顿)";
            else
                rating = "⭐☆☆☆☆ 较差 (明显卡顿)";
                
            System.Diagnostics.Debug.WriteLine($"   性能评级: {rating}");
            System.Diagnostics.Debug.WriteLine("========================================");
            #endif

            _frameTimer = null;
            _scrollStopwatch = null;
        }

    }
}
