using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 歌词项目管理器
    /// 负责 LyricsProject 的读取与保存
    /// </summary>
    public class LyricsProjectManager
    {
        private readonly CanvasDbContext _dbContext;

        public LyricsProjectManager(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public void ClearTracking()
        {
            _dbContext.ChangeTracker.Clear();
        }

        public LyricsProject FindByImageId(int imageId)
        {
            return _dbContext.LyricsProjects.FirstOrDefault(p => p.ImageId == imageId);
        }

        public LyricsProject FindById(int id)
        {
            return _dbContext.LyricsProjects
                .Include(p => p.LyricsGroup)
                .FirstOrDefault(p => p.Id == id);
        }

        public List<LyricsProject> GetByGroupId(int? groupId)
        {
            return _dbContext.LyricsProjects
                .Where(p => p.GroupId == groupId)
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Id)
                .ToList();
        }

        public void Add(LyricsProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            _dbContext.LyricsProjects.Add(project);
            _dbContext.SaveChanges();
        }

        public void Save(LyricsProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            if (project.Id == 0)
            {
                _dbContext.LyricsProjects.Add(project);
            }
            else
            {
                _dbContext.LyricsProjects.Update(project);
            }

            _dbContext.SaveChanges();
        }

        public void Delete(int id)
        {
            var project = _dbContext.LyricsProjects.FirstOrDefault(p => p.Id == id);
            if (project == null)
            {
                return;
            }

            _dbContext.LyricsProjects.Remove(project);
            _dbContext.SaveChanges();
        }
    }
}
