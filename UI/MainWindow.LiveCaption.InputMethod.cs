using System;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private bool _inputMethodSessionActive;
        private int _liveCaptionStateRevision;
        private readonly SemaphoreSlim _liveCaptionRecognitionGate = new(1, 1);

        private bool IsLiveCaptionInputMethodActive() =>
            _inputMethodSessionActive && _configManager?.LiveCaptionInputMethodEnabled == true;

        private bool IsLiveCaptionDisplayActive() =>
            IsLiveCaptionInputMethodActive() || IsLiveCaptionRealtimeConnected();

        private async void OnInputMethodToggleRequested(bool enabled)
        {
            if (enabled && !TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
            {
                _liveCaptionOverlayWindow?.SetInputMethodMode(false);
                return;
            }

            _configManager.LiveCaptionInputMethodEnabled = enabled;
            await ApplyRecognitionStateAsync();
        }

        private async Task ApplyRecognitionStateAsync()
        {
            int revision = ++_liveCaptionStateRevision;
            await _liveCaptionRecognitionGate.WaitAsync();
            try
            {
                if (revision != _liveCaptionStateRevision || _isDisposingLiveCaption) return;
                if (_configManager.LiveCaptionInputMethodEnabled)
                {
                    if (!TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
                    {
                        _configManager.LiveCaptionInputMethodEnabled = false;
                        StopLiveCaption();
                        _liveCaptionOverlayWindow?.SetInputMethodMode(false);
                        return;
                    }

                    // 先停止本程序采音，输入法自行管理麦克风和转写服务。
                    _liveCaptionEngine?.Stop();
                    _sharedAudioCaptureSession?.Stop();
                    UpdateLiveCaptionIdleShutdownWatcherState(false, false);
                    _bibleShortPhraseRuntime?.Abort();
                    if (revision != _liveCaptionStateRevision || !_configManager.LiveCaptionInputMethodEnabled) return;

                    EnsureLiveCaptionOverlayWindow();
                    if (!_inputMethodSessionActive)
                    {
                        ResetLiveCaptionInputOutput();
                        ResetAiCaptionInput();
                    }
                    _inputMethodSessionActive = true;
                    _liveCaptionOverlayWindow.SetRecognitionToggleStates(false, false);
                    _liveCaptionOverlayWindow.SetInputMethodMode(true);
                    OnInputMethodTextChanged(_liveCaptionOverlayWindow.InputMethodText);
                    RegisterLiveCaptionF4HotKey();
                    SyncAiCaptionUiState();
                    InputMethodCaptionDiagnostics.Log($"session=active revision={revision} capture=false");
                    ShowStatus("输入法字幕已开启，可直接使用输入法语音，文字会自动显示");
                    return;
                }

                if (_inputMethodSessionActive)
                {
                    _inputMethodSessionActive = false;
                    ResetAiCaptionInput();
                    ResetLiveCaptionInputOutput();
                    InputMethodCaptionDiagnostics.Log($"session=inactive revision={revision}");
                }
                _liveCaptionOverlayWindow?.SetInputMethodMode(false);
                ApplyRecognitionStateCore();
            }
            catch (Exception ex)
            {
                InputMethodCaptionDiagnostics.Log($"transition=failed type={ex.GetType().Name}");
                ShowStatus($"字幕模式切换失败：{ex.Message}");
            }
            finally
            {
                _liveCaptionRecognitionGate.Release();
            }
        }

        private void ResetLiveCaptionInputOutput()
        {
            _liveCaptionComposer.Reset();
            _liveCaptionNdiComposer.Reset();
            _liveCaptionOverlayWindow?.UpdateCaption(string.Empty, 0);
            _projectionManager?.HideProjectionCaptionOverlay();
            _ndiRouter?.PushCaptionIdleFrame();
        }

        private void OnInputMethodTextChanged(string text)
        {
            if (!IsLiveCaptionInputMethodActive() || _liveCaptionOverlayWindow == null) return;
            ForwardInputMethodTextToAi(text);
            // 渲染保留完整快照；AI接收由当前会话控制，不触发语音识别。
            RecalculateLiveCaptionComposerLayout();
            // 主字幕帧同时用于投影；仅在本机放不下时缩短，不能超过投影原有30字行上限。
            _liveCaptionComposer.SetLineCharLimit(Math.Min(LiveCaptionMainLineCharLimit,
                _liveCaptionOverlayWindow.GetInputMethodLineCharLimit()));
            LiveCaptionRenderFrame frame = _liveCaptionComposer.PushInputSnapshot(text);
            _liveCaptionNdiComposer.SetLineCharLimit(Math.Clamp(_configManager.LiveCaptionNdiLineCharLimit, 8, 80));
            LiveCaptionRenderFrame ndiFrame = _liveCaptionNdiComposer.PushInputSnapshot(text);
            if (ndiFrame.HasChanged) UpdateLiveCaptionNdiCaption(ndiFrame.Display, ndiFrame.HighlightStart);
            if (frame.HasChanged)
            {
                _liveCaptionOverlayWindow.UpdateCaption(frame.Display, frame.HighlightStart);
                UpdateLiveCaptionProjectionCaption(frame.Display, frame.HighlightStart);
            }
            InputMethodCaptionDiagnostics.Log($"render chars={text?.Length ?? 0} display={frame.Display.Length} changed={frame.HasChanged} ndiChanged={ndiFrame.HasChanged}");
        }
    }
}
