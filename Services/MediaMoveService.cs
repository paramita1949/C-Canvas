using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Services
{
    /// <summary>移动原图片记录，保留 ID；数据库失败时补偿文件移动。不删除/重建图片及歌词。</summary>
    public sealed class MediaMoveService
    {
        private readonly CanvasDbContext _db;
        public MediaMoveService(CanvasDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public string Move(int imageId, int targetFolderId, bool renameOnConflict, string newName = null)
        {
            if (_db.Database.CurrentTransaction != null)
                throw new InvalidOperationException("当前有其他数据操作，请稍后重试。");
            var image = _db.MediaFiles.SingleOrDefault(x => x.Id == imageId && x.FileTypeString == "image")
                ?? throw new InvalidOperationException("歌曲图片已不存在。");
            var folder = _db.Folders.SingleOrDefault(x => x.Id == targetFolderId)
                ?? throw new InvalidOperationException("目标文件夹已不存在。");
            string source = Path.GetFullPath(image.Path), directory = Path.GetFullPath(folder.Path);
            if (!File.Exists(source)) throw new FileNotFoundException("原图片文件已不存在，未修改数据。", source);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("目标文件夹已不存在，请重新选择。");
            if (SamePath(Path.GetDirectoryName(source), directory))
                throw new InvalidOperationException("歌曲已在这个文件夹中，无需移动。");
            string name = newName == null ? null : NormalizeName(newName, Path.GetExtension(source));
            string target = Path.Combine(directory, name == null ? Path.GetFileName(source) : name + Path.GetExtension(source));
            bool Occupied(string p) => File.Exists(p) || Directory.Exists(p) || _db.MediaFiles.Any(x => x.Path == p && x.Id != imageId);
            if (Occupied(target))
            {
                if (!renameOnConflict) throw new IOException("目标文件夹中已有同名文件，请更名后重试。不会覆盖原文件。");
                string stem = Path.GetFileNameWithoutExtension(target), ext = Path.GetExtension(source);
                int suffix = 2;
                do { target = Path.Combine(directory, $"{stem} ({suffix++}){ext}"); } while (Occupied(target));
            }

            return Relocate(image, folder, source, target, name == null ? null : Path.GetFileNameWithoutExtension(target));
        }

        public string Rename(int imageId, string newName)
        {
            var image = _db.MediaFiles.SingleOrDefault(x => x.Id == imageId && x.FileTypeString == "image")
                ?? throw new InvalidOperationException("歌曲图片已不存在。");
            string source = Path.GetFullPath(image.Path);
            string extension = Path.GetExtension(source);
            string name = NormalizeName(newName, extension);
            if (!File.Exists(source)) throw new FileNotFoundException("原图片文件已不存在，未修改数据。", source);
            string target = Path.Combine(Path.GetDirectoryName(source), name + extension);
            if (SamePath(source, target)) throw new InvalidOperationException("名称没有变化，无需重命名。");
            if (File.Exists(target) || Directory.Exists(target) || _db.MediaFiles.Any(x => x.Path == target && x.Id != imageId))
                throw new IOException("同目录已有这个名称的文件，请换一个名称。不会覆盖已有文件。");
            return Relocate(image, null, source, target, name);
        }

        private static string NormalizeName(string newName, string extension)
        {
            string name = (newName ?? "").Trim();
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name = name[..^extension.Length];
            if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl) || name.EndsWith('.') || name.EndsWith(' '))
                throw new InvalidOperationException("请输入有效名称（不超过 180 字），不能包含 \\ / : * ? \" < > | 或以句点结尾。");
            string device = name.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(device))
                throw new InvalidOperationException("这个名称是 Windows 保留名称，请换一个名称。");
            return name;
        }

        private string Relocate(MediaFile image, Folder folder, string source, string target, string newName)
        {
            if (_db.Database.CurrentTransaction != null || _db.ChangeTracker.HasChanges())
                throw new InvalidOperationException("当前有尚未保存的数据，请先保存后重试。");
            int imageId = image.Id;
            // 保存进入本次操作前的跟踪状态，失败不清空调用方已有编辑对象。
            var before = _db.ChangeTracker.Entries().Select(e => (Entry: e, Values: e.CurrentValues.Clone(), State: e.State)).ToList();
            var beforeEntries = before.Select(x => x.Entry.Entity).ToHashSet();
            bool moved = false;
            using var transaction = _db.Database.BeginTransaction();
            try
            {
                File.Move(source, target); // 不覆盖；跨卷由运行时执行复制并删除源文件。
                moved = true;
                image.Path = target;
                if (newName != null) image.Name = newName;
                if (folder != null)
                {
                    image.FolderId = folder.Id;
                    image.OrderIndex = (_db.FolderImages.Where(x => x.FolderId == folder.Id).Max(x => x.OrderIndex) ?? 0) + 1;
                }

                // 递归导入目录可能重叠：移除已不包含该文件的旧映射，保留仍包含它的目录映射。
                var folders = _db.Folders.AsNoTracking().ToList();
                var links = _db.FolderImages.Where(x => x.ImageId == imageId).ToList();
                foreach (var link in links)
                {
                    var owner = folders.FirstOrDefault(x => x.Id == link.FolderId);
                    if (owner == null || !Inside(target, owner.Path)) _db.FolderImages.Remove(link);
                    else { link.RelativePath = Path.GetRelativePath(owner.Path, target); link.UpdatedAt = DateTime.Now; }
                }
                foreach (var owner in folders.Where(x => Inside(target, x.Path)))
                {
                    if (links.Any(x => x.FolderId == owner.Id && _db.Entry(x).State != EntityState.Deleted)) continue;
                    _db.FolderImages.Add(new FolderImage
                    {
                        ImageId = imageId, FolderId = owner.Id, RelativePath = Path.GetRelativePath(owner.Path, target),
                        OrderIndex = (_db.FolderImages.Where(x => x.FolderId == owner.Id).Max(x => x.OrderIndex) ?? 0) + 1,
                        DiscoveredAt = DateTime.Now, UpdatedAt = DateTime.Now
                    });
                }

                // 已有幻灯片引用也跟随文件，不只更新 images.path。
                foreach (var project in _db.TextProjects.Where(x => x.BackgroundImagePath == source)) project.BackgroundImagePath = target;
                foreach (var slide in _db.Slides.Where(x => x.BackgroundImagePath == source)) slide.BackgroundImagePath = target;
                foreach (var lyrics in _db.ImageLyrics.Where(x => x.ProjectionWatermarkPath == source)) lyrics.ProjectionWatermarkPath = target;
                foreach (var lyrics in _db.LyricsProjects.Where(x => x.ProjectionWatermarkPath == source)) lyrics.ProjectionWatermarkPath = target;
                foreach (var slide in _db.Slides.Where(x => x.SplitRegionsData != null && x.SplitRegionsData != ""))
                {
                    try
                    {
                        var json = JsonNode.Parse(slide.SplitRegionsData);
                        if (RemapImagePaths(json, source, target)) slide.SplitRegionsData = json.ToJsonString();
                    }
                    catch (JsonException) { /* 不因其他幻灯片的旧格式数据阻断本图片移动；不改写无效数据。 */ }
                }
                _db.SaveChanges(false);
                transaction.Commit();
                _db.ChangeTracker.AcceptAllChanges();
                return target;
            }
            catch (Exception error)
            {
                transaction.Rollback();
                foreach (var entry in _db.ChangeTracker.Entries().Where(x => !beforeEntries.Contains(x.Entity)).ToList()) entry.State = EntityState.Detached;
                foreach (var snapshot in before)
                {
                    snapshot.Entry.CurrentValues.SetValues(snapshot.Values);
                    snapshot.Entry.State = snapshot.State;
                }
                if (moved)
                {
                    try { File.Move(target, source); }
                    catch (Exception restoreError)
                    {
                        throw new IOException($"数据库未更新；图片仍保留在 {target}。恢复原位置失败，请勿删除该文件。", new AggregateException(error, restoreError));
                    }
                }
                throw;
            }
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }
        private static bool Inside(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;
            try
            {
                string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
                return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }
        private static bool RemapImagePaths(JsonNode node, string oldPath, string newPath)
        {
            bool changed = false;
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(x => x.Key).ToList())
                {
                    if (key.Equals("ImagePath", StringComparison.OrdinalIgnoreCase) && obj[key] is JsonValue value && value.TryGetValue<string>(out var path) && !string.IsNullOrWhiteSpace(path) && SamePath(path, oldPath))
                    { obj[key] = newPath; changed = true; }
                    else changed |= RemapImagePaths(obj[key], oldPath, newPath);
                }
            }
            else if (node is JsonArray array) foreach (var child in array) changed |= RemapImagePaths(child, oldPath, newPath);
            return changed;
        }
    }
}
