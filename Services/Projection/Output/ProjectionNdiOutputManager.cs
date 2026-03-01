using SkiaSharp;

namespace ImageColorChanger.Services.Projection.Output
{
    public sealed class ProjectionNdiOutputManager
    {
        private readonly IProjectionNdiConfigProvider _configProvider;
        private readonly IProjectionNdiModeResolver _modeResolver;
        private readonly IProjectionNdiSender _sender;
        private long _lastStartFailLogTick;

        public ProjectionNdiOutputManager(
            IProjectionNdiConfigProvider configProvider,
            IProjectionNdiModeResolver modeResolver,
            IProjectionNdiSender sender)
        {
            _configProvider = configProvider;
            _modeResolver = modeResolver;
            _sender = sender;
        }

        public bool PublishFrame(SKBitmap frame, ProjectionNdiContentType contentType, SKColor? transparencyKeyColor = null)
        {
            if (frame == null)
            {
                return false;
            }

            var mode = _modeResolver.Resolve(contentType);
            if (mode == ProjectionNdiTransmissionMode.Disabled)
            {
                return false;
            }

            if (!_sender.IsRunning && !_sender.Start(CreateOptions()))
            {
                ThrottledLog(ref _lastStartFailLogTick, "PublishFrame failed: sender start returned false.");
                return false;
            }

            SKBitmap frameToSend = frame;
            SKBitmap transformed = null;
            try
            {
                if (mode == ProjectionNdiTransmissionMode.Transparent && transparencyKeyColor.HasValue)
                {
                    transformed = BuildColorKeyTransparentFrame(frame, transparencyKeyColor.Value, tolerance: 8);
                    frameToSend = transformed ?? frame;
                }

                return _sender.SendFrame(frameToSend);
            }
            finally
            {
                transformed?.Dispose();
            }
        }

        public void Stop()
        {
            _sender.Stop();
        }

        public int GetClientConnectionCount()
        {
            if (_sender is NativeProjectionNdiSender nativeSender)
            {
                return nativeSender.GetConnectionCount();
            }

            return 0;
        }

        public void ClearToBlackAndStop()
        {
            try
            {
                if (_sender.IsRunning)
                {
                    int width = System.Math.Max(16, _configProvider.ProjectionNdiWidth);
                    int height = System.Math.Max(16, _configProvider.ProjectionNdiHeight);
                    using var black = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    black.Erase(SKColors.Black);
                    _sender.SendFrame(black);
                }
            }
            catch
            {
                // 清屏失败不阻塞停止流程
            }
            finally
            {
                _sender.Stop();
            }
        }

        public void PushTransparentIdleFrame()
        {
            try
            {
                if (!_sender.IsRunning && !_sender.Start(CreateOptions()))
                {
                    return;
                }

                int width = System.Math.Max(16, _configProvider.ProjectionNdiWidth);
                int height = System.Math.Max(16, _configProvider.ProjectionNdiHeight);
                using var transparent = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                transparent.Erase(new SKColor(0, 0, 0, 0));
                _sender.SendFrame(transparent);
            }
            catch
            {
                // 空闲透明帧发送失败不影响主流程
            }
        }

        private ProjectionNdiOutputOptions CreateOptions()
        {
            return new ProjectionNdiOutputOptions
            {
                SenderName = _configProvider.ProjectionNdiSenderName,
                Width = _configProvider.ProjectionNdiWidth,
                Height = _configProvider.ProjectionNdiHeight,
                Fps = _configProvider.ProjectionNdiFps,
                PreferAlpha = _configProvider.ProjectionNdiPreferAlpha
            };
        }

        private static SKBitmap BuildColorKeyTransparentFrame(SKBitmap source, SKColor keyColor, int tolerance)
        {
            var output = new SKBitmap(source.Info);

            int keyR = keyColor.Red;
            int keyG = keyColor.Green;
            int keyB = keyColor.Blue;
            int pixelCount = output.Width * output.Height;

            unsafe
            {
                uint* src = (uint*)source.GetPixels();
                uint* dst = (uint*)output.GetPixels();
                for (int i = 0; i < pixelCount; i++)
                {
                    uint bgra = src[i];
                    int b = (byte)(bgra & 0xFF);
                    int g = (byte)((bgra >> 8) & 0xFF);
                    int r = (byte)((bgra >> 16) & 0xFF);

                    if (System.Math.Abs(r - keyR) <= tolerance
                        && System.Math.Abs(g - keyG) <= tolerance
                        && System.Math.Abs(b - keyB) <= tolerance)
                    {
                        dst[i] = bgra & 0x00FFFFFFu;
                    }
                    else
                    {
                        dst[i] = bgra;
                    }
                }
            }

            return output;
        }

        private static void ThrottledLog(ref long lastTick, string message, int intervalMs = 5000)
        {
            long now = Environment.TickCount64;
            if (now - lastTick < intervalMs)
            {
                return;
            }

            lastTick = now;
            ProjectionNdiDiagnostics.Log(message);
        }
    }
}
