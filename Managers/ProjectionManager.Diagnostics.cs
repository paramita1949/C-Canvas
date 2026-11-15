using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ImageColorChanger.Services.Diagnostics;
using Microsoft.Win32;
using System.Globalization;
using System.Threading;

namespace ImageColorChanger.Managers
{
    public partial class ProjectionManager
    {
        private ProjectionDiagnosticLog _projectionDiagnosticLog;
        private DispatcherTimer _projectionDiagnosticTimer;
        private bool _projectionDiagnosticDisplaySubscribed;
        private int _projectionDiagnosticTicks;
        private int _projectionDiagnosticChangeEvents;
        private int _projectionDiagnosticEventPending;
        public string ProjectionDiagnosticLogPath { get; private set; }

        private void TryRunDiagnosticOnMainDispatcher(Action action)
        {
            try
            {
                var dispatcher = _mainWindow?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                if (dispatcher.CheckAccess()) action();
                else dispatcher.Invoke(action);
            }
            catch (Exception ex)
            {
                _projectionDiagnosticLog?.Append("diagnostic-dispatch-failed", ProjectionDiagnosticLog.DescribeException(ex));
            }
        }

        private void BeginProjectionDiagnostics()
        {
            try
            {
                EndProjectionDiagnostics("previous-session-ended");
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                // 缺失special folder不回退到相对路径/当前目录。
                var directories = new[]
                {
                    string.IsNullOrEmpty(desktop) ? null : Path.Combine(desktop, "咏慕投影诊断"),
                    string.IsNullOrEmpty(local) ? null : Path.Combine(local, "CanvasCast", "Logs", "Projection")
                };
                _projectionDiagnosticLog = new ProjectionDiagnosticLog(directories,
                    typeof(ProjectionManager).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(ProjectionManager).Assembly.GetName().Version?.ToString());
                ProjectionDiagnosticLogPath = _projectionDiagnosticLog.FilePath;
                _projectionDiagnosticTicks = 0;
                _projectionDiagnosticChangeEvents = 0;
                CaptureProjectionDiagnostics("before-open");
                SystemEvents.DisplaySettingsChanged += ProjectionDiagnosticDisplaySettingsChanged;
                _projectionDiagnosticDisplaySubscribed = true;
            }
            catch (Exception ex)
            {
                _projectionDiagnosticLog?.Append("diagnostic-start-failed", ProjectionDiagnosticLog.DescribeException(ex));
                EndProjectionDiagnostics("diagnostic-start-aborted");
            }
        }

        private void CaptureProjectionDiagnostics(string phase, string note = null)
        {
            var log = _projectionDiagnosticLog;
            if (log == null) return;
            try
            {
                var text = new StringBuilder();
                text.AppendLine("Note=" + note);
                text.AppendLine("SelectedIndex=" + (_screenComboBox?.SelectedIndex ?? -1));
                text.AppendLine("CurrentScreenIndex=" + _currentScreenIndex);
                text.AppendLine("CachedScreenCount=" + (_screens?.Count ?? 0));
                if (_screens != null)
                {
                    for (int i = 0; i < _screens.Count; i++)
                    {
                        var s = _screens[i];
                        text.AppendLine(FormattableString.Invariant($"CachedScreen[{i}].Device={s.DeviceName}; Primary={s.IsPrimary}; PhysicalBounds={s.PhysicalBounds.ToString(CultureInfo.InvariantCulture)}; WpfBounds={s.WpfBounds.ToString(CultureInfo.InvariantCulture)}; DpiScale=({s.DpiScale.X},{s.DpiScale.Y})"));
                    }
                    int index = _screenComboBox?.SelectedIndex ?? -1;
                    if (index >= 0 && index < _screens.Count)
                        text.AppendLine("SelectedDevice=" + _screens[index].DeviceName);
                }
                text.Append(ProjectionDiagnosticSnapshotCollector.Capture(_mainWindow, _projectionWindow));
                log.Append(phase, text.ToString());
            }
            catch (Exception ex) { log.Append(phase + "-capture-failed", ProjectionDiagnosticLog.DescribeException(ex)); }
        }

        private void ScheduleProjectionDiagnosticSnapshots()
        {
            try
            {
                if (_projectionDiagnosticLog == null || _mainWindow == null) return;
                _projectionDiagnosticTimer = new DispatcherTimer(DispatcherPriority.Background, _mainWindow.Dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _projectionDiagnosticTimer.Tick += ProjectionDiagnosticTimerTick;
                _projectionDiagnosticTimer.Start();
            }
            catch (Exception ex) { _projectionDiagnosticLog?.Append("delayed-sampling-failed", ProjectionDiagnosticLog.DescribeException(ex)); }
        }

        private void ProjectionDiagnosticTimerTick(object sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, _projectionDiagnosticTimer)) return;
            _projectionDiagnosticTicks++;
            if (_projectionDiagnosticTicks == 1) CaptureProjectionDiagnostics("after-open-1s");
            if (_projectionDiagnosticTicks < 3) return;
            CaptureProjectionDiagnostics("after-open-3s");
            StopProjectionDiagnosticTimer();
        }

        private void ProjectionDiagnosticDisplaySettingsChanged(object sender, EventArgs e)
        {
            var log = _projectionDiagnosticLog;
            if (log == null) return;
            if (Interlocked.CompareExchange(ref _projectionDiagnosticEventPending, 1, 0) != 0) return;
            try
            {
                var dispatcher = _mainWindow?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    Interlocked.Exchange(ref _projectionDiagnosticEventPending, 0);
                    return;
                }
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    // 旧会话的后台事件不能写进后来新开的会话；最多采集20次系统变更。
                    try
                    {
                        if (!ReferenceEquals(log, _projectionDiagnosticLog) || _projectionDiagnosticChangeEvents >= 20) return;
                        _projectionDiagnosticChangeEvents++;
                        CaptureProjectionDiagnostics("display-settings-changed");
                    }
                    finally { Interlocked.Exchange(ref _projectionDiagnosticEventPending, 0); }
                }));
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _projectionDiagnosticEventPending, 0);
                log.Append("display-event-dispatch-failed", ProjectionDiagnosticLog.DescribeException(ex));
            }
        }

        private void StopProjectionDiagnosticTimer()
        {
            if (_projectionDiagnosticTimer == null) return;
            var timer = _projectionDiagnosticTimer;
            _projectionDiagnosticTimer = null;
            timer.Stop();
            timer.Tick -= ProjectionDiagnosticTimerTick;
        }

        private void EndProjectionDiagnostics(string phase)
        {
            CaptureProjectionDiagnostics(phase);
            try { StopProjectionDiagnosticTimer(); }
            catch (Exception ex) { _projectionDiagnosticLog?.Append("diagnostic-timer-cleanup-failed", ProjectionDiagnosticLog.DescribeException(ex)); }
            try
            {
                if (_projectionDiagnosticDisplaySubscribed)
                    SystemEvents.DisplaySettingsChanged -= ProjectionDiagnosticDisplaySettingsChanged;
                _projectionDiagnosticDisplaySubscribed = false;
            }
            catch (Exception ex) { _projectionDiagnosticLog?.Append("diagnostic-event-cleanup-failed", ProjectionDiagnosticLog.DescribeException(ex)); }
            finally { _projectionDiagnosticLog = null; }
        }
    }
}
