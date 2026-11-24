using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ImageColorChanger.Core;
using ImageColorChanger.Managers;
using ImageColorChanger.Services;
using ImageColorChanger.Services.LiveCaption;
using ImageColorChanger.Services.Projection.Output;
using SkiaSharp;

namespace ImageColorChanger.UI
{
    public partial class MainWindow : Window
    {
        private RealtimeCaptionEngine _liveCaptionEngine;
        private SharedAudioCaptureSession _sharedAudioCaptureSession;
        private BibleShortPhraseRuntime _bibleShortPhraseRuntime;
        private LiveCaptionOverlayWindow _liveCaptionOverlayWindow;
        private LiveCaptionStyleControlCenterWindow _captionStyleWindow;
        private LiveCaptionDockMode _liveCaptionDockMode = LiveCaptionDockMode.Floating;
        private bool _isDisposingLiveCaption;
        private bool _liveCaptionStopInProgress;
        private bool _liveCaptionOverlayManuallyHidden;
        private bool _liveCaptionProjectionCaptionHidden = true;
        private bool _liveCaptionToggleInProgress;
        private bool _liveCaptionPlatformSwitchInProgress;
        private readonly SemaphoreSlim _liveCaptionLifecycleGate = new(1, 1);
        private bool _liveCaptionReconfigInProgress;
        private int _liveCaptionF4HotKeyId = -1;
        private LiveCaptionAudioSource _liveCaptionCurrentSource = LiveCaptionAudioSource.SystemLoopback;
        private string _liveCaptionSelectedInputDeviceId = string.Empty;
        private string _liveCaptionSelectedSystemDeviceId = string.Empty;
        private readonly LiveCaptionDisplayComposer _liveCaptionComposer = new(lineCharLimit: 30, displayLineLimit: 2);
        private readonly LiveCaptionDisplayComposer _liveCaptionNdiComposer = new(lineCharLimit: 20, displayLineLimit: 2);
        private DateTime _liveCaptionLastToggleUtc = DateTime.MinValue;
        private DateTime _shortPhraseLastFeedbackUtc = DateTime.MinValue;
        private bool _hasShownRealtimeSubtitleFeedback;
        private DateTime _captionShiftProbeLastLogUtc = DateTime.MinValue;
        private string _captionShiftProbePrevDisplay = string.Empty;
        private DispatcherTimer _liveCaptionIdleShutdownTimer;
        private DateTime _liveCaptionLastRecognitionUtc = DateTime.MinValue;
        private bool _liveCaptionIdleShutdownInProgress;
        private const int LiveCaptionToggleDebounceMs = 220;
        private const int LiveCaptionMainLineCharLimit = 30;
        private static readonly TimeSpan LiveCaptionIdleShutdownCheckInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan LiveCaptionIdleShutdownTimeout = TimeSpan.FromMinutes(1);

        private string GetRealtimeLogTag()
        {
            return LiveCaptionPlatformLabelFormatter.BuildRealtimeTag(_configManager?.LiveCaptionRealtimeAsrProvider);
        }

        private string GetRealtimeProviderDisplayName()
        {
            return LiveCaptionPlatformLabelFormatter.ToPlatformDisplayName(_configManager?.LiveCaptionRealtimeAsrProvider);
        }

        private string GetShortPhraseLogTag()
        {
            return LiveCaptionPlatformLabelFormatter.BuildShortPhraseTag(_configManager?.LiveCaptionShortAsrProvider);
        }

        private string GetShortPhraseProviderDisplayName()
        {
            return LiveCaptionPlatformLabelFormatter.ToPlatformDisplayName(_configManager?.LiveCaptionShortAsrProvider);
        }

        private string GetVerseRecognitionProviderDisplayName()
        {
            if (_configManager?.LiveCaptionInputMethodEnabled == true) return "输入法文字";
            string verseSource = (_configManager?.LiveCaptionVerseSource ?? "shortPhrase").Trim().ToLowerInvariant();
            string realtime = GetRealtimeProviderDisplayName();
            string shortPhrase = GetShortPhraseProviderDisplayName();
            return verseSource switch
            {
                "realtime" => $"跟随实时({realtime})",
                "both" => $"双路({shortPhrase}+{realtime})",
                _ => shortPhrase
            };
        }

        private void SyncRecognitionPlatformNamesToOverlay()
        {
            _liveCaptionOverlayWindow?.SetRecognitionPlatformNames(
                GetRealtimeProviderDisplayName(),
                GetVerseRecognitionProviderDisplayName());
        }

        private void LogRealtimeCaption(string message)
        {
            LiveCaptionDebugLogger.Log($"[{GetRealtimeLogTag()}] {message}");
        }

        private void LogShortPhraseCaption(string message)
        {
            LiveCaptionDebugLogger.Log($"[{GetShortPhraseLogTag()}] {message}");
        }

        private void EnsureLiveCaptionIdleShutdownTimer()
        {
            if (_liveCaptionIdleShutdownTimer != null)
            {
                return;
            }

            _liveCaptionIdleShutdownTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = LiveCaptionIdleShutdownCheckInterval
            };
            _liveCaptionIdleShutdownTimer.Tick += LiveCaptionIdleShutdownTimer_Tick;
        }

        private void UpdateLiveCaptionIdleShutdownWatcherState(bool realtimeEnabled, bool shortPhraseEnabled)
        {
            bool recognitionEnabled = realtimeEnabled || shortPhraseEnabled;
            if (!recognitionEnabled)
            {
                if (_liveCaptionIdleShutdownTimer != null)
                {
                    _liveCaptionIdleShutdownTimer.Stop();
                }

                _liveCaptionIdleShutdownInProgress = false;
                _liveCaptionLastRecognitionUtc = DateTime.MinValue;
                return;
            }

            EnsureLiveCaptionIdleShutdownTimer();
            if (_liveCaptionLastRecognitionUtc == DateTime.MinValue)
            {
                _liveCaptionLastRecognitionUtc = DateTime.UtcNow;
            }

            if (_liveCaptionIdleShutdownTimer != null && !_liveCaptionIdleShutdownTimer.IsEnabled)
            {
                _liveCaptionIdleShutdownTimer.Start();
            }
        }

        private void TouchLiveCaptionRecognitionActivity()
        {
            _liveCaptionLastRecognitionUtc = DateTime.UtcNow;
        }

        private static string BuildLiveCaptionAutoShutdownToastMessage(bool realtimeWasEnabled, bool shortPhraseWasEnabled)
        {
            if (realtimeWasEnabled && shortPhraseWasEnabled)
            {
                return "实时语音已关闭，经文识别已关闭";
            }

            if (realtimeWasEnabled)
            {
                return "实时语音已关闭";
            }

            if (shortPhraseWasEnabled)
            {
                return "经文识别已关闭";
            }

            return "字幕识别已关闭";
        }

        private async void LiveCaptionIdleShutdownTimer_Tick(object sender, EventArgs e)
        {
            _ = sender;
            _ = e;

            if (_configManager == null)
            {
                return;
            }

            bool realtimeEnabled = _configManager.LiveCaptionRealtimeEnabled;
            bool shortPhraseEnabled = _configManager.LiveCaptionShortPhraseEnabled;
            if (!LiveCaptionIdleShutdownPolicy.ShouldAutoDisable(
                    _liveCaptionLastRecognitionUtc,
                    DateTime.UtcNow,
                    realtimeEnabled,
                    shortPhraseEnabled,
                    LiveCaptionIdleShutdownTimeout))
            {
                return;
            }

            if (_liveCaptionIdleShutdownInProgress)
            {
                return;
            }

            _liveCaptionIdleShutdownInProgress = true;
            try
            {
                bool realtimeWasEnabled = _configManager.LiveCaptionRealtimeEnabled;
                bool shortPhraseWasEnabled = _configManager.LiveCaptionShortPhraseEnabled;
                LiveCaptionDebugLogger.Log("RecognitionIdleWatch: no recognition for over 1 minute, auto disabling toggles.");
                _configManager.LiveCaptionRealtimeEnabled = false;
                _configManager.LiveCaptionShortPhraseEnabled = false;
                await ApplyRecognitionStateAsync();
                _liveCaptionOverlayWindow?.SetRecognitionToggleStates(false, false);
                string toastMessage = BuildLiveCaptionAutoShutdownToastMessage(realtimeWasEnabled, shortPhraseWasEnabled);
                ShowToast(toastMessage);
                ShowStatus($"超过1分钟未识别到声音，{toastMessage}");
            }
            catch (Exception ex)
            {
                LiveCaptionDebugLogger.Log($"RecognitionIdleWatch: auto shutdown failed with {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _liveCaptionIdleShutdownInProgress = false;
            }
        }

        private void EnsureLiveCaptionComponents()
        {
            if (_configManager?.LiveCaptionInputMethodEnabled != true)
                EnsureLiveCaptionCoreComponents();
            EnsureLiveCaptionOverlayWindow();
        }

        private void EnsureLiveCaptionCoreComponents()
        {
            LoadLiveCaptionAudioSourceFromConfig();

            _sharedAudioCaptureSession ??= new SharedAudioCaptureSession();

            if (_liveCaptionEngine == null)
            {
                _liveCaptionEngine = new RealtimeCaptionEngine(_configManager);
                _liveCaptionEngine.SubtitleUpdated += OnLiveCaptionSubtitleUpdated;
                _liveCaptionEngine.StatusChanged += OnLiveCaptionStatusChanged;
                _liveCaptionEngine.DebugInfoUpdated += OnLiveCaptionDebugInfoUpdated;
            }

            EnsureCaptionBibleTextComponents();
            _bibleBaiduShortSpeechClient ??= new BibleBaiduShortSpeechClient(_configManager);
            _bibleShortPhraseRuntime ??= new BibleShortPhraseRuntime(
                new BibleShortPhraseConsumer(
                    (wav, ct) => _bibleBaiduShortSpeechClient.TranscribeWavAsync(wav, ct),
                    ResolveCaptionBibleTextAsync,
                    msg => LogShortPhraseCaption(msg),
                    () => _captionBibleToken),
                result =>
                {
                    int revision = _liveCaptionStateRevision;
                    int documentRevision = _captionDocumentRevision;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (revision != _liveCaptionStateRevision || documentRevision != _captionDocumentRevision || _configManager.LiveCaptionInputMethodEnabled || !_configManager.LiveCaptionShortPhraseEnabled) return;
                        if (result == null || result.FailureReason == "canceled" || result.SessionToken.IsCancellationRequested) return;
                        if (!string.IsNullOrWhiteSpace(result.RecognizedText))
                        {
                            TouchLiveCaptionRecognitionActivity();
                            if (!_configManager.LiveCaptionRealtimeEnabled)
                            {
                                var update = new LiveCaptionAsrText(result.RecognizedText, isFinal: true);
                                ForwardLiveCaptionAsrToAi(update);
                                _liveCaptionOverlayWindow?.ApplyRecognitionText(update);
                            }
                        }

                        if (result.Success && !result.IsDuplicate)
                        {
                            int startVs = Math.Max(1, result.Reference.StartVerse);
                            int endVs   = Math.Max(1, result.FinalEndVerse);
                            LogShortPhraseCaption(
                                $"✅ 插入历史 book={result.Reference.BookId} " +
                                $"ch={result.Reference.Chapter} vs={startVs}~{endVs} | '{result.RecognizedText}'");
                            ShowToast(FormatBibleReferenceToastText(
                                result.Reference.BookId, result.Reference.Chapter, startVs, endVs));
                            AddPinyinHistoryToEmptySlot(
                                result.Reference.BookId, result.Reference.Chapter, startVs, endVs, focusHistorySlot: true);
                        }

                        ShowShortPhraseFeedback(result);
                    }));
                });
        }

        private void EnsureLiveCaptionOverlayWindow()
        {
            if (_liveCaptionOverlayWindow == null)
            {
                _liveCaptionOverlayWindow = new LiveCaptionOverlayWindow();
                _liveCaptionOverlayWindow.SettingsRequested += OpenLiveCaptionOverlaySettings;
                _liveCaptionOverlayWindow.AiPanelRequested += OpenAiPanelFromLiveCaptionOverlay;
                _liveCaptionOverlayWindow.HideRequested += HideLiveCaptionOverlayFromPanel;
                _liveCaptionOverlayWindow.CaptionStyleRequested += OpenLiveCaptionStyleSettings;
                _liveCaptionOverlayWindow.ProjectionToggleRequested += ToggleProjectionFromLiveCaptionOverlay;
                _liveCaptionOverlayWindow.NdiToggleRequested += ToggleNdiFromLiveCaptionOverlay;
                _liveCaptionOverlayWindow.CaptionOrientationRequested += OnLiveCaptionOverlayCaptionOrientationRequested;
                _liveCaptionOverlayWindow.CaptionPositionRequested += OnLiveCaptionOverlayCaptionPositionRequested;
                _liveCaptionOverlayWindow.CloseRequested += StopLiveCaption;
                _liveCaptionOverlayWindow.InputMethodToggleRequested += OnInputMethodToggleRequested;
                _liveCaptionOverlayWindow.CaptionTextChanged += OnCaptionTextChanged;
                _liveCaptionOverlayWindow.CaptionCleared += OnCaptionDocumentCleared;
                _liveCaptionOverlayWindow.CaptionPrefixArchived += OnCaptionPrefixArchived;
                _liveCaptionOverlayWindow.RealtimeRecognitionToggleRequested += OnRealtimeRecognitionToggleRequested;
                _liveCaptionOverlayWindow.ShortPhraseRecognitionToggleRequested += OnShortPhraseRecognitionToggleRequested;
                _liveCaptionOverlayWindow.FloatingBoundsChanged += OnLiveCaptionFloatingBoundsChanged;
                _liveCaptionOverlayWindow.Closed += LiveCaptionOverlayWindow_Closed;
                _liveCaptionOverlayWindow.SetWorkAreaReservationEnabled(ShouldReserveWorkAreaForDockMode(_liveCaptionDockMode));
                _liveCaptionOverlayWindow.SetProjectionToggleState(_liveCaptionProjectionCaptionHidden);
                SyncRecognitionPlatformNamesToOverlay();
                _liveCaptionOverlayWindow.SetRecognitionToggleStates(_configManager.LiveCaptionRealtimeEnabled, _configManager.LiveCaptionShortPhraseEnabled);
                UpdateLiveCaptionNdiActionState();
                LoadLiveCaptionFloatingBoundsFromConfig();
                ApplyLiveCaptionTypographyFromBible();
                ApplyProjectionCaptionLayoutFromConfig();
            }
        }

        private void BtnLiveCaptionMic_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
            {
                return;
            }

            StartLiveCaption(LiveCaptionAudioSource.Microphone);
        }

        private void BtnLiveCaptionSystem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
            {
                return;
            }

            StartLiveCaption(LiveCaptionAudioSource.SystemLoopback);
        }

        private void BtnLiveCaptionStop_Click(object sender, RoutedEventArgs e)
        {
            StopLiveCaption();
        }

        private void BtnAiCaption_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;
            if (IsLiveCaptionDisplayActive())
            {
                TryToggleLiveCaptionOverlayVisibilityInternal("top-button");
                SyncAiCaptionUiState();
                return;
            }

            if (!TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
            {
                return;
            }

            StartLiveCaption(_liveCaptionCurrentSource);
        }

        public static (string Text, string ToolTip, bool IsActive, bool IsEnabled) BuildAiCaptionButtonStateForTest(
            bool realtimeEnabled,
            bool engineRunning,
            bool overlayVisible,
            bool featureAllowed = true,
            string deniedToolTip = null,
            bool inputMethodActive = false)
        {
            if (!featureAllowed)
            {
                return ("字幕", string.IsNullOrWhiteSpace(deniedToolTip) ? "功能未开通" : deniedToolTip, false, false);
            }

            bool active = inputMethodActive || (realtimeEnabled && engineRunning);
            _ = overlayVisible;
            if (!active)
            {
                return ("字幕", "打开 AI字幕 (F4)", false, true);
            }

            return ("字幕", inputMethodActive ? "输入法字幕运行中 (F4)" : "AI字幕运行中 (F4)", true, true);
        }

        private bool IsLiveCaptionRealtimeConnected()
        {
            return _configManager?.LiveCaptionRealtimeEnabled == true && _liveCaptionEngine?.IsRunning == true;
        }

        private void SyncAiCaptionUiState()
        {
            UpdateAiCaptionTopButtonState();
            SyncAiPanelAsrConnectionStatus();
        }

        private void UpdateAiCaptionTopButtonState()
        {
            if (BtnAiCaption == null)
            {
                return;
            }

            var state = BuildAiCaptionButtonStateForTest(
                _configManager?.LiveCaptionRealtimeEnabled == true,
                _liveCaptionEngine?.IsRunning == true,
                _liveCaptionOverlayWindow?.IsVisible == true,
                TryGetPremiumFeatureUiState(Services.Licensing.PremiumFeature.LiveCaption, out string deniedToolTip),
                deniedToolTip,
                IsLiveCaptionInputMethodActive());

            if (BtnAiCaptionText != null)
            {
                BtnAiCaptionText.Text = state.Text;
            }
            BtnAiCaption.ToolTip = state.ToolTip;
            BtnAiCaption.IsEnabled = state.IsEnabled;
            ToolTipService.SetShowOnDisabled(BtnAiCaption, !state.IsEnabled);

            if (!state.IsEnabled)
            {
                BtnAiCaption.Opacity = 0.48;
                BtnAiCaption.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF1, 0xF3, 0xF5));
                BtnAiCaption.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD6, 0xDA, 0xDE));
                BtnAiCaption.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0xA1, 0xAA));
                if (BtnAiCaptionIcon != null)
                {
                    BtnAiCaptionIcon.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0xA1, 0xAA));
                }
                return;
            }

            BtnAiCaption.ClearValue(UIElement.OpacityProperty);

            if (state.IsActive)
            {
                BtnAiCaption.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD9, 0xF9, 0xE6));
                BtnAiCaption.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E));
                BtnAiCaption.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x3D, 0x24));
                if (BtnAiCaptionIcon != null)
                {
                    BtnAiCaptionIcon.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A));
                }
                return;
            }

            BtnAiCaption.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            BtnAiCaption.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
            BtnAiCaption.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
            if (BtnAiCaptionIcon != null)
            {
                BtnAiCaptionIcon.ClearValue(System.Windows.Shapes.Shape.StrokeProperty);
            }
        }

        private void SyncAiPanelAsrConnectionStatus()
        {
            _aiAssistantPanelWindow?.SetAsrConnectionState(_aiSermonReceiveAsr, IsLiveCaptionSpeechInputActive(), IsLiveCaptionInputMethodActive());
        }

        private void OpenLiveCaptionPanel()
        {
            if (!_liveCaptionOverlayWindow.IsVisible)
            {
                LoadLiveCaptionFloatingBoundsFromConfig();
                _liveCaptionOverlayWindow.Show();
            }

            SyncRecognitionPlatformNamesToOverlay();
            _liveCaptionOverlayWindow.SetRecognitionToggleStates(_configManager.LiveCaptionRealtimeEnabled, _configManager.LiveCaptionShortPhraseEnabled);
            _liveCaptionOverlayWindow.SetInputMethodMode(IsLiveCaptionInputMethodActive());
            _liveCaptionOverlayWindow.RefreshDockLayoutNow();
            ApplyLiveCaptionTypographyFromBible();
            SyncLiveCaptionVisibilityWithMainWindowContext("panel-open");
            SyncAiCaptionUiState();
        }

        private void OpenAiConfigFile()
        {
            try
            {
                var dialog = new AiConfigWindow(_configManager) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    ApplyLiveCaptionConfigImmediately();
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"打开 AI 配置失败: {ex.Message}");
            }
        }

        private void ApplyLiveCaptionConfigImmediately()
        {
            if (_liveCaptionReconfigInProgress)
            {
                LiveCaptionDebugLogger.Log("ConfigReload: skipped because reconfigure is already running.");
                return;
            }

            _liveCaptionReconfigInProgress = true;
            _liveCaptionLifecycleGate.Wait();
            bool wasRunning = _liveCaptionEngine?.IsRunning == true;
            LiveCaptionAudioSource source = _liveCaptionCurrentSource;
            try
            {
                if (wasRunning)
                {
                    StopLiveCaption();
                }

                if (_liveCaptionEngine != null)
                {
                    _liveCaptionEngine.SubtitleUpdated -= OnLiveCaptionSubtitleUpdated;
                    _liveCaptionEngine.StatusChanged -= OnLiveCaptionStatusChanged;
                    _liveCaptionEngine.DebugInfoUpdated -= OnLiveCaptionDebugInfoUpdated;
                    _liveCaptionEngine.Dispose();
                    _liveCaptionEngine = null;
                    LiveCaptionDebugLogger.Log("ConfigReload: previous engine disposed.");
                }

                if (wasRunning)
                {
                    StartLiveCaption(source);
                    ShowStatus("AI配置已保存并立即生效（实时字幕已重启）");
                    return;
                }

                SyncRecognitionPlatformNamesToOverlay();
                ShowStatus("AI配置已保存并立即生效");
            }
            finally
            {
                _liveCaptionLifecycleGate.Release();
                _liveCaptionReconfigInProgress = false;
            }
        }

        private async void StartLiveCaption(LiveCaptionAudioSource source)
        {
            if (!TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
            {
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _configManager.EnsureLiveCaptionSourceSelected();
            EnsureLiveCaptionComponents();
            if (_liveCaptionEngine?.IsRunning != true) _liveCaptionOverlayWindow?.ResetRecognitionSession();
            LiveCaptionDebugLogger.Log($"StartPerf: ensure-components={sw.ElapsedMilliseconds}ms");
            LiveCaptionDebugLogger.Log($"Start: request source={source}, configured={_liveCaptionEngine?.IsConfigured}, running={_liveCaptionEngine?.IsRunning}");

            _liveCaptionOverlayManuallyHidden = false;
            _liveCaptionProjectionCaptionHidden = true;
            _liveCaptionComposer.Reset();
            _hasShownRealtimeSubtitleFeedback = false;
            _shortPhraseLastFeedbackUtc = DateTime.MinValue;

            if (_liveCaptionOverlayWindow.GetDockMode() != _liveCaptionDockMode)
            {
                _liveCaptionOverlayWindow.SetDockMode(_liveCaptionDockMode);
            }
            _liveCaptionOverlayWindow.SetWorkAreaReservationEnabled(ShouldReserveWorkAreaForDockMode(_liveCaptionDockMode));

            bool wasVisible = _liveCaptionOverlayWindow.IsVisible;
            if (!wasVisible)
            {
                LoadLiveCaptionFloatingBoundsFromConfig();
                _liveCaptionOverlayWindow.Show();
            }
            LiveCaptionDebugLogger.Log($"StartPerf: overlay-show={sw.ElapsedMilliseconds}ms");

            _liveCaptionOverlayWindow.RefreshDockLayoutNow();
            ApplyLiveCaptionTypographyFromBible();
            UpdateLiveCaptionProjectionActionState();
            UpdateLiveCaptionNdiActionState();
            SyncLiveCaptionProjectionCaptionForProjectionState(_projectionManager?.IsProjectionActive == true);
            ApplyMainWindowLiveCaptionReservation();
            _liveCaptionCurrentSource = source;
            SyncRecognitionPlatformNamesToOverlay();
            _liveCaptionOverlayWindow.SetRecognitionToggleStates(
                _configManager.LiveCaptionRealtimeEnabled,
                _configManager.LiveCaptionShortPhraseEnabled);
            SyncLiveCaptionVisibilityWithMainWindowContext("start");
            LiveCaptionDebugLogger.Log($"StartPerf: overlay-prepare={sw.ElapsedMilliseconds}ms");

            RegisterLiveCaptionF4HotKey();
            LiveCaptionDebugLogger.Log($"StartPerf: engine-start-begin={sw.ElapsedMilliseconds}ms");
            try
            {
                await ApplyRecognitionStateAsync();
            }
            catch (Exception ex)
            {
                LiveCaptionDebugLogger.Log($"Start: apply recognition failed with {ex.GetType().Name}: {ex.Message}");
                ShowStatus($"字幕启动失败：{ex.Message}");
            }
            SyncAiCaptionUiState();
            LiveCaptionDebugLogger.Log($"Start: recognition state applied, dock={_liveCaptionDockMode}, overlayVisible={_liveCaptionOverlayWindow.IsVisible}");
        }

        private void StopLiveCaption()
        {
            if (_liveCaptionStopInProgress) return;
            _liveCaptionStopInProgress = true;
            ++_liveCaptionStateRevision;
            _inputMethodSessionActive = false;
            ResetCaptionBibleRecognition();
            try
            {
                _liveCaptionOverlayWindow?.SetInputMethodMode(false);
                _liveCaptionOverlayWindow?.ResetRecognitionSession();
                ResetAiCaptionInput(retainInputMethodBaseline: false);
                InputMethodCaptionDiagnostics.Log("session=stopped");
                LiveCaptionDebugLogger.Log($"Stop: request running={_liveCaptionEngine?.IsRunning}, overlayVisible={_liveCaptionOverlayWindow?.IsVisible}");
                _liveCaptionEngine?.Stop();
                _bibleShortPhraseRuntime?.Abort();
                _sharedAudioCaptureSession?.Stop();
                _configManager.LiveCaptionRealtimeEnabled = false;
                // 关闭字幕只停止运行时，经文识别的用户选择保留到下次打开。
                _liveCaptionOverlayWindow?.SetRecognitionToggleStates(false, false);
                PersistLiveCaptionFloatingBoundsToConfig("stop");

                if (_liveCaptionOverlayWindow != null && _liveCaptionOverlayWindow.IsVisible)
                {
                    _liveCaptionOverlayWindow.Hide();
                }

                _liveCaptionOverlayManuallyHidden = false;
                _liveCaptionToggleInProgress = false;
                _liveCaptionProjectionCaptionHidden = true;
                _liveCaptionComposer.Reset();
                _liveCaptionNdiComposer.Reset();
                _hasShownRealtimeSubtitleFeedback = false;
                _shortPhraseLastFeedbackUtc = DateTime.MinValue;
                UpdateLiveCaptionIdleShutdownWatcherState(realtimeEnabled: false, shortPhraseEnabled: false);
                UpdateLiveCaptionProjectionActionState();
                UpdateLiveCaptionNdiActionState();
                _projectionManager?.HideProjectionCaptionOverlay();
                ClearLiveCaptionNdiOutput(stopSender: true);
                ApplyMainWindowLiveCaptionReservation();
                UnregisterLiveCaptionF4HotKey();
                SyncAiCaptionUiState();
                LiveCaptionDebugLogger.Log("Stop: completed.");
            }
            catch (Exception ex)
            {
                LiveCaptionDebugLogger.Log($"Stop: failed with {ex.GetType().Name}: {ex.Message}");
                ShowStatus($"关闭字幕失败：{ex.Message}");
            }
            finally { _liveCaptionStopInProgress = false; }
        }

        private void LiveCaptionOverlayWindow_Closed(object sender, EventArgs e)
        {
            LiveCaptionDebugLogger.Log($"OverlayClosed: disposing={_isDisposingLiveCaption}");
            if (ReferenceEquals(sender, _liveCaptionOverlayWindow)) _liveCaptionOverlayWindow = null;
            if (_isDisposingLiveCaption)
            {
                return;
            }

            StopLiveCaption();
        }

        private async void OnRealtimeRecognitionToggleRequested(bool enabled)
        {
            LogRealtimeCaption($"RecognitionToggle: enabled={enabled}");
            if (enabled && !TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
            {
                _configManager.LiveCaptionRealtimeEnabled = false;
                _liveCaptionOverlayWindow?.SetRecognitionToggleStates(false, _configManager.LiveCaptionShortPhraseEnabled);
                SyncAiCaptionUiState();
                return;
            }

            _configManager.LiveCaptionRealtimeEnabled = enabled;
            await ApplyRecognitionStateAsync();
        }

        private async void OnShortPhraseRecognitionToggleRequested(bool enabled)
        {
            LogShortPhraseCaption($"RecognitionToggle: enabled={enabled}");
            if (enabled && !TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
            {
                _configManager.LiveCaptionShortPhraseEnabled = false;
                _liveCaptionOverlayWindow?.SetRecognitionToggleStates(_configManager.LiveCaptionRealtimeEnabled, false);
                return;
            }

            _configManager.LiveCaptionShortPhraseEnabled = enabled;
            await ApplyRecognitionStateAsync();
        }

        private void ApplyRecognitionStateCore()
        {
            EnsureLiveCaptionCoreComponents();

            bool realtimeEnabled = _configManager.LiveCaptionRealtimeEnabled;
            bool shortEnabled = _configManager.LiveCaptionShortPhraseEnabled;
            string verseSource = _configManager.LiveCaptionVerseSource ?? "shortPhrase";
            LiveCaptionDebugLogger.Log(
                $"[{GetRealtimeLogTag()}|{GetShortPhraseLogTag()}] RecognitionState: " +
                $"realtime={realtimeEnabled}, short={shortEnabled}, verseSource={verseSource}, " +
                $"source={_liveCaptionCurrentSource}, inputId='{_liveCaptionSelectedInputDeviceId}', systemId='{_liveCaptionSelectedSystemDeviceId}'");
            SyncRecognitionPlatformNamesToOverlay();
            _liveCaptionOverlayWindow?.SetRecognitionToggleStates(realtimeEnabled, shortEnabled);
            UpdateLiveCaptionIdleShutdownWatcherState(realtimeEnabled, shortEnabled);

            if ((realtimeEnabled || shortEnabled) &&
                !TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
            {
                LiveCaptionDebugLogger.Log("RecognitionState: premium.live_caption denied, stopping consumers and shared capture.");
                _configManager.LiveCaptionRealtimeEnabled = false;
                _configManager.LiveCaptionShortPhraseEnabled = false;
                _liveCaptionOverlayWindow?.SetRecognitionToggleStates(false, false);
                UpdateLiveCaptionIdleShutdownWatcherState(realtimeEnabled: false, shortPhraseEnabled: false);
                _liveCaptionEngine?.Stop();
                if (_bibleShortPhraseRuntime?.IsRunning == true)
                {
                    _bibleShortPhraseRuntime.Abort();
                }
                _sharedAudioCaptureSession?.Stop();
                _liveCaptionProjectionCaptionHidden = true;
                _liveCaptionComposer.Reset();
                _liveCaptionNdiComposer.Reset();
                _projectionManager?.HideProjectionCaptionOverlay();
                ClearLiveCaptionNdiOutput(stopSender: true);
                UpdateLiveCaptionProjectionActionState();
                UpdateLiveCaptionNdiActionState();
                SyncAiCaptionUiState();
                return;
            }

            if (!realtimeEnabled && !shortEnabled)
            {
                LiveCaptionDebugLogger.Log("RecognitionState: both disabled, stopping all consumers and shared capture.");
                _liveCaptionEngine?.Stop();
                if (_bibleShortPhraseRuntime?.IsRunning == true)
                {
                    _bibleShortPhraseRuntime.Abort();
                }
                _sharedAudioCaptureSession?.Stop();
                ShowStatus("字幕识别已关闭");
                SyncAiCaptionUiState();
                return;
            }

            _sharedAudioCaptureSession.SetSelection(_liveCaptionCurrentSource, _liveCaptionSelectedInputDeviceId, _liveCaptionSelectedSystemDeviceId);
            if (!_sharedAudioCaptureSession.IsRunning)
            {
                LiveCaptionDebugLogger.Log("RecognitionState: starting shared audio capture.");
                _sharedAudioCaptureSession.Start();
                if (!_sharedAudioCaptureSession.IsRunning)
                {
                    string error = _sharedAudioCaptureSession.LastStartError;
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        error = "音频采集启动失败：当前设备不可用，请切换其他设备重试。";
                    }

                    LiveCaptionDebugLogger.Log($"RecognitionState: shared audio capture failed, error='{error}'.");
                    ShowStatus(error);
                    SyncAiCaptionUiState();
                    return;
                }

                if (_sharedAudioCaptureSession.LastStartFallbackApplied &&
                    _liveCaptionCurrentSource != _sharedAudioCaptureSession.CurrentSource)
                {
                    _liveCaptionCurrentSource = _sharedAudioCaptureSession.CurrentSource;
                    if (_configManager != null)
                    {
                        _configManager.LiveCaptionAudioInputMode = _liveCaptionCurrentSource == LiveCaptionAudioSource.SystemLoopback ? "system" : "input";
                        _configManager.LiveCaptionInputDeviceId = _liveCaptionSelectedInputDeviceId ?? string.Empty;
                        _configManager.LiveCaptionSystemDeviceId = _liveCaptionSelectedSystemDeviceId ?? string.Empty;
                    }

                    string fallbackInfo = _sharedAudioCaptureSession.LastStartError;
                    if (string.IsNullOrWhiteSpace(fallbackInfo))
                    {
                        fallbackInfo = $"输入设备不可用，已自动切换为系统声音：{GetLiveCaptionSystemDeviceDisplayName(_liveCaptionSelectedSystemDeviceId)}";
                    }
                    ShowStatus(fallbackInfo);
                    LiveCaptionDebugLogger.Log($"RecognitionState: source auto-fallback applied, source={_liveCaptionCurrentSource}, info='{fallbackInfo}'.");
                }

                LiveCaptionDebugLogger.Log($"RecognitionState: shared audio capture started, device='{_sharedAudioCaptureSession.SelectedDeviceName}'.");
            }

            if (ShouldRouteXfyunShortPhraseToRealtime() &&
                !string.Equals(_configManager.LiveCaptionRealtimeAsrProvider, "xfyun", StringComparison.OrdinalIgnoreCase))
            {
                _configManager.LiveCaptionRealtimeAsrProvider = "xfyun";
                LogShortPhraseCaption("飞讯短语已按实时语音链路处理。");
            }

            bool needRealtimeEngine = realtimeEnabled || (shortEnabled && IsVerseSourceRealtime());
            if (needRealtimeEngine)
            {
                if (!_liveCaptionEngine.IsConfigured)
                {
                    LogRealtimeCaption("RecognitionState: requested but ASR config incomplete.");
                    ShowStatus("实时字幕未启动：请先完成 AI配置（ASR服务与鉴权信息）");
                }
                else if (!_liveCaptionEngine.IsRunning)
                {
                    LogRealtimeCaption("RecognitionState: starting engine with shared capture.");
                    _liveCaptionEngine.StartWithSharedCapture(_sharedAudioCaptureSession);
                    ShowStatus("实时语音已启用");
                }
            }
            else
            {
                LogRealtimeCaption("RecognitionState: stopping engine.");
                _liveCaptionEngine?.Stop();
            }

            bool needShortPhraseAsr = shortEnabled && IsVerseSourceShortPhrase();
            if (needShortPhraseAsr)
            {
                if (!_bibleBaiduShortSpeechClient.IsConfigured)
                {
                    string missing = _bibleBaiduShortSpeechClient.MissingConfigSummary;
                    LogShortPhraseCaption($"RecognitionState: requested but config incomplete. missing={missing}");
                    ShowStatus(string.IsNullOrWhiteSpace(missing)
                        ? "经文识别未启动：请先在 AI配置 中填写短语识别鉴权信息"
                        : $"经文识别未启动：缺少 {missing}");
                }
                else if (!_bibleShortPhraseRuntime.IsRunning)
                {
                    LogShortPhraseCaption("RecognitionState: starting runtime with shared capture.");
                    _bibleShortPhraseRuntime.Start(_sharedAudioCaptureSession);
                    ShowStatus("经文识别已启用，等待识别结果");
                }
                else
                {
                    LogShortPhraseCaption("RecognitionState: runtime already running.");
                }
            }
            else if (_bibleShortPhraseRuntime?.IsRunning == true)
            {
                LogShortPhraseCaption("RecognitionState: stopping runtime.");
                _bibleShortPhraseRuntime.Abort();
                ShowStatus("经文识别已关闭");
            }

            SyncAiCaptionUiState();
        }

        internal async Task ApplyLiveCaptionRecognitionStateFromConfigAsync()
        {
            bool realtimeEnabled = _configManager.LiveCaptionRealtimeEnabled;
            bool shortEnabled = _configManager.LiveCaptionShortPhraseEnabled;

            if (_liveCaptionOverlayWindow != null)
            {
                _liveCaptionOverlayWindow.SetRecognitionToggleStates(realtimeEnabled, shortEnabled);
            }

            bool sessionActive =
                IsLiveCaptionInputMethodActive() ||
                (_liveCaptionOverlayWindow?.IsVisible == true) ||
                (_sharedAudioCaptureSession?.IsRunning == true) ||
                (_liveCaptionEngine?.IsRunning == true) ||
                (_bibleShortPhraseRuntime?.IsRunning == true);

            if (sessionActive)
            {
                await ApplyRecognitionStateAsync();
            }
            else if (shortEnabled || realtimeEnabled)
            {
                // 识别模式已在设置中启用，但引擎尚未运行 → 静默启动识别引擎
                // 不调用 StartLiveCaption()，避免显示字幕框
                await ApplyRecognitionStateAsync();
            }
        }

        private void OnLiveCaptionSubtitleUpdated(LiveCaptionAsrText update)
        {
            int revision = _liveCaptionStateRevision;
            int documentRevision = _captionDocumentRevision;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (revision != _liveCaptionStateRevision || documentRevision != _captionDocumentRevision || _configManager.LiveCaptionInputMethodEnabled ||
                    _liveCaptionEngine == null || !_liveCaptionEngine.IsRunning)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(update.Text))
                {
                    TouchLiveCaptionRecognitionActivity();
                }

                ForwardLiveCaptionAsrToAi(update);

                // 语音只提供文本和final标记；独立文字服务负责经文能力。
                if (_configManager.LiveCaptionShortPhraseEnabled && IsVerseSourceRealtime() &&
                    _captionBibleTextBuffer.TryCreateRealtimeProbe(update.Text, update.IsFinal, DateTimeOffset.UtcNow, out var probe))
                    QueueCaptionBibleProbes(new[] { probe });

                // 主屏始终更新统一记录，隐藏窗口也继续接收文字；投影 / NDI 由文档快照独立排版。
                if ((!_configManager.LiveCaptionRealtimeEnabled && !ShouldRouteXfyunShortPhraseToRealtime()) ||
                    _liveCaptionOverlayWindow == null) return;
                _liveCaptionOverlayWindow.ApplyRecognitionText(update);
                if (!_hasShownRealtimeSubtitleFeedback && !string.IsNullOrWhiteSpace(update.Text))
                {
                    _hasShownRealtimeSubtitleFeedback = true;
                    ShowStatus("实时字幕已接收");
                }
                if (_liveCaptionOverlayManuallyHidden) return;
                if (!_liveCaptionOverlayWindow.IsVisible) _liveCaptionOverlayWindow.Show();
                SyncLiveCaptionVisibilityWithMainWindowContext("subtitle-updated");
                ApplyMainWindowLiveCaptionReservation();
            }));
        }

        private void ShowShortPhraseFeedback(BibleShortPhraseConsumer.Result result)
        {
            string message = BibleShortPhraseFeedbackPolicy.BuildStatusMessage(result);
            bool isSuccess = result?.Success == true;
            var nowUtc = DateTime.UtcNow;
            if (!isSuccess && (nowUtc - _shortPhraseLastFeedbackUtc).TotalSeconds < 6)
            {
                return;
            }

            _shortPhraseLastFeedbackUtc = nowUtc;
            ShowStatus(message);
        }

        private bool IsVerseSourceRealtime()
        {
            string src = _configManager?.LiveCaptionVerseSource ?? "shortPhrase";
            if (ShouldRouteXfyunShortPhraseToRealtime())
            {
                return true;
            }
            return string.Equals(src, "realtime", StringComparison.OrdinalIgnoreCase)
                || string.Equals(src, "both", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsVerseSourceShortPhrase()
        {
            string src = _configManager?.LiveCaptionVerseSource ?? "shortPhrase";
            if (ShouldRouteXfyunShortPhraseToRealtime())
            {
                return false;
            }
            return string.Equals(src, "shortPhrase", StringComparison.OrdinalIgnoreCase)
                || string.Equals(src, "both", StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldRouteXfyunShortPhraseToRealtime()
        {
            if (_configManager == null)
            {
                return false;
            }

            if (!_configManager.LiveCaptionShortPhraseEnabled)
            {
                return false;
            }

            string verseSource = (_configManager.LiveCaptionVerseSource ?? "shortPhrase").Trim();
            if (!string.Equals(verseSource, "shortPhrase", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string shortProvider = (_configManager.LiveCaptionShortAsrProvider ?? string.Empty).Trim();
            return string.Equals(shortProvider, "xfyun", StringComparison.OrdinalIgnoreCase);
        }

        private void OnLiveCaptionStatusChanged(string status)
        {
            LogRealtimeCaption($"EngineStatus: {status}");
            Dispatcher.BeginInvoke(new Action(() => ShowStatus(status)));
        }

        private void OnLiveCaptionDebugInfoUpdated(string debugInfo)
        {
            if (string.IsNullOrWhiteSpace(debugInfo))
                return;

            if (debugInfo.StartsWith("[started]", StringComparison.Ordinal)
                || debugInfo.StartsWith("[stopped]", StringComparison.Ordinal)
                || debugInfo.StartsWith("[error]", StringComparison.Ordinal))
            {
                LogRealtimeCaption($"EngineDebug: {debugInfo}");
            }
        }

        private void DisposeLiveCaption()
        {
            LiveCaptionDebugLogger.Log("Dispose: begin.");
            _isDisposingLiveCaption = true;
            ResetCaptionBibleRecognition();
            if (_captionBibleTextTimer != null) _captionBibleTextTimer.Tick -= OnCaptionBibleTextTimerTick;
            _captionBibleTextTimer = null;
            _captionBibleCts.Cancel();
            _captionBibleCts.Dispose();
            _captionBibleTextService = null;
            try
            {
                if (_liveCaptionIdleShutdownTimer != null)
                {
                    _liveCaptionIdleShutdownTimer.Stop();
                    _liveCaptionIdleShutdownTimer.Tick -= LiveCaptionIdleShutdownTimer_Tick;
                    _liveCaptionIdleShutdownTimer = null;
                }
                _liveCaptionLastRecognitionUtc = DateTime.MinValue;
                _liveCaptionIdleShutdownInProgress = false;

                if (_liveCaptionEngine != null)
                {
                    _liveCaptionEngine.SubtitleUpdated -= OnLiveCaptionSubtitleUpdated;
                    _liveCaptionEngine.StatusChanged -= OnLiveCaptionStatusChanged;
                    _liveCaptionEngine.DebugInfoUpdated -= OnLiveCaptionDebugInfoUpdated;
                    _liveCaptionEngine.Dispose();
                    _liveCaptionEngine = null;
                    LiveCaptionDebugLogger.Log("Dispose: engine disposed.");
                }

                if (_sharedAudioCaptureSession != null)
                {
                    _sharedAudioCaptureSession.Dispose();
                    _sharedAudioCaptureSession = null;
                }

                if (_bibleShortPhraseRuntime != null)
                {
                    _bibleShortPhraseRuntime.Dispose();
                    _bibleShortPhraseRuntime = null;
                }

                if (_liveCaptionOverlayWindow != null)
                {
                    PersistLiveCaptionFloatingBoundsToConfig("dispose");
                    _liveCaptionOverlayWindow.SettingsRequested -= OpenLiveCaptionOverlaySettings;
                    _liveCaptionOverlayWindow.AiPanelRequested -= OpenAiPanelFromLiveCaptionOverlay;
                    _liveCaptionOverlayWindow.HideRequested -= HideLiveCaptionOverlayFromPanel;
                    _liveCaptionOverlayWindow.CaptionStyleRequested -= OpenLiveCaptionStyleSettings;
                    _liveCaptionOverlayWindow.ProjectionToggleRequested -= ToggleProjectionFromLiveCaptionOverlay;
                    _liveCaptionOverlayWindow.NdiToggleRequested -= ToggleNdiFromLiveCaptionOverlay;
                    _liveCaptionOverlayWindow.CaptionOrientationRequested -= OnLiveCaptionOverlayCaptionOrientationRequested;
                    _liveCaptionOverlayWindow.CaptionPositionRequested -= OnLiveCaptionOverlayCaptionPositionRequested;
                    _liveCaptionOverlayWindow.CloseRequested -= StopLiveCaption;
                    _liveCaptionOverlayWindow.InputMethodToggleRequested -= OnInputMethodToggleRequested;
                    _liveCaptionOverlayWindow.CaptionTextChanged -= OnCaptionTextChanged;
                    _liveCaptionOverlayWindow.CaptionCleared -= OnCaptionDocumentCleared;
                    _liveCaptionOverlayWindow.CaptionPrefixArchived -= OnCaptionPrefixArchived;
                    _liveCaptionOverlayWindow.RealtimeRecognitionToggleRequested -= OnRealtimeRecognitionToggleRequested;
                    _liveCaptionOverlayWindow.ShortPhraseRecognitionToggleRequested -= OnShortPhraseRecognitionToggleRequested;
                    _liveCaptionOverlayWindow.FloatingBoundsChanged -= OnLiveCaptionFloatingBoundsChanged;
                    _liveCaptionOverlayWindow.Closed -= LiveCaptionOverlayWindow_Closed;
                    _liveCaptionOverlayWindow.Close();
                    _liveCaptionOverlayWindow = null;
                    LiveCaptionDebugLogger.Log("Dispose: overlay disposed.");
                }

            _liveCaptionComposer.Reset();
            _liveCaptionOverlayManuallyHidden = false;
            _liveCaptionProjectionCaptionHidden = true;
            _projectionManager?.HideProjectionCaptionOverlay();
            _liveCaptionNdiOutput?.Dispose();
            _liveCaptionNdiOutput = null;
            _liveCaptionNdiComposer.Reset();
            ApplyMainWindowLiveCaptionReservation();
            UnregisterLiveCaptionF4HotKey();
            }
            finally
            {
                _isDisposingLiveCaption = false;
                LiveCaptionDebugLogger.Log("Dispose: end.");
            }
        }

        private void OpenLiveCaptionOverlaySettings()
        {
            if (_liveCaptionOverlayWindow == null)
            {
                return;
            }

            var menu = new ContextMenu
            {
                MinWidth = 220,
                FontSize = 14
            };

            PopulateLiveCaptionInputDeviceMenu(menu);

            menu.PlacementTarget = _liveCaptionOverlayWindow.GetSettingsAnchorElement();
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            menu.HorizontalOffset = 6;
            menu.VerticalOffset = 0;
            menu.IsOpen = true;
            LiveCaptionDebugLogger.Log("Settings: context menu opened.");
        }

        private void OpenAiPanelFromLiveCaptionOverlay()
        {
            ToggleAiAssistantPanelVisibilityByShortcut();
        }

        private void OpenLiveCaptionStyleSettings()
        {
            OpenLiveCaptionStyleSettings("projection");
        }

        private void OpenLiveCaptionStyleSettings(string initialProfileKey)
        {
            if (_liveCaptionOverlayWindow == null || _configManager == null)
            {
                return;
            }

            if (_captionStyleWindow == null || !_captionStyleWindow.IsVisible)
            {
                _captionStyleWindow = new LiveCaptionStyleControlCenterWindow(
                    new[]
                    {
                        CreateLocalCaptionStyleProfile(),
                        CreateProjectionCaptionStyleProfile(),
                        CreateNdiCaptionStyleProfile(),
                    },
                    initialProfileKey);
                _captionStyleWindow.Owner = this;
                _captionStyleWindow.Closed += (_, _) => _captionStyleWindow = null;
                RestoreAndTrackLiveCaptionStyleWindow(_captionStyleWindow, "liveCaption.style.unified");
                _captionStyleWindow.Show();
            }
            else
            {
                _captionStyleWindow.SelectProfile(initialProfileKey);
                _captionStyleWindow.Activate();
            }
        }

        private LiveCaptionStyleControlCenterWindow.Profile CreateProjectionCaptionStyleProfile()
        {
            return new LiveCaptionStyleControlCenterWindow.Profile(
                key: "projection",
                label: "投影",
                title: "AI字幕 · 投影样式",
                subtitle: "控制台模式：滑条 / 数值，步长 0.5。",
                loadState: () => new LiveCaptionStyleControlCenterWindow.State
                {
                    FontFamily = string.IsNullOrWhiteSpace(_configManager.LiveCaptionFontFamily)
                        ? (string.IsNullOrWhiteSpace(_configManager.BibleFontFamily) ? "Microsoft YaHei UI" : _configManager.BibleFontFamily.Trim())
                        : _configManager.LiveCaptionFontFamily.Trim(),
                    FontSize = Math.Clamp(_configManager.LiveCaptionFontSize > 0 ? _configManager.LiveCaptionFontSize : (_configManager.BibleFontSize > 0 ? _configManager.BibleFontSize : 36), 20, 112),
                    LetterSpacing = Math.Clamp(_configManager.LiveCaptionLetterSpacing, 0, 10),
                    LineGapLevel = LineGapToLevel(_configManager.LiveCaptionLineGap),
                    TextColor = NormalizeColorHex(_configManager.LiveCaptionTextColor, "#FFFFFF"),
                    LatestColor = NormalizeColorHex(_configManager.LiveCaptionLatestTextColor, "#FFFF00"),
                    ShowProjectionLayout = true,
                    ProjectionOrientation = NormalizeProjectionCaptionOrientation(_configManager.LiveCaptionProjectionOrientation),
                    ProjectionHorizontalAnchor = NormalizeProjectionCaptionHorizontalAnchor(_configManager.LiveCaptionProjectionHorizontalAnchor),
                    ProjectionVerticalAnchor = NormalizeProjectionCaptionVerticalAnchor(_configManager.LiveCaptionProjectionVerticalAnchor),
                },
                setFontFamily: family => { _configManager.LiveCaptionFontFamily = family; ApplyLiveCaptionTypographyFromBible(); },
                setFontSize: SetLiveCaptionFontSize,
                setLetterSpacing: SetLiveCaptionLetterSpacing,
                setLineGapLevel: SetLiveCaptionLineGap,
                setTextColor: hex => { _configManager.LiveCaptionTextColor = NormalizeColorHex(hex, "#FFFFFF"); ApplyLiveCaptionTypographyFromBible(); },
                setLatestColor: hex => { _configManager.LiveCaptionLatestTextColor = NormalizeColorHex(hex, "#FFFF00"); ApplyLiveCaptionTypographyFromBible(); },
                setProjectionOrientation: value => SetProjectionCaptionOrientation(value, value == "vertical" ? "竖向" : "横向"),
                setProjectionHorizontalAnchor: value => SetProjectionCaptionHorizontalAnchor(value, GetProjectionCaptionHorizontalAnchorDisplayName(value)),
                setProjectionVerticalAnchor: value => SetProjectionCaptionVerticalAnchor(value, GetProjectionCaptionVerticalAnchorDisplayName(value)));
        }

        private LiveCaptionStyleControlCenterWindow.Profile CreateLocalCaptionStyleProfile()
        {
            return new LiveCaptionStyleControlCenterWindow.Profile(
                key: "local",
                label: "本机",
                title: "AI字幕 · 本机样式",
                subtitle: "主屏统一文本框：设置字体、字号和文字颜色。",
                loadState: () => new LiveCaptionStyleControlCenterWindow.State
                {
                    FontFamily = string.IsNullOrWhiteSpace(_configManager.LiveCaptionLocalFontFamily)
                        ? (string.IsNullOrWhiteSpace(_configManager.LiveCaptionFontFamily)
                            ? (string.IsNullOrWhiteSpace(_configManager.BibleFontFamily) ? "Microsoft YaHei UI" : _configManager.BibleFontFamily.Trim())
                            : _configManager.LiveCaptionFontFamily.Trim())
                        : _configManager.LiveCaptionLocalFontFamily.Trim(),
                    FontSize = Math.Clamp(_configManager.LiveCaptionLocalFontSize > 0 ? _configManager.LiveCaptionLocalFontSize : (_configManager.LiveCaptionFontSize > 0 ? _configManager.LiveCaptionFontSize : 36), 20, 36),
                    FontSizeMaximum = 36,
                    TextColor = NormalizeColorHex(_configManager.LiveCaptionLocalTextColor, NormalizeColorHex(_configManager.LiveCaptionTextColor, "#FFFFFF")),
                },
                setFontFamily: family => { _configManager.LiveCaptionLocalFontFamily = family; ApplyLiveCaptionTypographyFromBible(); },
                setFontSize: SetLocalCaptionFontSize,
                setLetterSpacing: null,
                setLineGapLevel: null,
                setTextColor: hex => { _configManager.LiveCaptionLocalTextColor = NormalizeColorHex(hex, "#FFFFFF"); ApplyLiveCaptionTypographyFromBible(); },
                setLatestColor: null);
        }

        private LiveCaptionStyleControlCenterWindow.Profile CreateNdiCaptionStyleProfile()
        {
            return new LiveCaptionStyleControlCenterWindow.Profile(
                key: "ndi",
                label: "NDI",
                title: "AI字幕 · NDI样式",
                subtitle: "控制台模式：滑条 / 数值，步长 0.5。",
                loadState: () => new LiveCaptionStyleControlCenterWindow.State
                {
                    FontFamily = string.IsNullOrWhiteSpace(_configManager.LiveCaptionNdiFontFamily)
                        ? (string.IsNullOrWhiteSpace(_configManager.LiveCaptionFontFamily)
                            ? (string.IsNullOrWhiteSpace(_configManager.BibleFontFamily) ? "Microsoft YaHei UI" : _configManager.BibleFontFamily.Trim())
                            : _configManager.LiveCaptionFontFamily.Trim())
                        : _configManager.LiveCaptionNdiFontFamily.Trim(),
                    FontSize = CreateLiveCaptionNdiRenderOptions().FontSize,
                    LetterSpacing = Math.Clamp(_configManager.LiveCaptionNdiLetterSpacing, 0, 10),
                    LineGapLevel = LineGapToLevel(_configManager.LiveCaptionNdiLineGap > 0 ? _configManager.LiveCaptionNdiLineGap : _configManager.LiveCaptionLineGap),
                    TextColor = NormalizeColorHex(_configManager.LiveCaptionNdiTextColor, NormalizeColorHex(_configManager.LiveCaptionTextColor, "#FFFFFF")),
                    LatestColor = NormalizeColorHex(_configManager.LiveCaptionNdiLatestTextColor, NormalizeColorHex(_configManager.LiveCaptionLatestTextColor, "#FFFF00")),
                    ShowNdiAdvanced = true,
                    NdiLineCharLimit = Math.Clamp(_configManager.LiveCaptionNdiLineCharLimit, 8, 80),
                    NdiAlignment = NormalizeNdiAlignment(_configManager.LiveCaptionNdiTextAlignment),
                },
                setFontFamily: family => { _configManager.LiveCaptionNdiFontFamily = family; RefreshLiveCaptionNdiPreview(); },
                setFontSize: SetNdiFontSize,
                setLetterSpacing: SetNdiLetterSpacing,
                setLineGapLevel: SetNdiLineGap,
                setTextColor: hex => { _configManager.LiveCaptionNdiTextColor = NormalizeColorHex(hex, "#FFFFFF"); RefreshLiveCaptionNdiPreview(); },
                setLatestColor: hex => { _configManager.LiveCaptionNdiLatestTextColor = NormalizeColorHex(hex, "#FFFF00"); RefreshLiveCaptionNdiPreview(); },
                setNdiLineCharLimit: value => SetLiveCaptionNdiChars((int)Math.Round(value)),
                setNdiAlignment: value => SetNdiAlignment(value, value switch { "left" => "左对齐", "right" => "右对齐", _ => "居中" }));
        }

        private void RestoreAndTrackLiveCaptionStyleWindow(Window window, string placementKey)
        {
            if (window == null || _configManager == null)
            {
                return;
            }

            WindowPlacementTracker.Restore(window, _configManager, placementKey);
            WindowPlacementTracker.Track(window, _configManager, placementKey);
        }

        private void PopulateNdiStyleMenu(MenuItem root)
        {
            var fontMenu = new MenuItem { Header = "字体" };
            PopulateNdiFontMenu(fontMenu);
            root.Items.Add(fontMenu);

            var fontSizeMenu = new MenuItem { Header = "字号" };
            PopulateNdiFontSizeMenu(fontSizeMenu);
            root.Items.Add(fontSizeMenu);

            var letterSpacingMenu = new MenuItem { Header = "字间距" };
            PopulateNdiLetterSpacingMenu(letterSpacingMenu);
            root.Items.Add(letterSpacingMenu);

            var lineGapMenu = new MenuItem { Header = "段间距" };
            PopulateNdiLineGapMenu(lineGapMenu);
            root.Items.Add(lineGapMenu);

            var alignmentMenu = new MenuItem { Header = "对齐" };
            PopulateNdiAlignmentMenu(alignmentMenu);
            root.Items.Add(alignmentMenu);

            var ndiCharsMenu = new MenuItem { Header = "字数" };
            PopulateLiveCaptionNdiCharsMenu(ndiCharsMenu);
            root.Items.Add(ndiCharsMenu);

            root.Items.Add(new Separator());

            var textColorMenu = new MenuItem { Header = "字幕颜色" };
            PopulateNdiTextColorMenu(textColorMenu);
            root.Items.Add(textColorMenu);

            var latestColorMenu = new MenuItem { Header = "最新字颜色" };
            PopulateNdiLatestColorMenu(latestColorMenu);
            root.Items.Add(latestColorMenu);
        }

        private void PopulateLiveCaptionFontMenu(MenuItem root)
        {
            string current = string.IsNullOrWhiteSpace(_configManager?.LiveCaptionFontFamily)
                ? (string.IsNullOrWhiteSpace(_configManager?.BibleFontFamily) ? "Microsoft YaHei UI" : _configManager.BibleFontFamily.Trim())
                : _configManager.LiveCaptionFontFamily.Trim();
            AddLiveCaptionFontItem(root, "微软雅黑", "Microsoft YaHei UI", current);
            AddLiveCaptionFontItem(root, "等线", "DengXian", current);
            AddLiveCaptionFontItem(root, "黑体", "SimHei", current);
            AddLiveCaptionFontItem(root, "宋体", "SimSun", current);
        }

        private void AddLiveCaptionFontItem(MenuItem root, string display, string family, string current)
        {
            var item = new MenuItem
            {
                Header = BuildSelectedMenuHeader(display, string.Equals(current, family, StringComparison.OrdinalIgnoreCase))
            };
            item.Click += (_, _) =>
            {
                if (_configManager == null)
                {
                    return;
                }

                _configManager.LiveCaptionFontFamily = family;
                ApplyLiveCaptionTypographyFromBible();
                ShowStatus($"投影字幕字体：{display}");
            };
            root.Items.Add(item);
        }

        private void PopulateLiveCaptionFontSizeMenu(MenuItem root)
        {
            int current = (int)Math.Round(Math.Clamp((_configManager?.LiveCaptionFontSize > 0 ? _configManager.LiveCaptionFontSize : (_configManager?.BibleFontSize ?? 36)), 20, 112));
            int[] sizes = { 20, 24, 28, 32, 36, 40, 46, 52, 60, 68, 76, 88, 100, 112 };
            foreach (int size in sizes)
            {
                AddLiveCaptionNumericItem(root, size.ToString(), current == size, () => SetLiveCaptionFontSize(size));
            }
        }

        private void SetLiveCaptionFontSize(double value)
        {
            if (_configManager == null)
            {
                return;
            }

            _configManager.LiveCaptionFontSize = Math.Clamp(value, 20, 112);
            ApplyLiveCaptionTypographyFromBible();
            ShowStatus($"投影字幕字号：{value:0}");
        }

        private void PopulateLiveCaptionLetterSpacingMenu(MenuItem root)
        {
            int current = (int)Math.Round(Math.Clamp(_configManager?.LiveCaptionLetterSpacing ?? 0, 0, 10));
            for (int value = 0; value <= 10; value++)
            {
                int selected = value;
                AddLiveCaptionNumericItem(root, value.ToString(), current == value, () => SetLiveCaptionLetterSpacing(selected));
            }
        }

        private void SetLiveCaptionLetterSpacing(double value)
        {
            if (_configManager == null)
            {
                return;
            }

            _configManager.LiveCaptionLetterSpacing = Math.Clamp(value, 0, 10);
            ApplyLiveCaptionTypographyFromBible();
            ShowStatus($"投影字幕字间距：{value:0}");
        }

        private void PopulateLiveCaptionLineGapMenu(MenuItem root)
        {
            int currentLevel = LineGapToLevel(_configManager?.LiveCaptionLineGap ?? 10);
            for (int level = 0; level <= 10; level++)
            {
                int selectedLevel = level;
                AddLiveCaptionNumericItem(root, level.ToString(), currentLevel == level, () => SetLiveCaptionLineGap(selectedLevel));
            }
        }

        private void SetLiveCaptionLineGap(double value)
        {
            if (_configManager == null)
            {
                return;
            }

            int level = (int)Math.Round(Math.Clamp(value, 0, 10));
            double actualGap = LevelToLineGap(level);
            _configManager.LiveCaptionLineGap = actualGap;
            ApplyLiveCaptionTypographyFromBible();
            ShowStatus($"投影字幕段间距：{level}");
        }

        private static int LineGapToLevel(double lineGap)
        {
            double normalized = (Math.Clamp(lineGap, 10, 60) - 10) / 5.0;
            return (int)Math.Round(normalized);
        }

        private static double LevelToLineGap(int level)
        {
            return 10 + (Math.Clamp(level, 0, 10) * 5);
        }

        private void PopulateLocalCaptionFontMenu(MenuItem root)
        {
            string current = string.IsNullOrWhiteSpace(_configManager?.LiveCaptionLocalFontFamily)
                ? (string.IsNullOrWhiteSpace(_configManager?.LiveCaptionFontFamily)
                    ? (string.IsNullOrWhiteSpace(_configManager?.BibleFontFamily) ? "Microsoft YaHei UI" : _configManager.BibleFontFamily.Trim())
                    : _configManager.LiveCaptionFontFamily.Trim())
                : _configManager.LiveCaptionLocalFontFamily.Trim();
            AddLocalCaptionFontItem(root, "微软雅黑", "Microsoft YaHei UI", current);
            AddLocalCaptionFontItem(root, "等线", "DengXian", current);
            AddLocalCaptionFontItem(root, "黑体", "SimHei", current);
            AddLocalCaptionFontItem(root, "宋体", "SimSun", current);
        }

        private void AddLocalCaptionFontItem(MenuItem root, string display, string family, string current)
        {
            var item = new MenuItem
            {
                Header = BuildSelectedMenuHeader(display, string.Equals(current, family, StringComparison.OrdinalIgnoreCase))
            };
            item.Click += (_, _) =>
            {
                if (_configManager == null) return;
                _configManager.LiveCaptionLocalFontFamily = family;
                ApplyLiveCaptionTypographyFromBible();
                ShowStatus($"本机字幕字体：{display}");
            };
            root.Items.Add(item);
        }

        private void PopulateLocalCaptionFontSizeMenu(MenuItem root)
        {
            int current = (int)Math.Round(Math.Clamp((_configManager?.LiveCaptionLocalFontSize > 0 ? _configManager.LiveCaptionLocalFontSize : (_configManager?.LiveCaptionFontSize > 0 ? _configManager.LiveCaptionFontSize : (_configManager?.BibleFontSize ?? 36))), 20, 36));
            int[] sizes = { 20, 24, 28, 32, 36 };
            foreach (int size in sizes)
            {
                int selected = size;
                AddLiveCaptionNumericItem(root, size.ToString(), current == size, () => SetLocalCaptionFontSize(selected));
            }
        }

        private void SetLocalCaptionFontSize(double value)
        {
            if (_configManager == null) return;
            _configManager.LiveCaptionLocalFontSize = Math.Clamp(value, 20, 36);
            ApplyLiveCaptionTypographyFromBible();
            ShowStatus($"本机字幕字号：{_configManager.LiveCaptionLocalFontSize:0.#}");
        }

        private void PopulateLocalCaptionTextColorMenu(MenuItem root)
        {
            string current = NormalizeColorHex(_configManager?.LiveCaptionLocalTextColor, NormalizeColorHex(_configManager?.LiveCaptionTextColor, "#FFFFFF"));
            PopulateColorMenuFromPresets(
                root,
                current,
                (hex, name) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionLocalTextColor = hex;
                    ApplyLiveCaptionTypographyFromBible();
                    ShowStatus($"本机字幕颜色：{name}");
                });
            AddCustomColorPickerMenuItem(
                root,
                "自定义颜色...",
                current,
                (hex) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionLocalTextColor = hex;
                    ApplyLiveCaptionTypographyFromBible();
                    ShowStatus($"本机字幕颜色：{hex}");
                });
        }

        private void PopulateLiveCaptionNdiCharsMenu(MenuItem root)
        {
            int current = Math.Clamp(_configManager?.LiveCaptionNdiLineCharLimit ?? 30, 8, 80);
            int[] presets = { 10, 15, 20, 25 };
            foreach (int value in presets)
            {
                int selected = value;
                AddLiveCaptionNumericItem(root, value.ToString(), current == value, () => SetLiveCaptionNdiChars(selected));
            }
        }

        private void SetLiveCaptionNdiChars(int value)
        {
            if (_configManager == null)
            {
                return;
            }

            _configManager.LiveCaptionNdiLineCharLimit = Math.Clamp(value, 8, 80);
            OnCaptionTextChanged(_liveCaptionOverlayWindow?.CaptionText);
            ShowStatus($"NDI字数：{_configManager.LiveCaptionNdiLineCharLimit}");
        }

        private void PopulateNdiFontMenu(MenuItem root)
        {
            string current = string.IsNullOrWhiteSpace(_configManager?.LiveCaptionNdiFontFamily)
                ? (string.IsNullOrWhiteSpace(_configManager?.LiveCaptionFontFamily)
                    ? (string.IsNullOrWhiteSpace(_configManager?.BibleFontFamily) ? "Microsoft YaHei UI" : _configManager.BibleFontFamily.Trim())
                    : _configManager.LiveCaptionFontFamily.Trim())
                : _configManager.LiveCaptionNdiFontFamily.Trim();
            AddNdiFontItem(root, "微软雅黑", "Microsoft YaHei UI", current);
            AddNdiFontItem(root, "等线", "DengXian", current);
            AddNdiFontItem(root, "黑体", "SimHei", current);
            AddNdiFontItem(root, "宋体", "SimSun", current);
        }

        private void AddNdiFontItem(MenuItem root, string display, string family, string current)
        {
            var item = new MenuItem
            {
                Header = BuildSelectedMenuHeader(display, string.Equals(current, family, StringComparison.OrdinalIgnoreCase))
            };
            item.Click += (_, _) =>
            {
                if (_configManager == null) return;
                _configManager.LiveCaptionNdiFontFamily = family;
                RefreshLiveCaptionNdiPreview();
                ShowStatus($"NDI字体：{display}");
            };
            root.Items.Add(item);
        }

        private void PopulateNdiFontSizeMenu(MenuItem root)
        {
            int current = (int)Math.Round(CreateLiveCaptionNdiRenderOptions().FontSize);
            int[] sizes = { 20, 24, 28, 32, 36, 40, 46, 52, 60, 68, 76, 88, 100, 112 };
            foreach (int size in sizes)
            {
                int selected = size;
                AddLiveCaptionNumericItem(root, size.ToString(), current == size, () => SetNdiFontSize(selected));
            }
        }

        private void SetNdiFontSize(double value)
        {
            if (_configManager == null) return;
            _configManager.LiveCaptionNdiFontSize = Math.Clamp(value, 20, 112);
            RefreshLiveCaptionNdiPreview();
            ShowStatus($"NDI字号：{value:0}");
        }

        private void PopulateNdiLetterSpacingMenu(MenuItem root)
        {
            int current = (int)Math.Round(Math.Clamp(_configManager?.LiveCaptionNdiLetterSpacing ?? _configManager?.LiveCaptionLetterSpacing ?? 0, 0, 10));
            for (int value = 0; value <= 10; value++)
            {
                int selected = value;
                AddLiveCaptionNumericItem(root, value.ToString(), current == value, () => SetNdiLetterSpacing(selected));
            }
        }

        private void SetNdiLetterSpacing(double value)
        {
            if (_configManager == null) return;
            _configManager.LiveCaptionNdiLetterSpacing = Math.Clamp(value, 0, 10);
            RefreshLiveCaptionNdiPreview();
            ShowStatus($"NDI字间距：{value:0}");
        }

        private void PopulateNdiLineGapMenu(MenuItem root)
        {
            int currentLevel = LineGapToLevel(_configManager?.LiveCaptionNdiLineGap ?? _configManager?.LiveCaptionLineGap ?? 10);
            for (int level = 0; level <= 10; level++)
            {
                int selected = level;
                AddLiveCaptionNumericItem(root, level.ToString(), currentLevel == level, () => SetNdiLineGap(selected));
            }
        }

        private void SetNdiLineGap(double value)
        {
            if (_configManager == null) return;
            int level = (int)Math.Round(Math.Clamp(value, 0, 10));
            _configManager.LiveCaptionNdiLineGap = LevelToLineGap(level);
            RefreshLiveCaptionNdiPreview();
            ShowStatus($"NDI段间距：{level}");
        }

        private void PopulateNdiAlignmentMenu(MenuItem root)
        {
            string current = NormalizeNdiAlignment(_configManager?.LiveCaptionNdiTextAlignment);
            AddLiveCaptionNumericItem(root, "左", string.Equals(current, "left", StringComparison.Ordinal), () => SetNdiAlignment("left", "左对齐"));
            AddLiveCaptionNumericItem(root, "中", string.Equals(current, "center", StringComparison.Ordinal), () => SetNdiAlignment("center", "居中"));
            AddLiveCaptionNumericItem(root, "右", string.Equals(current, "right", StringComparison.Ordinal), () => SetNdiAlignment("right", "右对齐"));
        }

        private void SetNdiAlignment(string value, string display)
        {
            if (_configManager == null) return;
            _configManager.LiveCaptionNdiTextAlignment = NormalizeNdiAlignment(value);
            RefreshLiveCaptionNdiPreview();
            ShowStatus($"NDI对齐：{display}");
        }

        private static string NormalizeNdiAlignment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "center";
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "left" => "left",
                "right" => "right",
                _ => "center"
            };
        }

        private void PopulateNdiTextColorMenu(MenuItem root)
        {
            string current = NormalizeColorHex(_configManager?.LiveCaptionNdiTextColor, NormalizeColorHex(_configManager?.LiveCaptionTextColor, "#FFFFFF"));
            PopulateColorMenuFromPresets(
                root,
                current,
                (hex, name) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionNdiTextColor = hex;
                    RefreshLiveCaptionNdiPreview();
                    ShowStatus($"NDI字幕颜色：{name}");
                });
            AddCustomColorPickerMenuItem(
                root,
                "自定义颜色...",
                current,
                (hex) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionNdiTextColor = hex;
                    RefreshLiveCaptionNdiPreview();
                    ShowStatus($"NDI字幕颜色：{hex}");
                });
        }

        private void PopulateNdiLatestColorMenu(MenuItem root)
        {
            string current = NormalizeColorHex(_configManager?.LiveCaptionNdiLatestTextColor, NormalizeColorHex(_configManager?.LiveCaptionLatestTextColor, "#FFFF00"));
            PopulateColorMenuFromPresets(
                root,
                current,
                (hex, name) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionNdiLatestTextColor = hex;
                    RefreshLiveCaptionNdiPreview();
                    ShowStatus($"NDI最新字颜色：{name}");
                });
            AddCustomColorPickerMenuItem(
                root,
                "自定义颜色...",
                current,
                (hex) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionNdiLatestTextColor = hex;
                    RefreshLiveCaptionNdiPreview();
                    ShowStatus($"NDI最新字颜色：{hex}");
                });
        }

        private void RefreshLiveCaptionNdiPreview()
        {
            if (_configManager?.LiveCaptionNdiEnabled != true)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_liveCaptionNdiComposer.CurrentDisplay))
            {
                return;
            }

            UpdateLiveCaptionNdiCaption(_liveCaptionNdiComposer.CurrentDisplay, _liveCaptionNdiComposer.CurrentHighlightStart);
        }

        private static void AddLiveCaptionNumericItem(MenuItem root, string text, bool selected, Action apply)
        {
            var item = new MenuItem
            {
                Header = BuildSelectedMenuHeader(text, selected)
            };
            item.Click += (_, _) => apply?.Invoke();
            root.Items.Add(item);
        }

        private void OnLiveCaptionOverlayCaptionOrientationRequested(ProjectionCaptionOrientation orientation)
        {
            SetProjectionCaptionOrientation(
                orientation == ProjectionCaptionOrientation.Vertical ? "vertical" : "horizontal",
                orientation == ProjectionCaptionOrientation.Vertical ? "竖向" : "横向");
        }

        private void OnLiveCaptionOverlayCaptionPositionRequested(
            ProjectionCaptionHorizontalAnchor horizontalAnchor,
            ProjectionCaptionVerticalAnchor verticalAnchor)
        {
            string orientation = NormalizeProjectionCaptionOrientation(_configManager?.LiveCaptionProjectionOrientation);
            string horizontal = horizontalAnchor switch
            {
                ProjectionCaptionHorizontalAnchor.Left => "left",
                ProjectionCaptionHorizontalAnchor.Right => "right",
                _ => "center"
            };
            string vertical = verticalAnchor switch
            {
                ProjectionCaptionVerticalAnchor.Top => "top",
                ProjectionCaptionVerticalAnchor.Bottom => "bottom",
                _ => "center"
            };

            if (orientation == "vertical")
            {
                SetProjectionCaptionHorizontalAnchor(horizontal, GetProjectionCaptionHorizontalAnchorDisplayName(horizontal));
                return;
            }

            SetProjectionCaptionVerticalAnchor(vertical, GetProjectionCaptionVerticalAnchorDisplayName(vertical));
        }

        private void SetProjectionCaptionOrientation(string value, string displayName)
        {
            if (_configManager == null)
            {
                return;
            }

            string next = NormalizeProjectionCaptionOrientation(value);
            if (string.Equals(NormalizeProjectionCaptionOrientation(_configManager.LiveCaptionProjectionOrientation), next, StringComparison.Ordinal))
            {
                return;
            }

            _configManager.LiveCaptionProjectionOrientation = next;
            if (next == "vertical")
            {
                // 竖向只看左右位置，垂直轴固定居中。
                _configManager.LiveCaptionProjectionVerticalAnchor = "center";
            }
            else
            {
                // 横向只看上下位置，水平轴固定居中，保证吐字逻辑一致。
                _configManager.LiveCaptionProjectionHorizontalAnchor = "center";
            }
            ApplyProjectionCaptionLayoutFromConfig();
            ShowStatus($"投影字幕框已切换为{displayName}");
        }

        private void SetProjectionCaptionHorizontalAnchor(string value, string displayName)
        {
            if (_configManager == null)
            {
                return;
            }

            string next = NormalizeProjectionCaptionHorizontalAnchor(value);
            if (string.Equals(NormalizeProjectionCaptionHorizontalAnchor(_configManager.LiveCaptionProjectionHorizontalAnchor), next, StringComparison.Ordinal))
            {
                return;
            }

            _configManager.LiveCaptionProjectionHorizontalAnchor = next;
            ApplyProjectionCaptionLayoutFromConfig();
            ShowStatus($"投影字幕框水平位置：{displayName}");
        }

        private void SetProjectionCaptionVerticalAnchor(string value, string displayName)
        {
            if (_configManager == null)
            {
                return;
            }

            string next = NormalizeProjectionCaptionVerticalAnchor(value);
            if (string.Equals(NormalizeProjectionCaptionVerticalAnchor(_configManager.LiveCaptionProjectionVerticalAnchor), next, StringComparison.Ordinal))
            {
                return;
            }

            _configManager.LiveCaptionProjectionVerticalAnchor = next;
            ApplyProjectionCaptionLayoutFromConfig();
            ShowStatus($"投影字幕框垂直位置：{displayName}");
        }

        private void ApplyProjectionCaptionLayoutFromConfig()
        {
            if (_projectionManager == null || _configManager == null)
            {
                return;
            }

            ProjectionCaptionOrientation orientation = ParseProjectionCaptionOrientation(_configManager.LiveCaptionProjectionOrientation);
            ProjectionCaptionHorizontalAnchor horizontalAnchor = ParseProjectionCaptionHorizontalAnchor(_configManager.LiveCaptionProjectionHorizontalAnchor);
            ProjectionCaptionVerticalAnchor verticalAnchor = ParseProjectionCaptionVerticalAnchor(_configManager.LiveCaptionProjectionVerticalAnchor);
            if (orientation == ProjectionCaptionOrientation.Vertical)
            {
                verticalAnchor = ProjectionCaptionVerticalAnchor.Center;
            }
            else
            {
                horizontalAnchor = ProjectionCaptionHorizontalAnchor.Center;
            }
            _projectionManager.SetProjectionCaptionLayout(orientation, horizontalAnchor, verticalAnchor);
            _liveCaptionOverlayWindow?.SetCaptionOrientationState(orientation);
            _liveCaptionOverlayWindow?.SetCaptionPositionState(horizontalAnchor, verticalAnchor);
            ApplyLiveCaptionTypographyFromBible();
        }

        private static string NormalizeProjectionCaptionOrientation(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "vertical" ? "vertical" : "horizontal";
        }

        private static string NormalizeProjectionCaptionHorizontalAnchor(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "left" => "left",
                "right" => "right",
                _ => "center"
            };
        }

        private static string GetProjectionCaptionHorizontalAnchorDisplayName(string value)
        {
            return NormalizeProjectionCaptionHorizontalAnchor(value) switch
            {
                "left" => "靠左",
                "right" => "靠右",
                _ => "中间"
            };
        }

        private static string NormalizeProjectionCaptionVerticalAnchor(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "top" => "top",
                "bottom" => "bottom",
                _ => "center"
            };
        }

        private static string GetProjectionCaptionVerticalAnchorDisplayName(string value)
        {
            return NormalizeProjectionCaptionVerticalAnchor(value) switch
            {
                "top" => "顶部",
                "bottom" => "底部",
                _ => "居中"
            };
        }

        private static ProjectionCaptionOrientation ParseProjectionCaptionOrientation(string value)
        {
            return NormalizeProjectionCaptionOrientation(value) == "vertical"
                ? ProjectionCaptionOrientation.Vertical
                : ProjectionCaptionOrientation.Horizontal;
        }

        private static ProjectionCaptionHorizontalAnchor ParseProjectionCaptionHorizontalAnchor(string value)
        {
            return NormalizeProjectionCaptionHorizontalAnchor(value) switch
            {
                "left" => ProjectionCaptionHorizontalAnchor.Left,
                "right" => ProjectionCaptionHorizontalAnchor.Right,
                _ => ProjectionCaptionHorizontalAnchor.Center
            };
        }

        private static ProjectionCaptionVerticalAnchor ParseProjectionCaptionVerticalAnchor(string value)
        {
            return NormalizeProjectionCaptionVerticalAnchor(value) switch
            {
                "top" => ProjectionCaptionVerticalAnchor.Top,
                "bottom" => ProjectionCaptionVerticalAnchor.Bottom,
                _ => ProjectionCaptionVerticalAnchor.Center
            };
        }

        private void ToggleProjectionFromLiveCaptionOverlay()
        {
            try
            {
                bool nextHidden = !_liveCaptionProjectionCaptionHidden;
                if (!nextHidden &&
                    !TryRequirePremiumFeature(Services.Licensing.PremiumFeature.LiveCaption))
                {
                    _liveCaptionProjectionCaptionHidden = true;
                    UpdateLiveCaptionProjectionActionState();
                    _projectionManager?.HideProjectionCaptionOverlay();
                    return;
                }

                _liveCaptionProjectionCaptionHidden = !_liveCaptionProjectionCaptionHidden;
                UpdateLiveCaptionProjectionActionState();
                if (_liveCaptionProjectionCaptionHidden)
                {
                    _projectionManager?.HideProjectionCaptionOverlay();
                    ShowStatus("投影字幕已隐藏");
                }
                else
                {
                    SyncLiveCaptionProjectionCaptionForProjectionState(_projectionManager?.IsProjectionActive == true);
                    ShowStatus("投影字幕已显示");
                }

                LiveCaptionDebugLogger.Log($"ProjectionToggle: overlay-click hidden={_liveCaptionProjectionCaptionHidden}");
            }
            catch (Exception ex)
            {
                LiveCaptionDebugLogger.Log($"ProjectionToggle: overlay-click failed, error={ex.Message}");
            }
        }

        private void UpdateLiveCaptionProjectionActionState()
        {
            _liveCaptionOverlayWindow?.SetProjectionToggleState(_liveCaptionProjectionCaptionHidden);
        }

        private bool IsLiveCaptionNdiEnabled()
        {
            return _configManager?.LiveCaptionNdiEnabled == true;
        }

        private void UpdateLiveCaptionNdiActionState()
        {
            _liveCaptionOverlayWindow?.SetNdiToggleState(IsLiveCaptionNdiEnabled());
        }

        private void ToggleNdiFromLiveCaptionOverlay()
        {
            try
            {
                SetLiveCaptionNdiEnabled(!IsLiveCaptionNdiEnabled());
            }
            catch (Exception ex)
            {
                LiveCaptionDebugLogger.Log($"NdiToggle: failed, error={ex.Message}");
                ShowStatus($"字幕NDI切换失败：{ex.Message}");
            }
        }

        private void UpdateLiveCaptionProjectionCaption(string captionText, int? highlightStart = null)
        {
            string next = captionText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(next))
            {
                _projectionManager?.HideProjectionCaptionOverlay();
                return;
            }

            if (!IsPremiumFeatureAllowed(Services.Licensing.PremiumFeature.LiveCaption))
            {
                _projectionManager?.HideProjectionCaptionOverlay();
                return;
            }

            if (_projectionManager?.IsProjectionActive != true)
            {
                return;
            }

            if (_liveCaptionProjectionCaptionHidden)
            {
                _projectionManager.HideProjectionCaptionOverlay();
                return;
            }

            _projectionManager.UpdateProjectionCaptionOverlay(next, highlightStart);
        }

        private void UpdateLiveCaptionNdiCaption(string captionText, int highlightStart)
        {
            PublishLiveCaptionToNdi(captionText, highlightStart);
        }

        internal void SyncLiveCaptionProjectionCaptionForProjectionState(bool isProjectionActive)
        {
            if (!IsPremiumFeatureAllowed(Services.Licensing.PremiumFeature.LiveCaption))
            {
                _projectionManager?.HideProjectionCaptionOverlay();
                return;
            }

            if (!isProjectionActive)
            {
                _projectionManager?.HideProjectionCaptionOverlay();
                return;
            }

            if (IsLiveCaptionDisplayActive() &&
                !_liveCaptionProjectionCaptionHidden &&
                !string.IsNullOrWhiteSpace(_liveCaptionComposer.CurrentDisplay))
            {
                _projectionManager?.UpdateProjectionCaptionOverlay(
                    _liveCaptionComposer.CurrentDisplay,
                    _liveCaptionComposer.CurrentHighlightStart);
                return;
            }

            _projectionManager?.HideProjectionCaptionOverlay();
        }

        private void PopulateLiveCaptionPlatformMenu(MenuItem platformMenu)
        {
            string provider = NormalizeLiveCaptionProvider(_configManager?.LiveCaptionAsrProvider);

            var baiduItem = new MenuItem
            {
                Header = BuildSelectedMenuHeader("百度", string.Equals(provider, "baidu", StringComparison.OrdinalIgnoreCase))
            };
            baiduItem.Click += (_, _) => SetLiveCaptionPlatform("baidu", "百度");
            platformMenu.Items.Add(baiduItem);

            var xfyunItem = new MenuItem
            {
                Header = BuildSelectedMenuHeader("飞讯语音", string.Equals(provider, "xfyun", StringComparison.OrdinalIgnoreCase))
            };
            xfyunItem.Click += (_, _) => SetLiveCaptionPlatform("xfyun", "飞讯语音");
            platformMenu.Items.Add(xfyunItem);

            var doubaoItem = new MenuItem
            {
                Header = BuildSelectedMenuHeader("豆包", string.Equals(provider, "doubao", StringComparison.OrdinalIgnoreCase))
            };
            doubaoItem.Click += (_, _) => SetLiveCaptionPlatform("doubao", "豆包");
            platformMenu.Items.Add(doubaoItem);

        }

        private static string BuildSelectedMenuHeader(string text, bool selected)
        {
            return selected ? $"{text}  ✓" : text;
        }

        private void PopulateLiveCaptionLatestColorMenu(MenuItem root)
        {
            string current = NormalizeColorHex(_configManager?.LiveCaptionLatestTextColor, "#FFFF00");
            PopulateColorMenuFromPresets(
                root,
                current,
                (hex, name) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionLatestTextColor = hex;
                    ApplyLiveCaptionTypographyFromBible();
                    ShowStatus($"投影最新字颜色：{name}");
                });
            AddCustomColorPickerMenuItem(
                root,
                "自定义颜色...",
                current,
                (hex) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionLatestTextColor = hex;
                    ApplyLiveCaptionTypographyFromBible();
                    ShowStatus($"投影最新字颜色：{hex}");
                });
        }

        private void PopulateLiveCaptionTextColorMenu(MenuItem root)
        {
            string current = NormalizeColorHex(_configManager?.LiveCaptionTextColor, "#FFFFFF");
            PopulateColorMenuFromPresets(
                root,
                current,
                (hex, name) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionTextColor = hex;
                    ApplyLiveCaptionTypographyFromBible();
                    ShowStatus($"投影字幕颜色：{name}");
                });
            AddCustomColorPickerMenuItem(
                root,
                "自定义颜色...",
                current,
                (hex) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionTextColor = hex;
                    ApplyLiveCaptionTypographyFromBible();
                    ShowStatus($"投影字幕颜色：{hex}");
                });
        }

        private void ApplyLiveCaptionTypographyFromBible()
        {
            if (_configManager == null)
            {
                return;
            }

            string projectionFontFamily = string.IsNullOrWhiteSpace(_configManager.LiveCaptionFontFamily)
                ? (string.IsNullOrWhiteSpace(_configManager.BibleFontFamily) ? "Microsoft YaHei UI" : _configManager.BibleFontFamily.Trim())
                : _configManager.LiveCaptionFontFamily.Trim();
            double projectionFontSize = Math.Clamp(_configManager.LiveCaptionFontSize > 0 ? _configManager.LiveCaptionFontSize : _configManager.BibleFontSize, 20, 112);
            double projectionPadding = Math.Clamp(_configManager.LiveCaptionPadding > 0 ? _configManager.LiveCaptionPadding : _configManager.BibleMargin, 6, 120);
            double projectionLetterSpacing = Math.Clamp(_configManager.LiveCaptionLetterSpacing, 0, 10);
            double projectionLineGap = Math.Clamp(_configManager.LiveCaptionLineGap, 10, 60);
            double projectionLineHeight = projectionFontSize + projectionLineGap;
            string projectionTextColor = NormalizeColorHex(_configManager.LiveCaptionTextColor, "#FFFFFF");
            string projectionLatestColor = NormalizeColorHex(_configManager.LiveCaptionLatestTextColor, "#FFFF00");

            string localFontFamily = string.IsNullOrWhiteSpace(_configManager.LiveCaptionLocalFontFamily)
                ? projectionFontFamily
                : _configManager.LiveCaptionLocalFontFamily.Trim();
            double localFontSize = Math.Clamp(_configManager.LiveCaptionLocalFontSize > 0 ? _configManager.LiveCaptionLocalFontSize : projectionFontSize, 20, 112);
            double localPadding = Math.Clamp(_configManager.LiveCaptionLocalPadding > 0 ? _configManager.LiveCaptionLocalPadding : projectionPadding, 6, 120);
            string localTextColor = NormalizeColorHex(_configManager.LiveCaptionLocalTextColor, projectionTextColor);

            _liveCaptionOverlayWindow?.SetCaptionTypography(localFontFamily, localFontSize, localPadding);
            _liveCaptionOverlayWindow?.SetCaptionTextColor(localTextColor);
            _projectionManager?.SetProjectionCaptionTypography(projectionFontFamily, projectionFontSize, projectionPadding, projectionLineHeight, projectionLetterSpacing, projectionTextColor, projectionLatestColor);
            RecalculateLiveCaptionComposerLayout();

            OnCaptionTextChanged(_liveCaptionOverlayWindow?.CaptionText);

            // 样式改动必须立即可见：强制用当前显示文本重绘一次，不依赖下一条 ASR 更新。
            if (!string.IsNullOrWhiteSpace(_liveCaptionComposer.CurrentDisplay))
            {
                if (_projectionManager?.IsProjectionActive == true && !_liveCaptionProjectionCaptionHidden)
                {
                    _projectionManager.UpdateProjectionCaptionOverlay(
                        _liveCaptionComposer.CurrentDisplay,
                        _liveCaptionComposer.CurrentHighlightStart);
                }
            }
        }

        private void RecalculateLiveCaptionComposerLayout()
        {
            if (_configManager == null)
            {
                return;
            }

            RecalculateLiveCaptionComposerLayout(
                Math.Clamp(_configManager.LiveCaptionFontSize > 0 ? _configManager.LiveCaptionFontSize : _configManager.BibleFontSize, 20, 112),
                Math.Clamp(_configManager.LiveCaptionPadding > 0 ? _configManager.LiveCaptionPadding : _configManager.BibleMargin, 6, 120),
                Math.Clamp(_configManager.LiveCaptionLetterSpacing, 0, 10));
        }

        private void RecalculateLiveCaptionComposerLayout(double fontSize, double padding, double letterSpacing)
        {
            _ = fontSize;
            _ = padding;
            _ = letterSpacing;
            _liveCaptionComposer.SetLineCharLimit(LiveCaptionMainLineCharLimit);
        }

        private void PopulateColorMenuFromPresets(MenuItem root, string currentHex, Action<string, string> applyColor)
        {
            var presets = _configManager?.GetAllColorPresets();
            if (presets == null)
            {
                return;
            }

            foreach (var preset in presets)
            {
                string hex = $"#{preset.R:X2}{preset.G:X2}{preset.B:X2}";
                string normalized = NormalizeColorHex(hex, "#FFFFFF");
                var item = new MenuItem
                {
                    Header = BuildSelectedMenuHeader(preset.Name, string.Equals(currentHex, normalized, StringComparison.OrdinalIgnoreCase))
                };
                string displayName = preset.Name;
                item.Click += (_, _) => applyColor?.Invoke(normalized, displayName);
                root.Items.Add(item);
            }
        }

        private void AddCustomColorPickerMenuItem(MenuItem root, string header, string initialHex, Action<string> applyColor)
        {
            var customItem = new MenuItem { Header = header };
            customItem.Click += (_, _) =>
            {
                string picked = PickColorHex(initialHex);
                if (string.IsNullOrWhiteSpace(picked))
                {
                    return;
                }

                applyColor?.Invoke(NormalizeColorHex(picked, "#FFFFFF"));
            };
            root.Items.Add(new Separator());
            root.Items.Add(customItem);
        }

        private static string PickColorHex(string initialHex)
        {
            try
            {
                using var dialog = new System.Windows.Forms.ColorDialog
                {
                    AllowFullOpen = true,
                    FullOpen = true
                };
                if (TryHexToDrawingColor(initialHex, out var initial))
                {
                    dialog.Color = initial;
                }

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return string.Empty;
                }

                var c = dialog.Color;
                return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryHexToDrawingColor(string hex, out System.Drawing.Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex))
            {
                return false;
            }

            string text = hex.Trim().TrimStart('#');
            if (text.Length != 6)
            {
                return false;
            }

            if (!byte.TryParse(text.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) ||
                !byte.TryParse(text.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) ||
                !byte.TryParse(text.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
            {
                return false;
            }

            color = System.Drawing.Color.FromArgb(r, g, b);
            return true;
        }

        private static string NormalizeColorHex(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string text = value.Trim().ToUpperInvariant();
            if (!text.StartsWith("#", StringComparison.Ordinal))
            {
                text = "#" + text;
            }

            if (text.Length == 7 || text.Length == 9)
            {
                return text;
            }

            return fallback;
        }

        private static string NormalizeLiveCaptionProvider(string provider)
        {
            string normalized = (provider ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "baidu" => "baidu",
                "xfyun" => "xfyun",
                "doubao" => "doubao",
                "funasr" => "doubao",
                _ => "xfyun"
            };
        }

        private void SetLiveCaptionPlatform(string provider, string displayName)
        {
            if (_configManager == null)
            {
                return;
            }

            if (_liveCaptionPlatformSwitchInProgress)
            {
                LiveCaptionDebugLogger.Log("PlatformSwitch: ignored because previous switch is still in progress.");
                return;
            }

            _liveCaptionPlatformSwitchInProgress = true;

            try
            {
                string next = NormalizeLiveCaptionProvider(provider);
                if (string.Equals(
                    NormalizeLiveCaptionProvider(_configManager.LiveCaptionAsrProvider),
                    next,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _configManager.LiveCaptionAsrProvider = next;
                ApplyLiveCaptionConfigImmediately();
                ShowStatus($"已切换平台：{displayName}");
                LiveCaptionDebugLogger.Log($"PlatformSwitch: provider={next}");
            }
            catch (Exception ex)
            {
                LiveCaptionDebugLogger.Log($"PlatformSwitch: failed, error={ex.Message}");
                ShowStatus($"平台切换失败：{ex.Message}");
            }
            finally
            {
                _liveCaptionPlatformSwitchInProgress = false;
            }
        }

        private void LoadLiveCaptionFloatingBoundsFromConfig()
        {
            if (_configManager == null || _liveCaptionOverlayWindow == null)
            {
                return;
            }

            if (!_configManager.TryGetLiveCaptionFloatingBounds(out double left, out double top, out double width, out double height))
            {
                LiveCaptionDebugLogger.Log("FloatingBounds: config not found (width/height<=0), use overlay defaults.");
                return;
            }

            LiveCaptionDebugLogger.Log(
                $"FloatingBounds: config loaded left={left:0.##}, top={top:0.##}, width={width:0.##}, height={height:0.##}");
            _liveCaptionOverlayWindow.SetFloatingBounds(left, top, width, height);
            LiveCaptionDebugLogger.Log(
                $"FloatingBounds: loaded left={left:0.##}, top={top:0.##}, width={width:0.##}, height={height:0.##}");
        }

        private void PersistLiveCaptionFloatingBoundsToConfig(string source)
        {
            if (_configManager == null || _liveCaptionOverlayWindow == null)
            {
                return;
            }

            Rect bounds = _liveCaptionOverlayWindow.GetFloatingBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            _configManager.SetLiveCaptionFloatingBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            LiveCaptionDebugLogger.Log(
                $"FloatingBounds: saved source={source}, left={bounds.Left:0.##}, top={bounds.Top:0.##}, width={bounds.Width:0.##}, height={bounds.Height:0.##}");
        }

        private void OnLiveCaptionFloatingBoundsChanged(Rect bounds)
        {
            if (_configManager == null)
            {
                return;
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            _configManager.SetLiveCaptionFloatingBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            LiveCaptionDebugLogger.Log(
                $"FloatingBounds: autosave left={bounds.Left:0.##}, top={bounds.Top:0.##}, width={bounds.Width:0.##}, height={bounds.Height:0.##}");
        }

        private void SetLiveCaptionAudioSource(LiveCaptionAudioSource source)
        {
            bool sourceChanged = _liveCaptionCurrentSource != source;
            string nextInputId = _liveCaptionSelectedInputDeviceId ?? string.Empty;
            string nextSystemId = _liveCaptionSelectedSystemDeviceId ?? string.Empty;

            bool inputChanged = !string.Equals(_configManager?.LiveCaptionInputDeviceId ?? string.Empty, nextInputId, StringComparison.Ordinal);
            bool systemChanged = !string.Equals(_configManager?.LiveCaptionSystemDeviceId ?? string.Empty, nextSystemId, StringComparison.Ordinal);
            if (!sourceChanged && !inputChanged && !systemChanged)
            {
                return;
            }

            _liveCaptionCurrentSource = source;
            if (_configManager != null)
            {
                _configManager.LiveCaptionAudioInputMode = source == LiveCaptionAudioSource.SystemLoopback ? "system" : "input";
                _configManager.LiveCaptionInputDeviceId = nextInputId;
                _configManager.LiveCaptionSystemDeviceId = nextSystemId;
            }

            LiveCaptionDebugLogger.Log($"AudioSource: switched to {source}, inputId='{nextInputId}', systemId='{nextSystemId}'.");

            if (_liveCaptionEngine?.IsRunning == true)
            {
                StartLiveCaption(source);
                ShowStatus(source == LiveCaptionAudioSource.SystemLoopback
                    ? $"已切换为系统声音：{GetLiveCaptionSystemDeviceDisplayName(_liveCaptionSelectedSystemDeviceId)}"
                    : $"已切换为输入设备：{GetLiveCaptionInputDeviceDisplayName(_liveCaptionSelectedInputDeviceId)}");
            }
        }

        private void SelectLiveCaptionSystemSource(string deviceId)
        {
            _liveCaptionSelectedSystemDeviceId = deviceId ?? string.Empty;
            SetLiveCaptionAudioSource(LiveCaptionAudioSource.SystemLoopback);
        }

        private void SelectLiveCaptionInputDevice(string deviceId)
        {
            _liveCaptionSelectedInputDeviceId = deviceId ?? string.Empty;
            SetLiveCaptionAudioSource(LiveCaptionAudioSource.Microphone);
        }

        private void PopulateLiveCaptionInputDeviceMenu(ContextMenu root)
        {
            if (root == null)
            {
                return;
            }

            var systemDevices = RealtimeCaptionEngine.EnumerateSystemLoopbackDevices();
            var devices = RealtimeCaptionEngine.EnumerateInputDevices();
            string current = _liveCaptionCurrentSource == LiveCaptionAudioSource.Microphone
                ? (_liveCaptionSelectedInputDeviceId ?? string.Empty)
                : string.Empty;
            string currentSystem = _liveCaptionCurrentSource == LiveCaptionAudioSource.SystemLoopback
                ? (_liveCaptionSelectedSystemDeviceId ?? string.Empty)
                : (_liveCaptionSelectedSystemDeviceId ?? string.Empty);

            var systemItems = new List<MenuItem>();
            bool systemCategorySelected = _liveCaptionCurrentSource == LiveCaptionAudioSource.SystemLoopback;
            if (systemDevices != null)
            {
                foreach (var device in systemDevices)
                {
                    if (device == null)
                    {
                        continue;
                    }

                    bool isSelected = _liveCaptionCurrentSource == LiveCaptionAudioSource.SystemLoopback &&
                                      (string.IsNullOrWhiteSpace(currentSystem)
                                          ? device.IsDefault
                                          : string.Equals(currentSystem, device.Id, StringComparison.Ordinal));
                    var item = new MenuItem
                    {
                        Header = BuildSelectedMenuHeader(device.Name, isSelected)
                    };
                    string selectedId = device.Id;
                    item.Click += (_, _) => SelectLiveCaptionSystemSource(selectedId);
                    if (isSelected)
                    {
                        systemCategorySelected = true;
                    }

                    systemItems.Add(item);
                }
            }

            var micItems = new List<MenuItem>();
            var lineInItems = new List<MenuItem>();
            var otherItems = new List<MenuItem>();
            bool micCategorySelected = false;
            bool lineInCategorySelected = false;
            bool otherCategorySelected = false;

            if (devices != null)
            {
                foreach (var device in devices)
                {
                    if (device == null)
                    {
                        continue;
                    }

                    bool isSelected = string.IsNullOrWhiteSpace(current)
                        ? (_liveCaptionCurrentSource == LiveCaptionAudioSource.Microphone && device.IsDefault)
                        : string.Equals(current, device.Id, StringComparison.Ordinal);
                    var item = new MenuItem
                    {
                        Header = BuildSelectedMenuHeader(device.Name, isSelected)
                    };
                    string selectedId = device.Id;
                    item.Click += (_, _) => SelectLiveCaptionInputDevice(selectedId);

                    switch (ClassifyLiveCaptionInputDevice(device))
                    {
                        case LiveCaptionInputDeviceCategory.Microphone:
                            micItems.Add(item);
                            if (isSelected) micCategorySelected = true;
                            break;
                        case LiveCaptionInputDeviceCategory.LineIn:
                            lineInItems.Add(item);
                            if (isSelected) lineInCategorySelected = true;
                            break;
                        case LiveCaptionInputDeviceCategory.Virtual:
                        default:
                            otherItems.Add(item);
                            if (isSelected) otherCategorySelected = true;
                            break;
                    }
                }
            }

            AppendInputCategoryMenu(root, BuildSelectedMenuHeader("系统声音", systemCategorySelected), systemItems);
            AppendInputCategoryMenu(root, BuildSelectedMenuHeader("麦克风", micCategorySelected), micItems);
            AppendInputCategoryMenu(root, BuildSelectedMenuHeader("线路输入", lineInCategorySelected), lineInItems);
            AppendInputCategoryMenu(root, BuildSelectedMenuHeader("未知", otherCategorySelected), otherItems);
        }

        private enum LiveCaptionInputDeviceCategory
        {
            Microphone = 0,
            LineIn = 1,
            Virtual = 2,
            Other = 3
        }

        private static void AppendInputCategoryMenu(ContextMenu root, string title, List<MenuItem> items)
        {
            if (root == null)
            {
                return;
            }

            var categoryMenu = new MenuItem
            {
                Header = title
            };
            if (items == null || items.Count == 0)
            {
                categoryMenu.IsEnabled = false;
            }
            else
            {
                foreach (var item in items)
                {
                    categoryMenu.Items.Add(item);
                }
            }

            root.Items.Add(categoryMenu);
        }

        private static LiveCaptionInputDeviceCategory ClassifyLiveCaptionInputDevice(SharedAudioCaptureSession.InputAudioDeviceInfo device)
        {
            if (device == null)
            {
                return LiveCaptionInputDeviceCategory.Other;
            }

            string name = (device.Name ?? string.Empty).ToLowerInvariant();
            string id = (device.Id ?? string.Empty).ToLowerInvariant();

            if (ContainsAny(name, id, "vb-cable", "voicemeeter", "virtual", "blackhole", "soundflower", "loopback", "virtual cable", "virtual audio", "obs virtual"))
            {
                return LiveCaptionInputDeviceCategory.Virtual;
            }

            if (ContainsAny(name, id, "line in", "line-in", "线路输入", "lineinput", "aux", "aux in"))
            {
                return LiveCaptionInputDeviceCategory.LineIn;
            }

            if (ContainsAny(name, id, "mic", "microphone", "麦克风", "array microphone", "阵列麦克风", "headset microphone"))
            {
                return LiveCaptionInputDeviceCategory.Microphone;
            }

            return LiveCaptionInputDeviceCategory.Other;
        }

        private static bool ContainsAny(string name, string id, params string[] keywords)
        {
            if (keywords == null || keywords.Length == 0)
            {
                return false;
            }

            foreach (string keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    id.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string GetLiveCaptionInputDeviceDisplayName(string deviceId)
        {
            string id = deviceId ?? string.Empty;
            foreach (var device in RealtimeCaptionEngine.EnumerateInputDevices())
            {
                if (string.Equals(device.Id, id, StringComparison.Ordinal))
                {
                    return device.IsDefault ? $"{device.Name}（默认）" : device.Name;
                }
            }

            return string.IsNullOrWhiteSpace(id) ? "默认输入设备" : "指定输入设备";
        }

        private string GetLiveCaptionSystemDeviceDisplayName(string deviceId)
        {
            string id = deviceId ?? string.Empty;
            foreach (var device in RealtimeCaptionEngine.EnumerateSystemLoopbackDevices())
            {
                if (string.Equals(device.Id, id, StringComparison.Ordinal))
                {
                    return device.Name;
                }

                if (string.IsNullOrWhiteSpace(id) && device.IsDefault)
                {
                    return device.Name;
                }
            }

            return string.IsNullOrWhiteSpace(id) ? "系统默认设备" : "指定系统设备";
        }

        private void LoadLiveCaptionAudioSourceFromConfig()
        {
            if (_configManager == null)
            {
                return;
            }

            string mode = (_configManager.LiveCaptionAudioInputMode ?? "system").Trim().ToLowerInvariant();
            _liveCaptionCurrentSource = mode == "input"
                ? LiveCaptionAudioSource.Microphone
                : LiveCaptionAudioSource.SystemLoopback;
            _liveCaptionSelectedInputDeviceId = _configManager.LiveCaptionInputDeviceId ?? string.Empty;
            _liveCaptionSelectedSystemDeviceId = _configManager.LiveCaptionSystemDeviceId ?? string.Empty;
            LiveCaptionDebugLogger.Log($"AudioSource: loaded from config mode={mode}, inputId='{_liveCaptionSelectedInputDeviceId}', systemId='{_liveCaptionSelectedSystemDeviceId}'.");
        }

        private void RegisterLiveCaptionF4HotKey()
        {
            if (_globalHotKeyManager == null || _liveCaptionF4HotKeyId > 0)
            {
                LiveCaptionDebugLogger.Log($"F4:Register skipped, managerNull={_globalHotKeyManager == null}, currentId={_liveCaptionF4HotKeyId}");
                return;
            }

            _liveCaptionF4HotKeyId = _globalHotKeyManager.RegisterHotKey(
                System.Windows.Input.Key.F4,
                System.Windows.Input.ModifierKeys.None,
                () => Dispatcher.BeginInvoke(new Action(() =>
                    TryToggleLiveCaptionOverlayVisibilityInternal("global-hotkey"))));
            LiveCaptionDebugLogger.Log($"F4:Register success, id={_liveCaptionF4HotKeyId}");
        }

        private void UnregisterLiveCaptionF4HotKey()
        {
            if (_globalHotKeyManager == null || _liveCaptionF4HotKeyId <= 0)
            {
                return;
            }

            _globalHotKeyManager.UnregisterHotKey(_liveCaptionF4HotKeyId);
            LiveCaptionDebugLogger.Log($"F4:Unregister id={_liveCaptionF4HotKeyId}");
            _liveCaptionF4HotKeyId = -1;
        }

        internal bool TryToggleLiveCaptionOverlayByShortcut()
        {
            if (_liveCaptionOverlayWindow == null)
            {
                return false;
            }

            // F4 已注册全局热键时，主窗口键盘事件只负责拦截，避免与全局回调双触发导致布局抖动。
            if (_liveCaptionF4HotKeyId > 0)
            {
                LiveCaptionDebugLogger.Log("F4: window-keydown intercepted; global hotkey callback will toggle.");
                return true;
            }

            return TryHideLiveCaptionOverlayByShortcut("window-keydown-fallback");
        }

        private void ToggleLiveCaptionOverlayVisibility()
        {
            TryToggleLiveCaptionOverlayVisibilityInternal("direct-call");
        }

        private void TryToggleLiveCaptionOverlayVisibilityInternal(string source)
        {
            if (_liveCaptionOverlayWindow == null)
            {
                LiveCaptionDebugLogger.Log($"F4:Toggle ignored source={source}, overlayNull={_liveCaptionOverlayWindow == null}");
                return;
            }

            if (_liveCaptionToggleInProgress)
            {
                LiveCaptionDebugLogger.Log($"F4:Toggle skipped source={source}, reason=toggle-in-progress");
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - _liveCaptionLastToggleUtc).TotalMilliseconds < LiveCaptionToggleDebounceMs)
            {
                LiveCaptionDebugLogger.Log($"F4:Toggle debounced source={source}, gap={(nowUtc - _liveCaptionLastToggleUtc).TotalMilliseconds:0}ms");
                return;
            }
            _liveCaptionLastToggleUtc = nowUtc;

            _liveCaptionToggleInProgress = true;
            try
            {
                if (_liveCaptionOverlayWindow.IsVisible)
                {
                    HideLiveCaptionOverlay(source);
                    return;
                }

                // 第二次按 F4：从手动隐藏状态恢复显示，不要求先点菜单。
                _liveCaptionOverlayManuallyHidden = false;
                _liveCaptionOverlayWindow.Show();
                _liveCaptionOverlayWindow.RefreshDockLayoutNow();
                _liveCaptionOverlayWindow.FocusInputMethodEditor();
                ApplyMainWindowLiveCaptionReservation();
                SyncAiCaptionUiState();
                LiveCaptionDebugLogger.Log($"F4: overlay shown source={source}.");
            }
            finally
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _liveCaptionToggleInProgress = false;
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private bool TryHideLiveCaptionOverlayByShortcut(string source)
        {
            TryToggleLiveCaptionOverlayVisibilityInternal(source);
            return true;
        }

        private void HideLiveCaptionOverlay(string source)
        {
            if (_liveCaptionOverlayWindow == null || !_liveCaptionOverlayWindow.IsVisible)
            {
                return;
            }

            _liveCaptionOverlayManuallyHidden = true;
            _liveCaptionOverlayWindow.Hide();
            ApplyMainWindowLiveCaptionReservation();
            SyncAiCaptionUiState();
            LiveCaptionDebugLogger.Log($"Overlay: hidden manually, source={source}.");
        }

        private void HideLiveCaptionOverlayFromPanel()
        {
            HideLiveCaptionOverlay("overlay-hide-button");
        }

        private static string TrimForLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return singleLine.Length <= 140 ? singleLine : singleLine.Substring(0, 140) + "...";
        }

        private static bool ShouldReserveWorkAreaForDockMode(LiveCaptionDockMode mode)
        {
            // 禁用 AppBar 系统工作区预留：
            // 该机制会触发 Explorer 重新排布桌面图标，在双屏环境下可能把图标迁移到扩展屏。
            // 当前统一采用窗口级停靠，避免系统级重排副作用。
            return false;
        }

        internal void SyncLiveCaptionVisibilityWithMainWindowContext(string source)
        {
            if (!IsLiveCaptionDisplayActive() || _liveCaptionOverlayWindow == null)
            {
                return;
            }

            // 改为独立悬浮模式：
            // 主窗口失焦/最小化时，字幕窗保持可见，不再做“跟随主窗状态自动隐藏”。
            if (_liveCaptionOverlayManuallyHidden)
            {
                ApplyMainWindowLiveCaptionReservation();
                return;
            }

            if (!_liveCaptionOverlayWindow.IsVisible)
            {
                _liveCaptionOverlayWindow.Show();
                _liveCaptionOverlayWindow.RefreshDockLayoutNow();
                LiveCaptionDebugLogger.Log($"WindowContext: overlay ensured visible source={source}");
            }

            ApplyMainWindowLiveCaptionReservation();
        }

        private void ApplyMainWindowLiveCaptionReservation()
        {
            if (MainLayoutGrid == null)
            {
                return;
            }

            if (_liveCaptionOverlayWindow == null ||
                !_liveCaptionOverlayWindow.IsVisible ||
                _liveCaptionOverlayManuallyHidden)
            {
                MainLayoutGrid.Margin = new Thickness(0);
                return;
            }

            double reserve = Math.Max(0, _liveCaptionOverlayWindow.GetBandHeight());
            switch (_liveCaptionOverlayWindow.GetDockMode())
            {
                case LiveCaptionDockMode.TopBand:
                    MainLayoutGrid.Margin = new Thickness(0, reserve, 0, 0);
                    break;
                case LiveCaptionDockMode.BottomBand:
                    MainLayoutGrid.Margin = new Thickness(0, 0, 0, reserve);
                    break;
                default:
                    MainLayoutGrid.Margin = new Thickness(0);
                    break;
            }
        }

    }
}
