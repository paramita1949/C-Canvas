using System;
using System.Collections.Generic;
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
        private const string UnlabeledSpeakerName = "未标记讲师";
        private readonly ConfigManager _configManager;
        private TextBlock _currentAssistantText;
        private bool _isUpdatingThresholdUi;
        private bool _isUpdatingOutputModeUi;
        private bool _isUpdatingViewModeUi;
        private bool _uiReady;
        private bool _isCollapsed;
        private string _lastAppliedSpeaker = string.Empty;
        private string _speakerFilterText = string.Empty;
        private System.Windows.Controls.TextBox _speakerSearchBox;
        private readonly List<string> _speakerNames = new();
        private double _expandedHeight = 500;

        public event Action<bool> DebugModeChanged;
        public event Action<string> SpeakerApplied;
        public event Action<string> SpeakerDeleteRequested;
        public event Action<string> OutputModeChanged;
        public event Action HistoryRequested;
        public event Action<int> HistorySessionDeleteRequested;
        public event Action<int> HistoryMessageDeleteRequested;

        public AiAssistantPanelWindow(ConfigManager configManager)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            InitializeComponent();
            SetModelName(_configManager.DeepSeekModel);
            SyncWriteThresholdUiFromConfig();
            SyncPanelOpacityUiFromConfig();
            ApplyPanelOpacityFromSlider();
            SetActiveView(showHistory: false, requestRefresh: false);
            _uiReady = true;
        }

        public void SetProjectTitle(string title)
        {
            Dispatcher.Invoke(() =>
            {
                ProjectTitleText.Text = string.IsNullOrWhiteSpace(title) ? "未绑定幻灯片项目" : title.Trim();
            });
        }

        public void SetReceiveAsr(bool enabled)
        {
            _ = enabled;
        }

        public void SetModelName(string modelName)
        {
            Dispatcher.Invoke(() =>
            {
                ModelNameText.Text = string.IsNullOrWhiteSpace(modelName)
                    ? "DeepSeek"
                    : modelName.Trim();
            });
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
                _ => "你"
            };
            string display = name switch
            {
                "project_context" => "读取幻灯片项目，建立本场主题、经文范围和后续ASR理解上下文。",
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
                    Text = $"{BuildTimePrefix()} [AI理解] ",
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

                if (string.Equals(text, "DeepSeek请求已发送，处理中…", StringComparison.Ordinal) ||
                    string.Equals(text, "DeepSeek已返回结果。", StringComparison.Ordinal))
                {
                    return;
                }

                AddMessage("系统", text, "#BFEFFF");
            });
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
                    Text = $"{BuildTimePrefix()} [{label}] {(content ?? string.Empty)}",
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
            var root = new Border
            {
                Background = CreateBrush("#24192E3F"),
                BorderBrush = CreateBrush("#365A78"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var stack = new StackPanel();
            root.Child = stack;

            stack.Children.Add(new TextBlock
            {
                Text = DisplaySpeakerName(group.SpeakerName),
                Foreground = CreateBrush("#F4FBFF"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(group.StyleSummary))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "风格摘要：" + TrimForPanel(group.StyleSummary, 160),
                    Foreground = CreateBrush("#A9C8DD"),
                    FontSize = 11,
                    LineHeight = 17,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 5, 0, 8)
                });
            }

            foreach (var session in group.Sessions.Take(8))
            {
                stack.Children.Add(CreateSessionHistoryBlock(session));
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
                Padding = new Thickness(9),
                Margin = new Thickness(0, 8, 0, 0)
            };
            var stack = new StackPanel();
            root.Child = stack;

            var header = new DockPanel { LastChildFill = true };
            var deleteButton = CreateInlineDeleteButton("删除本场", () => HistorySessionDeleteRequested?.Invoke(session.Id));
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
            stack.Children.Add(header);

            if (!string.IsNullOrWhiteSpace(session.Summary))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "本场摘要：" + TrimForPanel(session.Summary, 180),
                    Foreground = CreateBrush("#A9C8DD"),
                    FontSize = 11,
                    LineHeight = 17,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 4)
                });
            }

            foreach (var message in session.Messages.TakeLast(5))
            {
                stack.Children.Add(CreateMessageHistoryLine(message));
            }

            return root;
        }

        private FrameworkElement CreateMessageHistoryLine(AiConversationHistoryMessage message)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0), LastChildFill = true };
            var deleteButton = CreateInlineDeleteButton("删", () => HistoryMessageDeleteRequested?.Invoke(message.Id));
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

        private System.Windows.Controls.Button CreateInlineDeleteButton(string text, Action action)
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
            button.Click += (_, _) => action?.Invoke();
            return button;
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            MessageStackPanel.Children.Clear();
            _currentAssistantText = null;
            EmptyStatePanel.Visibility = Visibility.Visible;
            StatusText.Text = "等待AI解读或ASR字幕";
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isCollapsed)
            {
                _expandedHeight = Math.Max(Height, 220);
                MetaSectionGrid.Visibility = Visibility.Collapsed;
                MessageContainerBorder.Visibility = Visibility.Collapsed;
                FooterHintGrid.Visibility = Visibility.Collapsed;
                Height = 64;
                CollapseButton.Content = "▸";
                CollapseButton.ToolTip = "展开";
                _isCollapsed = true;
                return;
            }

            MetaSectionGrid.Visibility = Visibility.Visible;
            MessageContainerBorder.Visibility = Visibility.Visible;
            FooterHintGrid.Visibility = Visibility.Visible;
            Height = Math.Max(_expandedHeight, 260);
            CollapseButton.Content = "▾";
            CollapseButton.ToolTip = "折叠";
            _isCollapsed = false;
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

            _speakerFilterText = string.Empty;
            RebuildSpeakerMenu(_lastAppliedSpeaker);
            SpeakerPopup.IsOpen = true;
            FocusSpeakerSearchBox();
        }

        private void AddSpeakerFromMenu()
        {
            if (SpeakerPopup != null)
            {
                SpeakerPopup.IsOpen = false;
            }
            string name = PromptSpeakerName();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            AddSpeakerOptionAndApply(name.Trim());
        }

        private string PromptSpeakerName()
        {
            var dialog = new Window
            {
                Title = "添加讲师",
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
                Text = "请输入讲师名称",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = CreateBrush("#D7EEFF")
            });

            var textBox = new System.Windows.Controls.TextBox
            {
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
            dialog.Loaded += (_, _) => textBox.Focus();

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
            UpdateSpeakerSelectionVisual(speaker);
            RebuildSpeakerMenu(speaker);
            if (raiseEvent && changed)
            {
                SpeakerApplied?.Invoke(speaker);
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
            return string.IsNullOrWhiteSpace(value) ? "选择讲师" : value;
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

            _speakerSearchBox = null;
            SpeakerPopupStack.Children.Clear();

            var searchRow = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = CreateBrush("#3866B8EA"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = CreateBrush("#071C30")
            };
            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.Child = searchGrid;

            var searchIcon = new TextBlock
            {
                Text = "⌕",
                Foreground = CreateBrush("#7FA8C0"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            };
            searchGrid.Children.Add(searchIcon);

            var inputHost = new Grid();
            Grid.SetColumn(inputHost, 1);
            searchGrid.Children.Add(inputHost);

            var searchBox = new System.Windows.Controls.TextBox
            {
                Name = "SpeakerSearchBox",
                Text = _speakerFilterText,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = CreateBrush("#DFF5FF"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(0),
                MinWidth = 130,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "搜索讲师"
            };
            _speakerSearchBox = searchBox;
            inputHost.Children.Add(searchBox);

            var placeholder = new TextBlock
            {
                Text = "搜索讲师",
                Foreground = CreateBrush("#7294AA"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = string.IsNullOrWhiteSpace(_speakerFilterText) ? Visibility.Visible : Visibility.Collapsed
            };
            inputHost.Children.Add(placeholder);
            searchBox.TextChanged += (_, _) =>
            {
                _speakerFilterText = searchBox.Text ?? string.Empty;
                RebuildSpeakerMenu(selectedSpeaker);
                FocusSpeakerSearchBox();
            };
            SpeakerPopupStack.Children.Add(searchRow);

            var listPanel = new StackPanel
            {
                Margin = new Thickness(8, 6, 8, 4)
            };
            string filter = (_speakerFilterText ?? string.Empty).Trim();
            var visibleSpeakers = _speakerNames
                .Where(speaker =>
                    !string.Equals(speaker, UnlabeledSpeakerName, StringComparison.Ordinal) &&
                    (string.IsNullOrWhiteSpace(filter) || speaker.Contains(filter, StringComparison.OrdinalIgnoreCase)))
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
                    Text = "+  新增讲师",
                    Foreground = CreateBrush("#EAF9FF"),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            addButton.MouseLeftButtonUp += (_, _) => AddSpeakerFromMenu();
            SpeakerPopupStack.Children.Add(addButton);
        }

        private void FocusSpeakerSearchBox()
        {
            if (SpeakerPopup == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_speakerSearchBox == null)
                {
                    return;
                }

                _speakerSearchBox.Focus();
                _speakerSearchBox.CaretIndex = _speakerSearchBox.Text?.Length ?? 0;
            }));
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
                    ToolTip = "删除讲师标签"
                };
                deleteButton.Click += (_, e) =>
                {
                    e.Handled = true;
                    if (SpeakerPopup != null)
                    {
                        SpeakerPopup.IsOpen = false;
                    }

                    SpeakerDeleteRequested?.Invoke(speaker);
                };
                Grid.SetColumn(deleteButton, 2);
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
