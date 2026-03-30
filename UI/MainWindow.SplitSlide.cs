using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.DTOs;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Services.TextEditor.Application;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的分割图创建部分
    /// 用于在原图模式下快速创建分割幻灯片到赞美诗项目
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 常量定义

        /// <summary>
        /// 赞美诗项目的固定名称
        /// </summary>
        private const string PRAISE_PROJECT_NAME = "赞美诗";

        #endregion

        #region 分割图创建

        /// <summary>
        /// 从文件ID创建分割幻灯片（用于右键菜单）
        /// </summary>
        private async Task CreateSplitSlideInPraiseProjectFromFile(int fileId)
        {
            try
            {
                // 1. 查找相似图片
                bool foundSimilar = _originalManager.FindSimilarImages(fileId);
                if (!foundSimilar)
                {
                    ShowStatus("没有找到相似图片");
                    return;
                }

                // 2. 获取相似图片列表
                var similarImages = _originalManager.GetSimilarImages();
                int count = similarImages.Count;

                // 3. 验证数量（1-4张）
                if (count < 1)
                {
                    ShowStatus("没有相似图片");
                    return;
                }

                if (count > 4)
                {
                    ShowStatus($"相似图片过多（{count}张），仅支持1-4张");
                    return;
                }

                // 4. 确定分割模式
                ViewSplitMode splitMode = count switch
                {
                    1 => ViewSplitMode.Single,
                    2 => ViewSplitMode.Horizontal,  // 左右分割
                    3 => ViewSplitMode.TripleSplit, // 三分割
                    4 => ViewSplitMode.Quad,        // 四宫格
                    _ => ViewSplitMode.Single
                };

                // 5. 查找或创建"赞美诗"项目
                var praiseProject = await FindOrCreatePraiseProjectAsync();
                if (praiseProject == null)
                {
                    ShowStatus("创建赞美诗项目失败");
                    return;
                }

                // 6. 仅替换第一张幻灯片（不存在则创建），不再清空整个项目。
                var newSlide = await UpsertFirstSplitSlideAsync(praiseProject.Id, splitMode, similarImages);
                if (newSlide == null)
                {
                    ShowStatus("创建幻灯片失败");
                    return;
                }

                // 8. 自动打开新创建的幻灯片
                await OpenSlideAsync(praiseProject, newSlide);

                // 9. 显示成功提示
                string modeName = count switch
                {
                    1 => "单画面",
                    2 => "2分割",
                    3 => "3分割",
                    4 => "4分割",
                    _ => "分割"
                };
                ShowStatus($"已创建{modeName}幻灯片（{count}张图片）");
            }
            catch (Exception ex)
            {
                ShowStatus($"创建分割图失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在赞美诗项目中创建分割幻灯片（用于中间图片显示区域的右键菜单）
        /// </summary>
        private async Task CreateSplitSlideInPraiseProject()
        {
            try
            {
                // 1. 获取相似图片列表
                var similarImages = _originalManager.GetSimilarImages();
                int count = similarImages.Count;

                // 2. 验证数量（1-4张）
                if (count < 1)
                {
                    ShowStatus("没有相似图片");
                    return;
                }

                if (count > 4)
                {
                    ShowStatus($"相似图片过多（{count}张），仅支持1-4张");
                    return;
                }

                // 3. 确定分割模式
                ViewSplitMode splitMode = count switch
                {
                    1 => ViewSplitMode.Single,
                    2 => ViewSplitMode.Horizontal,  // 左右分割
                    3 => ViewSplitMode.TripleSplit, // 三分割
                    4 => ViewSplitMode.Quad,        // 四宫格
                    _ => ViewSplitMode.Single
                };

                // 4. 查找或创建"赞美诗"项目
                var praiseProject = await FindOrCreatePraiseProjectAsync();
                if (praiseProject == null)
                {
                    ShowStatus("创建赞美诗项目失败");
                    return;
                }

                // 5. 仅替换第一张幻灯片（不存在则创建），不再清空整个项目。
                var newSlide = await UpsertFirstSplitSlideAsync(praiseProject.Id, splitMode, similarImages);
                if (newSlide == null)
                {
                    ShowStatus("创建幻灯片失败");
                    return;
                }

                // 7. 自动打开新创建的幻灯片
                await OpenSlideAsync(praiseProject, newSlide);

                // 8. 显示成功提示
                string modeName = count switch
                {
                    1 => "单画面",
                    2 => "2分割",
                    3 => "3分割",
                    4 => "4分割",
                    _ => "分割"
                };
                ShowStatus($"已创建{modeName}幻灯片（{count}张图片）");
            }
            catch (Exception ex)
            {
                ShowStatus($"创建分割图失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 查找或创建"赞美诗"项目
        /// </summary>
        private async Task<TextProject> FindOrCreatePraiseProjectAsync()
        {
            try
            {
                if (_textProjectService == null)
                {
                    _textProjectService = _mainWindowServices.GetRequired<ITextProjectService>();
                }

                var allProjects = await _textProjectService.GetAllProjectsAsync();
                var praiseProject = allProjects.FirstOrDefault(p => p.Name == PRAISE_PROJECT_NAME);

                if (praiseProject != null)
                {
                    return praiseProject;
                }

                praiseProject = await _textProjectService.CreateProjectAsync(PRAISE_PROJECT_NAME);

                if (praiseProject != null)
                {
                    _projectTreeItems.Add(new ProjectTreeItem
                    {
                        Id = praiseProject.Id,
                        Name = praiseProject.Name,
                        Icon = "FileDocument",
                        IconKind = "FileDocument",
                        IconColor = "#2196F3",
                        Type = TreeItemType.TextProject,
                        Path = null
                    });
                    FilterProjectTree();
                }

                return praiseProject;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 创建或更新第一张分割幻灯片
        /// </summary>
        private async Task<Slide> UpsertFirstSplitSlideAsync(int projectId, ViewSplitMode splitMode, List<(int id, string name, string path)> similarImages)
        {
            try
            {
                var regionDataList = new List<SplitRegionData>();
                for (int i = 0; i < similarImages.Count; i++)
                {
                    regionDataList.Add(new SplitRegionData
                    {
                        RegionIndex = i,
                        ImagePath = similarImages[i].path
                    });
                }

                string splitRegionsJson = JsonSerializer.Serialize(regionDataList);

                var existingSlides = await _textProjectService.GetSlidesByProjectAsync(projectId);
                var firstSlide = existingSlides
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.Id)
                    .FirstOrDefault();

                if (firstSlide == null)
                {
                    var newSlide = new Slide
                    {
                        ProjectId = projectId,
                        Title = "幻灯片 1",
                        SortOrder = 1,
                        BackgroundColor = GetCurrentSlideThemeBackgroundColorHex(),
                        SplitMode = (int)splitMode,
                        SplitRegionsData = splitRegionsJson,
                        SplitStretchMode = _splitImageDisplayModePreference,
                        CreatedTime = DateTime.Now,
                        ModifiedTime = DateTime.Now
                    };

                    await _textProjectService.AddSlideAsync(newSlide);
                    return newSlide;
                }

                firstSlide.BackgroundColor = GetCurrentSlideThemeBackgroundColorHex();
                firstSlide.BackgroundImagePath = null;
                firstSlide.SplitMode = (int)splitMode;
                firstSlide.SplitRegionsData = splitRegionsJson;
                firstSlide.SplitStretchMode = _splitImageDisplayModePreference;
                firstSlide.ModifiedTime = DateTime.Now;

                await _textProjectService.UpdateSlideAsync(firstSlide);
                return firstSlide;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 从文件ID添加单画面幻灯片到赞美诗项目（用于右键菜单）
        /// </summary>
        private async Task AddSingleSlideToPraiseProjectFromFile(int fileId)
        {
            try
            {
                var mediaFile = DatabaseManagerService.GetMediaFileById(fileId);
                if (mediaFile == null)
                {
                    ShowStatus("文件不存在");
                    return;
                }

                var praiseProject = await FindOrCreatePraiseProjectAsync();
                if (praiseProject == null)
                {
                    ShowStatus("创建赞美诗项目失败");
                    return;
                }

                var newSlide = await CreateSingleSlideAsync(praiseProject.Id, mediaFile.Path, mediaFile.Name);
                if (newSlide == null)
                {
                    ShowStatus("创建幻灯片失败");
                    return;
                }

                var thumbnailPath = SaveSlideThumbnailFromImagePath(newSlide.Id, mediaFile.Path);
                if (!string.IsNullOrWhiteSpace(thumbnailPath))
                {
                    newSlide.ThumbnailPath = thumbnailPath;
                }

                // 静默添加：不要切换当前视图或加载新幻灯片，避免打断当前显示内容。
                if (TextEditorPanel.Visibility == Visibility.Visible &&
                    _currentTextProject?.Id == praiseProject.Id)
                {
                    await LoadSlideList();
                }

                ShowStatus($"已添加幻灯片: {mediaFile.Name}");
            }
            catch (Exception ex)
            {
                ShowStatus($"添加幻灯片失败: {ex.Message}");
            }
        }

        private string SaveSlideThumbnailFromImagePath(int slideId, string imagePath)
        {
            try
            {
                if (slideId <= 0 || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    return null;
                }

                var thumbnailDir = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "Thumbnails");
                Directory.CreateDirectory(thumbnailDir);

                var thumbnailPath = Path.Combine(thumbnailDir, $"slide_{slideId}.png");

                BitmapFrame sourceFrame;
                using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    sourceFrame = decoder.Frames.FirstOrDefault();
                }

                if (sourceFrame == null || sourceFrame.PixelWidth <= 0 || sourceFrame.PixelHeight <= 0)
                {
                    return null;
                }

                const double maxWidth = 320.0;
                const double maxHeight = 180.0;
                double scaleX = maxWidth / sourceFrame.PixelWidth;
                double scaleY = maxHeight / sourceFrame.PixelHeight;
                double scale = Math.Min(1.0, Math.Min(scaleX, scaleY));

                BitmapSource output = sourceFrame;
                if (scale < 1.0)
                {
                    var resized = new TransformedBitmap(sourceFrame, new ScaleTransform(scale, scale));
                    resized.Freeze();
                    output = resized;
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(output));
                using (var fileStream = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(fileStream);
                }

                return thumbnailPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 从单个文件创建单画面幻灯片
        /// </summary>
        private async Task<Slide> CreateSingleSlideAsync(int projectId, string filePath, string fileName)
        {
            try
            {
                // 1. 获取项目中的幻灯片数量，用于计算SortOrder
                var slideCount = await _textProjectService.GetSlideCountAsync(projectId);

                // 2. 创建分割区域数据（单画面）
                var regionDataList = new List<SplitRegionData>
                {
                    new SplitRegionData
                    {
                        RegionIndex = 0,
                        ImagePath = filePath
                    }
                };

                // 3. 序列化为JSON
                string splitRegionsJson = JsonSerializer.Serialize(regionDataList);

                // 4. 创建幻灯片
                var newSlide = new Slide
                {
                    ProjectId = projectId,
                    Title = $"幻灯片 {slideCount + 1}",
                    SortOrder = slideCount + 1,
                    BackgroundColor = GetCurrentSlideThemeBackgroundColorHex(),
                    SplitMode = (int)ViewSplitMode.Single,  // 单画面
                    SplitRegionsData = splitRegionsJson,
                    SplitStretchMode = _splitImageDisplayModePreference,  // 使用全局分割显示偏好
                    CreatedTime = DateTime.Now,
                    ModifiedTime = DateTime.Now
                };

                await _textProjectService.AddSlideAsync(newSlide);

                return newSlide;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 自动打开幻灯片
        /// </summary>
        private async Task OpenSlideAsync(TextProject project, Slide slide)
        {
            try
            {
                ResetViewStateForTextEditor();
                _currentTextProject = project;
                ShowTextEditor();
                await LoadSlideList();

                _currentSlide = slide;
                SlideListBox.SelectedItem = slide;
                await LoadSlide(slide);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (_currentSlide != null && _currentSlide.Id == slide.Id)
                    {
                        SaveSlideThumbnail(slide.Id);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                await LoadSlideList();
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}
