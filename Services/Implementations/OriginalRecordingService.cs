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
    /// 原图模式录制服务
    /// 参考Python版本：LOGIC_ANALYSIS_04 行119-323
    /// </summary>
    public class OriginalRecordingService : IRecordingService
    {
        private readonly IOriginalModeRepository _originalModeRepository;
        private readonly Stopwatch _stopwatch;

        private int _currentBaseImageId;
        private List<OriginalTimingSequenceDto> _recordingData;
        private int _lastSimilarImageId;
        private int _sequenceOrder;

        /// <summary>
        /// 当前录制模式
        /// </summary>
        public PlaybackMode Mode => PlaybackMode.Original;

        /// <summary>
        /// 是否正在录制
        /// </summary>
        public bool IsRecording { get; private set; }

        public OriginalRecordingService(IOriginalModeRepository originalModeRepository)
        {
            _originalModeRepository = originalModeRepository ?? throw new ArgumentNullException(nameof(originalModeRepository));
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// 开始录制
        /// </summary>
        public async Task StartRecordingAsync(int imageId, PlaybackMode mode)
        {
            if (mode != PlaybackMode.Original)
                throw new ArgumentException("此服务仅支持原图模式", nameof(mode));

            if (IsRecording)
            {
                Logger.Warning("已在录制中，忽略重复启动");
                return;
            }

            // 检查是否有相似图片
            var similarImages = await _originalModeRepository.GetSimilarImagesAsync(imageId);
            if (similarImages == null || similarImages.Count <= 1)
            {
                Logger.Warning("图片{ImageId}没有相似图片，无法录制原图模式", imageId);
                return;
            }

            _currentBaseImageId = imageId;
            _recordingData = new List<OriginalTimingSequenceDto>();
            _lastSimilarImageId = imageId; // 从主图开始
            _sequenceOrder = 0;

            IsRecording = true;
            _stopwatch.Restart();

            Logger.Info("开始原图录制: BaseImageId={ImageId}, 相似图片数量={Count}", 
                imageId, similarImages.Count);
        }

        /// <summary>
        /// 记录时间（图片切换）
        /// </summary>
        public Task RecordTimingAsync(int targetImageId)
        {
            if (!IsRecording)
                return Task.CompletedTask;

            // 计算时长
            var duration = _stopwatch.Elapsed.TotalSeconds;

            // 记录上一张图片的停留时间
            if (duration > 0)
            {
                var timingDto = new OriginalTimingSequenceDto
                {
                    BaseImageId = _currentBaseImageId,
                    FromImageId = _lastSimilarImageId,  // 从哪张图片切换
                    ToImageId = targetImageId,           // 切换到哪张图片
                    Duration = duration,
                    SequenceOrder = _sequenceOrder,
                    CreatedAt = DateTime.Now
                };

                _recordingData.Add(timingDto);
                Logger.Debug("记录原图时间: {FromId} -> {ToId}, Duration={Duration}s", 
                    _lastSimilarImageId, targetImageId, duration);

                _sequenceOrder++;
            }

            // 更新状态
            _lastSimilarImageId = targetImageId;
            _stopwatch.Restart();
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止录制
        /// </summary>
        public async Task StopRecordingAsync()
        {
            if (!IsRecording)
                return;

            // 记录最后一张图片的时长
            var duration = _stopwatch.Elapsed.TotalSeconds;
            if (duration > 0 && _lastSimilarImageId > 0)
            {
                // 最后一张图片通常回到第一张，形成循环
                var timingDto = new OriginalTimingSequenceDto
                {
                    BaseImageId = _currentBaseImageId,
                    FromImageId = _lastSimilarImageId,  // 从最后一张图片
                    ToImageId = _currentBaseImageId,     // 回到主图
                    Duration = duration,
                    SequenceOrder = _sequenceOrder,
                    CreatedAt = DateTime.Now
                };

                _recordingData.Add(timingDto);
            }

            // 保存到数据库
            if (_recordingData.Any())
            {
                await _originalModeRepository.BatchSaveOriginalTimingsAsync(_currentBaseImageId, _recordingData);
                Logger.Info("原图录制完成: BaseImageId={ImageId}, 共录制{Count}个时间点", 
                    _currentBaseImageId, _recordingData.Count);
            }
            else
            {
                Logger.Warning("录制结束但无数据");
            }

            // 重置状态
            IsRecording = false;
            _stopwatch.Stop();
            _recordingData.Clear();
            _sequenceOrder = 0;
            _lastSimilarImageId = 0;
        }

        /// <summary>
        /// 清除时间数据
        /// </summary>
        public async Task ClearTimingDataAsync(int imageId, PlaybackMode mode)
        {
            if (mode != PlaybackMode.Original)
                return;

            await _originalModeRepository.ClearOriginalTimingsByBaseIdAsync(imageId);
            Logger.Info("清除原图时间数据: BaseImageId={ImageId}", imageId);
        }
    }
}

