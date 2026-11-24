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
        private int _captionDocumentRevision;
        private readonly SemaphoreSlim _liveCaptionRecognitionGate = new(1, 1);

        private bool IsLiveCaptionInputMethodActive() =>
            _inputMethodSessionActive && _configManager?.LiveCaptionInputMethodEnabled == true;

        private bool IsLiveCaptionDisplayActive() =>
            IsLiveCaptionInputMethodActive() || IsLiveCaptionSpeechInputActive();

        private bool IsLiveCaptionSpeechInputActive() =>
            (_liveCaptionEngine?.IsRunning == true && (_configManager?.LiveCaptionRealtimeEnabled == true ||
                (_configManager?.LiveCaptionShortPhraseEnabled == true && IsVerseSourceRealtime()))) ||
            (_bibleShortPhraseRuntime?.IsRunning == true && _configManager?.LiveCaptionShortPhraseEnabled == true);

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
            ResetCaptionBibleRecognition(_liveCaptionOverlayWindow?.CaptionText ?? string.Empty);
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
                    _liveCaptionOverlayWindow.SetRecognitionToggleStates(false, _configManager.LiveCaptionShortPhraseEnabled);
                    SyncRecognitionPlatformNamesToOverlay();
                    StartInputMethodBibleRecognition();
                    _liveCaptionOverlayWindow.SetInputMethodMode(true);
                    OnCaptionTextChanged(_liveCaptionOverlayWindow.CaptionText);
                    RegisterLiveCaptionF4HotKey();
                    SyncAiCaptionUiState();
                    InputMethodCaptionDiagnostics.Log($"session=active revision={revision} capture=false");
                    ShowStatus("输入法字幕已开启，可直接使用输入法语音，文字会自动显示");
                    return;
                }

                if (_inputMethodSessionActive)
                {
                    _inputMethodSessionActive = false;
                    ResetAiCaptionInput(retainInputMethodBaseline: false);
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
            _projectionManager?.HideProjectionCaptionOverlay();
            ClearLiveCaptionNdiOutput();
        }

        private void OnCaptionDocumentCleared()
        {
            ++_captionDocumentRevision;
            ResetCaptionBibleRecognition();
            if (IsLiveCaptionInputMethodActive()) StartInputMethodBibleRecognition();
            ResetLiveCaptionInputOutput();
            ResetAiCaptionInput(retainInputMethodBaseline: false);
        }

        private void OnCaptionTextChanged(string text)
        {
            if (_liveCaptionOverlayWindow == null || !IsLiveCaptionDisplayActive()) return;
            try
            {
                if (IsLiveCaptionInputMethodActive())
                {
                    ForwardInputMethodTextToAi(text);
                    ObserveInputMethodBibleText(_liveCaptionOverlayWindow.CaptionCurrentText);
                }
                // 所有字幕输入共享文档；投影和 NDI 使用各自的行容量，不依赖主屏控件大小。
                long leading = _liveCaptionOverlayWindow.CaptionLeadingTextElementCount;
                _liveCaptionComposer.SetLineCharLimit(LiveCaptionMainLineCharLimit);
                LiveCaptionRenderFrame frame = _liveCaptionComposer.PushInputSnapshot(text, leading);
                _liveCaptionNdiComposer.SetLineCharLimit(Math.Clamp(_configManager.LiveCaptionNdiLineCharLimit, 8, 80));
                LiveCaptionRenderFrame ndiFrame = _liveCaptionNdiComposer.PushInputSnapshot(text, leading);
                if (ndiFrame.HasChanged) UpdateLiveCaptionNdiCaption(ndiFrame.Display, ndiFrame.HighlightStart);
                if (frame.HasChanged) UpdateLiveCaptionProjectionCaption(frame.Display, frame.HighlightStart);
                InputMethodCaptionDiagnostics.Log($"render chars={text?.Length ?? 0} display={frame.Display.Length} changed={frame.HasChanged} ndiChanged={ndiFrame.HasChanged}");
            }
            catch (Exception ex)
            {
                InputMethodCaptionDiagnostics.Log($"render failed={ex.GetType().Name}");
                ShowStatus($"字幕显示更新失败：{ex.Message}");
            }
        }

        private void OnCaptionPrefixArchived(string prefix)
        {
            if (!IsLiveCaptionInputMethodActive()) return;
            QueueCaptionBibleProbes(_captionBibleTextBuffer.TryFlush(DateTimeOffset.UtcNow, force: true));
            _captionBibleTextBuffer.Rebase(prefix);
            // 整理旧文不是用户修订。先交付尚未发送的稳定增量，再平移 AI 的比较基线。
            if (_aiSermonCoordinator != null && _aiSermonReceiveAsr && IsLiveCaptionInputMethodActive() &&
                _aiInputMethodTurnAggregator.TryFlush(DateTimeOffset.Now, out var turn, force: true))
                SubmitAsrTurnToAi(turn);
            _aiInputMethodTurnAggregator.Rebase(prefix);
        }
    }
}
