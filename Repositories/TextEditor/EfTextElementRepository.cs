using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Repositories.TextEditor
{
    public sealed class EfTextElementRepository : ITextElementRepository
    {
        private readonly CanvasDbContext _dbContext;

        public EfTextElementRepository(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<TextElement> AddAsync(TextElement element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (element.ProjectId.HasValue)
            {
                bool exists = await _dbContext.TextProjects.AnyAsync(p => p.Id == element.ProjectId.Value);
                if (!exists)
                {
                    throw new InvalidOperationException($"项目不存在: ID={element.ProjectId}");
                }
            }
            else if (element.SlideId.HasValue)
            {
                bool exists = await _dbContext.Slides.AnyAsync(s => s.Id == element.SlideId.Value);
                if (!exists)
                {
                    throw new InvalidOperationException($"幻灯片不存在: ID={element.SlideId}");
                }
            }
            else
            {
                throw new InvalidOperationException("文本元素必须关联到项目或幻灯片");
            }

            _dbContext.TextElements.Add(element);
            await _dbContext.SaveChangesAsync();
            await TouchRelatedProjectByElementAsync(element);
            return element;
        }

        public async Task UpdateAsync(TextElement element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            var tracked = await _dbContext.TextElements
                .FirstOrDefaultAsync(e => e.Id == element.Id);
            if (tracked == null)
            {
                throw new InvalidOperationException($"元素不存在: ID={element.Id}");
            }

            _dbContext.Entry(tracked).CurrentValues.SetValues(element);
            await _dbContext.SaveChangesAsync();
            await TouchRelatedProjectByElementAsync(element);
        }

        public async Task UpdateRangeAsync(IEnumerable<TextElement> elements)
        {
            if (elements == null)
            {
                return;
            }

            var list = elements
                .Where(e => e != null && e.Id > 0)
                .ToList();
            if (list.Count == 0)
            {
                return;
            }

            var ids = list.Select(e => e.Id).Distinct().ToList();
            var trackedMap = await _dbContext.TextElements
                .Where(e => ids.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

            foreach (var source in list)
            {
                if (!trackedMap.TryGetValue(source.Id, out var tracked))
                {
                    continue;
                }

                _dbContext.Entry(tracked).CurrentValues.SetValues(source);
            }

            await _dbContext.SaveChangesAsync();

            var projectIds = new HashSet<int>();
            foreach (var element in list)
            {
                if (element.ProjectId.HasValue)
                {
                    projectIds.Add(element.ProjectId.Value);
                }
            }

            var slideIds = list.Where(e => e.SlideId.HasValue).Select(e => e.SlideId.Value).Distinct().ToList();
            if (slideIds.Count > 0)
            {
                var slideProjectIds = await _dbContext.Slides
                    .Where(s => slideIds.Contains(s.Id))
                    .Select(s => s.ProjectId)
                    .Distinct()
                    .ToListAsync();

                foreach (var projectId in slideProjectIds)
                {
                    projectIds.Add(projectId);
                }
            }

            await TouchProjectsAsync(projectIds);
        }

        public async Task DeleteAsync(int elementId)
        {
            var element = await _dbContext.TextElements.FindAsync(elementId);
            if (element == null)
            {
                throw new InvalidOperationException($"元素不存在: ID={elementId}");
            }

            int? projectId = element.ProjectId;
            int? slideId = element.SlideId;

            _dbContext.TextElements.Remove(element);
            await _dbContext.SaveChangesAsync();

            var touched = new HashSet<int>();
            if (projectId.HasValue)
            {
                touched.Add(projectId.Value);
            }

            if (slideId.HasValue)
            {
                var slideProjectId = await _dbContext.Slides
                    .Where(s => s.Id == slideId.Value)
                    .Select(s => (int?)s.ProjectId)
                    .FirstOrDefaultAsync();
                if (slideProjectId.HasValue)
                {
                    touched.Add(slideProjectId.Value);
                }
            }

            await TouchProjectsAsync(touched);
        }

        public async Task DeleteByProjectAsync(int projectId)
        {
            var elements = await _dbContext.TextElements
                .Where(e => e.ProjectId == projectId)
                .ToListAsync();
            if (elements.Count == 0)
            {
                return;
            }

            _dbContext.TextElements.RemoveRange(elements);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<TextElement>> GetByProjectAsync(int projectId)
        {
            return await _dbContext.TextElements
                .AsNoTracking()
                .Where(e => e.ProjectId == projectId)
                .OrderBy(e => e.ZIndex)
                .ToListAsync();
        }

        public async Task<List<TextElement>> GetBySlideWithRichTextAsync(int slideId)
        {
            var elements = await _dbContext.TextElements
                .AsNoTracking()
                .Include(e => e.RichTextSpans)
                .Where(e => e.SlideId == slideId)
                .OrderBy(e => e.ZIndex)
                .ToListAsync();

            foreach (var element in elements)
            {
                if (element.RichTextSpans != null && element.RichTextSpans.Count > 0)
                {
                    element.RichTextSpans = element.RichTextSpans.OrderBy(s => s.SpanOrder).ToList();
                }
            }

            return elements;
        }

        public async Task RebindProjectElementsToSlideAsync(int projectId, int targetSlideId)
        {
            var oldElements = await _dbContext.TextElements
                .Where(e => e.ProjectId == projectId && e.SlideId == null)
                .ToListAsync();

            if (oldElements.Count == 0)
            {
                return;
            }

            foreach (var element in oldElements)
            {
                element.SlideId = targetSlideId;
            }

            await _dbContext.SaveChangesAsync();
        }

        private async Task TouchRelatedProjectByElementAsync(TextElement element)
        {
            var projectIds = new HashSet<int>();
            if (element.ProjectId.HasValue)
            {
                projectIds.Add(element.ProjectId.Value);
            }

            if (element.SlideId.HasValue)
            {
                var slideProjectId = await _dbContext.Slides
                    .Where(s => s.Id == element.SlideId.Value)
                    .Select(s => (int?)s.ProjectId)
                    .FirstOrDefaultAsync();
                if (slideProjectId.HasValue)
                {
                    projectIds.Add(slideProjectId.Value);
                }
            }

            await TouchProjectsAsync(projectIds);
        }

        private async Task TouchProjectsAsync(IEnumerable<int> projectIds)
        {
            var ids = projectIds?.Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0)
            {
                return;
            }

            var projects = await _dbContext.TextProjects
                .Where(p => ids.Contains(p.Id))
                .ToListAsync();
            if (projects.Count == 0)
            {
                return;
            }

            var now = DateTime.Now;
            foreach (var project in projects)
            {
                project.ModifiedTime = now;
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}
