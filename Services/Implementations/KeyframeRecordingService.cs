using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.DTOs;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Repositories.Interfaces;
using ImageColorChanger.Services.Interfaces;
using ImageColorChanger.Utils;

namespace ImageColorChanger.Services.Implementations
{
    /// <summary>
    /// 关键帧录制服务
    /// 参考Python版本：LOGIC_ANALYSIS_03 行22-217
    /// </summary>
    public class KeyframeRecordingService : IRecordingService
    {
        private readonly IKeyframeRepository _keyframeRepository;
        private readonly ITimingRepository _timingRepository;
        private readonly Stopwatch _stopwatch;

        private int _currentImageId;
        private List<TimingSequenceDto> _recordingData;
        private int _currentKeyframeIndex;

        /// <summary>
        /// 当前录制模式
        /// </summary>
        public PlaybackMode Mode => PlaybackMode.Keyframe;

        /// <summary>
        /// 是否正在录制
        /// </summary>
        public bool IsRecording { get; private set; }

        public KeyframeRecordingService(
            IKeyframeRepository keyframeRepository,
            ITimingRepository timingRepository)
        {
            _keyframeRepository = keyframeRepository ?? throw new ArgumentNullException(nameof(keyframeRepository));
            _timingRepository = timingRepository ?? throw new ArgumentNullException(nameof(timingRepository));
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// 开始录制
        /// </summary>
        public async Task StartRecordingAsync(int imageId, PlaybackMode mode)
        {
            if (mode != PlaybackMode.Keyframe)
                throw new ArgumentException("此服务仅支持关键帧模式", nameof(mode));

            if (IsRecording)
            {
                return;
            }

            // 检查关键帧是否存在
            var keyframes = await _keyframeRepository.GetKeyframesByImageIdAsync(imageId);
            if (keyframes == null || !keyframes.Any())
            {
                return;
            }

            _currentImageId = imageId;
            _recordingData = new List<TimingSequenceDto>();
            _currentKeyframeIndex = -1;

            IsRecording = true;
            _stopwatch.Restart();

        }

        /// <summary>
        /// 记录时间（关键帧切换前调用，记录当前帧停留时间）
        /// 参考Python版本：keyframe_navigation.py 第49-57行
        /// </summary>
        public async Task RecordTimingAsync(int keyframeId)
        {
            if (!IsRecording)
                return;

            try
            {
                // 计算停留时长
                var duration = _stopwatch.Elapsed.TotalSeconds;

                // 记录当前关键帧的停留时长（即时记录模式）
                if (duration > 0)
                {
                    //System.Diagnostics.Debug.WriteLine($"🔍 [RecordTiming] 查询关键帧 {keyframeId}...");
                    var keyframe = await _keyframeRepository.GetByIdAsync(keyframeId);
                    if (keyframe != null)
                    {
                        var timingDto = new TimingSequenceDto
                        {
                            KeyframeId = keyframeId,
                            Duration = duration,
                            SequenceOrder = _recordingData.Count,
                            Position = keyframe.Position,
                            YPosition = keyframe.YPosition,
                            LoopCount = keyframe.LoopCount,
                            CreatedAt = DateTime.Now
                        };

                        _recordingData.Add(timingDto);
                    }
                }

                // 重启计时器，为下一帧计时
                _stopwatch.Restart();
                _currentKeyframeIndex++;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ [RecordTiming] 错误详情: {ex}");
                throw;
            }
        }

        /// <summary>
        /// 停止录制
        /// 参考Python版本：playback_controller.py 第58-66行
        /// </summary>
        public async Task StopRecordingAsync()
        {
            if (!IsRecording)
                return;

            // 保存到数据库（最后一帧已经在循环检测时记录）
            if (_recordingData.Any())
            {
                await _timingRepository.BatchSaveTimingsAsync(_currentImageId, _recordingData);
            }
            else
            {
            }

            // 重置状态
            IsRecording = false;
            _stopwatch.Stop();
            _recordingData.Clear();
            _currentKeyframeIndex = -1;
        }

        /// <summary>
        /// 清除时间数据
        /// </summary>
        public async Task ClearTimingDataAsync(int imageId, PlaybackMode mode)
        {
            if (mode != PlaybackMode.Keyframe)
                return;

            await _timingRepository.ClearTimingsByImageIdAsync(imageId);
        }
    }
}

