using System;
using System.IO;
using System.Linq;
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
        private static readonly SemaphoreSlim DownloadLock = new(1, 1);
        public string DirectoryPath { get; }

        public OnlineScoreLibrary(DatabaseManager db, string directory = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            DirectoryPath = Path.GetFullPath(directory ?? Path.Combine(AppContext.BaseDirectory, "YM"));
        }

        // 仓库入口允许空目录。仅启动时扫描一次，下载时只登记当前新增图片。
        public Folder EnsureFolder(bool importExisting = false)
        {
            Directory.CreateDirectory(DirectoryPath);
            var folder = _db.ImportFolder(DirectoryPath, "YM");
            if (importExisting)
                foreach (string path in Directory.EnumerateFiles(DirectoryPath))
                    if (ImageFileSupport.ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                        RegisterImage(path);
            return folder;
        }

        public MediaFile RegisterImage(string path)
        {
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(path), DirectoryPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new ScoreIntegrationException("歌谱文件不在 YM 中或已不存在，未登记。");
            var folder = EnsureFolder();
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
                string prefix = result.Id + " - ";
                foreach (string existing in Directory.EnumerateFiles(DirectoryPath, prefix + "*"))
                    if (await Task.Run(() => IsValidImage(existing), ct)) return existing;

                // 临时文件与 YM 隔离，失败/取消不产生半成品或可被导入的错误图片。
                stage = Path.Combine(Path.GetTempPath(), "CanvasCast-score-download", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                downloaded = await download(stage, ct);
                if (!await Task.Run(() => IsValidImage(downloaded), ct))
                    throw new ScoreIntegrationException("下载的歌谱图片无效，未保存到 YM。");
                ct.ThrowIfCancellationRequested();
                string name = new string((result.Name ?? "歌谱").Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
                name = name.Trim().TrimEnd('.');
                if (name.Length > 70) name = name[..70];
                if (name.Length == 0) name = "歌谱";
                string target = Path.Combine(DirectoryPath, prefix + name + Path.GetExtension(downloaded).ToLowerInvariant());
                // staging 可能与程序不在同盘：先复制 .part，再同目录原子更名。
                string pending = target + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    File.Copy(downloaded, pending);
                    ct.ThrowIfCancellationRequested();
                    File.Move(pending, target, overwrite: true);
                }
                finally { if (File.Exists(pending)) File.Delete(pending); }
                return target;
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
