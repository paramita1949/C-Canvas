using System;
using System.IO;
using System.Text.RegularExpressions;
using ImageColorChanger.UI.Modules;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class CompositeCountdownDurationShortcutTests
    {
        [Theory]
        [InlineData(12.34, 12.3)]
        [InlineData(12.35, 12.4)]
        [InlineData(0.06, 0.1)]
        public void TryNormalizeElapsedDuration_RoundsDisplayedElapsedTimeToOneDecimal(double elapsedSeconds, double expected)
        {
            bool canSave = CompositeCountdownDurationShortcut.TryNormalizeElapsedDuration(elapsedSeconds, out double duration);

            Assert.True(canSave);
            Assert.Equal(expected, duration);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void TryNormalizeElapsedDuration_RejectsUnavailableElapsedTime(double elapsedSeconds)
        {
            bool canSave = CompositeCountdownDurationShortcut.TryNormalizeElapsedDuration(elapsedSeconds, out double duration);

            Assert.False(canSave);
            Assert.Equal(0, duration);
        }

        [Theory]
        [InlineData(90, 120, 95)]
        [InlineData(90, -120, 85)]
        [InlineData(3, -120, 1)]
        [InlineData(12.5, 120, 17.5)]
        [InlineData(double.NaN, 120, 6)]
        public void AdjustDurationByWheelDelta_ChangesByFiveSecondsAndClampsToOne(double currentDuration, int wheelDelta, double expected)
        {
            double adjusted = CompositeCountdownDurationShortcut.AdjustDurationByWheelDelta(currentDuration, wheelDelta);

            Assert.Equal(expected, adjusted);
        }

        [Fact]
        public void CountdownStatusBar_Should_DoubleClickApplyAndRightClickShowActionMenu()
        {
            string repoRoot = FindRepoRoot();
            string xaml = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.xaml"));
            string code = File.ReadAllText(Path.Combine(repoRoot, "UI", "MainWindow.Keyframe.Events.cs"));
            string quickEditCode = Regex.Match(
                code,
                "private async Task OpenCompositeDurationQuickEditDialogAsync\\([\\s\\S]*?private static bool TryParseCompositeDuration")
                .Value;

            string countdownBlock = Regex.Match(
                xaml,
                "<Border x:Name=\"CountdownBorder\"[\\s\\S]*?</Border>")
                .Value;

            Assert.Contains("MouseLeftButtonDown=\"CountdownBorder_MouseLeftButtonDown\"", countdownBlock);
            Assert.Contains("MouseRightButtonUp=\"CountdownBorder_MouseRightButtonUp\"", countdownBlock);
            Assert.Contains("ToolTip=\"双击：应用已播放时间 | 右键：应用/修改\"", countdownBlock);

            Assert.Contains("private async void CountdownBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)", code);
            Assert.Contains("private void CountdownBorder_MouseRightButtonUp(object sender, MouseButtonEventArgs e)", code);
            Assert.Contains("SaveCompositeDurationFromCountdownAsync()", code);
            Assert.Contains("ShowCountdownDurationShortcutMenu(sender as UIElement)", code);
            Assert.Contains("Header = \"应用\"", code);
            Assert.Contains("Header = \"修改\"", code);
            Assert.Contains("await OpenCompositeDurationQuickEditDialogAsync();", code);
            Assert.Contains("SelectAll()", code);
            Assert.Contains("if (await SetCompositeTotalDuration(duration))", code);
            Assert.Contains("ShowToast($\"已应用 {durationText} 秒\"", code);
            Assert.Contains("CompositeCountdownDurationShortcut.TryNormalizeElapsedDuration", code);
            Assert.Contains("CompositeCountdownDurationShortcut.AdjustDurationByWheelDelta", quickEditCode);
            Assert.Contains("Title = \"修改时间\"", quickEditCode);
            Assert.Contains("Width = 340", quickEditCode);
            Assert.Contains("Height = 260", quickEditCode);
            Assert.Contains("WindowStyle = WindowStyle.None", quickEditCode);
            Assert.Contains("AllowsTransparency = true", quickEditCode);
            Assert.Contains("PreviewMouseWheel", quickEditCode);
            Assert.Contains("滚轮 ±5 秒，回车保存", quickEditCode);
            Assert.Contains("Content = \"保存\"", quickEditCode);
            Assert.DoesNotContain("Content = \"确定\"", quickEditCode);
            Assert.Contains("CreateCompositeDurationQuickEditButtonTemplate()", quickEditCode);
            Assert.Contains("Border.CornerRadiusProperty", quickEditCode);
            Assert.Contains("new CornerRadius(8)", quickEditCode);
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
