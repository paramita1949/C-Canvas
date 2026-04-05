using System;
using System.Windows;

namespace ImageColorChanger.Services.LiveCaption
{
    internal readonly struct LiveCaptionWindowContext
    {
        public LiveCaptionWindowContext(
            WindowState windowState,
            bool isMainWindowActive,
            bool isOverlayVisible,
            bool isOverlayActive,
            IntPtr foregroundWindowHandle,
            IntPtr mainWindowHandle,
            IntPtr overlayWindowHandle)
        {
            WindowState = windowState;
            IsMainWindowActive = isMainWindowActive;
            IsOverlayVisible = isOverlayVisible;
            IsOverlayActive = isOverlayActive;
            ForegroundWindowHandle = foregroundWindowHandle;
            MainWindowHandle = mainWindowHandle;
            OverlayWindowHandle = overlayWindowHandle;
        }

        public WindowState WindowState { get; }

        public bool IsMainWindowActive { get; }

        public bool IsOverlayVisible { get; }

        public bool IsOverlayActive { get; }

        public IntPtr ForegroundWindowHandle { get; }

        public IntPtr MainWindowHandle { get; }

        public IntPtr OverlayWindowHandle { get; }
    }

    internal static class LiveCaptionWindowVisibilityPolicy
    {
        public static bool ShouldHideOverlay(in LiveCaptionWindowContext context)
        {
            if (!context.IsOverlayVisible)
            {
                return false;
            }

            if (context.WindowState == WindowState.Minimized)
            {
                return true;
            }

            if (context.IsMainWindowActive || context.IsOverlayActive)
            {
                return false;
            }

            if (context.ForegroundWindowHandle == IntPtr.Zero)
            {
                return true;
            }

            if (context.MainWindowHandle != IntPtr.Zero &&
                context.ForegroundWindowHandle == context.MainWindowHandle)
            {
                return false;
            }

            if (context.OverlayWindowHandle != IntPtr.Zero &&
                context.ForegroundWindowHandle == context.OverlayWindowHandle)
            {
                return false;
            }

            return true;
        }
    }
}
