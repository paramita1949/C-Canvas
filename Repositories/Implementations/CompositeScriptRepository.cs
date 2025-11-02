using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Repositories.Interfaces;

namespace ImageColorChanger.Repositories.Implementations
{
    /// <summary>
    /// 合成播放脚本仓储实现
    /// </summary>
    public class CompositeScriptRepository : ICompositeScriptRepository
    {
        private readonly CanvasDbContext _context;

        public CompositeScriptRepository(CanvasDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// 根据图片ID获取合成脚本
        /// </summary>
        public async Task<CompositeScript> GetByImageIdAsync(int imageId)
        {
            try
            {
                return await _context.CompositeScripts
                    .FirstOrDefaultAsync(s => s.ImageId == imageId);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 创建或更新合成脚本
        /// </summary>
        public async Task<CompositeScript> CreateOrUpdateAsync(int imageId, double totalDuration, bool autoCalculate)
        {
            try
            {
                var existing = await GetByImageIdAsync(imageId);

                if (existing != null)
                {
                    // 更新现有记录
                    existing.TotalDuration = totalDuration;
                    existing.AutoCalculate = autoCalculate;
                    existing.UpdatedAt = DateTime.Now;

                    _context.CompositeScripts.Update(existing);
                    await _context.SaveChangesAsync();

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ 更新合成脚本: ImageId={imageId}, TotalDuration={totalDuration:F2}秒, AutoCalculate={autoCalculate}");
                    #endif

                    return existing;
                }
                else
                {
                    // 创建新记录
                    var newScript = new CompositeScript
                    {
                        ImageId = imageId,
                        TotalDuration = totalDuration,
                        AutoCalculate = autoCalculate,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };

                    await _context.CompositeScripts.AddAsync(newScript);
                    await _context.SaveChangesAsync();

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ 创建合成脚本: ImageId={imageId}, TotalDuration={totalDuration:F2}秒, AutoCalculate={autoCalculate}");
                    #endif

                    return newScript;
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 更新总时长
        /// </summary>
        public async Task UpdateTotalDurationAsync(int imageId, double totalDuration)
        {
            try
            {
                var script = await GetByImageIdAsync(imageId);

                if (script != null)
                {
                    script.TotalDuration = totalDuration;
                    script.UpdatedAt = DateTime.Now;

                    _context.CompositeScripts.Update(script);
                    await _context.SaveChangesAsync();

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ 更新合成脚本总时长: ImageId={imageId}, TotalDuration={totalDuration:F2}秒");
                    #endif
                }
                else
                {
                    // 如果不存在，创建一个默认的
                    await CreateOrUpdateAsync(imageId, totalDuration, false);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 删除合成脚本
        /// </summary>
        public async Task DeleteAsync(int imageId)
        {
            try
            {
                var script = await GetByImageIdAsync(imageId);

                if (script != null)
                {
                    _context.CompositeScripts.Remove(script);
                    await _context.SaveChangesAsync();

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ 删除合成脚本: ImageId={imageId}");
                    #endif
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 检查是否存在合成脚本
        /// </summary>
        public async Task<bool> ExistsAsync(int imageId)
        {
            try
            {
                return await _context.CompositeScripts
                    .AnyAsync(s => s.ImageId == imageId);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}

