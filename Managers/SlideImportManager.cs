using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.Win32;
using System.Windows;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 幻灯片导入管理器
    /// 负责从.hdp格式文件导入幻灯片项目
    /// </summary>
    public class SlideImportManager
    {
        private readonly DatabaseManager _dbManager;

        public SlideImportManager(DatabaseManager dbManager)
        {
            _dbManager = dbManager ?? throw new ArgumentNullException(nameof(dbManager));
        }

        /// <summary>
        /// 获取 DbContext
        /// </summary>
        private CanvasDbContext GetDbContext()
        {
            return _dbManager.GetDbContext();
        }

        /// <summary>
        /// 导入幻灯片项目文件
        /// </summary>
        /// <returns>导入的项目数量</returns>
        public async Task<int> ImportProjectsAsync()
        {
            try
            {
                // 选择要导入的文件
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "幻灯片项目文件 (*.hdp)|*.hdp",
                    Title = "导入幻灯片项目"
                };

                if (openFileDialog.ShowDialog() != true)
                    return 0;

                // 读取并解析JSON文件
                var json = await File.ReadAllTextAsync(openFileDialog.FileName);
                var exportData = JsonSerializer.Deserialize<SlideProjectExportData>(json);

                if (exportData == null || exportData.Projects == null || exportData.Projects.Count == 0)
                {
                    System.Windows.MessageBox.Show("文件格式无效或没有项目数据", "错误",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return 0;
                }

                var dbContext = GetDbContext();
                int importedCount = 0;

                // 导入每个项目
                foreach (var projectData in exportData.Projects)
                {
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
                                SplitMode = slideData.SplitMode,
                                SplitRegionsData = slideData.SplitRegionsData,
                                SplitStretchMode = slideData.SplitStretchMode,
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
                                        FontFamily = elementData.FontFamily,
                                        FontSize = elementData.FontSize,
                                        FontColor = elementData.FontColor,
                                        IsBold = elementData.IsBold,
                                        IsItalic = elementData.IsItalic,
                                        IsUnderline = elementData.IsUnderline,
                                        TextAlign = elementData.TextAlign,
                                        BackgroundColor = elementData.BackgroundColor,
                                        BackgroundRadius = elementData.BackgroundRadius,
                                        BackgroundOpacity = elementData.BackgroundOpacity,
                                        BorderColor = elementData.BorderColor,
                                        BorderWidth = elementData.BorderWidth,
                                        BorderRadius = elementData.BorderRadius,
                                        BorderOpacity = elementData.BorderOpacity,
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
                }

                System.Windows.MessageBox.Show($"成功导入 {importedCount} 个项目", "导入成功",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                return importedCount;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导入失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return 0;
            }
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


