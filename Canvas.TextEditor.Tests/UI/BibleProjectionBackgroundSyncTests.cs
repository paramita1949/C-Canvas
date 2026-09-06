using System;
using System.IO;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class BibleProjectionBackgroundSyncTests
    {
        [Fact]
        public void BibleProjection_PropagatesConfiguredBackgroundToProjectionManager()
        {
            string navigation = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.Bible.Navigation.cs"));
            string search = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.Bible.Search.cs"));

            Assert.DoesNotContain(
                "UpdateBibleProjectionWithVisualBrush(BibleVerseScrollViewer);",
                navigation + search,
                StringComparison.Ordinal);
            Assert.Contains(
                "UpdateBibleProjectionWithVisualBrush(BibleVerseScrollViewer, _configManager?.BibleBackgroundColor);",
                navigation + search,
                StringComparison.Ordinal);
        }

        [Fact]
        public void ProjectionManager_PaintsBibleBackgroundBehindCapturedContent()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Managers", "ProjectionManager.cs"));

            Assert.Contains(
                "UpdateBibleProjectionWithVisualBrush(ScrollViewer bibleScrollViewer, string backgroundColorHex = null)",
                source,
                StringComparison.Ordinal);
            Assert.Contains("_projectionScrollViewer.Background = projectionBackground;", source, StringComparison.Ordinal);
            Assert.Contains("_projectionContainer.Background = projectionBackground;", source, StringComparison.Ordinal);
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
