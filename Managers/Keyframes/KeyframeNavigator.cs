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

                // 检测回跳
                bool isBackwardJump = _keyframeManager.IsBackwardJump(targetIndex);

                // 处理循环
                if (targetIndex < 0)
                {
                    // 循环到最后一帧
                    targetIndex = keyframes.Count - 1;
                    Console.WriteLine($"🔄 已到达第一帧，循环到最后一帧");
                }

                // 更新当前帧索引
                _keyframeManager.UpdateKeyframeIndex(targetIndex);

                // 获取目标位置
                var targetKeyframe = keyframes[targetIndex];
                var targetPosition = targetKeyframe.Position;

                // 判断是否使用直接跳转
                bool useDirectJump = isBackwardJump || _keyframeManager.ScrollDuration == 0;

                // 执行滚动或跳转
                if (useDirectJump)
                {
                    // 直接跳转
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        var scrollViewer = _mainWindow.ImageScrollViewer;
                        var targetOffset = targetPosition * scrollViewer.ScrollableHeight;
                        scrollViewer.ScrollToVerticalOffset(targetOffset);

                        // 同步投影
                        if (_mainWindow.IsProjectionEnabled)
                        {
                            _mainWindow.UpdateProjection();
                        }
                    });

                    Console.WriteLine($"⚡ 直接跳转到关键帧 {targetIndex + 1}/{keyframes.Count}");
                }
                else
                {
                    // 平滑滚动
                    _keyframeManager.SmoothScrollTo(targetPosition);
                    Console.WriteLine($"🎬 平滑滚动到关键帧 {targetIndex + 1}/{keyframes.Count}");
                }

                // 更新UI指示器
                await _keyframeManager.UpdateKeyframeIndicatorsAsync();

                // 显示状态
                _mainWindow.ShowStatus($"关键帧 {targetIndex + 1}/{keyframes.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 跳转上一关键帧异常: {ex.Message}");
                _mainWindow.ShowStatus($"跳转失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 跳转到下一个关键帧
        /// </summary>
        public async void StepToNextKeyframe()
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

                // 获取当前索引
                int currentIndex = _keyframeManager.CurrentKeyframeIndex;

                // 计算目标索引
                int targetIndex = currentIndex + 1;

                // 检查是否首次执行（之前未播放过关键帧）
                bool isFirstExecution = currentIndex == -1;

                // 处理循环
                if (targetIndex >= keyframes.Count)
                {
                    // 循环回第一帧
                    targetIndex = 0;
                    Console.WriteLine($"🔄 已到达最后一帧，循环回第一帧");
                }

                // 检测回跳
                bool isBackwardJump = _keyframeManager.IsBackwardJump(targetIndex);

                // 更新当前帧索引
                _keyframeManager.UpdateKeyframeIndex(targetIndex);

                // 获取目标位置
                var targetKeyframe = keyframes[targetIndex];
                var targetPosition = targetKeyframe.Position;

                // 判断是否使用直接跳转
                // 条件：首次执行、循环回第一帧、回跳、或滚动时间为0
                bool useDirectJump = isFirstExecution || 
                                    (targetIndex == 0 && currentIndex == keyframes.Count - 1) || 
                                    isBackwardJump || 
                                    _keyframeManager.ScrollDuration == 0;

                // 执行滚动或跳转
                if (useDirectJump)
                {
                    // 直接跳转
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        var scrollViewer = _mainWindow.ImageScrollViewer;
                        var targetOffset = targetPosition * scrollViewer.ScrollableHeight;
                        scrollViewer.ScrollToVerticalOffset(targetOffset);

                        // 同步投影
                        if (_mainWindow.IsProjectionEnabled)
                        {
                            _mainWindow.UpdateProjection();
                        }
                    });

                    if (isFirstExecution)
                        Console.WriteLine($"⚡ 首次执行，直接跳转到关键帧 {targetIndex + 1}/{keyframes.Count}");
                    else
                        Console.WriteLine($"⚡ 直接跳转到关键帧 {targetIndex + 1}/{keyframes.Count}");
                }
                else
                {
                    // 平滑滚动
                    _keyframeManager.SmoothScrollTo(targetPosition);
                    Console.WriteLine($"🎬 平滑滚动到关键帧 {targetIndex + 1}/{keyframes.Count}");
                }

                // 更新UI指示器
                await _keyframeManager.UpdateKeyframeIndicatorsAsync();

                // 显示状态
                _mainWindow.ShowStatus($"关键帧 {targetIndex + 1}/{keyframes.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 跳转下一关键帧异常: {ex.Message}");
                _mainWindow.ShowStatus($"跳转失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 跳转到指定索引的关键帧
        /// </summary>
        /// <param name="imageId">图片ID</param>
        /// <param name="index">关键帧索引</param>
        /// <param name="useDirectJump">是否使用直接跳转</param>
        public async Task JumpToKeyframeAsync(int imageId, int index, bool useDirectJump = false)
        {
            try
            {
                var keyframes = await _keyframeManager.GetKeyframesAsync(imageId);
                if (keyframes == null || index < 0 || index >= keyframes.Count)
                {
                    Console.WriteLine($"❌ 无效的关键帧索引: {index}");
                    return;
                }

                // 更新当前帧索引
                _keyframeManager.UpdateKeyframeIndex(index);

                // 获取目标位置
                var targetKeyframe = keyframes[index];
                var targetPosition = targetKeyframe.Position;

                // 执行跳转
                if (useDirectJump || _keyframeManager.ScrollDuration == 0)
                {
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        var scrollViewer = _mainWindow.ImageScrollViewer;
                        var targetOffset = targetPosition * scrollViewer.ScrollableHeight;
                        scrollViewer.ScrollToVerticalOffset(targetOffset);

                        if (_mainWindow.IsProjectionEnabled)
                        {
                            _mainWindow.UpdateProjection();
                        }
                    });
                }
                else
                {
                    _keyframeManager.SmoothScrollTo(targetPosition);
                }

                // 更新UI
                await _keyframeManager.UpdateKeyframeIndicatorsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 跳转到关键帧异常: {ex.Message}");
            }
        }
    }
}

