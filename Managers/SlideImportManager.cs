using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.Win32;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 幻灯片导入管理器
    /// 负责从.hdp格式文件导入幻灯片项目
    /// </summary>
    public class SlideImportManager
    {
        private const string PackageManifestEntryName = "manifest.json";
        private const string ImportLogPrefix = "[幻灯片导入]";

        private static readonly JsonSerializerOptions ImportJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly CanvasDbContext _dbContext;
        public string LastError { get; private set; }

        public SlideImportManager(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        /// <summary>
        /// 导入幻灯片项目文件
        /// </summary>
        /// <returns>导入的项目数量</returns>
        public async Task<int> ImportProjectsAsync()
        {
            LastError = null;
            try
            {
                LogInfo("[Import-Begin] open file dialog");

                // 选择要导入的文件
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "幻灯片项目文件 (*.hdp;*.zip)|*.hdp;*.zip|幻灯片项目文件 (*.hdp)|*.hdp|ZIP文件 (*.zip)|*.zip",
                    Title = "导入幻灯片项目"
                };

                if (openFileDialog.ShowDialog() != true)
                {
                    LogInfo("[Import-Cancel] user canceled file dialog");
                    return 0;
                }

                var selectedPath = openFileDialog.FileName;
                var selectedFileInfo = new FileInfo(selectedPath);
                LogInfo($"[Import-Select] file={selectedPath}, bytes={(selectedFileInfo.Exists ? selectedFileInfo.Length : 0)}");

                var exportData = await ReadExportDataAsync(selectedPath);

                if (exportData == null || exportData.Projects == null || exportData.Projects.Count == 0)
                {
                    LastError = "文件格式无效或没有项目数据";
                    LogError($"[Import-Invalid] {LastError}");
                    return 0;
                }

                LogInfo($"[Import-Parsed] format={exportData.Format ?? "(null)"}, version={exportData.Version ?? "(null)"}, projects={exportData.Projects.Count}");

                var dbContext = _dbContext;
                int importedCount = 0;

                // 导入每个项目
                foreach (var projectData in exportData.Projects)
                {
                    LogInfo($"[Import-Project-Begin] name={projectData?.Name ?? "(null)"}, slides={projectData?.Slides?.Count ?? 0}");

                    // 检查项目名称是否已存在，如果存在则添加后缀
                    var existingNames = dbContext.TextProjects
                        .Select(p => p.Name)
                        .ToList();
                    
                    string projectName = projectData.Name;
                    int suffix = 1;
                    while (existingNames.Contains(projectName))
                    {
                        projectName = $"{projectData.Name} ({suffix})";
                        suffix++;
                    }

                    // 创建新项目
                    var newProject = new TextProject
                    {
                        Name = projectName,
                        BackgroundImagePath = projectData.BackgroundImagePath,
                        CanvasWidth = projectData.CanvasWidth,
                        CanvasHeight = projectData.CanvasHeight,
                        CreatedTime = DateTime.Now,
                        ModifiedTime = DateTime.Now
                    };

                    dbContext.TextProjects.Add(newProject);
                    await dbContext.SaveChangesAsync();

                    // 导入幻灯片
                    if (projectData.Slides != null)
                    {
                        foreach (var slideData in projectData.Slides)
                        {
                            var newSlide = new Slide
                            {
                                ProjectId = newProject.Id,
                                Title = slideData.Title,
                                SortOrder = slideData.SortOrder,
                                BackgroundImagePath = slideData.BackgroundImagePath,
                                BackgroundColor = slideData.BackgroundColor,
                                BackgroundGradientEnabled = slideData.BackgroundGradientEnabled,
                                BackgroundGradientStartColor = slideData.BackgroundGradientStartColor,
                                BackgroundGradientEndColor = slideData.BackgroundGradientEndColor,
                                BackgroundGradientDirection = slideData.BackgroundGradientDirection,
                                BackgroundOpacity = slideData.BackgroundOpacity,
                                SplitMode = slideData.SplitMode,
                                SplitRegionsData = slideData.SplitRegionsData,
                                SplitStretchMode = slideData.SplitStretchMode,
                                OutputMode = slideData.OutputMode,
                                // 视频背景相关属性（向后兼容：如果字段不存在，使用默认值）
                                VideoBackgroundEnabled = slideData.VideoBackgroundEnabled,
                                VideoLoopEnabled = slideData.VideoLoopEnabled,
                                VideoVolume = slideData.VideoVolume,
                                CreatedTime = DateTime.Now,
                                ModifiedTime = DateTime.Now
                            };

                            dbContext.Slides.Add(newSlide);
                            await dbContext.SaveChangesAsync();

                            // 保存缩略图
                            if (!string.IsNullOrEmpty(slideData.ThumbnailBase64))
                            {
                                SaveThumbnailFromBase64(newSlide.Id, slideData.ThumbnailBase64);
                            }

                            // 导入文本元素
                            if (slideData.Elements != null)
                            {
                                // 用于映射旧的对称伙伴ID到新的ID
                                var symmetricPairMapping = new Dictionary<int?, int>();

                                foreach (var elementData in slideData.Elements)
                                {
                                    var newElement = new TextElement
                                    {
                                        SlideId = newSlide.Id,
                                        X = elementData.X,
                                        Y = elementData.Y,
                                        Width = elementData.Width,
                                        Height = elementData.Height,
                                        ZIndex = elementData.ZIndex,
                                        Content = elementData.Content,
                                        ComponentType = elementData.ComponentType ?? string.Empty,
                                        ComponentConfigJson = elementData.ComponentConfigJson ?? string.Empty,
                                        FontFamily = elementData.FontFamily,
                                        FontSize = elementData.FontSize,
                                        FontColor = elementData.FontColor,
                                        IsBold = elementData.IsBold,
                                        IsItalic = elementData.IsItalic,
                                        IsUnderline = elementData.IsUnderline,
                                        TextAlign = elementData.TextAlign,
                                        TextVerticalAlign = string.IsNullOrWhiteSpace(elementData.TextVerticalAlign) ? "Top" : elementData.TextVerticalAlign,
                                        BackgroundColor = elementData.BackgroundColor,
                                        BackgroundRadius = elementData.BackgroundRadius,
                                        BackgroundOpacity = elementData.BackgroundOpacity,
                                        BorderColor = elementData.BorderColor,
                                        BorderWidth = elementData.BorderWidth,
                                        BorderRadius = elementData.BorderRadius,
                                        BorderOpacity = elementData.BorderOpacity,
                                        ShadowType = elementData.ShadowType,
                                        ShadowPreset = elementData.ShadowPreset,
                                        ShadowColor = elementData.ShadowColor,
                                        ShadowOffsetX = elementData.ShadowOffsetX,
                                        ShadowOffsetY = elementData.ShadowOffsetY,
                                        ShadowBlur = elementData.ShadowBlur,
                                        ShadowOpacity = elementData.ShadowOpacity,
                                        LineSpacing = elementData.LineSpacing,
                                        LetterSpacing = elementData.LetterSpacing,
                                        IsSymmetric = elementData.IsSymmetric
                                    };

                                    dbContext.TextElements.Add(newElement);
                                    await dbContext.SaveChangesAsync();

                                    // 记录对称伙伴映射
                                    if (elementData.SymmetricPairId.HasValue)
                                    {
                                        symmetricPairMapping[elementData.SymmetricPairId] = newElement.Id;
                                    }

                                    // 导入富文本片段
                                    if (elementData.RichTextSpans != null)
                                    {
                                        foreach (var spanData in elementData.RichTextSpans)
                                        {
                                            var newSpan = new RichTextSpan
                                            {
                                                TextElementId = newElement.Id,
                                                SpanOrder = spanData.SpanOrder,
                                                Text = spanData.Text,
                                                ParagraphIndex = spanData.ParagraphIndex,
                                                RunIndex = spanData.RunIndex,
                                                FormatVersion = spanData.FormatVersion,
                                                FontFamily = spanData.FontFamily,
                                                FontSize = spanData.FontSize,
                                                FontColor = spanData.FontColor,
                                                IsBold = spanData.IsBold,
                                                IsItalic = spanData.IsItalic,
                                                IsUnderline = spanData.IsUnderline,
                                                BorderColor = spanData.BorderColor,
                                                BorderWidth = spanData.BorderWidth,
                                                BorderRadius = spanData.BorderRadius,
                                                BorderOpacity = spanData.BorderOpacity,
                                                BackgroundColor = spanData.BackgroundColor,
                                                BackgroundRadius = spanData.BackgroundRadius,
                                                BackgroundOpacity = spanData.BackgroundOpacity,
                                                ShadowColor = spanData.ShadowColor,
                                                ShadowOffsetX = spanData.ShadowOffsetX,
                                                ShadowOffsetY = spanData.ShadowOffsetY,
                                                ShadowBlur = spanData.ShadowBlur,
                                                ShadowOpacity = spanData.ShadowOpacity
                                            };

                                            dbContext.RichTextSpans.Add(newSpan);
                                        }
                                    }
                                }

                                await dbContext.SaveChangesAsync();

                                // 更新对称伙伴ID映射
                                foreach (var elementData in slideData.Elements)
                                {
                                    if (elementData.SymmetricPairId.HasValue &&
                                        symmetricPairMapping.ContainsKey(elementData.SymmetricPairId))
                                    {
                                        var element = dbContext.TextElements
                                            .FirstOrDefault(e => e.SlideId == newSlide.Id &&
                                                               e.ZIndex == elementData.ZIndex);

                                        if (element != null)
                                        {
                                            element.SymmetricPairId = symmetricPairMapping[elementData.SymmetricPairId];
                                            element.SymmetricType = elementData.SymmetricType;
                                        }
                                    }
                                }

                                await dbContext.SaveChangesAsync();
                            }
                        }
                    }

                    importedCount++;
                    LogInfo($"[Import-Project-End] name={projectName}, importedSlides={projectData?.Slides?.Count ?? 0}");
                }

                LogInfo($"[Import-End] importedProjects={importedCount}");
                return importedCount;
            }
            catch (Exception ex)
            {
                LastError = $"导入失败: [{ex.GetType().Name}] {ex.Message}";
                LogError($"[Import-Fail] {BuildExceptionDetails(ex)}");
                return 0;
            }
        }

        private async Task<SlideProjectExportData> ReadExportDataAsync(string sourcePath)
        {
            bool isZip = IsZipPackage(sourcePath);
            LogInfo($"[ReadData] path={sourcePath}, isZip={isZip}");

            if (isZip)
            {
                try
                {
                    var zipData = await ReadExportDataFromZipAsync(sourcePath);
                    if (IsValidExportData(zipData))
                    {
                        return zipData;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is JsonException)
                {
                    LogError($"[ReadData-ZipFallback] {ex.GetType().Name}: {ex.Message}");
                }
            }

            var jsonData = await TryReadExportDataFromJsonAsync(sourcePath);
            if (IsValidExportData(jsonData))
            {
                LogInfo("[ReadData] fallback json parse succeeded");
                return jsonData;
            }

            if (isZip)
            {
                throw new InvalidOperationException("幻灯片包为空或已损坏（缺少有效项目数据），请在源电脑重新导出后再导入。");
            }

            throw new InvalidOperationException("文件格式无效或文件内容为空。");
        }

        private async Task<SlideProjectExportData> ReadExportDataFromZipAsync(string sourcePath)
        {
            using var fs = File.OpenRead(sourcePath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            LogInfo($"[ReadZip] entries={zip.Entries.Count}, source={sourcePath}");

            if (zip.Entries.Count == 0)
            {
                throw new InvalidOperationException("压缩包为空（未包含任何文件）");
            }

            var manifestEntry = zip.GetEntry(PackageManifestEntryName);
            if (manifestEntry == null)
            {
                LogError("[ReadZip] manifest.json not found");
                throw new InvalidOperationException("压缩包缺少清单文件（manifest.json）");
            }

            string json;
            using (var stream = manifestEntry.Open())
            using (var reader = new StreamReader(stream))
            {
                json = await reader.ReadToEndAsync();
            }
            LogInfo($"[ReadZip] manifestLength={json?.Length ?? 0}");

            var exportData = JsonSerializer.Deserialize<SlideProjectExportData>(json, ImportJsonOptions);
            if (exportData == null)
            {
                LogError("[ReadZip] manifest deserialize returned null");
                return null;
            }

            string importAssetRoot = CreateImportAssetDirectory();
            LogInfo($"[ReadZip] importAssetRoot={importAssetRoot}");
            var extractedPathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SlidePackagePathMapper.RemapPaths(exportData, path => ExtractAssetPath(path, zip, importAssetRoot, extractedPathMap));
            LogInfo($"[ReadZip] extractedAssetCount={extractedPathMap.Count}");

            return exportData;
        }

        private async Task<SlideProjectExportData> TryReadExportDataFromJsonAsync(string sourcePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(sourcePath);
                LogInfo($"[ReadData-Json] jsonLength={json?.Length ?? 0}");
                return JsonSerializer.Deserialize<SlideProjectExportData>(json, ImportJsonOptions);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                LogError($"[ReadData-JsonFail] {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static bool IsValidExportData(SlideProjectExportData exportData)
        {
            return exportData != null &&
                   exportData.Projects != null &&
                   exportData.Projects.Count > 0;
        }

        private static bool IsZipPackage(string sourcePath)
        {
            try
            {
                using var fs = File.OpenRead(sourcePath);
                if (fs.Length < 4)
                {
                    return false;
                }

                Span<byte> header = stackalloc byte[4];
                int read = fs.Read(header);
                return read == 4 &&
                       header[0] == 0x50 &&
                       header[1] == 0x4B &&
                       (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07) &&
                       (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);
            }
            catch
            {
                return false;
            }
        }

        private static string ExtractAssetPath(
            string rawPath,
            ZipArchive zip,
            string importAssetRoot,
            Dictionary<string, string> extractedPathMap)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return rawPath;
            }

            string packagePath = NormalizePackagePath(rawPath);
            if (!packagePath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                return rawPath;
            }

            if (extractedPathMap.TryGetValue(packagePath, out var cachedPath))
            {
                return cachedPath;
            }

            var entry = zip.GetEntry(packagePath);
            if (entry == null)
            {
                LogError($"[ExtractAsset-Missing] path={packagePath}");
                return rawPath;
            }

            string fileName = SanitizeFileName(Path.GetFileName(packagePath));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "asset.bin";
            }

            string targetPath = Path.Combine(importAssetRoot, fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".bin";
            }

            int suffix = 1;
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(importAssetRoot, $"{stem}_{suffix}{ext}");
                suffix++;
            }

            using (var inStream = entry.Open())
            using (var outStream = File.Create(targetPath))
            {
                inStream.CopyTo(outStream);
            }

            string fullPath = Path.GetFullPath(targetPath);
            extractedPathMap[packagePath] = fullPath;
            LogInfo($"[ExtractAsset] packagePath={packagePath}, target={fullPath}");
            return fullPath;
        }

        private static string CreateImportAssetDirectory()
        {
            string dir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "data",
                "slide-assets",
                $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string NormalizePackagePath(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "asset.bin";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = fileName.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (invalidChars.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogInfo(string message)
        {
            System.Diagnostics.Debug.WriteLine($"{ImportLogPrefix} {message}");
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"{ImportLogPrefix} [ERROR] {message}");
        }

        private static string BuildExceptionDetails(Exception ex)
        {
            if (ex == null)
            {
                return "(null)";
            }

            var details = ex.ToString();
            var inner = ex.InnerException;
            int depth = 1;
            while (inner != null && depth <= 5)
            {
                details += $"{Environment.NewLine}[Inner#{depth}] {inner}";
                inner = inner.InnerException;
                depth++;
            }

            return details;
        }

        /// <summary>
        /// 从Base64字符串保存缩略图
        /// </summary>
        private void SaveThumbnailFromBase64(int slideId, string base64String)
        {
            try
            {
                var imageBytes = Convert.FromBase64String(base64String);

                var thumbnailDir = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "Thumbnails");

                if (!Directory.Exists(thumbnailDir))
                    Directory.CreateDirectory(thumbnailDir);

                var thumbnailPath = Path.Combine(thumbnailDir, $"slide_{slideId}.png");
                File.WriteAllBytes(thumbnailPath, imageBytes);
            }
            catch
            {
                // 忽略缩略图保存错误
            }
        }
    }
}


