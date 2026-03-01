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
            var enabledItem = new MenuItem { Header = "NDI输出", IsCheckable = true };
            var lyricsTransparentItem = new MenuItem { Header = "歌词透明", IsCheckable = true };
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

            ndiMenu.SubmenuOpened += (_, _) => RefreshUi();
            RefreshUi();

            ndiMenu.Items.Add(enabledItem);
            ndiMenu.Items.Add(new Separator());
            ndiMenu.Items.Add(lyricsTransparentItem);
            ndiMenu.Items.Add(new Separator());
            ndiMenu.Items.Add(runtimeStatusItem);
            return ndiMenu;
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
