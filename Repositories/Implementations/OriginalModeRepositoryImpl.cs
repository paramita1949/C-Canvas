using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// 原图模式仓储实现
    /// </summary>
    public class OriginalModeRepositoryImpl : RepositoryBase<OriginalModeTiming>, IOriginalModeRepository
    {
        public OriginalModeRepositoryImpl(CanvasDbContext context) : base(context)
        {
        }

        /// <summary>
        /// 获取原图模式时间序列
        /// </summary>
        public async Task<List<OriginalTimingSequenceDto>> GetOriginalTimingSequenceAsync(int baseImageId)
        {
            try
            {
                var timings = await _dbSet
                    .Where(t => t.BaseImageId == baseImageId)
                    .OrderBy(t => t.SequenceOrder)
                    .ToListAsync();

                var result = new List<OriginalTimingSequenceDto>();
                foreach (var timing in timings)
                {
                    // 获取相似图片路径
                    var similarImage = await _context.MediaFiles
                        .FirstOrDefaultAsync(m => m.Id == timing.ToImageId);

                    result.Add(new OriginalTimingSequenceDto
                    {
                        Id = timing.Id,
                        BaseImageId = timing.BaseImageId,
                        FromImageId = timing.FromImageId,
                        ToImageId = timing.ToImageId,
                        Duration = timing.Duration,
                        SequenceOrder = timing.SequenceOrder,
                        PausedTime = 0,
                        CreatedAt = timing.CreatedAt,
                        SimilarImagePath = similarImage?.Path
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "获取原图时间序列失败: BaseImageId={BaseImageId}", baseImageId);
                throw;
            }
        }

        /// <summary>
        /// 检查是否存在原图时间数据
        /// </summary>
        public async Task<bool> HasOriginalTimingDataAsync(int baseImageId)
        {
            try
            {
                return await _dbSet.AnyAsync(t => t.BaseImageId == baseImageId);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "检查原图时间数据失败: BaseImageId={BaseImageId}", baseImageId);
                throw;
            }
        }

        /// <summary>
        /// 清除原图模式时间数据
        /// </summary>
        public async Task ClearOriginalTimingsByBaseIdAsync(int baseImageId)
        {
            try
            {
                var timings = await _dbSet
                    .Where(t => t.BaseImageId == baseImageId)
                    .ToListAsync();

                if (timings.Any())
                {
                    _dbSet.RemoveRange(timings);
                    await _context.SaveChangesAsync();

                    Logger.Info("清除原图时间数据: BaseImageId={BaseImageId}, Count={Count}", 
                        baseImageId, timings.Count);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "清除原图时间数据失败: BaseImageId={BaseImageId}", baseImageId);
                throw;
            }
        }

        /// <summary>
        /// 批量保存原图时间序列
        /// </summary>
        public async Task BatchSaveOriginalTimingsAsync(int baseImageId, List<OriginalTimingSequenceDto> timings)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. 先清除旧数据
                await ClearOriginalTimingsByBaseIdAsync(baseImageId);

                // 2. 批量插入新数据
                var entities = timings.Select((t, index) => new OriginalModeTiming
                {
                    BaseImageId = baseImageId,
                    FromImageId = index == 0 ? baseImageId : timings[index - 1].SimilarImageId,
                    ToImageId = t.SimilarImageId,
                    Duration = t.Duration,
                    SequenceOrder = index,
                    CreatedAt = DateTime.Now
                }).ToList();

                await _dbSet.AddRangeAsync(entities);
                await _context.SaveChangesAsync();

                // 3. 提交事务
                await transaction.CommitAsync();

                Logger.Info("批量保存原图时间序列: BaseImageId={BaseImageId}, Count={Count}", 
                    baseImageId, timings.Count);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Logger.Error(ex, "批量保存原图时间序列失败: BaseImageId={BaseImageId}", baseImageId);
                throw;
            }
        }

        /// <summary>
        /// 获取相似图片列表
        /// </summary>
        public async Task<List<SimilarImageDto>> GetSimilarImagesAsync(int imageId)
        {
            try
            {
                var image = await _context.MediaFiles.FindAsync(imageId);
                if (image == null)
                    return new List<SimilarImageDto>();

                // 查找相似图片
                var similarImages = await FindSimilarImagesByPatternAsync(image.Path, image.FolderId);

                // 转换为DTO
                var result = similarImages.Select(img => new SimilarImageDto
                {
                    ImageId = img.Id,
                    Name = img.Name,
                    Path = img.Path,
                    FolderId = img.FolderId,
                    OrderIndex = img.OrderIndex,
                    SimilarityScore = 100, // TODO: 实现相似度计算
                    IsPrimary = img.Id == imageId,
                    IsOriginal = false, // TODO: 从原图标记表查询
                    MatchPattern = "序号匹配"
                }).ToList();

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "获取相似图片失败: ImageId={ImageId}", imageId);
                throw;
            }
        }

        /// <summary>
        /// 根据文件名模式查找相似图片
        /// 参考Python版本的yuantu.py逻辑
        /// </summary>
        public async Task<List<MediaFile>> FindSimilarImagesByPatternAsync(string imagePath, int? folderId)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(imagePath);
                var extension = Path.GetExtension(imagePath);

                // 提取基础名称和序号
                // 例如: "image_001" -> baseName="image_", number=1
                var match = Regex.Match(fileName, @"^(.+?)(\d+)$");
                if (!match.Success)
                {
                    // 没有序号，只返回自己
                    return await _context.MediaFiles
                        .Where(m => m.Path == imagePath)
                        .ToListAsync();
                }

                var baseName = match.Groups[1].Value;
                var numberLength = match.Groups[2].Value.Length;

                // 查找同一文件夹下，文件名匹配模式的所有图片
                var allFiles = await _context.MediaFiles
                    .Where(m => m.FolderId == folderId)
                    .Where(m => m.FileTypeString == "image")
                    .ToListAsync();

                // 筛选符合模式的文件
                var similarFiles = allFiles
                    .Where(m =>
                    {
                        var name = Path.GetFileNameWithoutExtension(m.Path);
                        var ext = Path.GetExtension(m.Path);
                        
                        // 扩展名必须相同
                        if (ext != extension)
                            return false;

                        // 检查文件名模式
                        var fileMatch = Regex.Match(name, @"^(.+?)(\d+)$");
                        if (!fileMatch.Success)
                            return false;

                        // 基础名称必须相同，数字长度必须相同
                        return fileMatch.Groups[1].Value == baseName 
                            && fileMatch.Groups[2].Value.Length == numberLength;
                    })
                    .OrderBy(m => m.Name)
                    .ToList();

                Logger.Debug("查找相似图片: 基础名={BaseName}, 找到{Count}张", baseName, similarFiles.Count);
                return similarFiles;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "查找相似图片失败: Path={Path}", imagePath);
                throw;
            }
        }

        /// <summary>
        /// 更新原图模式时间记录的持续时间
        /// 参考Python版本：keytime.py update_original_mode_timing_in_db方法
        /// </summary>
        public async Task UpdateOriginalDurationAsync(int baseImageId, int similarImageId, double newDuration)
        {
            try
            {
                // 查找对应的时间记录（base_image_id + to_image_id）
                var timing = await _dbSet
                    .FirstOrDefaultAsync(t => 
                        t.BaseImageId == baseImageId && 
                        t.ToImageId == similarImageId);

                if (timing != null)
                {
                    timing.Duration = newDuration;
                    await _context.SaveChangesAsync();

                    Logger.Info("更新原图时长: BaseImageId={BaseImageId}, ToImageId={ToImageId}, NewDuration={Duration}s",
                        baseImageId, similarImageId, newDuration);
                }
                else
                {
                    Logger.Warning("未找到原图时间记录: BaseImageId={BaseImageId}, ToImageId={ToImageId}",
                        baseImageId, similarImageId);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "更新原图时长失败: BaseImageId={BaseImageId}, ToImageId={ToImageId}",
                    baseImageId, similarImageId);
                throw;
            }
        }

        /// <summary>
        /// 更新原图模式时间记录（手动跳转时间修正）
        /// 参考Python版本：keytime.py update_original_mode_timing_in_db方法
        /// </summary>
        public async Task<bool> UpdateTimingDurationAsync(int baseImageId, int fromImageId, int toImageId, double newDuration)
        {
            try
            {
                // 🔍 先查看数据库中有哪些记录（调试用）
                var allTimings = await _dbSet
                    .Where(t => t.BaseImageId == baseImageId)
                    .ToListAsync();
                
                Logger.Debug("🔍 数据库中BaseImageId={BaseImageId}的所有记录:", baseImageId);
                foreach (var t in allTimings)
                {
                    Logger.Debug("  记录: {FromId} -> {ToId}, Duration={Duration}s, SequenceOrder={Order}",
                        t.FromImageId, t.ToImageId, t.Duration, t.SequenceOrder);
                }
                
                // 查找对应的时间记录（base_image_id + from_image_id + to_image_id）
                var timing = await _dbSet
                    .FirstOrDefaultAsync(t => 
                        t.BaseImageId == baseImageId && 
                        t.FromImageId == fromImageId &&
                        t.ToImageId == toImageId);

                if (timing != null)
                {
                    timing.Duration = newDuration;
                    await _context.SaveChangesAsync();

                    Logger.Info("🔧 手动修正原图时长: BaseImageId={BaseImageId}, {FromId} -> {ToId}, NewDuration={Duration}s",
                        baseImageId, fromImageId, toImageId, newDuration);
                    
                    return true;
                }
                else
                {
                    Logger.Warning("未找到原图时间记录: BaseImageId={BaseImageId}, {FromId} -> {ToId}",
                        baseImageId, fromImageId, toImageId);
                    Logger.Warning("  查询条件: BaseImageId={BaseImageId} AND FromImageId={FromId} AND ToImageId={ToId}",
                        baseImageId, fromImageId, toImageId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "手动修正原图时长失败: BaseImageId={BaseImageId}, {FromId} -> {ToId}",
                    baseImageId, fromImageId, toImageId);
                return false;
            }
        }

        /// <summary>
        /// 通过相似图片组中的任意一张图片ID，查找该组的BaseImageId
        /// 查询逻辑：在 original_mode_timings 表中查找包含该图片ID的记录（from_image_id 或 to_image_id）
        /// </summary>
        public async Task<int?> FindBaseImageIdBySimilarImageAsync(int similarImageId)
        {
            try
            {
                // 首先检查该图片ID是否就是BaseImageId
                var asBase = await _dbSet
                    .Where(t => t.BaseImageId == similarImageId)
                    .FirstOrDefaultAsync();
                
                if (asBase != null)
                {
                    Logger.Debug("图片{ImageId}本身就是BaseImageId", similarImageId);
                    return similarImageId;
                }

                // 查找from_image_id = similarImageId的记录
                var asFrom = await _dbSet
                    .Where(t => t.FromImageId == similarImageId)
                    .FirstOrDefaultAsync();
                
                if (asFrom != null)
                {
                    Logger.Debug("通过FromImageId找到BaseImageId: {SimilarId} -> {BaseId}", 
                        similarImageId, asFrom.BaseImageId);
                    return asFrom.BaseImageId;
                }

                // 查找to_image_id = similarImageId的记录
                var asTo = await _dbSet
                    .Where(t => t.ToImageId == similarImageId)
                    .FirstOrDefaultAsync();
                
                if (asTo != null)
                {
                    Logger.Debug("通过ToImageId找到BaseImageId: {SimilarId} -> {BaseId}", 
                        similarImageId, asTo.BaseImageId);
                    return asTo.BaseImageId;
                }

                Logger.Debug("未找到图片{ImageId}对应的BaseImageId", similarImageId);
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "查找BaseImageId失败: SimilarImageId={ImageId}", similarImageId);
                return null;
            }
        }
    }
}

