using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Core;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace ImageColorChanger.UI
{
    public partial class AiAssistantPanelWindow : Window
    {
        private readonly ConfigManager _configManager;
        private TextBlock _currentAssistantText;
        private bool _isUpdatingThresholdUi;
        private bool _uiReady;
        private bool _isCollapsed;
        private double _expandedHeight = 430;

        public event Action<bool> DebugModeChanged;

        public AiAssistantPanelWindow(ConfigManager configManager)
        {
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            InitializeComponent();
            SetModelName(_configManager.DeepSeekModel);
            SyncWriteThresholdUiFromConfig();
            SyncPanelOpacityUiFromConfig();
            ApplyPanelOpacityFromSlider();
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

        private void AddMessage(string sender, string content, string foreground)
        {
            Dispatcher.Invoke(() =>
            {
                HideEmptyState();
                var textBlock = new TextBlock
                {
                    Text = $"{BuildTimePrefix()} [{sender}] {(content ?? string.Empty)}",
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
                e.OriginalSource is not System.Windows.Controls.Button &&
                e.OriginalSource is not System.Windows.Controls.CheckBox)
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

        private static string BuildTimePrefix()
        {
            return DateTime.Now.ToString("HH:mm:ss");
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            return new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(hex));
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
                if (current is System.Windows.Controls.Button ||
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
