using System;
using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Database;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Services.Lyrics
{
    public sealed class ImageLyricsMigrationPlan
    {
        public List<int> EligibleProjectIds { get; } = new();
        public List<int> ConflictImageIds { get; } = new();
        public List<int> RetainedProjectIds { get; } = new();
        public List<int> QuarantineProjectIds { get; } = new();
    }

    /// <summary>只读所有权分类，不依赖名称，不对重复/来源冲突选择一个“赢家”。</summary>
    public static class ImageLyricsMigrationPlanner
    {
        public static ImageLyricsMigrationPlan Plan(CanvasDbContext db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            var plan = new ImageLyricsMigrationPlan();
            var rows = db.LyricsProjects.AsNoTracking().ToList();
            var images = db.MediaFiles.AsNoTracking().Where(m => m.FileTypeString == "image").Select(m => m.Id).ToHashSet();
            var ordinaryGroups = db.LyricsGroups.AsNoTracking().Where(g => !g.IsSystem).Select(g => g.Id).ToHashSet();
            foreach (var group in rows.Where(p => p.ImageId.HasValue).GroupBy(p => p.ImageId.Value))
            {
                var list = group.ToList();
                if (images.Contains(group.Key) && list.Count == 1 && list[0].SourceType == 0)
                    plan.EligibleProjectIds.Add(list[0].Id);
                else
                {
                    if (images.Contains(group.Key)) plan.ConflictImageIds.Add(group.Key);
                    plan.QuarantineProjectIds.AddRange(list.Select(p => p.Id));
                }
            }
            foreach (var row in rows.Where(p => !p.ImageId.HasValue))
            {
                // 普通分组里的历史独立歌曲可以尚未回填source_type。
                if (row.SourceType == 1 || (row.GroupId.HasValue && ordinaryGroups.Contains(row.GroupId.Value)))
                    plan.RetainedProjectIds.Add(row.Id);
                else
                {
                    plan.RetainedProjectIds.Add(row.Id); // 原文必须保留，不作为可迁移图片。
                    plan.QuarantineProjectIds.Add(row.Id);
                }
            }
            return plan;
        }
    }
}
