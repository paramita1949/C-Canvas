using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Services.Projection.Output;
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
            var openControlCenterItem = new MenuItem { Header = "打开 NDI 控制台..." };
            var quickEnableItem = new MenuItem { Header = "快速开关：启用NDI", IsCheckable = true };
            var runtimeStatusItem = new MenuItem { Header = "NDI状态：未连接", IsEnabled = false, Foreground = WpfBrushes.Gray };

            void RefreshUi()
            {
                quickEnableItem.IsChecked = _configManager?.ProjectionNdiEnabled == true;
                UpdateNdiRuntimeStatusItem(runtimeStatusItem);
            }

            openControlCenterItem.Click += (_, _) => ShowNdiControlCenterDialog();
            quickEnableItem.Click += (_, _) =>
            {
                if (_configManager == null)
                {
                    return;
                }

                SetNdiMasterEnabled(quickEnableItem.IsChecked);
                RefreshUi();
            };

            ndiMenu.SubmenuOpened += (_, _) => RefreshUi();
            RefreshUi();

            ndiMenu.Items.Add(openControlCenterItem);
            ndiMenu.Items.Add(CreateNdiMenuSeparator());
            ndiMenu.Items.Add(quickEnableItem);
            ndiMenu.Items.Add(runtimeStatusItem);
            return ndiMenu;
        }

        private void ShowNdiControlCenterDialog()
        {
            if (_configManager == null)
            {
                return;
            }

            var dialog = new NdiControlCenterWindow(
                loadState: () => new NdiControlCenterWindow.State
                {
                    MasterEnabled = _configManager?.ProjectionNdiEnabled == true,
                    LyricsEnabled = _ndiRouter?.IsChannelEnabled(Services.Ndi.NdiChannel.Lyrics) == true,
                    CaptionEnabled = _ndiRouter?.IsChannelEnabled(Services.Ndi.NdiChannel.Caption) == true,
                    LyricsTransparentEnabled = _configManager?.ProjectionNdiLyricsTransparentEnabled == true,
                    ConnectionCount = _projectionNdiOutputManager?.GetClientConnectionCount() ?? 0,
                    WatermarkPosition = _configManager?.ProjectionNdiIdleFrameWatermarkPosition,
                    WatermarkFontFamily = _configManager?.ProjectionNdiIdleFrameWatermarkFontFamily,
                    WatermarkFontSize = _configManager?.ProjectionNdiIdleFrameWatermarkFontSize ?? 48,
                    WatermarkOpacity = _configManager?.ProjectionNdiIdleFrameWatermarkOpacity ?? 43
                },
                setMaster: enabled => SetNdiMasterEnabled(enabled),
                setLyrics: enabled =>
                {
                    _ndiRouter?.SetChannelEnabled(Services.Ndi.NdiChannel.Lyrics, enabled);
                    ShowStatus(enabled ? "歌词NDI已开启" : "歌词NDI已关闭");
                },
                setCaption: enabled =>
                {
                    _ndiRouter?.SetChannelEnabled(Services.Ndi.NdiChannel.Caption, enabled);
                    if (!enabled)
                    {
                        _ndiRouter?.PushCaptionIdleFrame();
                    }

                    ShowStatus(enabled ? "字幕NDI已开启" : "字幕NDI已关闭");
                },
                setTransparent: enabled =>
                {
                    if (_configManager == null)
                    {
                        return;
                    }

                    _configManager.ProjectionNdiLyricsTransparentEnabled = enabled;
                    ShowStatus(enabled ? "歌词透明已开启" : "歌词透明已关闭");
                },
                setWatermarkPosition: position =>
                {
                    if (_configManager == null)
                    {
                        return;
                    }
                    _configManager.ProjectionNdiIdleFrameWatermarkPosition = position;
                },
                setWatermarkFontFamily: fontFamily =>
                {
                    if (_configManager == null)
                    {
                        return;
                    }
                    _configManager.ProjectionNdiIdleFrameWatermarkFontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Microsoft YaHei UI" : fontFamily;
                },
                setWatermarkFontSize: fontSize =>
                {
                    if (_configManager == null)
                    {
                        return;
                    }
                    _configManager.ProjectionNdiIdleFrameWatermarkFontSize = Math.Clamp(fontSize, 10, 220);
                },
                setWatermarkOpacity: opacity =>
                {
                    if (_configManager == null)
                    {
                        return;
                    }
                    _configManager.ProjectionNdiIdleFrameWatermarkOpacity = Math.Clamp(opacity, 0, 100);
                },
                pushWatermarkFrame: () =>
                {
                    _projectionNdiOutputManager?.PushTransparentIdleFrame();
                })
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        private void UpdateNdiRuntimeStatusItem(MenuItem statusItem)
        {
            if (_configManager == null || statusItem == null)
            {
                return;
            }

            int connectionCount = _projectionNdiOutputManager?.GetClientConnectionCount() ?? 0;
            bool connected = _configManager.ProjectionNdiEnabled && connectionCount > 0;
            statusItem.Header = connected ? $"NDI状态：已连接（{connectionCount}台）" : "NDI状态：未连接";
            statusItem.Foreground = connected ? WpfBrushes.LimeGreen : WpfBrushes.Gray;
        }

        private void SetNdiMasterEnabled(bool enabled)
        {
            if (_configManager == null)
            {
                return;
            }

            _configManager.ProjectionNdiEnabled = enabled;
            if (!enabled)
            {
                _projectionNdiOutputManager?.Stop();
                StopVideoNdiTimer();
                StopNdiDiscoveryTimer();
                ShowStatus("NDI输出已关闭");
                return;
            }

            if (_videoPlayerManager?.IsPlaying == true)
            {
                StartVideoNdiTimer();
            }

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
                return;
            }

            _projectionNdiOutputManager?.PushTransparentIdleFrame();
            StartNdiDiscoveryTimer(resetWindow: true);
            ShowStatus("NDI已开启，等待客户端连接");
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




