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
        private readonly DatabaseManager _dbManager;

        public TextProjectManager(DatabaseManager dbManager)
        {
            _dbManager = dbManager ?? throw new ArgumentNullException(nameof(dbManager));
        }

        /// <summary>
        /// 获取 DbContext（每次操作时动态获取，避免使用已释放的上下文）
        /// </summary>
        private CanvasDbContext GetDbContext()
        {
            return _dbManager.GetDbContext();
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

            var dbContext = GetDbContext();
            dbContext.TextProjects.Add(project);
            await dbContext.SaveChangesAsync();

            //System.Diagnostics.Debug.WriteLine($"✅ 创建文本项目成功: ID={project.Id}, Name={project.Name}");
            return project;
        }

        /// <summary>
        /// 加载文本项目（包含所有元素和富文本片段）
        /// </summary>
        /// <param name="projectId">项目ID</param>
        /// <returns>项目实体（包含元素和富文本片段）</returns>
        public async Task<TextProject> LoadProjectAsync(int projectId)
        {
            var dbContext = GetDbContext();
            var project = await dbContext.TextProjects
                .Include(p => p.Elements)
                    .ThenInclude(e => e.RichTextSpans)  // 🔧 加载富文本片段
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null)
                throw new InvalidOperationException($"项目不存在: ID={projectId}");

            // 🔧 手动排序（EF Core 不支持在 Include 中使用 OrderBy）
            if (project.Elements != null)
            {
                project.Elements = project.Elements.OrderBy(e => e.ZIndex).ToList();
                foreach (var element in project.Elements)
                {
                    if (element.RichTextSpans != null && element.RichTextSpans.Count > 0)
                    {
                        element.RichTextSpans = element.RichTextSpans.OrderBy(s => s.SpanOrder).ToList();
                    }
                }
            }

//#if DEBUG
//            int totalSpans = project.Elements.Sum(e => e.RichTextSpans?.Count ?? 0);
//            System.Diagnostics.Debug.WriteLine($"✅ [加载项目] ID={project.Id}, Name={project.Name}, Elements={project.Elements.Count}, RichTextSpans={totalSpans}");
//#endif
            return project;
        }

        /// <summary>
        /// 获取所有项目（仅基本信息，不加载元素）
        /// </summary>
        /// <returns>项目列表</returns>
        public async Task<List<TextProject>> GetAllProjectsAsync()
        {
            var dbContext = GetDbContext();
            return await dbContext.TextProjects
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
            var dbContext = GetDbContext();
            dbContext.TextProjects.Update(project);
            await dbContext.SaveChangesAsync();

            //System.Diagnostics.Debug.WriteLine($"✅ 保存文本项目成功: ID={project.Id}, Name={project.Name}");
        }

        /// <summary>
        /// 删除项目（级联删除所有元素）
        /// </summary>
        /// <param name="projectId">项目ID</param>
        public async Task DeleteProjectAsync(int projectId)
        {
            var dbContext = GetDbContext();
            var project = await dbContext.TextProjects.FindAsync(projectId);
            if (project == null)
                throw new InvalidOperationException($"项目不存在: ID={projectId}");

            dbContext.TextProjects.Remove(project);
            await dbContext.SaveChangesAsync();

            //System.Diagnostics.Debug.WriteLine($"✅ 删除文本项目成功: ID={projectId}");
        }

        /// <summary>
        /// 更新项目背景图
        /// </summary>
        /// <param name="projectId">项目ID</param>
        /// <param name="imagePath">背景图路径</param>
        public async Task UpdateBackgroundImageAsync(int projectId, string imagePath)
        {
            var dbContext = GetDbContext();
            var project = await dbContext.TextProjects.FindAsync(projectId);
            if (project == null)
                throw new InvalidOperationException($"项目不存在: ID={projectId}");

            project.BackgroundImagePath = imagePath;
            project.ModifiedTime = DateTime.Now;
            await dbContext.SaveChangesAsync();

            //System.Diagnostics.Debug.WriteLine($"✅ 更新背景图成功: ProjectID={projectId}, Path={imagePath}");
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

            var dbContext = GetDbContext();

            // 检查关联是否存在（ProjectId 或 SlideId 至少有一个）
            if (element.ProjectId.HasValue)
            {
                var projectExists = await dbContext.TextProjects.AnyAsync(p => p.Id == element.ProjectId);
                if (!projectExists)
                    throw new InvalidOperationException($"项目不存在: ID={element.ProjectId}");
            }
            else if (element.SlideId.HasValue)
            {
                var slideExists = await dbContext.Slides.AnyAsync(s => s.Id == element.SlideId);
                if (!slideExists)
                    throw new InvalidOperationException($"幻灯片不存在: ID={element.SlideId}");
            }
            else
            {
                throw new InvalidOperationException("文本元素必须关联到项目或幻灯片");
            }

            dbContext.TextElements.Add(element);
            await dbContext.SaveChangesAsync();

            // 更新项目修改时间
            if (element.ProjectId.HasValue)
                await UpdateProjectModifiedTimeAsync(element.ProjectId.Value);
            if (element.SlideId.HasValue)
            {
                var slide = await dbContext.Slides.FindAsync(element.SlideId.Value);
                if (slide != null && slide.ProjectId > 0)
                    await UpdateProjectModifiedTimeAsync(slide.ProjectId);
            }

            //System.Diagnostics.Debug.WriteLine($"✅ 添加文本元素成功: ID={element.Id}, ProjectID={element.ProjectId}, SlideID={element.SlideId}");
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

            var dbContext = GetDbContext();
            dbContext.TextElements.Update(element);
            await dbContext.SaveChangesAsync();

            // 更新项目修改时间
            if (element.ProjectId.HasValue)
                await UpdateProjectModifiedTimeAsync(element.ProjectId.Value);
            if (element.SlideId.HasValue)
            {
                var slide = await dbContext.Slides.FindAsync(element.SlideId.Value);
                if (slide != null && slide.ProjectId > 0)
                    await UpdateProjectModifiedTimeAsync(slide.ProjectId);
            }

            //System.Diagnostics.Debug.WriteLine($"✅ 更新文本元素成功: ID={element.Id}");
        }

        /// <summary>
        /// 批量更新文本元素（性能优化）
        /// </summary>
        /// <param name="elements">元素列表</param>
        public async Task UpdateElementsAsync(IEnumerable<TextElement> elements)
        {
            if (elements == null || !elements.Any())
                return;

            var dbContext = GetDbContext();
            dbContext.TextElements.UpdateRange(elements);
            await dbContext.SaveChangesAsync();

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
                var slide = await dbContext.Slides.FindAsync(slideId);
                if (slide != null && slide.ProjectId > 0)
                    await UpdateProjectModifiedTimeAsync(slide.ProjectId);
            }
        }

        /// <summary>
        /// 添加富文本片段
        /// </summary>
        /// <param name="span">富文本片段实体</param>
        /// <returns>添加后的片段（包含ID）</returns>
        public async Task<RichTextSpan> AddRichTextSpanAsync(RichTextSpan span)
        {
            if (span == null)
                throw new ArgumentNullException(nameof(span));

            var dbContext = GetDbContext();
            dbContext.RichTextSpans.Add(span);
            await dbContext.SaveChangesAsync();

            return span;
        }

        /// <summary>
        /// 删除文本元素的所有富文本片段
        /// </summary>
        /// <param name="textElementId">文本元素ID</param>
        public async Task DeleteRichTextSpansByElementIdAsync(int textElementId)
        {
            var dbContext = GetDbContext();
            var spans = await dbContext.RichTextSpans
                .Where(s => s.TextElementId == textElementId)
                .ToListAsync();

            if (spans.Any())
            {
                dbContext.RichTextSpans.RemoveRange(spans);
                await dbContext.SaveChangesAsync();
            }
        }

        /// <summary>
        /// 批量保存富文本片段（先删除旧的，再添加新的）
        /// </summary>
        /// <param name="textElementId">文本元素ID</param>
        /// <param name="spans">新的富文本片段列表</param>
        public async Task SaveRichTextSpansAsync(int textElementId, List<RichTextSpan> spans)
        {
            if (spans == null)
                throw new ArgumentNullException(nameof(spans));

            var dbContext = GetDbContext();

            // 删除旧的片段
            var oldSpans = await dbContext.RichTextSpans
                .Where(s => s.TextElementId == textElementId)
                .ToListAsync();

            if (oldSpans.Any())
            {
                dbContext.RichTextSpans.RemoveRange(oldSpans);
            }

            // 添加新的片段
            if (spans.Any())
            {
                dbContext.RichTextSpans.AddRange(spans);
            }

            await dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// 删除文本元素
        /// </summary>
        /// <param name="elementId">元素ID</param>
        public async Task DeleteElementAsync(int elementId)
        {
            var dbContext = GetDbContext();
            var element = await dbContext.TextElements.FindAsync(elementId);
            if (element == null)
                throw new InvalidOperationException($"元素不存在: ID={elementId}");

            int? projectId = element.ProjectId;
            int? slideId = element.SlideId;

            dbContext.TextElements.Remove(element);
            await dbContext.SaveChangesAsync();

            // 更新项目修改时间
            if (projectId.HasValue)
                await UpdateProjectModifiedTimeAsync(projectId.Value);
            if (slideId.HasValue)
            {
                var slide = await dbContext.Slides.FindAsync(slideId.Value);
                if (slide != null && slide.ProjectId > 0)
                    await UpdateProjectModifiedTimeAsync(slide.ProjectId);
            }

            //System.Diagnostics.Debug.WriteLine($"✅ 删除文本元素成功: ID={elementId}");
        }

        /// <summary>
        /// 删除项目的所有元素
        /// </summary>
        /// <param name="projectId">项目ID</param>
        public async Task DeleteAllElementsAsync(int projectId)
        {
            var dbContext = GetDbContext();
            var elements = await dbContext.TextElements
                .Where(e => e.ProjectId == projectId)
                .ToListAsync();

            if (elements.Any())
            {
                dbContext.TextElements.RemoveRange(elements);
                await dbContext.SaveChangesAsync();

                //System.Diagnostics.Debug.WriteLine($"✅ 删除所有文本元素成功: ProjectID={projectId}, Count={elements.Count}");
            }
        }

        /// <summary>
        /// 获取项目的所有元素（按ZIndex排序）
        /// </summary>
        /// <param name="projectId">项目ID</param>
        /// <returns>元素列表</returns>
        public async Task<List<TextElement>> GetElementsByProjectAsync(int projectId)
        {
            var dbContext = GetDbContext();
            return await dbContext.TextElements
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
            var dbContext = GetDbContext();
            var project = await dbContext.TextProjects.FindAsync(projectId);
            if (project != null)
            {
                project.ModifiedTime = DateTime.Now;
                await dbContext.SaveChangesAsync();
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

