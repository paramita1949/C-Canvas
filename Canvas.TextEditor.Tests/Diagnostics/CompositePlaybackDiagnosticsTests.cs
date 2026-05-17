using System;
using System.IO;
using ImageColorChanger.Services.Diagnostics;

namespace Canvas.TextEditor.Tests.Diagnostics
{
    public sealed class CompositePlaybackDiagnosticsTests : IDisposable
    {
        private readonly string _tempDir;

        public CompositePlaybackDiagnosticsTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "canvas-composite-diag-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [Fact]
        public void AppendLine_CreatesLogFileAndWritesMessage()
        {
            string logPath = Path.Combine(_tempDir, "composite-playback-diagnostics.log");

            CompositePlaybackDiagnostics.AppendLine(logPath, "test diagnostic line");

            Assert.True(File.Exists(logPath));
            string content = File.ReadAllText(logPath);
            Assert.Contains("test diagnostic line", content);
        }

        [Fact]
        public void GetDefaultLogFilePath_UsesApplicationDirectoryLogFolder()
        {
            string expectedDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "log"));

            string logPath = CompositePlaybackDiagnostics.GetDefaultLogFilePath();

            Assert.Equal(expectedDirectory, Path.GetFullPath(Path.GetDirectoryName(logPath)!));
            Assert.Equal("composite-playback-diagnostics.log", Path.GetFileName(logPath));
        }

        [Fact]
        public void BuildSessionStartLine_IncludesMachineAndPlaybackContext()
        {
            var sessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

            string line = CompositePlaybackDiagnostics.BuildSessionStartLine(
                sessionId,
                machineName: "PC-A",
                userName: "operator",
                osVersion: "Windows Test",
                imageId: 42,
                isProjectionActive: true,
                screenSelectorIndex: 1,
                mainScrollTop: 12.5,
                mainScrollableHeight: 300.25,
                projectionScrollTop: 10,
                projectionScrollableHeight: 280,
                monitorSummary: "screen0=main;screen1=projection");

            Assert.Contains("event=session_start", line);
            Assert.Contains("session=11111111-2222-3333-4444-555555555555", line);
            Assert.Contains("machine=PC-A", line);
            Assert.Contains("user=operator", line);
            Assert.Contains("imageId=42", line);
            Assert.Contains("projectionActive=True", line);
            Assert.Contains("screenSelectorIndex=1", line);
            Assert.Contains("mainOffset=12.50", line);
            Assert.Contains("projectionOffset=10.00", line);
            Assert.Contains("monitors=\"screen0=main;screen1=projection\"", line);
        }

        [Fact]
        public void BuildFrameSampleLine_IncludesFrameCountersAndLargestGap()
        {
            var sessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

            string line = CompositePlaybackDiagnostics.BuildFrameSampleLine(
                sessionId,
                elapsedMs: 1500,
                frameCount: 120,
                syncCount: 118,
                largestFrameGapMs: 35.75,
                mainFps: 119.5,
                projectionFps: 117.25,
                mainScrollTop: 500,
                mainScrollableHeight: 1200,
                projectionScrollTop: 460,
                projectionScrollableHeight: 1100);

            Assert.Contains("event=frame_sample", line);
            Assert.Contains("session=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", line);
            Assert.Contains("elapsedMs=1500", line);
            Assert.Contains("frameCount=120", line);
            Assert.Contains("syncCount=118", line);
            Assert.Contains("largestFrameGapMs=35.75", line);
            Assert.Contains("mainFps=119.50", line);
            Assert.Contains("projectionFps=117.25", line);
            Assert.Contains("mainOffset=500.00", line);
            Assert.Contains("projectionOffset=460.00", line);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
    }
}
