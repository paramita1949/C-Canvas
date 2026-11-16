using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>帧同步短滚动。只在活动动画期间订阅 Rendering，卸载/隐藏时停止。</summary>
    public sealed class BibleVerseSpringScroller : IDisposable
    {
        private readonly ScrollViewer _viewer;
        private readonly BibleVerseSpringMotion _motion;
        private const double MaximumFrameSeconds = 1.0 / 30.0;
        private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
        private Action _completed;
        private bool _disposed;

        public BibleVerseSpringScroller(ScrollViewer viewer)
        {
            _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
            _motion = new BibleVerseSpringMotion(viewer.VerticalOffset);
            _viewer.Unloaded += OnUnloaded;
            _viewer.IsVisibleChanged += OnVisibleChanged;
        }

        public bool IsRunning { get; private set; }

        public void SetSpeedLevel(int level)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BibleVerseSpringScroller));
            _motion.SetSpeedLevel(level);
        }

        public void ScrollTo(double target, Action completed = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BibleVerseSpringScroller));
            if (!double.IsFinite(target)) { Stop(); return; }
            if (!_viewer.IsLoaded || !_viewer.IsVisible) { Stop(); return; }
            target = Math.Clamp(target, 0, GetMaximum());
            if (!IsRunning)
            {
                _motion.Reset(_viewer.VerticalOffset);
                _lastRenderingTime = TimeSpan.MinValue;
            }
            // 连续输入只改目标。位置仅在渲染帧推进，不把UI等待时间算成已播放动画。
            _motion.Retarget(target);
            _completed = completed;
            if (_motion.IsAtRest) { Complete(); return; }
            if (!IsRunning)
            {
                IsRunning = true;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        public void Stop()
        {
            if (IsRunning) CompositionTarget.Rendering -= OnRendering;
            IsRunning = false;
            _completed = null;
            _lastRenderingTime = TimeSpan.MinValue;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            if (!IsRunning) return;
            if (_disposed || !_viewer.IsVisible || !_viewer.IsLoaded) { Stop(); return; }
            if (!(e is RenderingEventArgs rendering)) return;
            if (rendering.RenderingTime == _lastRenderingTime) return;
            double seconds = _lastRenderingTime == TimeSpan.MinValue ? 0 :
                Math.Clamp((rendering.RenderingTime - _lastRenderingTime).TotalSeconds, 0, MaximumFrameSeconds);
            _lastRenderingTime = rendering.RenderingTime;
            // 首帧从当前位置开始；卡顿后不在一帧内追赶几百毫秒而跳到终点。
            AdvanceFrame(seconds);
            if (_motion.IsAtRest) Complete();
        }

        private void AdvanceFrame(double seconds)
        {
            // 内容/视口变化后重限目标，不重置当前速度；实际位置也不得越过滚动边界。
            double maximum = GetMaximum();
            double target = Math.Clamp(_motion.Target, 0, maximum);
            _motion.Retarget(target);
            double position = _motion.Advance(seconds);
            double clamped = Math.Clamp(position, 0, maximum);
            if (clamped != position)
            {
                _motion.Reset(clamped);
                _motion.Retarget(target);
            }
            _viewer.ScrollToVerticalOffset(clamped);
        }

        private void Complete()
        {
            double target = Math.Clamp(_motion.Target, 0, GetMaximum());
            var completed = _completed;
            Stop();
            _motion.Reset(target);
            _viewer.ScrollToVerticalOffset(target);
            completed?.Invoke();
        }

        private double GetMaximum()
        {
            double maximum = _viewer.ScrollableHeight;
            return double.IsFinite(maximum) ? Math.Max(0, maximum) : 0;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => Stop();
        private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) Stop();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _viewer.Unloaded -= OnUnloaded;
            _viewer.IsVisibleChanged -= OnVisibleChanged;
            _disposed = true;
        }
    }
}
