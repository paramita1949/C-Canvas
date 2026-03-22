using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 幻灯片导出管理器
    /// 负责将幻灯片项目导出为.hdp格式文件
    /// </summary>
    public class SlideExportManager
    {
        private const string PackageManifestEntryName = "manifest.json";
        private const string SlidePackageFormat = "canvas.hdp";
        private const string SlidePackageVersion = "2.0";

        private static readonly JsonSerializerOptions PackageJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly CanvasDbContext _dbContext;
        public string LastError { get; private set; }

        public SlideExportManager(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        /// <summary>
        /// 导出单个项目
        /// </summary>
        /// <param name="projectId">项目ID</param>
        /// <returns>是否成功</returns>
        public async Task<bool> ExportProjectAsync(int projectId)
        {
            LastError = null;
            try
            {
                var dbContext = _dbContext;
                
                // 加载项目及其所有关联数据
                var project = await dbContext.TextProjects
                    .Include(p => p.Slides)
                        .ThenInclude(s => s.Elements)
                            .ThenInclude(e => e.RichTextSpans)
                    .FirstOrDefaultAsync(p => p.Id == projectId);

                if (project == null)
                {
                    LastError = "项目不存在";
                    return false;
                }

                bool ok = await ExportProjectsToPackageAsync(
                    new List<TextProject> { project },
                    $"{project.Name}.hdp",
                    "导出幻灯片项目");
                return ok;
            }
            catch (Exception ex)
            {
                LastError = $"导出失败: [{ex.GetType().Name}] {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 导出所有项目
        /// </summary>
        /// <returns>是否成功</returns>
        public async Task<bool> ExportAllProjectsAsync()
        {
            LastError = null;
            try
            {
                var dbContext = _dbContext;
                
                // 加载所有项目及其关联数据
                var projects = await dbContext.TextProjects
                    .Include(p => p.Slides)
                        .ThenInclude(s => s.Elements)
                            .ThenInclude(e => e.RichTextSpans)
                    .ToListAsync();

                if (projects.Count == 0)
                {
                    LastError = "没有可导出的项目";
                    return false;
                }

                bool ok = await ExportProjectsToPackageAsync(
                    projects,
                    $"所有项目_{DateTime.Now:yyyyMMdd_HHmmss}.hdp",
                    "导出所有幻灯片项目");
                return ok;
            }
            catch (Exception ex)
            {
                LastError = $"导出失败: [{ex.GetType().Name}] {ex.Message}";
                return false;
            }
        }

        private async Task<bool> ExportProjectsToPackageAsync(List<TextProject> projects, string defaultFileName, string dialogTitle)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "幻灯片项目文件 (*.hdp)|*.hdp",
                FileName = defaultFileName,
                Title = dialogTitle
            };

            if (saveFileDialog.ShowDialog() != true)
            {
                return false;
            }

            var exportData = CreateExportData(projects);
            await WritePackageAsync(saveFileDialog.FileName, exportData);
            return true;
        }

        private SlideProjectExportData CreateExportData(List<TextProject> projects)
        {
            return new SlideProjectExportData
            {
                Format = SlidePackageFormat,
                Version = SlidePackageVersion,
                ExportTime = DateTime.Now,
                Projects = projects.Select(CreateProjectData).ToList()
            };
        }

        private async Task WritePackageAsync(string targetPath, SlideProjectExportData exportData)
        {
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string tempPath = Path.Combine(
                string.IsNullOrWhiteSpace(targetDir) ? Path.GetTempPath() : targetDir,
                $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (var fs = File.Create(tempPath))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var sourceToEntryPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var usedEntryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    SlidePackagePathMapper.RemapPaths(exportData, path => MapPathToPackageEntry(path, zip, sourceToEntryPath, usedEntryPaths));

                    var manifestEntry = zip.CreateEntry(PackageManifestEntryName);
                    using var manifestStream = manifestEntry.Open();
                    using var writer = new StreamWriter(manifestStream);
                    string json = JsonSerializer.Serialize(exportData, PackageJsonOptions);
                    await writer.WriteAsync(json);
                }

                ValidatePackageFile(tempPath);

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(tempPath, targetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void ValidatePackageFile(string packagePath)
        {
            using var fs = File.OpenRead(packagePath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            if (zip.Entries.Count == 0)
            {
                throw new InvalidOperationException("导出失败：生成的幻灯片包为空。");
            }

            var manifestEntry = zip.GetEntry(PackageManifestEntryName);
            if (manifestEntry == null)
            {
                throw new InvalidOperationException("导出失败：生成的幻灯片包缺少 manifest.json。");
            }

            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            var manifest = JsonSerializer.Deserialize<SlideProjectExportData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest?.Projects == null || manifest.Projects.Count == 0)
            {
                throw new InvalidOperationException("导出失败：manifest 中没有有效项目数据。");
            }
        }

        private string MapPathToPackageEntry(
            string rawPath,
            ZipArchive zip,
            Dictionary<string, string> sourceToEntryPath,
            HashSet<string> usedEntryPaths)
        {
            string sourcePath = ResolveExistingPath(rawPath);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return rawPath;
            }

            if (sourceToEntryPath.TryGetValue(sourcePath, out var mappedEntryPath))
            {
                return mappedEntryPath;
            }

            string entryPath = BuildUniqueAssetEntryPath(sourcePath, usedEntryPaths);
            zip.CreateEntryFromFile(sourcePath, entryPath, CompressionLevel.Optimal);
            sourceToEntryPath[sourcePath] = entryPath;
            return entryPath;
        }

        private static string ResolveExistingPath(string rawPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    return null;
                }

                string normalized = rawPath.Trim();

                if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && uri.IsFile)
                {
                    normalized = uri.LocalPath;
                }

                if (Path.IsPathRooted(normalized))
                {
                    return File.Exists(normalized) ? Path.GetFullPath(normalized) : null;
                }

                string fromBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, normalized);
                if (File.Exists(fromBaseDir))
                {
                    return Path.GetFullPath(fromBaseDir);
                }

                string fromCurrentDir = Path.GetFullPath(normalized);
                return File.Exists(fromCurrentDir) ? fromCurrentDir : null;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildUniqueAssetEntryPath(string sourcePath, HashSet<string> usedEntryPaths)
        {
            string baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "asset";
            }

            string ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".bin";
            }

            string candidate = $"assets/{baseName}{ext}";
            int suffix = 1;
            while (usedEntryPaths.Contains(candidate))
            {
                candidate = $"assets/{baseName}_{suffix}{ext}";
                suffix++;
            }

            usedEntryPaths.Add(candidate);
            return candidate;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "asset";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (invalidChars.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        /// <summary>
        /// 获取幻灯片缩略图的Base64编码
        /// </summary>
        private string GetThumbnailBase64(int slideId)
        {
            try
            {
                var thumbnailDir = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "Thumbnails");
                var thumbnailPath = Path.Combine(thumbnailDir, $"slide_{slideId}.png");

                if (File.Exists(thumbnailPath))
                {
                    var imageBytes = File.ReadAllBytes(thumbnailPath);
                    return Convert.ToBase64String(imageBytes);
                }
            }
            catch
            {
                // 忽略缩略图读取错误
            }
            return null;
        }

        /// <summary>
        /// 创建项目数据对象（用于序列化）
        /// </summary>
        private TextProjectData CreateProjectData(TextProject project)
        {
            return new TextProjectData
            {
                Name = project.Name,
                BackgroundImagePath = project.BackgroundImagePath,
                CanvasWidth = project.CanvasWidth,
                CanvasHeight = project.CanvasHeight,
                CreatedTime = project.CreatedTime,
                ModifiedTime = project.ModifiedTime,
                Slides = project.Slides?.OrderBy(s => s.SortOrder).Select(s => new SlideData
                {
                    Title = s.Title,
                    SortOrder = s.SortOrder,
                    BackgroundImagePath = s.BackgroundImagePath,
                    BackgroundColor = s.BackgroundColor,
                    BackgroundGradientEnabled = s.BackgroundGradientEnabled,
                    BackgroundGradientStartColor = s.BackgroundGradientStartColor,
                    BackgroundGradientEndColor = s.BackgroundGradientEndColor,
                    BackgroundGradientDirection = s.BackgroundGradientDirection,
                    BackgroundOpacity = s.BackgroundOpacity,
                    SplitMode = s.SplitMode,
                    SplitRegionsData = s.SplitRegionsData,
                    SplitStretchMode = s.SplitStretchMode,
                    OutputMode = s.OutputMode,
                    VideoBackgroundEnabled = s.VideoBackgroundEnabled,
                    VideoLoopEnabled = s.VideoLoopEnabled,
                    VideoVolume = s.VideoVolume,
                    CreatedTime = s.CreatedTime,
                    ModifiedTime = s.ModifiedTime,
                    ThumbnailBase64 = GetThumbnailBase64(s.Id),
                    Elements = s.Elements?.OrderBy(e => e.ZIndex).Select(e => new TextElementData
                    {
                        X = e.X,
                        Y = e.Y,
                        Width = e.Width,
                        Height = e.Height,
                        ZIndex = e.ZIndex,
                        Content = e.Content,
                        ComponentType = e.ComponentType,
                        ComponentConfigJson = e.ComponentConfigJson,
                        FontFamily = e.FontFamily,
                        FontSize = e.FontSize,
                        FontColor = e.FontColor,
                        IsBold = e.IsBold,
                        IsItalic = e.IsItalic,
                        IsUnderline = e.IsUnderline,
                        TextAlign = e.TextAlign,
                        TextVerticalAlign = e.TextVerticalAlign,
                        BackgroundColor = e.BackgroundColor,
                        BackgroundRadius = e.BackgroundRadius,
                        BackgroundOpacity = e.BackgroundOpacity,
                        BorderColor = e.BorderColor,
                        BorderWidth = e.BorderWidth,
                        BorderRadius = e.BorderRadius,
                        BorderOpacity = e.BorderOpacity,
                        ShadowType = e.ShadowType,
                        ShadowPreset = e.ShadowPreset,
                        ShadowColor = e.ShadowColor,
                        ShadowOffsetX = e.ShadowOffsetX,
                        ShadowOffsetY = e.ShadowOffsetY,
                        ShadowBlur = e.ShadowBlur,
                        ShadowOpacity = e.ShadowOpacity,
                        LineSpacing = e.LineSpacing,
                        LetterSpacing = e.LetterSpacing,
                        IsSymmetric = e.IsSymmetric,
                        SymmetricPairId = e.SymmetricPairId,
                        SymmetricType = e.SymmetricType,
                        RichTextSpans = e.RichTextSpans?.OrderBy(r => r.SpanOrder).Select(r => new RichTextSpanData
                        {
                            SpanOrder = r.SpanOrder,
                            Text = r.Text,
                            ParagraphIndex = r.ParagraphIndex,
                            RunIndex = r.RunIndex,
                            FormatVersion = r.FormatVersion,
                            FontFamily = r.FontFamily,
                            FontSize = r.FontSize,
                            FontColor = r.FontColor,
                            IsBold = r.IsBold,
                            IsItalic = r.IsItalic,
                            IsUnderline = r.IsUnderline,
                            BorderColor = r.BorderColor,
                            BorderWidth = r.BorderWidth,
                            BorderRadius = r.BorderRadius,
                            BorderOpacity = r.BorderOpacity,
                            BackgroundColor = r.BackgroundColor,
                            BackgroundRadius = r.BackgroundRadius,
                            BackgroundOpacity = r.BackgroundOpacity,
                            ShadowColor = r.ShadowColor,
                            ShadowOffsetX = r.ShadowOffsetX,
                            ShadowOffsetY = r.ShadowOffsetY,
                            ShadowBlur = r.ShadowBlur,
                            ShadowOpacity = r.ShadowOpacity
                        }).ToList()
                    }).ToList()
                }).ToList()
            };
        }
    }

    #region 导出数据模型

    /// <summary>
    /// 幻灯片项目导出数据根对象
    /// </summary>
    public class SlideProjectExportData
    {
        public string Format { get; set; }
        public string Version { get; set; }
        public DateTime ExportTime { get; set; }
        public List<TextProjectData> Projects { get; set; }
    }

    /// <summary>
    /// 文本项目数据
    /// </summary>
    public class TextProjectData
    {
        public string Name { get; set; }
        public string BackgroundImagePath { get; set; }
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime? ModifiedTime { get; set; }
        public List<SlideData> Slides { get; set; }
    }

    /// <summary>
    /// 幻灯片数据
    /// </summary>
    public class SlideData
    {
        public string Title { get; set; }
        public int SortOrder { get; set; }
        public string BackgroundImagePath { get; set; }
        public string BackgroundColor { get; set; }
        public bool BackgroundGradientEnabled { get; set; } = false;
        public string BackgroundGradientStartColor { get; set; }
        public string BackgroundGradientEndColor { get; set; }
        public int BackgroundGradientDirection { get; set; } = 1;
        public int BackgroundOpacity { get; set; } = 0;
        public int SplitMode { get; set; }
        public string SplitRegionsData { get; set; }
        [JsonConverter(typeof(LegacySplitImageDisplayModeJsonConverter))]
        public SplitImageDisplayMode SplitStretchMode { get; set; } = SplitImageDisplayMode.FitCenter;
        public Database.Models.Enums.SlideOutputMode OutputMode { get; set; } = Database.Models.Enums.SlideOutputMode.Normal;
        public bool VideoBackgroundEnabled { get; set; } = false;
        public bool VideoLoopEnabled { get; set; } = true;
        public double VideoVolume { get; set; } = 0.0;
        public DateTime CreatedTime { get; set; }
        public DateTime? ModifiedTime { get; set; }
        public string ThumbnailBase64 { get; set; }
        public List<TextElementData> Elements { get; set; }
    }

    /// <summary>
    /// 文本元素数据
    /// </summary>
    public class TextElementData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int ZIndex { get; set; }
        public string Content { get; set; }
        public string ComponentType { get; set; }
        public string ComponentConfigJson { get; set; }
        public string FontFamily { get; set; }
        public double FontSize { get; set; }
        public string FontColor { get; set; }
        public int IsBold { get; set; }
        public int IsItalic { get; set; }
        public int IsUnderline { get; set; }
        public string TextAlign { get; set; }
        public string TextVerticalAlign { get; set; } = "Top";
        public string BackgroundColor { get; set; }
        public double BackgroundRadius { get; set; }
        public int BackgroundOpacity { get; set; }
        public string BorderColor { get; set; }
        public double BorderWidth { get; set; }
        public double BorderRadius { get; set; }
        public int BorderOpacity { get; set; }
        public int ShadowType { get; set; }
        public int ShadowPreset { get; set; }
        public string ShadowColor { get; set; }
        public double ShadowOffsetX { get; set; }
        public double ShadowOffsetY { get; set; }
        public double ShadowBlur { get; set; }
        public int ShadowOpacity { get; set; }
        public double LineSpacing { get; set; }
        public double LetterSpacing { get; set; }
        public int IsSymmetric { get; set; }
        public int? SymmetricPairId { get; set; }
        public string SymmetricType { get; set; }
        public List<RichTextSpanData> RichTextSpans { get; set; }
    }

    /// <summary>
    /// 富文本片段数据
    /// </summary>
    public class RichTextSpanData
    {
        public int SpanOrder { get; set; }
        public string Text { get; set; }
        public int? ParagraphIndex { get; set; }
        public int? RunIndex { get; set; }
        public string FormatVersion { get; set; }
        public string FontFamily { get; set; }
        public double? FontSize { get; set; }
        public string FontColor { get; set; }
        public int IsBold { get; set; }
        public int IsItalic { get; set; }
        public int IsUnderline { get; set; }
        public string BorderColor { get; set; }
        public double? BorderWidth { get; set; }
        public double? BorderRadius { get; set; }
        public int? BorderOpacity { get; set; }
        public string BackgroundColor { get; set; }
        public double? BackgroundRadius { get; set; }
        public int? BackgroundOpacity { get; set; }
        public string ShadowColor { get; set; }
        public double? ShadowOffsetX { get; set; }
        public double? ShadowOffsetY { get; set; }
        public double? ShadowBlur { get; set; }
        public int? ShadowOpacity { get; set; }
    }

    #endregion
}

