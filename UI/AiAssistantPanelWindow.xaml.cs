using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace ImageColorChanger.UI
{
    public partial class AiAssistantPanelWindow : Window
    {
        private const string AiSermonPanelPlacementKey = "ai.sermon.panel";
        private const double CollapsedPanelHeight = 104;
        private const string UnlabeledSpeakerName = "未标记讲师";
        private const string DefaultCollapsedStatus = "等待幻灯片 / 实时识别";
        private const string InsertedScriptureStatusPrefix = "AI已加入历史记录：";
        private readonly ConfigManager _configManager;
        private TextBlock _currentAssistantText;
        private bool _isUpdatingThresholdUi;
        private bool _isUpdatingOutputModeUi;
        private bool _isUpdatingViewModeUi;
        private bool _uiReady;
        private bool _isCollapsed;
        private string _lastAppliedSpeaker = string.Empty;
        private readonly List<string> _speakerNames = new();
        private readonly List<string> _dialectTags = new();
        private readonly Dictionary<string, HashSet<string>> _speakerDialectBindings = new(StringComparer.Ordinal);
        private bool _dialectSchemeEnabled;
        private readonly HashSet<string> _selectedDialectTags = new(StringComparer.Ordinal);
        private double _expandedHeight = 500;
        private string _lastAsrConnectionStatus = string.Empty;

        public event Action<bool> DebugModeChanged;
        public event Action<string> SpeakerApplied;
        public event Action<string> SpeakerDeleteRequested;
        public event Action<string, string> SpeakerRenameRequested;
        public event Action<string> OutputModeChanged;
        public event Action<string> ModelChanged;
        public event Action<string> ScriptureCorrectionSubmitted;
        public event Action<bool, IReadOnlyList<string>> DialectSchemeChanged;
        public event Func<Task> EndSessionRequested;
        public event Action HistoryRequested;
        public event Action<int> HistorySessionDeleteRequested;
        public event Action<int> HistoryMessageDeleteRequested;

        public AiAssistantPanelWindow(ConfigManager configManager)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            InitializeComponent();
            SetModelName(_configManager.DeepSeekModel);
            LoadDialectSchemeFromConfig();
            SyncWriteThresholdUiFromConfig();
            SyncPanelOpacityUiFromConfig();
            ApplyPanelOpacityFromSlider();
            SetActiveView(showHistory: false, requestRefresh: false);
            WindowPlacementTracker.Restore(
                this,
                _configManager,
                AiSermonPanelPlacementKey,
                includeSize: true,
                restoreCollapsedState: RestoreCollapsedState);
            WindowPlacementTracker.Track(
                this,
                _configManager,
                AiSermonPanelPlacementKey,
                includeSize: true,
                getCollapsedState: () => _isCollapsed);
            _uiReady = true;
        }

        private void LoadDialectSchemeFromConfig()
        {
            _dialectSchemeEnabled = _configManager.AiSermonDialectSchemeEnabled;
            _dialectTags.Clear();
            _selectedDialectTags.Clear();
            _speakerDialectBindings.Clear();
            foreach (string tag in _configManager.AiSermonDialectTags)
            {
                if (!string.IsNullOrWhiteSpace(tag) && !_dialectTags.Contains(tag, StringComparer.Ordinal))
                {
                    _dialectTags.Add(tag.Trim());
                }
            }

            if (_dialectTags.Count == 0)
            {
                _dialectTags.Add("国语");
                _dialectTags.Add("吴语");
                _dialectTags.Add("宁波话");
                _dialectTags.Add("绍兴话");
            }

            foreach (string selected in _configManager.AiSermonSelectedDialectTags)
            {
                if (string.IsNullOrWhiteSpace(selected))
                {
                    continue;
                }

                string value = selected.Trim();
                if (!_dialectTags.Contains(value, StringComparer.Ordinal))
                {
                    _dialectTags.Insert(0, value);
                }

                _selectedDialectTags.Add(value);
            }

            if (_selectedDialectTags.Count == 0)
            {
                _selectedDialectTags.Add("国语");
            }

            foreach (var entry in _configManager.AiSermonSpeakerDialectBindings)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Speaker))
                {
                    continue;
                }

                string speaker = entry.Speaker.Trim();
                var tags = new HashSet<string>(StringComparer.Ordinal);
                foreach (string tag in entry.Tags ?? Array.Empty<string>())
                {
                    string value = (tag ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        tags.Add(value);
                    }
                }

                _speakerDialectBindings[speaker] = tags;
            }
        }

        public void SetProjectTitle(string title)
        {
            Dispatcher.Invoke(() =>
            {
                ProjectTitleText.Text = string.IsNullOrWhiteSpace(title) ? "主题待读取" : title.Trim();
            });
        }

        public void SetReceiveAsr(bool enabled)
        {
            SetAsrConnectionState(enabled, realtimeConnected: false);
        }

        public void SetAsrConnectionState(bool aiReceiveAsr, bool realtimeConnected)
        {
            string status = BuildAsrConnectionStatus(aiReceiveAsr, realtimeConnected);
            if (string.Equals(_lastAsrConnectionStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            _lastAsrConnectionStatus = status;
            AppendStatus(status);
        }

        public static string BuildAsrConnectionStatus(bool aiReceiveAsr, bool realtimeConnected)
        {
            return aiReceiveAsr && realtimeConnected ? "ASR已连接" : "ASR未启用";
        }

        public void SetModelName(string modelName)
        {
            Dispatcher.Invoke(() =>
            {
                string value = string.IsNullOrWhiteSpace(modelName)
                    ? "DeepSeek"
                    : modelName.Trim();
                ModelNameText.Text = value;
                UpdateModelOptionVisual(value);
                BalanceStatusText.Visibility = IsGeminiModel(value) ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        public void SetBalanceStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (IsGeminiModel(_configManager.DeepSeekModel))
                {
                    BalanceStatusText.Visibility = Visibility.Collapsed;
                    return;
                }

                BalanceStatusText.Visibility = Visibility.Visible;
                BalanceStatusText.Text = status.Trim();
            });
        }

        private void UpdateModelOptionVisual(string modelName)
        {
            if (ModelOptionFlash == null || ModelOptionPro == null || ModelOptionGeminiFlash == null)
            {
                return;
            }

            bool isFlash = string.Equals(modelName, "deepseek-v4-flash", StringComparison.OrdinalIgnoreCase);
            bool isPro = string.Equals(modelName, "deepseek-v4-pro", StringComparison.OrdinalIgnoreCase);
            bool isGeminiFlash = string.Equals(modelName, "gemini-3.5-flash", StringComparison.OrdinalIgnoreCase);
            ModelOptionFlash.Background = isFlash ? CreateBrush("#2F8CD7") : System.Windows.Media.Brushes.Transparent;
            ModelOptionPro.Background = isPro ? CreateBrush("#2F8CD7") : System.Windows.Media.Brushes.Transparent;
            ModelOptionGeminiFlash.Background = isGeminiFlash ? CreateBrush("#2F8CD7") : System.Windows.Media.Brushes.Transparent;
        }

        private static bool IsGeminiModel(string modelName)
        {
            return !string.IsNullOrWhiteSpace(modelName) &&
                   modelName.Trim().StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);
        }

        public void SetSpeakerNames(IEnumerable<string> speakerNames, string currentSpeaker = "")
        {
            Dispatcher.Invoke(() =>
            {
                if (SpeakerSelectionText == null || SpeakerPopupStack == null)
                {
                    return;
                }

                string currentText = string.IsNullOrWhiteSpace(currentSpeaker)
                    ? _lastAppliedSpeaker
                    : currentSpeaker.Trim();
                _speakerNames.Clear();
                if (speakerNames != null)
                {
                    foreach (string name in speakerNames)
                    {
                        string value = (name ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(value) &&
                            !string.Equals(value, UnlabeledSpeakerName, StringComparison.Ordinal) &&
                            !_speakerNames.Contains(value, StringComparer.Ordinal))
                        {
                            _speakerNames.Add(value);
                        }
                    }
                }

                string next = string.IsNullOrWhiteSpace(currentText) ? UnlabeledSpeakerName : currentText;
                if (!string.Equals(next, UnlabeledSpeakerName, StringComparison.Ordinal) &&
                    !_speakerNames.Contains(next, StringComparer.Ordinal))
                {
                    _speakerNames.Add(next);
                }

                _lastAppliedSpeaker = next;
                LoadDialectTagsForSpeaker(next);
                DialectSchemeChanged?.Invoke(_selectedDialectTags.Count > 0, _selectedDialectTags.ToList());
                UpdateSpeakerSelectionVisual(next);
                RebuildSpeakerMenu(next);
            });
        }

        public void SetOutputMode(string outputMode)
        {
            Dispatcher.Invoke(() =>
            {
                _isUpdatingOutputModeUi = true;
                try
                {
                    bool detailed = string.Equals(outputMode, "detailed", StringComparison.OrdinalIgnoreCase);
                    ConciseModeButton.IsChecked = !detailed;
                    DetailedModeButton.IsChecked = detailed;
                }
                finally
                {
                    _isUpdatingOutputModeUi = false;
                }
            });
        }

        public void SetHistoryGroups(IReadOnlyList<AiSpeakerSessionGroup> groups)
        {
            Dispatcher.Invoke(() =>
            {
                HistoryStackPanel.Children.Clear();
                bool hasItems = groups != null && groups.Count > 0;
                HistoryEmptyStatePanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
                if (!hasItems)
                {
                    return;
                }

                foreach (var group in groups)
                {
                    HistoryStackPanel.Children.Add(CreateSpeakerHistoryBlock(group));
                }

                HistoryScrollViewer.ScrollToTop();
            });
        }

        public void AppendUserMessage(string name, string content)
        {
            if (string.Equals(name, "asr", StringComparison.Ordinal))
            {
                // 面板不显示原始 ASR 字幕，避免和底部实时字幕重复。
                return;
            }

            string label = name switch
            {
                "project_context" => "主题解读",
                "scripture_correction" => "经文修正",
                _ => "你"
            };
            string display = name switch
            {
                "project_context" => "读取幻灯片项目，建立本场主题、经文范围和后续ASR理解上下文。",
                "scripture_correction" => ExtractScriptureCorrectionDisplay(content),
                _ => content
            };
            AddMessage(label, display, "#8DEAFF");
        }

        public void BeginAssistantMessage()
        {
            Dispatcher.Invoke(() =>
            {
                HideEmptyState();
                _currentAssistantText = new TextBlock
                {
                    Text = BuildMessageHeader("摘要") + Environment.NewLine,
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Foreground = CreateBrush("#F4FBFF"),
                    FontSize = 12,
                    LineHeight = 18,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                ApplyMessageTextWidth(_currentAssistantText);
                MessageStackPanel.Children.Add(_currentAssistantText);
                ScrollToEnd();
            });
        }

        public void AppendAssistantDelta(string delta)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (_currentAssistantText == null)
                {
                    BeginAssistantMessage();
                }

                _currentAssistantText.Text += delta;
                ScrollToEnd();
            });
        }

        public void AppendStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                HideEmptyState();
                string text = status.Trim();
                StatusText.Text = text;
                UpdateCollapsedStatus(text);
                UpdateCollapsedScripture(text);

                if (!ShouldAppendStatusToTimeline(text))
                {
                    return;
                }

                AddMessage("系统", text, "#BFEFFF");
            });
        }

        public static bool ShouldAppendStatusToTimeline(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return false;
            }

            string text = status.Trim();
            return text.StartsWith("AI缓存", StringComparison.Ordinal)
                || string.Equals(text, "ASR已连接", StringComparison.Ordinal)
                || string.Equals(text, "ASR未启用", StringComparison.Ordinal);
        }

        public static string ExtractCollapsedScriptureText(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            string text = status.Trim();
            return text.StartsWith(InsertedScriptureStatusPrefix, StringComparison.Ordinal)
                ? text.Substring(InsertedScriptureStatusPrefix.Length).Trim()
                : string.Empty;
        }

        private void UpdateCollapsedStatus(string status)
        {
            if (CollapsedStatusText == null)
            {
                return;
            }

            string text = string.IsNullOrWhiteSpace(status) ? DefaultCollapsedStatus : status.Trim();
            CollapsedStatusText.Text = text;
        }

        private void UpdateCollapsedScripture(string status)
        {
            if (CollapsedScriptureText == null)
            {
                return;
            }

            string scripture = ExtractCollapsedScriptureText(status);
            if (string.IsNullOrWhiteSpace(scripture))
            {
                return;
            }

            CollapsedScriptureText.Text = $"经文：{scripture}";
            CollapsedScriptureText.Visibility = Visibility.Visible;
        }

        public void AppendDebug(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                HideEmptyState();
                AddMessage("调试", message.Trim(), "#9FDEFF");
            });
        }

        private void AddMessage(string sender, string content, string foreground)
        {
            Dispatcher.Invoke(() =>
            {
                HideEmptyState();
                string label = string.Equals(sender, "系统", StringComparison.Ordinal) ? "状态" : sender;
                var textBlock = new TextBlock
                {
                    Text = $"{BuildMessageHeader(label)} {(content ?? string.Empty)}",
                    FontWeight = FontWeights.Medium,
                    Foreground = CreateBrush(foreground),
                    FontSize = 12,
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    LineHeight = 18,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                ApplyMessageTextWidth(textBlock);
                MessageStackPanel.Children.Add(textBlock);
                ScrollToEnd();
            });
        }

        private FrameworkElement CreateSpeakerHistoryBlock(AiSpeakerSessionGroup group)
        {
            string speakerName = DisplaySpeakerName(group.SpeakerName);
            if (string.IsNullOrWhiteSpace(speakerName))
            {
                speakerName = "传道人";
            }

            var root = new Border
            {
                Background = CreateBrush("#24192E3F"),
                BorderBrush = CreateBrush("#365A78"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var speakerExpander = new Expander
            {
                IsExpanded = false,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Header = new TextBlock
                {
                    Text = $"{speakerName} · {group.Sessions.Count} 场",
                    Foreground = CreateBrush("#F4FBFF"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            root.Child = speakerExpander;

            var body = new StackPanel { Margin = new Thickness(4, 6, 4, 2) };
            speakerExpander.Content = body;

            if (!string.IsNullOrWhiteSpace(group.StyleSummary))
            {
                var summaryBorder = new Border
                {
                    Background = CreateBrush("#1C0B1524"),
                    BorderBrush = CreateBrush("#2C4A63"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                summaryBorder.Child = new TextBlock
                {
                    Text = TrimForPanel(group.StyleSummary, 280),
                    Foreground = CreateBrush("#A9C8DD"),
                    FontSize = 11,
                    LineHeight = 17,
                    TextWrapping = TextWrapping.Wrap
                };
                body.Children.Add(summaryBorder);
            }

            foreach (var session in group.Sessions.Take(8))
            {
                body.Children.Add(CreateSessionHistoryBlock(session));
            }

            return root;
        }

        private FrameworkElement CreateSessionHistoryBlock(AiSermonSessionHistory session)
        {
            var root = new Border
            {
                Background = CreateBrush("#1C0B1524"),
                BorderBrush = CreateBrush("#2C4A63"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var sessionExpander = new Expander
            {
                IsExpanded = false,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            root.Child = sessionExpander;

            var header = new DockPanel { LastChildFill = true };
            var deleteButton = CreateInlineDeleteButton(
                "删除本场",
                "确认删除本场历史会话吗？此操作不可恢复。",
                () => HistorySessionDeleteRequested?.Invoke(session.Id));
            DockPanel.SetDock(deleteButton, Dock.Right);
            header.Children.Add(deleteButton);
            header.Children.Add(new TextBlock
            {
                Text = $"{session.StartedAt:MM-dd HH:mm} · {session.Title}",
                Foreground = CreateBrush("#DDF5FF"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            sessionExpander.Header = header;

            var stack = new StackPanel { Margin = new Thickness(4, 4, 4, 0) };
            sessionExpander.Content = stack;

            if (!string.IsNullOrWhiteSpace(session.Summary))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "本场摘要：" + TrimForPanel(session.Summary, 220),
                    Foreground = CreateBrush("#A9C8DD"),
                    FontSize = 11,
                    LineHeight = 17,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 4)
                });
            }

            string settlementText = BuildSessionSettlementText(session);
            if (!string.IsNullOrWhiteSpace(settlementText))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = settlementText,
                    Foreground = CreateBrush("#7EEBFF"),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 4)
                });
            }

            if (session.Messages.Count > 0)
            {
                var detailsExpander = new Expander
                {
                    IsExpanded = false,
                    Header = new TextBlock
                    {
                        Text = $"会话明细（{Math.Min(session.Messages.Count, 5)}）",
                        Foreground = CreateBrush("#A9C8DD"),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold
                    },
                    Margin = new Thickness(0, 4, 0, 0)
                };

                var detailStack = new StackPanel();
                foreach (var message in session.Messages.TakeLast(5))
                {
                    detailStack.Children.Add(CreateMessageHistoryLine(message));
                }

                detailsExpander.Content = detailStack;
                stack.Children.Add(detailsExpander);
            }

            return root;
        }

        public static string BuildSessionSettlementTextForTest(AiSermonSessionHistory session)
        {
            return BuildSessionSettlementText(session);
        }

        private static string BuildSessionSettlementText(AiSermonSessionHistory session)
        {
            if (session == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (session.SessionCost.HasValue)
            {
                parts.Add("消耗 " + session.SessionCost.Value.ToString("0.00", CultureInfo.InvariantCulture));
            }

            if (session.EndedAt.HasValue)
            {
                parts.Add("结束 " + session.EndedAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
            }

            return string.Join(" · ", parts);
        }

        private FrameworkElement CreateMessageHistoryLine(AiConversationHistoryMessage message)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0), LastChildFill = true };
            var deleteButton = CreateInlineDeleteButton(
                "删",
                "确认删除这条历史消息吗？此操作不可恢复。",
                () => HistoryMessageDeleteRequested?.Invoke(message.Id));
            DockPanel.SetDock(deleteButton, Dock.Right);
            row.Children.Add(deleteButton);

            string label = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? "AI"
                : (string.IsNullOrWhiteSpace(message.Name) ? "用户" : message.Name);
            row.Children.Add(new TextBlock
            {
                Text = $"{message.CreatedAt:HH:mm} [{label}] {TrimForPanel(message.Content, 140)}",
                Foreground = CreateBrush(string.Equals(label, "AI", StringComparison.Ordinal) ? "#F4FBFF" : "#8DEAFF"),
                FontSize = 11,
                LineHeight = 16,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 8, 0)
            });
            return row;
        }

        private System.Windows.Controls.Button CreateInlineDeleteButton(string text, string confirmMessage, Action action)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = text,
                Padding = new Thickness(6, 1, 6, 1),
                MinWidth = 0,
                FontSize = 10,
                Foreground = CreateBrush("#9FC7DD"),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = CreateBrush("#2F5C78"),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(8, 0, 0, 0)
            };
            button.Click += (_, _) =>
            {
                if (ConfirmDeleteAction(confirmMessage))
                {
                    action?.Invoke();
                }
            };
            return button;
        }

        private static bool ConfirmDeleteAction(string message)
        {
            return System.Windows.MessageBox.Show(
                message,
                "删除确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            MessageStackPanel.Children.Clear();
            _currentAssistantText = null;
            EmptyStatePanel.Visibility = Visibility.Visible;
            StatusText.Text = DefaultCollapsedStatus;
            UpdateCollapsedStatus(DefaultCollapsedStatus);
            CollapsedScriptureText.Text = string.Empty;
            CollapsedScriptureText.Visibility = Visibility.Collapsed;
            DebugModeChanged?.Invoke(false);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed &&
                e.OriginalSource is DependencyObject source &&
                !IsInteractiveControl(source))
            {
                DragMove();
            }
        }

        private async void EndSessionButton_Click(object sender, RoutedEventArgs e)
        {
            await RequestEndSessionAsync();
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            await RequestEndSessionAsync();
            Close();
        }

        private async Task RequestEndSessionAsync()
        {
            var handler = EndSessionRequested;
            if (handler == null)
            {
                return;
            }

            try
            {
                StatusText.Text = "正在结束本场…";
                await handler.Invoke();
            }
            catch (Exception ex)
            {
                AppendStatus($"结束本场失败：{ex.Message}");
            }
        }

        private void ModelMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModelPopup == null)
            {
                return;
            }

            ModelPopup.IsOpen = true;
        }

        private void ModelOptionFlash_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ApplyModelSelection("deepseek-v4-flash");
        }

        private void ModelOptionPro_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ApplyModelSelection("deepseek-v4-pro");
        }

        private void ModelOptionGeminiFlash_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ApplyModelSelection("gemini-3.5-flash");
        }

        private void ApplyModelSelection(string model)
        {
            string next = string.IsNullOrWhiteSpace(model) ? "deepseek-v4-flash" : model.Trim();
            _configManager.DeepSeekModel = next;
            SetModelName(next);
            if (ModelPopup != null)
            {
                ModelPopup.IsOpen = false;
            }

            ModelChanged?.Invoke(next);
        }

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            SetCollapsedState(!_isCollapsed, rememberExpandedHeight: true);
        }

        private void RestoreCollapsedState(bool collapsed)
        {
            if (!collapsed)
            {
                return;
            }

            SetCollapsedState(collapsed: true, rememberExpandedHeight: false);
        }

        private void SetCollapsedState(bool collapsed, bool rememberExpandedHeight)
        {
            if (collapsed == _isCollapsed)
            {
                return;
            }

            if (collapsed)
            {
                if (rememberExpandedHeight)
                {
                    _expandedHeight = Math.Max(Height, 220);
                }

                MetaSectionGrid.Visibility = Visibility.Collapsed;
                MessageContainerBorder.Visibility = Visibility.Collapsed;
                ScriptureCorrectionGrid.Visibility = Visibility.Collapsed;
                FooterHintGrid.Visibility = Visibility.Collapsed;
                CollapsedInfoGrid.Visibility = Visibility.Visible;
                Height = CollapsedPanelHeight;
                CollapseButton.Content = "▸";
                CollapseButton.ToolTip = "展开";
                _isCollapsed = true;
                return;
            }

            CollapsedInfoGrid.Visibility = Visibility.Collapsed;
            MetaSectionGrid.Visibility = Visibility.Visible;
            MessageContainerBorder.Visibility = Visibility.Visible;
            _isCollapsed = false;
            UpdateScriptureCorrectionVisibility();
            FooterHintGrid.Visibility = Visibility.Visible;
            Height = Math.Max(_expandedHeight, 260);
            CollapseButton.Content = "▾";
            CollapseButton.ToolTip = "折叠";
        }

        private void ViewModeToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingViewModeUi ||
                RealtimeContentGrid == null ||
                HistoryContentGrid == null ||
                sender is not ToggleButton button)
            {
                return;
            }

            bool showHistory = string.Equals(button.Content?.ToString(), "历史", StringComparison.Ordinal);
            SetActiveView(showHistory, requestRefresh: true);
        }

        private void SetActiveView(bool showHistory, bool requestRefresh)
        {
            if (RealtimeViewButton == null ||
                HistoryViewButton == null ||
                RealtimeContentGrid == null ||
                HistoryContentGrid == null)
            {
                return;
            }

            _isUpdatingViewModeUi = true;
            try
            {
                RealtimeViewButton.IsChecked = !showHistory;
                HistoryViewButton.IsChecked = showHistory;
                RealtimeContentGrid.Visibility = showHistory ? Visibility.Collapsed : Visibility.Visible;
                HistoryContentGrid.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;
                UpdateScriptureCorrectionVisibility();
            }
            finally
            {
                _isUpdatingViewModeUi = false;
            }

            if (showHistory && requestRefresh)
            {
                HistoryRequested?.Invoke();
            }
        }

        private void SpeakerMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (SpeakerPopup == null || SpeakerPopupStack == null || SpeakerMenuButton == null)
            {
                return;
            }

            RebuildSpeakerMenu(_lastAppliedSpeaker);
            SpeakerPopup.IsOpen = true;
        }

        private void AddSpeakerFromMenu()
        {
            if (SpeakerPopup != null)
            {
                SpeakerPopup.IsOpen = false;
            }
            string name = PromptSpeakerName("添加传道人", "请输入传道人名称", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            AddSpeakerOptionAndApply(name.Trim());
        }

        private void RenameSpeakerFromMenu(string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker) || !CanDeleteSpeaker(speaker))
            {
                return;
            }

            string target = PromptSpeakerName("重命名传道人", "请输入新名称", speaker);
            string next = (target ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(next) || string.Equals(next, speaker, StringComparison.Ordinal))
            {
                return;
            }

            SpeakerRenameRequested?.Invoke(speaker, next);
        }

        private string PromptSpeakerName(string title, string hint, string initialValue)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 360,
                Height = 165,
                MinWidth = 320,
                MinHeight = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this,
                Background = CreateBrush("#101A28"),
                Foreground = CreateBrush("#EAF9FF"),
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var panel = new Grid { Margin = new Thickness(14) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            panel.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = CreateBrush("#D7EEFF")
            });

            var textBox = new System.Windows.Controls.TextBox
            {
                Text = initialValue ?? string.Empty,
                Height = 30,
                FontSize = 13,
                Padding = new Thickness(8, 3, 8, 3),
                Background = CreateBrush("#EAF3FA"),
                Foreground = CreateBrush("#0F172A"),
                BorderBrush = CreateBrush("#4D90C4")
            };
            Grid.SetRow(textBox, 1);
            panel.Children.Add(textBox);

            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(buttons, 2);
            panel.Children.Add(buttons);

            var okButton = new System.Windows.Controls.Button
            {
                Content = "确定",
                Width = 70,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Background = CreateBrush("#2F8CD7"),
                Foreground = CreateBrush("#FFFFFF"),
                BorderBrush = CreateBrush("#88CAFF"),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsDefault = true
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 70,
                Height = 28,
                Background = CreateBrush("#1E4261"),
                Foreground = CreateBrush("#EAF9FF"),
                BorderBrush = CreateBrush("#4D90C4"),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsCancel = true
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            string result = string.Empty;
            okButton.Click += (_, _) =>
            {
                result = (textBox.Text ?? string.Empty).Trim();
                dialog.DialogResult = true;
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;

            dialog.Content = panel;
            dialog.Loaded += (_, _) =>
            {
                textBox.Focus();
                textBox.CaretIndex = textBox.Text?.Length ?? 0;
            };

            return dialog.ShowDialog() == true ? result : string.Empty;
        }

        private void AddSpeakerOptionAndApply(string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker))
            {
                return;
            }

            if (!_speakerNames.Contains(speaker, StringComparer.Ordinal))
            {
                _speakerNames.Add(speaker);
            }

            ApplySpeakerSelection(speaker, raiseEvent: true);
        }

        private void ApplySpeakerSelection(string speaker, bool raiseEvent)
        {
            speaker = (speaker ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(speaker))
            {
                speaker = UnlabeledSpeakerName;
            }

            bool changed = !string.Equals(speaker, _lastAppliedSpeaker, StringComparison.Ordinal);
            _lastAppliedSpeaker = speaker;
            LoadDialectTagsForSpeaker(speaker);
            UpdateSpeakerSelectionVisual(speaker);
            RebuildSpeakerMenu(speaker);
            if (raiseEvent && changed)
            {
                SpeakerApplied?.Invoke(speaker);
                DialectSchemeChanged?.Invoke(_selectedDialectTags.Count > 0, _selectedDialectTags.ToList());
            }
        }

        private void LoadDialectTagsForSpeaker(string speaker)
        {
            _selectedDialectTags.Clear();
            if (!string.IsNullOrWhiteSpace(speaker) &&
                !string.Equals(speaker, UnlabeledSpeakerName, StringComparison.Ordinal) &&
                _speakerDialectBindings.TryGetValue(speaker, out var tags))
            {
                foreach (string tag in tags)
                {
                    _selectedDialectTags.Add(tag);
                }
            }

            if (_selectedDialectTags.Count == 0)
            {
                _selectedDialectTags.Add("国语");
            }
        }

        private void UpdateSpeakerSelectionVisual(string speaker)
        {
            if (SpeakerSelectionText == null)
            {
                return;
            }

            string value = DisplaySpeakerSelectionText(speaker);
            SpeakerSelectionText.Text = value;
            SpeakerSelectionText.ToolTip = value;
        }

        private static string DisplaySpeakerSelectionText(string speaker)
        {
            string value = DisplaySpeakerName(speaker);
            return string.IsNullOrWhiteSpace(value) ? "选择传道人" : value;
        }

        private static string DisplaySpeakerName(string speaker)
        {
            string value = (speaker ?? string.Empty).Trim();
            return string.Equals(value, UnlabeledSpeakerName, StringComparison.Ordinal) ? string.Empty : value;
        }

        private void RebuildSpeakerMenu(string selectedSpeaker)
        {
            if (SpeakerPopupStack == null)
            {
                return;
            }

            SpeakerPopupStack.Children.Clear();
            BuildDialectSchemeSection(selectedSpeaker);

            var listPanel = new StackPanel
            {
                Margin = new Thickness(8, 6, 8, 4)
            };
            var visibleSpeakers = _speakerNames
                .Where(speaker =>
                    !string.Equals(speaker, UnlabeledSpeakerName, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (string speaker in visibleSpeakers)
            {
                string value = speaker;
                bool isSelected = string.Equals(value, selectedSpeaker, StringComparison.Ordinal);
                var item = new Border
                {
                    Height = 30,
                    Padding = new Thickness(8, 0, 8, 0),
                    Margin = new Thickness(0, 0, 0, 2),
                    CornerRadius = new CornerRadius(4),
                    Background = isSelected
                        ? CreateBrush("#2F8CD7")
                        : System.Windows.Media.Brushes.Transparent,
                    BorderBrush = isSelected ? CreateBrush("#88CAFF") : System.Windows.Media.Brushes.Transparent,
                    BorderThickness = isSelected ? new Thickness(1) : new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = CreateSpeakerMenuOptionContent(value, isSelected, CanDeleteSpeaker(value))
                };
                item.MouseEnter += (_, _) =>
                {
                    if (!isSelected)
                    {
                        item.Background = CreateBrush("#143B58");
                    }
                };
                item.MouseLeave += (_, _) =>
                {
                    if (!isSelected)
                    {
                        item.Background = System.Windows.Media.Brushes.Transparent;
                    }
                };
                item.MouseLeftButtonUp += (_, e) =>
                {
                    if (e.OriginalSource is DependencyObject source && IsInteractiveControl(source))
                    {
                        return;
                    }

                    if (SpeakerPopup != null)
                    {
                        SpeakerPopup.IsOpen = false;
                    }
                    ApplySpeakerSelection(value, raiseEvent: true);
                };
                listPanel.Children.Add(item);
            }
            var listScroll = new ScrollViewer
            {
                MaxHeight = 150,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = true,
                Content = listPanel
            };
            SpeakerPopupStack.Children.Add(listScroll);

            var addButton = new Border
            {
                Height = 34,
                Padding = new Thickness(14, 0, 10, 0),
                Margin = new Thickness(8, 0, 8, 6),
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = CreateBrush("#7366B8EA"),
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "+  新增传道人",
                    Foreground = CreateBrush("#EAF9FF"),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            addButton.MouseLeftButtonUp += (_, _) => AddSpeakerFromMenu();
            SpeakerPopupStack.Children.Add(addButton);
        }

        private void BuildDialectSchemeSection(string selectedSpeaker)
        {
            var header = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = CreateBrush("#3866B8EA"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = CreateBrush("#071C30")
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.Child = row;

            row.Children.Add(new TextBlock
            {
                Text = "语言",
                Foreground = CreateBrush("#DFF5FF"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            SpeakerPopupStack.Children.Add(header);

            var dialectPanel = new StackPanel
            {
                Margin = new Thickness(10, 6, 10, 6)
            };
            dialectPanel.Children.Add(new TextBlock
            {
                Text = "语言标签",
                Foreground = CreateBrush("#8FB8D1"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var wrap = new WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            foreach (string tag in _dialectTags)
            {
                bool selected = _selectedDialectTags.Contains(tag);
                var chip = new Border
                {
                    Height = 24,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(8, 0, 8, 0),
                    CornerRadius = new CornerRadius(12),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    BorderBrush = selected ? CreateBrush("#88CAFF") : CreateBrush("#2F5C78"),
                    BorderThickness = new Thickness(1),
                    Background = selected ? CreateBrush("#2F8CD7") : CreateBrush("#102A40"),
                    Child = new TextBlock
                    {
                        Text = tag,
                        Foreground = CreateBrush("#EAF9FF"),
                        FontSize = 11,
                        FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    if (_selectedDialectTags.Contains(tag))
                    {
                        if (string.Equals(tag, "国语", StringComparison.Ordinal) && _selectedDialectTags.Count == 1)
                        {
                            return;
                        }
                        _selectedDialectTags.Remove(tag);
                    }
                    else
                    {
                        _selectedDialectTags.Add(tag);
                    }

                    PersistDialectSchemeToConfig();
                    DialectSchemeChanged?.Invoke(_selectedDialectTags.Count > 0, _selectedDialectTags.ToList());
                    RebuildSpeakerMenu(selectedSpeaker);
                };
                wrap.Children.Add(chip);
            }

            var addChip = new Border
            {
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(12),
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderBrush = CreateBrush("#3F7EA6"),
                BorderThickness = new Thickness(1),
                Background = CreateBrush("#0D2438"),
                Child = new TextBlock
                {
                    Text = "+ 添加语言",
                    Foreground = CreateBrush("#BFEFFF"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            addChip.MouseLeftButtonUp += (_, _) => AddDialectTagFromMenu(selectedSpeaker);
            wrap.Children.Add(addChip);

            dialectPanel.Children.Add(wrap);
            SpeakerPopupStack.Children.Add(dialectPanel);
        }

        private void AddDialectTagFromMenu(string selectedSpeaker)
        {
            string tag = PromptDialectTagName();
            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            tag = tag.Trim();
            if (!_dialectTags.Contains(tag, StringComparer.Ordinal))
            {
                _dialectTags.Add(tag);
            }

            _selectedDialectTags.Add(tag);
            PersistDialectSchemeToConfig();
            DialectSchemeChanged?.Invoke(_selectedDialectTags.Count > 0, _selectedDialectTags.ToList());
            RebuildSpeakerMenu(selectedSpeaker);
        }

        private string PromptDialectTagName()
        {
            var dialog = new Window
            {
                Title = "新增语言标签",
                Width = 340,
                Height = 160,
                MinWidth = 320,
                MinHeight = 145,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this,
                Background = CreateBrush("#101A28"),
                Foreground = CreateBrush("#EAF9FF"),
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var panel = new Grid { Margin = new Thickness(14) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            panel.Children.Add(new TextBlock
            {
                Text = "输入语言标签（如：宁波话）",
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = CreateBrush("#D7EEFF")
            });

            var textBox = new System.Windows.Controls.TextBox
            {
                Height = 30,
                FontSize = 12,
                Padding = new Thickness(8, 3, 8, 3),
                Background = CreateBrush("#EAF3FA"),
                Foreground = CreateBrush("#0F172A"),
                BorderBrush = CreateBrush("#4D90C4")
            };
            Grid.SetRow(textBox, 1);
            panel.Children.Add(textBox);

            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            Grid.SetRow(buttons, 2);
            panel.Children.Add(buttons);

            var okButton = new System.Windows.Controls.Button
            {
                Content = "确定",
                Width = 68,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Background = CreateBrush("#2F8CD7"),
                Foreground = CreateBrush("#FFFFFF"),
                BorderBrush = CreateBrush("#88CAFF"),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsDefault = true
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 68,
                Height = 28,
                Background = CreateBrush("#1E4261"),
                Foreground = CreateBrush("#EAF9FF"),
                BorderBrush = CreateBrush("#4D90C4"),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsCancel = true
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            string result = string.Empty;
            okButton.Click += (_, _) =>
            {
                result = (textBox.Text ?? string.Empty).Trim();
                dialog.DialogResult = true;
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;

            dialog.Content = panel;
            dialog.Loaded += (_, _) => textBox.Focus();
            return dialog.ShowDialog() == true ? result : string.Empty;
        }

        private void PersistDialectSchemeToConfig()
        {
            if (!string.IsNullOrWhiteSpace(_lastAppliedSpeaker) &&
                !string.Equals(_lastAppliedSpeaker, UnlabeledSpeakerName, StringComparison.Ordinal))
            {
                _speakerDialectBindings[_lastAppliedSpeaker] = new HashSet<string>(_selectedDialectTags, StringComparer.Ordinal);
            }

            _dialectSchemeEnabled = _selectedDialectTags.Count > 0;
            _configManager.AiSermonDialectSchemeEnabled = _dialectSchemeEnabled;
            _configManager.AiSermonDialectTags = _dialectTags.ToArray();
            _configManager.AiSermonSelectedDialectTags = _selectedDialectTags.ToArray();
            _configManager.AiSermonSpeakerDialectBindings = _speakerDialectBindings
                .Select(kvp => new AiSpeakerDialectBindingEntry
                {
                    Speaker = kvp.Key,
                    Tags = kvp.Value.ToArray()
                })
                .ToArray();
        }

        private bool CanDeleteSpeaker(string speaker)
        {
            return !string.IsNullOrWhiteSpace(speaker) &&
                   !string.Equals(speaker, UnlabeledSpeakerName, StringComparison.Ordinal);
        }

        private Grid CreateSpeakerMenuOptionContent(string speaker, bool selected, bool canDelete)
        {
            string displaySpeaker = DisplaySpeakerName(speaker);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var accent = new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Background = selected ? CreateBrush("#54C4FF") : System.Windows.Media.Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            grid.Children.Add(accent);

            var speakerText = new TextBlock
            {
                Text = displaySpeaker,
                Foreground = CreateBrush("#DDF4FF"),
                FontSize = 13,
                FontWeight = selected ? FontWeights.Bold : FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(speakerText, 1);
            grid.Children.Add(speakerText);

            if (canDelete)
            {
                var renameButton = new System.Windows.Controls.Button
                {
                    Content = "✎",
                    Width = 22,
                    Height = 22,
                    Padding = new Thickness(0),
                    Margin = new Thickness(8, 0, 0, 0),
                    Foreground = CreateBrush("#9FC7DD"),
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderBrush = CreateBrush("#2F5C78"),
                    BorderThickness = new Thickness(1),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "重命名传道人"
                };
                renameButton.Click += (_, e) =>
                {
                    e.Handled = true;
                    RenameSpeakerFromMenu(speaker);
                };
                Grid.SetColumn(renameButton, 2);
                grid.Children.Add(renameButton);

                var deleteButton = new System.Windows.Controls.Button
                {
                    Content = "×",
                    Width = 22,
                    Height = 22,
                    Padding = new Thickness(0),
                    Margin = new Thickness(8, 0, 0, 0),
                    Foreground = CreateBrush("#9FC7DD"),
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderBrush = CreateBrush("#2F5C78"),
                    BorderThickness = new Thickness(1),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "删除传道人"
                };
                deleteButton.Click += (_, e) =>
                {
                    e.Handled = true;
                    if (!ConfirmDeleteAction("确认删除该传道人吗？历史会自动归档到默认传道人。"))
                    {
                        return;
                    }

                    if (SpeakerPopup != null)
                    {
                        SpeakerPopup.IsOpen = false;
                    }

                    SpeakerDeleteRequested?.Invoke(speaker);
                };
                Grid.SetColumn(deleteButton, 3);
                grid.Children.Add(deleteButton);
            }

            return grid;
        }

        private void HideEmptyState()
        {
            if (EmptyStatePanel != null)
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ScrollToEnd()
        {
            MessageScrollViewer.ScrollToEnd();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            if (e.OriginalSource is DependencyObject source && IsInteractiveControl(source))
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshMessageTextWidths();
        }

        private void MessageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshMessageTextWidths();
        }

        private void ScriptureCorrectionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ScriptureCorrectionPlaceholder == null || ScriptureCorrectionTextBox == null)
            {
                return;
            }

            ScriptureCorrectionPlaceholder.Visibility = string.IsNullOrWhiteSpace(ScriptureCorrectionTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ScriptureCorrectionTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                return;
            }

            e.Handled = true;
            SubmitScriptureCorrectionInput();
        }

        private void ScriptureCorrectionSendButton_Click(object sender, RoutedEventArgs e)
        {
            SubmitScriptureCorrectionInput();
        }

        private void SubmitScriptureCorrectionInput()
        {
            if (ScriptureCorrectionTextBox == null)
            {
                return;
            }

            string text = (ScriptureCorrectionTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                AppendStatus("请输入需要修正的经文线索");
                ScriptureCorrectionTextBox.Focus();
                return;
            }

            ScriptureCorrectionTextBox.Clear();
            ScriptureCorrectionSubmitted?.Invoke(text);
        }

        private void UpdateScriptureCorrectionVisibility()
        {
            if (ScriptureCorrectionGrid == null || HistoryContentGrid == null)
            {
                return;
            }

            bool showHistory = HistoryContentGrid.Visibility == Visibility.Visible;
            ScriptureCorrectionGrid.Visibility = (!_isCollapsed && !showHistory)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void PanelOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_uiReady)
            {
                return;
            }

            ApplyPanelOpacityFromSlider();
            _configManager.AiSermonPanelOpacity = (int)Math.Round(PanelOpacitySlider.Value);
        }

        private void ThresholdToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_configManager == null ||
                !_uiReady ||
                _isUpdatingThresholdUi ||
                sender is not ToggleButton button)
            {
                return;
            }

            string level = (button.Content?.ToString() ?? string.Empty).Trim();
            double value = level switch
            {
                "低" => 0.55,
                "高" => 0.85,
                _ => 0.70
            };
            SetThresholdButtons(level);
            _configManager.AiSermonMinWriteConfidence = value;
            UpdateThresholdToolTip(level, value);
            AppendStatus($"经文候选阈值已设为{level}（{value:0.00}）");
        }

        private void OutputModeToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingOutputModeUi || !_uiReady || sender is not ToggleButton button)
            {
                return;
            }

            string mode = string.Equals(button.Content?.ToString(), "详细", StringComparison.Ordinal)
                ? "detailed"
                : "concise";
            SetOutputMode(mode);
            OutputModeChanged?.Invoke(mode);
        }

        private static string BuildTimePrefix()
        {
            return DateTime.Now.ToString("HH:mm:ss");
        }

        internal static string BuildMessageHeaderForTest(string label)
        {
            return BuildMessageHeader(label);
        }

        private static string BuildMessageHeader(string label)
        {
            string safeLabel = string.IsNullOrWhiteSpace(label) ? "信息" : label.Trim();
            return $"{BuildTimePrefix()} [{safeLabel}]";
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            return new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(hex));
        }

        private static string TrimForPanel(string text, int maxLength)
        {
            string value = (text ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "...";
        }

        private static string ExtractScriptureCorrectionDisplay(string content)
        {
            const string marker = "人工修正：";
            string value = (content ?? string.Empty).Trim();
            int index = value.LastIndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                value = value.Substring(index + marker.Length).Trim();
            }

            return string.IsNullOrWhiteSpace(value) ? "已提交人工经文修正" : value;
        }

        private void RefreshMessageTextWidths()
        {
            Dispatcher.Invoke(() =>
            {
                if (MessageStackPanel == null)
                {
                    return;
                }

                foreach (var child in MessageStackPanel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        ApplyMessageTextWidth(textBlock);
                    }
                }
            });
        }

        private void ApplyMessageTextWidth(TextBlock textBlock)
        {
            if (textBlock == null || MessageScrollViewer == null)
            {
                return;
            }

            double width = MessageScrollViewer.ViewportWidth;
            if (double.IsNaN(width) || width <= 0)
            {
                width = MessageScrollViewer.ActualWidth;
            }

            textBlock.MaxWidth = Math.Max(120, width - 10);
        }

        private static bool IsInteractiveControl(DependencyObject source)
        {
            for (DependencyObject current = source; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is System.Windows.Controls.Primitives.ButtonBase ||
                    current is System.Windows.Controls.CheckBox ||
                    current is System.Windows.Controls.TextBox ||
                    current is PasswordBox ||
                    current is System.Windows.Controls.ComboBox ||
                    current is System.Windows.Controls.Primitives.ScrollBar ||
                    current is Slider ||
                    current is Thumb)
                {
                    return true;
                }
            }

            return false;
        }

        private void SyncWriteThresholdUiFromConfig()
        {
            if (ThresholdLowButton == null || ThresholdMidButton == null || ThresholdHighButton == null)
            {
                return;
            }

            double value = _configManager.AiSermonMinWriteConfidence;
            int index = value switch
            {
                <= 0.60 => 0,
                >= 0.80 => 2,
                _ => 1
            };

            _isUpdatingThresholdUi = true;
            try
            {
                string level = index == 0 ? "低" : (index == 2 ? "高" : "中");
                double levelValue = index == 0 ? 0.55 : (index == 2 ? 0.85 : 0.70);
                SetThresholdButtons(level);
                UpdateThresholdToolTip(level, levelValue);
            }
            finally
            {
                _isUpdatingThresholdUi = false;
            }
        }

        private void UpdateThresholdToolTip(string level, double value)
        {
            if (ThresholdLowButton == null || ThresholdMidButton == null || ThresholdHighButton == null)
            {
                return;
            }

            string explain = level switch
            {
                "低" => "低：更宽松，允许合理推测候选更容易进入历史。",
                "高" => "高：更严格，只保留高把握候选，误写更少。",
                _ => "中：平衡模式，兼顾召回率与准确性。"
            };
            string tip = $"阈值 {level}（{value:0.00}）\n{explain}";
            ThresholdLowButton.ToolTip = tip;
            ThresholdMidButton.ToolTip = tip;
            ThresholdHighButton.ToolTip = tip;
        }

        private void SetThresholdButtons(string level)
        {
            if (ThresholdLowButton == null || ThresholdMidButton == null || ThresholdHighButton == null)
            {
                return;
            }

            ThresholdLowButton.IsChecked = string.Equals(level, "低", StringComparison.Ordinal);
            ThresholdMidButton.IsChecked = string.Equals(level, "中", StringComparison.Ordinal);
            ThresholdHighButton.IsChecked = string.Equals(level, "高", StringComparison.Ordinal);
        }

        private void ApplyPanelOpacityFromSlider()
        {
            if (PanelOpacitySlider == null || PanelChromeBorder == null || MessageContainerBorder == null)
            {
                return;
            }

            double ratio = Math.Clamp(PanelOpacitySlider.Value / 100.0, 0.35, 1.0);
            byte outerAlpha = (byte)Math.Round(192 * ratio);

            PanelChromeBorder.Background = new SolidColorBrush(WpfColor.FromArgb(outerAlpha, 0x10, 0x1A, 0x28));
        }

        private void SyncPanelOpacityUiFromConfig()
        {
            if (PanelOpacitySlider == null || _configManager == null)
            {
                return;
            }

            double value = Math.Clamp(_configManager.AiSermonPanelOpacity, 35, 100);
            if (Math.Abs(PanelOpacitySlider.Value - value) > 0.01)
            {
                PanelOpacitySlider.Value = value;
            }
        }
    }
}


