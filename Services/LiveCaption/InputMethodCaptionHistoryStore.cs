using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.LiveCaption
{
    /// <summary>分段保存已移出编辑区的文字；所有文件操作在后台执行，控件只提交已保存的段。</summary>
    internal sealed class InputMethodCaptionHistoryStore
    {
        internal sealed class StagedPrefix
        {
            public string SessionDirectory { get; init; }
            public int Sequence { get; init; }
            public string PendingPath { get; init; }
            public string SavedPath { get; init; }
            public bool Accepted { get; set; }
        }

        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private string _sessionDirectory;
        private int _acceptedChunks;

        public InputMethodCaptionHistoryStore(string rootDirectory = null)
        {
            RootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CanvasCast", "caption-history"));
            Reset();
        }

        public string RootDirectory { get; }

        public void Reset()
        {
            _sessionDirectory = Path.Combine(RootDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            _acceptedChunks = 0;
        }

        public Task<StagedPrefix> StagePrefixAsync(string prefix)
        {
            string session = _sessionDirectory;
            int sequence = _acceptedChunks + 1;
            string path = Path.Combine(session, $"{sequence:D6}.txt");
            var staged = new StagedPrefix
            {
                SessionDirectory = session,
                Sequence = sequence,
                PendingPath = path + ".pending",
                SavedPath = path
            };
            return Task.Run(async () =>
            {
                Directory.CreateDirectory(session);
                await File.WriteAllTextAsync(staged.PendingPath, prefix, Utf8).ConfigureAwait(false);
                return staged;
            });
        }

        public Task CommitAsync(StagedPrefix staged) => Task.Run(() =>
            File.Move(staged.PendingPath, staged.SavedPath));

        public void Accept(StagedPrefix staged)
        {
            if (staged.SessionDirectory != _sessionDirectory || staged.Sequence != _acceptedChunks + 1)
                throw new InvalidOperationException("字幕记录会话已经改变");
            staged.Accepted = true;
            _acceptedChunks++;
        }

        public Task DiscardAsync(StagedPrefix staged) => Task.Run(() =>
        {
            if (staged == null || staged.Accepted) return;
            if (File.Exists(staged.PendingPath)) File.Delete(staged.PendingPath);
            if (File.Exists(staged.SavedPath)) File.Delete(staged.SavedPath);
        });

        public Task ExportAsync(string destination, string currentText)
            => ExportAsync(destination, currentText, createDirectory: false);

        public Task SaveFinalAsync(string currentText)
            => string.IsNullOrEmpty(currentText) && _acceptedChunks == 0 ? Task.CompletedTask
                : ExportAsync(Path.Combine(_sessionDirectory, "完整字幕.txt"), currentText, createDirectory: true);

        private Task ExportAsync(string destination, string currentText, bool createDirectory)
        {
            // 在调用线程捕获同一时刻的段数和编辑区；后续收缩不会造成重复或遗漏。
            string session = _sessionDirectory;
            int count = _acceptedChunks;
            return Task.Run(async () =>
            {
                if (createDirectory) Directory.CreateDirectory(Path.GetDirectoryName(destination));
                await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write,
                    FileShare.Read, 65536, useAsync: true);
                await using var writer = new StreamWriter(output, Utf8);
                char[] buffer = new char[8192];
                for (int i = 1; i <= count; i++)
                {
                    using var reader = new StreamReader(Path.Combine(session, $"{i:D6}.txt"), Utf8);
                    int read;
                    while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
                        await writer.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                }
                await writer.WriteAsync(currentText ?? string.Empty).ConfigureAwait(false);
            });
        }
    }
}
