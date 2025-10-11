using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers.Keyframes;

namespace ImageColorChanger.Managers.Playback
{
    /// <summary>
    /// 时间录制器
    /// 负责录制关键帧的停留时间和原图模式的切换时间
    /// </summary>
    public class TimeRecorder
    {
        private readonly KeyframeRepository _repository;

        #region 普通模式录制状态

        /// <summary>
        /// 是否正在录制
        /// </summary>
        public bool IsRecording { get; private set; }

        /// <summary>
        /// 当前录制的图片ID
        /// </summary>
        public int? CurrentImageId { get; private set; }

        private DateTime? _recordingStartTime;
        private DateTime? _lastKeyframeTime;
        private int _currentKeyframeIndex;
        private List<TimingInfo> _timingSequence = new();

        #endregion

        #region 原图模式录制状态

        /// <summary>
        /// 是否正在原图模式录制
        /// </summary>
        public bool IsOriginalModeRecording { get; private set; }

        private List<OriginalTimingInfo> _originalModeSequence = new();
        private List<(int ImageId, string Name, string Path)> _similarImages = new();
        private DateTime? _lastImageSwitchTime;

        #endregion

        #region 内部数据结构

        /// <summary>
        /// 时间信息
        /// </summary>
        private class TimingInfo
        {
            public int KeyframeId { get; set; }
            public double Duration { get; set; }
            public int SequenceOrder { get; set; }
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// 原图模式时间信息
        /// </summary>
        private class OriginalTimingInfo
        {
            public int FromImageId { get; set; }
            public int ToImageId { get; set; }
            public double Duration { get; set; }
            public int SequenceOrder { get; set; }
            public DateTime Timestamp { get; set; }
        }

        #endregion

        public TimeRecorder(KeyframeRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        #region 普通模式录制

        /// <summary>
        /// 开始录制时间差
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否成功开始</returns>
        public async Task<bool> StartRecordingAsync(int imageId)
        {
            if (IsRecording)
            {
                Console.WriteLine("⚠️ 已经在录制中，请先停止当前录制");
                return false;
            }

            // 清除旧的时间记录
            await _repository.ClearTimingDataAsync(imageId);

            CurrentImageId = imageId;
            IsRecording = true;
            _recordingStartTime = DateTime.Now;
            _lastKeyframeTime = _recordingStartTime;
            _currentKeyframeIndex = 0;
            _timingSequence.Clear();

            Console.WriteLine($"🎬 开始录制图片 {imageId} 的关键帧时间差");
            return true;
        }

        /// <summary>
        /// 停止录制
        /// </summary>
        /// <returns>是否成功停止</returns>
        public async Task<bool> StopRecordingAsync()
        {
            if (!IsRecording)
            {
                Console.WriteLine("⚠️ 当前没有在录制");
                return false;
            }

            IsRecording = false;

            // 保存录制的时间序列到数据库
            if (_timingSequence.Count > 0)
            {
                await SaveTimingSequenceAsync();
                Console.WriteLine($"✅ 停止录制，共录制了 {_timingSequence.Count} 个时间间隔");
            }
            else
            {
                Console.WriteLine("⚠️ 停止录制，但没有记录任何时间间隔");
            }

            // 重置状态
            CurrentImageId = null;
            _recordingStartTime = null;
            _lastKeyframeTime = null;
            _currentKeyframeIndex = 0;
            _timingSequence.Clear();

            return true;
        }

        /// <summary>
        /// 记录关键帧停留时间
        /// </summary>
        /// <param name="keyframeId">关键帧ID</param>
        /// <param name="manualDuration">手动指定的时间（秒），如果不提供则自动计算</param>
        /// <returns>是否成功记录</returns>
        public bool RecordKeyframeTiming(int keyframeId, double? manualDuration = null)
        {
            if (!IsRecording)
            {
                Console.WriteLine("⚠️ 未在录制状态，无法记录时间");
                return false;
            }

            var currentTime = DateTime.Now;
            double duration;

            if (manualDuration.HasValue)
            {
                duration = manualDuration.Value;
            }
            else
            {
                // 计算从上一个关键帧到现在的时间间隔
                if (_lastKeyframeTime.HasValue)
                {
                    duration = (currentTime - _lastKeyframeTime.Value).TotalSeconds;
                }
                else
                {
                    duration = 0.0;
                }
            }

            // 记录到时间序列
            var timingInfo = new TimingInfo
            {
                KeyframeId = keyframeId,
                Duration = duration,
                SequenceOrder = _currentKeyframeIndex,
                Timestamp = currentTime
            };

            _timingSequence.Add(timingInfo);

            // 更新状态
            _lastKeyframeTime = currentTime;
            _currentKeyframeIndex++;

            Console.WriteLine($"📝 记录关键帧 {keyframeId} 停留时间: {duration:F2}秒");
            return true;
        }

        /// <summary>
        /// 保存时间序列到数据库
        /// </summary>
        private async Task SaveTimingSequenceAsync()
        {
            if (!CurrentImageId.HasValue || _timingSequence.Count == 0)
                return;

            try
            {
                var timings = _timingSequence.Select(t => new KeyframeTiming
                {
                    ImageId = CurrentImageId.Value,
                    KeyframeId = t.KeyframeId,
                    Duration = t.Duration,
                    SequenceOrder = t.SequenceOrder,
                    CreatedAt = t.Timestamp
                }).ToList();

                await _repository.SaveTimingSequenceAsync(CurrentImageId.Value, timings);
                Console.WriteLine($"✅ 成功保存 {timings.Count} 个时间记录到数据库");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 保存时间序列失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取时间序列
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>时间序列</returns>
        public async Task<List<KeyframeTiming>> GetTimingSequenceAsync(int imageId)
        {
            return await _repository.GetTimingSequenceAsync(imageId);
        }

        /// <summary>
        /// 检查是否有时间数据
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否有时间数据</returns>
        public async Task<bool> HasTimingDataAsync(int imageId)
        {
            return await _repository.HasTimingDataAsync(imageId);
        }

        /// <summary>
        /// 清除时间数据
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否成功清除</returns>
        public async Task<bool> ClearTimingDataAsync(int imageId)
        {
            return await _repository.ClearTimingDataAsync(imageId);
        }

        #endregion

        #region 实时更新时间

        /// <summary>
        /// 实时更新数据库中的关键帧时间
        /// 用于播放时的手动修正和暂停增加时间
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <param name="keyframeId">关键帧ID</param>
        /// <param name="newDuration">新的停留时间（秒）</param>
        /// <returns>是否成功更新</returns>
        public async Task<bool> UpdateKeyframeTimingInDbAsync(int imageId, int keyframeId, double newDuration)
        {
            try
            {
                var success = await _repository.UpdateKeyframeTimingAsync(imageId, keyframeId, newDuration);
                
                if (success)
                {
                    Console.WriteLine($"⏱️ 实时修正关键帧 {keyframeId} 的停留时间为: {newDuration:F2}秒");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 更新关键帧时间失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 原图模式录制

        /// <summary>
        /// 开始原图模式录制
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <param name="similarImages">相似图片列表</param>
        /// <param name="markType">标记类型</param>
        /// <returns>是否成功开始</returns>
        public async Task<bool> StartOriginalModeRecordingAsync(
            int baseImageId,
            List<(int ImageId, string Name, string Path)> similarImages,
            MarkType markType = MarkType.Loop)
        {
            if (IsOriginalModeRecording)
            {
                Console.WriteLine("⚠️ 原图模式已经在录制中，请先停止当前录制");
                return false;
            }

            if (similarImages == null || similarImages.Count == 0)
            {
                Console.WriteLine("❌ 没有相似图片，无法开始原图模式录制");
                return false;
            }

            // 清除旧的原图模式时间记录
            await _repository.ClearOriginalModeTimingDataAsync(baseImageId);

            IsOriginalModeRecording = true;
            CurrentImageId = baseImageId;
            _similarImages = similarImages;
            _originalModeSequence.Clear();
            _recordingStartTime = DateTime.Now;
            _lastImageSwitchTime = _recordingStartTime;

            Console.WriteLine($"🎬 开始原图模式录制，基础图片ID: {baseImageId}，相似图片数量: {similarImages.Count}，标记类型: {markType}");
            return true;
        }

        /// <summary>
        /// 停止原图模式录制
        /// </summary>
        /// <returns>是否成功停止</returns>
        public async Task<bool> StopOriginalModeRecordingAsync()
        {
            if (!IsOriginalModeRecording)
            {
                Console.WriteLine("⚠️ 当前没有在原图模式录制");
                return false;
            }

            IsOriginalModeRecording = false;

            // 保存录制的时间序列到数据库
            if (_originalModeSequence.Count > 0)
            {
                await SaveOriginalModeSequenceAsync();
                Console.WriteLine($"✅ 停止原图模式录制，共录制了 {_originalModeSequence.Count} 个图片切换时间间隔");
            }
            else
            {
                Console.WriteLine("⚠️ 停止原图模式录制，但没有记录任何时间间隔");
            }

            // 重置状态
            CurrentImageId = null;
            _similarImages.Clear();
            _originalModeSequence.Clear();
            _recordingStartTime = null;
            _lastImageSwitchTime = null;

            return true;
        }

        /// <summary>
        /// 记录图片切换时间（原图模式）
        /// </summary>
        /// <param name="fromImageId">源图片ID</param>
        /// <param name="toImageId">目标图片ID</param>
        /// <param name="manualDuration">手动指定的时间（秒）</param>
        /// <returns>是否成功记录</returns>
        public bool RecordImageSwitchTiming(int fromImageId, int toImageId, double? manualDuration = null)
        {
            if (!IsOriginalModeRecording)
            {
                Console.WriteLine("⚠️ 未在原图模式录制状态，无法记录时间");
                return false;
            }

            var currentTime = DateTime.Now;
            double duration;

            if (manualDuration.HasValue)
            {
                duration = manualDuration.Value;
            }
            else
            {
                // 计算从上一个图片切换到现在的时间间隔
                if (_lastImageSwitchTime.HasValue)
                {
                    duration = (currentTime - _lastImageSwitchTime.Value).TotalSeconds;
                }
                else
                {
                    duration = 0.0;
                }
            }

            // 记录到时间序列
            var timingInfo = new OriginalTimingInfo
            {
                FromImageId = fromImageId,
                ToImageId = toImageId,
                Duration = duration,
                SequenceOrder = _originalModeSequence.Count,
                Timestamp = currentTime
            };

            _originalModeSequence.Add(timingInfo);

            // 更新状态
            _lastImageSwitchTime = currentTime;

            Console.WriteLine($"📝 记录图片切换时间: {fromImageId} → {toImageId}, 停留时间: {duration:F2}秒");
            return true;
        }

        /// <summary>
        /// 保存原图模式时间序列到数据库
        /// </summary>
        private async Task SaveOriginalModeSequenceAsync()
        {
            if (!CurrentImageId.HasValue || _originalModeSequence.Count == 0)
                return;

            try
            {
                var timings = _originalModeSequence.Select(t => new OriginalModeTiming
                {
                    BaseImageId = CurrentImageId.Value,
                    FromImageId = t.FromImageId,
                    ToImageId = t.ToImageId,
                    Duration = t.Duration,
                    SequenceOrder = t.SequenceOrder,
                    MarkTypeString = "loop", // 默认循环模式
                    CreatedAt = t.Timestamp
                }).ToList();

                await _repository.SaveOriginalModeSequenceAsync(CurrentImageId.Value, timings);
                Console.WriteLine($"✅ 成功保存 {timings.Count} 个原图模式时间记录到数据库");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 保存原图模式时间序列失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取原图模式时间序列
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <returns>时间序列</returns>
        public async Task<List<OriginalModeTiming>> GetOriginalModeTimingSequenceAsync(int baseImageId)
        {
            return await _repository.GetOriginalModeTimingSequenceAsync(baseImageId);
        }

        /// <summary>
        /// 检查是否有原图模式时间数据
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <returns>是否有时间数据</returns>
        public async Task<bool> HasOriginalModeTimingDataAsync(int baseImageId)
        {
            return await _repository.HasOriginalModeTimingDataAsync(baseImageId);
        }

        /// <summary>
        /// 清除原图模式时间数据
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <returns>是否成功清除</returns>
        public async Task<bool> ClearOriginalModeTimingDataAsync(int baseImageId)
        {
            return await _repository.ClearOriginalModeTimingDataAsync(baseImageId);
        }

        /// <summary>
        /// 更新原图模式时间
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <param name="fromImageId">源图片ID</param>
        /// <param name="toImageId">目标图片ID</param>
        /// <param name="newDuration">新的停留时间</param>
        /// <returns>是否成功更新</returns>
        public async Task<bool> UpdateOriginalModeTimingInDbAsync(
            int baseImageId, int fromImageId, int toImageId, double newDuration)
        {
            try
            {
                var success = await _repository.UpdateOriginalModeTimingAsync(
                    baseImageId, fromImageId, toImageId, newDuration);
                
                if (success)
                {
                    Console.WriteLine($"⏱️ 实时修正原图模式时间：{fromImageId} → {toImageId} 时间修正为 {newDuration:F2}秒");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 更新原图模式时间失败: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}

