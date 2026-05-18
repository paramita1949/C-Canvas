using ImageColorChanger.Core;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class WindowPlacementConfigTests
    {
        [Fact]
        public void SetWindowPlacement_PersistsBoundsAndCollapsedState()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas-window-placement-{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);

                config.SetWindowPlacement("ai.panel", 120, 80, 360, 104, isCollapsed: true);

                var reloaded = new ConfigManager(tempFile);
                Assert.True(reloaded.TryGetWindowPlacement("ai.panel", out WindowPlacementState state));
                Assert.Equal(120, state.Left);
                Assert.Equal(80, state.Top);
                Assert.Equal(360, state.Width);
                Assert.Equal(104, state.Height);
                Assert.True(state.IsCollapsed);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void SetWindowPlacement_IgnoresInvalidBounds()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas-window-placement-{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);

                config.SetWindowPlacement("style.local", double.NaN, 0, 720, 475);

                Assert.False(config.TryGetWindowPlacement("style.local", out _));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
