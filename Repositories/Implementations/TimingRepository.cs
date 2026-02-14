using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.DTOs;
using ImageColorChanger.Repositories.Interfaces;

namespace ImageColorChanger.Repositories.Implementations
{
    /// <summary>
    /// 关键帧时间记录仓储实现
    /// 包含缓存机制以提高性能
    /// </summary>
    public class TimingRepository : RepositoryBase<KeyframeTiming>, ITimingRepository
    {
        // ========== 缓存机制 ==========
        private readonly Dictionary<int, CachedTimingData> _cache = new();
        private readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 缓存数据结构
        /// </summary>
        private class CachedTimingData
        {
            public List<TimingSequenceDto> Data { get; set; }
            public DateTime CachedAt { get; set; }
        }

        public TimingRepository(CanvasDbContext context) : base(context)
        {
        }

        /// <summary>
        /// 获取指定图片的时间序列（带缓存）
        /// </summary>
        public async Task<List<TimingSequenceDto>> GetTimingSequenceAsync(int imageId)
        {
            // 检查缓存
            if (_cache.TryGetValue(imageId, out var cached))
            {
                if (DateTime.Now - cached.CachedAt < _cacheTtl)
                {
                    return cached.Data;
                }
                else
                {
                    // 缓存过期，移除
                    _cache.Remove(imageId);
                }
            }

            // 从数据库查询
            var timings = await _context.KeyframeTimings
                .Include(t => t.Keyframe)
                .Where(t => t.ImageId == imageId)
                .OrderBy(t => t.SequenceOrder)
                .Select(t => new TimingSequenceDto
                {
                    KeyframeId = t.KeyframeId,
                    Duration = t.Duration,
                    SequenceOrder = t.SequenceOrder,
                    Position = t.Keyframe.Position,
                    YPosition = t.Keyframe.YPosition,
                    LoopCount = t.Keyframe.LoopCount,
                    PausedTime = 0, // 暂停时间需要单独计算
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();

            // 更新缓存
            _cache[imageId] = new CachedTimingData
            {
                Data = timings,
                CachedAt = DateTime.Now
            };

            return timings;
        }

        /// <summary>
        /// 检查是否存在时间数据
        /// </summary>
        public async Task<bool> HasTimingDataAsync(int imageId)
        {
            return await _context.KeyframeTimings
                .AnyAsync(t => t.ImageId == imageId);
        }

        /// <summary>
        /// 清除指定图片的所有时间数据
        /// </summary>
        public async Task ClearTimingsByImageIdAsync(int imageId)
        {
            var timings = await _context.KeyframeTimings
                .Where(t => t.ImageId == imageId)
                .ToListAsync();

            if (timings.Any())
            {
                _context.KeyframeTimings.RemoveRange(timings);
                await _context.SaveChangesAsync();

                // 清除缓存
                _cache.Remove(imageId);
            }
        }

        /// <summary>
        /// 批量保存时间序列
        /// </summary>
        public async Task BatchSaveTimingsAsync(int imageId, List<TimingSequenceDto> timings)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. 先清除旧数据
                await ClearTimingsByImageIdAsync(imageId);

                // 2. 批量插入新数据
                // 修复：使用DTO中的SequenceOrder，确保跳帧录制时顺序正确
                var entities = timings.Select(t => new KeyframeTiming
                {
                    ImageId = imageId,
                    KeyframeId = t.KeyframeId,
                    Duration = t.Duration,
                    SequenceOrder = t.SequenceOrder, // 使用录制时设置的顺序，支持跳帧录制
                    CreatedAt = DateTime.Now
                }).ToList();

                await _context.KeyframeTimings.AddRangeAsync(entities);
                await _context.SaveChangesAsync();

                // 3. 提交事务
                await transaction.CommitAsync();

                // 4. 清除缓存
                _cache.Remove(imageId);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// 更新指定关键帧的持续时间（通过ImageId和SequenceOrder精确定位，支持跳帧录制）
        /// </summary>
        public async Task UpdateDurationAsync(int imageId, int sequenceOrder, double newDuration)
        {
            try
            {
                var timing = await _context.KeyframeTimings
                    .FirstOrDefaultAsync(t => t.ImageId == imageId && t.SequenceOrder == sequenceOrder);

                if (timing != null)
                {
                    var oldDuration = timing.Duration;
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"💾 [数据库写入前] ImageId={imageId}, SequenceOrder={sequenceOrder}, KeyframeId={timing.KeyframeId}");
                    System.Diagnostics.Debug.WriteLine($"   旧值: {oldDuration:F2}秒 → 新值: {newDuration:F2}秒");
                    #endif
                    
                    timing.Duration = newDuration;
                    await _context.SaveChangesAsync();
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"💾 [数据库写入完成] ImageId={imageId}, SequenceOrder={sequenceOrder} 已更新为 {newDuration:F2}秒");
                    #endif

                    // 清除相关缓存
                    _cache.Remove(imageId);
                }
                else
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [数据库写入失败] 找不到 ImageId={imageId}, SequenceOrder={sequenceOrder} 的Timing记录");
                    #endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [数据库写入异常] ImageId={imageId}, SequenceOrder={sequenceOrder}: {ex.Message}");
                #endif
                throw;
            }
        }
        
        /// <summary>
        /// 更新指定关键帧的持续时间（旧方法，仅通过KeyframeId，可能匹配到错误的记录）
        /// </summary>
        [Obsolete("使用 UpdateDurationAsync(int imageId, int sequenceOrder, double newDuration) 替代，以支持跳帧录制")]
        public async Task UpdateDurationAsync(int keyframeId, double newDuration)
        {
            try
            {
                var timing = await _context.KeyframeTimings
                    .FirstOrDefaultAsync(t => t.KeyframeId == keyframeId);

                if (timing != null)
                {
                    var oldDuration = timing.Duration;
                    //System.Diagnostics.Debug.WriteLine($"💾 [数据库写入前] KeyframeId={keyframeId}, ImageId={timing.ImageId}, Order={timing.SequenceOrder}");
                    //System.Diagnostics.Debug.WriteLine($"   旧值: {oldDuration:F2}秒 → 新值: {newDuration:F2}秒");
                    
                    timing.Duration = newDuration;
                    await _context.SaveChangesAsync();
                    
                    //System.Diagnostics.Debug.WriteLine($"💾 [数据库写入完成] KeyframeId={keyframeId} 已更新为 {newDuration:F2}秒");

                    // 清除相关缓存
                    _cache.Remove(timing.ImageId);
                    //System.Diagnostics.Debug.WriteLine($"💾 [缓存已清除] ImageId={timing.ImageId}");
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine($"⚠️ [数据库写入失败] 找不到 KeyframeId={keyframeId} 的Timing记录");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [数据库写入异常] KeyframeId={keyframeId}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 更新暂停时间（通过增加持续时间实现）
        /// </summary>
        public async Task UpdatePausedTimeAsync(int imageId, int sequenceOrder, double pausedTime)
        {
            var timing = await _context.KeyframeTimings
                .FirstOrDefaultAsync(t => t.ImageId == imageId && t.SequenceOrder == sequenceOrder);

            if (timing != null)
            {
                // 暂停时间累加到持续时间
                timing.Duration += pausedTime;
                await _context.SaveChangesAsync();

                // 清除缓存
                _cache.Remove(imageId);
            }
        }

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
        }
    }
}

