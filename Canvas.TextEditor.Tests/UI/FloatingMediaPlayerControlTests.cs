using System;
using System.IO;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class FloatingMediaPlayerControlTests
    {
        [Fact]
        public void MainWindow_ShouldHostMediaPlayerInBottomRowOutsideProjectionSurface()
        {
            string repoRoot = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.xaml"))
                .Replace("\r\n", "\n");

            Assert.Contains("<views:MediaPlayerSectionView x:Name=\"MediaPlayerSectionView\"", xaml);
            Assert.Contains("Grid.Row=\"2\"", xaml);

            int videoContainerStart = xaml.IndexOf("<Grid x:Name=\"VideoContainer\"", StringComparison.Ordinal);
            int videoContainerEnd = xaml.IndexOf("<!--  圣经显示区域", videoContainerStart, StringComparison.Ordinal);
            string videoContainer = xaml.Substring(videoContainerStart, videoContainerEnd - videoContainerStart);
            Assert.DoesNotContain("MediaPlayerSectionView", videoContainer);
        }

        [Fact]
        public void MediaPlayerControl_ShouldUseMainWindowBottomSurface()
        {
            string repoRoot = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(repoRoot, "UI", "Views", "MediaPlayerSectionView.xaml"));

            Assert.Contains("Background=\"#2C2C2C\"", xaml);
            Assert.Contains("BorderThickness=\"0,1,0,0\"", xaml);
            Assert.DoesNotContain("Panel.ZIndex=\"500\"", xaml);
        }

        [Fact]
        public void MediaPlayerControl_ShouldRetainCompletePlaybackActions()
        {
            string repoRoot = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(repoRoot, "UI", "Views", "MediaPlayerSectionView.xaml"));

            foreach (string controlName in new[]
            {
                "BtnMediaPrev", "BtnMediaPlayPause", "BtnMediaNext",
                "MediaProgressSlider", "BtnPlayMode", "VolumeSlider"
            })
            {
                Assert.Contains($"x:Name=\"{controlName}\"", xaml);
            }
        }

        [Fact]
        public void MediaControl_ShouldNotContainSquareStopButton()
        {
            string repoRoot = FindRepoRoot();
            string viewXaml = File.ReadAllText(Path.Combine(repoRoot, "UI", "Views", "MediaPlayerSectionView.xaml"));
            string bindings = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Media.ViewBindings.cs"));

            Assert.DoesNotContain("x:Name=\"BtnMediaStop\"", viewXaml);
            Assert.DoesNotContain("BtnMediaStop.Click", bindings);
        }





        [Fact]
        public void VideoViewInitialization_ShouldNotRemoveANullSizeChangedHandlerDuringReentrancy()
        {
            string repoRoot = FindRepoRoot();
            string controller = File.ReadAllText(Path.Combine(repoRoot, "UI", "Modules", "MediaModuleController.cs"));

            Assert.Contains("bool mediaPlayerInitializationInProgress", controller);
            Assert.Contains("var handler = _mainVideoViewSizeChangedHandler", controller);
            Assert.Contains("if (handler != null)", controller);
        }

        [Fact]
        public void StartupVideoPrewarm_ShouldNotWaitForProjectTreeIdle()
        {
            string repoRoot = FindRepoRoot();
            string lifecycle = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Lifecycle.cs"));
            string bootstrap = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.UIBootstrap.cs"));

            Assert.Contains("StartDeferredVideoPlayerInitialization(delayMs: 200)", lifecycle);
            Assert.DoesNotContain("await WaitForProjectTreeIdleAsync", bootstrap);
        }

        [Fact]
        public void VideoView_ShouldNotOwnPlaybackControlOverlay()
        {
            string repoRoot = FindRepoRoot();
            string initialization = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.InfrastructureInit.cs"));
            string controller = File.ReadAllText(Path.Combine(repoRoot, "UI", "Modules", "MediaModuleController.cs"));

            Assert.Contains("InitializeMainVideoView(VideoContainer)", initialization);
            Assert.DoesNotContain("_mainVideoView.Content = overlayContent", controller);
        }

        [Fact]
        public void LoadingVideo_ShouldShowBottomControlAndWriteDiagnostics()
        {
            string repoRoot = FindRepoRoot();
            string videoCode = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Video.cs"));
            string controller = File.ReadAllText(Path.Combine(repoRoot, "UI", "Modules", "MediaModuleController.cs"));
            string playbackCore = File.ReadAllText(Path.Combine(repoRoot, "Managers", "VideoPlayerManager.PlaybackCore.cs"));

            Assert.Contains("MediaPlayerPanel.Visibility = Visibility.Visible", videoCode);
            Assert.Contains("[MediaPlayerUI] 显示底部控制栏", videoCode);
            Assert.Contains("[MediaPlayerInit] Main VideoView", controller);
            Assert.Contains("[VideoPlayerManager] MediaPlayer 绑定", playbackCore);
        }

        [Fact]
        public void BottomControl_ShouldMatchVideoAreaWidthInsteadOfWholeWindow()
        {
            string repoRoot = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.xaml"));

            Assert.Contains("Width=\"{Binding ActualWidth, ElementName=VideoContainer}\"", xaml);
            Assert.Contains("HorizontalAlignment=\"Right\"", xaml);
        }

        [Fact]
        public void PlayModeButton_ShouldExposeThreeRequestedModes()
        {
            string repoRoot = FindRepoRoot();
            string viewXaml = File.ReadAllText(Path.Combine(repoRoot, "UI", "Views", "MediaPlayerSectionView.xaml"));
            string mediaCode = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Media.cs"));

            Assert.Contains("Header=\"随机播放\" Tag=\"Random\"", viewXaml);
            Assert.Contains("Header=\"单曲循环\" Tag=\"LoopOne\"", viewXaml);
            Assert.Contains("Header=\"歌单循环\" Tag=\"LoopAll\"", viewXaml);
            Assert.Contains("MediaPlayModeMenuItem_Click", mediaCode);
            Assert.Contains("ApplyMediaPlayMode", mediaCode);
            Assert.DoesNotContain("nextMode = PlayMode.Sequential", mediaCode);
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UI", "MainWindow.xaml")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Canvas repo root from test output directory.");
        }
    }
}
