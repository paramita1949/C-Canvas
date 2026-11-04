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

        /// <summary>图片入口只读取本图歌词；没有记录时返回未保存的空白项目。</summary>
        public LyricsProject LoadOrCreateImageProject(int imageId)
        {
            if (imageId <= 0 || !_dbContext.MediaFiles.Any(m => m.Id == imageId))
            {
                return null;
            }

            return FindByImageId(imageId) ?? new LyricsProject
            {
                Name = $"歌词_图片_{imageId}",
                ImageId = imageId,
                SourceType = 0,
                Content = string.Empty,
                FontSize = 60,
                TextAlign = "Center"
            };
        }

        /// <summary>保存到进入时绑定的图片，禁止旧项目被另一张图片重新绑定。</summary>
        public void SaveImageProject(LyricsProject project, int imageId)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (imageId <= 0 || project.ImageId != imageId)
            {
                throw new InvalidOperationException("歌词项目与当前图片入口不匹配。");
            }

            var stored = project.Id > 0
                ? _dbContext.LyricsProjects.AsNoTracking().FirstOrDefault(p => p.Id == project.Id)
                : null;
            if (stored != null && stored.ImageId != imageId)
            {
                throw new InvalidOperationException("不能将已有歌词改绑到另一张图片。");
            }
            if (!_dbContext.MediaFiles.Any(m => m.Id == imageId))
            {
                throw new InvalidOperationException("对应图片已不存在，未保存歌词。");
            }

            project.SourceType = 0;
            Save(project);
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
