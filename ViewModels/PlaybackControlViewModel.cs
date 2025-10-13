using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Services.Interfaces;
using ImageColorChanger.Services.StateMachine;
using ImageColorChanger.Utils;

namespace ImageColorChanger.ViewModels
{
    /// <summary>
    /// 跳转到关键帧事件参数
    /// </summary>
    public class JumpToKeyframeEventArgs : EventArgs
    {
        public int KeyframeId { get; set; }
    }

    /// <summary>
    /// 播放控制ViewModel
    /// 管理播放、录制、暂停等操作
    /// 参考Python版本：LOGIC_ANALYSIS_05
    /// </summary>
    public partial class PlaybackControlViewModel : ViewModelBase
    {
        private readonly Services.PlaybackServiceFactory _serviceFactory;
        private readonly ICountdownService _countdownService;
        private readonly PlaybackStateMachine _stateMachine;
        private readonly Repositories.Interfaces.ITimingRepository _timingRepository;
        
        /// <summary>
        /// 标志：是否正在加载设置（防止加载时触发保存）
        /// </summary>
        private bool _isLoadingSettings;

        #region 可观察属性

        /// <summary>
        /// 当前图片ID
        /// </summary>
        [ObservableProperty]
        private int _currentImageId;

        /// <summary>
        /// 当前播放模式
        /// </summary>
        [ObservableProperty]
        private PlaybackMode _currentMode = PlaybackMode.Keyframe;

        /// <summary>
        /// 是否正在录制
        /// </summary>
        [ObservableProperty]
        private bool _isRecording;

        /// <summary>
        /// 是否正在播放
        /// </summary>
        [ObservableProperty]
        private bool _isPlaying;

        /// <summary>
        /// 是否已暂停
        /// </summary>
        [ObservableProperty]
        private bool _isPaused;

        /// <summary>
        /// 播放次数（-1表示无限循环）
        /// 参考Python版本：keytime.py 第632行，默认5次
        /// </summary>
        [ObservableProperty]
        private int _playCount = 5;
        
        /// <summary>
        /// 属性改变回调：PlayCount改变时保存到数据库
        /// </summary>
        partial void OnPlayCountChanged(int value)
        {
            // 如果正在加载设置，不触发保存
            if (_isLoadingSettings) return;
            
            SavePlayCountSetting();
        }

        /// <summary>
        /// 已完成播放次数
        /// </summary>
        [ObservableProperty]
        private int _completedPlayCount;

        /// <summary>
        /// 倒计时显示文本
        /// </summary>
        [ObservableProperty]
        private string _countdownText = "--";

        /// <summary>
        /// 是否有时间数据
        /// </summary>
        [ObservableProperty]
        private bool _hasTimingData;

        /// <summary>
        /// 录制按钮文本
        /// </summary>
        [ObservableProperty]
        private string _recordButtonText = "开始录制";

        /// <summary>
        /// 播放按钮文本
        /// </summary>
        [ObservableProperty]
        private string _playButtonText = "开始播放";

        /// <summary>
        /// 暂停按钮文本
        /// </summary>
        [ObservableProperty]
        private string _pauseButtonText = "暂停";

        #endregion

        #region 按钮启用状态

        /// <summary>
        /// 录制按钮是否可用
        /// </summary>
        [ObservableProperty]
        private bool _canRecord = true;

        /// <summary>
        /// 播放按钮是否可用
        /// </summary>
        [ObservableProperty]
        private bool _canPlay;

        /// <summary>
        /// 暂停按钮是否可用
        /// </summary>
        [ObservableProperty]
        private bool _canPause;

        /// <summary>
        /// 清除时间数据按钮是否可用
        /// </summary>
        [ObservableProperty]
        private bool _canClearTiming;

        /// <summary>
        /// 显示脚本按钮是否可用
        /// </summary>
        [ObservableProperty]
        private bool _canShowScript;

        #endregion

        public PlaybackControlViewModel(
            Services.PlaybackServiceFactory serviceFactory,
            ICountdownService countdownService,
            PlaybackStateMachine stateMachine,
            Repositories.Interfaces.ITimingRepository timingRepository)
        {
            _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
            _countdownService = countdownService ?? throw new ArgumentNullException(nameof(countdownService));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _timingRepository = timingRepository ?? throw new ArgumentNullException(nameof(timingRepository));

            // 订阅事件
            _countdownService.CountdownUpdated += OnCountdownUpdated;
            _countdownService.CountdownCompleted += OnCountdownCompleted;
            _stateMachine.StatusChanged += OnStatusChanged;

            // 从数据库加载播放次数设置（参考Python版本：config_manager.py 第568-600行）
            LoadPlayCountSetting();

            // 初始化按钮状态
            UpdateButtonStates();
        }

        #region 私有辅助方法

        /// <summary>
        /// 获取当前模式的录制服务
        /// </summary>
        private IRecordingService GetRecordingService()
        {
            return _serviceFactory.GetRecordingService(CurrentMode);
        }

        /// <summary>
        /// 获取当前模式的播放服务
        /// </summary>
        private IPlaybackService GetPlaybackService()
        {
            return _serviceFactory.GetPlaybackService(CurrentMode);
        }

        #endregion

        #region 命令

        /// <summary>
        /// 开始录制命令（公开给UI调用）
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRecord))]
        private async Task StartRecordingAsync()
        {
            if (!IsRecording)
            {
                await ToggleRecordingAsync();
            }
        }

        /// <summary>
        /// 停止录制命令（公开给UI调用）
        /// </summary>
        [RelayCommand]
        private async Task StopRecordingAsync()
        {
            if (IsRecording)
            {
                await ToggleRecordingAsync();
            }
        }

        /// <summary>
        /// 开始播放命令（公开给UI调用）
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanPlay))]
        private async Task StartPlaybackAsync()
        {
            if (!IsPlaying)
            {
                await TogglePlaybackAsync();
            }
        }

        /// <summary>
        /// 停止播放命令（公开给UI调用）
        /// </summary>
        [RelayCommand]
        private async Task StopPlaybackAsync()
        {
            if (IsPlaying)
            {
                await TogglePlaybackAsync();
            }
        }

        /// <summary>
        /// 恢复播放命令（公开给UI调用）
        /// </summary>
        [RelayCommand]
        private async Task ResumePlaybackAsync()
        {
            if (IsPaused)
            {
                await TogglePauseAsync();
            }
        }

        /// <summary>
        /// 暂停播放命令（公开给UI调用）
        /// </summary>
        [RelayCommand]
        private async Task PausePlaybackAsync()
        {
            if (!IsPaused && IsPlaying)
            {
                await TogglePauseAsync();
            }
        }

        /// <summary>
        /// 切换录制命令
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanRecord))]
        private async Task ToggleRecordingAsync()
        {
            try
            {
                var recordingService = GetRecordingService();
                
                if (IsRecording)
                {
                    // 停止录制
                    await recordingService.StopRecordingAsync();
                    IsRecording = false;
                    RecordButtonText = "开始录制";
                    _stateMachine.TryTransition(PlaybackStatus.Idle);
                    
                    // 更新时间数据标志（录制完成后肯定有数据了）
                    HasTimingData = true;
                    
                    Logger.Info("停止录制");
                }
                else
                {
                    // 开始录制
                    if (_stateMachine.TryTransition(PlaybackStatus.Recording))
                    {
                        await recordingService.StartRecordingAsync(CurrentImageId, CurrentMode);
                        IsRecording = true;
                        RecordButtonText = "停止录制";
                        Logger.Info("开始录制");
                    }
                }

                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "录制操作失败");
            }
        }

        /// <summary>
        /// 切换播放命令
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanPlay))]
        private async Task TogglePlaybackAsync()
        {
            try
            {
                var playbackService = GetPlaybackService();
                
                if (IsPlaying)
                {
                    // 停止播放
                    await playbackService.StopPlaybackAsync();
                    _countdownService.Stop();
                    IsPlaying = false;
                    IsPaused = false;
                    PlayButtonText = "开始播放";
                    CountdownText = "--"; // 重置倒计时显示
                    _stateMachine.TryTransition(PlaybackStatus.Idle);
                    Logger.Info("停止播放");
                }
                else
                {
                    // 开始播放
                    if (_stateMachine.TryTransition(PlaybackStatus.Playing))
                    {
                        // 🎯 订阅播放服务事件（每次播放时重新订阅，确保使用正确的服务）
                        playbackService.ProgressUpdated -= OnPlaybackProgressUpdated;
                        playbackService.PlaybackCompleted -= OnPlaybackCompleted;
                        playbackService.ProgressUpdated += OnPlaybackProgressUpdated;
                        playbackService.PlaybackCompleted += OnPlaybackCompleted;
                        
                        playbackService.PlayCount = PlayCount;
                        await playbackService.StartPlaybackAsync(CurrentImageId);
                        IsPlaying = true;
                        IsPaused = false;
                        PlayButtonText = "停止播放";
                        Logger.Info("开始播放");
                    }
                }

                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "播放操作失败");
            }
        }

        /// <summary>
        /// 切换暂停命令
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanPause))]
        private async Task TogglePauseAsync()
        {
            try
            {
                var playbackService = GetPlaybackService();
                
                if (IsPaused)
                {
                    // 继续播放
                    await playbackService.ResumePlaybackAsync();
                    _countdownService.Resume();
                    IsPaused = false;
                    PauseButtonText = "暂停";
                    _stateMachine.TryTransition(PlaybackStatus.Playing);
                    Logger.Info("继续播放");
                }
                else
                {
                    // 暂停播放
                    await playbackService.PausePlaybackAsync();
                    _countdownService.Pause();
                    IsPaused = true;
                    PauseButtonText = "继续";
                    _stateMachine.TryTransition(PlaybackStatus.Paused);
                    Logger.Info("暂停播放");
                }

                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "暂停操作失败");
            }
        }

        /// <summary>
        /// 设置播放次数命令
        /// </summary>
        [RelayCommand]
        private void SetPlayCount(int count)
        {
            PlayCount = count;
            SavePlayCountSetting();
            Logger.Info("设置播放次数: {Count}", count == -1 ? "无限循环" : count.ToString());
        }

        /// <summary>
        /// 清除时间数据命令
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanClearTiming))]
        private async Task ClearTimingDataAsync()
        {
            try
            {
                var recordingService = GetRecordingService();
                await recordingService.ClearTimingDataAsync(CurrentImageId, CurrentMode);
                HasTimingData = false;
                UpdateButtonStates();
                Logger.Info("清除时间数据");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "清除时间数据失败");
            }
        }

        /// <summary>
        /// 获取格式化的脚本信息
        /// </summary>
        public async Task<string> GetFormattedScriptInfoAsync()
        {
            try
            {
                var timings = await _timingRepository.GetTimingSequenceAsync(CurrentImageId);
                if (timings == null || timings.Count == 0)
                {
                    return "暂无脚本数据";
                }

                var lines = new System.Collections.Generic.List<string>
                {
                    $"═══ 关键帧脚本信息 ═══",
                    $"图片ID: {CurrentImageId}",
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
            catch (Exception ex)
            {
                Logger.Error(ex, "获取脚本信息失败");
                return $"获取脚本信息失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 显示脚本编辑器命令（暂时显示脚本信息对话框）
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanShowScript))]
        private void ShowScript()
        {
            // 注意：这里只是触发命令，实际显示由外部处理（MainWindow）
            Logger.Info("请求显示脚本信息");
        }

        /// <summary>
        /// 录制关键帧时间（供外部调用）
        /// </summary>
        /// <param name="keyframeId">关键帧ID</param>
        public async Task RecordKeyframeTimeAsync(int keyframeId)
        {
            if (!IsRecording)
            {
                return;
            }

            try
            {
                var recordingService = GetRecordingService();
                await recordingService.RecordTimingAsync(keyframeId);
                // System.Diagnostics.Debug.WriteLine($"📝 [ViewModel] 已记录关键帧时间: KeyframeId={keyframeId}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "录制关键帧时间失败: KeyframeId={KeyframeId}", keyframeId);
            }
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 倒计时更新事件
        /// </summary>
        private void OnCountdownUpdated(object sender, CountdownUpdateEventArgs e)
        {
            CountdownText = $"{e.RemainingTime:F1}s";
        }

        /// <summary>
        /// 倒计时完成事件
        /// </summary>
        private void OnCountdownCompleted(object sender, EventArgs e)
        {
            CountdownText = "0.0s";
        }

        /// <summary>
        /// 播放进度更新事件
        /// </summary>
        private void OnPlaybackProgressUpdated(object sender, PlaybackProgressEventArgs e)
        {
            // sender 就是播放服务本身，可以直接获取
            if (sender is IPlaybackService playbackService)
            {
                CompletedPlayCount = playbackService.CompletedPlayCount;
            }
            
            // System.Diagnostics.Debug.WriteLine($"📊 [ViewModel] 播放进度更新: 当前={e.CurrentIndex + 1}/{e.TotalCount}, 倒计时={e.RemainingTime:F1}秒");
            
            // 启动倒计时
            if (e.RemainingTime > 0)
            {
                // System.Diagnostics.Debug.WriteLine($"⏱️ [ViewModel] 启动倒计时服务: {e.RemainingTime:F1}秒");
                _countdownService.Start(e.RemainingTime);
            }
            else
            {
                // System.Diagnostics.Debug.WriteLine($"⚠️ [ViewModel] 倒计时时长无效: {e.RemainingTime}秒");
            }
        }

        /// <summary>
        /// 播放完成事件
        /// </summary>
        private void OnPlaybackCompleted(object sender, EventArgs e)
        {
            IsPlaying = false;
            IsPaused = false;
            PlayButtonText = "开始播放";
            CountdownText = "--"; // 重置倒计时显示
            _stateMachine.TryTransition(PlaybackStatus.Idle);
            UpdateButtonStates();
            Logger.Info("播放完成");
        }

        /// <summary>
        /// 状态变化事件
        /// </summary>
        private void OnStatusChanged(object sender, PlaybackStatusChangedEventArgs e)
        {
            UpdateButtonStates();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 更新按钮状态
        /// 参考Python版本：LOGIC_ANALYSIS_05 行718-763
        /// </summary>
        private void UpdateButtonStates()
        {
            var status = _stateMachine.CurrentStatus;

            // 录制按钮：在Idle状态可以开始录制，在Recording状态可以停止录制
            CanRecord = status == PlaybackStatus.Idle || status == PlaybackStatus.Recording;

            // 播放按钮：在Idle或Playing状态可用（且有时间数据）
            CanPlay = (status == PlaybackStatus.Idle || status == PlaybackStatus.Playing) && HasTimingData;

            // 暂停按钮：在Playing或Paused状态可用
            CanPause = status == PlaybackStatus.Playing || status == PlaybackStatus.Paused;

            // 清除时间数据：只有在Idle状态且有数据时可用
            CanClearTiming = status == PlaybackStatus.Idle && HasTimingData;

            // 显示脚本：只有在Idle状态且有数据时可用
            CanShowScript = status == PlaybackStatus.Idle && HasTimingData;

            // 通知命令状态更新
            ToggleRecordingCommand.NotifyCanExecuteChanged();
            TogglePlaybackCommand.NotifyCanExecuteChanged();
            TogglePauseCommand.NotifyCanExecuteChanged();
            ClearTimingDataCommand.NotifyCanExecuteChanged();
            ShowScriptCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// 设置当前图片并检查时间数据
        /// </summary>
        public async Task SetCurrentImageAsync(int imageId, PlaybackMode mode)
        {
            CurrentImageId = imageId;
            CurrentMode = mode;

            // 根据模式检查是否有时间数据
            try
            {
                if (mode == PlaybackMode.Keyframe)
                {
                    // 关键帧模式：使用TimingRepository
                    var timingRepository = App.GetRequiredService<Repositories.Interfaces.ITimingRepository>();
                    HasTimingData = await timingRepository.HasTimingDataAsync(imageId);
                }
                else if (mode == PlaybackMode.Original)
                {
                    // 原图模式：先查找BaseImageId，再检查是否有数据
                    var originalRepo = App.GetRequiredService<Repositories.Interfaces.IOriginalModeRepository>();
                    var baseImageId = await originalRepo.FindBaseImageIdBySimilarImageAsync(imageId);
                    
                    if (baseImageId.HasValue)
                    {
                        HasTimingData = await originalRepo.HasOriginalTimingDataAsync(baseImageId.Value);
                        Utils.Logger.Debug("原图模式SetCurrentImage: ImageId={ImageId}, BaseImageId={BaseId}, HasData={HasData}",
                            imageId, baseImageId.Value, HasTimingData);
                    }
                    else
                    {
                        // 如果找不到BaseImageId，尝试直接用imageId查询
                        HasTimingData = await originalRepo.HasOriginalTimingDataAsync(imageId);
                        Utils.Logger.Debug("原图模式SetCurrentImage(直接): ImageId={ImageId}, HasData={HasData}",
                            imageId, HasTimingData);
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Error(ex, "检查时间数据失败");
                HasTimingData = false;
            }
            
            UpdateButtonStates();
        }

        /// <summary>
        /// 从数据库加载播放次数设置
        /// 参考Python版本：config_manager.py 第568-600行
        /// </summary>
        private void LoadPlayCountSetting()
        {
            _isLoadingSettings = true;
            try
            {
                // 🔧 创建独立的临时DbContext实例，不从DI容器获取
                // 避免释放DI容器中的DbContext导致其他服务无法使用
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
                using var context = new Database.CanvasDbContext(dbPath);
                var setting = context.Settings.FirstOrDefault(s => s.Key == "play_count");
                
                if (setting != null)
                {
                    if (setting.Value == "-1")
                    {
                        PlayCount = -1;
                    }
                    else if (int.TryParse(setting.Value, out int count) && count > 0)
                    {
                        PlayCount = count;
                    }
                    
                    Utils.Logger.Info("加载播放次数设置: {0}", PlayCount == -1 ? "无限循环" : PlayCount.ToString());
                }
                else
                {
                    Utils.Logger.Info("使用默认播放次数设置: 5次");
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Error(ex, "加载播放次数设置失败，使用默认值5");
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        /// <summary>
        /// 保存播放次数设置到数据库
        /// 参考Python版本：config_manager.py 第602-613行
        /// </summary>
        private void SavePlayCountSetting()
        {
            try
            {
                // 🔧 创建独立的临时DbContext实例，不从DI容器获取
                // 避免释放DI容器中的DbContext导致其他服务无法使用
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
                using var context = new Database.CanvasDbContext(dbPath);
                var setting = context.Settings.FirstOrDefault(s => s.Key == "play_count");
                
                if (setting == null)
                {
                    setting = new Database.Models.Setting
                    {
                        Key = "play_count",
                        Value = PlayCount.ToString()
                    };
                    context.Settings.Add(setting);
                }
                else
                {
                    setting.Value = PlayCount.ToString();
                }
                
                context.SaveChanges();
                Utils.Logger.Debug("保存播放次数设置: {0}", PlayCount);
            }
            catch (Exception ex)
            {
                Utils.Logger.Error(ex, "保存播放次数设置失败");
            }
        }

        /// <summary>
        /// 更新时间数据状态（供外部调用）
        /// </summary>
        public async Task UpdateTimingDataStatus()
        {
            try
            {
                // 根据当前模式检查时间数据
                if (CurrentMode == PlaybackMode.Keyframe)
                {
                    HasTimingData = await _timingRepository.HasTimingDataAsync(CurrentImageId);
                }
                else
                {
                    // 原图模式：从DI容器获取OriginalModeRepository
                    var originalRepo = App.GetRequiredService<Repositories.Interfaces.IOriginalModeRepository>();
                    
                    // 🎯 先通过当前图片ID查找BaseImageId（可能当前图片不是录制时的起始图片）
                    var baseImageId = await originalRepo.FindBaseImageIdBySimilarImageAsync(CurrentImageId);
                    
                    if (baseImageId.HasValue)
                    {
                        HasTimingData = await originalRepo.HasOriginalTimingDataAsync(baseImageId.Value);
                        Utils.Logger.Debug("原图模式HasTimingData检测: CurrentImageId={CurrentId}, BaseImageId={BaseId}, HasData={HasData}",
                            CurrentImageId, baseImageId.Value, HasTimingData);
                    }
                    else
                    {
                        // 如果找不到BaseImageId，尝试直接用CurrentImageId查询
                        HasTimingData = await originalRepo.HasOriginalTimingDataAsync(CurrentImageId);
                        Utils.Logger.Debug("原图模式HasTimingData检测(直接): CurrentImageId={CurrentId}, HasData={HasData}",
                            CurrentImageId, HasTimingData);
                    }
                }
                
                UpdateButtonStates();
            }
            catch (Exception ex)
            {
                Utils.Logger.Error(ex, "更新时间数据状态失败");
                HasTimingData = false;
            }
        }

        #endregion
    }
}

