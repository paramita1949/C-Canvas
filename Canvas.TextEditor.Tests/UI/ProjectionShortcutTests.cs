using System;
using System.IO;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class ProjectionShortcutTests
    {
        [Fact]
        public void F9_IsRoutedToProjectionOpenAction()
        {
            string shortcutManager = ReadRepoFile(Path.Combine("Utils", "KeyboardShortcutManager.cs"));
            string actionHandler = ReadRepoFile(Path.Combine("Utils", "ShortcutActionHandler.cs"));

            Assert.Contains("case Key.F9:", shortcutManager, StringComparison.Ordinal);
            Assert.Contains("_actionHandler.HandleF9Key()", shortcutManager, StringComparison.Ordinal);
            Assert.Contains("public bool HandleF9Key()", actionHandler, StringComparison.Ordinal);
            Assert.Contains("_mainWindow.TryOpenProjectionByHotkey()", actionHandler, StringComparison.Ordinal);
        }

        [Fact]
        public void F9_OpensOnlyWhenProjectionIsInactive()
        {
            string shortcutSupport = ReadRepoFile(Path.Combine("UI", "MainWindow.ShortcutSupport.cs"));

            Assert.Contains("public bool TryOpenProjectionByHotkey()", shortcutSupport, StringComparison.Ordinal);
            Assert.Contains("if (_projectionManager.IsProjectionActive)", shortcutSupport, StringComparison.Ordinal);
            Assert.Contains("BtnProjection_Click(null, null);", shortcutSupport, StringComparison.Ordinal);
        }

        private static string ReadRepoFile(string relativePath)
        {
            return File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));
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
