using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.DTOs;
using ImageColorChanger.Database.Models.Enums;
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
                    ShowStatus("❌ 没有找到相似图片");
                    return;
                }

                // 2. 获取相似图片列表
                var similarImages = _originalManager.GetSimilarImages();
                int count = similarImages.Count;

                // 3. 验证数量（1-4张）
                if (count < 1)
                {
                    ShowStatus("❌ 没有相似图片");
                    return;
                }

                if (count > 4)
                {
                    ShowStatus($"❌ 相似图片过多（{count}张），仅支持1-4张");
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
                    ShowStatus("❌ 创建赞美诗项目失败");
                    return;
                }

                // 6. 删除项目中的所有旧幻灯片
                await ClearProjectSlidesAsync(praiseProject.Id);

                // 7. 创建新幻灯片
                var newSlide = await CreateSplitSlideAsync(praiseProject.Id, splitMode, similarImages);
                if (newSlide == null)
                {
                    ShowStatus("❌ 创建幻灯片失败");
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
                ShowStatus($"✅ 已创建{modeName}幻灯片（{count}张图片）");
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 创建分割图失败: {ex.Message}");
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
                    ShowStatus("❌ 没有相似图片");
                    return;
                }

                if (count > 4)
                {
                    ShowStatus($"❌ 相似图片过多（{count}张），仅支持1-4张");
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
                    ShowStatus("❌ 创建赞美诗项目失败");
                    return;
                }

                // 5. 删除项目中的所有旧幻灯片
                await ClearProjectSlidesAsync(praiseProject.Id);

                // 6. 创建新幻灯片
                var newSlide = await CreateSplitSlideAsync(praiseProject.Id, splitMode, similarImages);
                if (newSlide == null)
                {
                    ShowStatus("❌ 创建幻灯片失败");
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
                ShowStatus($"✅ 已创建{modeName}幻灯片（{count}张图片）");
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 创建分割图失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 查找或创建"赞美诗"项目
        /// </summary>
        private async Task<TextProject> FindOrCreatePraiseProjectAsync()
        {
            try
            {
                if (_textProjectManager == null)
                {
                    if (_dbManager == null)
                    {
                        return null;
                    }
                    _textProjectManager = new Managers.TextProjectManager(_dbManager);
                }

                var allProjects = await _textProjectManager.GetAllProjectsAsync();
                var praiseProject = allProjects.FirstOrDefault(p => p.Name == PRAISE_PROJECT_NAME);

                if (praiseProject != null)
                {
                    return praiseProject;
                }

                praiseProject = await _textProjectManager.CreateProjectAsync(PRAISE_PROJECT_NAME);
                
                if (praiseProject != null)
                {
                    LoadProjects();
                }
                
                return praiseProject;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 清空项目中的所有幻灯片
        /// </summary>
        private async Task ClearProjectSlidesAsync(int projectId)
        {
            try
            {
                var slides = await _dbContext.Slides
                    .Where(s => s.ProjectId == projectId)
                    .ToListAsync();

                if (slides.Any())
                {
                    _dbContext.Slides.RemoveRange(slides);
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 创建分割幻灯片
        /// </summary>
        private async Task<Slide> CreateSplitSlideAsync(int projectId, ViewSplitMode splitMode, List<(int id, string name, string path)> similarImages)
        {
            try
            {
                // 1. 创建分割区域数据
                var regionDataList = new List<SplitRegionData>();
                for (int i = 0; i < similarImages.Count; i++)
                {
                    regionDataList.Add(new SplitRegionData
                    {
                        RegionIndex = i,
                        ImagePath = similarImages[i].path
                    });
                }

                // 2. 序列化为JSON
                string splitRegionsJson = JsonSerializer.Serialize(regionDataList);

                // 3. 创建幻灯片
                var newSlide = new Slide
                {
                    ProjectId = projectId,
                    Title = "幻灯片 1",
                    SortOrder = 1,
                    BackgroundColor = "#000000",  // 黑色背景
                    SplitMode = (int)splitMode,
                    SplitRegionsData = splitRegionsJson,
                    SplitStretchMode = true,  // 默认拉伸模式
                    CreatedTime = DateTime.Now,
                    ModifiedTime = DateTime.Now
                };

                _dbContext.Slides.Add(newSlide);
                await _dbContext.SaveChangesAsync();

                return newSlide;
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
                var mediaFile = _dbManager.GetMediaFileById(fileId);
                if (mediaFile == null)
                {
                    ShowStatus("❌ 文件不存在");
                    return;
                }

                var praiseProject = await FindOrCreatePraiseProjectAsync();
                if (praiseProject == null)
                {
                    ShowStatus("❌ 创建赞美诗项目失败");
                    return;
                }

                var newSlide = await CreateSingleSlideAsync(praiseProject.Id, mediaFile.Path, mediaFile.Name);
                if (newSlide == null)
                {
                    ShowStatus("❌ 创建幻灯片失败");
                    return;
                }

                await OpenSlideAsync(praiseProject, newSlide);
                ShowStatus($"✅ 已添加幻灯片: {mediaFile.Name}");
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 添加幻灯片失败: {ex.Message}");
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
                var slideCount = await _dbContext.Slides
                    .Where(s => s.ProjectId == projectId)
                    .CountAsync();

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
                    BackgroundColor = "#000000",  // 黑色背景
                    SplitMode = (int)ViewSplitMode.Single,  // 单画面
                    SplitRegionsData = splitRegionsJson,
                    SplitStretchMode = true,  // 默认拉伸模式
                    CreatedTime = DateTime.Now,
                    ModifiedTime = DateTime.Now
                };

                _dbContext.Slides.Add(newSlide);
                await _dbContext.SaveChangesAsync();

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
                LoadSlideList();

                await Dispatcher.InvokeAsync(() =>
                {
                    _currentSlide = slide;
                    SlideListBox.SelectedItem = slide;
                    LoadSlide(slide);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (_currentSlide != null && _currentSlide.Id == slide.Id)
                    {
                        SaveSlideThumbnail(slide.Id);
                        LoadSlideList();
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}

