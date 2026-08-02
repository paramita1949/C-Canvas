using System;
using System.IO;
using Xunit;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class BibleVerseSingleClickSelectionTests
    {
        [Fact]
        public void EndVerseList_HandlesSecondClickForSameVerseSelection()
        {
            string root = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.xaml"));
            string code = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.Bible.Core.cs"));

            Assert.Contains("x:Name=\"BibleEndVerse\"", xaml, StringComparison.Ordinal);
            Assert.Contains("MouseLeftButtonUp=\"BibleEndVerse_MouseLeftButtonUp\"", xaml, StringComparison.Ordinal);
            Assert.Contains("private async void BibleEndVerse_MouseLeftButtonUp", code, StringComparison.Ordinal);
            Assert.Contains("startVerse != endVerse", code, StringComparison.Ordinal);
            Assert.Contains("LoadVerseRangeAsync(bookId, chapter, startVerse, startVerse)", code, StringComparison.Ordinal);
        }

        private static string FindRepoRoot()
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "ImageColorChanger.csproj")))
                    return directory;

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException("Canvas repository root was not found.");
        }
    }
}
