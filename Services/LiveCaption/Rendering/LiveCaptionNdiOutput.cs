using System;
using System.Threading.Tasks;
using ImageColorChanger.Services.Ndi;
using SkiaSharp;

namespace ImageColorChanger.Services.LiveCaption.Rendering
{
    /// <summary>单工作循环、单待处理槽。控制动作与发布串行，绘制不占 UI 线程。</summary>
    internal sealed class LiveCaptionNdiOutput : IDisposable
    {
        private sealed record Request(LiveCaptionRenderFrame Frame, LiveCaptionNdiRenderOptions Options, long Epoch);

        private readonly object _sync = new();
        private readonly INdiRouter _router;
        private readonly Func<LiveCaptionRenderFrame, LiveCaptionNdiRenderOptions, SKBitmap> _render;
        private Request _pending;
        private bool _processing;
        private bool _disposed;
        private long _epoch;

        public LiveCaptionNdiOutput(INdiRouter router,
            Func<LiveCaptionRenderFrame, LiveCaptionNdiRenderOptions, SKBitmap> render = null)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _render = render ?? new LiveCaptionNdiRenderer().Render;
        }

        public event Action<bool, Exception> PublishCompleted;

        public bool Submit(LiveCaptionRenderFrame frame, LiveCaptionNdiRenderOptions options)
        {
            lock (_sync)
            {
                if (_disposed || !_router.IsChannelEnabled(NdiChannel.Caption)) return false;
                _pending = new Request(frame, options, _epoch);
                if (!_processing)
                {
                    _processing = true;
                    _ = Task.Run(ProcessPending);
                }
                return true;
            }
        }

        public void SetChannelEnabled(bool enabled)
        {
            lock (_sync)
            {
                if (_disposed) return;
                Invalidate();
                _router.SetChannelEnabled(NdiChannel.Caption, enabled);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                if (_disposed) return;
                Invalidate();
                _router.PushCaptionIdleFrame();
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_disposed) return;
                Invalidate();
                _router.StopCaptionOutput();
            }
        }

        private void Invalidate()
        {
            _epoch++;
            _pending = null;
        }

        private void ProcessPending()
        {
            while (true)
            {
                Request request;
                lock (_sync)
                {
                    request = _pending;
                    _pending = null;
                    if (_disposed || request == null)
                    {
                        _processing = false;
                        return;
                    }
                }

                bool report = false, sent = false;
                Exception failure = null;
                try
                {
                    using SKBitmap bitmap = _render(request.Frame, request.Options);
                    lock (_sync)
                    {
                        if (!_disposed && request.Epoch == _epoch && _router.IsChannelEnabled(NdiChannel.Caption))
                        {
                            report = true;
                            sent = _router.PublishCaptionFrame(bitmap);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                    lock (_sync) report = !_disposed && request.Epoch == _epoch;
                }
                if (report)
                {
                    // 观察回调不能终止工作循环，也不能在发布锁里调用 UI。
                    try { PublishCompleted?.Invoke(sent, failure); }
                    catch { }
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                Invalidate();
                PublishCompleted = null;
                _router.StopCaptionOutput();
            }
        }
    }
}
