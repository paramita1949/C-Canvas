using System.Diagnostics;
using System;

namespace ImageColorChanger.Services.LiveCaption
{
    internal static class LiveCaptionDebugLogger
    {
        internal static readonly bool Enabled =
#if DEBUG
            !string.Equals(Environment.GetEnvironmentVariable("CANVAS_LIVECAPTION_DEBUG"), "0", StringComparison.Ordinal);
#else
            false;
#endif

        public static void Log(string message)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (!ShouldLogMessage(message))
            {
                return;
            }

            Debug.WriteLine($"[LiveCaption] {message}");
        }

        private static bool ShouldLogMessage(string message)
        {
            // 仅保留识别与经文命中核心日志，其他生命周期/布局/性能日志全部静默。
            return message.Contains("RealtimeVerse: ASR文本", StringComparison.Ordinal)
                || message.Contains("RealtimeVerse: ✅ 直接解析", StringComparison.Ordinal)
                || message.Contains("RealtimeVerse: ✅ 内容反查", StringComparison.Ordinal)
                || message.Contains("✅ 插入历史", StringComparison.Ordinal)
                || message.Contains("[RL] ✅ triggered", StringComparison.Ordinal)
                || message.Contains("Direct parse succeeded.", StringComparison.Ordinal);
        }
    }
}
