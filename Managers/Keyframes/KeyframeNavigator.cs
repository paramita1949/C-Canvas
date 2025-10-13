using System;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.UI;

namespace ImageColorChanger.Managers.Keyframes
{
    /// <summary>
    /// 关键帧导航器
    /// 负责关键帧的前进、后退、循环处理
    /// </summary>
    public class KeyframeNavigator
    {
        private readonly KeyframeManager _keyframeManager;
        private readonly MainWindow _mainWindow;
        private readonly KeyframeRepository _repository;

        public KeyframeNavigator(KeyframeManager keyframeManager, MainWindow mainWindow, KeyframeRepository repository)
        {
            _keyframeManager = keyframeManager ?? throw new ArgumentNullException(nameof(keyframeManager));
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// 跳转到上一个关键帧
        /// </summary>
        public async void StepToPrevKeyframe()
        {
            try
            {
                // 获取当前图片ID
                var currentImageId = _mainWindow.GetCurrentImageId();
                if (currentImageId == 0)
                {
                    _mainWindow.ShowStatus("请先选择一张图片");
                    return;
                }

                // 获取关键帧列表
                var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                if (keyframes == null || keyframes.Count == 0)
                {
                    _mainWindow.ShowStatus("当前图片没有关键帧");
                    return;
                }

                // 计算目标索引
                int targetIndex = _keyframeManager.CurrentKeyframeIndex - 1;

                // 特殊处理：如果当前有滚动动画正在进行，立即停止并强制直接跳转
                bool forceDirectJump = false;
                if (_keyframeManager.IsScrolling)
                {
                    _keyframeManager.StopScrollAnimation();
                    System.Diagnostics.Debug.WriteLine("检测到滚动动画正在进行，立即停止并直接跳转");
                    forceDirectJump = true;
                }

                // 如果正在播放，强制使用直接跳转（参考Python版本：keyframe_navigation.py 第148-167行）
                if (_mainWindow._playbackViewModel?.IsPlaying == true)
                {
                    forceDirectJump = true;
                    System.Diagnostics.Debug.WriteLine("🎬 检测到正在播放，强制使用直接跳转");
                }

                // 检测回跳
                bool isBackwardJump = _keyframeManager.IsBackwardJump(targetIndex);
                if (isBackwardJump)
                {
                    forceDirectJump = true;
                    System.Diagnostics.Debug.WriteLine("检测到回跳，强制使用直接跳转");
                }

                // 处理循环
                if (targetIndex < 0)
                {
                    // 循环到最后一帧
                    targetIndex = keyframes.Count - 1;
                }

                // 更新当前帧索引
                _keyframeManager.UpdateKeyframeIndex(targetIndex);

                // 获取目标位置
                var targetKeyframe = keyframes[targetIndex];
                var targetPosition = targetKeyframe.Position;

                // 判断是否使用直接跳转
                bool useDirectJump = forceDirectJump || isBackwardJump || _keyframeManager.ScrollDuration == 0;

                if (useDirectJump)
                {
                    // 直接跳转
                    System.Diagnostics.Debug.WriteLine($"⚡ [上一帧] 直接跳转 #{targetIndex + 1} (滚动中:{forceDirectJump}, 回跳:{isBackwardJump}, 持续0:{_keyframeManager.ScrollDuration == 0})");
                    var scrollViewer = _mainWindow.ImageScrollViewer;
                    var targetOffset = targetPosition * scrollViewer.ScrollableHeight;
                    scrollViewer.ScrollToVerticalOffset(targetOffset);
                    
                    if (_mainWindow.IsProjectionEnabled)
                    {
                        _mainWindow.UpdateProjection();
                    }
                }
                else
                {
                    // 平滑滚动
                    System.Diagnostics.Debug.WriteLine($"🎬 [上一帧] 平滑滚动 #{targetIndex + 1} (持续:{_keyframeManager.ScrollDuration}秒)");
                    _keyframeManager.SmoothScrollTo(targetPosition);
                }

                // 更新UI指示器
                await _keyframeManager.UpdateKeyframeIndicatorsAsync();

                // 显示状态
                _mainWindow.ShowStatus($"关键帧 {targetIndex + 1}/{keyframes.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 跳转上一关键帧异常: {ex.Message}");
                _mainWindow.ShowStatus($"跳转失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 跳转到下一个关键帧
        /// </summary>
        /// <returns>是否应该继续记录时间（如果检测到循环并停止录制，则返回false）</returns>
        public async System.Threading.Tasks.Task<bool> StepToNextKeyframe()
        {
            try
            {
                // 获取当前图片ID
                var currentImageId = _mainWindow.GetCurrentImageId();
                if (currentImageId == 0)
                {
                    _mainWindow.ShowStatus("请先选择一张图片");
                    return false;
                }

                // 获取关键帧列表
                var keyframes = await _keyframeManager.GetKeyframesAsync(currentImageId);
                if (keyframes == null || keyframes.Count == 0)
                {
                    _mainWindow.ShowStatus("当前图片没有关键帧");
                    return false;
                }

                // 获取当前索引
                int currentIndex = _keyframeManager.CurrentKeyframeIndex;

                // 特殊处理：如果当前有滚动动画正在进行，立即停止并强制直接跳转
                bool forceDirectJump = false;
                if (_keyframeManager.IsScrolling)
                {
                    _keyframeManager.StopScrollAnimation();
                    System.Diagnostics.Debug.WriteLine("检测到滚动动画正在进行，立即停止并直接跳转");
                    forceDirectJump = true;
                }

                // 如果正在播放，强制使用直接跳转（参考Python版本：keyframe_navigation.py 第148-167行）
                if (_mainWindow._playbackViewModel?.IsPlaying == true)
                {
                    forceDirectJump = true;
                    System.Diagnostics.Debug.WriteLine("🎬 检测到正在播放，强制使用直接跳转");
                }

                // 计算目标索引
                int targetIndex = currentIndex + 1;

                // 检查是否首次执行（之前未播放过关键帧）
                bool isFirstExecution = currentIndex == -1;

                // 处理循环
                bool shouldReturn = false;
                if (targetIndex >= keyframes.Count)
                {
                    // 循环回第一帧
                    targetIndex = 0;
                    
                    // 检查录制状态（优先使用新的ViewModel系统）
                    bool wasRecording = _mainWindow._playbackViewModel?.IsRecording ?? false;
                    System.Diagnostics.Debug.WriteLine($"🔍 [循环检测] 是否正在录制: {wasRecording}");
                    
                    // 如果正在录制，自动停止录制（参考Python版本 playback_controller.py 第50-64行）
                    if (wasRecording)
                    {
                        System.Diagnostics.Debug.WriteLine("📹 [录制] 检测到循环，准备自动停止录制");
                        
                        // 1. 先记录最后一帧的时间（重要！参考Python版本第50-56行）
                        if (currentIndex >= 0 && currentIndex < keyframes.Count)
                        {
                            var lastKeyframe = keyframes[currentIndex];
                            await _mainWindow._playbackViewModel.RecordKeyframeTimeAsync(lastKeyframe.Id);
                            System.Diagnostics.Debug.WriteLine($"📝 [录制] 循环前记录最后一帧: #{currentIndex + 1}, KeyframeId={lastKeyframe.Id}");
                        }
                        
                        // 2. 然后停止录制
                        var viewModel = _mainWindow._playbackViewModel;
                        var command = viewModel?.ToggleRecordingCommand;
                        bool canExecute = command?.CanExecute(null) ?? false;
                        
                        System.Diagnostics.Debug.WriteLine($"🔍 [命令检查] ViewModel存在: {viewModel != null}, Command存在: {command != null}, CanExecute: {canExecute}");
                        
                        // 使用ViewModel的录制命令停止录制
                        if (canExecute)
                        {
                            await command.ExecuteAsync(null);
                            System.Diagnostics.Debug.WriteLine("✅ [录制] 停止完成");
                            
                            // 等待一小段时间确保录制状态完全清除
                            await System.Threading.Tasks.Task.Delay(50);
                            
                            // 录制结束后，延迟自动启动播放（参考Python版本第64行）
                            _ = _mainWindow.Dispatcher.InvokeAsync(async () =>
                            {
                                await System.Threading.Tasks.Task.Delay(100);
                                await AutoStartPlayAfterRecording(currentImageId);
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("⚠️ [录制] 命令无法执行");
                        }
                        
                        // 无论停止录制是否成功，都标记为不再记录时间
                        shouldReturn = true;
                    }
                }

                // 注意：不要在这里提前返回，继续执行跳转逻辑
                // shouldReturn 只用于控制最后是否记录时间

                // 检测回跳
                bool isBackwardJump = _keyframeManager.IsBackwardJump(targetIndex);
                if (isBackwardJump)
                {
                    forceDirectJump = true;
                    System.Diagnostics.Debug.WriteLine("检测到回跳，强制使用直接跳转");
                }

                // 更新当前帧索引
                _keyframeManager.UpdateKeyframeIndex(targetIndex);

                // 获取目标位置
                var targetKeyframe = keyframes[targetIndex];
                var targetPosition = targetKeyframe.Position;

                // 判断是否使用直接跳转
                bool isLoopingBack = (targetIndex == 0 && currentIndex == keyframes.Count - 1);
                bool useDirectJump = forceDirectJump || isFirstExecution || isLoopingBack || isBackwardJump || _keyframeManager.ScrollDuration == 0;

                if (useDirectJump)
                {
                    // 直接跳转
                    System.Diagnostics.Debug.WriteLine($"⚡ [下一帧] 直接跳转 #{targetIndex + 1} (首次:{isFirstExecution}, 滚动中:{forceDirectJump}, 循环回:{isLoopingBack}, 回跳:{isBackwardJump}, 持续0:{_keyframeManager.ScrollDuration == 0})");
                    var scrollViewer = _mainWindow.ImageScrollViewer;
                    var targetOffset = targetPosition * scrollViewer.ScrollableHeight;
                    scrollViewer.ScrollToVerticalOffset(targetOffset);
                    
                    if (_mainWindow.IsProjectionEnabled)
                    {
                        _mainWindow.UpdateProjection();
                    }
                }
                else
                {
                    // 平滑滚动
                    System.Diagnostics.Debug.WriteLine($"🎬 [下一帧] 平滑滚动 #{targetIndex + 1} (持续:{_keyframeManager.ScrollDuration}秒)");
                    _keyframeManager.SmoothScrollTo(targetPosition);
                }

                // 更新UI指示器
                await _keyframeManager.UpdateKeyframeIndicatorsAsync();

                // 显示状态
                _mainWindow.ShowStatus($"关键帧 {targetIndex + 1}/{keyframes.Count}");
                
                // 根据 shouldReturn 标志决定是否允许继续记录时间
                // 如果检测到循环并停止了录制，则返回 false
                return !shouldReturn;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 跳转下一关键帧异常: {ex.Message}");
                _mainWindow.ShowStatus($"跳转失败: {ex.Message}");
                return false; // 异常情况下，不记录时间
            }
        }

        /// <summary>
        /// 跳转到指定索引的关键帧
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <param name="index">关键帧索引</param>
        /// <param name="useDirectJump">是否使用直接跳转（默认false，根据滚动设置决定）</param>
        public async Task JumpToKeyframeAsync(int imageId, int index, bool useDirectJump = false)
        {
            try
            {
                var keyframes = await _keyframeManager.GetKeyframesAsync(imageId);
                if (keyframes == null || index < 0 || index >= keyframes.Count)
                {
                    return;
                }

                // 更新当前帧索引
                _keyframeManager.UpdateKeyframeIndex(index);

                // 获取目标位置
                var targetKeyframe = keyframes[index];
                var targetPosition = targetKeyframe.Position;

                // 执行跳转（根据参数决定是否使用平滑滚动）
                if (useDirectJump || _keyframeManager.ScrollDuration == 0)
                {
                    // 直接跳转
                    var scrollViewer = _mainWindow.ImageScrollViewer;
                    var targetOffset = targetPosition * scrollViewer.ScrollableHeight;
                    scrollViewer.ScrollToVerticalOffset(targetOffset);
                    
                    if (_mainWindow.IsProjectionEnabled)
                    {
                        _mainWindow.UpdateProjection();
                    }
                }
                else
                {
                    // 平滑滚动（用于自动播放等场景）
                    _keyframeManager.SmoothScrollTo(targetPosition);
                }

                // 更新UI
                await _keyframeManager.UpdateKeyframeIndicatorsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 跳转到关键帧异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 录制完成后自动启动播放
        /// </summary>
        private async System.Threading.Tasks.Task AutoStartPlayAfterRecording(int imageId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🎬 [自动播放] 录制结束，准备启动播放: ImageId={imageId}");
                
                // 检查是否有录制的时间数据
                var keyframes = await _keyframeManager.GetKeyframesAsync(imageId);
                if (keyframes == null || keyframes.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ [自动播放] 没有关键帧，取消自动播放");
                    return;
                }
                
                // 使用ViewModel的播放命令
                if (_mainWindow._playbackViewModel?.TogglePlaybackCommand?.CanExecute(null) == true)
                {
                    System.Diagnostics.Debug.WriteLine("▶️ [自动播放] 启动播放");
                    await _mainWindow._playbackViewModel.TogglePlaybackCommand.ExecuteAsync(null);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ [自动播放] 播放命令不可执行");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [自动播放] 失败: {ex.Message}");
            }
        }
    }
}

