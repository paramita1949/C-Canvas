using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.Services.Interfaces
{
    /// <summary>
    /// 录制服务接口
    /// </summary>
    public interface IRecordingService
    {
        /// <summary>
        /// 当前录制模式
        /// </summary>
        PlaybackMode Mode { get; }

        /// <summary>
        /// 是否正在录制
        /// </summary>
        bool IsRecording { get; }

        /// <summary>
        /// 开始录制
        /// </summary>
        Task StartRecordingAsync(int imageId, PlaybackMode mode);

        /// <summary>
        /// 记录时间（关键帧切换或图片切换）
        /// </summary>
        Task RecordTimingAsync(int targetId);

        /// <summary>
        /// 停止录制
        /// </summary>
        Task StopRecordingAsync();

        /// <summary>
        /// 清除时间数据
        /// </summary>
        Task ClearTimingDataAsync(int imageId, PlaybackMode mode);
    }
}

