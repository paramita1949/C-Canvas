using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Repositories.TextEditor
{
    public sealed class EfTextProjectRepository : ITextProjectRepository
    {
        private readonly CanvasDbContext _dbContext;

        public EfTextProjectRepository(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<TextProject> CreateAsync(string name, int canvasWidth = 1920, int canvasHeight = 1080)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("项目名称不能为空", nameof(name));
            }

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
            return project;
        }

        public async Task<TextProject> LoadWithElementsAndRichTextAsync(int projectId)
        {
            var project = await _dbContext.TextProjects
                .AsNoTracking()
                .Include(p => p.Elements)
                .ThenInclude(e => e.RichTextSpans)
                .FirstOrDefaultAsync(p => p.Id == projectId);

            if (project == null)
            {
                throw new InvalidOperationException($"项目不存在: ID={projectId}");
            }

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

            return project;
        }

        public async Task<TextProject> GetByIdAsync(int projectId)
        {
            return await _dbContext.TextProjects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId);
        }

        public async Task<List<TextProject>> GetAllAsync()
        {
            return await _dbContext.TextProjects
                .AsNoTracking()
                .OrderBy(p => p.SortOrder)
                .ThenByDescending(p => p.ModifiedTime ?? p.CreatedTime)
                .ToListAsync();
        }

        public async Task SaveAsync(TextProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            var tracked = await _dbContext.TextProjects
                .FirstOrDefaultAsync(p => p.Id == project.Id);
            if (tracked == null)
            {
                throw new InvalidOperationException($"项目不存在: ID={project.Id}");
            }

            _dbContext.Entry(tracked).CurrentValues.SetValues(project);
            tracked.ModifiedTime = DateTime.Now;
            await _dbContext.SaveChangesAsync();
        }

        public async Task DeleteAsync(int projectId)
        {
            var project = await _dbContext.TextProjects.FindAsync(projectId);
            if (project == null)
            {
                throw new InvalidOperationException($"项目不存在: ID={projectId}");
            }

            _dbContext.TextProjects.Remove(project);
            await _dbContext.SaveChangesAsync();
        }

        public async Task UpdateBackgroundImageAsync(int projectId, string imagePath)
        {
            var project = await _dbContext.TextProjects.FindAsync(projectId);
            if (project == null)
            {
                throw new InvalidOperationException($"项目不存在: ID={projectId}");
            }

            project.BackgroundImagePath = imagePath;
            project.ModifiedTime = DateTime.Now;
            await _dbContext.SaveChangesAsync();
        }

        public async Task UpdateModifiedTimeAsync(int projectId)
        {
            var project = await _dbContext.TextProjects.FindAsync(projectId);
            if (project == null)
            {
                return;
            }

            project.ModifiedTime = DateTime.Now;
            await _dbContext.SaveChangesAsync();
        }
    }
}
