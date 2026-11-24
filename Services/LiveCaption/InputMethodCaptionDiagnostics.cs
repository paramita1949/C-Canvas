using System;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.LiveCaption
{
    /// <summary>输入法兼容性日志，只接收事件和计数，不记录字幕正文。</summary>
    internal static class InputMethodCaptionDiagnostics
    {
        private static readonly object Gate = new();
        private static readonly Channel<(DateTimeOffset At, string Metadata)> Pending = Channel.CreateBounded<(DateTimeOffset, string)>(
            new BoundedChannelOptions(128) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
        internal static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CanvasCast", "logs", "inputmethod-caption.log");
        internal const long MaxLogBytes = 1024 * 1024;

        static InputMethodCaptionDiagnostics()
        {
            _ = Task.Run(async () =>
            {
                await foreach (var entry in Pending.Reader.ReadAllAsync().ConfigureAwait(false))
                    Write(LogPath, entry.Metadata, entry.At);
            });
        }

        public static void Log(string metadata)
        {
            string bounded = metadata ?? string.Empty;
            if (bounded.Length > 512) bounded = bounded.Substring(0, 512);
            Pending.Writer.TryWrite((DateTimeOffset.Now, bounded));
        }

        internal static void Write(string path, string metadata)
            => Write(path, metadata, DateTimeOffset.Now);

        private static void Write(string path, string metadata, DateTimeOffset at)
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
                    File.AppendAllText(path, $"{at:O} {singleLine}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // 只写辅助日志，不影响字幕链路。
            }
        }
    }
}
