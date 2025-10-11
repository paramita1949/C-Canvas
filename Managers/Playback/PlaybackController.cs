using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ImageColorChanger.Managers.Keyframes;
using ImageColorChanger.UI;

namespace ImageColorChanger.Managers.Playback
{
    /// <summary>
    /// 播放控制器
    /// 协调 TimeRecorder 和 AutoPlayer，统一管理录制和播放流程
    /// </summary>
    public class PlaybackController
    {
        private readonly MainWindow _mainWindow;
        private readonly TimeRecorder _timeRecorder;
        private readonly AutoPlayer _autoPlayer;
        private readonly KeyframeManager _keyframeManager;

        #region 状态属性

        /// <summary>
        /// 是否正在录制时间
        /// </summary>
        public bool IsRecording => _timeRecorder.IsRecording;

        /// <summary>
        /// 是否正在自动播放
        /// </summary>
        public bool IsPlaying => _autoPlayer.IsPlaying;

        /// <summary>
        /// 是否已暂停
        /// </summary>
        public bool IsPaused => _autoPlayer.IsPaused;

        /// <summary>
        /// 是否启用倒计时显示
        /// </summary>
        public bool CountdownEnabled
        {
            get => _autoPlayer.CountdownEnabled;
            set => _autoPlayer.CountdownEnabled = value;
        }

        /// <summary>
        /// 播放速度
        /// </summary>
        public double PlaySpeed
        {
            get => _autoPlayer.PlaySpeed;
            set => _autoPlayer.SetPlaySpeed(value);
        }

        /// <summary>
        /// 目标播放次数（-1表示无限循环）
        /// </summary>
        public int TargetPlayCount
        {
            get => _autoPlayer.TargetPlayCount;
            set => _autoPlayer.TargetPlayCount = value;
        }

        /// <summary>
        /// 已完成的播放次数
        /// </summary>
        public int CompletedPlayCount => _autoPlayer.CompletedPlayCount;

        #endregion

        #region 事件

        /// <summary>
        /// 播放完成事件
        /// </summary>
        public event EventHandler PlayFinished;

        /// <summary>
        /// 录制状态改变事件
        /// </summary>
        public event EventHandler<bool> RecordingStateChanged;

        /// <summary>
        /// 播放状态改变事件
        /// </summary>
        public event EventHandler<bool> PlayingStateChanged;

        #endregion

        public PlaybackController(
            MainWindow mainWindow,
            TimeRecorder timeRecorder,
            AutoPlayer autoPlayer,
            KeyframeManager keyframeManager)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _timeRecorder = timeRecorder ?? throw new ArgumentNullException(nameof(timeRecorder));
            _autoPlayer = autoPlayer ?? throw new ArgumentNullException(nameof(autoPlayer));
            _keyframeManager = keyframeManager ?? throw new ArgumentNullException(nameof(keyframeManager));

            // 订阅播放器事件
            _autoPlayer.PlayFinished += OnAutoPlayFinished;
        }

        #region 时间录制控制

        /// <summary>
        /// 切换时间录制状态
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否成功切换</returns>
        public async Task<bool> ToggleTimingRecordingAsync(int imageId)
        {
            if (IsPlaying)
            {
                ShowStatus("请先停止自动播放");
                return false;
            }

            bool success;

            if (!IsRecording)
            {
                // 开始录制
                success = await _timeRecorder.StartRecordingAsync(imageId);
                if (success)
                {
                    ShowStatus($"📍 开始录制关键帧时间 - 图片ID: {imageId}");
                    RecordingStateChanged?.Invoke(this, true);
                }
            }
            else
            {
                // 停止录制
                success = await _timeRecorder.StopRecordingAsync();
                if (success)
                {
                    ShowStatus("✅ 已停止录制并保存时间序列");
                    RecordingStateChanged?.Invoke(this, false);
                }
            }

            return success;
        }

        /// <summary>
        /// 在关键帧导航时记录时间
        /// </summary>
        /// <param name="keyframeId">关键帧ID</param>
        /// <returns>是否成功记录</returns>
        public bool RecordKeyframeTime(int keyframeId)
        {
            if (!IsRecording)
                return false;

            return _timeRecorder.RecordKeyframeTiming(keyframeId);
        }

        /// <summary>
        /// 检查是否有时间数据
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否有时间数据</returns>
        public async Task<bool> HasTimingDataAsync(int imageId)
        {
            return await _timeRecorder.HasTimingDataAsync(imageId);
        }

        /// <summary>
        /// 清除时间数据
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否成功清除</returns>
        public async Task<bool> ClearTimingDataAsync(int imageId)
        {
            if (IsPlaying)
            {
                ShowStatus("请先停止播放");
                return false;
            }

            if (IsRecording)
            {
                ShowStatus("请先停止录制");
                return false;
            }

            bool success = await _timeRecorder.ClearTimingDataAsync(imageId);
            if (success)
            {
                ShowStatus("✅ 已清除时间数据");
            }

            return success;
        }

        #endregion

        #region 自动播放控制

        /// <summary>
        /// 切换自动播放状态
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>是否成功切换</returns>
        public async Task<bool> ToggleAutoPlayAsync(int imageId)
        {
            if (IsRecording)
            {
                ShowStatus("正在录制中，无法自动播放");
                return false;
            }

            bool success;

            if (!IsPlaying)
            {
                // 检查是否有时间数据
                if (!await HasTimingDataAsync(imageId))
                {
                    ShowStatus("❌ 没有时间数据，请先录制关键帧时间");
                    return false;
                }

                // 开始播放
                success = await _autoPlayer.StartAutoPlayAsync(imageId);
                if (success)
                {
                    ShowStatus($"▶️ 开始自动播放 - 图片ID: {imageId}");
                    PlayingStateChanged?.Invoke(this, true);
                }
            }
            else
            {
                // 停止播放
                success = _autoPlayer.StopAutoPlay();
                if (success)
                {
                    ShowStatus("⏹️ 已停止自动播放");
                    PlayingStateChanged?.Invoke(this, false);
                }
            }

            return success;
        }

        /// <summary>
        /// 切换暂停/继续
        /// </summary>
        /// <returns>是否成功切换</returns>
        public async Task<bool> ToggleCountdownPauseAsync()
        {
            if (!IsPlaying)
            {
                ShowStatus("当前没有在播放");
                return false;
            }

            bool success;

            if (!IsPaused)
            {
                // 暂停
                success = _autoPlayer.PauseAutoPlay();
                if (success)
                {
                    ShowStatus("⏸️ 已暂停播放（时间持续增加中）");
                }
            }
            else
            {
                // 继续
                success = await _autoPlayer.ResumeAutoPlayAsync();
                if (success)
                {
                    ShowStatus("▶️ 已继续播放");
                }
            }

            return success;
        }

        #endregion

        #region 播放设置

        /// <summary>
        /// 设置播放次数
        /// </summary>
        /// <param name="count">播放次数（-1表示无限循环）</param>
        public void SetPlayCount(int count)
        {
            if (count < -1)
                count = -1;

            TargetPlayCount = count;

            string message = count == -1
                ? "🔁 设置为无限循环播放"
                : $"🔢 设置播放次数: {count}";

            ShowStatus(message);
        }

        /// <summary>
        /// 设置播放速度
        /// </summary>
        /// <param name="speed">速度倍率（0.1-5.0）</param>
        public void SetSpeed(double speed)
        {
            if (speed <= 0)
                speed = 1.0;

            PlaySpeed = speed;
            ShowStatus($"⚡ 播放速度设置为: {speed}x");
        }

        /// <summary>
        /// 设置循环模式
        /// </summary>
        /// <param name="enabled">是否启用循环</param>
        public void SetLoopMode(bool enabled)
        {
            _autoPlayer.SetLoopMode(enabled);
            ShowStatus($"🔁 循环模式: {(enabled ? "启用" : "禁用")}");
        }

        /// <summary>
        /// 设置倒计时显示
        /// </summary>
        /// <param name="enabled">是否启用</param>
        public void SetCountdownDisplay(bool enabled)
        {
            CountdownEnabled = enabled;
            ShowStatus($"⏱️ 倒计时显示: {(enabled ? "启用" : "禁用")}");
        }

        #endregion

        #region 播放状态查询

        /// <summary>
        /// 获取播放状态信息
        /// </summary>
        /// <returns>播放状态信息</returns>
        public Dictionary<string, object> GetPlayStatus()
        {
            var status = _autoPlayer.GetPlayStatus();
            status["IsRecording"] = IsRecording;
            return status;
        }

        /// <summary>
        /// 获取播放进度文本
        /// </summary>
        /// <returns>播放进度文本</returns>
        public string GetPlayProgressText()
        {
            if (!IsPlaying)
                return "未播放";

            var status = _autoPlayer.GetPlayStatus();
            var current = (int)status["CurrentFrame"];
            var total = (int)status["TotalFrames"];
            var completed = (int)status["CompletedPlayCount"];
            var target = (int)status["TargetPlayCount"];

            string targetText = target == -1 ? "∞" : target.ToString();

            return $"帧: {current}/{total} | 轮次: {completed}/{targetText}";
        }

        #endregion

        #region 原图模式（扩展功能）

        /// <summary>
        /// 开始原图模式录制
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <param name="similarImages">相似图片列表</param>
        /// <returns>是否成功开始</returns>
        public async Task<bool> StartOriginalModeRecordingAsync(
            int baseImageId,
            List<(int ImageId, string Name, string Path)> similarImages)
        {
            if (IsPlaying || IsRecording)
            {
                ShowStatus("请先停止当前操作");
                return false;
            }

            bool success = await _timeRecorder.StartOriginalModeRecordingAsync(
                baseImageId, similarImages);

            if (success)
            {
                ShowStatus($"📍 开始原图模式录制 - 基础图片ID: {baseImageId}");
            }

            return success;
        }

        /// <summary>
        /// 停止原图模式录制
        /// </summary>
        /// <returns>是否成功停止</returns>
        public async Task<bool> StopOriginalModeRecordingAsync()
        {
            bool success = await _timeRecorder.StopOriginalModeRecordingAsync();
            
            if (success)
            {
                ShowStatus("✅ 已停止原图模式录制");
            }

            return success;
        }

        /// <summary>
        /// 记录图片切换时间
        /// </summary>
        /// <param name="fromImageId">源图片ID</param>
        /// <param name="toImageId">目标图片ID</param>
        /// <returns>是否成功记录</returns>
        public bool RecordImageSwitchTime(int fromImageId, int toImageId)
        {
            return _timeRecorder.RecordImageSwitchTiming(fromImageId, toImageId);
        }

        /// <summary>
        /// 检查原图模式是否有数据
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <returns>是否有数据</returns>
        public async Task<bool> HasOriginalModeTimingDataAsync(int baseImageId)
        {
            return await _timeRecorder.HasOriginalModeTimingDataAsync(baseImageId);
        }

        /// <summary>
        /// 清除原图模式时间数据
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <returns>是否成功清除</returns>
        public async Task<bool> ClearOriginalModeTimingDataAsync(int baseImageId)
        {
            if (IsPlaying || IsRecording)
            {
                ShowStatus("请先停止当前操作");
                return false;
            }

            bool success = await _timeRecorder.ClearOriginalModeTimingDataAsync(baseImageId);
            if (success)
            {
                ShowStatus("✅ 已清除原图模式时间数据");
            }

            return success;
        }

        #endregion

        #region 脚本信息

        /// <summary>
        /// 获取格式化的脚本信息
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <returns>脚本信息文本</returns>
        public async Task<string> GetFormattedScriptInfoAsync(int imageId)
        {
            var timings = await _timeRecorder.GetTimingSequenceAsync(imageId);
            if (timings == null || timings.Count == 0)
            {
                return "暂无脚本数据";
            }

            var lines = new List<string>
            {
                $"═══ 关键帧脚本信息 ═══",
                $"图片ID: {imageId}",
                $"关键帧数量: {timings.Count}",
                $"总时长: {timings.Sum(t => t.Duration):F2}秒",
                $"",
                $"序号 | 关键帧ID | 停留时间 | 创建时间",
                $"-----|---------|---------|-------------------"
            };

            int index = 1;
            foreach (var timing in timings.OrderBy(t => t.SequenceOrder))
            {
                lines.Add($"{index,4} | {timing.KeyframeId,7} | {timing.Duration,7:F2}s | {timing.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                index++;
            }

            lines.Add("");
            lines.Add("═".PadRight(40, '═'));

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// 获取原图模式格式化脚本信息
        /// </summary>
        /// <param name="baseImageId">基础图片ID</param>
        /// <returns>脚本信息文本</returns>
        public async Task<string> GetOriginalModeFormattedScriptInfoAsync(int baseImageId)
        {
            var timings = await _timeRecorder.GetOriginalModeTimingSequenceAsync(baseImageId);
            if (timings == null || timings.Count == 0)
            {
                return "暂无原图模式脚本数据";
            }

            var lines = new List<string>
            {
                $"═══ 原图模式脚本信息 ═══",
                $"基础图片ID: {baseImageId}",
                $"切换次数: {timings.Count}",
                $"总时长: {timings.Sum(t => t.Duration):F2}秒",
                $"",
                $"序号 | 从图片 | 到图片 | 停留时间 | 创建时间",
                $"-----|-------|-------|---------|-------------------"
            };

            int index = 1;
            foreach (var timing in timings.OrderBy(t => t.SequenceOrder))
            {
                lines.Add($"{index,4} | {timing.FromImageId,5} | {timing.ToImageId,5} | {timing.Duration,7:F2}s | {timing.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                index++;
            }

            lines.Add("");
            lines.Add("═".PadRight(50, '═'));

            return string.Join(Environment.NewLine, lines);
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 播放完成回调
        /// </summary>
        private void OnAutoPlayFinished(object sender, EventArgs e)
        {
            ShowStatus($"✅ 播放完成 - 共播放 {CompletedPlayCount} 次");
            PlayingStateChanged?.Invoke(this, false);
            PlayFinished?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 显示状态消息
        /// </summary>
        /// <param name="message">消息内容</param>
        private void ShowStatus(string message)
        {
            Console.WriteLine(message);
            _mainWindow.Dispatcher.Invoke(() =>
            {
                // TODO: 更新状态栏显示
                // _mainWindow.StatusText.Text = message;
            });
        }

        #endregion
    }
}

