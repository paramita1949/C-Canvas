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

        // 兼容旧调用方的临时适配对象，不再写入lyrics_projects。新UI使用ImageLyricsService/LyricsDocument。
        [Obsolete("图片入口请使用ImageLyricsService。此适配器仅供旧调用方过渡。")]
        public LyricsProject FindByImageId(int imageId)
        {
            var row = _dbContext.ImageLyrics.AsNoTracking().SingleOrDefault(p => p.ImageId == imageId);
            if (row == null) return null;
            var project = new LyricsProject { Id = row.Id, Name = string.Empty, ImageId = imageId, SourceType = 0, CreatedTime = row.CreatedTime, ModifiedTime = row.ModifiedTime };
            Services.Lyrics.LyricsDocument.FromImage(row).ApplyTo(project);
            return project;
        }

        [Obsolete("图片入口请使用ImageLyricsService.Load。")]
        public LyricsProject LoadOrCreateImageProject(int imageId)
        {
            var doc = new Services.Lyrics.ImageLyricsService(_dbContext).Load(imageId);
            if (doc == null) return null;
            var project = FindByImageId(imageId) ?? new LyricsProject { Name = string.Empty, ImageId = imageId, SourceType = 0 };
            doc.ApplyTo(project); return project;
        }

        [Obsolete("图片入口请使用ImageLyricsService.Save。")]
        public void SaveImageProject(LyricsProject project, int imageId)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (imageId <= 0 || project.ImageId != imageId) throw new InvalidOperationException("歌词项目与当前图片入口不匹配。");
            if (project.Id > 0)
            {
                var stored = _dbContext.ImageLyrics.AsNoTracking().SingleOrDefault(p => p.Id == project.Id);
                if (stored == null) return; // 已删除项目不因旧回调复活。
                if (stored.ImageId != imageId) throw new InvalidOperationException("不能将已有歌词改绑到另一张图片。");
            }
            new Services.Lyrics.ImageLyricsService(_dbContext).Save(imageId, Services.Lyrics.LyricsDocument.FromProject(project));
            project.Id = _dbContext.ImageLyrics.AsNoTracking().Single(p => p.ImageId == imageId).Id;
        }

        public LyricsProject FindById(int id)
        {
            return _dbContext.LyricsProjects
                .Include(p => p.LyricsGroup)
                .FirstOrDefault(p => p.Id == id);
        }

        /// <summary>文件树和搜索共用的独立歌曲范围；图片歌词始终通过图片入口访问。</summary>
        public static IQueryable<LyricsProject> QueryLibraryProjects(CanvasDbContext dbContext)
        {
            return dbContext.LyricsProjects.AsNoTracking()
                .Where(p => p.ImageId == null &&
                    (p.SourceType == 1 ||
                        // 兼容旧版普通分组中尚未回填来源类型的独立歌曲。
                        (p.LyricsGroup != null && !p.LyricsGroup.IsSystem)));
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
