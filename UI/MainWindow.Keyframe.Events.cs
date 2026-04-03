using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers.Keyframes;
using MessageBox = System.Windows.MessageBox;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Keyframe Events (Button Events, Playback Events)
    /// </summary>
    public partial class MainWindow
    {
        private ContextMenu _compositeSpeedMenu;
        private System.Windows.Threading.DispatcherTimer _compositeSpeedMenuAutoCloseTimer;
        private DateTime _compositeSpeedMenuLastKeepAliveUtc = DateTime.MinValue;
        private const double CompositeSpeedMenuCloseGracePeriodMs = 320;

        #region 关键帧按钮事件

        /// <summary>
        /// 添加关键帧按钮点击事件
        /// </summary>
        private async void BtnAddKeyframe_Click(object sender, RoutedEventArgs e)
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"[关键帧][AddClick] enter, imageId={_currentImageId}, verticalOffset={ImageScrollViewer?.VerticalOffset:F2}, scrollableHeight={ImageScrollViewer?.ScrollableHeight:F2}");
            #endif

            if (_currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_keyframeManager == null)
            {
                ShowStatus("关键帧系统未初始化");
                return;
            }

            try
            {
                // 计算滚动位置
                double position = ImageScrollViewer.ScrollableHeight > 0
                    ? ImageScrollViewer.VerticalOffset / ImageScrollViewer.ScrollableHeight
                    : 0;
                int yPosition = (int)ImageScrollViewer.VerticalOffset;

                // 添加关键帧
                bool success = await _keyframeManager.AddKeyframeAsync(
                    _currentImageId, position, yPosition);

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[关键帧][AddClick] result={success}, imageId={_currentImageId}, position={position:F4}, yPosition={yPosition}");
                #endif

                if (success)
                {
                    ShowStatus($"已添加关键帧");
                    UpdatePreviewLines();
                    
                    // 如果正在录制，获取最新的关键帧ID并记录时间（使用新架构）
                    if (_playbackViewModel?.IsRecording == true)
                    {
                        var keyframes = _keyframeManager.GetKeyframes(_currentImageId); // 同步获取（会更新缓存）
                        if (keyframes != null && keyframes.Count > 0)
                        {
                            var lastKeyframe = keyframes.OrderByDescending(k => k.Id).FirstOrDefault();
                            if (lastKeyframe != null)
                            {
                                _ = _playbackViewModel.RecordKeyframeTimeAsync(lastKeyframe.Id); // 异步不等待
                            }
                        }
                    }
                }
                else
                {
                    ShowStatus("添加关键帧失败（该位置附近已存在关键帧）");
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[关键帧][AddClick][ERROR] {ex}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[关键帧][AddClick][INNER] {ex.InnerException}");
                }
                #endif
                ShowStatus($"添加关键帧出错: {ex.Message}");
                // System.Diagnostics.Debug.WriteLine($"添加关键帧异常: {ex}");
            }
        }

        /// <summary>
        /// 清除关键帧按钮点击事件
        /// </summary>
        private async void BtnClearKeyframes_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_keyframeManager == null)
            {
                ShowStatus("关键帧系统未初始化");
                return;
            }

            var result = MessageBox.Show(
                "确定要清除当前图片的所有关键帧吗？\n此操作不可撤销。",
                "确认清除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _keyframeManager.ClearKeyframesAsync(_currentImageId);
                    ShowStatus("已清除所有关键帧");
                    UpdatePreviewLines();
                }
                catch (Exception ex)
                {
                    ShowStatus($"清除关键帧出错: {ex.Message}");
                }
            }
        }

        // 防重入标志：防止快速点击导致并发执行
        private volatile bool _isNavigatingKeyframe = false;

        /// <summary>
        /// 上一个关键帧/上一张图/上一个媒体按钮点击事件（四模式支持）
        /// </summary>
        private async void BtnPrevKeyframe_Click(object sender, RoutedEventArgs e)
        {
            // 防重入：如果上一次操作还没完成，直接返回
            if (_isNavigatingKeyframe)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" [防重入] 上一帧操作正在执行中，忽略本次点击");
                #endif
                return;
            }

            _isNavigatingKeyframe = true;
            try
            {
                // ⏱ 性能调试：测量关键帧切换总耗时
                var sw = System.Diagnostics.Stopwatch.StartNew();
                //System.Diagnostics.Debug.WriteLine($"");
                //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 开始上一帧操作 ==========");
                
                // 模式-1：圣经模式（向上滚动经文）
                if (_isBibleMode && BibleVerseScrollViewer.Visibility == Visibility.Visible)
                {
                    BtnBiblePrevVerse_Click(sender, e);
                    return;
                }
                
                // 模式0：文本编辑器模式（切换幻灯片）
                if (TextEditorPanel.Visibility == Visibility.Visible)
                {
                    //System.Diagnostics.Debug.WriteLine("文本编辑器模式，切换到上一张幻灯片");
                    NavigateToPreviousSlide();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 幻灯片切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }
                
                if (_currentImageId == 0)
                {
                    ShowStatus("请先选择一张图片");
                    return;
                }

                // 模式1：媒体播放模式（视频/音频）
                if (IsMediaPlaybackMode())
                {
                    await SwitchToPreviousMediaFile();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 媒体切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }

                // 模式2：原图标记模式（切换相似图片）
                if (IsOriginalMarkMode())
                {
                    SwitchToPreviousSimilarImage();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 相似图片切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }

                // 模式3：关键帧模式（默认）
                if (_keyframeManager == null)
                {
                    ShowStatus("关键帧系统未初始化");
                    return;
                }
                
                //System.Diagnostics.Debug.WriteLine(" 关键帧模式：上一帧");

                // 修复：在跳转前保存当前索引，避免StepToPrevKeyframe()更新索引后导致记录错误
                int currentIndexBeforeJump = _keyframeManager.CurrentKeyframeIndex;

                // 如果正在录制，先记录当前帧的时间（跳转前）
                if (_playbackViewModel?.IsRecording == true && currentIndexBeforeJump >= 0)
                {
                    var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                    if (keyframes != null && currentIndexBeforeJump < keyframes.Count)
                    {
                        var currentKeyframe = keyframes[currentIndexBeforeJump];
                        _ = _playbackViewModel.RecordKeyframeTimeAsync(currentKeyframe.Id); // 异步执行不等待
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"[上一帧-录制] 离开关键帧 #{currentIndexBeforeJump + 1} (ID={currentKeyframe.Id})，记录停留时间");
                        #endif
                    }
                }
                
                // 如果正在播放，记录手动操作用于实时修正（参考Python版本：keytime.py 第750-786行）
                if (_playbackViewModel?.IsPlaying == true && currentIndexBeforeJump >= 0)
                {
                    var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                    if (keyframes != null && currentIndexBeforeJump < keyframes.Count)
                    {
                        var currentKeyframe = keyframes[currentIndexBeforeJump];
                        
                        // 调用播放服务的手动修正方法
                        var playbackService = _playbackServiceFactory?.GetPlaybackService(Database.Models.Enums.PlaybackMode.Keyframe);
                        if (playbackService is Services.Implementations.KeyframePlaybackService kfService)
                        {
                            _ = kfService.RecordManualOperationAsync(currentKeyframe.Id); // 异步执行不等待
                            //System.Diagnostics.Debug.WriteLine($"[手动跳转] 播放中点击上一帧，记录修正时间: 关键帧#{currentIndexBeforeJump + 1}");
                            
                            // 跳过当前等待，立即播放下一帧（参考Python版本：keyframe_navigation.py 第157-167行）
                            // 注意：上一帧总是回跳，会被Navigator强制直接跳转，所以这里跳过等待是安全的
                            kfService.SkipCurrentWaitAndPlayNext();
                            //System.Diagnostics.Debug.WriteLine($" [手动跳转] 点击上一帧，跳过当前等待");
                        }
                    }
                }
                
                // 然后执行跳转
                var navStart = sw.ElapsedMilliseconds;
                _keyframeManager.Navigator.StepToPrevKeyframe();
                var navTime = sw.ElapsedMilliseconds - navStart;
                //System.Diagnostics.Debug.WriteLine($"⏱ [手动跳转] Navigator.StepToPrevKeyframe: {navTime}ms");
                
                sw.Stop();
                //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 关键帧切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                //System.Diagnostics.Debug.WriteLine($"");
            }
            finally
            {
                _isNavigatingKeyframe = false;
            }
        }

        /// <summary>
        /// 下一个关键帧/下一张图/下一个媒体按钮点击事件（四模式支持）
        /// </summary>
        private async void BtnNextKeyframe_Click(object sender, RoutedEventArgs e)
        {
            // 防重入：如果上一次操作还没完成，直接返回
            if (_isNavigatingKeyframe)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" [防重入] 下一帧操作正在执行中，忽略本次点击");
                #endif
                return;
            }

            _isNavigatingKeyframe = true;
            try
            {
                // ⏱ 性能调试：测量关键帧切换总耗时
                var sw = System.Diagnostics.Stopwatch.StartNew();
                //System.Diagnostics.Debug.WriteLine($"");
                //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 开始下一帧操作 ==========");
                
                // 模式-1：圣经模式（向下滚动经文）
                if (_isBibleMode && BibleVerseScrollViewer.Visibility == Visibility.Visible)
                {
                    BtnBibleNextVerse_Click(sender, e);
                    return;
                }
                
                // 模式0：文本编辑器模式（切换幻灯片）
                if (TextEditorPanel.Visibility == Visibility.Visible)
                {
                    //System.Diagnostics.Debug.WriteLine("文本编辑器模式，切换到下一张幻灯片");
                    NavigateToNextSlide();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 幻灯片切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }
                
                if (_currentImageId == 0)
                {
                    ShowStatus("请先选择一张图片");
                    return;
                }

                // 模式1：媒体播放模式（视频/音频）
                if (IsMediaPlaybackMode())
                {
                    await SwitchToNextMediaFile();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 媒体切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }

                // 模式2：原图标记模式（切换相似图片）
                if (IsOriginalMarkMode())
                {
                    SwitchToNextSimilarImage();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 相似图片切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }

                // 模式3：关键帧模式（默认）
                if (_keyframeManager == null)
                {
                    ShowStatus("关键帧系统未初始化");
                    return;
                }
                
                //System.Diagnostics.Debug.WriteLine(" 关键帧模式：下一帧");

                // 修复：在跳转前保存当前索引，避免StepToNextKeyframe()更新索引后导致记录错误
                int currentIndexBeforeJump = _keyframeManager.CurrentKeyframeIndex;
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[下一帧-调试] 保存的索引: currentIndexBeforeJump={currentIndexBeforeJump}, CurrentKeyframeIndex={_keyframeManager.CurrentKeyframeIndex}");
                #endif
                
                // 如果正在录制，先记录当前帧的时间（跳转前）
                if (_playbackViewModel?.IsRecording == true && currentIndexBeforeJump >= 0)
            {
                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                if (keyframes != null && currentIndexBeforeJump < keyframes.Count)
                {
                    var currentKeyframe = keyframes[currentIndexBeforeJump];
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($" [下一帧-录制] 准备记录关键帧 #{currentIndexBeforeJump + 1} (ID={currentKeyframe.Id}) 的停留时间");
                    #endif
                    _ = _playbackViewModel.RecordKeyframeTimeAsync(currentKeyframe.Id); // 异步执行不等待
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[下一帧-录制] 已调用RecordKeyframeTimeAsync，离开关键帧 #{currentIndexBeforeJump + 1}，记录停留时间");
                    #endif
                }
                else
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($" [下一帧-录制] 无法记录：currentIndexBeforeJump={currentIndexBeforeJump}, keyframes.Count={keyframes?.Count ?? 0}");
                    #endif
                }
            }
            
            // 合成播放中：更新TOTAL时间并重新开始循环
            var compositeService = _playbackServiceFactory?.GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite) 
                as Services.Implementations.CompositePlaybackService;
            
            if (compositeService != null && compositeService.IsPlaying)
            {
                // 合成播放中：更新TOTAL时间为当前已播放时间，并重新开始循环
                await compositeService.UpdateTotalAndRestartAsync();
                ShowStatus($"已更新TOTAL时间为 {compositeService.GetElapsedSeconds():F1}秒，重新开始循环");
                sw.Stop();
                return;
            }
            
            // 如果正在播放，记录手动操作用于实时修正（参考Python版本：keytime.py 第750-786行）
            if (_playbackViewModel?.IsPlaying == true && currentIndexBeforeJump >= 0)
            {
                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                if (keyframes != null && currentIndexBeforeJump < keyframes.Count)
                {
                    var currentKeyframe = keyframes[currentIndexBeforeJump];
                    
                    // 调用播放服务的手动修正方法
                    var playbackService = _playbackServiceFactory?.GetPlaybackService(Database.Models.Enums.PlaybackMode.Keyframe);
                    if (playbackService is Services.Implementations.KeyframePlaybackService kfService)
                    {
                        _ = kfService.RecordManualOperationAsync(currentKeyframe.Id); // 异步执行不等待
                        //System.Diagnostics.Debug.WriteLine($"[手动跳转] 播放中点击下一帧，记录修正时间: 关键帧#{currentIndexBeforeJump + 1}");
                        
                        // 跳过当前等待，立即结束当前帧（参考Python版本：keyframe_navigation.py 第157-167行）
                        // 播放循环会基于实际关键帧数量判断循环，不会进入错误数据
                        kfService.SkipCurrentWaitAndPlayNext();
                        //System.Diagnostics.Debug.WriteLine($" [手动跳转] 跳过当前等待，让播放循环立即进入下一帧判断");
                    }
                }
            }
            
                // 然后执行跳转
                var navStart = sw.ElapsedMilliseconds;
                await _keyframeManager.Navigator.StepToNextKeyframe();
                var navTime = sw.ElapsedMilliseconds - navStart;
                //System.Diagnostics.Debug.WriteLine($"⏱ [手动跳转] Navigator.StepToNextKeyframe: {navTime}ms");
                
                sw.Stop();
                //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ========== 关键帧切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                //System.Diagnostics.Debug.WriteLine($"");
            }
            finally
            {
                _isNavigatingKeyframe = false;
            }
        }

        #endregion

        #region 播放按钮事件

        /// <summary>
        /// 合成播放按钮点击事件
        /// </summary>
        private async void BtnCompositePlay_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_keyframeManager == null)
            {
                ShowStatus("关键帧系统未初始化");
                return;
            }

            try
            {
                // 获取合成播放服务
                var compositeService = _playbackServiceFactory?.GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite) 
                    as Services.Implementations.CompositePlaybackService;

                if (compositeService == null)
                {
                    ShowStatus("合成播放服务未初始化");
                    return;
                }

                // 如果正在播放，停止
                if (compositeService.IsPlaying)
                {
                    await StopCompositePlaybackLikeButtonAsync(compositeService, "已停止合成播放");
                    return;
                }

                // 获取关键帧（可以为空或少于2个，支持无关键帧播放）
                // 注意：即使没有录制数据（时间数据）也允许播放
                // - 有关键帧（>=2）：使用TOTAL时间从第一帧滚动到最后一帧
                // - 无关键帧：使用TOTAL时间从顶部滚动到底部
                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);

                // 订阅滚动请求事件
                compositeService.ScrollRequested -= OnCompositeScrollRequested;
                compositeService.ScrollRequested += OnCompositeScrollRequested;

                // 订阅停止滚动事件
                compositeService.ScrollStopRequested -= OnCompositeScrollStopRequested;
                compositeService.ScrollStopRequested += OnCompositeScrollStopRequested;

                // 订阅播放完成事件
                compositeService.PlaybackCompleted -= OnCompositePlaybackCompleted;
                compositeService.PlaybackCompleted += OnCompositePlaybackCompleted;

                // 订阅进度更新事件（用于倒计时）
                compositeService.ProgressUpdated -= OnCompositeProgressUpdated;
                compositeService.ProgressUpdated += OnCompositeProgressUpdated;

                // 订阅当前关键帧变化事件（用于更新指示块颜色）
                compositeService.CurrentKeyframeChanged -= OnCompositeCurrentKeyframeChanged;
                compositeService.CurrentKeyframeChanged += OnCompositeCurrentKeyframeChanged;

                // 订阅获取可滚动高度事件
                compositeService.ScrollableHeightRequested -= OnScrollableHeightRequested;
                compositeService.ScrollableHeightRequested += OnScrollableHeightRequested;
                
                // 订阅获取当前滚动位置事件
                compositeService.CurrentScrollPositionRequested -= OnCurrentScrollPositionRequested;
                compositeService.CurrentScrollPositionRequested += OnCurrentScrollPositionRequested;

                // 订阅速度变化事件
                compositeService.SpeedChanged -= OnCompositeSpeedChanged;
                compositeService.SpeedChanged += OnCompositeSpeedChanged;

                // 设置播放次数（使用当前的播放次数设置）
                compositeService.PlayCount = _playbackViewModel?.PlayCount ?? -1;
                _lastCompositeAutoStopKeyframeId = -1;
                
                // 每次开始新一轮播放时，重置速度为1.0（正常速度）
                compositeService.SetSpeed(1.0);

                // 在开始播放前，根据情况跳转到起始位置
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [合成播放] ========== 开始播放前检查 ==========");
                //System.Diagnostics.Debug.WriteLine($"   当前关键帧索引: {_keyframeManager.CurrentKeyframeIndex}");
                //System.Diagnostics.Debug.WriteLine($"   当前滚动位置: {ImageScrollViewer.VerticalOffset:F1}");
                //#endif
                
                if (keyframes != null && keyframes.Count >= 1)
                {
                    var orderedKeyframes = keyframes.OrderBy(k => k.YPosition).ThenBy(k => k.Id).ToList();
                    var firstKeyframe = orderedKeyframes.First();
                    bool isSingleAutoPauseKeyframe = orderedKeyframes.Count == 1 && firstKeyframe.AutoPause;

                    // 单P关键帧仅作为停止点，不做“起播前跳到第一帧”。
                    // 其余情况（含两个及以上关键帧）保持旧逻辑：先跳第一帧再播放。
                    if (isSingleAutoPauseKeyframe)
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine(" [合成播放] 单P关键帧模式：跳过起播前首帧跳转");
                        #endif
                    }
                    else
                    {
                        // 有关键帧：检查是否已经在第一帧位置，避免不必要的刷新
                    var currentOffset = ImageScrollViewer.VerticalOffset;
                    var targetOffset = firstKeyframe.YPosition;

                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   有关键帧数量: {keyframes.Count}");
                    //System.Diagnostics.Debug.WriteLine($"   第一个关键帧位置: {targetOffset:F1}");
                    //System.Diagnostics.Debug.WriteLine($"   位置差值: {Math.Abs(currentOffset - targetOffset):F1}");
                    //#endif

                    // 如果当前已经在第一个关键帧位置，且位置也一致，就不需要跳转
                    bool isAtFirstKeyframe = _keyframeManager.CurrentKeyframeIndex == 0;
                    bool isAtTargetPosition = Math.Abs(currentOffset - targetOffset) <= 1;
                    
                    if (isAtFirstKeyframe && isAtTargetPosition)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    [合成播放] 已在第一个关键帧位置，跳过跳转，直接播放");
                        //#endif
                        // 不执行跳转，直接开始播放
                    }
                    else if (Math.Abs(currentOffset - targetOffset) > 1)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    [合成播放] 执行跳转到第一帧: {currentOffset:F1} -> {targetOffset:F1}");
                        //#endif
                        ImageScrollViewer.ScrollToVerticalOffset(targetOffset);
                    }
                    else
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    [合成播放] 已在第一帧位置，不跳转");
                        //#endif
                    }
                    }
                }
                else
                {
                    // 无关键帧：检查是否已经在顶部，避免不必要的刷新
                    var currentOffset = ImageScrollViewer.VerticalOffset;

                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   无关键帧");
                    //System.Diagnostics.Debug.WriteLine($"   当前滚动位置: {currentOffset:F1}");
                    //#endif

                    // 只有当前不在顶部时才跳转（容差1像素）
                    if (currentOffset > 1)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    [合成播放] 执行跳转到顶部: {currentOffset:F1} -> 0");
                        //#endif
                        ImageScrollViewer.ScrollToVerticalOffset(0);
                    }
                    else
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    [合成播放] 已在顶部，不跳转");
                        //#endif
                    }
                }

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"   跳转后滚动位置: {ImageScrollViewer.VerticalOffset:F1}");
                //System.Diagnostics.Debug.WriteLine($" [合成播放] ========== 开始调用 StartPlaybackAsync ==========");
                //#endif

                // 开始播放
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[合成播放][StartRequest] source=BtnCompositePlay_Click, imageId={_currentImageId}, isPlayingBefore={compositeService.IsPlaying}, isPausedBefore={compositeService.IsPaused}");
                #endif
                await compositeService.StartPlaybackAsync(_currentImageId);
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [合成播放] ========== StartPlaybackAsync 调用完成 ==========");
                //#endif
                SetCompositePlayButtonContent(true);
                BtnCompositePause.Visibility = Visibility.Visible;
                SetCompositePauseButtonContent(compositeService.IsPaused);
                
                // 显示速度控制按钮
                BtnCompositeSpeed.Visibility = Visibility.Visible;
                UpdateSpeedButtonText(compositeService.Speed);

                ShowStatus("开始合成播放");
            }
            catch (Exception ex)
            {
                ShowStatus($"合成播放失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($" 合成播放异常: {ex}");
            }
        }

        /// <summary>
        /// 合成播放按钮右键点击事件 - 快捷设置总时长
        /// </summary>
        private void BtnCompositePlay_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (_currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            try
            {
                // 创建右键菜单
                var contextMenu = new ContextMenu();
                
                // 应用自定义样式
                contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");

                // 标题项
                var titleItem = new MenuItem 
                { 
                    Header = "快捷设置总时长",
                    IsEnabled = false,
                    FontWeight = FontWeights.Bold
                };
                contextMenu.Items.Add(titleItem);
                contextMenu.Items.Add(new Separator());

                // 预设时长选项：60、70、80、90、100、110、120秒
                var durations = new[] { 60, 70, 80, 90, 100, 110, 120 };
                
                foreach (var duration in durations)
                {
                    var menuItem = new MenuItem 
                    { 
                        Header = $"{duration} 秒",
                        Tag = duration
                    };
                    
                    menuItem.Click += async (s, args) =>
                    {
                        await SetCompositeTotalDuration(duration);
                    };
                    
                    contextMenu.Items.Add(menuItem);
                }

                contextMenu.Items.Add(new Separator());

                // 自定义时长选项
                var customItem = new MenuItem { Header = "自定义时长..." };
                customItem.Click += (s, args) => OpenScriptEditWindow();
                contextMenu.Items.Add(customItem);

                contextMenu.Items.Add(new Separator());

                // 全局默认时间设定
                var currentDefaultDuration = _configManager?.CompositePlaybackDefaultDuration ?? 105.0;
                var globalDefaultItem = new MenuItem 
                { 
                    Header = $"全局默认时间: {currentDefaultDuration:F0} 秒"
                };
                globalDefaultItem.Click += (s, args) => OpenGlobalDefaultDurationDialog();
                contextMenu.Items.Add(globalDefaultItem);

                // 显示菜单
                contextMenu.PlacementTarget = sender as UIElement;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                contextMenu.IsOpen = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" 显示右键菜单失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置合成播放的总时长
        /// </summary>
        private async System.Threading.Tasks.Task SetCompositeTotalDuration(int duration)
        {
            try
            {
                // 获取CompositeScriptRepository
                var compositeScriptRepo = _compositeScriptRepository;
                if (compositeScriptRepo == null) return;
                
                // 更新TOTAL时长
                await compositeScriptRepo.CreateOrUpdateAsync(_currentImageId, duration, autoCalculate: false);
                
                ShowStatus($"总时长已设置为 {duration} 秒");
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" 总时长已设置: {duration}秒 (图片ID: {_currentImageId})");
                #endif
            }
            catch (Exception ex)
            {
                ShowStatus($"设置时长失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($" 设置总时长失败: {ex}");
            }
        }

        /// <summary>
        /// 打开全局默认时间设置对话框
        /// </summary>
        private void OpenGlobalDefaultDurationDialog()
        {
            try
            {
                if (_configManager == null)
                {
                    ShowStatus("配置管理器未初始化");
                    return;
                }

                var currentDuration = _configManager.CompositePlaybackDefaultDuration;

                // 创建输入对话框
                var inputDialog = new Window
                {
                    Title = "设置全局默认时间",
                    Width = 350,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false
                };

                var stackPanel = new System.Windows.Controls.StackPanel
                {
                    Margin = new Thickness(20),
                    Orientation = System.Windows.Controls.Orientation.Vertical
                };

                // 提示文本
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = "请输入全局默认时间（秒）：",
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                stackPanel.Children.Add(label);

                // 输入框
                var textBox = new System.Windows.Controls.TextBox
                {
                    Text = currentDuration.ToString("F0"),
                    FontSize = 14,
                    Height = 30,
                    Margin = new Thickness(0, 0, 0, 15),
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center
                };
                stackPanel.Children.Add(textBox);

                // 按钮面板
                var buttonPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };

                var okButton = new System.Windows.Controls.Button
                {
                    Content = "确定",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };

                var cancelButton = new System.Windows.Controls.Button
                {
                    Content = "取消",
                    Width = 80,
                    Height = 30,
                    IsCancel = true
                };

                okButton.Click += (s, args) =>
                {
                    if (double.TryParse(textBox.Text, out double duration))
                    {
                        if (duration > 0 && duration <= 600) // 限制在1-600秒之间
                        {
                            _configManager.CompositePlaybackDefaultDuration = duration;
                            ShowStatus($"全局默认时间已设置为 {duration:F0} 秒");
                            inputDialog.DialogResult = true;
                            inputDialog.Close();
                        }
                        else
                        {
                            MessageBox.Show("时间必须在 1-600 秒之间", "输入错误", 
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        MessageBox.Show("请输入有效的数字", "输入错误", 
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };

                cancelButton.Click += (s, args) =>
                {
                    inputDialog.DialogResult = false;
                    inputDialog.Close();
                };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);
                stackPanel.Children.Add(buttonPanel);

                inputDialog.Content = stackPanel;
                inputDialog.Loaded += (s, e) => 
                {
                    textBox.SelectAll();
                    textBox.Focus();
                };

                inputDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowStatus($"打开设置对话框失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($" 打开全局默认时间设置对话框失败: {ex}");
            }
        }

        /// <summary>
        /// 打开脚本编辑窗口
        /// </summary>
        private async void OpenScriptEditWindow()
        {
            try
            {
                // 从数据库获取关键帧时间数据
                var timings = new System.Collections.Generic.List<Database.Models.DTOs.TimingSequenceDto>();
                
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
                using (var context = new Database.CanvasDbContext(dbPath))
                {
                    var keyframeTimings = await context.KeyframeTimings
                        .Where(t => t.ImageId == _currentImageId)
                        .OrderBy(t => t.SequenceOrder)
                        .ToListAsync();
                    
                    timings = keyframeTimings.Select(t => new Database.Models.DTOs.TimingSequenceDto
                    {
                        KeyframeId = t.KeyframeId,
                        Duration = t.Duration,
                        SequenceOrder = t.SequenceOrder
                    }).ToList();
                }
                
                // 打开脚本编辑窗口
                var scriptWindow = _mainWindowServices
                    .GetRequired<Composition.ScriptEditWindowFactory>()
                    .CreateForKeyframe(_currentImageId, timings);
                scriptWindow.Owner = this;
                scriptWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowStatus($"打开脚本窗口失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($" 打开脚本窗口失败: {ex}");
            }
        }

        /// <summary>
        /// 合成播放滚动请求事件处理
        /// </summary>
        private void OnCompositeScrollRequested(object sender, Services.Implementations.CompositeScrollEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var scrollViewer = ImageScrollViewer;
                    if (scrollViewer == null) return;

                    //#if DEBUG
                    //var currentOffsetBefore = scrollViewer.VerticalOffset;
                    //System.Diagnostics.Debug.WriteLine($"[合成播放滚动] ========== 收到滚动请求 ==========");
                    //System.Diagnostics.Debug.WriteLine($"   当前滚动位置: {currentOffsetBefore:F1}");
                    //System.Diagnostics.Debug.WriteLine($"   起始位置: {e.StartPosition:F1}");
                    //System.Diagnostics.Debug.WriteLine($"   结束位置: {e.EndPosition:F1}");
                    //System.Diagnostics.Debug.WriteLine($"   时长: {e.Duration:F1}秒");
                    //System.Diagnostics.Debug.WriteLine($"   位置差值(当前-起始): {Math.Abs(currentOffsetBefore - e.StartPosition):F1}");
                    //System.Diagnostics.Debug.WriteLine($"   位置差值(起始-结束): {Math.Abs(e.StartPosition - e.EndPosition):F1}");
                    //System.Diagnostics.Debug.WriteLine($"   当前关键帧索引: {_keyframeManager?.CurrentKeyframeIndex ?? -1}");
                    //#endif
                    var currentOffsetBefore = scrollViewer.VerticalOffset;

                    // 停止之前的合成滚动动画（如果有）
                    StopCompositeScrollAnimation();

                    // 如果时长为0，表示直接跳转，不滚动
                    if (e.Duration <= 0)
                    {
                        // 如果目标位置和当前位置一致（容差1像素），就不需要跳转
                        if (Math.Abs(currentOffsetBefore - e.EndPosition) <= 1)
                        {
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"    [合成播放滚动] 已在目标位置，跳过跳转");
                            //#endif
                            
                            // 更新投影
                            if (IsProjectionEnabled)
                            {
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"    [合成播放滚动] 更新投影");
                                //#endif
                                UpdateProjection();
                            }
                            return;
                        }
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    [合成播放滚动] 执行直接跳转: {currentOffsetBefore:F1} -> {e.EndPosition:F1}");
                        //#endif
                        scrollViewer.ScrollToVerticalOffset(e.EndPosition);
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   跳转后位置: {scrollViewer.VerticalOffset:F1}");
                        //#endif
                        
                        // 更新投影
                        if (IsProjectionEnabled)
                        {
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"    [合成播放滚动] 更新投影");
                            //#endif
                            UpdateProjection();
                        }
                        return;
                    }

                    // 检查当前位置和起始位置
                    bool isAtStartPosition = Math.Abs(currentOffsetBefore - e.StartPosition) <= 1;
                    bool isStartEndSame = Math.Abs(e.StartPosition - e.EndPosition) <= 1;
                    
                    // 如果已在起始位置且起始和结束位置相同，不需要滚动
                    if (isAtStartPosition && isStartEndSame)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"    [合成播放滚动] 已在起始位置且无需滚动，跳过滚动动画");
                        //#endif
                        
                        // 更新投影
                        if (IsProjectionEnabled)
                        {
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"    [合成播放滚动] 更新投影");
                            //#endif
                            UpdateProjection();
                        }
                        return;
                    }
                    
                    // 如果当前位置和起始位置不同，但当前位置已经在第一个关键帧位置，调整起始位置为当前位置
                    // 这样可以避免从当前位置跳转到起始位置再开始滚动
                    double actualStartPosition = e.StartPosition;
                    if (!isAtStartPosition && _keyframeManager != null && _keyframeManager.CurrentKeyframeIndex == 0)
                    {
                        var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                        if (keyframes != null && keyframes.Count > 0)
                        {
                            var firstKeyframe = keyframes.OrderBy(k => k.YPosition).ThenBy(k => k.Id).First();
                            // P关键帧仅作为停止点：不要把起点“吸附”为当前位置，否则二次播放会卡在P点不动。
                            if (!firstKeyframe.AutoPause && Math.Abs(currentOffsetBefore - firstKeyframe.YPosition) <= 1)
                            {
                                actualStartPosition = currentOffsetBefore;
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"   [合成播放滚动] 已在第一个关键帧位置，调整起始位置为当前位置");
                                //System.Diagnostics.Debug.WriteLine($"      原始起始位置: {e.StartPosition:F1}, 调整后: {actualStartPosition:F1}");
                                //#endif
                            }
                        }
                    }

                    // 开始FPS监控
                    StartFpsMonitoring();
                    SetAutoProjectionSyncEnabled(false);

                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"    [合成播放滚动] 开始滚动动画");
                    //System.Diagnostics.Debug.WriteLine($"      从 {actualStartPosition:F1} 滚动到 {e.EndPosition:F1}");
                    //#endif

                    // 使用AnimationHelper执行滚动动画，并保存Storyboard引用
                    _compositeScrollStoryboard = Utils.AnimationHelper.AnimateScroll(
                        scrollViewer,
                        actualStartPosition,
                        e.EndPosition,
                        TimeSpan.FromSeconds(e.Duration),
                        () =>
                        {
                            // 滚动完成回调
                            _compositeScrollStoryboard = null; // 清除引用
                            SetAutoProjectionSyncEnabled(true);
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [合成播放滚动] 滚动完成，最终位置: {scrollViewer.VerticalOffset:F1}");
                            //#endif
                            
                            // 更新投影
                            if (IsProjectionEnabled)
                            {
                                _projectionManager?.SyncProjectionScroll(force: true);
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"    [合成播放滚动] 滚动完成后更新投影");
                                //#endif
                                UpdateProjection();
                            }

                            // 停止FPS监控
                            StopFpsMonitoring();
                        },
                        _keyframeManager?.ScrollEasingType ?? "Bezier",
                        _keyframeManager?.IsLinearScrolling ?? false,
                        e.SpeedRatio, // 传递速度倍率，直接加速动画
                        () =>
                        {
                            _fpsMonitor?.RecordMainFrame();
                            _projectionManager?.SyncSharedRendering();
                            if (IsProjectionEnabled)
                            {
                                _projectionManager?.SyncProjectionScroll(force: true);
                            }
                        }
                    );
                }
                catch (Exception)
                {
                    SetAutoProjectionSyncEnabled(true);
                    // 忽略异常
                }
            });
        }
        
        /// <summary>
        /// 停止合成播放的滚动动画
        /// </summary>
        private void StopCompositeScrollAnimation()
        {
            if (_compositeScrollStoryboard != null)
            {
                var scrollViewer = ImageScrollViewer;
                if (scrollViewer != null)
                {
                    // 获取当前滚动位置
                    var currentOffset = scrollViewer.VerticalOffset;
                    
                    // 停止Storyboard
                    _compositeScrollStoryboard.Stop();
                    
                    // 清除动画属性（关键！）
                    scrollViewer.BeginAnimation(Utils.AnimationHelper.GetAnimatedVerticalOffsetProperty(), null);
                    
                    // 清除引用
                    _compositeScrollStoryboard = null;
                    
                    // 保持当前位置
                    scrollViewer.ScrollToVerticalOffset(currentOffset);
                    SetAutoProjectionSyncEnabled(true);
                    if (IsProjectionEnabled)
                    {
                        _projectionManager?.SyncProjectionScroll(force: true);
                    }
                }
            }
        }

        /// <summary>
        /// 合成播放停止滚动请求事件处理
        /// </summary>
        private void OnCompositeScrollStopRequested(object sender, Services.Implementations.CompositeScrollStopEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    bool isPauseOnly = (sender as Services.Implementations.CompositePlaybackService)?.IsPaused == true;
                    bool preserveCountdown = e?.PreserveCountdown == true;

                    // 立即停止合成播放的滚动动画
                    StopCompositeScrollAnimation();
                    
                    // 停止FPS监控
                    StopFpsMonitoring();

                    // 暂停场景：保留当前倒计时显示与状态
                    // 停止场景：清空倒计时并恢复指示块
                    if (!isPauseOnly && !preserveCountdown)
                    {
                        CountdownText.Text = COUNTDOWN_DEFAULT_TEXT;
                        _countdownService?.Stop();
                        _keyframeManager?.UpdatePreviewLines();
                    }
                }
                catch (Exception)
                {
                    // 忽略异常
                }
            });
        }

        /// <summary>
        /// 合成播放完成事件处理
        /// </summary>
        private void OnCompositePlaybackCompleted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _lastCompositeAutoStopKeyframeId = -1;
                SetCompositePlayButtonContent(false);
                BtnCompositePause.Visibility = Visibility.Collapsed;
                SetCompositePauseButtonContent(false);
                BtnCompositeSpeed.Visibility = Visibility.Collapsed;
                ShowStatus("合成播放完成");
                
                // 停止倒计时显示
                _countdownService?.Stop();
                
                // 恢复正常的关键帧指示块显示
                _keyframeManager?.UpdatePreviewLines();
            });
        }

        private async Task TryAutoStopAtCompositeKeyframeAsync(int keyframeId)
        {
            try
            {
                if (keyframeId <= 0 || _keyframeManager == null)
                {
                    return;
                }

                if (_lastCompositeAutoStopKeyframeId != keyframeId)
                {
                    _lastCompositeAutoStopKeyframeId = -1;
                }

                var compositeService = _playbackServiceFactory?.GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite)
                    as Services.Implementations.CompositePlaybackService;
                if (compositeService == null || !compositeService.IsPlaying)
                {
                    return;
                }

                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[合成播放][AutoStop] enter keyframeId={keyframeId}, currentImageId={_currentImageId}, last={_lastCompositeAutoStopKeyframeId}, isPlaying={compositeService.IsPlaying}, isPaused={compositeService.IsPaused}");
                #endif

                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                if (keyframes == null || keyframes.Count == 0)
                {
                    keyframes = await _keyframeManager.GetKeyframesAsync(_currentImageId);
                }

                var keyframe = keyframes?.FirstOrDefault(k => k.Id == keyframeId);
                if (keyframe == null || !keyframe.AutoPause)
                {
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[合成播放][AutoStop] skip keyframeId={keyframeId}, found={keyframe != null}, autoStop={(keyframe?.AutoPause ?? false)}");
                    #endif
                    return;
                }

                if (_lastCompositeAutoStopKeyframeId == keyframeId)
                {
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[合成播放][AutoStop] dedup keyframeId={keyframeId}");
                    #endif
                    return;
                }

                _lastCompositeAutoStopKeyframeId = keyframeId;
                int displayIndex = (keyframe.OrderIndex ?? 0) + 1;
                await StopCompositePlaybackLikeButtonAsync(compositeService, $"到达关键帧 #{displayIndex}，已自动停止");
            }
            catch
            {
                // 忽略自动停止异常，避免影响主播放链路
            }
        }

        /// <summary>
        /// 与“合成播放按钮点击后的停止分支”保持一致的停止逻辑。
        /// </summary>
        private async Task StopCompositePlaybackLikeButtonAsync(
            Services.Implementations.CompositePlaybackService compositeService,
            string statusMessage)
        {
            if (compositeService == null)
            {
                return;
            }

            await compositeService.StopPlaybackAsync();
            _lastCompositeAutoStopKeyframeId = -1;

            #if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[合成播放][StopLikeButton] status='{statusMessage}', imageId={_currentImageId}, isPlayingAfterStop={compositeService.IsPlaying}, isPausedAfterStop={compositeService.IsPaused}");
            #endif

            SetCompositePlayButtonContent(false);
            BtnCompositePause.Visibility = Visibility.Collapsed;
            SetCompositePauseButtonContent(false);
            BtnCompositeSpeed.Visibility = Visibility.Collapsed;

            // 与按钮停止一致：确保滚动动画和倒计时都被清理
            _keyframeManager?.StopScrollAnimation();
            StopCompositeScrollAnimation();
            CountdownText.Text = COUNTDOWN_DEFAULT_TEXT;
            _countdownService?.Stop();

            ShowStatus(statusMessage);
        }

        /// <summary>
        /// 合成播放进度更新事件处理（显示倒计时）
        /// </summary>
        private void OnCompositeProgressUpdated(object sender, Services.Interfaces.PlaybackProgressEventArgs e)
        {
            if (_countdownService == null)
            {
                return;
            }

            bool isCompositePaused = (sender as Services.Implementations.CompositePlaybackService)?.IsPaused == true;

            // 暂停期间（例如暂停时调速触发的进度同步）只校准剩余时间，禁止重启倒计时
            if (isCompositePaused)
            {
                _countdownService.SyncRemaining(e.RemainingTime);
                return;
            }

            // 首次进度事件启动倒计时；后续（含切速）只同步剩余时间，避免已运行时间被重置
            if (_countdownService.IsRunning)
            {
                _countdownService.SyncRemaining(e.RemainingTime);
            }
            else
            {
                _countdownService.Start(e.RemainingTime);
            }
        }
        
        /// <summary>
        /// 合成播放速度变化事件处理
        /// </summary>
        private void OnCompositeSpeedChanged(object sender, Services.Implementations.SpeedChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateSpeedButtonText(e.Speed);
            });
        }
        
        /// <summary>
        /// 更新速度按钮文本
        /// </summary>
        private void UpdateSpeedButtonText(double speed)
        {
            SetCompositeSpeedButtonContent(speed);
        }
        
        private void BtnCompositeSpeed_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowCompositeSpeedMenu();
        }

        private async void BtnCompositePause_Click(object sender, RoutedEventArgs e)
        {
            await ToggleCompositePauseResumeByHotkeyAsync();
        }

        private void ShowCompositeSpeedMenu()
        {
            var compositeService = _playbackServiceFactory?.GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite) 
                as Services.Implementations.CompositePlaybackService;
            
            if (compositeService == null || !compositeService.IsPlaying)
            {
                return;
            }

            _compositeSpeedMenu ??= BuildCompositeSpeedMenu();
            RefreshCompositeSpeedMenuCheckedState(_compositeSpeedMenu, compositeService.Speed);

            if (_compositeSpeedMenu.IsOpen)
            {
                StartCompositeSpeedMenuAutoCloseTimer();
                return;
            }

            _compositeSpeedMenu.PlacementTarget = BtnCompositeSpeed;
            _compositeSpeedMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            _compositeSpeedMenu.HorizontalOffset = 0;
            _compositeSpeedMenu.VerticalOffset = 4;
            if (BtnCompositeSpeed != null && BtnCompositeSpeed.ActualWidth > 0)
            {
                _compositeSpeedMenu.Width = BtnCompositeSpeed.ActualWidth;
                _compositeSpeedMenu.MinWidth = BtnCompositeSpeed.ActualWidth;
            }
            _compositeSpeedMenu.IsOpen = true;
            _compositeSpeedMenuLastKeepAliveUtc = DateTime.UtcNow;
            StartCompositeSpeedMenuAutoCloseTimer();
        }

        private ContextMenu BuildCompositeSpeedMenu()
        {
            var contextMenu = new ContextMenu
            {
                MinWidth = 1
            };

            if (TryFindResource("NoBorderContextMenuStyle") is Style menuStyle)
            {
                contextMenu.Style = menuStyle;
            }

            var speedOptions = new double[]
            {
                0.50,
                0.75,
                1.00,
                1.10,
                1.25,
                1.50,
                2.00,
                2.50,
                3.00
            };

            foreach (var speed in speedOptions)
            {
                var menuItem = new MenuItem
                {
                    Header = BuildCompositeSpeedMenuHeader(speed),
                    IsCheckable = true,
                    Tag = speed
                };

                menuItem.Click += (_, _) =>
                {
                    var compositeService = _playbackServiceFactory?.GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite)
                        as Services.Implementations.CompositePlaybackService;
                    if (compositeService == null || !compositeService.IsPlaying)
                    {
                        return;
                    }

                    if (Math.Abs(compositeService.Speed - speed) < 0.01)
                    {
                        return;
                    }

                    _ = Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() =>
                        {
                            compositeService.SetSpeed(speed);
                            RefreshCompositeSpeedMenuCheckedState(_compositeSpeedMenu, speed);
                            ShowStatus($"播放速度已设置为 {speed:F2}x");
                        }));
                };

                contextMenu.Items.Add(menuItem);
            }

            contextMenu.MouseEnter += (_, _) => StopCompositeSpeedMenuAutoCloseTimer();
            contextMenu.MouseLeave += (_, _) => StartCompositeSpeedMenuAutoCloseTimer();
            contextMenu.Closed += (_, _) => StopCompositeSpeedMenuAutoCloseTimer();

            return contextMenu;
        }

        private UIElement BuildCompositeSpeedMenuHeader(double speed)
        {
            var speedText = new TextBlock
            {
                Text = $"{speed:F2}x",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            speedText.SetResourceReference(TextBlock.ForegroundProperty, "BrushMenuText");
            return speedText;
        }

        private void RefreshCompositeSpeedMenuCheckedState(ContextMenu menu, double currentSpeed)
        {
            if (menu == null)
            {
                return;
            }

            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Tag is double speed)
                {
                    item.IsChecked = Math.Abs(currentSpeed - speed) < 0.01;
                }
            }
        }

        private void StartCompositeSpeedMenuAutoCloseTimer()
        {
            _compositeSpeedMenuAutoCloseTimer ??= new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };

            _compositeSpeedMenuAutoCloseTimer.Tick -= CompositeSpeedMenuAutoCloseTimer_Tick;
            _compositeSpeedMenuAutoCloseTimer.Tick += CompositeSpeedMenuAutoCloseTimer_Tick;
            _compositeSpeedMenuAutoCloseTimer.Stop();
            _compositeSpeedMenuAutoCloseTimer.Start();
        }

        private void StopCompositeSpeedMenuAutoCloseTimer()
        {
            _compositeSpeedMenuAutoCloseTimer?.Stop();
        }

        private void CompositeSpeedMenuAutoCloseTimer_Tick(object sender, EventArgs e)
        {
            var menu = _compositeSpeedMenu;
            if (menu == null || !menu.IsOpen)
            {
                StopCompositeSpeedMenuAutoCloseTimer();
                return;
            }

            bool mouseOnButton = IsMouseInsideElement(BtnCompositeSpeed);
            bool mouseOnMenu = IsMouseInsideContextMenuPopup(menu);
            bool mouseOnSubmenu = IsMouseInsideAnyOpenSubmenuPopup(menu);
            bool shouldKeepOpen = mouseOnButton || mouseOnMenu || mouseOnSubmenu;

            if (shouldKeepOpen)
            {
                _compositeSpeedMenuLastKeepAliveUtc = DateTime.UtcNow;
                return;
            }

            var elapsedSinceKeepAlive = (DateTime.UtcNow - _compositeSpeedMenuLastKeepAliveUtc).TotalMilliseconds;
            if (elapsedSinceKeepAlive < CompositeSpeedMenuCloseGracePeriodMs)
            {
                return;
            }

            StopCompositeSpeedMenuAutoCloseTimer();
            menu.IsOpen = false;
        }

        /// <summary>
        /// 合成播放当前关键帧变化事件处理（更新指示块颜色）
        /// </summary>
        private void OnCompositeCurrentKeyframeChanged(object sender, Services.Implementations.CurrentKeyframeChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    //#if DEBUG
                    //var currentOffset = ImageScrollViewer?.VerticalOffset ?? 0;
                    //System.Diagnostics.Debug.WriteLine($"[合成播放关键帧变化] ========== 关键帧变化事件 ==========");
                    //System.Diagnostics.Debug.WriteLine($"   关键帧ID: {e.KeyframeId}");
                    //System.Diagnostics.Debug.WriteLine($"   关键帧位置: {e.YPosition:F1}");
                    //System.Diagnostics.Debug.WriteLine($"   当前滚动位置: {currentOffset:F1}");
                    //System.Diagnostics.Debug.WriteLine($"   当前关键帧索引: {_keyframeManager?.CurrentKeyframeIndex ?? -1}");
                    //System.Diagnostics.Debug.WriteLine($"   位置差值: {Math.Abs(currentOffset - e.YPosition):F1}");
                    //#endif
                    
                    // 重绘关键帧指示块，高亮当前播放的关键帧
                    UpdateCompositePlaybackIndicator(e.KeyframeId, e.YPosition);

                    #if DEBUG
                    var compositeService = sender as Services.Implementations.CompositePlaybackService;
                    //System.Diagnostics.Debug.WriteLine($"[合成播放][KeyframeChanged] keyframeId={e.KeyframeId}, y={e.YPosition:F1}, arrived={e.IsSegmentArrived}, isPlaying={compositeService?.IsPlaying}, isPaused={compositeService?.IsPaused}");
                    #endif

                    // 仅在滚动段真正到达终点时触发自动停止，避免回调竞态导致“停了又开”
                    if (e.IsSegmentArrived)
                    {
                        _ = TryAutoStopAtCompositeKeyframeAsync(e.KeyframeId);
                    }
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"    [合成播放关键帧变化] 指示块更新完成");
                    //#endif
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($" 更新合成播放指示块失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 获取当前滚动位置事件处理
        /// </summary>
        private void OnCurrentScrollPositionRequested(object sender, Services.Implementations.CurrentScrollPositionRequestEventArgs e)
        {
            void ResolveCurrentPosition()
            {
                try
                {
                    var scrollViewer = ImageScrollViewer;
                    if (scrollViewer != null)
                    {
                        e.CurrentScrollPosition = scrollViewer.VerticalOffset;
                    }
                    else
                    {
                        e.CurrentScrollPosition = 0;
                    }
                }
                #if DEBUG
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($" 获取当前滚动位置失败: {ex.Message}");
                    e.CurrentScrollPosition = 0;
                }
                #else
                catch (Exception)
                {
                    e.CurrentScrollPosition = 0;
                }
                #endif
            }

            if (Dispatcher.CheckAccess())
            {
                ResolveCurrentPosition();
            }
            else
            {
                Dispatcher.Invoke(ResolveCurrentPosition);
            }
        }
        
        /// <summary>
        /// 处理获取可滚动高度请求
        /// </summary>
        private void OnScrollableHeightRequested(object sender, Services.Implementations.ScrollableHeightRequestEventArgs e)
        {
            void ResolveScrollableHeight()
            {
                try
                {
                    // 获取ScrollViewer的可滚动高度（ExtentHeight - ViewportHeight）
                    // ExtentHeight: 内容的总高度
                    // ViewportHeight: 可视区域的高度
                    // ScrollableHeight: 可滚动的高度（ExtentHeight - ViewportHeight）
                    double scrollableHeight = ImageScrollViewer.ScrollableHeight;
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"ScrollViewer信息:");
                    //System.Diagnostics.Debug.WriteLine($"   ExtentHeight: {ImageScrollViewer.ExtentHeight}");
                    //System.Diagnostics.Debug.WriteLine($"   ViewportHeight: {ImageScrollViewer.ViewportHeight}");
                    //System.Diagnostics.Debug.WriteLine($"   ScrollableHeight: {scrollableHeight}");
                    //#endif
                    
                    // 返回可滚动高度
                    e.ScrollableHeight = scrollableHeight;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($" 获取可滚动高度失败: {ex.Message}");
                    e.ScrollableHeight = 0; // 失败时返回0，使用默认值
                }
            }

            if (Dispatcher.CheckAccess())
            {
                ResolveScrollableHeight();
            }
            else
            {
                Dispatcher.Invoke(ResolveScrollableHeight);
            }
        }

        /// <summary>
        /// 更新浮动合成播放按钮的显示状态
        /// </summary>
        private void UpdateFloatingCompositePlayButton()
        {
            // 简化逻辑：只判断是否是正常图片文件
            // 原图模式 → 隐藏
            // 媒体文件 → 隐藏
            // 歌词模式 → 隐藏
            // 圣经模式 → 隐藏
            // 正常图片 → 显示
            
            if (_originalMode)
            {
                // 原图模式，隐藏按钮面板
                CompositePlaybackPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (_isLyricsMode)
            {
                // 歌词模式，隐藏按钮面板
                CompositePlaybackPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (_isBibleMode)
            {
                // 圣经模式，隐藏按钮面板
                CompositePlaybackPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (_currentImageId == 0)
            {
                // 没有加载图片，隐藏按钮面板
                CompositePlaybackPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // 幻灯片状态且非分割模式：隐藏按钮面板
            if (TextEditorPanel.Visibility == Visibility.Visible && _currentTextProject != null && !IsInSplitMode())
            {
                // 幻灯片状态但非分割模式（正常文本编辑），隐藏按钮面板
                CompositePlaybackPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // 正常文件夹的图片，显示按钮面板
            CompositePlaybackPanel.Visibility = Visibility.Visible;
            BtnFloatingCompositePlay.Visibility = Visibility.Visible;
            BtnCompositePause.Visibility = Visibility.Collapsed;
            SetCompositePauseButtonContent(false);
            BtnCompositeSpeed.Visibility = Visibility.Collapsed;
            
            // 异步加载合成标记状态并设置按钮颜色
            _ = UpdateCompositeButtonColorAsync();
        }

        /// <summary>
        /// 异步更新合成播放按钮颜色
        /// </summary>
        private async Task UpdateCompositeButtonColorAsync()
        {
            if (_keyframeManager == null || _currentImageId <= 0)
            {
                // 默认蓝色
                SetCompositeButtonColor(false);
                return;
            }

            try
            {
                bool isEnabled = await _keyframeManager.GetCompositePlaybackEnabledAsync(_currentImageId);
                SetCompositeButtonColor(isEnabled);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" 更新按钮颜色失败: {ex.Message}");
                SetCompositeButtonColor(false);
            }
        }

        /// <summary>
        /// 设置合成播放按钮颜色
        /// </summary>
        private void SetCompositeButtonColor(bool isCompositeEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                if (isCompositeEnabled)
                {
                    // 已标记合成播放 → 绿色
                    BtnFloatingCompositePlay.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(76, 175, 80)); // #4CAF50 绿色
                    BtnFloatingCompositePlay.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(56, 142, 60)); // #388E3C 深绿色
                }
                else
                {
                    // 未标记 → 蓝色（默认）
                    BtnFloatingCompositePlay.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(33, 150, 243)); // #2196F3 蓝色
                    BtnFloatingCompositePlay.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(25, 118, 210)); // #1976D2 深蓝色
                }
            });
        }

        /// <summary>
        /// 停止合成播放（用于切换图片或清空图片时重置状态）
        /// </summary>
        internal async Task StopCompositePlaybackAsync()
        {
            try
            {
                var compositeService = _playbackServiceFactory?.GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite) 
                    as Services.Implementations.CompositePlaybackService;

                    if (compositeService != null && compositeService.IsPlaying)
                    {
                        await compositeService.StopPlaybackAsync();
                    
                    // 更新UI（必须在UI线程）
                    if (Dispatcher.CheckAccess())
                    {
                        SetCompositePlayButtonContent(false);
                        BtnCompositePause.Visibility = Visibility.Collapsed;
                        SetCompositePauseButtonContent(false);
                        BtnCompositeSpeed.Visibility = Visibility.Collapsed;
                        
                        // 停止滚动动画
                        _keyframeManager?.StopScrollAnimation();
                        StopCompositeScrollAnimation();
                        
                        // 重置倒计时显示
                        CountdownText.Text = COUNTDOWN_DEFAULT_TEXT;
                            _countdownService?.Stop();
                        }
                        else
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            SetCompositePlayButtonContent(false);
                            BtnCompositePause.Visibility = Visibility.Collapsed;
                            SetCompositePauseButtonContent(false);
                            BtnCompositeSpeed.Visibility = Visibility.Collapsed;
                            
                            // 停止滚动动画
                            _keyframeManager?.StopScrollAnimation();
                            StopCompositeScrollAnimation();
                            
                            // 重置倒计时显示
                            CountdownText.Text = COUNTDOWN_DEFAULT_TEXT;
                                _countdownService?.Stop();
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                System.Diagnostics.Debug.WriteLine($" 停止合成播放失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新合成播放指示块（高亮当前播放的关键帧）
        /// </summary>
        private void UpdateCompositePlaybackIndicator(int currentKeyframeId, double yPosition)
        {
            try
            {
                if (_currentImageId <= 0) return;

                //#if DEBUG
                //var childrenCountBefore = ScrollbarIndicatorsCanvas.Children.Count;
                //System.Diagnostics.Debug.WriteLine($"[合成播放指示块] ========== 更新指示块 ==========");
                //System.Diagnostics.Debug.WriteLine($"   当前关键帧ID: {currentKeyframeId}");
                //System.Diagnostics.Debug.WriteLine($"   关键帧位置: {yPosition:F1}");
                //System.Diagnostics.Debug.WriteLine($"   清除前指示块数量: {childrenCountBefore}");
                //#endif

                // 清除所有指示块
                ScrollbarIndicatorsCanvas.Children.Clear();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"    [合成播放指示块] 已清除所有指示块");
                //#endif

                // 获取当前图片的所有关键帧
                var keyframes = _keyframeManager?.GetKeyframesFromCache(_currentImageId);
                if (keyframes == null || !keyframes.Any()) return;

                // 获取尺寸信息
                double imageCanvasHeight = KeyframePreviewLinesCanvas.ActualHeight;
                double scrollbarCanvasHeight = ScrollbarIndicatorsCanvas.ActualHeight;

                if (imageCanvasHeight <= 0 || scrollbarCanvasHeight <= 0) return;

                // 绘制每个关键帧指示块
                foreach (var keyframe in keyframes)
                {
                    double relativePosition = keyframe.YPosition / imageCanvasHeight;
                    double indicatorY = relativePosition * scrollbarCanvasHeight;

                    // 创建容器
                    var indicatorContainer = new Grid();

                    // 判断是否是当前播放的关键帧
                    bool isCurrentPlayback = (keyframe.Id == currentKeyframeId);

                    // 方块颜色：当前播放=绿色，其他=红色
                    var indicator = new System.Windows.Shapes.Rectangle
                    {
                        Width = isCurrentPlayback ? 22 : 20,
                        Height = isCurrentPlayback ? 22 : 20,
                        Fill = new System.Windows.Media.SolidColorBrush(
                            isCurrentPlayback 
                                ? System.Windows.Media.Color.FromRgb(0, 255, 0)   // 绿色
                                : System.Windows.Media.Color.FromRgb(255, 32, 32)), // 红色
                        RadiusX = 3,
                        RadiusY = 3,
                        Opacity = isCurrentPlayback ? 0.7 : 0.45,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = keyframe.Id
                    };

                    indicatorContainer.Children.Add(indicator);

                    // 如果有循环次数提示，显示数字
                    if (keyframe.LoopCount.HasValue && keyframe.LoopCount.Value > 0)
                    {
                        var loopText = new TextBlock
                        {
                            Text = keyframe.LoopCount.Value.ToString(),
                            FontSize = isCurrentPlayback ? 14 : 13,
                            FontWeight = FontWeights.Bold,
                            Foreground = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(255, 255, 255)),
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                            VerticalAlignment = System.Windows.VerticalAlignment.Center,
                            IsHitTestVisible = false
                        };
                        indicatorContainer.Children.Add(loopText);
                    }

                    Canvas.SetTop(indicatorContainer, indicatorY - (isCurrentPlayback ? 11 : 10));
                    Canvas.SetLeft(indicatorContainer, -2);
                    ScrollbarIndicatorsCanvas.Children.Add(indicatorContainer);
                }

                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"    [合成播放指示块] 指示块更新完成，共绘制 {keyframes.Count} 个指示块");
                //System.Diagnostics.Debug.WriteLine($"   最终指示块数量: {ScrollbarIndicatorsCanvas.Children.Count}");
                //#endif

            }
            catch (Exception)
            {
                // 忽略异常
            }
        }

        /// <summary>
        /// 清除时间数据按钮点击事件
        /// </summary>
        private async void BtnClearTiming_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            if (_playbackViewModel == null)
            {
                ShowStatus("播放系统未初始化");
                return;
            }

            var result = MessageBox.Show(
                "确定要清除当前图片的时间数据吗？\n此操作不可撤销。",
                "确认清除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _playbackViewModel.CurrentImageId = _currentImageId;
                await _playbackViewModel.ClearTimingDataCommand.ExecuteAsync(null);
                ShowStatus("已清除时间数据");
            }
        }


        #endregion

    }
}






