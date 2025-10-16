using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 文本项目管理器
    /// 负责文本项目的CRUD操作
    /// </summary>
    public class TextProjectManager
    {
        private readonly CanvasDbContext _dbContext;

        public TextProjectManager(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        #region 项目管理

        /// <summary>
        /// 创建新的文本项目
        /// </summary>
        /// <param name="name">项目名称</param>
        /// <param name="canvasWidth">画布宽度（默认1920）</param>
        /// <param name="canvasHeight">画布高度（默认1080）</param>
        /// <returns>创建的项目实体</returns>
        public async Task<TextProject> CreateProjectAsync(string name, int canvasWidth = 1920, int canvasHeight = 1080)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("项目名称不能为空", nameof(name));

            var project = new TextProject
            {
                Name = name.Trim(),
                CanvasWidth = canvasWidth,
                CanvasHeight = canvasHeight,
                CreatedTime = DateTime.Now,
                ModifiedTime = DateTime.Now
            };

            _dbContext.TextProjects.Add(project);
            await _dbContext.SaveChangesAsync();

            System.Diagnostics.Debug.WriteLine($"✅ 创建文本项目成功: ID={project.Id}, Name={project.Name}");
            return project;
        }

        /// <summary>
        /// 加载文本项目（包含所有元素）
        /// </summary>
        /// <param name="projectId">项目ID</param>
        /// <returns>项目实体（包含元素）</returns>
        public async Task<TextProject> LoadProjectAsync(int projectId)
        {
            var project = await _dbContext.TextProjects
                .Include(p => p.Elements.OrderBy(e => e.ZIndex))
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null)
                throw new InvalidOperationException($"项目不存在: ID={projectId}");

            System.Diagnostics.Debug.WriteLine($"✅ 加载文本项目成功: ID={project.Id}, Name={project.Name}, Elements={project.Elements.Count}");
            return project;
        }

        /// <summary>
        /// 获取所有项目（仅基本信息，不加载元素）
        /// </summary>
        /// <returns>项目列表</returns>
        public async Task<List<TextProject>> GetAllProjectsAsync()
        {
            return await _dbContext.TextProjects
                .OrderByDescending(p => p.ModifiedTime ?? p.CreatedTime)
                .ToListAsync();
        }

        /// <summary>
        /// 保存项目（更新修改时间）
        /// </summary>
        /// <param name="project">项目实体</param>
        public async Task SaveProjectAsync(TextProject project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            project.ModifiedTime = DateTime.Now;
            _dbContext.TextProjects.Update(project);
            await _dbContext.SaveChangesAsync();

            System.Diagnostics.Debug.WriteLine($"✅ 保存文本项目成功: ID={project.Id}, Name={project.Name}");
        }

        /// <summary>
        /// 删除项目（级联删除所有元素）
        /// </summary>
        /// <param name="projectId">项目ID</param>
        public async Task DeleteProjectAsync(int projectId)
        {
            var project = await _dbContext.TextProjects.FindAsync(projectId);
            if (project == null)
                throw new InvalidOperationException($"项目不存在: ID={projectId}");

            _dbContext.TextProjects.Remove(project);
            await _dbContext.SaveChangesAsync();

            System.Diagnostics.Debug.WriteLine($"✅ 删除文本项目成功: ID={projectId}");
        }

        /// <summary>
        /// 更新项目背景图
        /// </summary>
        /// <param name="projectId">项目ID</param>
        /// <param name="imagePath">背景图路径</param>
        public async Task UpdateBackgroundImageAsync(int projectId, string imagePath)
        {
            var project = await _dbContext.TextProjects.FindAsync(projectId);
            if (project == null)
                throw new InvalidOperationException($"项目不存在: ID={projectId}");

            project.BackgroundImagePath = imagePath;
            project.ModifiedTime = DateTime.Now;
            await _dbContext.SaveChangesAsync();

            System.Diagnostics.Debug.WriteLine($"✅ 更新背景图成功: ProjectID={projectId}, Path={imagePath}");
        }

        #endregion

        #region 元素管理

        /// <summary>
        /// 添加文本元素
        /// </summary>
        /// <param name="element">元素实体</param>
        /// <returns>添加后的元素（包含ID）</returns>
        public async Task<TextElement> AddElementAsync(TextElement element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            // 检查关联是否存在（ProjectId 或 SlideId 至少有一个）
            if (element.ProjectId.HasValue)
            {
                var projectExists = await _dbContext.TextProjects.AnyAsync(p => p.Id == element.ProjectId);
                if (!projectExists)
                    throw new InvalidOperationException($"项目不存在: ID={element.ProjectId}");
            }
            else if (element.SlideId.HasValue)
            {
                var slideExists = await _dbContext.Slides.AnyAsync(s => s.Id == element.SlideId);
                if (!slideExists)
                    throw new InvalidOperationException($"幻灯片不存在: ID={element.SlideId}");
            }
            else
            {
                throw new InvalidOperationException("文本元素必须关联到项目或幻灯片");
            }

            _dbContext.TextElements.Add(element);
            await _dbContext.SaveChangesAsync();

            // 更新项目修改时间
            if (element.ProjectId.HasValue)
                await UpdateProjectModifiedTimeAsync(element.ProjectId.Value);
            if (element.SlideId.HasValue)
            {
                var slide = await _dbContext.Slides.FindAsync(element.SlideId.Value);
                if (slide != null && slide.ProjectId > 0)
                    await UpdateProjectModifiedTimeAsync(slide.ProjectId);
            }

            System.Diagnostics.Debug.WriteLine($"✅ 添加文本元素成功: ID={element.Id}, ProjectID={element.ProjectId}, SlideID={element.SlideId}");
            return element;
        }

        /// <summary>
        /// 更新文本元素
        /// </summary>
        /// <param name="element">元素实体</param>
        public async Task UpdateElementAsync(TextElement element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            _dbContext.TextElements.Update(element);
            await _dbContext.SaveChangesAsync();

            // 更新项目修改时间
            if (element.ProjectId.HasValue)
                await UpdateProjectModifiedTimeAsync(element.ProjectId.Value);
            if (element.SlideId.HasValue)
            {
                var slide = await _dbContext.Slides.FindAsync(element.SlideId.Value);
                if (slide != null && slide.ProjectId > 0)
                    await UpdateProjectModifiedTimeAsync(slide.ProjectId);
            }

            System.Diagnostics.Debug.WriteLine($"✅ 更新文本元素成功: ID={element.Id}");
        }

        /// <summary>
        /// 批量更新文本元素（性能优化）
        /// </summary>
        /// <param name="elements">元素列表</param>
        public async Task UpdateElementsAsync(IEnumerable<TextElement> elements)
        {
            if (elements == null || !elements.Any())
                return;

            _dbContext.TextElements.UpdateRange(elements);
            await _dbContext.SaveChangesAsync();

            // 更新所有涉及项目的修改时间
            var projectIds = elements.Where(e => e.ProjectId.HasValue).Select(e => e.ProjectId.Value).Distinct();
            foreach (var projectId in projectIds)
            {
                await UpdateProjectModifiedTimeAsync(projectId);
            }
            
            // 更新涉及幻灯片的项目修改时间
            var slideIds = elements.Where(e => e.SlideId.HasValue).Select(e => e.SlideId.Value).Distinct();
            foreach (var slideId in slideIds)
            {
                var slide = await _dbContext.Slides.FindAsync(slideId);
                if (slide != null && slide.ProjectId > 0)
                    await UpdateProjectModifiedTimeAsync(slide.ProjectId);
            }

            System.Diagnostics.Debug.WriteLine($"✅ 批量更新文本元素成功: Count={elements.Count()}");
        }

        /// <summary>
        /// 删除文本元素
        /// </summary>
        /// <param name="elementId">元素ID</param>
        public async Task DeleteElementAsync(int elementId)
        {
            var element = await _dbContext.TextElements.FindAsync(elementId);
            if (element == null)
                throw new InvalidOperationException($"元素不存在: ID={elementId}");

            int? projectId = element.ProjectId;
            int? slideId = element.SlideId;

            _dbContext.TextElements.Remove(element);
            await _dbContext.SaveChangesAsync();

            // 更新项目修改时间
            if (projectId.HasValue)
                await UpdateProjectModifiedTimeAsync(projectId.Value);
            if (slideId.HasValue)
            {
                var slide = await _dbContext.Slides.FindAsync(slideId.Value);
                if (slide != null && slide.ProjectId > 0)
                    await UpdateProjectModifiedTimeAsync(slide.ProjectId);
            }

            System.Diagnostics.Debug.WriteLine($"✅ 删除文本元素成功: ID={elementId}");
        }

        /// <summary>
        /// 删除项目的所有元素
        /// </summary>
        /// <param name="projectId">项目ID</param>
        public async Task DeleteAllElementsAsync(int projectId)
        {
            var elements = await _dbContext.TextElements
                .Where(e => e.ProjectId == projectId)
                .ToListAsync();

            if (elements.Any())
            {
                _dbContext.TextElements.RemoveRange(elements);
                await _dbContext.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"✅ 删除所有文本元素成功: ProjectID={projectId}, Count={elements.Count}");
            }
        }

        /// <summary>
        /// 获取项目的所有元素（按ZIndex排序）
        /// </summary>
        /// <param name="projectId">项目ID</param>
        /// <returns>元素列表</returns>
        public async Task<List<TextElement>> GetElementsByProjectAsync(int projectId)
        {
            return await _dbContext.TextElements
                .Where(e => e.ProjectId == projectId)
                .OrderBy(e => e.ZIndex)
                .ToListAsync();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 更新项目修改时间
        /// </summary>
        private async Task UpdateProjectModifiedTimeAsync(int projectId)
        {
            var project = await _dbContext.TextProjects.FindAsync(projectId);
            if (project != null)
            {
                project.ModifiedTime = DateTime.Now;
                await _dbContext.SaveChangesAsync();
            }
        }

        /// <summary>
        /// 克隆文本元素（用于复制、对称等）
        /// </summary>
        /// <param name="source">源元素</param>
        /// <returns>克隆的元素（未保存到数据库）</returns>
        public TextElement CloneElement(TextElement source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return new TextElement
            {
                ProjectId = source.ProjectId,
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height,
                ZIndex = source.ZIndex,
                Content = source.Content,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                FontColor = source.FontColor,
                IsBold = source.IsBold,
                TextAlign = source.TextAlign
            };
        }

        #endregion
    }
}

