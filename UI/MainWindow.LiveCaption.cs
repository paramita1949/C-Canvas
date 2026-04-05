using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Managers;
using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.UI
{
    public partial class MainWindow : Window
    {
        private RealtimeCaptionEngine _liveCaptionEngine;
        private LiveCaptionOverlayWindow _liveCaptionOverlayWindow;
        private LiveCaptionDockMode _liveCaptionDockMode = LiveCaptionDockMode.Floating;
        private bool _isDisposingLiveCaption;
        private bool _liveCaptionOverlayManuallyHidden;
        private bool _liveCaptionProjectionCaptionHidden;
        private bool _liveCaptionToggleInProgress;
        private int _liveCaptionF4HotKeyId = -1;
        private LiveCaptionAudioSource _liveCaptionCurrentSource = LiveCaptionAudioSource.SystemLoopback;
        private readonly LiveCaptionDisplayComposer _liveCaptionComposer = new(lineCharLimit: 34, displayLineLimit: 2);
        private DateTime _liveCaptionLastToggleUtc = DateTime.MinValue;
        private const int LiveCaptionToggleDebounceMs = 220;

        private void EnsureLiveCaptionComponents()
        {
            if (_liveCaptionEngine == null)
            {
                _liveCaptionEngine = new RealtimeCaptionEngine(_configManager);
                _liveCaptionEngine.SubtitleUpdated += OnLiveCaptionSubtitleUpdated;
                _liveCaptionEngine.StatusChanged += OnLiveCaptionStatusChanged;
                LiveCaptionDebugLogger.Log("EnsureComponents: RealtimeCaptionEngine created and handlers attached.");
            }

            if (_liveCaptionOverlayWindow == null)
            {
                _liveCaptionOverlayWindow = new LiveCaptionOverlayWindow();
                _liveCaptionOverlayWindow.SettingsRequested += OpenLiveCaptionOverlaySettings;
                _liveCaptionOverlayWindow.ProjectionToggleRequested += ToggleProjectionFromLiveCaptionOverlay;
                _liveCaptionOverlayWindow.CaptionOrientationRequested += OnLiveCaptionOverlayCaptionOrientationRequested;
                _liveCaptionOverlayWindow.CaptionPositionRequested += OnLiveCaptionOverlayCaptionPositionRequested;
                _liveCaptionOverlayWindow.CloseRequested += StopLiveCaption;
                _liveCaptionOverlayWindow.FloatingBoundsChanged += OnLiveCaptionFloatingBoundsChanged;
                _liveCaptionOverlayWindow.Closed += LiveCaptionOverlayWindow_Closed;
                _liveCaptionOverlayWindow.SetTypingAnimationEnabled(false);
                _liveCaptionOverlayWindow.SetWorkAreaReservationEnabled(ShouldReserveWorkAreaForDockMode(_liveCaptionDockMode));
                _liveCaptionOverlayWindow.SetProjectionToggleState(_liveCaptionProjectionCaptionHidden);
                LoadLiveCaptionFloatingBoundsFromConfig();
                ApplyProjectionCaptionLayoutFromConfig();
                LiveCaptionDebugLogger.Log("EnsureComponents: OverlayWindow created and handlers attached.");
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
            bool wasRunning = _liveCaptionEngine?.IsRunning == true;
            LiveCaptionAudioSource source = _liveCaptionCurrentSource;

            if (wasRunning)
            {
                StopLiveCaption();
            }

            if (_liveCaptionEngine != null)
            {
                _liveCaptionEngine.SubtitleUpdated -= OnLiveCaptionSubtitleUpdated;
                _liveCaptionEngine.StatusChanged -= OnLiveCaptionStatusChanged;
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

        private void StartLiveCaption(LiveCaptionAudioSource source)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            EnsureLiveCaptionComponents();
            LiveCaptionDebugLogger.Log($"StartPerf: ensure-components={sw.ElapsedMilliseconds}ms");
            LiveCaptionDebugLogger.Log($"Start: request source={source}, configured={_liveCaptionEngine?.IsConfigured}, running={_liveCaptionEngine?.IsRunning}");
            if (!_liveCaptionEngine.IsConfigured)
            {
                ShowStatus("实时字幕未启动：请先完成 AI配置（ASR服务与鉴权信息）");
                LiveCaptionDebugLogger.Log("Start: blocked because configuration is incomplete.");
                return;
            }

            _liveCaptionOverlayManuallyHidden = false;
            _liveCaptionProjectionCaptionHidden = false;
            _liveCaptionComposer.Reset();

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
            _liveCaptionOverlayWindow.UpdateCaption(string.Empty, 0);
            _liveCaptionOverlayWindow.SetTypingAnimationEnabled(false);
            UpdateLiveCaptionProjectionActionState();
            SyncLiveCaptionProjectionCaptionForProjectionState(_projectionManager?.IsProjectionActive == true);
            ApplyMainWindowLiveCaptionReservation();
            _liveCaptionCurrentSource = source;
            SyncLiveCaptionVisibilityWithMainWindowContext("start");
            LiveCaptionDebugLogger.Log($"StartPerf: overlay-prepare={sw.ElapsedMilliseconds}ms");

            RegisterLiveCaptionF4HotKey();
            LiveCaptionDebugLogger.Log($"StartPerf: engine-start-scheduled={sw.ElapsedMilliseconds}ms");
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var startSw = System.Diagnostics.Stopwatch.StartNew();
                _liveCaptionEngine.Start(source);
                LiveCaptionDebugLogger.Log($"StartPerf: engine-start-done={startSw.ElapsedMilliseconds}ms");
                LiveCaptionDebugLogger.Log($"Start: engine started, dock={_liveCaptionDockMode}, overlayVisible={_liveCaptionOverlayWindow.IsVisible}");
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void StopLiveCaption()
        {
            LiveCaptionDebugLogger.Log($"Stop: request running={_liveCaptionEngine?.IsRunning}, overlayVisible={_liveCaptionOverlayWindow?.IsVisible}");
            _liveCaptionEngine?.Stop();
            PersistLiveCaptionFloatingBoundsToConfig("stop");

            if (_liveCaptionOverlayWindow != null && _liveCaptionOverlayWindow.IsVisible)
            {
                _liveCaptionOverlayWindow.Hide();
            }

            _liveCaptionOverlayManuallyHidden = false;
            _liveCaptionToggleInProgress = false;
            _liveCaptionProjectionCaptionHidden = false;
            _liveCaptionComposer.Reset();
            UpdateLiveCaptionProjectionActionState();
            _projectionManager?.HideProjectionCaptionOverlay();
            ApplyMainWindowLiveCaptionReservation();
            UnregisterLiveCaptionF4HotKey();
            LiveCaptionDebugLogger.Log("Stop: completed.");
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

        private void OnLiveCaptionSubtitleUpdated(LiveCaptionAsrText update)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_liveCaptionOverlayWindow == null)
                {
                    return;
                }

                LiveCaptionDebugLogger.Log($"SubtitleUpdated: raw='{TrimForLog(update.Text)}', final={update.IsFinal}");
                LiveCaptionRenderFrame frame = _liveCaptionComposer.Push(update);
                if (!frame.HasChanged)
                {
                    LiveCaptionDebugLogger.Log("SubtitleUpdated: unchanged, skipped.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(frame.Display))
                {
                    LiveCaptionDebugLogger.Log("SubtitleUpdated: display empty, skipped.");
                    return;
                }

                // 投影字幕与本机字幕窗显示状态解耦：
                // 即使本机字幕窗被手动隐藏，也要继续推送投影字幕。
                UpdateLiveCaptionProjectionCaption(frame.Display);

                if (_liveCaptionOverlayManuallyHidden)
                {
                    LiveCaptionDebugLogger.Log("SubtitleUpdated: overlay hidden manually, local render skipped (projection kept updating).");
                    return;
                }

                if (!_liveCaptionOverlayWindow.IsVisible)
                {
                    _liveCaptionOverlayWindow.Show();
                }

                _liveCaptionOverlayWindow.UpdateCaption(frame.Display, frame.HighlightStart);
                SyncLiveCaptionVisibilityWithMainWindowContext("subtitle-updated");
                ApplyMainWindowLiveCaptionReservation();
                LiveCaptionDebugLogger.Log($"SubtitleUpdated: rendered='{TrimForLog(frame.Display)}'");
            }));
        }

        private void OnLiveCaptionStatusChanged(string status)
        {
            LiveCaptionDebugLogger.Log($"EngineStatus: {status}");
            Dispatcher.BeginInvoke(new Action(() => ShowStatus(status)));
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
                    _liveCaptionEngine.Dispose();
                    _liveCaptionEngine = null;
                    LiveCaptionDebugLogger.Log("Dispose: engine disposed.");
                }

                if (_liveCaptionOverlayWindow != null)
                {
                    PersistLiveCaptionFloatingBoundsToConfig("dispose");
                    _liveCaptionOverlayWindow.SettingsRequested -= OpenLiveCaptionOverlaySettings;
                    _liveCaptionOverlayWindow.ProjectionToggleRequested -= ToggleProjectionFromLiveCaptionOverlay;
                    _liveCaptionOverlayWindow.CaptionOrientationRequested -= OnLiveCaptionOverlayCaptionOrientationRequested;
                    _liveCaptionOverlayWindow.CaptionPositionRequested -= OnLiveCaptionOverlayCaptionPositionRequested;
                    _liveCaptionOverlayWindow.CloseRequested -= StopLiveCaption;
                    _liveCaptionOverlayWindow.FloatingBoundsChanged -= OnLiveCaptionFloatingBoundsChanged;
                    _liveCaptionOverlayWindow.Closed -= LiveCaptionOverlayWindow_Closed;
                    _liveCaptionOverlayWindow.Close();
                    _liveCaptionOverlayWindow = null;
                    LiveCaptionDebugLogger.Log("Dispose: overlay disposed.");
                }

            _liveCaptionComposer.Reset();
            _liveCaptionOverlayManuallyHidden = false;
            _liveCaptionProjectionCaptionHidden = false;
            _projectionManager?.HideProjectionCaptionOverlay();
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

            var menu = new ContextMenu();

            var sourceMenu = new MenuItem { Header = "输入源" };
            bool isSystemSource = _liveCaptionCurrentSource == LiveCaptionAudioSource.SystemLoopback;
            var sourceSystemItem = new MenuItem
            {
                Header = BuildSelectedMenuHeader("系统声音", isSystemSource)
            };
            sourceSystemItem.Click += (_, _) => SetLiveCaptionAudioSource(LiveCaptionAudioSource.SystemLoopback);
            sourceMenu.Items.Add(sourceSystemItem);

            bool isMicSource = _liveCaptionCurrentSource == LiveCaptionAudioSource.Microphone;
            var sourceMicItem = new MenuItem
            {
                Header = BuildSelectedMenuHeader("麦克风", isMicSource)
            };
            sourceMicItem.Click += (_, _) => SetLiveCaptionAudioSource(LiveCaptionAudioSource.Microphone);
            sourceMenu.Items.Add(sourceMicItem);
            menu.Items.Add(sourceMenu);

            menu.Items.Add(new Separator());

            var platformMenu = new MenuItem { Header = "平台切换" };
            PopulateLiveCaptionPlatformMenu(platformMenu);
            menu.Items.Add(platformMenu);

            menu.PlacementTarget = _liveCaptionOverlayWindow.GetSettingsAnchorElement();
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            menu.HorizontalOffset = 6;
            menu.VerticalOffset = 0;
            menu.IsOpen = true;
            LiveCaptionDebugLogger.Log("Settings: context menu opened.");
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

        private void UpdateLiveCaptionProjectionCaption(string captionText)
        {
            if (_projectionManager?.IsProjectionActive != true)
            {
                return;
            }

            if (_liveCaptionProjectionCaptionHidden)
            {
                _projectionManager.HideProjectionCaptionOverlay();
                return;
            }

            _projectionManager.UpdateProjectionCaptionOverlay(captionText);
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
                _projectionManager?.UpdateProjectionCaptionOverlay(_liveCaptionComposer.CurrentDisplay);
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

        private static string NormalizeLiveCaptionProvider(string provider)
        {
            string normalized = (provider ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "baidu" => "baidu",
                "tencent" => "tencent",
                "aliyun" => "aliyun",
                "doubao" => "doubao",
                _ => "baidu"
            };
        }

        private void SetLiveCaptionPlatform(string provider, string displayName)
        {
            if (_configManager == null)
            {
                return;
            }

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
            if (_liveCaptionCurrentSource == source)
            {
                return;
            }

            _liveCaptionCurrentSource = source;
            LiveCaptionDebugLogger.Log($"AudioSource: switched to {source}.");

            if (_liveCaptionEngine?.IsRunning == true)
            {
                StartLiveCaption(source);
                ShowStatus(source == LiveCaptionAudioSource.SystemLoopback
                    ? "已切换为系统声音"
                    : "已切换为麦克风");
            }
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
            if (_liveCaptionEngine == null || !_liveCaptionEngine.IsRunning || _liveCaptionOverlayWindow == null)
            {
                return false;
            }

            // F4 已注册全局热键时，主窗口键盘事件只负责拦截，避免与全局回调双触发导致布局抖动。
            if (_liveCaptionF4HotKeyId > 0)
            {
                LiveCaptionDebugLogger.Log("F4: window-keydown intercepted; global hotkey callback will toggle.");
                return true;
            }

            TryToggleLiveCaptionOverlayVisibilityInternal("window-keydown-fallback");
            return true;
        }

        private void ToggleLiveCaptionOverlayVisibility()
        {
            TryToggleLiveCaptionOverlayVisibilityInternal("direct-call");
        }

        private void TryToggleLiveCaptionOverlayVisibilityInternal(string source)
        {
            if (_liveCaptionEngine == null || !_liveCaptionEngine.IsRunning || _liveCaptionOverlayWindow == null)
            {
                LiveCaptionDebugLogger.Log($"F4:Toggle ignored source={source}, engineNull={_liveCaptionEngine == null}, running={_liveCaptionEngine?.IsRunning}, overlayNull={_liveCaptionOverlayWindow == null}");
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
                    _liveCaptionOverlayManuallyHidden = true;
                    _liveCaptionOverlayWindow.Hide();
                    ApplyMainWindowLiveCaptionReservation();
                    LiveCaptionDebugLogger.Log($"F4: overlay hidden manually, source={source}.");
                    return;
                }

                _liveCaptionOverlayManuallyHidden = false;
                if (_liveCaptionOverlayWindow.GetDockMode() != _liveCaptionDockMode)
                {
                    _liveCaptionOverlayWindow.SetDockMode(_liveCaptionDockMode);
                }
                _liveCaptionOverlayWindow.SetWorkAreaReservationEnabled(ShouldReserveWorkAreaForDockMode(_liveCaptionDockMode));
                _liveCaptionOverlayWindow.Show();
                _liveCaptionOverlayWindow.RefreshDockLayoutNow();
                _liveCaptionOverlayWindow.UpdateCaption(
                    _liveCaptionComposer.CurrentDisplay,
                    _liveCaptionComposer.CurrentHighlightStart);
                SyncLiveCaptionVisibilityWithMainWindowContext("f4-show");
                ApplyMainWindowLiveCaptionReservation();

                LiveCaptionDebugLogger.Log($"F4: overlay shown, source={source}.");
            }
            finally
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _liveCaptionToggleInProgress = false;
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
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
