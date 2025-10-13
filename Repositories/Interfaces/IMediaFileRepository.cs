using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Repositories.Interfaces
{
    /// <summary>
    /// 媒体文件仓储接口
    /// </summary>
    public interface IMediaFileRepository : IRepository<MediaFile>
    {
        /// <summary>
        /// 根据路径获取媒体文件
        /// </summary>
        Task<MediaFile> GetByPathAsync(string path);

        /// <summary>
        /// 获取指定文件夹的所有媒体文件
        /// </summary>
        Task<List<MediaFile>> GetMediaFilesByFolderIdAsync(int? folderId, bool includeSubfolders = false);

        /// <summary>
        /// 根据文件名搜索媒体文件
        /// </summary>
        Task<List<MediaFile>> SearchByNameAsync(string searchTerm);

        /// <summary>
        /// 获取指定类型的媒体文件
        /// </summary>
        Task<List<MediaFile>> GetMediaFilesByTypeAsync(string fileType);

        /// <summary>
        /// 批量导入媒体文件
        /// </summary>
        Task<int> BatchImportAsync(List<MediaFile> mediaFiles);

        /// <summary>
        /// 更新媒体文件排序
        /// </summary>
        Task UpdateOrderIndexesAsync(List<MediaFile> mediaFiles);
    }
}

