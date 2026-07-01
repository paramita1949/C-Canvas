using System;
using System.IO;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class UpdatePolicyWindowTests
    {
        [Fact]
        public void UpdateWindow_ShouldRemainClosableForAllServerPolicies()
        {
            string repoRoot = FindRepoRoot();
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "UpdateWindow.xaml.cs"));

            Assert.Contains("public UpdateWindow(VersionInfo versionInfo)", code);
            Assert.DoesNotContain("public UpdateWindow(VersionInfo versionInfo, bool isRequired)", code);
            Assert.DoesNotContain("SkipButtonText.Text = \"退出软件\"", code);
            Assert.DoesNotContain("CloseButton.Visibility = Visibility.Collapsed", code);
            Assert.DoesNotContain("System.Windows.Application.Current.Shutdown()", code);
        }

        [Fact]
        public void UpdateWindow_ShouldNotKeepRequiredCloseGuard()
        {
            string repoRoot = FindRepoRoot();
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "UpdateWindow.xaml.cs"))
                .Replace("\r\n", "\n");

            Assert.DoesNotContain("_isRequired", code);
            Assert.DoesNotContain("_allowRequiredClose", code);
            Assert.DoesNotContain("e.Cancel = true", code);
        }

        [Fact]
        public void MainWindow_UpdateCheck_ShouldOpenClosableWindowForRequiredPolicy()
        {
            string repoRoot = FindRepoRoot();
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Lifecycle.cs"));

            Assert.Contains("ClientUsageReportService", code);
            Assert.Contains("RequiresUpgrade", code);
            Assert.Contains("policy.RequiresUpgrade || policy.ShouldRecommend", code);
            Assert.Contains("ShowStartupUpdatePolicyWindow(versionInfo)", code);
            Assert.Contains("new UpdateWindow(versionInfo)", code);
            Assert.DoesNotContain("new UpdateWindow(versionInfo, isRequired: true)", code);
            Assert.DoesNotContain("ShowRequiredUpdateWindow", code);
        }

        [Fact]
        public void MainWindow_UpdateCheck_ShouldOpenRecommendedPolicyWindow()
        {
            string repoRoot = FindRepoRoot();
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Lifecycle.cs"));

            Assert.Contains("policy.ShouldRecommend", code);
            Assert.Contains("ShowStartupUpdatePolicyWindow(versionInfo)", code);
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
