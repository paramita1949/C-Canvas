using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class CompositeSpeedButtonInteractionTests
    {
        [Fact]
        public void CompositeSpeedButton_Should_SwallowMouseClickBeforeButtonDefaultClick()
        {
            string xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.xaml"));
            string buttonBlock = Regex.Match(
                xaml,
                "<Button x:Name=\"BtnCompositeSpeed\"[\\s\\S]*?</Button>")
                .Value;

            Assert.Contains("MouseEnter=\"BtnCompositeSpeed_MouseEnter\"", buttonBlock);
            Assert.Contains("PreviewMouseLeftButtonDown=\"BtnCompositeSpeed_PreviewMouseLeftButtonDown\"", buttonBlock);
            Assert.Contains("Click=\"BtnCompositeSpeed_Click\"", buttonBlock);
        }

        [Fact]
        public void CompositeSpeedButton_ClickHandlers_Should_OnlyMarkEventHandled()
        {
            string code = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.Keyframe.Events.cs"));

            Assert.Matches(
                "private void BtnCompositeSpeed_PreviewMouseLeftButtonDown\\(object sender, System\\.Windows\\.Input\\.MouseButtonEventArgs e\\)[\\s\\S]*?e\\.Handled = true;",
                code);
            Assert.Matches(
                "private void BtnCompositeSpeed_Click\\(object sender, RoutedEventArgs e\\)[\\s\\S]*?e\\.Handled = true;",
                code);
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
