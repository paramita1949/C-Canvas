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
            return await _dbSet
                .FirstOrDefaultAsync(m => m.Path == path);
        }

        /// <summary>
        /// 获取指定文件夹的所有媒体文件
        /// </summary>
        public async Task<List<MediaFile>> GetMediaFilesByFolderIdAsync(int? folderId, bool includeSubfolders = false)
        {
            var query = _dbSet.AsQueryable();

            if (folderId.HasValue)
            {
                if (includeSubfolders)
                {
                    var folderIds = await GetFolderAndDescendantIdsAsync(folderId.Value);
                    if (!folderIds.Any())
                    {
                        return new List<MediaFile>();
                    }

                    query = query.Where(m => m.FolderId.HasValue && folderIds.Contains(m.FolderId.Value));
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

        /// <summary>
        /// 根据文件名搜索媒体文件
        /// </summary>
        public async Task<List<MediaFile>> SearchByNameAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return new List<MediaFile>();

            return await _dbSet
                .Where(m => EF.Functions.Like(m.Name, $"%{searchTerm}%"))
                .OrderBy(m => m.Name)
                .ToListAsync();
        }

        /// <summary>
        /// 获取指定类型的媒体文件
        /// </summary>
        public async Task<List<MediaFile>> GetMediaFilesByTypeAsync(string fileType)
        {
            return await _dbSet
                .Where(m => m.FileTypeString == fileType)
                .OrderBy(m => m.OrderIndex)
                .ThenBy(m => m.Name)
                .ToListAsync();
        }

        /// <summary>
        /// 批量导入媒体文件
        /// </summary>
        public async Task<int> BatchImportAsync(List<MediaFile> mediaFiles)
        {
            if (!mediaFiles.Any())
                return 0;

            await _dbSet.AddRangeAsync(mediaFiles);
            var count = await _context.SaveChangesAsync();

            return count;
        }

        /// <summary>
        /// 更新媒体文件排序
        /// </summary>
        public async Task UpdateOrderIndexesAsync(List<MediaFile> mediaFiles)
        {
            foreach (var file in mediaFiles)
            {
                _dbSet.Update(file);
            }
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// 获取指定文件夹及其所有子文件夹ID（基于路径前缀）
        /// </summary>
        private async Task<HashSet<int>> GetFolderAndDescendantIdsAsync(int folderId)
        {
            var folders = await _context.Folders
                .Select(f => new { f.Id, f.Path })
                .ToListAsync();

            var rootFolder = folders.FirstOrDefault(f => f.Id == folderId);
            if (rootFolder == null || string.IsNullOrWhiteSpace(rootFolder.Path))
            {
                return new HashSet<int>();
            }

            var rootPath = NormalizeFolderPath(rootFolder.Path);

            return folders
                .Where(f => !string.IsNullOrWhiteSpace(f.Path))
                .Where(f =>
                {
                    var currentPath = NormalizeFolderPath(f.Path);
                    return currentPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
                        || currentPath.StartsWith(rootPath + "\\", StringComparison.OrdinalIgnoreCase);
                })
                .Select(f => f.Id)
                .ToHashSet();
        }

        private static string NormalizeFolderPath(string path)
        {
            return path.Replace('/', '\\').TrimEnd('\\');
        }
    }
}

