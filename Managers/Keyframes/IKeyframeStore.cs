using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Managers.Keyframes
{
    /// <summary>
    /// 关键帧管理模块所需的最小仓储抽象。
    /// </summary>
    public interface IKeyframeStore
    {
        Task<int> AddKeyframeAsync(int imageId, double position, int yPosition);
        List<Keyframe> GetKeyframesByImageId(int imageId);
        Task<List<Keyframe>> GetKeyframesAsync(int imageId);
        Task<bool> DeleteKeyframeAsync(int keyframeId);
        Task<bool> ClearKeyframesAsync(int imageId);
        Task<bool> UpdateLoopCountAsync(int keyframeId, int? loopCount);
    }
}
