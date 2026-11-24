using System;
using System.Windows.Threading;
using ImageColorChanger.Services.LiveCaption;
using ImageColorChanger.Services.LiveCaption.Rendering;
using ImageColorChanger.Services.Ndi;
using SkiaSharp;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private LiveCaptionNdiOutput _liveCaptionNdiOutput;
        private long _captionNdiLastFailureTick;
        private bool _captionNdiHadFailure;

        private LiveCaptionNdiOutput GetLiveCaptionNdiOutput()
        {
            if (_liveCaptionNdiOutput == null && _ndiRouter != null && !_isDisposingLiveCaption)
            {
                _liveCaptionNdiOutput = new LiveCaptionNdiOutput(_ndiRouter);
                _liveCaptionNdiOutput.PublishCompleted += OnCaptionNdiPublishCompleted;
            }
            return _liveCaptionNdiOutput;
        }

        private void SetLiveCaptionNdiEnabled(bool enabled)
        {
            if (_configManager == null) return;
            if (enabled && (!TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption) ||
                !TryRequirePremiumFeature(Services.Licensing.PremiumFeature.Ndi))) enabled = false;

            LiveCaptionNdiOutput output = GetLiveCaptionNdiOutput();
            if (output == null)
            {
                _configManager.LiveCaptionNdiEnabled = false;
                UpdateLiveCaptionNdiActionState();
                ShowStatus("字幕NDI服务未初始化");
                return;
            }
            output.SetChannelEnabled(enabled);
            UpdateLiveCaptionNdiActionState();
            if (enabled)
            {
                // 明确开启时发送当前字幕或透明首帧；普通清空只清理已有输出。
                PublishLiveCaptionToNdi(_liveCaptionNdiComposer.CurrentDisplay,
                    _liveCaptionNdiComposer.CurrentHighlightStart, allowEmptyStart: true);
                ShowStatus("字幕NDI已开启，等待发送与连接");
            }
            else ShowStatus("字幕NDI已关闭");
            LiveCaptionDebugLogger.Log($"NdiToggle: enabled={enabled}");
        }

        private void PublishLiveCaptionToNdi(string captionText, int highlightStart, bool allowEmptyStart = false)
        {
            if (_ndiRouter == null || _configManager?.LiveCaptionNdiEnabled != true) return;
            if (!IsPremiumFeatureAllowed(Services.Licensing.PremiumFeature.LiveCaption) ||
                !IsPremiumFeatureAllowed(Services.Licensing.PremiumFeature.Ndi))
            {
                ClearLiveCaptionNdiOutput(stopSender: true);
                return;
            }
            if (!allowEmptyStart && string.IsNullOrWhiteSpace(captionText))
            {
                ClearLiveCaptionNdiOutput();
                return;
            }
            var frame = new LiveCaptionRenderFrame(captionText, highlightStart, hasChanged: true);
            GetLiveCaptionNdiOutput()?.Submit(frame, CreateLiveCaptionNdiRenderOptions());
        }

        private LiveCaptionNdiRenderOptions CreateLiveCaptionNdiRenderOptions()
        {
            string fontFamily = _configManager.LiveCaptionNdiFontFamily;
            if (string.IsNullOrWhiteSpace(fontFamily))
                fontFamily = string.IsNullOrWhiteSpace(_configManager.BibleFontFamily)
                    ? "Microsoft YaHei UI" : _configManager.BibleFontFamily;
            float fontSize = (float)(_configManager.LiveCaptionNdiFontSize > 0 ? _configManager.LiveCaptionNdiFontSize : 52);
            float padding = (float)(_configManager.LiveCaptionNdiPadding > 0 ? _configManager.LiveCaptionNdiPadding : 56);
            return new LiveCaptionNdiRenderOptions(
                Math.Max(320, _configManager.ProjectionNdiWidth), Math.Max(180, _configManager.ProjectionNdiHeight),
                fontFamily, fontSize, padding, (float)_configManager.LiveCaptionNdiLetterSpacing,
                (float)_configManager.LiveCaptionNdiLineGap,
                ParseHexColor(_configManager.LiveCaptionNdiTextColor,
                    ParseHexColor(_configManager.LiveCaptionTextColor, SKColors.White)),
                ParseHexColor(_configManager.LiveCaptionNdiLatestTextColor,
                    ParseHexColor(_configManager.LiveCaptionLatestTextColor, new SKColor(255, 255, 0))),
                _configManager.LiveCaptionNdiTextAlignment);
        }

        private void ClearLiveCaptionNdiOutput(bool stopSender = false)
        {
            if (_liveCaptionNdiOutput != null)
            {
                if (stopSender) _liveCaptionNdiOutput.Stop();
                else _liveCaptionNdiOutput.Clear();
            }
            else if (stopSender) _ndiRouter?.StopCaptionOutput();
            else _ndiRouter?.PushCaptionIdleFrame();
        }

        private void OnCaptionNdiPublishCompleted(bool sent, Exception failure)
        {
            if (_isDisposingLiveCaption || _configManager?.LiveCaptionNdiEnabled != true) return;
            if (sent)
            {
                if (!_captionNdiHadFailure) return;
                _captionNdiHadFailure = false;
                InputMethodCaptionDiagnostics.Log("ndi publish=recovered");
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (!_isDisposingLiveCaption && IsLiveCaptionNdiEnabled()) ShowStatus("字幕NDI已恢复发送");
                }));
                return;
            }
            _captionNdiHadFailure = true;
            long now = Environment.TickCount64;
            if (_captionNdiLastFailureTick != 0 && now - _captionNdiLastFailureTick < 5000) return;
            _captionNdiLastFailureTick = now;
            InputMethodCaptionDiagnostics.Log($"ndi publish=failed type={failure?.GetType().Name ?? "TransportRejected"}");
            LiveCaptionDebugLogger.Log($"CaptionNdi: publish failed, error={failure?.Message ?? "transport returned false"}");
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!_isDisposingLiveCaption && IsLiveCaptionNdiEnabled())
                    ShowStatus(failure == null ? "字幕NDI发送失败，请检查 NDI Runtime 和输出设置"
                        : $"字幕NDI绘制或发送失败：{failure.Message}");
            }));
        }

        private static SKColor ParseHexColor(string hex, SKColor fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            string value = hex.Trim().TrimStart('#');
            try
            {
                if (value.Length == 6)
                    return new SKColor(Convert.ToByte(value.Substring(0, 2), 16),
                        Convert.ToByte(value.Substring(2, 2), 16), Convert.ToByte(value.Substring(4, 2), 16));
                if (value.Length == 8)
                    return new SKColor(Convert.ToByte(value.Substring(2, 2), 16),
                        Convert.ToByte(value.Substring(4, 2), 16), Convert.ToByte(value.Substring(6, 2), 16),
                        Convert.ToByte(value.Substring(0, 2), 16));
            }
            catch (FormatException) { }
            catch (OverflowException) { }
            return fallback;
        }
    }
}
