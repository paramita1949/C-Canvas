using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Repositories.Interfaces;

namespace ImageColorChanger.Repositories.Implementations
{
    /// <summary>
    /// 关键帧仓储实现
    /// </summary>
    public class KeyframeRepositoryImpl : RepositoryBase<Keyframe>, IKeyframeRepository
    {
        public KeyframeRepositoryImpl(CanvasDbContext context) : base(context)
        {
        }

        /// <summary>
        /// 获取指定图片的所有关键帧（按顺序）- 同步版本
        /// </summary>
        public List<Keyframe> GetKeyframesByImageId(int imageId)
        {
            try
            {
                return _dbSet
                    .Where(k => k.ImageId == imageId)
                    .OrderBy(k => k.OrderIndex)
                    .ToList();
            }
            catch (SqliteException ex) when (IsMissingAutoPauseColumn(ex))
            {
                EnsureAutoPauseColumnExists();
                return _dbSet
                    .Where(k => k.ImageId == imageId)
                    .OrderBy(k => k.OrderIndex)
                    .ToList();
            }
        }

        /// <summary>
        /// 获取指定图片的所有关键帧（按顺序）
        /// </summary>
        public async Task<List<Keyframe>> GetKeyframesByImageIdAsync(int imageId)
        {
            try
            {
                return await _dbSet
                    .Where(k => k.ImageId == imageId)
                    .OrderBy(k => k.OrderIndex)
                    .ToListAsync();
            }
            catch (SqliteException ex) when (IsMissingAutoPauseColumn(ex))
            {
                await EnsureAutoPauseColumnExistsAsync();
                return await _dbSet
                    .Where(k => k.ImageId == imageId)
                    .OrderBy(k => k.OrderIndex)
                    .ToListAsync();
            }
        }

        /// <summary>
        /// 获取指定图片的关键帧数量
        /// </summary>
        public async Task<int> GetKeyframeCountAsync(int imageId)
        {
            return await _dbSet.CountAsync(k => k.ImageId == imageId);
        }

        /// <summary>
        /// 批量更新关键帧排序
        /// </summary>
        public async Task UpdateKeyframeOrdersAsync(List<Keyframe> keyframes)
        {
            foreach (var keyframe in keyframes)
            {
                _dbSet.Update(keyframe);
            }
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// 删除指定图片的所有关键帧
        /// </summary>
        public async Task DeleteKeyframesByImageIdAsync(int imageId)
        {
            var keyframes = await GetKeyframesByImageIdAsync(imageId);
            if (keyframes.Any())
            {
                _dbSet.RemoveRange(keyframes);
                await _context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// 根据位置查找最接近的关键帧
        /// </summary>
        public async Task<Keyframe> FindClosestKeyframeAsync(int imageId, double position)
        {
            var keyframes = await GetKeyframesByImageIdAsync(imageId);
            if (!keyframes.Any())
                return null;

            // 找到最接近的关键帧
            return keyframes
                .OrderBy(k => Math.Abs(k.Position - position))
                .FirstOrDefault();
        }

        private static bool IsMissingAutoPauseColumn(SqliteException ex)
        {
            var message = ex?.Message ?? string.Empty;
            return message.Contains("no such column", StringComparison.OrdinalIgnoreCase)
                && message.Contains("auto_pause", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureAutoPauseColumnExists()
        {
            try
            {
                _context.Database.ExecuteSqlRaw("ALTER TABLE keyframes ADD COLUMN auto_pause INTEGER NOT NULL DEFAULT 0");
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("[关键帧][Repo] 已自动补齐 keyframes.auto_pause 列（sync）");
                #endif
            }
            catch (SqliteException ex) when (ex.Message?.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase) == true)
            {
                // 并发补列时忽略重复列错误
            }
        }

        private async Task EnsureAutoPauseColumnExistsAsync()
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync("ALTER TABLE keyframes ADD COLUMN auto_pause INTEGER NOT NULL DEFAULT 0");
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("[关键帧][Repo] 已自动补齐 keyframes.auto_pause 列（async）");
                #endif
            }
            catch (SqliteException ex) when (ex.Message?.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase) == true)
            {
                // 并发补列时忽略重复列错误
            }
        }
    }
}
