using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Services
{
    public enum LyricsImportConflictStrategy
    {
        Skip = 0,
        Overwrite = 1,
        SaveAsCopy = 2
    }

    public sealed class LyricsTransferResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Overwritten { get; set; }
        public int Copied { get; set; }
        public int Failed { get; set; }
    }

    public sealed class LyricsTransferService
    {
        private readonly CanvasDbContext _dbContext;
        private const string LogPrefix = "[歌词导入导出]";

        private static void LogInfo(string message)
        {
            // System.Diagnostics.Debug.WriteLine($"{LogPrefix} {message}");
        }

        private static void LogWarn(string message)
        {
            // System.Diagnostics.Debug.WriteLine($"{LogPrefix} [WARN] {message}");
        }

        private static void LogError(string message)
        {
            // System.Diagnostics.Debug.WriteLine($"{LogPrefix} [ERROR] {message}");
        }

        public LyricsTransferService(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public Task<LyricsTransferResult> ExportSongAsync(int songId, string targetPath)
        {
            LogInfo($"[ExportSong-Begin] songId={songId}, target={targetPath}");
            var song = _dbContext.LyricsProjects.FirstOrDefault(s => s.Id == songId);
            if (song == null)
            {
                LogWarn($"[ExportSong-Skip] song not found, songId={songId}");
                return Task.FromResult(new LyricsTransferResult { Success = false, Message = "歌曲不存在" });
            }

            var group = song.GroupId.HasValue
                ? _dbContext.LyricsGroups.FirstOrDefault(g => g.Id == song.GroupId.Value)
                : null;

            var manifest = BuildManifest("single", group == null ? new List<LyricsGroup>() : new List<LyricsGroup> { group }, new List<LyricsProject> { song });
            WriteLyrPackage(targetPath, manifest);
            LogInfo($"[ExportSong-End] songId={songId}, songs=1, group={(group == null ? 0 : 1)}");
            return Task.FromResult(new LyricsTransferResult { Success = true, Message = "导出成功", Imported = 1 });
        }

        public Task<LyricsTransferResult> ExportGroupAsync(int groupId, string targetPath)
        {
            LogInfo($"[ExportGroup-Begin] groupId={groupId}, target={targetPath}");
            var group = _dbContext.LyricsGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null)
            {
                LogWarn($"[ExportGroup-Skip] group not found, groupId={groupId}");
                return Task.FromResult(new LyricsTransferResult { Success = false, Message = "分组不存在" });
            }

            var songs = _dbContext.LyricsProjects.Where(s => s.GroupId == groupId).OrderBy(s => s.SortOrder).ToList();
            var manifest = BuildManifest("library", new List<LyricsGroup> { group }, songs);
            WriteLyrPackage(targetPath, manifest);
            LogInfo($"[ExportGroup-End] groupId={groupId}, songs={songs.Count}");
            return Task.FromResult(new LyricsTransferResult { Success = true, Message = "导出成功", Imported = songs.Count });
        }

        public Task<LyricsTransferResult> ExportLibraryAsync(string targetPath)
        {
            LogInfo($"[ExportLibrary-Begin] target={targetPath}");
            var groups = _dbContext.LyricsGroups.OrderBy(g => g.SortOrder).ToList();
            var songs = _dbContext.LyricsProjects.OrderBy(s => s.SortOrder).ToList();
            var manifest = BuildManifest("library", groups, songs);
            WriteLyrPackage(targetPath, manifest);
            LogInfo($"[ExportLibrary-End] groups={groups.Count}, songs={songs.Count}");
            return Task.FromResult(new LyricsTransferResult { Success = true, Message = "导出成功", Imported = songs.Count });
        }

        public async Task<LyricsTransferResult> ImportAsync(string sourcePath, LyricsImportConflictStrategy strategy)
        {
            LogInfo($"[Import-Begin] source={sourcePath}, strategy={strategy}");
            if (!File.Exists(sourcePath))
            {
                LogWarn($"[Import-Skip] source file missing: {sourcePath}");
                return new LyricsTransferResult { Success = false, Message = "文件不存在" };
            }

            LyricsPackageManifest manifest;
            try
            {
                manifest = ReadLyrPackage(sourcePath);
                LogInfo($"[Import-Manifest] format={manifest?.Format}, schema={manifest?.SchemaVersion}, groups={manifest?.Groups?.Count ?? 0}, songs={manifest?.Songs?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                LogError($"[Import-ParseFail] {ex}");
                return new LyricsTransferResult { Success = false, Message = $"解析失败: {ex.Message}" };
            }

            if (!IsSupportedLyricsManifest(manifest))
            {
                LogWarn($"[Import-Unsupported] format={manifest?.Format ?? "(null)"}");
                return new LyricsTransferResult { Success = false, Message = "不支持的歌词包格式" };
            }

            manifest = ResolveImportedWatermarkFiles(manifest, sourcePath);

            var result = new LyricsTransferResult();
            await using var tx = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                var groupMap = new Dictionary<string, int>();
                foreach (var g in manifest.Groups ?? new List<LyricsGroupDto>())
                {
                    var existing = _dbContext.LyricsGroups.FirstOrDefault(x => x.ExternalId == g.ExternalId);
                    if (existing == null)
                    {
                        existing = new LyricsGroup
                        {
                            Name = string.IsNullOrWhiteSpace(g.Name) ? "未命名分组" : g.Name,
                            ExternalId = string.IsNullOrWhiteSpace(g.ExternalId) ? Guid.NewGuid().ToString() : g.ExternalId,
                            SortOrder = g.SortOrder,
                            HighlightColor = g.HighlightColor,
                            IsSystem = false,
                            CreatedTime = DateTime.Now
                        };
                        _dbContext.LyricsGroups.Add(existing);
                        await _dbContext.SaveChangesAsync();
                    }
                    else
                    {
                        bool changed = false;
                        if (!string.IsNullOrWhiteSpace(g.Name) && !string.Equals(existing.Name, g.Name, StringComparison.Ordinal))
                        {
                            existing.Name = g.Name;
                            changed = true;
                        }

                        if (existing.SortOrder != g.SortOrder)
                        {
                            existing.SortOrder = g.SortOrder;
                            changed = true;
                        }

                        string incomingColor = g.HighlightColor ?? string.Empty;
                        string currentColor = existing.HighlightColor ?? string.Empty;
                        if (!string.Equals(currentColor, incomingColor, StringComparison.Ordinal))
                        {
                            existing.HighlightColor = incomingColor;
                            changed = true;
                        }

                        if (changed)
                        {
                            existing.ModifiedTime = DateTime.Now;
                            await _dbContext.SaveChangesAsync();
                        }
                    }

                    groupMap[g.ExternalId ?? ""] = existing.Id;
                }

                foreach (var s in manifest.Songs ?? new List<LyricsSongDto>())
                {
                    try
                    {
                        var existing = FindSongConflict(s.ExternalId, s.Name);
                        if (existing == null)
                        {
                            var created = BuildSongFromDto(s, groupMap);
                            _dbContext.LyricsProjects.Add(created);
                            result.Imported++;
                            continue;
                        }

                        switch (strategy)
                        {
                            case LyricsImportConflictStrategy.Skip:
                                result.Skipped++;
                                break;
                            case LyricsImportConflictStrategy.Overwrite:
                                ApplySongDto(existing, s, groupMap, keepName: false);
                                result.Overwritten++;
                                break;
                            case LyricsImportConflictStrategy.SaveAsCopy:
                                var copy = BuildSongFromDto(s, groupMap);
                                copy.ExternalId = Guid.NewGuid().ToString();
                                copy.Name = GenerateCopyName(s.Name);
                                _dbContext.LyricsProjects.Add(copy);
                                result.Copied++;
                                break;
                        }
                    }
                    catch (Exception exSong)
                    {
                        LogError($"[Import-SongFail] name={s?.Name ?? "(null)"}, externalId={s?.ExternalId ?? "(null)"}, err={exSong.Message}");
                        result.Failed++;
                    }
                }

                await _dbContext.SaveChangesAsync();
                await tx.CommitAsync();
                result.Success = true;
                result.Message = $"导入完成：新增{result.Imported}，覆盖{result.Overwritten}，副本{result.Copied}，跳过{result.Skipped}，失败{result.Failed}";
                LogInfo($"[Import-End] success=true, imported={result.Imported}, overwritten={result.Overwritten}, copied={result.Copied}, skipped={result.Skipped}, failed={result.Failed}");
                return result;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                LogError($"[Import-Fail] {ex}");
                return new LyricsTransferResult { Success = false, Message = $"导入失败: {ex.Message}" };
            }
        }

        public Task<int> CountConflictsAsync(string sourcePath)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("文件不存在", sourcePath);
            }

            var manifest = ReadLyrPackage(sourcePath);
            if (!IsSupportedLyricsManifest(manifest))
            {
                throw new InvalidOperationException("不支持的歌词包格式");
            }

            int count = 0;
            foreach (var s in manifest.Songs ?? new List<LyricsSongDto>())
            {
                if (FindSongConflict(s.ExternalId, s.Name) != null)
                {
                    count++;
                }
            }

            LogInfo($"[Import-Precheck] source={sourcePath}, songs={manifest.Songs?.Count ?? 0}, conflicts={count}");
            return Task.FromResult(count);
        }

        private LyricsProject BuildSongFromDto(LyricsSongDto s, Dictionary<string, int> groupMap)
        {
            int? groupId = null;
            if (!string.IsNullOrWhiteSpace(s.GroupExternalId) && groupMap.TryGetValue(s.GroupExternalId, out var gid))
            {
                groupId = gid;
            }

            return new LyricsProject
            {
                Name = string.IsNullOrWhiteSpace(s.Name) ? $"歌曲_{DateTime.Now:HHmmss}" : s.Name,
                GroupId = groupId,
                ExternalId = string.IsNullOrWhiteSpace(s.ExternalId) ? Guid.NewGuid().ToString() : s.ExternalId,
                Content = s.Content ?? "",
                FontSize = s.FontSize <= 0 ? 60 : s.FontSize,
                TextAlign = string.IsNullOrWhiteSpace(s.TextAlign) ? "Center" : s.TextAlign,
                ViewMode = s.ViewMode,
                ProjectionWatermarkPath = s.ProjectionWatermarkPath ?? "",
                SourceType = s.SourceType,
                SortOrder = s.SortOrder,
                ImageId = null,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now
            };
        }

        private void ApplySongDto(LyricsProject target, LyricsSongDto source, Dictionary<string, int> groupMap, bool keepName)
        {
            if (!keepName && !string.IsNullOrWhiteSpace(source.Name))
            {
                target.Name = source.Name;
            }

            if (!string.IsNullOrWhiteSpace(source.GroupExternalId) && groupMap.TryGetValue(source.GroupExternalId, out var gid))
            {
                target.GroupId = gid;
            }

            target.Content = source.Content ?? "";
            target.FontSize = source.FontSize <= 0 ? target.FontSize : source.FontSize;
            target.TextAlign = string.IsNullOrWhiteSpace(source.TextAlign) ? target.TextAlign : source.TextAlign;
            target.ViewMode = source.ViewMode;
            target.ProjectionWatermarkPath = source.ProjectionWatermarkPath ?? "";
            target.SourceType = source.SourceType;
            target.SortOrder = source.SortOrder;
            target.ModifiedTime = DateTime.Now;
        }

        private LyricsProject FindSongConflict(string externalId, string name)
        {
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                var byExternal = _dbContext.LyricsProjects.FirstOrDefault(x => x.ExternalId == externalId);
                if (byExternal != null)
                {
                    return byExternal;
                }
            }

            string normalizedName = NormalizeName(name);
            return _dbContext.LyricsProjects
                .AsEnumerable()
                .FirstOrDefault(x => NormalizeName(x.Name) == normalizedName);
        }

        private string GenerateCopyName(string baseName)
        {
            string name = string.IsNullOrWhiteSpace(baseName) ? "歌曲" : baseName;
            string candidate = $"{name}_导入";
            int n = 1;
            while (_dbContext.LyricsProjects.Any(x => x.Name == candidate))
            {
                n++;
                candidate = $"{name}_导入{n}";
            }
            return candidate;
        }

        private static string NormalizeName(string name)
        {
            return (name ?? "").Trim().ToLowerInvariant();
        }

        private LyricsPackageManifest BuildManifest(string packageType, List<LyricsGroup> groups, List<LyricsProject> songs)
        {
            return new LyricsPackageManifest
            {
                Format = "canvas.zip",
                SchemaVersion = 1,
                PackageType = packageType,
                ExportedAt = DateTime.UtcNow,
                AppVersion = "6.0.1.x",
                Groups = groups.Select(g => new LyricsGroupDto
                {
                    ExternalId = g.ExternalId,
                    Name = g.Name,
                    SortOrder = g.SortOrder,
                    HighlightColor = g.HighlightColor
                }).ToList(),
                Songs = songs.Select(s => new LyricsSongDto
                {
                    ExternalId = s.ExternalId,
                    Name = s.Name,
                    GroupExternalId = groups.FirstOrDefault(g => g.Id == s.GroupId)?.ExternalId,
                    Content = s.Content ?? "",
                    FontSize = s.FontSize,
                    TextAlign = s.TextAlign,
                    ViewMode = s.ViewMode,
                    ProjectionWatermarkPath = s.ProjectionWatermarkPath ?? "",
                    SourceType = s.SourceType,
                    SortOrder = s.SortOrder,
                    ModifiedTime = s.ModifiedTime
                }).ToList()
            };
        }

        private static void WriteLyrPackage(string targetPath, LyricsPackageManifest manifest)
        {
            if (manifest == null)
            {
                throw new InvalidOperationException("歌词清单为空，无法导出");
            }

            LogInfo($"[Package-Write-Begin] target={targetPath}, songs={manifest?.Songs?.Count ?? 0}");
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            using (var fs = File.Create(targetPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var watermarkMapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var song in manifest.Songs ?? new List<LyricsSongDto>())
                {
                    if (string.IsNullOrWhiteSpace(song.ProjectionWatermarkPath))
                    {
                        continue;
                    }

                    string sourcePath = song.ProjectionWatermarkPath;
                    if (!Path.IsPathRooted(sourcePath))
                    {
                        sourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, sourcePath);
                    }

                    if (!File.Exists(sourcePath))
                    {
                        song.ProjectionWatermarkPath = "";
                        continue;
                    }

                    if (!watermarkMapped.TryGetValue(sourcePath, out var packagePath))
                    {
                        string ext = Path.GetExtension(sourcePath);
                        string stem = Path.GetFileNameWithoutExtension(sourcePath);
                        if (string.IsNullOrWhiteSpace(ext))
                        {
                            ext = ".png";
                        }
                        if (string.IsNullOrWhiteSpace(stem))
                        {
                            stem = "watermark";
                        }

                        string candidate = $"watermarks/{stem}{ext}";
                        int n = 1;
                        while (zip.GetEntry(candidate) != null)
                        {
                            n++;
                            candidate = $"watermarks/{stem}_{n}{ext}";
                        }

                        zip.CreateEntryFromFile(sourcePath, candidate, CompressionLevel.Optimal);
                        packagePath = candidate;
                        watermarkMapped[sourcePath] = packagePath;
                    }

                    song.ProjectionWatermarkPath = packagePath;
                }

                var entry = zip.CreateEntry("manifest.json");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
            string json = JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                writer.Write(json);
                LogInfo("[Package-Write-End] manifest.json written");
            }

            // 导出后立即自检，避免出现“空zip”而UI仍显示成功
            ValidateLyrPackageFile(targetPath);
        }

        private static LyricsPackageManifest ResolveImportedWatermarkFiles(LyricsPackageManifest manifest, string sourcePath)
        {
            if (manifest?.Songs == null || manifest.Songs.Count == 0 || !File.Exists(sourcePath))
            {
                return manifest;
            }

            string targetDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "watermarks");
            Directory.CreateDirectory(targetDirectory);

            using var fs = File.OpenRead(sourcePath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int remapped = 0;
            int missing = 0;
            foreach (var song in manifest.Songs)
            {
                string packagePath = song.ProjectionWatermarkPath ?? "";
                if (string.IsNullOrWhiteSpace(packagePath))
                {
                    continue;
                }

                if (!packagePath.StartsWith("watermarks/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!extracted.TryGetValue(packagePath, out var relativePath))
                {
                    var entry = zip.GetEntry(packagePath);
                    if (entry == null)
                    {
                        song.ProjectionWatermarkPath = "";
                        missing++;
                        continue;
                    }

                    string fileName = Path.GetFileName(packagePath);
                    string stem = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    if (string.IsNullOrWhiteSpace(stem))
                    {
                        stem = "watermark";
                    }
                    if (string.IsNullOrWhiteSpace(ext))
                    {
                        ext = ".png";
                    }

                    string targetPath = Path.Combine(targetDirectory, $"{stem}{ext}");
                    int n = 1;
                    while (File.Exists(targetPath))
                    {
                        n++;
                        targetPath = Path.Combine(targetDirectory, $"{stem}_{n}{ext}");
                    }

                    using (var inStream = entry.Open())
                    using (var outStream = File.Create(targetPath))
                    {
                        inStream.CopyTo(outStream);
                    }

                    relativePath = Path.Combine("data", "watermarks", Path.GetFileName(targetPath)).Replace('\\', '/');
                    extracted[packagePath] = relativePath;
                }

                song.ProjectionWatermarkPath = relativePath;
                remapped++;
            }

            LogInfo($"[Watermark-Resolve] source={sourcePath}, remapped={remapped}, missing={missing}");

            return manifest;
        }

        private static LyricsPackageManifest ReadLyrPackage(string sourcePath)
        {
            LogInfo($"[Package-Read-Begin] source={sourcePath}");

            try
            {
                using var fs = File.OpenRead(sourcePath);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

                if (zip.Entries.Count == 0)
                {
                    throw new InvalidOperationException("歌词包为空（未包含任何文件）");
                }

                var entry = zip.GetEntry("manifest.json");
                if (entry == null)
                {
                    throw new InvalidOperationException("歌词包缺少清单文件（manifest.json）");
                }

                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                var manifest = JsonSerializer.Deserialize<LyricsPackageManifest>(json, CreateManifestJsonOptions())
                    ?? throw new InvalidOperationException("manifest.json 解析失败");
                LogInfo($"[Package-Read-End] mode=zip, entry={entry.FullName}, format={manifest.Format}, schema={manifest.SchemaVersion}, groups={manifest.Groups?.Count ?? 0}, songs={manifest.Songs?.Count ?? 0}");
                return manifest;
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException("仅支持 ZIP 歌词包（.zip）", ex);
            }
        }

        private static void ValidateLyrPackageFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("导出后未找到歌词包文件");
            }

            using var fs = File.OpenRead(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            if (zip.Entries.Count == 0)
            {
                throw new InvalidOperationException("导出失败：歌词包为空");
            }

            var manifest = zip.GetEntry("manifest.json");
            if (manifest == null)
            {
                throw new InvalidOperationException("导出失败：歌词包缺少manifest");
            }

            using var stream = manifest.Open();
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            var manifestObject = JsonSerializer.Deserialize<LyricsPackageManifest>(json, CreateManifestJsonOptions())
                ?? throw new InvalidOperationException("导出失败：manifest解析失败");
            if (!IsSupportedLyricsManifest(manifestObject))
            {
                throw new InvalidOperationException("导出失败：manifest格式无效");
            }
        }

        private static JsonSerializerOptions CreateManifestJsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        private static bool IsSupportedLyricsManifest(LyricsPackageManifest manifest)
        {
            if (manifest == null)
            {
                return false;
            }

            string format = (manifest.Format ?? string.Empty).Trim();
            if (string.Equals(format, "canvas.zip", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private sealed class LyricsPackageManifest
        {
            [JsonPropertyName("format")]
            public string Format { get; set; } = "";
            [JsonPropertyName("schemaVersion")]
            public int SchemaVersion { get; set; }
            [JsonPropertyName("packageType")]
            public string PackageType { get; set; } = "";
            [JsonPropertyName("exportedAt")]
            public DateTime ExportedAt { get; set; }
            [JsonPropertyName("appVersion")]
            public string AppVersion { get; set; } = "";
            [JsonPropertyName("groups")]
            public List<LyricsGroupDto> Groups { get; set; } = new();
            [JsonPropertyName("songs")]
            public List<LyricsSongDto> Songs { get; set; } = new();
        }

        private sealed class LyricsGroupDto
        {
            [JsonPropertyName("externalId")]
            public string ExternalId { get; set; } = "";
            [JsonPropertyName("name")]
            public string Name { get; set; } = "";
            [JsonPropertyName("sortOrder")]
            public int SortOrder { get; set; }
            [JsonPropertyName("highlightColor")]
            public string HighlightColor { get; set; } = "";
        }

        private sealed class LyricsSongDto
        {
            [JsonPropertyName("externalId")]
            public string ExternalId { get; set; } = "";
            [JsonPropertyName("name")]
            public string Name { get; set; } = "";
            [JsonPropertyName("groupExternalId")]
            public string GroupExternalId { get; set; } = "";
            [JsonPropertyName("content")]
            public string Content { get; set; } = "";
            [JsonPropertyName("fontSize")]
            public double FontSize { get; set; }
            [JsonPropertyName("textAlign")]
            public string TextAlign { get; set; } = "";
            [JsonPropertyName("viewMode")]
            public int ViewMode { get; set; }
            [JsonPropertyName("projectionWatermarkPath")]
            public string ProjectionWatermarkPath { get; set; } = "";
            [JsonPropertyName("sourceType")]
            public int SourceType { get; set; }
            [JsonPropertyName("sortOrder")]
            public int SortOrder { get; set; }
            [JsonPropertyName("modifiedTime")]
            public DateTime? ModifiedTime { get; set; }
        }
    }
}
