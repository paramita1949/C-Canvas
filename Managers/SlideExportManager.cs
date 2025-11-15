using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System.Windows;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 幻灯片导出管理器
    /// 负责将幻灯片项目导出为.hdp格式文件
    /// </summary>
    public class SlideExportManager
    {
        private readonly DatabaseManager _dbManager;

        public SlideExportManager(DatabaseManager dbManager)
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
        /// 导出单个项目
        /// </summary>
        /// <param name="projectId">项目ID</param>
        /// <returns>是否成功</returns>
        public async Task<bool> ExportProjectAsync(int projectId)
        {
            try
            {
                var dbContext = GetDbContext();
                
                // 加载项目及其所有关联数据
                var project = await dbContext.TextProjects
                    .Include(p => p.Slides)
                        .ThenInclude(s => s.Elements)
                            .ThenInclude(e => e.RichTextSpans)
                    .FirstOrDefaultAsync(p => p.Id == projectId);

                if (project == null)
                {
                    System.Windows.MessageBox.Show("项目不存在", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return false;
                }

                // 创建导出数据对象
                var exportData = new SlideProjectExportData
                {
                    Version = "1.0",
                    ExportTime = DateTime.Now,
                    Projects = new List<TextProjectData> { CreateProjectData(project) }
                };

                // 选择保存路径
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "幻灯片项目文件 (*.hdp)|*.hdp",
                    FileName = $"{project.Name}.hdp",
                    Title = "导出幻灯片项目"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    // 序列化为JSON并保存
                    var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

                    await File.WriteAllTextAsync(saveFileDialog.FileName, json);
                    System.Windows.MessageBox.Show($"项目已导出: {Path.GetFileName(saveFileDialog.FileName)}",
                        "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导出失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 导出所有项目
        /// </summary>
        /// <returns>是否成功</returns>
        public async Task<bool> ExportAllProjectsAsync()
        {
            try
            {
                var dbContext = GetDbContext();
                
                // 加载所有项目及其关联数据
                var projects = await dbContext.TextProjects
                    .Include(p => p.Slides)
                        .ThenInclude(s => s.Elements)
                            .ThenInclude(e => e.RichTextSpans)
                    .ToListAsync();

                if (projects.Count == 0)
                {
                    System.Windows.MessageBox.Show("没有可导出的项目", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return false;
                }

                // 创建导出数据对象
                var exportData = new SlideProjectExportData
                {
                    Version = "1.0",
                    ExportTime = DateTime.Now,
                    Projects = projects.Select(p => CreateProjectData(p)).ToList()
                };

                // 选择保存路径
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "幻灯片项目文件 (*.hdp)|*.hdp",
                    FileName = $"所有项目_{DateTime.Now:yyyyMMdd_HHmmss}.hdp",
                    Title = "导出所有幻灯片项目"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    // 序列化为JSON并保存
                    var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

                    await File.WriteAllTextAsync(saveFileDialog.FileName, json);
                    System.Windows.MessageBox.Show($"已导出 {projects.Count} 个项目: {Path.GetFileName(saveFileDialog.FileName)}",
                        "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导出失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }
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
                    SplitMode = s.SplitMode,
                    SplitRegionsData = s.SplitRegionsData,
                    SplitStretchMode = s.SplitStretchMode,
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
                        FontFamily = e.FontFamily,
                        FontSize = e.FontSize,
                        FontColor = e.FontColor,
                        IsBold = e.IsBold,
                        IsItalic = e.IsItalic,
                        IsUnderline = e.IsUnderline,
                        TextAlign = e.TextAlign,
                        BackgroundColor = e.BackgroundColor,
                        BackgroundRadius = e.BackgroundRadius,
                        BackgroundOpacity = e.BackgroundOpacity,
                        BorderColor = e.BorderColor,
                        BorderWidth = e.BorderWidth,
                        BorderRadius = e.BorderRadius,
                        BorderOpacity = e.BorderOpacity,
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
        public int SplitMode { get; set; }
        public string SplitRegionsData { get; set; }
        public bool SplitStretchMode { get; set; }
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
        public string FontFamily { get; set; }
        public double FontSize { get; set; }
        public string FontColor { get; set; }
        public int IsBold { get; set; }
        public int IsItalic { get; set; }
        public int IsUnderline { get; set; }
        public string TextAlign { get; set; }
        public string BackgroundColor { get; set; }
        public double BackgroundRadius { get; set; }
        public int BackgroundOpacity { get; set; }
        public string BorderColor { get; set; }
        public double BorderWidth { get; set; }
        public double BorderRadius { get; set; }
        public int BorderOpacity { get; set; }
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

