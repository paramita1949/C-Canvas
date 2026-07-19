using System;
using System.IO;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class BibleViewSwitchTitleStateTests
    {
        [Fact]
        public void BibleEntry_RestoresCurrentTitleInsteadOfClearingIt()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.Bible.Core.cs"));
            string method = Slice(
                source,
                "private async void BtnShowBible_Click(object sender, RoutedEventArgs e)",
                "#endregion");

            Assert.Equal(2, CountOccurrences(method, "RestoreBibleTitleDisplayAfterViewSwitch();"));
            Assert.DoesNotContain("BibleChapterTitle.Text = string.Empty;", method, StringComparison.Ordinal);
        }

        [Fact]
        public void BibleSettings_UsesCurrentViewStateInsteadOfForcingAnEmptyTitleVisible()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.Bible.Settings.cs"));
            string method = Slice(
                source,
                "private void ApplyBibleSettings()",
                "private void ApplyBiblePinyinPreviewThemeResources()");

            Assert.Contains("ShouldShowCurrentBibleTitle()", method, StringComparison.Ordinal);
            Assert.DoesNotContain("ApplyBibleTitleDisplayMode(true);", method, StringComparison.Ordinal);
        }

        [Fact]
        public void SlideSwitch_ClearsProjectionWithoutDiscardingCurrentBibleState()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.NavigationView.cs"));
            string method = Slice(
                source,
                "private void ClearProjectedBibleContentForSlideSwitch()",
                "private void UpdateViewModeButtons()");

            Assert.Contains("_projectionManager?.ClearProjectionDisplay();", method, StringComparison.Ordinal);
            Assert.Contains(
                "_projectionManager?.SetBibleTitle(BibleChapterTitle?.Text ?? string.Empty, false);",
                method,
                StringComparison.Ordinal);
            Assert.Contains("ApplyBibleTitleDisplayMode(false);", method, StringComparison.Ordinal);
            Assert.DoesNotContain("BibleChapterTitle.Text =", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_mergedVerses?.Clear();", method, StringComparison.Ordinal);
        }

        private static string Slice(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"未找到起始标记：{startMarker}");
            int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            Assert.True(end > start, $"未找到结束标记：{endMarker}");
            return source[start..end];
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }

            return count;
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
