using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
            catch (Exception)
            {
                throw;
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
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 获取指定图片的关键帧数量
        /// </summary>
        public async Task<int> GetKeyframeCountAsync(int imageId)
        {
            try
            {
                return await _dbSet.CountAsync(k => k.ImageId == imageId);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 批量更新关键帧排序
        /// </summary>
        public async Task UpdateKeyframeOrdersAsync(List<Keyframe> keyframes)
        {
            try
            {
                foreach (var keyframe in keyframes)
                {
                    _dbSet.Update(keyframe);
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 删除指定图片的所有关键帧
        /// </summary>
        public async Task DeleteKeyframesByImageIdAsync(int imageId)
        {
            try
            {
                var keyframes = await GetKeyframesByImageIdAsync(imageId);
                if (keyframes.Any())
                {
                    _dbSet.RemoveRange(keyframes);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 根据位置查找最接近的关键帧
        /// </summary>
        public async Task<Keyframe> FindClosestKeyframeAsync(int imageId, double position)
        {
            try
            {
                var keyframes = await GetKeyframesByImageIdAsync(imageId);
                if (!keyframes.Any())
                    return null;

                // 找到最接近的关键帧
                return keyframes
                    .OrderBy(k => Math.Abs(k.Position - position))
                    .FirstOrDefault();
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
