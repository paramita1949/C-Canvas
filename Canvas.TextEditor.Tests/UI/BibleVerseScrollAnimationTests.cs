using System;
using System.IO;
using ImageColorChanger.Core;
using ImageColorChanger.UI.Modules;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class BibleVerseScrollAnimationTests
    {
        [Fact]
        public void BibleVerseScrollAnimation_DefaultsToEnabled_AndRoundTrips()
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-bible-scroll-{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(path);
                var property = typeof(ConfigManager).GetProperty("BibleVerseScrollAnimationEnabled");
                Assert.NotNull(property);
                Assert.True((bool)property.GetValue(config));

                property.SetValue(config, false);

                var reloaded = new ConfigManager(path);
                Assert.False((bool)property.GetValue(reloaded));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void BibleVerseScrollAnimation_NullConfigFallbackRemainsEnabled(bool? configured, bool expected)
        {
            Assert.Equal(expected, BibleVerseScrollAnimationPolicy.ResolveEnabled(configured));
        }

        [Fact]
        public void BibleVerseScrollAnimation_MenuRefreshesAfterConfigManagerInjection()
        {
            string root = FindRepoRoot();
            string bootstrap = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.InfrastructureInit.cs"));

            Assert.Contains("_configManager = _mainWindowServices.GetRequired<ConfigManager>();", bootstrap, StringComparison.Ordinal);
            Assert.Contains("RefreshBibleVerseScrollMenuState();", bootstrap, StringComparison.Ordinal);
        }

        [Fact]
        public void BibleVerseScrollAnimation_ContextMenu_IsCheckableAndWired()
        {
            string root = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "UI", "Views", "BibleSectionView.xaml")).Replace("\r\n", "\n");
            string bindings = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.Bible.ViewBindings.cs"));

            Assert.Contains("x:Name=\"MenuBibleVerseScroll\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Header=\"经文滚动\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<Separator/>\n                    <MenuItem x:Name=\"MenuBibleVerseScroll\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"MenuBibleVerseScrollEnabled\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Header=\"开启\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"MenuBibleVerseScrollSpeedSlow\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"MenuBibleVerseScrollSpeedMedium\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"MenuBibleVerseScrollSpeedFast\"", xaml, StringComparison.Ordinal);
            Assert.Contains("MenuBibleVerseScrollEnabled.Click += BibleVerseScrollAnimation_Click", bindings, StringComparison.Ordinal);
            Assert.Contains("MenuBibleVerseScrollSpeedSlow.Click += BibleVerseScrollSpeed_Click", bindings, StringComparison.Ordinal);
            Assert.Contains("MenuBibleVerseScrollSpeedMedium.Click += BibleVerseScrollSpeed_Click", bindings, StringComparison.Ordinal);
            Assert.Contains("MenuBibleVerseScrollSpeedFast.Click += BibleVerseScrollSpeed_Click", bindings, StringComparison.Ordinal);
        }

        [Fact]
        public void BibleVerseScrollAnimation_SpeedDefaultsToMedium_AndRoundTrips()
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-bible-scroll-speed-{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(path);
                Assert.Equal(BibleVerseScrollAnimationPolicy.MediumSpeed, config.BibleVerseScrollSpeed);

                config.BibleVerseScrollSpeed = BibleVerseScrollAnimationPolicy.FastSpeed;

                var reloaded = new ConfigManager(path);
                Assert.Equal(BibleVerseScrollAnimationPolicy.FastSpeed, reloaded.BibleVerseScrollSpeed);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Theory]
        [InlineData(0, 420)]
        [InlineData(1, 260)]
        [InlineData(2, 140)]
        [InlineData(99, 260)]
        public void BibleVerseScrollAnimation_SpeedMapsToBoundedDuration(int speed, int expectedMilliseconds)
        {
            Assert.Equal(expectedMilliseconds, BibleVerseScrollAnimationPolicy.GetDurationMilliseconds(speed));
        }

        [Fact]
        public void BibleVerseScrollAnimation_NavigationUsesDirectionGate()
        {
            string root = FindRepoRoot();
            string navigation = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.Bible.Navigation.cs"));
            string helpers = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.Bible.Helpers.cs"));

            Assert.Contains("BibleVerseScrollAnimationPolicy.ShouldAnimate", navigation, StringComparison.Ordinal);
            Assert.Contains("ScrollToVerseSmooth(targetIndex)", navigation, StringComparison.Ordinal);
            Assert.Contains("ScrollToVerseInstant(targetIndex)", navigation, StringComparison.Ordinal);
            Assert.Contains("StopBibleVerseScrollAnimation()", helpers, StringComparison.Ordinal);
            Assert.Contains("GetDurationMilliseconds", helpers, StringComparison.Ordinal);
            Assert.DoesNotContain("_scrollAlignTimer.Tick -= null", helpers, StringComparison.Ordinal);
        }

        [Fact]
        public void BibleVerseScrollAnimation_PolicyExistsAsPureDirectionRule()
        {
            string root = FindRepoRoot();
            string policy = File.ReadAllText(Path.Combine(root, "UI", "Modules", "BibleVerseScrollAnimationPolicy.cs"));

            Assert.Contains("direction > 0", policy, StringComparison.Ordinal);
            Assert.Contains("enabled", policy, StringComparison.Ordinal);
            Assert.Contains("return direction > 0 && enabled", policy, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(-1, true, false)]
        [InlineData(-1, false, false)]
        [InlineData(0, true, false)]
        [InlineData(1, false, false)]
        [InlineData(1, true, true)]
        public void BibleVerseScrollAnimation_PolicyOnlyAllowsEnabledNextDirection(
            int direction,
            bool enabled,
            bool expected)
        {
            Assert.Equal(expected, BibleVerseScrollAnimationPolicy.ShouldAnimate(direction, enabled));
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ImageColorChanger.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("未找到 Canvas 仓库根目录。");
        }
    }
}
