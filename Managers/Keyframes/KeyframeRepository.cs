using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Managers.Keyframes
{
    /// <summary>
    /// 关键帧数据仓库
    /// 封装所有关键帧相关的数据库操作
    /// </summary>
    public class KeyframeRepository : IDisposable
    {
        private readonly CanvasDbContext _context;
        private bool _disposed = false;

        public KeyframeRepository(CanvasDbContext context)
        {
            _context = context;
        }

        #region 关键帧操作

        /// <summary>
        /// 添加关键帧
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <param name="position">滚动位置（0.0-1.0）</param>
        /// <param name="yPosition">Y坐标位置（像素）</param>
        /// <returns>新关键帧ID，-1表示已存在相近关键帧</returns>
        public async Task<int> AddKeyframeAsync(int imageId, double position, int yPosition)
        {
            // 检查是否已存在相近位置的关键帧（50像素范围内）
            var existsNearby = await _context.Keyframes
                .AnyAsync(k => k.ImageId == imageId && Math.Abs(k.YPosition - yPosition) < 50);

            if (existsNearby)
            {
                return -1; // 已存在相近关键帧
            }

            // 获取最大order_index
            var maxOrder = await _context.Keyframes
                .Where(k => k.ImageId == imageId)
                .MaxAsync(k => (int?)k.OrderIndex) ?? -1;

            var keyframe = new Keyframe
            {
                ImageId = imageId,
                Position = position,
                YPosition = yPosition,
                OrderIndex = maxOrder + 1
            };

            _context.Keyframes.Add(keyframe);
            await _context.SaveChangesAsync();

            return keyframe.Id;
        }

        /// <summary>
        /// 获取指定图片的所有关键帧
        /// </summary>
        public async Task<List<Keyframe>> GetKeyframesAsync(int imageId)
        {
            return await _context.Keyframes
                .Where(k => k.ImageId == imageId)
                .OrderBy(k => k.OrderIndex)
                .ToListAsync();
        }

        /// <summary>
        /// 删除关键帧
        /// </summary>
        public async Task<bool> DeleteKeyframeAsync(int keyframeId)
        {
            var keyframe = await _context.Keyframes.FindAsync(keyframeId);
            if (keyframe == null)
                return false;

            var imageId = keyframe.ImageId;
            var orderIndex = keyframe.OrderIndex ?? 0;

            // 删除关键帧
            _context.Keyframes.Remove(keyframe);

            // 更新后续关键帧的order_index
            var subsequentKeyframes = await _context.Keyframes
                .Where(k => k.ImageId == imageId && k.OrderIndex > orderIndex)
                .ToListAsync();

            foreach (var kf in subsequentKeyframes)
            {
                kf.OrderIndex = (kf.OrderIndex ?? 0) - 1;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// 清除指定图片的所有关键帧
        /// </summary>
        public async Task<bool> ClearKeyframesAsync(int imageId)
        {
            var keyframes = await _context.Keyframes
                .Where(k => k.ImageId == imageId)
                .ToListAsync();

            if (keyframes.Count == 0)
                return false;

            _context.Keyframes.RemoveRange(keyframes);
            await _context.SaveChangesAsync();
            return true;
        }

        #endregion

        #region 关键帧时间操作

        /// <summary>
        /// 批量保存时间序列
        /// </summary>
        public async Task<bool> SaveTimingSequenceAsync(int imageId, List<KeyframeTiming> timings)
        {
            if (timings == null || timings.Count == 0)
                return false;

            try
            {
                _context.KeyframeTimings.AddRange(timings);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存时间序列失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 更新关键帧时间
        /// </summary>
        public async Task<bool> UpdateKeyframeTimingAsync(int imageId, int keyframeId, double newDuration)
        {
            // 先尝试查找已存在的记录
            var existing = await _context.KeyframeTimings
                .FirstOrDefaultAsync(t => t.ImageId == imageId && t.KeyframeId == keyframeId);

            if (existing != null)
            {
                // 更新现有记录
                existing.Duration = newDuration;
            }
            else
            {
                // 插入新记录
                var maxSequenceOrder = await _context.KeyframeTimings
                    .Where(t => t.ImageId == imageId)
                    .MaxAsync(t => (int?)t.SequenceOrder) ?? -1;

                var timing = new KeyframeTiming
                {
                    ImageId = imageId,
                    KeyframeId = keyframeId,
                    Duration = newDuration,
                    SequenceOrder = maxSequenceOrder + 1,
                    CreatedAt = DateTime.Now
                };

                _context.KeyframeTimings.Add(timing);
            }

            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// 获取时间序列
        /// </summary>
        public async Task<List<KeyframeTiming>> GetTimingSequenceAsync(int imageId)
        {
            return await _context.KeyframeTimings
                .Where(t => t.ImageId == imageId)
                .OrderBy(t => t.SequenceOrder)
                .ToListAsync();
        }

        /// <summary>
        /// 清除时间数据
        /// </summary>
        public async Task<bool> ClearTimingDataAsync(int imageId)
        {
            var timings = await _context.KeyframeTimings
                .Where(t => t.ImageId == imageId)
                .ToListAsync();

            if (timings.Count == 0)
                return false;

            _context.KeyframeTimings.RemoveRange(timings);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// 检查是否有时间数据
        /// </summary>
        public async Task<bool> HasTimingDataAsync(int imageId)
        {
            return await _context.KeyframeTimings
                .AnyAsync(t => t.ImageId == imageId);
        }

        #endregion

        #region 原图模式时间操作

        /// <summary>
        /// 批量保存原图模式时间序列
        /// </summary>
        public async Task<bool> SaveOriginalModeSequenceAsync(int baseImageId, List<OriginalModeTiming> timings)
        {
            if (timings == null || timings.Count == 0)
                return false;

            try
            {
                _context.OriginalModeTimings.AddRange(timings);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存原图模式时间序列失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取原图模式时间序列
        /// </summary>
        public async Task<List<OriginalModeTiming>> GetOriginalModeTimingSequenceAsync(int baseImageId)
        {
            return await _context.OriginalModeTimings
                .Where(t => t.BaseImageId == baseImageId)
                .OrderBy(t => t.SequenceOrder)
                .ToListAsync();
        }

        /// <summary>
        /// 更新原图模式时间
        /// </summary>
        public async Task<bool> UpdateOriginalModeTimingAsync(
            int baseImageId, int fromImageId, int toImageId, double newDuration)
        {
            var timing = await _context.OriginalModeTimings
                .FirstOrDefaultAsync(t =>
                    t.BaseImageId == baseImageId &&
                    t.FromImageId == fromImageId &&
                    t.ToImageId == toImageId);

            if (timing == null)
                return false;

            timing.Duration = newDuration;
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// 清除原图模式时间数据
        /// </summary>
        public async Task<bool> ClearOriginalModeTimingDataAsync(int baseImageId)
        {
            var timings = await _context.OriginalModeTimings
                .Where(t => t.BaseImageId == baseImageId)
                .ToListAsync();

            if (timings.Count == 0)
                return false;

            _context.OriginalModeTimings.RemoveRange(timings);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// 检查是否有原图模式时间数据
        /// </summary>
        public async Task<bool> HasOriginalModeTimingDataAsync(int baseImageId)
        {
            return await _context.OriginalModeTimings
                .AnyAsync(t => t.BaseImageId == baseImageId);
        }

        #endregion

        #region IDisposable实现

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 注意：不要dispose context，它由DatabaseManager管理
                }
                _disposed = true;
            }
        }

        #endregion
    }
}

