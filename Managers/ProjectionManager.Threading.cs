using System;
using System.Windows.Threading;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 线程调度与节流辅助（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        private bool ShouldThrottleSync()
        {
            var currentTime = DateTime.Now;
            if (currentTime - _lastSyncTime < _syncThrottleInterval)
            {
                return true;
            }

            _lastSyncTime = currentTime;
            return false;
        }

        private void RunOnMainDispatcher(Action action)
        {
            _mainWindow.Dispatcher.Invoke(action);
        }

        private T RunOnMainDispatcher<T>(Func<T> action)
        {
            return _mainWindow.Dispatcher.Invoke(action);
        }

        private bool TryRunOnProjectionDispatcher(Action action)
        {
            var projectionWindow = _projectionWindow;
            if (projectionWindow == null)
            {
                return false;
            }

            var dispatcher = projectionWindow.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return false;
            }

            if (dispatcher.CheckAccess())
            {
                action();
                return true;
            }

            dispatcher.Invoke(action);
            return true;
        }

        private bool TryBeginOnProjectionDispatcher(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            var projectionWindow = _projectionWindow;
            if (projectionWindow == null)
            {
                return false;
            }

            var dispatcher = projectionWindow.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return false;
            }

            dispatcher.BeginInvoke(priority, action);
            return true;
        }
    }
}
