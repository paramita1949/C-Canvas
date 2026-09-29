using System;
using System.IO;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class AiAssistantPanelSessionLifecycleTests
    {
        [Fact]
        public void CloseButton_HidesPanelWithoutEndingActiveSession()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "AiAssistantPanelWindow.xaml.cs"));
            string method = Slice(
                source,
                "CloseButton_Click(object sender, RoutedEventArgs e)",
                "private async Task RequestEndSessionAsync()");

            Assert.Contains("Hide();", method, StringComparison.Ordinal);
            Assert.DoesNotContain("RequestEndSessionAsync", method, StringComparison.Ordinal);
            Assert.DoesNotContain("Close();", method, StringComparison.Ordinal);
        }

        [Fact]
        public void PanelInitialization_DoesNotEnableAsrBeforeSpeakerSelection()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            string method = Slice(
                source,
                "private void EnsureAiSermonPanel()",
                "private async Task FinalizeAiSermonSessionAsync()");

            Assert.DoesNotContain("SetAiSermonReceiveAsr(true);", method, StringComparison.Ordinal);
        }

        [Fact]
        public void PanelOpen_ResumesAsrOnlyWhenAnActiveSessionAlreadyExists()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            const string expected = "SetAiSermonReceiveAsr(_aiSermonCoordinator?.HasActiveSession == true);";

            Assert.Equal(2, CountOccurrences(source, expected));
        }

        [Fact]
        public void SpeakerSelection_EnablesAsrAfterSpeakerIsApplied()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            string method = Slice(
                source,
                "private async Task ApplyAiSpeakerAsync(string speaker)",
                "private async Task DeleteAiSpeakerAsync(string speaker)");

            int setSpeakerIndex = method.IndexOf("SetSpeakerAsync", StringComparison.Ordinal);
            int enableAsrIndex = method.IndexOf("SetAiSermonReceiveAsr(true);", StringComparison.Ordinal);

            Assert.True(setSpeakerIndex >= 0);
            Assert.True(enableAsrIndex > setSpeakerIndex);
        }

        [Fact]
        public void SpeakerSelection_WithoutPendingSlide_StartsLiveCaptionOnlyWhenItIsNotRunning()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            string method = Slice(
                source,
                "private async Task ApplyAiSpeakerAsync(string speaker)",
                "private async Task DeleteAiSpeakerAsync(string speaker)");

            Assert.Contains("bool hasPendingProject = _pendingAiSermonProjectRequest != null", method, StringComparison.Ordinal);
            Assert.Contains("if (!hasPendingProject", method, StringComparison.Ordinal);
            Assert.Contains("_liveCaptionEngine?.IsRunning != true", method, StringComparison.Ordinal);
            Assert.Contains("StartLiveCaption(_liveCaptionCurrentSource);", method, StringComparison.Ordinal);
        }

        [Fact]
        public void SpeakerSelection_ReportsLifecycleFailureToPanel()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            string method = Slice(
                source,
                "private async Task ApplyAiSpeakerAsync(string speaker)",
                "private async Task DeleteAiSpeakerAsync(string speaker)");

            Assert.Contains("catch (Exception ex)", method, StringComparison.Ordinal);
            Assert.Contains("AI操作失败：{ex.Message}", method, StringComparison.Ordinal);
        }

        [Fact]
        public void SlideAiEntryPoints_UseUnifiedQueueInsteadOfStartingProjectDirectly()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            string analyzeMethod = Slice(
                source,
                "private async Task AnalyzeTextProjectWithAiAsync(ProjectTreeItem item, bool startAsr)",
                "private async Task SetTextProjectAsAiSermonContextAsync(ProjectTreeItem item)");
            string contextMethod = Slice(
                source,
                "private async Task SetTextProjectAsAiSermonContextAsync(ProjectTreeItem item)",
                "private async Task QueueOrStartAiProjectAsync(");

            Assert.Contains("QueueOrStartAiProjectAsync", analyzeMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("StartProjectAsync", analyzeMethod, StringComparison.Ordinal);
            Assert.Contains("QueueOrStartAiProjectAsync", contextMethod, StringComparison.Ordinal);
            Assert.DoesNotContain("StartProjectAsync", contextMethod, StringComparison.Ordinal);
        }

        [Fact]
        public void QueuedSlideProject_StartsOnlyAfterSpeakerIsApplied()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            string method = Slice(
                source,
                "private async Task ApplyAiSpeakerAsync(string speaker)",
                "private async Task DeleteAiSpeakerAsync(string speaker)");

            int setSpeakerIndex = method.IndexOf("SetSpeakerAsync", StringComparison.Ordinal);
            int startPendingIndex = method.IndexOf("StartPendingAiProjectAfterSpeakerSelectionAsync", StringComparison.Ordinal);
            int enableAsrIndex = method.IndexOf("SetAiSermonReceiveAsr(true);", StringComparison.Ordinal);

            Assert.True(setSpeakerIndex >= 0);
            Assert.True(startPendingIndex > setSpeakerIndex);
            Assert.True(enableAsrIndex > startPendingIndex);
        }

        [Fact]
        public void QueueHelper_DefersSlideProjectWhenThereIsNoActiveSession()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            string method = Slice(
                source,
                "private async Task QueueOrStartAiProjectAsync(",
                "private async Task StartAiProjectRequestAsync(");

            Assert.Contains("if (!_aiSermonCoordinator.HasActiveSession)", method, StringComparison.Ordinal);
            Assert.Contains("_pendingAiSermonProjectRequest = request;", method, StringComparison.Ordinal);
            Assert.Contains("请选择传道人后开始AI解读", method, StringComparison.Ordinal);
        }

        [Fact]
        public void QueueHelper_ReportsSpeakerListStageInsteadOfPretendingToReadProjectContext()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            string method = Slice(
                source,
                "private async Task QueueOrStartAiProjectAsync(",
                "private async Task StartAiProjectRequestAsync(");

            Assert.Contains("正在加载传道人列表", method, StringComparison.Ordinal);
            Assert.Contains("传道人列表加载完成", method, StringComparison.Ordinal);
            Assert.DoesNotContain("正在读取项目上下文", method, StringComparison.Ordinal);
        }

        [Fact]
        public void SlideAiEntry_ReportsStagesAndSurfacesExceptions()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.AiSermon.cs"));
            string analyzeMethod = Slice(
                source,
                "private async Task AnalyzeTextProjectWithAiAsync(ProjectTreeItem item, bool startAsr)",
                "private async Task SetTextProjectAsAiSermonContextAsync(ProjectTreeItem item)");
            Assert.Contains("ReportAiProjectDiagnostic(\"已触发\")", analyzeMethod, StringComparison.Ordinal);
            Assert.Contains("catch (Exception ex)", analyzeMethod, StringComparison.Ordinal);
            Assert.Contains("ReportAiProjectDiagnostic($\"解读失败：{ex.Message}\")", analyzeMethod, StringComparison.Ordinal);
            Assert.Contains("AppendStatus(status)", source, StringComparison.Ordinal);
            Assert.Contains("ShowStatus(status)", source, StringComparison.Ordinal);
            Assert.Contains("Debug.WriteLine", source, StringComparison.Ordinal);
        }

        [Fact]
        public void SlideAiContextMenu_UsesObservedAsyncHandler()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.ProjectTree.ContextMenu.cs"));
            Assert.Contains("CreateAsyncIconMenuItem", source, StringComparison.Ordinal);
            Assert.Contains("await onClick()", source, StringComparison.Ordinal);
            Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
            Assert.Contains("System.Diagnostics.Debug.WriteLine", source, StringComparison.Ordinal);
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
