using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class ImageZoomControlTests
    {
        [Fact]
        public void ImageDisplay_ShouldExposeBottomCenteredMinusAndPlusControls()
        {
            string repoRoot = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.xaml"))
                .Replace("\r\n", "\n");

            int imageSurfaceStart = xaml.IndexOf("<!-- 右侧：图片显示区 -->", StringComparison.Ordinal);
            int imageSurfaceEnd = xaml.IndexOf("<!--  圣经显示区域", imageSurfaceStart, StringComparison.Ordinal);
            string imageSurface = xaml.Substring(imageSurfaceStart, imageSurfaceEnd - imageSurfaceStart);

            Assert.Contains("x:Name=\"ImageZoomControlsPanel\"", imageSurface);
            Assert.Contains("HorizontalAlignment=\"Center\"", imageSurface);
            Assert.Contains("VerticalAlignment=\"Bottom\"", imageSurface);
            Assert.Contains("x:Name=\"BtnImageZoomOut\"", imageSurface);
            Assert.Contains("x:Name=\"ImageZoomWheelHint\"", imageSurface);
            Assert.Contains("x:Name=\"BtnImageZoomIn\"", imageSurface);
            Assert.Contains("Data=\"{StaticResource IconLucideMinus}\"", imageSurface);
            Assert.Contains("Data=\"{StaticResource IconLucidePlus}\"", imageSurface);
            Assert.Contains("Data=\"{StaticResource IconLucideMouseWheel}\"", imageSurface);
            Assert.Contains("Width=\"50\" Height=\"40\"", imageSurface);
            Assert.Contains("Cursor=\"SizeNS\"", imageSurface);
            Assert.Contains("ToolTip=\"滚轮缩放图片\"", imageSurface);
            Assert.Contains("PreviewMouseWheel=\"ImageZoomWheelHint_PreviewMouseWheel\"", imageSurface);
            Assert.Contains("Click=\"BtnImageZoomOut_Click\"", imageSurface);
            Assert.Contains("Click=\"BtnImageZoomIn_Click\"", imageSurface);
        }

        [Fact]
        public void ImageZoomButtons_ShouldUseSharedClampedZoomStep()
        {
            string repoRoot = FindRepoRoot();
            string zoomCode = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Zoom.cs"));
            string imageCoreCode = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.ImageCore.cs"));

            Assert.Contains("private void BtnImageZoomOut_Click", zoomCode);
            Assert.Contains("private void BtnImageZoomIn_Click", zoomCode);
            Assert.Contains("private void ImageZoomWheelHint_PreviewMouseWheel", zoomCode);
            Assert.Contains("ChangeZoomByStep(-ZoomStep)", zoomCode);
            Assert.Contains("ChangeZoomByStep(ZoomStep)", zoomCode);
            Assert.Contains("ChangeZoomFromMouseWheel(e)", zoomCode);
            Assert.Contains("ChangeZoomByStep(e.Delta / 120.0 * ZoomStep)", zoomCode);
            Assert.Contains("Math.Max(MinZoom, Math.Min(MaxZoom, _currentZoom + delta))", zoomCode);
            Assert.Contains("UpdateImageZoomControlsVisibility", imageCoreCode);
        }

        [Fact]
        public void ImageZoomControls_ShouldHideOutsideImageModeAndBlockStaleImageZoom()
        {
            string repoRoot = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.xaml"));
            string zoomCode = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Zoom.cs"));

            Assert.Equal(2, Regex.Matches(xaml, "IsVisibleChanged=\"ImageZoomSurface_IsVisibleChanged\"").Count);
            Assert.Contains("private void ImageZoomSurface_IsVisibleChanged", zoomCode);
            Assert.Contains("private bool CanChangeImageZoom()", zoomCode);
            Assert.Contains("ImageScrollViewer.Visibility == Visibility.Visible", zoomCode);
            Assert.Contains("VideoContainer.Visibility != Visibility.Visible", zoomCode);
            Assert.Contains("if (!CanChangeImageZoom())", zoomCode);
        }

        [Fact]
        public void ImageZoomIconResources_ShouldHaveUniqueKeys()
        {
            string repoRoot = FindRepoRoot();
            string resources = File.ReadAllText(Path.Combine(
                repoRoot,
                "UI",
                "Resources",
                "Styles",
                "MainWindow.BaseStyles.xaml"));

            Assert.Single(Regex.Matches(resources, "x:Key=\"IconLucideMinus\"").Cast<Match>());
            Assert.Single(Regex.Matches(resources, "x:Key=\"IconLucidePlus\"").Cast<Match>());
            Assert.Single(Regex.Matches(resources, "x:Key=\"IconLucideMouseWheel\"").Cast<Match>());
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ImageColorChanger.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("Canvas repository root was not found.");
        }
    }
}
