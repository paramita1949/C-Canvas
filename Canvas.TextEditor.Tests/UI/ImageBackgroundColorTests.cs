using System;
using System.IO;
using System.Linq;
using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class ImageBackgroundColorTests
    {
        [Fact]
        public void ImageBackgroundColor_DefaultsToBlack_AndInvalidValuesNormalizeToBlack()
        {
            Assert.Equal("#000000", ImageBackgroundColorCatalog.Normalize(null));
            Assert.Equal("#000000", ImageBackgroundColorCatalog.Normalize("not-a-color"));
            Assert.Equal("#AABBCC", ImageBackgroundColorCatalog.Normalize("#aabbcc"));
        }

        [Fact]
        public void ImageBackgroundColor_PresetsContainBlackWhiteAndCommonColors()
        {
            var names = ImageBackgroundColorCatalog.Presets.Select(x => x.Name).ToArray();

            Assert.Contains("黑色", names);
            Assert.Contains("白色", names);
            Assert.Contains("深灰", names);
            Assert.Contains("深蓝", names);
            Assert.Contains("深绿", names);
        }

        [Fact]
        public void ImageBackgroundColor_ConfigRoundTripsAcrossRestart()
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-image-background-{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(path);
                Assert.Equal("#000000", config.ImageBackgroundColor);

                config.ImageBackgroundColor = "#ffffff";

                var reloaded = new ConfigManager(path);
                Assert.Equal("#FFFFFF", reloaded.ImageBackgroundColor);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Fact]
        public void ImageBackgroundColor_UIAndProjectionUsePersistedState()
        {
            string root = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.xaml"));
            string contextMenu = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.ContextMenu.cs"));
            string projectionSync = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.ProjectionSync.cs"));
            string projectionManager = File.ReadAllText(Path.Combine(root, "Managers", "ProjectionManager.RenderFlow.cs"));

            Assert.Contains("x:Name=\"ImageContainer\" Background=\"{Binding ImageBackgroundBrush, RelativeSource={RelativeSource AncestorType={x:Type Window}}}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Header = \"背景色\"", contextMenu, StringComparison.Ordinal);
            Assert.Contains("ApplyImageBackgroundColor", contextMenu, StringComparison.Ordinal);
            Assert.Contains("ImageBackgroundColor", projectionSync, StringComparison.Ordinal);
            Assert.Contains("SetImageBackgroundColor", projectionManager, StringComparison.Ordinal);
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
