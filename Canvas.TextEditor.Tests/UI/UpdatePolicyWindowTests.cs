using System;
using System.IO;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class UpdatePolicyWindowTests
    {
        [Fact]
        public void UpdateWindow_ShouldSupportRequiredUpgradeMode()
        {
            string repoRoot = FindRepoRoot();
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "UpdateWindow.xaml.cs"));

            Assert.Contains("public UpdateWindow(VersionInfo versionInfo, bool isRequired)", code);
            Assert.Contains("SkipButtonText.Text = \"退出软件\"", code);
            Assert.Contains("CloseButton.Visibility = Visibility.Collapsed", code);
            Assert.Contains("System.Windows.Application.Current.Shutdown()", code);
        }

        [Fact]
        public void UpdateWindow_RequiredMode_ShouldRestoreCloseGuardWhenUpdateThrows()
        {
            string repoRoot = FindRepoRoot();
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "UpdateWindow.xaml.cs"))
                .Replace("\r\n", "\n");

            Assert.Matches(
                @"catch\s*\(Exception ex\)\s*\{\s*if\s*\(_isRequired\)\s*\{\s*_allowRequiredClose = false;",
                code);
        }

        [Fact]
        public void MainWindow_UpdateCheck_ShouldOpenRequiredPolicyWindow()
        {
            string repoRoot = FindRepoRoot();
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Lifecycle.cs"));

            Assert.Contains("ClientUsageReportService", code);
            Assert.Contains("RequiresUpgrade", code);
            Assert.Contains("new UpdateWindow(versionInfo, isRequired: true)", code);
        }

        [Fact]
        public void MainWindow_UpdateCheck_ShouldOpenRecommendedPolicyWindow()
        {
            string repoRoot = FindRepoRoot();
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Lifecycle.cs"));

            Assert.Contains("policy.ShouldRecommend", code);
            Assert.Contains("ShowRecommendedUpdateWindow(versionInfo)", code);
            Assert.Contains("new UpdateWindow(versionInfo)", code);
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UI", "UpdateWindow.xaml.cs")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Canvas repo root from test output directory.");
        }
    }
}
