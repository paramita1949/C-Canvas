using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
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

        public LyricsTransferService(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public Task<LyricsTransferResult> ExportSongAsync(int songId, string targetPath)
        {
            var song = _dbContext.LyricsProjects.FirstOrDefault(s => s.Id == songId);
            if (song == null)
            {
                return Task.FromResult(new LyricsTransferResult { Success = false, Message = "歌曲不存在" });
            }

            var group = song.GroupId.HasValue
                ? _dbContext.LyricsGroups.FirstOrDefault(g => g.Id == song.GroupId.Value)
                : null;

            var manifest = BuildManifest("single", group == null ? new List<LyricsGroup>() : new List<LyricsGroup> { group }, new List<LyricsProject> { song });
            WriteLyrPackage(targetPath, manifest);
            return Task.FromResult(new LyricsTransferResult { Success = true, Message = "导出成功", Imported = 1 });
        }

        public Task<LyricsTransferResult> ExportGroupAsync(int groupId, string targetPath)
        {
            var group = _dbContext.LyricsGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null)
            {
                return Task.FromResult(new LyricsTransferResult { Success = false, Message = "分组不存在" });
            }

            var songs = _dbContext.LyricsProjects.Where(s => s.GroupId == groupId).OrderBy(s => s.SortOrder).ToList();
            var manifest = BuildManifest("library", new List<LyricsGroup> { group }, songs);
            WriteLyrPackage(targetPath, manifest);
            return Task.FromResult(new LyricsTransferResult { Success = true, Message = "导出成功", Imported = songs.Count });
        }

        public Task<LyricsTransferResult> ExportLibraryAsync(string targetPath)
        {
            var groups = _dbContext.LyricsGroups.OrderBy(g => g.SortOrder).ToList();
            var songs = _dbContext.LyricsProjects.OrderBy(s => s.SortOrder).ToList();
            var manifest = BuildManifest("library", groups, songs);
            WriteLyrPackage(targetPath, manifest);
            return Task.FromResult(new LyricsTransferResult { Success = true, Message = "导出成功", Imported = songs.Count });
        }

        public async Task<LyricsTransferResult> ImportAsync(string sourcePath, LyricsImportConflictStrategy strategy)
        {
            if (!File.Exists(sourcePath))
            {
                return new LyricsTransferResult { Success = false, Message = "文件不存在" };
            }

            LyricsPackageManifest manifest;
            try
            {
                manifest = ReadLyrPackage(sourcePath);
            }
            catch (Exception ex)
            {
                return new LyricsTransferResult { Success = false, Message = $"解析失败: {ex.Message}" };
            }

            if (!string.Equals(manifest.Format, "canvas.lyr", StringComparison.OrdinalIgnoreCase))
            {
                return new LyricsTransferResult { Success = false, Message = "不支持的歌词包格式" };
            }

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
                            IsSystem = false,
                            CreatedTime = DateTime.Now
                        };
                        _dbContext.LyricsGroups.Add(existing);
                        await _dbContext.SaveChangesAsync();
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
                    catch
                    {
                        result.Failed++;
                    }
                }

                await _dbContext.SaveChangesAsync();
                await tx.CommitAsync();
                result.Success = true;
                result.Message = $"导入完成：新增{result.Imported}，覆盖{result.Overwritten}，副本{result.Copied}，跳过{result.Skipped}，失败{result.Failed}";
                return result;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new LyricsTransferResult { Success = false, Message = $"导入失败: {ex.Message}" };
            }
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
                FontSize = s.FontSize <= 0 ? 88 : s.FontSize,
                TextAlign = string.IsNullOrWhiteSpace(s.TextAlign) ? "Center" : s.TextAlign,
                ViewMode = s.ViewMode,
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
                Format = "canvas.lyr",
                SchemaVersion = 1,
                PackageType = packageType,
                ExportedAt = DateTime.UtcNow,
                AppVersion = "6.0.1.x",
                Groups = groups.Select(g => new LyricsGroupDto
                {
                    ExternalId = g.ExternalId,
                    Name = g.Name,
                    SortOrder = g.SortOrder
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
                    SourceType = s.SourceType,
                    SortOrder = s.SortOrder,
                    ModifiedTime = s.ModifiedTime
                }).ToList()
            };
        }

        private static void WriteLyrPackage(string targetPath, LyricsPackageManifest manifest)
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            using var fs = File.Create(targetPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var entry = zip.CreateEntry("manifest.json");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            writer.Write(json);
        }

        private static LyricsPackageManifest ReadLyrPackage(string sourcePath)
        {
            using var fs = File.OpenRead(sourcePath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry("manifest.json") ?? throw new InvalidOperationException("manifest.json 不存在");
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<LyricsPackageManifest>(json) ?? throw new InvalidOperationException("manifest.json 解析失败");
        }

        private sealed class LyricsPackageManifest
        {
            public string Format { get; set; } = "";
            public int SchemaVersion { get; set; }
            public string PackageType { get; set; } = "";
            public DateTime ExportedAt { get; set; }
            public string AppVersion { get; set; } = "";
            public List<LyricsGroupDto> Groups { get; set; } = new();
            public List<LyricsSongDto> Songs { get; set; } = new();
        }

        private sealed class LyricsGroupDto
        {
            public string ExternalId { get; set; } = "";
            public string Name { get; set; } = "";
            public int SortOrder { get; set; }
        }

        private sealed class LyricsSongDto
        {
            public string ExternalId { get; set; } = "";
            public string Name { get; set; } = "";
            public string GroupExternalId { get; set; } = "";
            public string Content { get; set; } = "";
            public double FontSize { get; set; }
            public string TextAlign { get; set; } = "";
            public int ViewMode { get; set; }
            public int SourceType { get; set; }
            public int SortOrder { get; set; }
            public DateTime? ModifiedTime { get; set; }
        }
    }
}
