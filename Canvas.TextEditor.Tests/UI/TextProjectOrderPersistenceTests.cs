using System;
using System.IO;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class TextProjectOrderPersistenceTests
    {
        [Fact]
        public void ProjectTreeReload_ShouldPreserveRepositorySortOrder()
        {
            string repoRoot = FindRepoRoot();
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.NavigationView.cs"))
                .Replace("\r\n", "\n");

            int methodStart = code.IndexOf("private async Task LoadTextProjectsToTreeAsync", StringComparison.Ordinal);
            int methodEnd = code.IndexOf("private void LoadLyricsLibraryToTree", methodStart, StringComparison.Ordinal);
            string method = code.Substring(methodStart, methodEnd - methodStart);

            Assert.Contains("foreach (var project in textProjects)", method);
            Assert.DoesNotContain("textProjects.OrderBy(p => p.Id)", method);
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ImageColorChanger.csproj")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        }
    }
}
