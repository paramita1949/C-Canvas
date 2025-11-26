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
        private readonly Repositories.Interfaces.ICompositeScriptRepository _compositeScriptRepository;
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
            ITimingRepository timingRepository,
            Repositories.Interfaces.ICompositeScriptRepository compositeScriptRepository)
        {
            _keyframeRepository = keyframeRepository ?? throw new ArgumentNullException(nameof(keyframeRepository));
            _timingRepository = timingRepository ?? throw new ArgumentNullException(nameof(timingRepository));
            _compositeScriptRepository = compositeScriptRepository ?? throw new ArgumentNullException(nameof(compositeScriptRepository));
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
                // 修复：即使duration为0也要记录，确保跳帧录制时所有帧都被记录
                // 如果duration为0，说明用户快速切换，这是合法的录制行为
                var keyframe = await _keyframeRepository.GetByIdAsync(keyframeId);
                if (keyframe != null)
                {
                    var timingDto = new TimingSequenceDto
                    {
                        KeyframeId = keyframeId,
                        Duration = duration, // 允许为0，表示快速切换
                        SequenceOrder = _recordingData.Count,
                        Position = keyframe.Position,
                        YPosition = keyframe.YPosition,
                        LoopCount = keyframe.LoopCount,
                        CreatedAt = DateTime.Now
                    };

                    _recordingData.Add(timingDto);
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"📹 [录制] 记录关键帧: KeyframeId={keyframeId}, Duration={duration:F2}秒, SequenceOrder={timingDto.SequenceOrder}, 总记录数={_recordingData.Count}");
                    if (duration <= 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"   ⚠️ [录制] 注意：Duration为0或负数，可能是快速切换");
                    }
                    #endif
                }
                else
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [录制] 找不到关键帧: KeyframeId={keyframeId}");
                    #endif
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
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"💾 [录制完成] 准备保存 {_recordingData.Count} 条记录:");
                for (int i = 0; i < _recordingData.Count; i++)
                {
                    var t = _recordingData[i];
                    System.Diagnostics.Debug.WriteLine($"   #{i + 1}: KeyframeId={t.KeyframeId}, Duration={t.Duration:F2}秒, SequenceOrder={t.SequenceOrder}");
                }
                #endif
                
                await _timingRepository.BatchSaveTimingsAsync(_currentImageId, _recordingData);

                // 🎬 自动更新合成脚本的总时长（从关键帧时间累计）
                double totalDuration = _recordingData.Sum(t => t.Duration);
                await _compositeScriptRepository.CreateOrUpdateAsync(_currentImageId, totalDuration, autoCalculate: true);

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ 录制完成，自动更新合成脚本总时长: {totalDuration:F2}秒");
                #endif
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

