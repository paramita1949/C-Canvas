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
        private enum Control { None, Clear, Stop }

        private readonly object _sync = new();
        private readonly INdiRouter _router;
        private readonly Func<LiveCaptionRenderFrame, LiveCaptionNdiRenderOptions, SKBitmap> _render;
        private Request _pending;
        private Control _control;
        private bool _processing;
        private bool _disposed;
        private bool _accepting = true;
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
                if (_disposed || !_accepting || !_router.IsChannelEnabled(NdiChannel.Caption)) return false;
                _pending = new Request(frame, options, _epoch);
                StartProcessing();
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
                _accepting = enabled;
                _control = enabled ? Control.None : Control.Stop;
                if (!enabled) StartProcessing();
            }
        }

        public void Resume()
        {
            lock (_sync)
            {
                if (_disposed || _accepting) return;
                Invalidate();
                _accepting = true;
                _control = Control.None;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                if (_disposed) return;
                Invalidate();
                if (!_accepting) return;
                _control = Control.Clear;
                StartProcessing();
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_disposed) return;
                Invalidate();
                _accepting = false;
                _control = Control.Stop;
                StartProcessing();
            }
        }

        private void Invalidate()
        {
            _epoch++;
            _pending = null;
        }

        private void StartProcessing()
        {
            if (_processing) return;
            _processing = true;
            _ = Task.Run(ProcessPending);
        }

        private void ProcessPending()
        {
            while (true)
            {
                Request request;
                Control control;
                lock (_sync)
                {
                    control = _control;
                    _control = Control.None;
                    request = control == Control.None ? _pending : null;
                    if (request != null) _pending = null;
                    if (control == Control.None && (_disposed || request == null))
                    {
                        _processing = false;
                        return;
                    }
                }

                if (control != Control.None)
                {
                    try
                    {
                        if (control == Control.Stop) _router.StopCaptionOutput();
                        else _router.PushCaptionIdleFrame();
                    }
                    catch (Exception ex)
                    {
                        InputMethodCaptionDiagnostics.Log($"ndi control={control} failed={ex.GetType().Name}");
                    }
                    continue;
                }

                bool report = false, sent = false;
                Exception failure = null;
                try
                {
                    using SKBitmap bitmap = _render(request.Frame, request.Options);
                    lock (_sync)
                    {
                        report = !_disposed && _accepting && request.Epoch == _epoch &&
                            _router.IsChannelEnabled(NdiChannel.Caption);
                    }
                    // 所有原生控制动作由此工作循环执行，不在 UI 需要取得的锁里等待发送。
                    if (report) sent = _router.PublishCaptionFrame(bitmap);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    lock (_sync) report = !_disposed && _accepting && request.Epoch == _epoch;
                }
                lock (_sync) report &= !_disposed && _accepting && request.Epoch == _epoch;
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
                _accepting = false;
                Invalidate();
                PublishCompleted = null;
                _control = Control.Stop;
                StartProcessing();
            }
        }
    }
}
