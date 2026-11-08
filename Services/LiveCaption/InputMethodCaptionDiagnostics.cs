using System;
using System.IO;
using System.Text;

namespace ImageColorChanger.Services.LiveCaption
{
    /// <summary>输入法兼容性日志，只接收事件和计数，不记录字幕正文。</summary>
    internal static class InputMethodCaptionDiagnostics
    {
        private static readonly object Gate = new();
        internal static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CanvasCast", "logs", "inputmethod-caption.log");
        internal const long MaxLogBytes = 1024 * 1024;

        public static void Log(string metadata) => Write(LogPath, metadata);

        internal static void Write(string path, string metadata)
        {
            // 日志失败不可打断输入；最多保留当前和上一份，各约 1 MiB。
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (File.Exists(path) && new FileInfo(path).Length >= MaxLogBytes)
                        File.Move(path, path + ".1", overwrite: true);
                    string singleLine = (metadata ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
                    if (singleLine.Length > 512) singleLine = singleLine.Substring(0, 512);
                    File.AppendAllText(path, $"{DateTimeOffset.Now:O} {singleLine}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // 只写辅助日志，不影响字幕链路。
            }
        }
    }
}
