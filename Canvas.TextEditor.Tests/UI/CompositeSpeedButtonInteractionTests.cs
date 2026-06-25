using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class CompositeSpeedButtonInteractionTests
    {
        [Fact]
        public void CompositeSpeedOptions_Should_RenderAsTwoTransparentRowsWithoutLargeIndicator()
        {
            string xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.xaml"));
            string optionsBlock = Regex.Match(
                xaml,
                "<UniformGrid x:Name=\"CompositeSpeedOptionsPanel\"[\\s\\S]*?</UniformGrid>")
                .Value;

            Assert.DoesNotContain("x:Name=\"BtnCompositeSpeed\"", xaml);
            Assert.Contains("Rows=\"2\"", optionsBlock);
            Assert.Contains("Columns=\"4\"", optionsBlock);
            Assert.Contains("Margin=\"0,8,0,0\"", optionsBlock);
            Assert.Contains("Opacity\" Value=\"0.82\"", optionsBlock);
            Assert.DoesNotContain("<WrapPanel", optionsBlock);
            Assert.Equal(8, Regex.Matches(optionsBlock, "Click=\"CompositeSpeedOption_Click\"").Count);

            string[] expectedSpeedTags = { "0.50", "0.75", "1.00", "1.25", "1.50", "2.00", "2.50", "3.00" };
            foreach (string speedTag in expectedSpeedTags)
            {
                Assert.Contains($"Tag=\"{speedTag}\"", optionsBlock);
            }

            Assert.DoesNotContain("Tag=\"1.10\"", optionsBlock);
        }

        [Fact]
        public void CompositeSpeedOptions_Should_NotUseHoverContextMenu()
        {
            string xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.xaml"));
            string code = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.Keyframe.Events.cs"));

            Assert.Contains("x:Name=\"CompositeSpeedOptionsPanel\"", xaml);
            Assert.Contains("Click=\"CompositeSpeedOption_Click\"", xaml);
            Assert.DoesNotContain("BtnCompositeSpeed", xaml);
            Assert.DoesNotContain("BtnCompositeSpeed_MouseEnter", xaml);
            Assert.DoesNotContain("BtnCompositeSpeed_PreviewMouseDown", xaml);
            Assert.DoesNotContain("BtnCompositeSpeed_MouseLeave", xaml);

            Assert.DoesNotContain("BtnCompositeSpeed", code);
            Assert.DoesNotContain("_compositeSpeedMenu", code);
            Assert.DoesNotContain("ShowCompositeSpeedMenu", code);
            Assert.DoesNotContain("BuildCompositeSpeedMenu", code);
            Assert.DoesNotContain("RefreshCompositeSpeedMenuCheckedState", code);
            Assert.DoesNotContain("CompositeSpeedMenuAutoCloseTimer_Tick", code);
            Assert.Contains("private void CompositeSpeedOption_Click(object sender, RoutedEventArgs e)", code);
            Assert.Contains("UpdateCompositeSpeedOptionSelection(speed)", code);
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
