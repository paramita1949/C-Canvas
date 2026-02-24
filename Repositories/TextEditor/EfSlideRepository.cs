using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Repositories.TextEditor
{
    public sealed class EfSlideRepository : ISlideRepository
    {
        private readonly CanvasDbContext _dbContext;

        public EfSlideRepository(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<bool> ProjectHasSlidesAsync(int projectId)
        {
            return await _dbContext.Slides.AnyAsync(s => s.ProjectId == projectId);
        }

        public async Task<int> GetCountByProjectAsync(int projectId)
        {
            return await _dbContext.Slides.CountAsync(s => s.ProjectId == projectId);
        }

        public async Task<int> GetMaxSortOrderAsync(int projectId)
        {
            var maxOrder = await _dbContext.Slides
                .Where(s => s.ProjectId == projectId)
                .Select(s => (int?)s.SortOrder)
                .MaxAsync();
            return maxOrder ?? 0;
        }

        public async Task<Slide> AddAsync(Slide slide)
        {
            if (slide == null)
            {
                throw new ArgumentNullException(nameof(slide));
            }

            _dbContext.Slides.Add(slide);
            await _dbContext.SaveChangesAsync();
            return slide;
        }

        public async Task AddRangeAsync(IEnumerable<Slide> slides)
        {
            if (slides == null)
            {
                throw new ArgumentNullException(nameof(slides));
            }

            var slideList = slides.ToList();
            if (slideList.Count == 0)
            {
                return;
            }

            _dbContext.Slides.AddRange(slideList);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<Slide> GetByIdAsync(int slideId)
        {
            return await _dbContext.Slides
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == slideId);
        }

        public async Task UpdateAsync(Slide slide)
        {
            if (slide == null)
            {
                throw new ArgumentNullException(nameof(slide));
            }

            var tracked = await _dbContext.Slides
                .FirstOrDefaultAsync(s => s.Id == slide.Id);
            if (tracked == null)
            {
                throw new InvalidOperationException($"幻灯片不存在: ID={slide.Id}");
            }

            _dbContext.Entry(tracked).CurrentValues.SetValues(slide);
            await _dbContext.SaveChangesAsync();
        }

        public async Task UpdateThumbnailAsync(int slideId, string thumbnailPath)
        {
            var slide = await _dbContext.Slides.FindAsync(slideId);
            if (slide == null)
            {
                return;
            }

            slide.ThumbnailPath = thumbnailPath;
            slide.ModifiedTime = DateTime.Now;
            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<Slide>> GetByProjectAsync(int projectId)
        {
            return await _dbContext.Slides
                .AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();
        }

        public async Task<List<Slide>> GetByProjectWithElementsAsync(int projectId)
        {
            return await _dbContext.Slides
                .AsNoTracking()
                .Include(s => s.Elements)
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();
        }

        public async Task UpdateSortOrdersAsync(IEnumerable<Slide> slides)
        {
            if (slides == null)
            {
                throw new ArgumentNullException(nameof(slides));
            }

            var slideList = slides.ToList();
            if (slideList.Count == 0)
            {
                return;
            }

            foreach (var slide in slideList)
            {
                _dbContext.Entry(slide).Property(s => s.SortOrder).IsModified = true;
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task ShiftSortOrdersAsync(int projectId, int fromSortOrder, int delta)
        {
            if (delta == 0)
            {
                return;
            }

            var slides = await _dbContext.Slides
                .Where(s => s.ProjectId == projectId && s.SortOrder >= fromSortOrder)
                .ToListAsync();

            foreach (var slide in slides)
            {
                slide.SortOrder += delta;
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task DeleteAsync(int slideId)
        {
            var slide = await _dbContext.Slides.FindAsync(slideId);
            if (slide == null)
            {
                return;
            }

            _dbContext.Slides.Remove(slide);
            await _dbContext.SaveChangesAsync();
        }

        public async Task DeleteByProjectAsync(int projectId)
        {
            var slides = await _dbContext.Slides
                .Where(s => s.ProjectId == projectId)
                .ToListAsync();

            if (slides.Count == 0)
            {
                return;
            }

            _dbContext.Slides.RemoveRange(slides);
            await _dbContext.SaveChangesAsync();
        }
    }
}
