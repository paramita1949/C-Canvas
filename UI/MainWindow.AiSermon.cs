using System;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Services.Ai;
using ImageColorChanger.Services.LiveCaption;
using ImageColorChanger.UI.Modules;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private AiAssistantPanelWindow _aiAssistantPanelWindow;
        private AiPlatformWindow _aiPlatformWindow;
        private AiSermonConversationCoordinator _aiSermonCoordinator;
        private readonly AiAsrTurnAggregator _aiAsrTurnAggregator = new();
        private System.Windows.Threading.DispatcherTimer _aiAsrFlushTimer;
        private bool _aiSermonReceiveAsr;
        private bool _aiSermonDebugEnabled;
        private DateTimeOffset _lastInterimDebugAt = DateTimeOffset.MinValue;
        private int _aiPanelF5HotKeyId = -1;

        private async Task AnalyzeTextProjectWithAiAsync(ProjectTreeItem item, bool startAsr)
        {
            if (item == null || (item.Type != TreeItemType.Project && item.Type != TreeItemType.TextProject))
            {
                ShowStatus("请选择幻灯片项目");
                return;
            }

            EnsureAiSermonPanel();
            EnsureAiSermonCoordinator();
            _aiAssistantPanelWindow.SetProjectTitle(item.Name);
            _aiAssistantPanelWindow.SetModelName(_configManager.DeepSeekModel);
            _aiAssistantPanelWindow.Show();
            _aiAssistantPanelWindow.Activate();

            await _aiSermonCoordinator.StartProjectAsync(item.Id, CancellationToken.None);
            await RefreshAiSpeakerListAsync();

            if (startAsr)
            {
                SetAiSermonReceiveAsr(true);
                _configManager.LiveCaptionRealtimeEnabled = true;
                StartLiveCaption(_liveCaptionCurrentSource);
            }
        }

        private async Task SetTextProjectAsAiSermonContextAsync(ProjectTreeItem item)
        {
            if (item == null)
            {
                ShowStatus("请选择幻灯片项目");
                return;
            }

            EnsureAiSermonPanel();
            EnsureAiSermonCoordinator();
            _aiAssistantPanelWindow.SetProjectTitle(item.Name);
            _aiAssistantPanelWindow.SetModelName(_configManager.DeepSeekModel);
            _aiAssistantPanelWindow.Show();
            _aiAssistantPanelWindow.Activate();
            await _aiSermonCoordinator.StartProjectAsync(item.Id, CancellationToken.None);
            await RefreshAiSpeakerListAsync();
            ShowStatus($"AI字幕已读取本场主题：{item.Name}");
        }

        private void OpenAiPlatformWindow(bool focusDeepSeekConfig = false)
        {
            if (_aiPlatformWindow == null)
            {
                _aiPlatformWindow = new AiPlatformWindow(_configManager)
                {
                    Owner = this
                };
                _aiPlatformWindow.AiCaptionRequested += OpenAiSubtitlePanel;
                _aiPlatformWindow.AsrEngineSettingsRequested += OpenAiConfigFile;
                _aiPlatformWindow.Closed += (_, _) => _aiPlatformWindow = null;
            }

            _aiPlatformWindow.RefreshFromConfig();
            _aiPlatformWindow.Show();
            _aiPlatformWindow.Activate();

            if (focusDeepSeekConfig)
            {
                _aiPlatformWindow.FocusDeepSeekConfig();
            }
        }

        private void OpenAiSubtitlePanel()
        {
            EnsureAiSermonPanel();
            EnsureAiSermonCoordinator();
            _aiAssistantPanelWindow.SetModelName(_configManager.DeepSeekModel);
            _aiAssistantPanelWindow.Show();
            _aiAssistantPanelWindow.Activate();
            _ = RefreshAiSpeakerListAsync(_aiSermonCoordinator?.CurrentSpeakerName ?? string.Empty);
            _ = RefreshAiHistoryInPanelAsync();
        }

        private void OpenAiRealtimeSubtitle()
        {
            _configManager.LiveCaptionRealtimeEnabled = true;
            StartLiveCaption(_liveCaptionCurrentSource);
            OpenLiveCaptionPanel();
        }

        private void EnsureAiSermonPanel()
        {
            RegisterAiPanelF5HotKey();

            if (_aiAssistantPanelWindow != null)
            {
                return;
            }

            _aiAssistantPanelWindow = new AiAssistantPanelWindow(_configManager)
            {
                Owner = this
            };
            EnsureAiAsrFlushTimer();
            SetAiSermonReceiveAsr(true);
            _aiAssistantPanelWindow.DebugModeChanged += enabled => _aiSermonDebugEnabled = enabled;
            _aiAssistantPanelWindow.ModelChanged += model =>
            {
                _aiAssistantPanelWindow.SetModelName(model);
                ShowStatus($"AI模型已切换：{model}");
            };
            _aiAssistantPanelWindow.SpeakerApplied += speaker => _ = ApplyAiSpeakerAsync(speaker);
            _aiAssistantPanelWindow.SpeakerDeleteRequested += speaker => _ = DeleteAiSpeakerAsync(speaker);
            _aiAssistantPanelWindow.OutputModeChanged += mode => _ = _aiSermonCoordinator?.SetOutputModeAsync(mode);
            _aiAssistantPanelWindow.DialectSchemeChanged += (enabled, tags) => _ = _aiSermonCoordinator?.SetDialectSchemeAsync(enabled, tags);
            _aiAssistantPanelWindow.HistoryRequested += () => _ = RefreshAiHistoryInPanelAsync();
            _aiAssistantPanelWindow.HistorySessionDeleteRequested += sessionId => _ = DeleteAiHistorySessionAsync(sessionId);
            _aiAssistantPanelWindow.HistoryMessageDeleteRequested += messageId => _ = DeleteAiHistoryMessageAsync(messageId);
            _aiAssistantPanelWindow.Closed += (_, _) =>
            {
                _aiAssistantPanelWindow = null;
                _aiSermonReceiveAsr = false;
                _aiSermonDebugEnabled = false;
                _aiAsrFlushTimer?.Stop();
            };
        }

        private void RegisterAiPanelF5HotKey()
        {
            if (_globalHotKeyManager == null || _aiPanelF5HotKeyId > 0)
            {
                return;
            }

            _aiPanelF5HotKeyId = _globalHotKeyManager.RegisterHotKey(
                System.Windows.Input.Key.F5,
                System.Windows.Input.ModifierKeys.None,
                () => Dispatcher.BeginInvoke(new Action(() =>
                    ToggleAiAssistantPanelVisibilityByShortcut())));
        }

        internal bool ToggleAiAssistantPanelVisibilityByShortcut()
        {
            EnsureAiSermonPanel();
            EnsureAiSermonCoordinator();
            _aiAssistantPanelWindow.SetModelName(_configManager.DeepSeekModel);

            if (_aiAssistantPanelWindow.IsVisible)
            {
                _aiAssistantPanelWindow.Hide();
            }
            else
            {
                _aiAssistantPanelWindow.Show();
                _aiAssistantPanelWindow.Activate();
                _ = RefreshAiSpeakerListAsync(_aiSermonCoordinator?.CurrentSpeakerName ?? string.Empty);
                _ = RefreshAiHistoryInPanelAsync();
            }

            return true;
        }

        private void EnsureAiAsrFlushTimer()
        {
            if (_aiAsrFlushTimer != null)
            {
                return;
            }

            _aiAsrFlushTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background,
                Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _aiAsrFlushTimer.Tick += (_, _) => FlushPendingAiAsrTurn();
            _aiAsrFlushTimer.Start();
        }

        private void EnsureAiSermonCoordinator()
        {
            if (_aiSermonCoordinator != null)
            {
                return;
            }

            _aiSermonCoordinator = _mainWindowServices.GetRequired<AiSermonConversationCoordinator>();
            _aiSermonCoordinator.MessageAppended += message =>
            {
                _aiAssistantPanelWindow?.AppendUserMessage(message.Name, message.Content);
            };
            _aiSermonCoordinator.AssistantMessageStarted += () =>
            {
                _aiAssistantPanelWindow?.BeginAssistantMessage();
            };
            _aiSermonCoordinator.AssistantDeltaReceived += delta =>
            {
                _aiAssistantPanelWindow?.AppendAssistantDelta(delta);
            };
            _aiSermonCoordinator.StatusChanged += status =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _aiAssistantPanelWindow?.AppendStatus(status);
                    ShowStatus(status);
                }));
            };
            _aiSermonCoordinator.ScriptureCandidateAccepted += candidate =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AddPinyinHistoryToEmptySlot(
                        candidate.BookId,
                        candidate.Chapter,
                        candidate.StartVerse,
                        candidate.EndVerse);
                    string msg = $"AI已加入历史记录：{candidate.BookName}{candidate.Chapter}章{candidate.StartVerse}节";
                    _aiAssistantPanelWindow?.AppendStatus(msg);
                    ShowStatus(msg);
                }));
            };
        }

        private async Task ApplyAiSpeakerAsync(string speaker)
        {
            EnsureAiSermonCoordinator();
            await _aiSermonCoordinator.SetSpeakerAsync(speaker, CancellationToken.None);
            await RefreshAiSpeakerListAsync(speaker);
            await RefreshAiHistoryInPanelAsync();
        }

        private async Task DeleteAiSpeakerAsync(string speaker)
        {
            EnsureAiSermonCoordinator();
            if (string.IsNullOrWhiteSpace(speaker) ||
                string.Equals(speaker.Trim(), "未标记讲师", StringComparison.Ordinal))
            {
                return;
            }

            bool deletingCurrent = string.Equals(
                _aiSermonCoordinator.CurrentSpeakerName,
                speaker.Trim(),
                StringComparison.Ordinal);
            await _aiSermonCoordinator.DeleteSpeakerAsync(speaker.Trim());
            var remainingBindings = _configManager.AiSermonSpeakerDialectBindings
                .Where(entry => !string.Equals(entry?.Speaker?.Trim(), speaker.Trim(), StringComparison.Ordinal))
                .ToArray();
            _configManager.AiSermonSpeakerDialectBindings = remainingBindings;
            string nextSpeaker = deletingCurrent ? "未标记讲师" : _aiSermonCoordinator.CurrentSpeakerName;
            if (deletingCurrent)
            {
                await _aiSermonCoordinator.SetSpeakerAsync(nextSpeaker, CancellationToken.None);
            }

            await RefreshAiSpeakerListAsync(nextSpeaker);
            await RefreshAiHistoryInPanelAsync();
        }

        private async Task RefreshAiSpeakerListAsync(string currentSpeaker = "")
        {
            if (_aiSermonCoordinator == null || _aiAssistantPanelWindow == null)
            {
                return;
            }

            var names = await _aiSermonCoordinator.GetSpeakerNamesAsync();
            _aiAssistantPanelWindow.SetSpeakerNames(names, currentSpeaker);
        }

        private async Task RefreshAiHistoryInPanelAsync()
        {
            EnsureAiSermonCoordinator();
            var groups = await _aiSermonCoordinator.GetHistoryGroupsAsync();
            _aiAssistantPanelWindow?.SetHistoryGroups(groups);
        }

        private async Task DeleteAiHistorySessionAsync(int sessionId)
        {
            EnsureAiSermonCoordinator();
            await _aiSermonCoordinator.DeleteHistorySessionAsync(sessionId);
            await RefreshAiHistoryInPanelAsync();
        }

        private async Task DeleteAiHistoryMessageAsync(int messageId)
        {
            EnsureAiSermonCoordinator();
            await _aiSermonCoordinator.DeleteHistoryMessageAsync(messageId);
            await RefreshAiHistoryInPanelAsync();
        }

        private void SetAiSermonReceiveAsr(bool enabled)
        {
            bool changed = _aiSermonReceiveAsr != enabled;
            _aiSermonReceiveAsr = enabled;
            _aiAssistantPanelWindow?.SetReceiveAsr(enabled);
            if (enabled)
            {
                _aiAsrFlushTimer?.Start();
            }
            else
            {
                _aiAsrFlushTimer?.Stop();
            }
            if (changed)
            {
                ShowStatus(enabled ? "AI字幕已开始接收ASR" : "AI字幕已停止接收ASR");
            }
        }

        private void ForwardLiveCaptionAsrToAi(LiveCaptionAsrText update)
        {
            if (_aiSermonCoordinator == null)
            {
                return;
            }

            if (!_aiSermonReceiveAsr)
            {
                if (_aiSermonDebugEnabled && update.IsFinal && !string.IsNullOrWhiteSpace(update.Text))
                {
                    _aiAssistantPanelWindow?.AppendDebug("ASR已收到，但 AI 面板未开启 ASR 接收。");
                }
                return;
            }

            if (_aiAsrTurnAggregator.TryAccept(update.Text, update.IsFinal, DateTimeOffset.Now, out var turn))
            {
                SubmitAsrTurnToAi(turn);
            }
            else if (_aiSermonDebugEnabled && update.IsFinal && !string.IsNullOrWhiteSpace(update.Text))
            {
                _aiAssistantPanelWindow?.AppendDebug("ASR未转发：被聚合器过滤（重复/过短/无效）。");
            }
            else if (_aiSermonDebugEnabled && !update.IsFinal && !string.IsNullOrWhiteSpace(update.Text))
            {
                var now = DateTimeOffset.Now;
                if (now - _lastInterimDebugAt >= TimeSpan.FromSeconds(6))
                {
                    _lastInterimDebugAt = now;
                    _aiAssistantPanelWindow?.AppendDebug("收到 interim ASR，正在等待聚合阈值或 final 结果。");
                }
            }
        }

        private void FlushPendingAiAsrTurn()
        {
            if (_aiSermonCoordinator == null || !_aiSermonReceiveAsr)
            {
                return;
            }

            if (_aiAsrTurnAggregator.TryFlushPendingInterim(DateTimeOffset.Now, out var turn))
            {
                SubmitAsrTurnToAi(turn);
            }
        }

        private void SubmitAsrTurnToAi(AiAsrTurnEnvelope turn)
        {
            if (_aiSermonDebugEnabled)
            {
                string preview = TrimDebugPreview(turn.Text);
                string source = turn.IsFinal ? "final" : "interim-silence";
                _aiAssistantPanelWindow?.AppendDebug($"ASR已转发到DeepSeek（{source}）：{preview}");
            }

            _ = _aiSermonCoordinator.SendAsrTurnAsync(turn, CancellationToken.None);
        }

        private static string TrimDebugPreview(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length <= 36)
            {
                return value;
            }

            return value.Substring(0, 36) + "...";
        }
    }
}
