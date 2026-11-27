using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.Lyrics;
using ImageColorChanger.Utils;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace ImageColorChanger.Services.OnlineScores
{
    /// <summary>在线来源落地为普通本地图片；不负责显示、播放或投影。</summary>
    public sealed class OnlineScoreLibrary
    {
        private readonly DatabaseManager _db;
        private readonly string[] _cacheDirectories;
        private static readonly SemaphoreSlim DownloadLock = new(1, 1);
        private const string SourceFilesKey = "OnlineScores.SourceFiles.v1";
        private sealed class SourceFile
        {
            public string Path { get; set; }
            public int? MediaId { get; set; }
            public string Sha256 { get; set; }
        }
        private static readonly ConcurrentDictionary<string, (long Size, long Written, int? Id)> LocalIdentities = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, SourceFile> ReadSourceFiles()
        {
            string json = _db.GetUISetting(SourceFilesKey);
            return string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<Dictionary<int, SourceFile>>(json)
                ?? throw new ScoreIntegrationException("在线歌谱来源索引无效，未覆盖已有文件。");
        }

        /// <summary>只用外部身份识别本地谱，不用歌名猜同一首；数据库快照在UI线程取得，文件检查在后台完成。</summary>
        public Task<Dictionary<int, string>> GetLocalScorePathsAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); var wanted = ids.ToHashSet();
            var media = _db.GetDbContext().MediaFiles.AsNoTracking().Where(m => m.FileTypeString == "image")
                .Select(m => new { m.Id, m.Path }).ToArray();
            var registered = media.ToDictionary(m => m.Id, m => m.Path);
            var sources = ReadSourceFiles();
            return Task.Run(() =>
            {
                var paths = new HashSet<string>(media.Select(m => m.Path), StringComparer.OrdinalIgnoreCase);
                foreach (string directory in _cacheDirectories)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Directory.Exists(directory)) foreach (string file in Directory.EnumerateFiles(directory)) paths.Add(file);
                }
                var result = new Dictionary<int, string>();
                // Structure identity survives title-only filenames, moves and renames, including imported local folders.
                foreach (string path in paths.OrderBy(p => StructuredScoreDocument.IsScoreFile(p) ? 0 : 1))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!StructuredScoreDocument.IsScoreFile(path) && !string.Equals(System.IO.Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)) continue;
                    int? id = ReadLocalIdentity(path);
                    if (id is > 0 && wanted.Contains(id.Value)) result.TryAdd(id.Value, path);
                }
                // Ordinary images have no code chunk: persist external-id mapping separately, not as a filename prefix.
                foreach (var item in sources.Where(p => wanted.Contains(p.Key)))
                {
                    ct.ThrowIfCancellationRequested();
                    string path = item.Value.MediaId.HasValue && registered.TryGetValue(item.Value.MediaId.Value, out string moved) ? moved : item.Value.Path;
                    if (result.ContainsKey(item.Key) || string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    try
                    {
                        if (IsValidImage(path) && item.Value.Sha256 == HashFile(path)) result.Add(item.Key, path);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                // Compatibility for historical raster downloads; never infer identity from a title alone.
                foreach (string path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    var match = Regex.Match(System.IO.Path.GetFileName(path), @"^(\d+) - ");
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out int id) || !wanted.Contains(id) || result.ContainsKey(id)) continue;
                    if (_cacheDirectories.Contains(System.IO.Path.GetDirectoryName(path), StringComparer.OrdinalIgnoreCase) &&
                        !StructuredScoreDocument.IsScoreFile(path) && ReadLocalIdentity(path) == null && IsValidImage(path)) result.Add(id, path);
                }
                return result;
            }, ct);
        }

        private static int? ReadLocalIdentity(string path)
        {
            try
            {
                var info = new FileInfo(path); if (!info.Exists) return null;
                if (LocalIdentities.TryGetValue(path, out var saved) && saved.Size == info.Length && saved.Written == info.LastWriteTimeUtc.Ticks) return saved.Id;
                int? id = StructuredScoreDocument.ReadFromFile(path)?.ShigebenId;
                LocalIdentities[path] = (info.Length, info.LastWriteTimeUtc.Ticks, id); return id;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ScoreIntegrationException) { return null; }
        }
        private static string HashFile(string path)
        { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

        private static string SongFileStem(string title)
        {
            string name = new string((title ?? "歌谱").Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
            if (name.Length > 70) name = name[..70].TrimEnd('.', ' ');
            if (name.Length == 0) name = "歌谱";
            string device = name.Split('.')[0].ToUpperInvariant();
            if (device is "CON" or "PRN" or "AUX" or "NUL" || device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3] >= '1' && device[3] <= '9') name = "_" + name;
            return name;
        }
        public string DirectoryPath { get; }
        public string DisplayName => new DirectoryInfo(DirectoryPath).Name;

        public OnlineScoreLibrary(DatabaseManager db, string directory = null) : this(db, directory, null) { }

        public OnlineScoreLibrary(DatabaseManager db, string directory, System.Collections.Generic.IReadOnlyList<string> cacheDirectories)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            DirectoryPath = OnlineScoreStorageSettings.Normalize(directory ?? new OnlineScoreStorageSettings(db).DirectoryPath);
            _cacheDirectories = new[] { DirectoryPath }.Concat(cacheDirectories ?? Array.Empty<string>())
                .Select(OnlineScoreStorageSettings.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        // 仓库入口允许空目录。仅启动时扫描一次，下载时只登记当前新增图片。
        public Folder EnsureFolder(bool importExisting = false)
        {
            Directory.CreateDirectory(DirectoryPath);
            var folder = _db.ImportFolder(DirectoryPath, DisplayName);
            if (importExisting)
                foreach (string path in Directory.EnumerateFiles(DirectoryPath))
                    if (StructuredScoreDocument.IsScoreFile(path) || ImageFileSupport.ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                        RegisterImage(path);
            return folder;
        }

        public MediaFile RegisterImage(string path)
        {
            path = Path.GetFullPath(path);
            if (!_cacheDirectories.Contains(Path.GetDirectoryName(path), StringComparer.OrdinalIgnoreCase) || !File.Exists(path))
                throw new ScoreIntegrationException("歌谱文件不在所选存储文件夹中或已不存在，未登记。");
            // 旧缓存不搬动，保持原图片 ID 与用户歌词；只登记它实际所属的目录。
            var folder = _db.ImportFolder(Path.GetDirectoryName(path), new DirectoryInfo(Path.GetDirectoryName(path)).Name);
            var context = _db.GetDbContext();
            using var transaction = context.Database.BeginTransaction();
            var image = _db.AddMediaFile(path, folder.Id);
            if (!context.FolderImages.Any(x => x.FolderId == folder.Id && x.ImageId == image.Id))
                context.FolderImages.Add(new FolderImage
                {
                    FolderId = folder.Id, ImageId = image.Id, OrderIndex = image.OrderIndex,
                    RelativePath = Path.GetFileName(path), DiscoveredAt = DateTime.Now, UpdatedAt = DateTime.Now
                });
            context.SaveChanges();
            transaction.Commit();
            var sources = ReadSourceFiles();
            bool updated = false;
            foreach (var source in sources.Values.Where(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            { source.MediaId = image.Id; updated = true; }
            if (updated) _db.SaveUISetting(SourceFilesKey, JsonSerializer.Serialize(sources));
            return image;
        }

        // 任意已有记录（包括用户清空的正文）都归用户所有，不自动覆盖内容或样式。
        public bool HasLyrics(int imageId) => new ImageLyricsService(_db.GetDbContext()).GetRecordId(imageId) > 0;

        public bool StoreLyricsIfMissing(int imageId, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || HasLyrics(imageId)) return false;
            new ImageLyricsService(_db.GetDbContext()).Save(imageId, new LyricsDocument { Content = text });
            return true;
        }

        public async Task<string> GetOrDownloadAsync(OnlineScoreResult result,
            Func<string, CancellationToken, Task<string>> download, CancellationToken ct)
        {
            if (result == null || result.Id <= 0) throw new ArgumentException("歌谱 ID 无效。", nameof(result));
            await DownloadLock.WaitAsync(ct);
            string stage = null, downloaded = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(DirectoryPath);
                var local = await GetLocalScorePathsAsync(new[] { result.Id }, ct);
                if (local.TryGetValue(result.Id, out string existing) &&
                    (result.HasStructuredNotation != true || StructuredScoreDocument.ReadFromFile(existing) != null)) return existing;

                // 临时文件与 YM 隔离，失败/取消不产生半成品或可被导入的错误图片。
                stage = Path.Combine(Path.GetTempPath(), "CanvasCast-score-download", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                downloaded = await download(stage, ct);
                bool structured = string.Equals(Path.GetExtension(downloaded), StructuredScoreDocument.Extension, StringComparison.OrdinalIgnoreCase);
                if (!structured && !await Task.Run(() => IsValidImage(downloaded), ct))
                    throw new ScoreIntegrationException("下载的歌谱图片无效，未保存到歌谱文件夹。");
                ct.ThrowIfCancellationRequested();
                string name = SongFileStem(result.Name), extension = Path.GetExtension(downloaded).ToLowerInvariant();
                string target = Path.Combine(DirectoryPath, name + extension);
                StructuredScoreDocument score = null;
                if (structured)
                {
                    score = StructuredScoreDocument.Read(downloaded); score.Title = result.Name ?? score.Title;
                    if (score.ShigebenId != result.Id) throw new ScoreIntegrationException("下载谱码的来源编号不匹配，未保存。");
                    score.Save(downloaded);
                }
                // staging 可能与程序不在同盘：先复制 .part，再同目录原子更名。
                string pending = target + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    File.Copy(downloaded, pending);
                    ct.ThrowIfCancellationRequested();
                    // Same-title songs or user files never overwrite one another; an external creator can race safely.
                    for (int suffix = 1; ; suffix++)
                    {
                        ct.ThrowIfCancellationRequested();
                        target = Path.Combine(DirectoryPath, name + (suffix == 1 ? "" : $" ({suffix})") + extension);
                        try { File.Move(pending, target, overwrite: false); break; }
                        catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { }
                    }
                }
                finally { if (File.Exists(pending)) File.Delete(pending); }
                if (!structured && StructuredScoreDocument.TryReadPng(target) == null)
                {
                    var sources = ReadSourceFiles(); sources[result.Id] = new SourceFile { Path = target, Sha256 = HashFile(target) };
                    _db.SaveUISetting(SourceFilesKey, JsonSerializer.Serialize(sources));
                }
                return target; // 有结构数据只保存谱文原件，不在歌本目录保存PNG。
            }
            finally
            {
                // 仅清理本次请求生成的暂存文件，不删除 YM 或上一张图片。
                try
                {
                    if (downloaded != null && stage != null && string.Equals(Path.GetDirectoryName(Path.GetFullPath(downloaded)), stage, StringComparison.OrdinalIgnoreCase))
                        File.Delete(downloaded);
                    if (stage != null && Directory.Exists(stage) && !Directory.EnumerateFileSystemEntries(stage).Any()) Directory.Delete(stage);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                DownloadLock.Release();
            }
        }

        /// <summary>显式图片兜底走独立缓存，不经过结构优先入口，也不新增库节点或覆盖谱码。</summary>
        public static async Task<string> GetOrDownloadOriginalImageAsync(int id, string cacheDirectory,
            Func<string, CancellationToken, Task<string>> download, CancellationToken ct)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            ArgumentNullException.ThrowIfNull(download);
            cacheDirectory = OnlineScoreStorageSettings.Normalize(cacheDirectory);
            await DownloadLock.WaitAsync(ct).ConfigureAwait(false);
            string stage = null, pending = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(cacheDirectory);
                foreach (string extension in new[] { ".png", ".jpg", ".webp", ".bmp" })
                {
                    string cached = Path.Combine(cacheDirectory, id + extension);
                    if (await Task.Run(() => IsValidImage(cached), ct).ConfigureAwait(false)) return cached;
                }
                stage = Path.Combine(cacheDirectory, ".download-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                string downloaded = await download(stage, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(downloaded) ||
                    !string.Equals(Path.GetDirectoryName(Path.GetFullPath(downloaded)), stage, StringComparison.OrdinalIgnoreCase) ||
                    !await Task.Run(() => IsValidImage(downloaded), ct).ConfigureAwait(false))
                    throw new ScoreIntegrationException("诗歌本未提供有效的图片，原谱码和画面保留。");
                string target = Path.Combine(cacheDirectory, id + Path.GetExtension(downloaded).ToLowerInvariant());
                pending = target + "." + Guid.NewGuid().ToString("N") + ".part";
                File.Copy(downloaded, pending);
                ct.ThrowIfCancellationRequested();
                File.Move(pending, target, overwrite: true);
                return target;
            }
            finally
            {
                try
                {
                    if (pending != null && File.Exists(pending)) File.Delete(pending);
                    if (stage != null && Directory.Exists(stage))
                    {
                        // 只清理本次创建的下载暂存，不触碰正式歌本文件。
                        foreach (string file in Directory.EnumerateFiles(stage)) File.Delete(file);
                        if (!Directory.EnumerateFileSystemEntries(stage).Any()) Directory.Delete(stage);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                DownloadLock.Release();
            }
        }

        private static bool IsValidImage(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > ShigebenClient.MaxImageBytes ||
                    !ImageFileSupport.ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return false;
                using var data = SKData.Create(path);
                using var codec = SKCodec.Create(data);
                if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 40_000_000) return false;
                using var bitmap = SKBitmap.Decode(path);
                return bitmap != null;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
