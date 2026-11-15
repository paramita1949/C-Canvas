using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;

namespace ImageColorChanger.Services.Diagnostics
{
    /// <summary>投影会话独立 TXT；立即落盘，诊断错误不得影响正常投影。</summary>
    public sealed class ProjectionDiagnosticLog
    {
        private const long MaxBytes = 2 * 1024 * 1024;
        private const int MaxDetailChars = 32768;
        private readonly object _gate = new object();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        public string FilePath { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public string SessionId { get; } = Guid.NewGuid().ToString("N");

        public static string DescribeException(Exception exception) => exception == null ? "unknown"
            : exception.GetType().FullName + "; HRESULT=0x" + exception.HResult.ToString("X8", CultureInfo.InvariantCulture);

        public ProjectionDiagnosticLog(IEnumerable<string> directories, string version)
        {
            foreach (string directory in directories ?? Array.Empty<string>())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(directory)) continue;
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, $"投影诊断-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{SessionId}.txt");
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, Utf8))
                    {
                        writer.WriteLine("咏慕投影诊断（只读采样，不调整显示设置，不采集桌面/投影内容）");
                        writer.WriteLine($"SchemaVersion=1\nSessionId={SessionId}\nAppVersion={version}\nOS={Environment.OSVersion}\nProcess64Bit={Environment.Is64BitProcess}");
                        if (LastError.Length > 0) writer.WriteLine("PathFallback=" + LastError);
                    }
                    FilePath = path;
                    return;
                }
                catch (Exception ex)
                {
                    // 不记录用户名/绝对失败路径；保留异常类别用于排查权限/目录问题。
                    LastError = ex.GetType().Name + ": diagnostic directory unavailable";
                }
            }
            if (LastError.Length == 0) LastError = "No diagnostic directory available";
        }

        public bool Append(string phase, string detail)
        {
            lock (_gate)
            {
                if (FilePath == null) return false;
                try
                {
                    detail ??= string.Empty;
                    if (detail.Length > MaxDetailChars)
                        detail = detail.Substring(0, MaxDetailChars) + "\n[TRUNCATED: entry size limit]";
                    string entry = $"\n===== Phase={phase}; UTC={DateTime.UtcNow:O}; Local={DateTimeOffset.Now:O} =====\n{detail}\n";
                    using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Write, FileShare.Read);
                    if (stream.Length + Utf8.GetByteCount(entry) > MaxBytes)
                    {
                        LastError = "Diagnostic capacity reached (2 MiB); existing evidence retained";
                        return false;
                    }
                    stream.Seek(0, SeekOrigin.End);
                    using var writer = new StreamWriter(stream, Utf8);
                    writer.Write(entry);
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.GetType().Name + ": diagnostic append failed";
                    return false;
                }
            }
        }
    }
}
