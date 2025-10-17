using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Repositories.Interfaces
{
    /// <summary>
    /// 关键帧仓储接口
    /// </summary>
    public interface IKeyframeRepository : IRepository<Keyframe>
    {
        /// <summary>
        /// 获取指定图片的所有关键帧（按顺序）- 异步
        /// </summary>
        Task<List<Keyframe>> GetKeyframesByImageIdAsync(int imageId);
        
        /// <summary>
        /// 获取指定图片的所有关键帧（按顺序）- 同步
        /// </summary>
        List<Keyframe> GetKeyframesByImageId(int imageId);

        /// <summary>
        /// 获取指定图片的关键帧数量
        /// </summary>
        Task<int> GetKeyframeCountAsync(int imageId);

        /// <summary>
        /// 批量更新关键帧排序
        /// </summary>
        Task UpdateKeyframeOrdersAsync(List<Keyframe> keyframes);

        /// <summary>
        /// 删除指定图片的所有关键帧
        /// </summary>
        Task DeleteKeyframesByImageIdAsync(int imageId);

        /// <summary>
        /// 根据位置查找最接近的关键帧
        /// </summary>
        Task<Keyframe> FindClosestKeyframeAsync(int imageId, double position);
    }
}

