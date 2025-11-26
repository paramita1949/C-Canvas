using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.DTOs;

namespace ImageColorChanger.Repositories.Interfaces
{
    /// <summary>
    /// 关键帧时间记录仓储接口
    /// </summary>
    public interface ITimingRepository : IRepository<KeyframeTiming>
    {
        /// <summary>
        /// 获取指定图片的时间序列（按顺序）
        /// </summary>
        Task<List<TimingSequenceDto>> GetTimingSequenceAsync(int imageId);

        /// <summary>
        /// 检查是否存在时间数据
        /// </summary>
        Task<bool> HasTimingDataAsync(int imageId);

        /// <summary>
        /// 清除指定图片的所有时间数据
        /// </summary>
        Task ClearTimingsByImageIdAsync(int imageId);

        /// <summary>
        /// 批量保存时间序列
        /// </summary>
        Task BatchSaveTimingsAsync(int imageId, List<TimingSequenceDto> timings);

        /// <summary>
        /// 更新指定关键帧的持续时间（通过ImageId和SequenceOrder精确定位，支持跳帧录制）
        /// </summary>
        Task UpdateDurationAsync(int imageId, int sequenceOrder, double newDuration);
        
        /// <summary>
        /// 更新指定关键帧的持续时间（旧方法，仅通过KeyframeId，可能匹配到错误的记录）
        /// </summary>
        [Obsolete("使用 UpdateDurationAsync(int imageId, int sequenceOrder, double newDuration) 替代，以支持跳帧录制")]
        Task UpdateDurationAsync(int keyframeId, double newDuration);

        /// <summary>
        /// 更新暂停时间
        /// </summary>
        Task UpdatePausedTimeAsync(int imageId, int sequenceOrder, double pausedTime);
    }
}

