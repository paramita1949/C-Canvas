using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ImageColorChanger.Utils
{
    /// <summary>Debug默认开启，Release仅CANVAS_SCORE_TRACE=1启用；0强制关闭。不记录谱文、账户或异常消息。</summary>
    internal sealed class StructuredScorePerfTrace : IDisposable
    {
        private static readonly bool Enabled = IsEnabled();
        private static bool IsEnabled()
        {
            string setting = Environment.GetEnvironmentVariable("CANVAS_SCORE_TRACE");
            if (setting == "0") return false;
#if DEBUG
            return true;
#else
            return setting == "1";
#endif
        }
        private static readonly object Gate = new();
        private static long _nextId;
        internal static string LogFilePath => Enabled ? Path.Combine(Path.GetTempPath(), "CanvasCast-diagnostics",
            $"structured-score-{Environment.ProcessId}.log") : string.Empty;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private readonly long _id = Interlocked.Increment(ref _nextId);
        private readonly string _operation;
        private double _previousMs;

        private StructuredScorePerfTrace(string operation) { _operation = operation; Mark("begin"); }
        internal static StructuredScorePerfTrace Start(string operation) => Enabled ? new(operation) : null;
        internal void Mark(string phase)
        {
            double elapsed = _watch.Elapsed.TotalMilliseconds;
            Write($"id={_id} op={_operation} phase={phase} elapsed_ms={elapsed:F3} step_ms={elapsed - _previousMs:F3}");
            _previousMs = elapsed;
        }
        internal static void Event(string phase) { if (Enabled) Write($"id=0 op=display phase={phase}"); }
        internal static void SourceAssigned(System.Windows.Controls.Image image, System.Windows.Media.Imaging.BitmapSource photo)
        {
            if (!Enabled) return;
            Event("source-assigned");
            try
            {
                // 仅标记WPF调度轮次，不把它冒称为GPU呈现/屏幕像素完成。
                image.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                    Event(ReferenceEquals(image.Source, photo) ? "dispatcher-render-turn" : "source-superseded")));
            }
            catch { }
        }
        internal static void Error(string phase, Exception ex)
        {
            if (!Enabled || ex == null) return;
            Write($"id=0 op=error phase={phase} type={ex.GetType().Name} hresult={ex.HResult:X8}");
            // 不附源文件路径/行号、异常消息；仍保留实际托管调用链用于定位逸出边界。
            Write("error-stack=" + new StackTrace(ex, false).ToString());
            if (ex.InnerException != null) Error("inner-" + phase, ex.InnerException);
        }
        private static void Write(string message)
        {
            // 诊断本身失败不能干扰显示；默认关闭时不创建目录、不做文件IO。
            try
            {
                lock (Gate)
                {
                    string path = LogFilePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string line = $"{DateTime.UtcNow:O} pid={Environment.ProcessId} build_mvid={typeof(StructuredScorePerfTrace).Module.ModuleVersionId} thread={Environment.CurrentManagedThreadId} {message}{Environment.NewLine}";
                    Debug.WriteLine(line);
                    File.AppendAllText(path, line);
                }
            }
            catch { }
        }
        public void Dispose() => Mark("end");
    }
}
