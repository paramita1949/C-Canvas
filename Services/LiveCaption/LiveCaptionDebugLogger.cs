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

            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            Debug.WriteLine($"[LiveCaption][{ts}] {message}");
        }

        private static bool ShouldLogMessage(string message)
        {
            // 仅保留识别文本 + 经文命中/入槽结果，屏蔽发送帧/重连/诊断噪声。
            return message.Contains("RealtimeVerse: ASR文本", StringComparison.Ordinal)
                || message.Contains("RealtimeVerse: ✅", StringComparison.Ordinal)
                || message.Contains("✅ 插入历史", StringComparison.Ordinal)
                || message.Contains("[RL] ✅ triggered", StringComparison.Ordinal)
                || message.Contains("short-speech success: recognized=", StringComparison.Ordinal)
                || message.Contains("Transcribe result:", StringComparison.Ordinal);
        }
    }
}
