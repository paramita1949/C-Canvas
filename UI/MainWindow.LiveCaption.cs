using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        private LiveCaptionDockMode _liveCaptionDockMode = LiveCaptionDockMode.Floating;
        private bool _isDisposingLiveCaption;
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
        private const int LiveCaptionToggleDebounceMs = 220;
        private const int LiveCaptionMainLineCharLimit = 30;
        private DateTime _realtimeVerseLastAttemptUtc = DateTime.MinValue;
        private string _realtimeVerseLastText = string.Empty;
        private int _realtimeLastResolvedBookId;
        private int _realtimeLastResolvedChapter;
        private DateTime _realtimeLastResolvedUtc = DateTime.MinValue;

        private string GetRealtimeLogTag()
        {
            return LiveCaptionPlatformLabelFormatter.BuildRealtimeTag(_configManager?.LiveCaptionRealtimeAsrProvider);
        }

        private string GetShortPhraseLogTag()
        {
            return LiveCaptionPlatformLabelFormatter.BuildShortPhraseTag(_configManager?.LiveCaptionShortAsrProvider);
        }

        private void LogRealtimeCaption(string message)
        {
            LiveCaptionDebugLogger.Log($"[{GetRealtimeLogTag()}] {message}");
        }

        private void LogShortPhraseCaption(string message)
        {
            LiveCaptionDebugLogger.Log($"[{GetShortPhraseLogTag()}] {message}");
        }

        private void EnsureLiveCaptionComponents()
        {
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

            if (_bibleService == null)
            {
                InitializeBibleService();
            }

            _bibleBaiduShortSpeechClient ??= new BibleBaiduShortSpeechClient(_configManager);

            if (_bibleVerseContentIndex == null)
            {
                _bibleVerseContentIndex = new BibleVerseContentIndex();
                _ = Task.Run(() => _bibleVerseContentIndex.LoadAsync(_bibleService, dbFilePath: GetBibleDbFilePath()));
            }

            _bibleSpeechReverseLookupService ??= new BibleSpeechReverseLookupService(
                _bibleVerseContentIndex,
                msg => LogShortPhraseCaption(msg));
            _bibleShortPhraseRuntime ??= new BibleShortPhraseRuntime(
                new BibleShortPhraseConsumer(
                    _bibleService,
                    (wav, ct) => _bibleBaiduShortSpeechClient.TranscribeWavAsync(wav, ct),
                    _bibleSpeechReverseLookupService,
                    msg => LogShortPhraseCaption(msg)),
                result =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (result.Success)
                        {
                            int startVs = Math.Max(1, result.Reference.StartVerse);
                            int endVs   = Math.Max(1, result.FinalEndVerse);
                            LogShortPhraseCaption(
                                $"✅ 插入历史 book={result.Reference.BookId} " +
                                $"ch={result.Reference.Chapter} vs={startVs}~{endVs} | '{result.RecognizedText}'");
                            ShowToast(FormatBibleReferenceToastText(
                                result.Reference.BookId, result.Reference.Chapter, startVs, endVs));
                            AddPinyinHistoryToEmptySlot(
                                result.Reference.BookId, result.Reference.Chapter, startVs, endVs);
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
                _liveCaptionOverlayWindow.CaptionStyleRequested += OpenLiveCaptionStyleSettings;
                _liveCaptionOverlayWindow.NdiStyleRequested += OpenLiveCaptionNdiStyleSettings;
                _liveCaptionOverlayWindow.LocalStyleRequested += OpenLiveCaptionLocalStyleSettings;
                _liveCaptionOverlayWindow.ProjectionToggleRequested += ToggleProjectionFromLiveCaptionOverlay;
                _liveCaptionOverlayWindow.NdiToggleRequested += ToggleNdiFromLiveCaptionOverlay;
                _liveCaptionOverlayWindow.CaptionOrientationRequested += OnLiveCaptionOverlayCaptionOrientationRequested;
                _liveCaptionOverlayWindow.CaptionPositionRequested += OnLiveCaptionOverlayCaptionPositionRequested;
                _liveCaptionOverlayWindow.CloseRequested += StopLiveCaption;
                _liveCaptionOverlayWindow.RealtimeRecognitionToggleRequested += OnRealtimeRecognitionToggleRequested;
                _liveCaptionOverlayWindow.ShortPhraseRecognitionToggleRequested += OnShortPhraseRecognitionToggleRequested;
                _liveCaptionOverlayWindow.FloatingBoundsChanged += OnLiveCaptionFloatingBoundsChanged;
                _liveCaptionOverlayWindow.Closed += LiveCaptionOverlayWindow_Closed;
                _liveCaptionOverlayWindow.SetTypingAnimationEnabled(false);
                _liveCaptionOverlayWindow.SetWorkAreaReservationEnabled(ShouldReserveWorkAreaForDockMode(_liveCaptionDockMode));
                _liveCaptionOverlayWindow.SetProjectionToggleState(_liveCaptionProjectionCaptionHidden);
                _liveCaptionOverlayWindow.SetRecognitionToggleStates(_configManager.LiveCaptionRealtimeEnabled, _configManager.LiveCaptionShortPhraseEnabled);
                UpdateLiveCaptionNdiActionState();
                LoadLiveCaptionFloatingBoundsFromConfig();
                ApplyLiveCaptionTypographyFromBible();
                ApplyProjectionCaptionLayoutFromConfig();
            }
        }

        private void BtnLiveCaptionMic_Click(object sender, RoutedEventArgs e)
        {
            StartLiveCaption(LiveCaptionAudioSource.Microphone);
        }

        private void BtnLiveCaptionSystem_Click(object sender, RoutedEventArgs e)
        {
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
            StartLiveCaption(_liveCaptionCurrentSource);
        }

        private void OpenLiveCaptionPanel()
        {
            if (!_liveCaptionOverlayWindow.IsVisible)
            {
                _liveCaptionOverlayWindow.Show();
            }

            _liveCaptionOverlayWindow.SetRecognitionToggleStates(_configManager.LiveCaptionRealtimeEnabled, _configManager.LiveCaptionShortPhraseEnabled);
            _liveCaptionOverlayWindow.RefreshDockLayoutNow();
            ApplyLiveCaptionTypographyFromBible();
            SyncLiveCaptionVisibilityWithMainWindowContext("panel-open");
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            EnsureLiveCaptionComponents();
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
                _liveCaptionOverlayWindow.Show();
            }
            LiveCaptionDebugLogger.Log($"StartPerf: overlay-show={sw.ElapsedMilliseconds}ms");

            _liveCaptionOverlayWindow.RefreshDockLayoutNow();
            ApplyLiveCaptionTypographyFromBible();
            _liveCaptionOverlayWindow.UpdateCaption(string.Empty, 0);
            _liveCaptionOverlayWindow.SetTypingAnimationEnabled(false);
            UpdateLiveCaptionProjectionActionState();
            UpdateLiveCaptionNdiActionState();
            SyncLiveCaptionProjectionCaptionForProjectionState(_projectionManager?.IsProjectionActive == true);
            ApplyMainWindowLiveCaptionReservation();
            _liveCaptionCurrentSource = source;
            _liveCaptionOverlayWindow.SetRecognitionToggleStates(
                _configManager.LiveCaptionRealtimeEnabled,
                _configManager.LiveCaptionShortPhraseEnabled);
            SyncLiveCaptionVisibilityWithMainWindowContext("start");
            LiveCaptionDebugLogger.Log($"StartPerf: overlay-prepare={sw.ElapsedMilliseconds}ms");

            bool realtimeEnabled = _configManager.LiveCaptionRealtimeEnabled;
            bool shortPhraseEnabled = _configManager.LiveCaptionShortPhraseEnabled;
            if (!LiveCaptionStartupPolicy.ShouldAutoStartRecognition(realtimeEnabled, shortPhraseEnabled))
            {
                LiveCaptionDebugLogger.Log("Start: no recognition mode enabled, panel opened without starting engines.");
                ShowStatus("请选择实时语音或经文识别");
                RegisterLiveCaptionF4HotKey();
                return;
            }

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
            LiveCaptionDebugLogger.Log($"Start: recognition state applied, dock={_liveCaptionDockMode}, overlayVisible={_liveCaptionOverlayWindow.IsVisible}");
        }

        private void StopLiveCaption()
        {
            try
            {
                LiveCaptionDebugLogger.Log($"Stop: request running={_liveCaptionEngine?.IsRunning}, overlayVisible={_liveCaptionOverlayWindow?.IsVisible}");
                _liveCaptionEngine?.Stop();
                if (_bibleShortPhraseRuntime?.IsRunning == true)
                {
                    _ = _bibleShortPhraseRuntime.StopAsync(CancellationToken.None);
                }
                _sharedAudioCaptureSession?.Stop();
                _configManager.LiveCaptionRealtimeEnabled = false;
                _configManager.LiveCaptionShortPhraseEnabled = false;
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
                UpdateLiveCaptionProjectionActionState();
                UpdateLiveCaptionNdiActionState();
                _projectionManager?.HideProjectionCaptionOverlay();
                _projectionNdiOutputManager?.PushTransparentIdleFrame(startSenderIfNeeded: false);
                StopProjectionNdiSenderIfUnused();
                ApplyMainWindowLiveCaptionReservation();
                UnregisterLiveCaptionF4HotKey();
                LiveCaptionDebugLogger.Log("Stop: completed.");
            }
            catch (Exception ex)
            {
                LiveCaptionDebugLogger.Log($"Stop: failed with {ex.GetType().Name}: {ex.Message}");
                ShowStatus($"关闭字幕失败：{ex.Message}");
            }
        }

        private void LiveCaptionOverlayWindow_Closed(object sender, EventArgs e)
        {
            LiveCaptionDebugLogger.Log($"OverlayClosed: disposing={_isDisposingLiveCaption}");
            if (_isDisposingLiveCaption)
            {
                return;
            }

            StopLiveCaption();
        }

        private async void OnRealtimeRecognitionToggleRequested(bool enabled)
        {
            LogRealtimeCaption($"RecognitionToggle: enabled={enabled}");
            _configManager.LiveCaptionRealtimeEnabled = enabled;
            await ApplyRecognitionStateAsync();
        }

        private async void OnShortPhraseRecognitionToggleRequested(bool enabled)
        {
            LogShortPhraseCaption($"RecognitionToggle: enabled={enabled}");
            _configManager.LiveCaptionShortPhraseEnabled = enabled;
            await ApplyRecognitionStateAsync();
        }

        private async Task ApplyRecognitionStateAsync()
        {
            EnsureLiveCaptionCoreComponents();

            bool realtimeEnabled = _configManager.LiveCaptionRealtimeEnabled;
            bool shortEnabled = _configManager.LiveCaptionShortPhraseEnabled;
            string verseSource = _configManager.LiveCaptionVerseSource ?? "shortPhrase";
            LiveCaptionDebugLogger.Log(
                $"[{GetRealtimeLogTag()}|{GetShortPhraseLogTag()}] RecognitionState: " +
                $"realtime={realtimeEnabled}, short={shortEnabled}, verseSource={verseSource}, " +
                $"source={_liveCaptionCurrentSource}, inputId='{_liveCaptionSelectedInputDeviceId}', systemId='{_liveCaptionSelectedSystemDeviceId}'");
            _liveCaptionOverlayWindow?.SetRecognitionToggleStates(realtimeEnabled, shortEnabled);

            if (!realtimeEnabled && !shortEnabled)
            {
                LiveCaptionDebugLogger.Log("RecognitionState: both disabled, stopping all consumers and shared capture.");
                _liveCaptionEngine?.Stop();
                if (_bibleShortPhraseRuntime?.IsRunning == true)
                {
                    await _bibleShortPhraseRuntime.StopAsync(CancellationToken.None);
                }
                _sharedAudioCaptureSession?.Stop();
                ShowStatus("字幕识别已关闭");
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
                await _bibleShortPhraseRuntime.StopAsync(CancellationToken.None);
                ShowStatus("经文识别已关闭");
            }
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_liveCaptionEngine == null || !_liveCaptionEngine.IsRunning)
                {
                    return;
                }

                // 经文匹配：不依赖 overlay 窗口，静默运行也能触发
                // 策略分层：
                // - interim: 尾部窗口 + 冷却，低频触发，避免抖动
                // - final:   整句强制触发（不受冷却限制），提高经文入槽确定性
                bool verseSourceRealtime = IsVerseSourceRealtime();
                if (!verseSourceRealtime && update.IsFinal && !string.IsNullOrWhiteSpace(update.Text))
                {
                    LogRealtimeCaption($"RealtimeVerse: ASR文本(final-passive) '{TrimForLog(update.Text.Trim())}'");
                }

                if (verseSourceRealtime && !string.IsNullOrWhiteSpace(update.Text))
                {
                    string rawText = update.Text.Trim();
                    if (TryBuildRealtimeVerseProbeText(rawText, update.IsFinal, out string probeText, out string probeReason))
                    {
                        _realtimeVerseLastAttemptUtc = DateTime.UtcNow;
                        _realtimeVerseLastText = probeText;
                        LogRealtimeCaption($"RealtimeVerse: ASR文本({probeReason}) '{TrimForLog(probeText)}'");
                        _ = TryMatchVerseFromRealtimeAsync(probeText, update.IsFinal);
                    }
                }

                // 实时语音未启用或 overlay 未创建 → 不渲染字幕
                if (!_configManager.LiveCaptionRealtimeEnabled || _liveCaptionOverlayWindow == null)
                    return;

                // NDI 独立链路：不复用主屏/投影链路的排版与窗口更新条件。
                LiveCaptionRenderFrame ndiFrame = PushNdiCaptionFrame(update);
                if (ndiFrame.HasChanged)
                {
                    UpdateLiveCaptionNdiCaption(ndiFrame.Display, ndiFrame.HighlightStart);
                }

                RecalculateLiveCaptionComposerLayout();
                LiveCaptionRenderFrame frame = _liveCaptionComposer.Push(update);
                ProbeHorizontalShiftAnomaly(frame, update);
                if (!frame.HasChanged)
                {
                    // unchanged, skip
                    return;
                }

                if (string.IsNullOrWhiteSpace(frame.Display))
                {
                    // display empty, skip
                    return;
                }

                if (!_hasShownRealtimeSubtitleFeedback)
                {
                    _hasShownRealtimeSubtitleFeedback = true;
                    ShowStatus("实时字幕已接收");
                }

                // 投影字幕与本机字幕窗显示状态解耦：
                // 即使本机字幕窗被手动隐藏，也要继续推送投影字幕。
                UpdateLiveCaptionProjectionCaption(frame.Display, frame.HighlightStart);

                if (_liveCaptionOverlayManuallyHidden)
                {
                    // overlay hidden manually, local render skipped
                    return;
                }

                if (!_liveCaptionOverlayWindow.IsVisible)
                {
                    _liveCaptionOverlayWindow.Show();
                }

                _liveCaptionOverlayWindow.UpdateCaption(frame.Display, frame.HighlightStart);
                SyncLiveCaptionVisibilityWithMainWindowContext("subtitle-updated");
                ApplyMainWindowLiveCaptionReservation();
                // subtitle rendered
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
            return string.Equals(src, "realtime", StringComparison.OrdinalIgnoreCase)
                || string.Equals(src, "both", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsVerseSourceShortPhrase()
        {
            string src = _configManager?.LiveCaptionVerseSource ?? "shortPhrase";
            return string.Equals(src, "shortPhrase", StringComparison.OrdinalIgnoreCase)
                || string.Equals(src, "both", StringComparison.OrdinalIgnoreCase);
        }

        private async Task TryMatchVerseFromRealtimeAsync(string text, bool isFinal)
        {
            if (string.IsNullOrWhiteSpace(text) || _bibleSpeechReverseLookupService == null)
                return;

            try
            {
                var parseCandidates = BibleSpeechTextNormalizer.BuildReferenceCandidates(
                    text,
                    aggressive: isFinal,
                    allowInferVerseUnit: isFinal);
                if (TryParseReferenceFromCandidates(parseCandidates, out var directRef, out string matchedCandidate))
                {
                    int endVs = directRef.EndVerse;
                    if (endVs <= 0)
                    {
                        int vc = await _bibleService.GetVerseCountAsync(directRef.BookId, directRef.Chapter);
                        endVs = vc > 0 ? vc : Math.Max(1, directRef.StartVerse);
                    }
                    _bibleSpeechReverseLookupService.NotifyDirectParse(directRef.BookId, directRef.Chapter, Math.Max(1, directRef.StartVerse));
                    UpdateRealtimeVerseContext(directRef.BookId, directRef.Chapter);
                    LiveCaptionDebugLogger.Log(
                        $"[{GetRealtimeLogTag()}] RealtimeVerse: ✅ 直接解析 book={directRef.BookId} ch={directRef.Chapter} vs={directRef.StartVerse}~{endVs} | src='{TrimForLog(text)}' matched='{TrimForLog(matchedCandidate)}'");
                    ShowToast(FormatBibleReferenceToastText(directRef.BookId, directRef.Chapter, Math.Max(1, directRef.StartVerse), endVs));
                    AddPinyinHistoryToEmptySlot(directRef.BookId, directRef.Chapter, Math.Max(1, directRef.StartVerse), endVs);
                    return;
                }

                // 实时 interim 片段只走规则解析，不走内容反查，避免半句触发误匹配。
                if (!isFinal)
                {
                    return;
                }

                if (TryResolveWithRealtimeContext(parseCandidates, out var contextualRef, out string contextualCandidate))
                {
                    int contextualEnd = contextualRef.EndVerse;
                    if (contextualEnd <= 0)
                    {
                        int vc = await _bibleService.GetVerseCountAsync(contextualRef.BookId, contextualRef.Chapter);
                        contextualEnd = vc > 0 ? vc : Math.Max(1, contextualRef.StartVerse);
                    }

                    _bibleSpeechReverseLookupService.NotifyDirectParse(
                        contextualRef.BookId,
                        contextualRef.Chapter,
                        Math.Max(1, contextualRef.StartVerse));
                    UpdateRealtimeVerseContext(contextualRef.BookId, contextualRef.Chapter);
                    LiveCaptionDebugLogger.Log(
                        $"[{GetRealtimeLogTag()}] RealtimeVerse: ✅ 上下文补全 book={contextualRef.BookId} ch={contextualRef.Chapter} vs={contextualRef.StartVerse}~{contextualEnd} | src='{TrimForLog(text)}' matched='{TrimForLog(contextualCandidate)}'");
                    ShowToast(FormatBibleReferenceToastText(
                        contextualRef.BookId,
                        contextualRef.Chapter,
                        Math.Max(1, contextualRef.StartVerse),
                        contextualEnd));
                    AddPinyinHistoryToEmptySlot(
                        contextualRef.BookId,
                        contextualRef.Chapter,
                        Math.Max(1, contextualRef.StartVerse),
                        contextualEnd);
                    return;
                }

                var resolved = await _bibleSpeechReverseLookupService.TryResolveAsync(
                    _bibleService, text, CancellationToken.None);
                if (!resolved.HasValue)
                    return;

                var r = resolved.Value;
                int finalEnd = r.EndVerse;
                if (finalEnd <= 0)
                {
                    int vc = await _bibleService.GetVerseCountAsync(r.BookId, r.Chapter);
                    finalEnd = vc > 0 ? vc : Math.Max(1, r.StartVerse);
                }
                UpdateRealtimeVerseContext(r.BookId, r.Chapter);
                LiveCaptionDebugLogger.Log(
                    $"[{GetRealtimeLogTag()}] RealtimeVerse: ✅ 内容反查 book={r.BookId} ch={r.Chapter} vs={r.StartVerse}~{finalEnd} | '{TrimForLog(text)}'");
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowToast(FormatBibleReferenceToastText(r.BookId, r.Chapter, Math.Max(1, r.StartVerse), finalEnd));
                    AddPinyinHistoryToEmptySlot(r.BookId, r.Chapter, Math.Max(1, r.StartVerse), finalEnd);
                }));
            }
            catch (Exception ex)
            {
                LogRealtimeCaption($"RealtimeVerse: error {ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool TryBuildRealtimeVerseProbeText(string rawText, bool isFinal, out string probeText, out string reason)
        {
            probeText = string.Empty;
            reason = string.Empty;
            string text = (rawText ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            if (isFinal)
            {
                probeText = text;
                reason = "final";
                return !string.Equals(probeText, _realtimeVerseLastText, StringComparison.Ordinal);
            }

            int tailLen = Math.Min(text.Length, 30);
            string tail = text.Substring(text.Length - tailLen);
            if (tail.Length < 6)
            {
                return false;
            }

            bool textChanged = !string.Equals(tail, _realtimeVerseLastText, StringComparison.Ordinal);
            bool cooldownPassed = (DateTime.UtcNow - _realtimeVerseLastAttemptUtc).TotalMilliseconds >= 3000;
            if (!textChanged || !cooldownPassed)
            {
                return false;
            }

            probeText = tail;
            reason = "interim";
            return true;
        }

        private static bool TryParseReferenceFromCandidates(
            IReadOnlyList<string> candidates,
            out BibleSpeechReference reference,
            out string matchedCandidate)
        {
            reference = default;
            matchedCandidate = string.Empty;
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            int bestScore = int.MinValue;
            bool matched = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (!BibleSpeechReferenceParser.TryParse(candidate, out var parsed))
                {
                    continue;
                }

                int score = 0;
                if (parsed.EndVerse > 0)
                {
                    score += 6;
                }
                if (parsed.StartVerse > 1)
                {
                    score += 3;
                }
                if (candidate.Contains("节", StringComparison.Ordinal) || candidate.Contains(":", StringComparison.Ordinal))
                {
                    score += 2;
                }
                if (candidate.Contains("章", StringComparison.Ordinal))
                {
                    score += 1;
                }

                if (!matched || score > bestScore)
                {
                    matched = true;
                    bestScore = score;
                    reference = parsed;
                    matchedCandidate = candidate;
                }
            }

            return matched;
        }

        private void UpdateRealtimeVerseContext(int bookId, int chapter)
        {
            if (bookId <= 0 || chapter <= 0)
            {
                return;
            }

            _realtimeLastResolvedBookId = bookId;
            _realtimeLastResolvedChapter = chapter;
            _realtimeLastResolvedUtc = DateTime.UtcNow;
        }

        private bool TryResolveWithRealtimeContext(
            IReadOnlyList<string> candidates,
            out BibleSpeechReference reference,
            out string matchedCandidate)
        {
            reference = default;
            matchedCandidate = string.Empty;
            if (_realtimeLastResolvedBookId <= 0 || _realtimeLastResolvedChapter <= 0)
            {
                return false;
            }

            if ((DateTime.UtcNow - _realtimeLastResolvedUtc).TotalSeconds > 12)
            {
                return false;
            }

            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            string bookName = BibleBookConfig.GetBook(_realtimeLastResolvedBookId)?.Name;
            if (string.IsNullOrWhiteSpace(bookName))
            {
                return false;
            }

            int bestScore = int.MinValue;
            bool matched = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                string synthetic = $"{bookName}{_realtimeLastResolvedChapter}章{candidate}";
                if (!BibleSpeechReferenceParser.TryParse(synthetic, out var parsed))
                {
                    continue;
                }

                if (parsed.BookId != _realtimeLastResolvedBookId || parsed.Chapter != _realtimeLastResolvedChapter)
                {
                    continue;
                }

                // 只接受带节号的补全，避免把“二章”再次补全成整章。
                if (parsed.EndVerse <= 0)
                {
                    continue;
                }

                int score = 0;
                if (candidate.Contains("节", StringComparison.Ordinal) || candidate.Contains(":", StringComparison.Ordinal))
                {
                    score += 3;
                }
                if (parsed.StartVerse > 1)
                {
                    score += 2;
                }
                score += 1;

                if (!matched || score > bestScore)
                {
                    matched = true;
                    bestScore = score;
                    reference = parsed;
                    matchedCandidate = synthetic;
                }
            }

            return matched;
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
            try
            {
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
                    _liveCaptionOverlayWindow.CaptionStyleRequested -= OpenLiveCaptionStyleSettings;
                    _liveCaptionOverlayWindow.NdiStyleRequested -= OpenLiveCaptionNdiStyleSettings;
                    _liveCaptionOverlayWindow.LocalStyleRequested -= OpenLiveCaptionLocalStyleSettings;
                    _liveCaptionOverlayWindow.ProjectionToggleRequested -= ToggleProjectionFromLiveCaptionOverlay;
                    _liveCaptionOverlayWindow.NdiToggleRequested -= ToggleNdiFromLiveCaptionOverlay;
                    _liveCaptionOverlayWindow.CaptionOrientationRequested -= OnLiveCaptionOverlayCaptionOrientationRequested;
                    _liveCaptionOverlayWindow.CaptionPositionRequested -= OnLiveCaptionOverlayCaptionPositionRequested;
                    _liveCaptionOverlayWindow.CloseRequested -= StopLiveCaption;
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
            _projectionNdiOutputManager?.PushTransparentIdleFrame(startSenderIfNeeded: false);
            StopProjectionNdiSenderIfUnused();
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

        private void OpenLiveCaptionStyleSettings()
        {
            if (_liveCaptionOverlayWindow == null || _configManager == null)
            {
                return;
            }

            var menu = new ContextMenu();

            var fontMenu = new MenuItem { Header = "字体" };
            PopulateLiveCaptionFontMenu(fontMenu);
            menu.Items.Add(fontMenu);

            var fontSizeMenu = new MenuItem { Header = "字号" };
            PopulateLiveCaptionFontSizeMenu(fontSizeMenu);
            menu.Items.Add(fontSizeMenu);

            var letterSpacingMenu = new MenuItem { Header = "字间距" };
            PopulateLiveCaptionLetterSpacingMenu(letterSpacingMenu);
            menu.Items.Add(letterSpacingMenu);

            var lineGapMenu = new MenuItem { Header = "段间距" };
            PopulateLiveCaptionLineGapMenu(lineGapMenu);
            menu.Items.Add(lineGapMenu);

            menu.Items.Add(new Separator());

            var textColorMenu = new MenuItem { Header = "字幕颜色" };
            PopulateLiveCaptionTextColorMenu(textColorMenu);
            menu.Items.Add(textColorMenu);

            var latestColorMenu = new MenuItem { Header = "最新字颜色" };
            PopulateLiveCaptionLatestColorMenu(latestColorMenu);
            menu.Items.Add(latestColorMenu);

            menu.PlacementTarget = _liveCaptionOverlayWindow.GetStyleAnchorElement();
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 2;
            menu.IsOpen = true;
            LiveCaptionDebugLogger.Log("StyleSettings: context menu opened.");
        }

        private void OpenLiveCaptionLocalStyleSettings()
        {
            if (_liveCaptionOverlayWindow == null || _configManager == null)
            {
                return;
            }

            var menu = new ContextMenu();

            var fontMenu = new MenuItem { Header = "字体" };
            PopulateLocalCaptionFontMenu(fontMenu);
            menu.Items.Add(fontMenu);

            var fontSizeMenu = new MenuItem { Header = "字号" };
            PopulateLocalCaptionFontSizeMenu(fontSizeMenu);
            menu.Items.Add(fontSizeMenu);

            var letterSpacingMenu = new MenuItem { Header = "字间距" };
            PopulateLocalCaptionLetterSpacingMenu(letterSpacingMenu);
            menu.Items.Add(letterSpacingMenu);

            var lineGapMenu = new MenuItem { Header = "段间距" };
            PopulateLocalCaptionLineGapMenu(lineGapMenu);
            menu.Items.Add(lineGapMenu);

            menu.Items.Add(new Separator());

            var textColorMenu = new MenuItem { Header = "字幕颜色" };
            PopulateLocalCaptionTextColorMenu(textColorMenu);
            menu.Items.Add(textColorMenu);

            var latestColorMenu = new MenuItem { Header = "最新字颜色" };
            PopulateLocalCaptionLatestColorMenu(latestColorMenu);
            menu.Items.Add(latestColorMenu);

            menu.PlacementTarget = _liveCaptionOverlayWindow.GetLocalStyleAnchorElement();
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 2;
            menu.IsOpen = true;
            LiveCaptionDebugLogger.Log("LocalStyleSettings: context menu opened.");
        }

        private void OpenLiveCaptionNdiStyleSettings()
        {
            if (_liveCaptionOverlayWindow == null || _configManager == null)
            {
                return;
            }

            var menu = new ContextMenu();

            var fontMenu = new MenuItem { Header = "字体" };
            PopulateNdiFontMenu(fontMenu);
            menu.Items.Add(fontMenu);

            var fontSizeMenu = new MenuItem { Header = "字号" };
            PopulateNdiFontSizeMenu(fontSizeMenu);
            menu.Items.Add(fontSizeMenu);

            var letterSpacingMenu = new MenuItem { Header = "字间距" };
            PopulateNdiLetterSpacingMenu(letterSpacingMenu);
            menu.Items.Add(letterSpacingMenu);

            var lineGapMenu = new MenuItem { Header = "段间距" };
            PopulateNdiLineGapMenu(lineGapMenu);
            menu.Items.Add(lineGapMenu);

            var alignmentMenu = new MenuItem { Header = "对齐" };
            PopulateNdiAlignmentMenu(alignmentMenu);
            menu.Items.Add(alignmentMenu);

            var ndiCharsMenu = new MenuItem { Header = "字数" };
            PopulateLiveCaptionNdiCharsMenu(ndiCharsMenu);
            menu.Items.Add(ndiCharsMenu);

            menu.Items.Add(new Separator());

            var textColorMenu = new MenuItem { Header = "字幕颜色" };
            PopulateNdiTextColorMenu(textColorMenu);
            menu.Items.Add(textColorMenu);

            var latestColorMenu = new MenuItem { Header = "最新字颜色" };
            PopulateNdiLatestColorMenu(latestColorMenu);
            menu.Items.Add(latestColorMenu);

            menu.PlacementTarget = _liveCaptionOverlayWindow.GetNdiStyleAnchorElement();
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 2;
            menu.IsOpen = true;
            LiveCaptionDebugLogger.Log("NdiStyleSettings: context menu opened.");
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
            int current = (int)Math.Round(Math.Clamp((_configManager?.LiveCaptionLocalFontSize > 0 ? _configManager.LiveCaptionLocalFontSize : (_configManager?.LiveCaptionFontSize > 0 ? _configManager.LiveCaptionFontSize : (_configManager?.BibleFontSize ?? 36))), 20, 112));
            int[] sizes = { 20, 24, 28, 32, 36, 40, 46, 52, 60, 68, 76, 88, 100, 112 };
            foreach (int size in sizes)
            {
                int selected = size;
                AddLiveCaptionNumericItem(root, size.ToString(), current == size, () => SetLocalCaptionFontSize(selected));
            }
        }

        private void ProbeHorizontalShiftAnomaly(in LiveCaptionRenderFrame frame, in LiveCaptionAsrText update)
        {
            string display = frame.Display ?? string.Empty;
            if (string.IsNullOrWhiteSpace(display))
            {
                _captionShiftProbePrevDisplay = display;
                return;
            }

            string[] lines = display.Replace("\r", string.Empty).Split('\n');
            int lineCount = lines.Length;
            int maxLineLen = 0;
            foreach (string line in lines)
            {
                if (line.Length > maxLineLen)
                {
                    maxLineLen = line.Length;
                }
            }

            bool isHorizontal = !string.Equals(
                NormalizeProjectionCaptionOrientation(_configManager?.LiveCaptionProjectionOrientation),
                "vertical",
                StringComparison.Ordinal);
            bool hasWrap = display.IndexOf('\n') >= 0;
            bool exceededLimitNoWrap = isHorizontal && !hasWrap && maxLineLen > LiveCaptionMainLineCharLimit + 1;
            bool lostWrapUnexpectedly = isHorizontal
                && _captionShiftProbePrevDisplay.IndexOf('\n') >= 0
                && !hasWrap
                && maxLineLen > LiveCaptionMainLineCharLimit / 2;

            if (exceededLimitNoWrap || lostWrapUnexpectedly)
            {
                var now = DateTime.UtcNow;
                if ((now - _captionShiftProbeLastLogUtc).TotalMilliseconds >= 350)
                {
                    _captionShiftProbeLastLogUtc = now;
                    string msg =
                        $"[CaptionShiftProbe:Main] anomaly wrapLost={lostWrapUnexpectedly}, exceededNoWrap={exceededLimitNoWrap}, " +
                        $"lineCount={lineCount}, maxLineLen={maxLineLen}, limit={LiveCaptionMainLineCharLimit}, " +
                        $"highlight={frame.HighlightStart}, final={update.IsFinal}, " +
                        $"font={_configManager?.LiveCaptionLocalFontSize:0.##}/{_configManager?.LiveCaptionFontSize:0.##}, " +
                        $"spacing={_configManager?.LiveCaptionLocalLetterSpacing:0.##}/{_configManager?.LiveCaptionLetterSpacing:0.##}, " +
                        $"gap={_configManager?.LiveCaptionLocalLineGap:0.##}/{_configManager?.LiveCaptionLineGap:0.##}, " +
                        $"display='{TrimForLog(display)}'";
                    LiveCaptionDebugLogger.Log(msg);
                }
            }

            _captionShiftProbePrevDisplay = display;
        }

        private void SetLocalCaptionFontSize(double value)
        {
            if (_configManager == null) return;
            _configManager.LiveCaptionLocalFontSize = Math.Clamp(value, 20, 112);
            ApplyLiveCaptionTypographyFromBible();
            ShowStatus($"本机字幕字号：{value:0}");
        }

        private void PopulateLocalCaptionLetterSpacingMenu(MenuItem root)
        {
            int current = (int)Math.Round(Math.Clamp(_configManager?.LiveCaptionLocalLetterSpacing ?? _configManager?.LiveCaptionLetterSpacing ?? 0, 0, 10));
            for (int value = 0; value <= 10; value++)
            {
                int selected = value;
                AddLiveCaptionNumericItem(root, value.ToString(), current == value, () => SetLocalCaptionLetterSpacing(selected));
            }
        }

        private void SetLocalCaptionLetterSpacing(double value)
        {
            if (_configManager == null) return;
            _configManager.LiveCaptionLocalLetterSpacing = Math.Clamp(value, 0, 10);
            ApplyLiveCaptionTypographyFromBible();
            ShowStatus($"本机字幕字间距：{value:0}");
        }

        private void PopulateLocalCaptionLineGapMenu(MenuItem root)
        {
            int currentLevel = LineGapToLevel(_configManager?.LiveCaptionLocalLineGap ?? _configManager?.LiveCaptionLineGap ?? 10);
            for (int level = 0; level <= 10; level++)
            {
                int selectedLevel = level;
                AddLiveCaptionNumericItem(root, level.ToString(), currentLevel == level, () => SetLocalCaptionLineGap(selectedLevel));
            }
        }

        private void SetLocalCaptionLineGap(double value)
        {
            if (_configManager == null) return;
            int level = (int)Math.Round(Math.Clamp(value, 0, 10));
            _configManager.LiveCaptionLocalLineGap = LevelToLineGap(level);
            ApplyLiveCaptionTypographyFromBible();
            ShowStatus($"本机字幕段间距：{level}");
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

        private void PopulateLocalCaptionLatestColorMenu(MenuItem root)
        {
            string current = NormalizeColorHex(_configManager?.LiveCaptionLocalLatestTextColor, NormalizeColorHex(_configManager?.LiveCaptionLatestTextColor, "#FFFF00"));
            PopulateColorMenuFromPresets(
                root,
                current,
                (hex, name) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionLocalLatestTextColor = hex;
                    ApplyLiveCaptionTypographyFromBible();
                    ShowStatus($"本机最新字颜色：{name}");
                });
            AddCustomColorPickerMenuItem(
                root,
                "自定义颜色...",
                current,
                (hex) =>
                {
                    if (_configManager == null) return;
                    _configManager.LiveCaptionLocalLatestTextColor = hex;
                    ApplyLiveCaptionTypographyFromBible();
                    ShowStatus($"本机最新字颜色：{hex}");
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
            int current = (int)Math.Round(Math.Clamp((_configManager?.LiveCaptionNdiFontSize > 0 ? _configManager.LiveCaptionNdiFontSize : (_configManager?.LiveCaptionFontSize > 0 ? _configManager.LiveCaptionFontSize : (_configManager?.BibleFontSize ?? 36))), 20, 112));
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
                if (_configManager == null)
                {
                    return;
                }

                bool nextEnabled = !IsLiveCaptionNdiEnabled();
                _configManager.LiveCaptionNdiEnabled = nextEnabled;
                if (nextEnabled)
                {
                    ShowStatus("字幕NDI已开启");
                }
                else
                {
                    _projectionNdiOutputManager?.PushTransparentIdleFrame(startSenderIfNeeded: false);
                    StopProjectionNdiSenderIfUnused();
                    ShowStatus("字幕NDI已关闭");
                }

                UpdateLiveCaptionNdiActionState();
                LiveCaptionDebugLogger.Log($"NdiToggle: enabled={nextEnabled}");
            }
            catch (Exception ex)
            {
                LiveCaptionDebugLogger.Log($"NdiToggle: failed, error={ex.Message}");
            }
        }

        private void UpdateLiveCaptionProjectionCaption(string captionText, int? highlightStart = null)
        {
            string next = captionText ?? string.Empty;

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

        private LiveCaptionRenderFrame PushNdiCaptionFrame(in LiveCaptionAsrText update)
        {
            int ndiLineCharLimit = Math.Clamp(_configManager?.LiveCaptionNdiLineCharLimit ?? 30, 8, 80);
            _liveCaptionNdiComposer.SetLineCharLimit(ndiLineCharLimit);
            return _liveCaptionNdiComposer.Push(update);
        }

        private void UpdateLiveCaptionNdiCaption(string captionText, int highlightStart)
        {
            PublishLiveCaptionToNdi(captionText, highlightStart);
        }

        private void PublishLiveCaptionToNdi(string captionText, int highlightStart)
        {
            if (_projectionNdiOutputManager == null || _configManager?.LiveCaptionNdiEnabled != true)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(captionText))
            {
                _projectionNdiOutputManager.PushTransparentIdleFrame();
                return;
            }

            try
            {
                int width = Math.Max(320, _configManager.ProjectionNdiWidth);
                int height = Math.Max(180, _configManager.ProjectionNdiHeight);
                using var frame = BuildLiveCaptionNdiFrame(width, height, captionText, highlightStart);
                _projectionNdiOutputManager.PublishFrameDirect(
                    frame,
                    transparent: false,
                    transparencyKeyColor: null);
            }
            catch (Exception ex)
            {
                LiveCaptionDebugLogger.Log($"CaptionNdi: publish failed, error={ex.Message}");
            }
        }

        private void StopProjectionNdiSenderIfUnused()
        {
            if (_projectionNdiOutputManager == null || _configManager == null)
            {
                return;
            }

            if (_configManager.LiveCaptionNdiEnabled || _configManager.ProjectionNdiEnabled)
            {
                return;
            }

            _projectionNdiOutputManager.Stop();
        }

        private SKBitmap BuildLiveCaptionNdiFrame(int width, int height, string captionText, int highlightStart)
        {
            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(LiveCaptionNdiFrameDefaults.TransparentBackground);

            string normalized = (captionText ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            int ndiLineCharLimit = Math.Clamp(_configManager?.LiveCaptionNdiLineCharLimit ?? 30, 8, 80);
            var lines = BuildNdiWrappedLines(normalized, ndiLineCharLimit, 2);

            string fontFamily = string.IsNullOrWhiteSpace(_configManager?.LiveCaptionNdiFontFamily)
                ? (string.IsNullOrWhiteSpace(_configManager?.LiveCaptionFontFamily)
                    ? "Microsoft YaHei UI"
                    : _configManager.LiveCaptionFontFamily.Trim())
                : _configManager.LiveCaptionNdiFontFamily.Trim();
            double resolvedNdiFontSize = _configManager?.LiveCaptionNdiFontSize > 0
                ? _configManager.LiveCaptionNdiFontSize
                : (_configManager?.LiveCaptionFontSize > 0 ? _configManager.LiveCaptionFontSize : 52);
            double resolvedNdiPadding = _configManager?.LiveCaptionNdiPadding > 0
                ? _configManager.LiveCaptionNdiPadding
                : (_configManager?.LiveCaptionPadding > 0 ? _configManager.LiveCaptionPadding : 56);
            double resolvedNdiLetterSpacing = _configManager?.LiveCaptionNdiLetterSpacing > 0
                ? _configManager.LiveCaptionNdiLetterSpacing
                : (_configManager?.LiveCaptionLetterSpacing ?? 0);
            float fontSize = (float)Math.Clamp(resolvedNdiFontSize, 20, 140);
            float padding = (float)Math.Clamp(resolvedNdiPadding, 12, 160);
            float availableWidth = Math.Max(120, width - (padding * 2));
            float letterSpacing = (float)Math.Clamp(resolvedNdiLetterSpacing, 0, 10);

            SKColor baseColor = ParseHexColor(_configManager?.LiveCaptionNdiTextColor, ParseHexColor(_configManager?.LiveCaptionTextColor, SKColors.White));
            SKColor latestColor = ParseHexColor(_configManager?.LiveCaptionNdiLatestTextColor, ParseHexColor(_configManager?.LiveCaptionLatestTextColor, new SKColor(255, 255, 0)));

            using var typeface = SKTypeface.FromFamilyName(fontFamily) ?? SKTypeface.FromFamilyName("Microsoft YaHei UI");
            using var baseFont = new SKFont(typeface, fontSize);
            using var latestFont = new SKFont(typeface, fontSize);
            using var basePaint = new SKPaint
            {
                IsAntialias = true,
                Color = baseColor
            };
            using var latestPaint = new SKPaint
            {
                IsAntialias = true,
                Color = latestColor
            };

            float maxLineWidth = 0;
            foreach (string line in lines)
            {
                maxLineWidth = Math.Max(maxLineWidth, MeasureLineWidth(baseFont, line, letterSpacing));
            }

            if (maxLineWidth > availableWidth && maxLineWidth > 0.1f)
            {
                float scale = Math.Max(0.5f, availableWidth / maxLineWidth);
                float scaled = Math.Max(18f, baseFont.Size * scale * 0.98f);
                baseFont.Size = scaled;
                latestFont.Size = scaled;

                // 字体缩放后重新计算宽度，确保后续居中坐标准确。
                maxLineWidth = 0f;
                foreach (string line in lines)
                {
                    maxLineWidth = Math.Max(maxLineWidth, MeasureLineWidth(baseFont, line, letterSpacing));
                }
            }

            double resolvedNdiLineGap = _configManager?.LiveCaptionNdiLineGap > 0
                ? _configManager.LiveCaptionNdiLineGap
                : (_configManager?.LiveCaptionLineGap ?? 30);
            float gap = lines.Count > 1 ? (float)Math.Clamp(resolvedNdiLineGap, 10, 60) : 0f;
            float lineHeight = baseFont.Size + gap;
            float totalHeight = (lines.Count * lineHeight);
            float blockTop = Math.Max(padding, height - padding - totalHeight);
            float baselineOffset = baseFont.Size;
            string ndiAlignment = NormalizeNdiAlignment(_configManager?.LiveCaptionNdiTextAlignment);
            float contentLeft = padding;

            int safeHighlight = Math.Clamp(highlightStart, 0, normalized.Length);
            int globalIndex = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                float y = blockTop + baselineOffset + (i * lineHeight);
                float lineWidth = MeasureLineWidth(baseFont, line, letterSpacing);
                float lineStartX = ndiAlignment switch
                {
                    "left" => contentLeft,
                    "right" => contentLeft + Math.Max(0f, availableWidth - lineWidth),
                    _ => contentLeft + Math.Max(0f, (availableWidth - lineWidth) * 0.5f)
                };
                int lineStart = globalIndex;
                int lineEnd = lineStart + line.Length;
                int split = Math.Clamp(safeHighlight - lineStart, 0, line.Length);

                if (split <= 0)
                {
                    DrawSpacedText(canvas, latestFont, latestPaint, line, lineStartX, y, letterSpacing);
                }
                else if (split >= line.Length)
                {
                    DrawSpacedText(canvas, baseFont, basePaint, line, lineStartX, y, letterSpacing);
                }
                else
                {
                    string prefix = line.Substring(0, split);
                    string suffix = line.Substring(split);
                    DrawSpacedText(canvas, baseFont, basePaint, prefix, lineStartX, y, letterSpacing);
                    float x2 = lineStartX + MeasureLineWidth(baseFont, prefix, letterSpacing);
                    DrawSpacedText(canvas, latestFont, latestPaint, suffix, x2, y, letterSpacing);
                }

                globalIndex = lineEnd;
            }

            return bitmap;
        }

        private static float MeasureLineWidth(SKFont font, string text, float letterSpacing)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            if (letterSpacing <= 0.01f)
            {
                return font.MeasureText(text);
            }

            float width = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                width += font.MeasureText(text[i].ToString());
                if (i < text.Length - 1)
                {
                    width += letterSpacing;
                }
            }
            return width;
        }

        private static void DrawSpacedText(SKCanvas canvas, SKFont font, SKPaint paint, string text, float x, float y, float letterSpacing)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (letterSpacing <= 0.01f)
            {
                canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
                return;
            }

            float cursor = x;
            for (int i = 0; i < text.Length; i++)
            {
                string ch = text[i].ToString();
                canvas.DrawText(ch, cursor, y, SKTextAlign.Left, font, paint);
                cursor += font.MeasureText(ch);
                if (i < text.Length - 1)
                {
                    cursor += letterSpacing;
                }
            }
        }

        private static List<string> BuildNdiWrappedLines(string source, int lineCharLimit, int maxLines)
        {
            var result = new List<string>(Math.Max(1, maxLines));
            string text = (source ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Add(string.Empty);
                return result;
            }

            int limit = Math.Clamp(lineCharLimit, 8, 80);
            for (int i = 0; i < text.Length; i += limit)
            {
                int len = Math.Min(limit, text.Length - i);
                result.Add(text.Substring(i, len));
            }

            if (result.Count > maxLines)
            {
                result = result.GetRange(result.Count - maxLines, maxLines);
            }

            if (result.Count == 0)
            {
                result.Add(string.Empty);
            }

            return result;
        }

        private static SKColor ParseHexColor(string hex, SKColor fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return fallback;
            }

            string value = hex.Trim().TrimStart('#');
            try
            {
                if (value.Length == 6)
                {
                    byte r = Convert.ToByte(value.Substring(0, 2), 16);
                    byte g = Convert.ToByte(value.Substring(2, 2), 16);
                    byte b = Convert.ToByte(value.Substring(4, 2), 16);
                    return new SKColor(r, g, b, 255);
                }

                if (value.Length == 8)
                {
                    byte a = Convert.ToByte(value.Substring(0, 2), 16);
                    byte r = Convert.ToByte(value.Substring(2, 2), 16);
                    byte g = Convert.ToByte(value.Substring(4, 2), 16);
                    byte b = Convert.ToByte(value.Substring(6, 2), 16);
                    return new SKColor(r, g, b, a);
                }
            }
            catch
            {
            }

            return fallback;
        }

        internal void SyncLiveCaptionProjectionCaptionForProjectionState(bool isProjectionActive)
        {
            if (!isProjectionActive)
            {
                _projectionManager?.HideProjectionCaptionOverlay();
                return;
            }

            if (_liveCaptionEngine?.IsRunning == true &&
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

            var tencentItem = new MenuItem
            {
                Header = BuildSelectedMenuHeader("腾讯", string.Equals(provider, "tencent", StringComparison.OrdinalIgnoreCase))
            };
            tencentItem.Click += (_, _) => SetLiveCaptionPlatform("tencent", "腾讯");
            platformMenu.Items.Add(tencentItem);

            var aliyunItem = new MenuItem
            {
                Header = BuildSelectedMenuHeader("阿里", string.Equals(provider, "aliyun", StringComparison.OrdinalIgnoreCase))
            };
            aliyunItem.Click += (_, _) => SetLiveCaptionPlatform("aliyun", "阿里");
            platformMenu.Items.Add(aliyunItem);

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
            double localLetterSpacing = Math.Clamp(_configManager.LiveCaptionLocalLetterSpacing, 0, 10);
            double localLineGap = Math.Clamp(_configManager.LiveCaptionLocalLineGap, 10, 60);
            double localLineHeight = localFontSize + localLineGap;
            string localTextColor = NormalizeColorHex(_configManager.LiveCaptionLocalTextColor, projectionTextColor);
            string localLatestColor = NormalizeColorHex(_configManager.LiveCaptionLocalLatestTextColor, projectionLatestColor);

            _liveCaptionOverlayWindow?.SetCaptionTypography(localFontFamily, localFontSize, localPadding, localLineHeight);
            _liveCaptionOverlayWindow?.SetCaptionLetterSpacing(localLetterSpacing);
            _liveCaptionOverlayWindow?.SetCaptionTextColor(localTextColor);
            _liveCaptionOverlayWindow?.SetLatestTextHighlightColor(localLatestColor);
            _projectionManager?.SetProjectionCaptionTypography(projectionFontFamily, projectionFontSize, projectionPadding, projectionLineHeight, projectionLetterSpacing, projectionTextColor, projectionLatestColor);
            RecalculateLiveCaptionComposerLayout(localFontSize, localPadding, localLetterSpacing);

            // 样式改动必须立即可见：强制用当前显示文本重绘一次，不依赖下一条 ASR 更新。
            if (!string.IsNullOrWhiteSpace(_liveCaptionComposer.CurrentDisplay))
            {
                _liveCaptionOverlayWindow?.UpdateCaption(_liveCaptionComposer.CurrentDisplay, _liveCaptionComposer.CurrentHighlightStart);
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
                "tencent" => "tencent",
                "aliyun" => "aliyun",
                "doubao" => "doubao",
                "funasr" => "doubao",
                _ => "baidu"
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

                LiveCaptionDebugLogger.Log($"F4: hide ignored source={source}, overlayVisible={_liveCaptionOverlayWindow.IsVisible}.");
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
            LiveCaptionDebugLogger.Log($"Overlay: hidden manually, source={source}.");
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
            if (_liveCaptionEngine == null || !_liveCaptionEngine.IsRunning || _liveCaptionOverlayWindow == null)
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
                _liveCaptionOverlayWindow.UpdateCaption(
                    _liveCaptionComposer.CurrentDisplay,
                    _liveCaptionComposer.CurrentHighlightStart);
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
