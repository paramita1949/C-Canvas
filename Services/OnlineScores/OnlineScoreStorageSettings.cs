using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImageColorChanger.Database;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Services.OnlineScores
{
    // 与现有界面设置共用数据库。目录身份来自路径，不从“YM”等显示名称推断。
    public sealed class OnlineScoreStorageSettings
    {
        private const string Key = "OnlineScores.Storage.v1";
        public const string FolderColor = "#2A9D8F";
        private readonly DatabaseManager _db;
        private State _state;
        private sealed class State
        {
            public string Directory { get; set; }
            public List<string> PreviousDirectories { get; set; } = new();
            public List<BookFolder> Books { get; set; } = new();
        }
        private sealed class BookFolder
        {
            public string Name { get; set; }
            public string Path { get; set; }
        }

        public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "YM");
        public string DirectoryPath => Normalize(_state.Directory);
        public string DisplayName => new DirectoryInfo(DirectoryPath).Name;
        public IReadOnlyList<string> CacheDirectories => new[] { DirectoryPath }
            .Concat(_state.PreviousDirectories).Concat(_state.Books.Select(b => b.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        public IReadOnlyList<string> BookDirectories => _state.Books.Select(b => b.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public OnlineScoreStorageSettings(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            string saved = db.GetUISetting(Key);
            // 损坏设置不静默换目录，以免新下载落入错误位置；原数据不会被覆盖。
            _state = string.IsNullOrWhiteSpace(saved) ? new State { Directory = DefaultDirectory }
                : JsonSerializer.Deserialize<State>(saved) ?? throw new InvalidDataException("在线歌谱存储设置无效。");
            _state.Directory = Normalize(_state.Directory);
            _state.PreviousDirectories = (_state.PreviousDirectories ?? new()).Select(Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _state.Books ??= new();
            foreach (var book in _state.Books)
            {
                book.Path = Normalize(book.Path);
                if (!IsStorageRoot(System.IO.Path.GetDirectoryName(book.Path)))
                    throw new InvalidDataException("歌本目录不在已配置的在线歌谱存储位置中。");
            }
        }

        public static string Normalize(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
                throw new ArgumentException("请选择完整的歌谱文件夹路径。");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }

        public bool IsLibraryDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;
            try
            {
                string path = Normalize(directory);
                return string.Equals(path, DirectoryPath, StringComparison.OrdinalIgnoreCase) ||
                    _state.PreviousDirectories.Contains(path, StringComparer.OrdinalIgnoreCase) ||
                    _state.Books.Any(b => string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase));
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        public bool IsStorageRoot(string path) => !string.IsNullOrWhiteSpace(path) &&
            new[] { DirectoryPath }.Concat(_state.PreviousDirectories)
                .Contains(Normalize(path), StringComparer.OrdinalIgnoreCase);

        public string GetBookDirectory(string bookName)
        {
            string name = string.IsNullOrWhiteSpace(bookName) ? "未分类歌本" : bookName.Trim();
            var existing = _state.Books.FirstOrDefault(b => b.Name == name &&
                string.Equals(System.IO.Path.GetDirectoryName(b.Path), DirectoryPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                System.IO.Directory.CreateDirectory(existing.Path);
                _db.ImportFolder(existing.Path, name);
                return existing.Path;
            }
            string safe = new string(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ||
                char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
            while (safe.Contains("..", StringComparison.Ordinal)) safe = safe.Replace("..", "_");
            if (safe.Length > 70) safe = safe[..70].TrimEnd('.', ' ');
            if (safe.Length == 0) safe = "未分类歌本";
            string device = safe.Split('.')[0].ToUpperInvariant();
            if (device is "CON" or "PRN" or "AUX" or "NUL" ||
                device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) &&
                device[3] >= '1' && device[3] <= '9') safe = "_" + safe;
            string path = System.IO.Path.Combine(DirectoryPath, safe);
            // 已导入的自定义目录或同名净化结果不能被改成在线歌本。
            for (int i = 1; System.IO.Directory.Exists(path) || System.IO.File.Exists(path) ||
                _state.Books.Any(b => string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                _db.GetAllFolders().Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)); i++)
                path = System.IO.Path.Combine(DirectoryPath, safe + (i == 1 ? "（在线）" : "（在线" + i + "）"));
            System.IO.Directory.CreateDirectory(path);
            var entry = new BookFolder { Name = name, Path = path };
            _state.Books.Add(entry);
            try
            {
                using var transaction = _db.GetDbContext().Database.BeginTransaction();
                _db.ImportFolder(path, name);
                _db.SaveUISetting(Key, JsonSerializer.Serialize(_state));
                transaction.Commit();
            }
            catch { _state.Books.Remove(entry); throw; }
            return path;
        }

        public string GetLibraryDisplayName(string path) => _state.Books.FirstOrDefault(b =>
            string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase))?.Name ?? "未分类歌本（旧缓存）";

        public void ChangeDirectory(string directory)
        {
            string path = Normalize(directory);
            System.IO.Directory.CreateDirectory(path);
            // 先确认能够写入；测试文件只属于本次操作，不删除任何用户文件。
            string probe = Path.Combine(path, ".canvas-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1, FileOptions.DeleteOnClose)) { }
            if (string.Equals(path, DirectoryPath, StringComparison.OrdinalIgnoreCase)) return;
            var next = new State
            {
                Directory = path,
                Books = _state.Books.ToList(),
                PreviousDirectories = _state.PreviousDirectories.Concat(new[] { DirectoryPath })
                    .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
            using (var transaction = _db.GetDbContext().Database.BeginTransaction())
            {
                _db.SaveUISetting(Key, JsonSerializer.Serialize(next));
                transaction.Commit();
            }
            _state = next; // 保存成功后才切换；不移动文件、不重建图片ID/歌词记录。
        }
    }
}
