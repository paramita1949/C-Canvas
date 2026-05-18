using System;
using System.Windows;
using System.Windows.Threading;
using ImageColorChanger.Core;

namespace ImageColorChanger.UI
{
    internal static class WindowPlacementTracker
    {
        private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(350);

        public static bool Restore(
            Window window,
            ConfigManager configManager,
            string key,
            bool includeSize = false,
            Action<bool> restoreCollapsedState = null)
        {
            if (window == null || configManager == null)
            {
                return false;
            }

            if (!configManager.TryGetWindowPlacement(key, out WindowPlacementState state))
            {
                return false;
            }

            double width = includeSize ? Math.Max(window.MinWidth, state.Width) : ResolveCurrentWidth(window);
            double height = includeSize ? Math.Max(window.MinHeight, state.Height) : ResolveCurrentHeight(window);
            Rect workArea = SystemParameters.WorkArea;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Width = width;
            window.Height = height;
            window.Left = Clamp(state.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
            window.Top = Clamp(state.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
            restoreCollapsedState?.Invoke(state.IsCollapsed);
            return true;
        }

        public static void Track(
            Window window,
            ConfigManager configManager,
            string key,
            bool includeSize = false,
            Func<bool> getCollapsedState = null)
        {
            if (window == null || configManager == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
            {
                Interval = SaveDelay
            };

            void Save()
            {
                if (window.WindowState != WindowState.Normal ||
                    !IsFinite(window.Left) ||
                    !IsFinite(window.Top))
                {
                    return;
                }

                double width = includeSize ? ResolveCurrentWidth(window) : Math.Max(1, window.Width);
                double height = includeSize ? ResolveCurrentHeight(window) : Math.Max(1, window.Height);
                configManager.SetWindowPlacement(
                    key,
                    window.Left,
                    window.Top,
                    width,
                    height,
                    getCollapsedState?.Invoke() == true);
            }

            void ScheduleSave()
            {
                if (window.WindowState != WindowState.Normal)
                {
                    return;
                }

                timer.Stop();
                timer.Start();
            }

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Save();
            };
            window.LocationChanged += (_, _) => ScheduleSave();
            window.SizeChanged += (_, _) => ScheduleSave();
            window.Closed += (_, _) =>
            {
                timer.Stop();
                Save();
            };
        }

        private static double ResolveCurrentWidth(Window window)
        {
            double width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            return IsFinite(width) && width > 0 ? width : Math.Max(1, window.MinWidth);
        }

        private static double ResolveCurrentHeight(Window window)
        {
            double height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            return IsFinite(height) && height > 0 ? height : Math.Max(1, window.MinHeight);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                max = min;
            }

            return Math.Max(min, Math.Min(max, value));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
