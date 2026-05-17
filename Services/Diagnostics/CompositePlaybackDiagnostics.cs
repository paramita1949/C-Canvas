using System;
using System.Globalization;
using System.IO;

namespace ImageColorChanger.Services.Diagnostics
{
    internal static class CompositePlaybackDiagnostics
    {
        private const string LogFileName = "composite-playback-diagnostics.log";

        public static string GetDefaultLogFilePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "log", LogFileName);
        }

        public static void AppendLine(string logPath, string line)
        {
            if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never affect playback.
            }
        }

        public static void Log(string line)
        {
            AppendLine(GetDefaultLogFilePath(), line);
        }

        public static string BuildSessionStartLine(
            Guid sessionId,
            string machineName,
            string userName,
            string osVersion,
            int imageId,
            bool isProjectionActive,
            int screenSelectorIndex,
            double mainScrollTop,
            double mainScrollableHeight,
            double projectionScrollTop,
            double projectionScrollableHeight,
            string monitorSummary)
        {
            return Format(
                sessionId,
                "session_start",
                $"machine={Safe(machineName)} user={Safe(userName)} os=\"{Escape(osVersion)}\" " +
                $"imageId={imageId} projectionActive={isProjectionActive} screenSelectorIndex={screenSelectorIndex} " +
                $"mainOffset={F2(mainScrollTop)} mainScrollable={F2(mainScrollableHeight)} " +
                $"projectionOffset={F2(projectionScrollTop)} projectionScrollable={F2(projectionScrollableHeight)} " +
                $"monitors=\"{Escape(monitorSummary)}\"");
        }

        public static string BuildScrollRequestLine(
            Guid sessionId,
            double startPosition,
            double endPosition,
            double durationSeconds,
            double speedRatio,
            double mainScrollTop,
            double projectionScrollTop)
        {
            return Format(
                sessionId,
                "scroll_request",
                $"start={F2(startPosition)} end={F2(endPosition)} durationSec={F2(durationSeconds)} " +
                $"speed={F2(speedRatio)} mainOffset={F2(mainScrollTop)} projectionOffset={F2(projectionScrollTop)}");
        }

        public static string BuildFrameSampleLine(
            Guid sessionId,
            long elapsedMs,
            int frameCount,
            int syncCount,
            double largestFrameGapMs,
            double mainFps,
            double projectionFps,
            double mainScrollTop,
            double mainScrollableHeight,
            double projectionScrollTop,
            double projectionScrollableHeight)
        {
            return Format(
                sessionId,
                "frame_sample",
                $"elapsedMs={elapsedMs} frameCount={frameCount} syncCount={syncCount} " +
                $"largestFrameGapMs={F2(largestFrameGapMs)} mainFps={F2(mainFps)} projectionFps={F2(projectionFps)} " +
                $"mainOffset={F2(mainScrollTop)} mainScrollable={F2(mainScrollableHeight)} " +
                $"projectionOffset={F2(projectionScrollTop)} projectionScrollable={F2(projectionScrollableHeight)}");
        }

        public static string BuildSessionEndLine(
            Guid sessionId,
            string reason,
            long elapsedMs,
            int frameCount,
            int syncCount,
            double largestFrameGapMs,
            double mainScrollTop,
            double projectionScrollTop)
        {
            return Format(
                sessionId,
                "session_end",
                $"reason=\"{Escape(reason)}\" elapsedMs={elapsedMs} frameCount={frameCount} syncCount={syncCount} " +
                $"largestFrameGapMs={F2(largestFrameGapMs)} mainOffset={F2(mainScrollTop)} projectionOffset={F2(projectionScrollTop)}");
        }

        public static string BuildEventLine(Guid sessionId, string eventName, string details)
        {
            return Format(sessionId, eventName, details ?? string.Empty);
        }

        private static string Format(Guid sessionId, string eventName, string details)
        {
            return $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] event={eventName} session={sessionId:D} {details}".TrimEnd();
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : Escape(value).Replace(" ", "_");
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\"", "'");
        }

        private static string F2(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
