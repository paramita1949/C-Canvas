using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.DTOs;

namespace ImageColorChanger.Repositories.Interfaces
{
    /// <summary>
    /// 原图模式仓储接口
    /// </summary>
    public interface IOriginalModeRepository : IRepository<OriginalModeTiming>
    {
        /// <summary>
        /// 获取原图模式时间序列
        /// </summary>
        Task<List<OriginalTimingSequenceDto>> GetOriginalTimingSequenceAsync(int baseImageId);

        /// <summary>
        /// 检查是否存在原图时间数据
        /// </summary>
        Task<bool> HasOriginalTimingDataAsync(int baseImageId);

        /// <summary>
        /// 清除原图模式时间数据
        /// </summary>
        Task ClearOriginalTimingsByBaseIdAsync(int baseImageId);

        /// <summary>
        /// 批量保存原图时间序列
        /// </summary>
        Task BatchSaveOriginalTimingsAsync(int baseImageId, List<OriginalTimingSequenceDto> timings);

        /// <summary>
        /// 获取相似图片列表
        /// </summary>
        Task<List<SimilarImageDto>> GetSimilarImagesAsync(int imageId);

        /// <summary>
        /// 根据文件名模式查找相似图片
        /// </summary>
        Task<List<MediaFile>> FindSimilarImagesByPatternAsync(string imagePath, int? folderId);

        /// <summary>
        /// 更新原图模式时间记录的持续时间
        /// 用于暂停时间累加
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <param name="similarImageId">相似图片ID（目标图片）</param>
        /// <param name="newDuration">新的持续时间（秒）</param>
        Task UpdateOriginalDurationAsync(int baseImageId, int similarImageId, double newDuration);

        /// <summary>
        /// 更新原图模式时间记录（手动跳转时间修正）
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <param name="fromImageId">源图片ID</param>
        /// <param name="toImageId">目标图片ID</param>
        /// <param name="newDuration">新的停留时间（秒）</param>
        /// <returns>是否更新成功</returns>
        Task<bool> UpdateTimingDurationAsync(int baseImageId, int fromImageId, int toImageId, double newDuration);

        /// <summary>
        /// 通过相似图片组中的任意一张图片ID，查找该组的BaseImageId
        /// </summary>
        /// <param name="similarImageId">相似图片ID（可以是组中任意一张）</param>
        /// <returns>BaseImageId，如果未找到返回null</returns>
        Task<int?> FindBaseImageIdBySimilarImageAsync(int similarImageId);
    }
}

