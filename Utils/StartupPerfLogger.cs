using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 启动性能日志：写入程序目录下 debug 文件夹，便于跨机器对比启动耗时。
    /// </summary>
    public static class StartupPerfLogger
    {
        private static readonly object SyncRoot = new();
        private static readonly Stopwatch StartupStopwatch = Stopwatch.StartNew();
        private static string _logFilePath;
        private static int _sequence;
        private static bool _sessionStarted;

        public static string LogFilePath => _logFilePath;

        public static void Initialize(string phase, string detail = null)
        {
            Write("INIT", phase, detail);
        }

        public static void Mark(string phase, string detail = null)
        {
            Write("MARK", phase, detail);
        }

        public static void Error(string phase, Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            Write("ERROR", phase, $"{exception.GetType().Name}: {exception.Message}");
        }

        private static void Write(string level, string phase, string detail)
        {
            try
            {
                lock (SyncRoot)
                {
                    EnsureLogFile();
                    if (string.IsNullOrWhiteSpace(_logFilePath))
                    {
                        return;
                    }

                    if (!_sessionStarted)
                    {
                        _sessionStarted = true;
                        AppendLine($"# Session started at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        AppendLine($"# ProcessId={Environment.ProcessId}; Machine={Environment.MachineName}; OS={Environment.OSVersion}");
                        AppendLine("# Timestamp | ElapsedMs | Seq | Level | Phase | Detail");
                    }

                    int seq = ++_sequence;
                    string line =
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {StartupStopwatch.ElapsedMilliseconds,8} | {seq,4} | {level,-5} | {phase ?? "<null>"} | {detail ?? string.Empty}";
                    AppendLine(line);
                }
            }
            catch
            {
                // 记录失败时不能影响主流程
            }
        }

        private static void EnsureLogFile()
        {
            if (!string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            try
            {
                string baseDir = AppContext.BaseDirectory;
                string debugDir = Path.Combine(baseDir, "debug");
                Directory.CreateDirectory(debugDir);
                string fileName = $"startup_{DateTime.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}.log";
                _logFilePath = Path.Combine(debugDir, fileName);
            }
            catch
            {
                _logFilePath = null;
            }
        }

        private static void AppendLine(string text)
        {
            File.AppendAllText(_logFilePath, text + Environment.NewLine, Encoding.UTF8);
        }
    }
}
