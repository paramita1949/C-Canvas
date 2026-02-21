using System;
using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 歌词分组管理器。
    /// </summary>
    public class LyricsGroupManager
    {
        private readonly CanvasDbContext _dbContext;

        public LyricsGroupManager(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public List<LyricsGroup> GetAllGroups()
        {
            return _dbContext.LyricsGroups
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Id)
                .ToList();
        }

        public LyricsGroup CreateGroup(string name, int sortOrder = 0, bool isSystem = false)
        {
            var group = new LyricsGroup
            {
                Name = name,
                SortOrder = sortOrder,
                IsSystem = isSystem,
                CreatedTime = DateTime.Now
            };

            _dbContext.LyricsGroups.Add(group);
            _dbContext.SaveChanges();
            return group;
        }
    }
}

