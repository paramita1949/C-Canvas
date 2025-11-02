using System.Threading.Tasks;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Repositories.Interfaces
{
    /// <summary>
    /// 合成播放脚本仓储接口
    /// </summary>
    public interface ICompositeScriptRepository
    {
        /// <summary>
        /// 根据图片ID获取合成脚本
        /// </summary>
        Task<CompositeScript> GetByImageIdAsync(int imageId);

        /// <summary>
        /// 创建或更新合成脚本
        /// </summary>
        Task<CompositeScript> CreateOrUpdateAsync(int imageId, double totalDuration, bool autoCalculate);

        /// <summary>
        /// 更新总时长
        /// </summary>
        Task UpdateTotalDurationAsync(int imageId, double totalDuration);

        /// <summary>
        /// 删除合成脚本
        /// </summary>
        Task DeleteAsync(int imageId);

        /// <summary>
        /// 检查是否存在合成脚本
        /// </summary>
        Task<bool> ExistsAsync(int imageId);
    }
}

