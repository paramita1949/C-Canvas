using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Services.Projection.Output;
using Forms = System.Windows.Forms;
using WpfBrushes = System.Windows.Media.Brushes;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow NDI 菜单开关与选项
    /// </summary>
    public partial class MainWindow
    {
        private const string NdiOfficialDownloadUrl = "https://ndi.link/NDIRedistV6";
        private static readonly TimeSpan NdiDiscoveryDuration = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan NdiDiscoveryInterval = TimeSpan.FromSeconds(5);
        private System.Windows.Threading.DispatcherTimer _ndiDiscoveryTimer;
        private DateTime _ndiDiscoveryStartedAtUtc = DateTime.MinValue;

        internal MenuItem BuildNdiSubMenu()
        {
            var ndiMenu = new MenuItem { Header = "NDI网络流" };
            var enabledItem = new MenuItem { Header = "NDI开启", IsCheckable = true };
            var lyricsTransparentItem = new MenuItem { Header = "歌词透明", IsCheckable = true };
            var frameWatermarkItem = new MenuItem { Header = "帧水印" };
            var runtimeStatusItem = new MenuItem { Header = "NDI未连接", IsEnabled = false, Foreground = WpfBrushes.Gray };

            void RefreshUi()
            {
                if (_configManager == null)
                {
                    return;
                }

                enabledItem.IsChecked = _configManager.ProjectionNdiEnabled;
                lyricsTransparentItem.IsChecked = _configManager.ProjectionNdiLyricsTransparentEnabled;
                int connectionCount = _projectionNdiOutputManager?.GetClientConnectionCount() ?? 0;
                bool connected = _configManager.ProjectionNdiEnabled && connectionCount > 0;
                runtimeStatusItem.Header = connected ? $"NDI已连接（{connectionCount}台）" : "NDI未连接";
                runtimeStatusItem.Foreground = connected ? WpfBrushes.LimeGreen : WpfBrushes.Gray;
            }

            void TryPushIdleFramePreview()
            {
                if ((_configManager?.ProjectionNdiEnabled ?? false) && _projectionManager?.IsProjectionActive != true)
                {
                    _projectionNdiOutputManager?.PushTransparentIdleFrame();
                }
            }

            enabledItem.Click += (_, _) =>
            {
                if (_configManager == null)
                {
                    return;
                }

                _configManager.ProjectionNdiEnabled = enabledItem.IsChecked;
                if (!_configManager.ProjectionNdiEnabled)
                {
                    _projectionNdiOutputManager?.Stop();
                    StopVideoNdiTimer();
                    StopNdiDiscoveryTimer();
                }
                else if (_videoPlayerManager?.IsPlaying == true)
                {
                    StartVideoNdiTimer();
                }

                if (_configManager.ProjectionNdiEnabled)
                {
                    bool runtimeReady = ProjectionNdiRuntimeProbe.IsRuntimeAvailable();
                    if (!runtimeReady)
                    {
                        bool started = TryLaunchNdiRuntimeInstaller();
                        if (started)
                        {
                            ShowStatus("NDI未连接，已打开安装程序");
                        }
                        else
                        {
                            bool openedOfficial = PromptAndOpenNdiOfficialDownloadPage();
                            ShowStatus(openedOfficial
                                ? "NDI未连接，已打开官方下载页"
                                : "NDI未连接，请先安装 NDI Runtime");
                        }
                    }
                    else
                    {
                        _projectionNdiOutputManager?.PushTransparentIdleFrame();
                        StartNdiDiscoveryTimer(resetWindow: true);
                        ShowStatus("NDI已开启，等待客户端连接");
                    }
                }
                else
                {
                    ShowStatus("NDI输出已关闭");
                }
            };

            lyricsTransparentItem.Click += (_, _) =>
            {
                if (_configManager == null)
                {
                    return;
                }

                _configManager.ProjectionNdiLyricsTransparentEnabled = lyricsTransparentItem.IsChecked;
                ShowStatus(_configManager.ProjectionNdiLyricsTransparentEnabled ? "歌词透明已开启" : "歌词透明已关闭");
            };

            frameWatermarkItem.Click += (_, _) =>
            {
                ShowNdiFrameWatermarkSettingsDialog(TryPushIdleFramePreview, RefreshUi);
            };

            ndiMenu.SubmenuOpened += (_, _) => RefreshUi();
            RefreshUi();

            ndiMenu.Items.Add(enabledItem);
            ndiMenu.Items.Add(lyricsTransparentItem);
            ndiMenu.Items.Add(frameWatermarkItem);
            ndiMenu.Items.Add(CreateNdiMenuSeparator());
            ndiMenu.Items.Add(runtimeStatusItem);
            return ndiMenu;
        }

        private static Separator CreateNdiMenuSeparator()
        {
            var separator = new Separator
            {
                Margin = new Thickness(10, 6, 10, 6)
            };

            var lineBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 216, 226));
            var template = new ControlTemplate(typeof(Separator));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.HeightProperty, 1.0);
            border.SetValue(Border.BackgroundProperty, lineBrush);
            border.SetValue(Border.SnapsToDevicePixelsProperty, true);
            template.VisualTree = border;
            separator.Template = template;
            return separator;
        }

        private void ShowNdiFrameWatermarkSettingsDialog(Action onChanged, Action refreshUi)
        {
            if (_configManager == null)
            {
                return;
            }

            var dialog = new Window
            {
                Title = "帧水印设置",
                Width = 760,
                Height = 520,
                MinWidth = 760,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(246, 248, 251))
            };

            var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(header, 0);

            var titleStack = new StackPanel();
            var title = new TextBlock
            {
                Text = "帧水印",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39))
            };
            var subtitle = new TextBlock
            {
                Text = "统一设置文字、位置、字号与字体",
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 13,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139))
            };
            titleStack.Children.Add(title);
            titleStack.Children.Add(subtitle);
            Grid.SetColumn(titleStack, 0);
            header.Children.Add(titleStack);
            root.Children.Add(header);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 2);

            var formCard = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(228, 233, 242)),
                Background = WpfBrushes.White,
                Padding = new Thickness(18, 16, 18, 14)
            };
            Grid.SetColumn(formCard, 0);

            var form = new Grid();
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var textBox = new System.Windows.Controls.TextBox
            {
                Height = 38,
                FontSize = 14,
                Padding = new Thickness(12, 6, 12, 6),
                Text = _configManager.ProjectionNdiIdleFrameWatermarkText ?? string.Empty
            };
            textBox.SetCurrentValue(System.Windows.Controls.TextBox.ToolTipProperty, "留空表示关闭水印");
            var textField = CreateFormField("文字", textBox);
            Grid.SetRow(textField, 0);
            form.Children.Add(textField);

            string selectedPosition = _configManager.ProjectionNdiIdleFrameWatermarkPosition;
            var positionPanel = new System.Windows.Controls.Primitives.UniformGrid
            {
                Rows = 1,
                Columns = 5
            };
            var posLeftTop = CreatePositionButton("左上");
            var posRightTop = CreatePositionButton("右上");
            var posLeftBottom = CreatePositionButton("左下");
            var posRightBottom = CreatePositionButton("右下");
            var posCenter = CreatePositionButton("居中");
            positionPanel.Children.Add(posLeftTop);
            positionPanel.Children.Add(posRightTop);
            positionPanel.Children.Add(posLeftBottom);
            positionPanel.Children.Add(posRightBottom);
            positionPanel.Children.Add(posCenter);
            var posField = CreateFormField("位置", positionPanel);
            Grid.SetRow(posField, 2);
            form.Children.Add(posField);

            var sizeGrid = new Grid();
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            var sizeSlider = new Slider
            {
                Minimum = 10,
                Maximum = 220,
                Value = _configManager.ProjectionNdiIdleFrameWatermarkFontSize,
                TickFrequency = 2,
                IsSnapToTickEnabled = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(sizeSlider, 0);
            sizeGrid.Children.Add(sizeSlider);
            var sizeBox = new System.Windows.Controls.TextBox
            {
                Height = 34,
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Text = _configManager.ProjectionNdiIdleFrameWatermarkFontSize.ToString("0.#")
            };
            Grid.SetColumn(sizeBox, 2);
            sizeGrid.Children.Add(sizeBox);
            var sizeField = CreateFormField("字号", sizeGrid);
            Grid.SetRow(sizeField, 4);
            form.Children.Add(sizeField);

            var fontGrid = new Grid();
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fontTextBox = new System.Windows.Controls.TextBox
            {
                Height = 38,
                FontSize = 14,
                Padding = new Thickness(12, 6, 12, 6),
                IsReadOnly = true,
                Text = _configManager.ProjectionNdiIdleFrameWatermarkFontFamily
            };
            Grid.SetColumn(fontTextBox, 0);
            fontGrid.Children.Add(fontTextBox);

            var chooseFontButton = new System.Windows.Controls.Button
            {
                Content = "选择字体",
                Height = 38,
                MinWidth = 96,
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 13,
                BorderThickness = new Thickness(0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235)),
                Foreground = WpfBrushes.White
            };
            Grid.SetColumn(chooseFontButton, 2);
            fontGrid.Children.Add(chooseFontButton);

            var fontField = CreateFormField("字体", fontGrid);
            Grid.SetRow(fontField, 6);
            form.Children.Add(fontField);

            formCard.Child = form;
            body.Children.Add(formCard);

            var previewCard = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(228, 233, 242)),
                Background = WpfBrushes.White,
                Padding = new Thickness(14)
            };
            Grid.SetColumn(previewCard, 2);
            var previewWrap = new Grid();
            previewWrap.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previewWrap.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            previewWrap.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var previewTitle = new TextBlock
            {
                Text = "预览",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105))
            };
            Grid.SetRow(previewTitle, 0);
            previewWrap.Children.Add(previewTitle);

            var previewArea = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 24, 38)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 42, 60)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14)
            };
            Grid.SetRow(previewArea, 2);
            var previewViewbox = new Viewbox
            {
                Stretch = System.Windows.Media.Stretch.Uniform,
                StretchDirection = StretchDirection.Both
            };
            var previewCanvas = new Canvas
            {
                Width = 1920,
                Height = 1080,
                ClipToBounds = true
            };
            previewViewbox.Child = previewCanvas;
            var previewText = new TextBlock
            {
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 255, 255, 255)),
                Text = textBox.Text,
                FontFamily = new System.Windows.Media.FontFamily(fontTextBox.Text),
                FontSize = _configManager.ProjectionNdiIdleFrameWatermarkFontSize,
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap
            };
            previewCanvas.Children.Add(previewText);
            previewArea.Child = previewViewbox;
            previewWrap.Children.Add(previewArea);
            previewCard.Child = previewWrap;
            body.Children.Add(previewCard);

            root.Children.Add(body);

            var actions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            Grid.SetRow(actions, 4);

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 96,
                Height = 40,
                Margin = new Thickness(0, 0, 10, 0),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                Background = WpfBrushes.White
            };
            var saveButton = new System.Windows.Controls.Button
            {
                Content = "保存",
                Width = 96,
                Height = 40,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235)),
                Foreground = WpfBrushes.White
            };
            actions.Children.Add(cancelButton);
            actions.Children.Add(saveButton);
            root.Children.Add(actions);

            void UpdatePositionButtons()
            {
                ApplyPositionButtonStyle(posLeftTop, selectedPosition == "LeftTop");
                ApplyPositionButtonStyle(posRightTop, selectedPosition == "RightTop");
                ApplyPositionButtonStyle(posLeftBottom, selectedPosition == "LeftBottom");
                ApplyPositionButtonStyle(posRightBottom, selectedPosition == "RightBottom");
                ApplyPositionButtonStyle(posCenter, selectedPosition == "Center");
            }

            void UpdatePreview()
            {
                const double previewWidth = 1920;
                const double previewHeight = 1080;
                const double margin = 48;

                string currentText = textBox.Text ?? string.Empty;
                previewText.Text = currentText;
                try
                {
                    previewText.FontFamily = new System.Windows.Media.FontFamily(fontTextBox.Text);
                }
                catch
                {
                    previewText.FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
                }

                if (string.IsNullOrWhiteSpace(currentText))
                {
                    previewText.Visibility = Visibility.Collapsed;
                    return;
                }

                previewText.Visibility = Visibility.Visible;
                double configuredFont = Math.Clamp(sizeSlider.Value, 10, 220);
                previewText.FontSize = configuredFont;
                previewText.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = Math.Max(1, previewText.DesiredSize.Width);
                double textHeight = Math.Max(1, previewText.DesiredSize.Height);

                double x = selectedPosition switch
                {
                    "LeftTop" => margin,
                    "LeftBottom" => margin,
                    "Center" => (previewWidth - textWidth) / 2,
                    _ => previewWidth - margin - textWidth
                };

                double y = selectedPosition switch
                {
                    "LeftTop" => margin,
                    "RightTop" => margin,
                    "Center" => (previewHeight - textHeight) / 2,
                    _ => previewHeight - margin - textHeight
                };

                Canvas.SetLeft(previewText, Math.Max(0, x));
                Canvas.SetTop(previewText, Math.Max(0, y));
            }

            void SelectPosition(string value)
            {
                selectedPosition = value;
                UpdatePositionButtons();
                UpdatePreview();
            }

            posLeftTop.Click += (_, _) => SelectPosition("LeftTop");
            posRightTop.Click += (_, _) => SelectPosition("RightTop");
            posLeftBottom.Click += (_, _) => SelectPosition("LeftBottom");
            posRightBottom.Click += (_, _) => SelectPosition("RightBottom");
            posCenter.Click += (_, _) => SelectPosition("Center");

            sizeSlider.ValueChanged += (_, _) =>
            {
                sizeBox.Text = sizeSlider.Value.ToString("0.#");
                UpdatePreview();
            };
            sizeBox.TextChanged += (_, _) =>
            {
                if (double.TryParse(sizeBox.Text.Trim(), out double val))
                {
                    val = Math.Clamp(val, 10, 220);
                    if (Math.Abs(sizeSlider.Value - val) > 0.01)
                    {
                        sizeSlider.Value = val;
                    }
                    UpdatePreview();
                }
            };
            textBox.TextChanged += (_, _) => UpdatePreview();

            chooseFontButton.Click += (_, _) =>
            {
                try
                {
                    using var fontDialog = new Forms.FontDialog
                    {
                        FontMustExist = true,
                        ShowColor = false,
                        MinSize = 10,
                        MaxSize = 220,
                        Font = new System.Drawing.Font(
                            fontTextBox.Text,
                            float.TryParse(sizeBox.Text, out float tempSize) ? tempSize : 48f)
                    };

                    if (fontDialog.ShowDialog() != Forms.DialogResult.OK || fontDialog.Font == null)
                    {
                        return;
                    }

                    fontTextBox.Text = fontDialog.Font.FontFamily.Name;
                    sizeBox.Text = fontDialog.Font.Size.ToString("0.#");
                    UpdatePreview();
                }
                catch (Exception ex)
                {
                    ShowStatus($"选择字体失败: {ex.Message}");
                }
            };

            cancelButton.Click += (_, _) => dialog.Close();
            saveButton.Click += (_, _) =>
            {
                if (!double.TryParse(sizeBox.Text.Trim(), out double parsedSize) || parsedSize < 10 || parsedSize > 220)
                {
                    ShowStatus("帧水印字号格式无效");
                    return;
                }

                string text = textBox.Text ?? string.Empty;
                string fontFamily = string.IsNullOrWhiteSpace(fontTextBox.Text)
                    ? "Microsoft YaHei UI"
                    : fontTextBox.Text.Trim();
                string position = string.IsNullOrWhiteSpace(selectedPosition) ? "RightBottom" : selectedPosition;

                _configManager.ProjectionNdiIdleFrameWatermarkText = text;
                _configManager.ProjectionNdiIdleFrameWatermarkPosition = position;
                _configManager.ProjectionNdiIdleFrameWatermarkFontSize = parsedSize;
                _configManager.ProjectionNdiIdleFrameWatermarkFontFamily = fontFamily;

                onChanged?.Invoke();
                refreshUi?.Invoke();

                string saved = _configManager.ProjectionNdiIdleFrameWatermarkText ?? string.Empty;
                ShowStatus(string.IsNullOrWhiteSpace(saved) ? "帧水印已清空（无文字时不显示）" : $"帧水印已更新: {saved}");
                dialog.Close();
            };

            dialog.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    dialog.Close();
                    e.Handled = true;
                    return;
                }

                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    saveButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    e.Handled = true;
                }
            };

            dialog.Content = root;
            dialog.Loaded += (_, _) =>
            {
                textBox.Focus();
                textBox.SelectAll();
                UpdatePositionButtons();
                UpdatePreview();
            };
            dialog.ShowDialog();

            Border CreateFormField(string labelText, UIElement control)
            {
                var panel = new Grid();
                panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
                panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock
                {
                    Text = labelText,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139))
                };
                Grid.SetRow(label, 0);
                panel.Children.Add(label);
                Grid.SetRow(control, 2);
                panel.Children.Add(control);

                return new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
                    Child = panel
                };
            }

            System.Windows.Controls.Button CreatePositionButton(string text)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Content = text,
                    Height = 34,
                    Margin = new Thickness(0, 0, 8, 0),
                    BorderThickness = new Thickness(1),
                    FontSize = 13
                };
                return btn;
            }

            static void ApplyPositionButtonStyle(System.Windows.Controls.Button button, bool isActive)
            {
                if (isActive)
                {
                    button.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
                    button.Foreground = WpfBrushes.White;
                    button.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
                }
                else
                {
                    button.Background = WpfBrushes.White;
                    button.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85));
                    button.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225));
                }
            }
        }

        private static bool TryLaunchNdiRuntimeInstaller()
        {
            try
            {
                string appBase = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates =
                {
                    Path.Combine(appBase, "data", "NDI6Runtime.exe"),
                    Path.Combine(AppContext.BaseDirectory, "data", "NDI6Runtime.exe"),
                    Path.Combine(Environment.CurrentDirectory, "data", "NDI6Runtime.exe")
                };

                foreach (string path in candidates)
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool PromptAndOpenNdiOfficialDownloadPage()
        {
            try
            {
                var result = System.Windows.MessageBox.Show(
                    "未检测到本地 NDI Runtime 安装包，是否打开 NDI 官方下载页面？",
                    "NDI Runtime 未检测到",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result != MessageBoxResult.Yes)
                {
                    return false;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = NdiOfficialDownloadUrl,
                    UseShellExecute = true
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StartNdiDiscoveryTimer(bool resetWindow = false)
        {
            if (_ndiDiscoveryTimer == null)
            {
                _ndiDiscoveryTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = NdiDiscoveryInterval
                };
                _ndiDiscoveryTimer.Tick += (_, _) => TryPublishNdiDiscoveryHeartbeat();
            }

            if (resetWindow || _ndiDiscoveryStartedAtUtc == DateTime.MinValue)
            {
                _ndiDiscoveryStartedAtUtc = DateTime.UtcNow;
            }

            if (!_ndiDiscoveryTimer.IsEnabled)
            {
                _ndiDiscoveryTimer.Start();
            }
        }

        private void StopNdiDiscoveryTimer()
        {
            if (_ndiDiscoveryTimer != null && _ndiDiscoveryTimer.IsEnabled)
            {
                _ndiDiscoveryTimer.Stop();
            }

            _ndiDiscoveryStartedAtUtc = DateTime.MinValue;
        }

        private void TryPublishNdiDiscoveryHeartbeat()
        {
            try
            {
                if (!(_configManager?.ProjectionNdiEnabled ?? false))
                {
                    StopNdiDiscoveryTimer();
                    return;
                }

                int connectionCount = _projectionNdiOutputManager?.GetClientConnectionCount() ?? 0;
                if (connectionCount > 0)
                {
                    StopNdiDiscoveryTimer();
                    return;
                }

                bool discoveryExpired = _ndiDiscoveryStartedAtUtc != DateTime.MinValue
                    && (DateTime.UtcNow - _ndiDiscoveryStartedAtUtc) >= NdiDiscoveryDuration;
                if (discoveryExpired)
                {
                    _configManager.ProjectionNdiEnabled = false;
                    _projectionNdiOutputManager?.Stop();
                    StopVideoNdiTimer();
                    StopNdiDiscoveryTimer();
                    ShowStatus("2分钟内无客户端连接，NDI输出已自动关闭");
                    return;
                }

                // 只在未投影时发送透明心跳，避免覆盖正在投影的静态内容。
                if (_projectionManager?.IsProjectionActive != true)
                {
                    _projectionNdiOutputManager?.PushTransparentIdleFrame();
                }
            }
            catch
            {
            }
        }

    }
}

