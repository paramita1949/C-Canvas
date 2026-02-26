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
                _dbContext.SaveChanges();
                return;
            }

            // 项目可能在其他流程（如项目树删除）中被移除，直接跳过可避免并发更新异常。
            var exists = _dbContext.LyricsProjects
                .AsNoTracking()
                .Any(p => p.Id == project.Id);
            if (!exists)
            {
                return;
            }

            try
            {
                _dbContext.LyricsProjects.Update(project);
                _dbContext.SaveChanges();
            }
            catch (DbUpdateConcurrencyException)
            {
                // 并发删除/修改导致更新目标不存在，忽略本次保存并清理跟踪状态。
                _dbContext.Entry(project).State = EntityState.Detached;
            }
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
