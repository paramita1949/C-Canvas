using System;
using System.Windows;
using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionWindowVisibilityPolicyTests
    {
        [Fact]
        public void ShouldHideOverlay_WhenMainWindowIsMinimized_ReturnsTrue()
        {
            var context = new LiveCaptionWindowContext(
                windowState: WindowState.Minimized,
                isMainWindowActive: false,
                isOverlayVisible: true,
                isOverlayActive: false,
                foregroundWindowHandle: IntPtr.Zero,
                mainWindowHandle: new IntPtr(11),
                overlayWindowHandle: new IntPtr(22));

            Assert.True(LiveCaptionWindowVisibilityPolicy.ShouldHideOverlay(context));
        }

        [Fact]
        public void ShouldHideOverlay_WhenMainWindowInactiveButOverlayActive_ReturnsFalse()
        {
            var context = new LiveCaptionWindowContext(
                windowState: WindowState.Normal,
                isMainWindowActive: false,
                isOverlayVisible: true,
                isOverlayActive: true,
                foregroundWindowHandle: new IntPtr(22),
                mainWindowHandle: new IntPtr(11),
                overlayWindowHandle: new IntPtr(22));

            Assert.False(LiveCaptionWindowVisibilityPolicy.ShouldHideOverlay(context));
        }

        [Fact]
        public void ShouldHideOverlay_WhenForegroundIsExternalApp_ReturnsTrue()
        {
            var context = new LiveCaptionWindowContext(
                windowState: WindowState.Normal,
                isMainWindowActive: false,
                isOverlayVisible: true,
                isOverlayActive: false,
                foregroundWindowHandle: new IntPtr(33),
                mainWindowHandle: new IntPtr(11),
                overlayWindowHandle: new IntPtr(22));

            Assert.True(LiveCaptionWindowVisibilityPolicy.ShouldHideOverlay(context));
        }
    }
}
