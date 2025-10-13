using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.DTOs;
using ImageColorChanger.Repositories.Interfaces;
using ImageColorChanger.Utils;

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
            try
            {
                // 检查缓存
                if (_cache.TryGetValue(imageId, out var cached))
                {
                    if (DateTime.Now - cached.CachedAt < _cacheTtl)
                    {
                        Logger.Debug("时间序列缓存命中: ImageId={ImageId}", imageId);
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

                Logger.Debug("从数据库加载时间序列: ImageId={ImageId}, Count={Count}", imageId, timings.Count);
                return timings;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "获取时间序列失败: ImageId={ImageId}", imageId);
                throw;
            }
        }

        /// <summary>
        /// 检查是否存在时间数据
        /// </summary>
        public async Task<bool> HasTimingDataAsync(int imageId)
        {
            try
            {
                return await _context.KeyframeTimings
                    .AnyAsync(t => t.ImageId == imageId);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "检查时间数据失败: ImageId={ImageId}", imageId);
                throw;
            }
        }

        /// <summary>
        /// 清除指定图片的所有时间数据
        /// </summary>
        public async Task ClearTimingsByImageIdAsync(int imageId)
        {
            try
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

                    Logger.Info("清除时间数据: ImageId={ImageId}, Count={Count}", imageId, timings.Count);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "清除时间数据失败: ImageId={ImageId}", imageId);
                throw;
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
                var entities = timings.Select((t, index) => new KeyframeTiming
                {
                    ImageId = imageId,
                    KeyframeId = t.KeyframeId,
                    Duration = t.Duration,
                    SequenceOrder = index,
                    CreatedAt = DateTime.Now
                }).ToList();

                await _context.KeyframeTimings.AddRangeAsync(entities);
                await _context.SaveChangesAsync();

                // 3. 提交事务
                await transaction.CommitAsync();

                // 4. 清除缓存
                _cache.Remove(imageId);

                Logger.Info("批量保存时间序列: ImageId={ImageId}, Count={Count}", imageId, timings.Count);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Logger.Error(ex, "批量保存时间序列失败: ImageId={ImageId}", imageId);
                throw;
            }
        }

        /// <summary>
        /// 更新指定关键帧的持续时间
        /// </summary>
        public async Task UpdateDurationAsync(int keyframeId, double newDuration)
        {
            try
            {
                var timing = await _context.KeyframeTimings
                    .FirstOrDefaultAsync(t => t.KeyframeId == keyframeId);

                if (timing != null)
                {
                    timing.Duration = newDuration;
                    await _context.SaveChangesAsync();

                    // 清除相关缓存
                    _cache.Remove(timing.ImageId);

                    Logger.Info("更新时长: KeyframeId={KeyframeId}, NewDuration={Duration}s", 
                        keyframeId, newDuration);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "更新时长失败: KeyframeId={KeyframeId}", keyframeId);
                throw;
            }
        }

        /// <summary>
        /// 更新暂停时间（通过增加持续时间实现）
        /// </summary>
        public async Task UpdatePausedTimeAsync(int imageId, int sequenceOrder, double pausedTime)
        {
            try
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

                    Logger.Info("更新暂停时间: ImageId={ImageId}, SequenceOrder={Order}, PausedTime={Time}s", 
                        imageId, sequenceOrder, pausedTime);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "更新暂停时间失败: ImageId={ImageId}, SequenceOrder={Order}", 
                    imageId, sequenceOrder);
                throw;
            }
        }

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
            Logger.Debug("清除时间序列缓存");
        }
    }
}

