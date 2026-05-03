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
            SKBitmap watermarked = null;
            try
            {
                if (mode == ProjectionNdiTransmissionMode.Transparent && transparencyKeyColor.HasValue)
                {
                    transformed = BuildColorKeyTransparentFrame(frame, transparencyKeyColor.Value, tolerance: 8);
                    frameToSend = transformed ?? frame;
                }

                watermarked = BuildWatermarkedFrameIfNeeded(frameToSend);
                frameToSend = watermarked ?? frameToSend;

                return _sender.SendFrame(frameToSend);
            }
            finally
            {
                transformed?.Dispose();
                watermarked?.Dispose();
            }
        }

        public bool PublishFrameDirect(SKBitmap frame, bool transparent = false, SKColor? transparencyKeyColor = null)
        {
            if (frame == null)
            {
                return false;
            }

            if (!_sender.IsRunning && !_sender.Start(CreateOptions()))
            {
                ThrottledLog(ref _lastStartFailLogTick, "PublishFrameDirect failed: sender start returned false.");
                return false;
            }

            SKBitmap frameToSend = frame;
            SKBitmap transformed = null;
            SKBitmap watermarked = null;
            try
            {
                if (transparent && transparencyKeyColor.HasValue)
                {
                    transformed = BuildColorKeyTransparentFrame(frame, transparencyKeyColor.Value, tolerance: 8);
                    frameToSend = transformed ?? frame;
                }

                watermarked = BuildWatermarkedFrameIfNeeded(frameToSend);
                frameToSend = watermarked ?? frameToSend;

                return _sender.SendFrame(frameToSend);
            }
            finally
            {
                transformed?.Dispose();
                watermarked?.Dispose();
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

        public void PushTransparentIdleFrame(bool startSenderIfNeeded = true)
        {
            try
            {
                if (!_sender.IsRunning)
                {
                    if (!startSenderIfNeeded || !_sender.Start(CreateOptions()))
                    {
                        return;
                    }
                }

                int width = System.Math.Max(16, _configProvider.ProjectionNdiWidth);
                int height = System.Math.Max(16, _configProvider.ProjectionNdiHeight);
                using var transparent = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                transparent.Erase(new SKColor(0, 0, 0, 0));
                DrawWatermark(
                    transparent,
                    _configProvider.ProjectionNdiIdleFrameWatermarkText,
                    _configProvider.ProjectionNdiIdleFrameWatermarkPosition,
                    _configProvider.ProjectionNdiIdleFrameWatermarkFontSize,
                    _configProvider.ProjectionNdiIdleFrameWatermarkFontFamily,
                    _configProvider.ProjectionNdiIdleFrameWatermarkOpacity);
                _sender.SendFrame(transparent);
            }
            catch
            {
                // 空闲透明帧发送失败不影响主流程
            }
        }

        private SKBitmap BuildWatermarkedFrameIfNeeded(SKBitmap source)
        {
            if (source == null || string.IsNullOrWhiteSpace(_configProvider.ProjectionNdiIdleFrameWatermarkText))
            {
                return null;
            }

            var output = new SKBitmap(source.Info);
            using var canvas = new SKCanvas(output);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0);
            DrawWatermark(
                output,
                _configProvider.ProjectionNdiIdleFrameWatermarkText,
                _configProvider.ProjectionNdiIdleFrameWatermarkPosition,
                _configProvider.ProjectionNdiIdleFrameWatermarkFontSize,
                _configProvider.ProjectionNdiIdleFrameWatermarkFontFamily,
                _configProvider.ProjectionNdiIdleFrameWatermarkOpacity);
            return output;
        }

        private static void DrawWatermark(
            SKBitmap target,
            string watermark,
            string position,
            double fontSize,
            string fontFamily,
            double opacityPercent)
        {
            if (target == null || string.IsNullOrWhiteSpace(watermark))
            {
                return;
            }

            string text = watermark.Trim();
            if (text.Length == 0)
            {
                return;
            }

            using var canvas = new SKCanvas(target);
            float resolvedFontSize = (float)System.Math.Clamp(fontSize, 10d, 220d);
            byte alpha = (byte)System.Math.Round(System.Math.Clamp(opacityPercent, 0d, 100d) * 255d / 100d);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(255, 255, 255, alpha)
            };
            using var typeface = ResolveTypeface(fontFamily);
            using var font = new SKFont(typeface, resolvedFontSize);

            float margin = System.Math.Max(12f, resolvedFontSize * 0.6f);
            float textWidth = font.MeasureText(text, paint);
            float ascent = -font.Metrics.Ascent;
            float descent = font.Metrics.Descent;
            float textHeight = System.Math.Max(1f, ascent + descent);

            float x = position switch
            {
                "LeftTop" => margin,
                "RightTop" => System.Math.Max(margin, target.Width - margin - textWidth),
                "LeftBottom" => margin,
                "Center" => System.Math.Max(margin, (target.Width - textWidth) / 2f),
                _ => System.Math.Max(margin, target.Width - margin - textWidth)
            };

            float y = position switch
            {
                "LeftTop" => margin + ascent,
                "RightTop" => margin + ascent,
                "Center" => System.Math.Max(margin + ascent, (target.Height + textHeight) / 2f - descent),
                _ => System.Math.Max(margin + ascent, target.Height - margin - descent)
            };
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        }

        private static SKTypeface ResolveTypeface(string fontFamily)
        {
            string name = (fontFamily ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return SKTypeface.Default;
            }

            var typeface = SKTypeface.FromFamilyName(name);
            return typeface ?? SKTypeface.Default;
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
