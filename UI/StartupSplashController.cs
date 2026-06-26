using System;
using System.Threading;
using System.Windows.Threading;

namespace ImageColorChanger.UI
{
    internal static class StartupSplashController
    {
        private static readonly object SyncRoot = new object();
        private static Thread _thread;
        private static Dispatcher _dispatcher;
        private static StartupSplashWindow _window;
        private static ManualResetEventSlim _readySignal;
        private static string _latestStatus = "正在启动...";
        private static string _latestScripture = "";

        public static void Show(string initialStatus, string scriptureText)
        {
            lock (SyncRoot)
            {
                _latestStatus = NormalizeStatus(initialStatus);
                _latestScripture = NormalizeScripture(scriptureText);
                if (_thread != null)
                {
                    UpdateStatus(_latestStatus);
                    UpdateScripture(_latestScripture);
                    return;
                }

                _readySignal = new ManualResetEventSlim(false);
                _thread = new Thread(RunSplashThread)
                {
                    IsBackground = true,
                    Name = "Startup Splash"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }

            try
            {
                _readySignal?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 开屏线程失败不能阻断主程序启动。
            }
        }

        public static void Show(string initialStatus)
        {
            Show(initialStatus, StartupScriptureVerseProvider.GetRandomVerseText());
        }

        public static void UpdateStatus(string status)
        {
            string normalizedStatus = NormalizeStatus(status);
            Dispatcher dispatcher;

            lock (SyncRoot)
            {
                _latestStatus = normalizedStatus;
                dispatcher = _dispatcher;
            }

            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                dispatcher.BeginInvoke(new Action(() => _window?.SetStatus(normalizedStatus)));
            }
            catch
            {
                // 启动状态更新失败不影响主程序启动。
            }
        }

        private static void UpdateScripture(string scriptureText)
        {
            string normalizedScripture = NormalizeScripture(scriptureText);
            Dispatcher dispatcher;

            lock (SyncRoot)
            {
                _latestScripture = normalizedScripture;
                dispatcher = _dispatcher;
            }

            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                dispatcher.BeginInvoke(new Action(() => _window?.SetScripture(normalizedScripture)));
            }
            catch
            {
                // 经句更新失败不影响主程序启动。
            }
        }

        public static void Close()
        {
            Dispatcher dispatcher;

            lock (SyncRoot)
            {
                dispatcher = _dispatcher;
            }

            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                ResetState();
                return;
            }

            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _window?.Close();
                    }
                    catch
                    {
                    }
                }));
            }
            catch
            {
                ResetState();
            }
        }

        private static void RunSplashThread()
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var window = new StartupSplashWindow(_latestStatus, _latestScripture);
                window.Closed += (_, _) =>
                {
                    ResetState();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                };

                lock (SyncRoot)
                {
                    _dispatcher = dispatcher;
                    _window = window;
                }

                _readySignal?.Set();
                window.Show();
                Dispatcher.Run();
            }
            catch
            {
                _readySignal?.Set();
                ResetState();
            }
        }

        private static string NormalizeStatus(string status)
        {
            return string.IsNullOrWhiteSpace(status)
                ? "正在启动..."
                : status.Trim();
        }

        private static string NormalizeScripture(string scriptureText)
        {
            return string.IsNullOrWhiteSpace(scriptureText)
                ? StartupScriptureVerseProvider.GetRandomVerseText()
                : scriptureText.Trim();
        }

        private static void ResetState()
        {
            lock (SyncRoot)
            {
                _window = null;
                _dispatcher = null;
                _thread = null;
                _readySignal?.Dispose();
                _readySignal = null;
                _latestScripture = "";
            }
        }
    }
}
