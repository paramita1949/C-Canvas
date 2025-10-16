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
    /// 媒体文件仓储实现
    /// </summary>
    public class MediaFileRepositoryImpl : RepositoryBase<MediaFile>, IMediaFileRepository
    {
        public MediaFileRepositoryImpl(CanvasDbContext context) : base(context)
        {
        }

        /// <summary>
        /// 根据路径获取媒体文件
        /// </summary>
        public async Task<MediaFile> GetByPathAsync(string path)
        {
            try
            {
                return await _dbSet
                    .FirstOrDefaultAsync(m => m.Path == path);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 获取指定文件夹的所有媒体文件
        /// </summary>
        public async Task<List<MediaFile>> GetMediaFilesByFolderIdAsync(int? folderId, bool includeSubfolders = false)
        {
            try
            {
                var query = _dbSet.AsQueryable();

                if (folderId.HasValue)
                {
                    if (includeSubfolders)
                    {
                        // TODO: 实现递归查询子文件夹
                        query = query.Where(m => m.FolderId == folderId.Value);
                    }
                    else
                    {
                        query = query.Where(m => m.FolderId == folderId.Value);
                    }
                }
                else
                {
                    // folderId为null表示根目录
                    query = query.Where(m => m.FolderId == null);
                }

                return await query
                    .OrderBy(m => m.OrderIndex)
                    .ThenBy(m => m.Name)
                    .ToListAsync();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 根据文件名搜索媒体文件
        /// </summary>
        public async Task<List<MediaFile>> SearchByNameAsync(string searchTerm)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                    return new List<MediaFile>();

                return await _dbSet
                    .Where(m => EF.Functions.Like(m.Name, $"%{searchTerm}%"))
                    .OrderBy(m => m.Name)
                    .ToListAsync();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 获取指定类型的媒体文件
        /// </summary>
        public async Task<List<MediaFile>> GetMediaFilesByTypeAsync(string fileType)
        {
            try
            {
                return await _dbSet
                    .Where(m => m.FileTypeString == fileType)
                    .OrderBy(m => m.OrderIndex)
                    .ThenBy(m => m.Name)
                    .ToListAsync();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 批量导入媒体文件
        /// </summary>
        public async Task<int> BatchImportAsync(List<MediaFile> mediaFiles)
        {
            try
            {
                if (!mediaFiles.Any())
                    return 0;

                await _dbSet.AddRangeAsync(mediaFiles);
                var count = await _context.SaveChangesAsync();

                return count;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 更新媒体文件排序
        /// </summary>
        public async Task UpdateOrderIndexesAsync(List<MediaFile> mediaFiles)
        {
            try
            {
                foreach (var file in mediaFiles)
                {
                    _dbSet.Update(file);
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}

