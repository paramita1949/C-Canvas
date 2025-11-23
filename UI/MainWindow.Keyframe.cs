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
    /// MainWindow 关键帧功能扩展
    /// </summary>
    public partial class MainWindow
    {
        #region 关键帧和播放字段

        private KeyframeManager _keyframeManager;
        private KeyframeRepository _keyframeRepository; // 仅供 KeyframeManager 使用
        
        // 滚动速度设置（默认9秒）
        private double _scrollDuration = 9.0;
        
        // 滚动缓动类型（默认线性）
        private string _scrollEasingType = "Linear";
        
        // 是否使用线性滚动（无缓动）
        private bool _isLinearScrolling = true;
        
        // 合成播放的Storyboard引用（用于停止时清除）
        private System.Windows.Media.Animation.Storyboard _compositeScrollStoryboard = null;

        #endregion

        #region 关键帧状态管理

        /// <summary>
        /// 重置关键帧索引到-1（图片加载时调用）
        /// 参考Python版本：image_processor.py 第341行
        /// </summary>
        public void ResetKeyframeIndex()
        {
            if (_keyframeManager != null)
            {
                _keyframeManager.CurrentKeyframeIndex = -1;
                // System.Diagnostics.Debug.WriteLine("🔄 [图片加载] 重置关键帧索引为-1");
                
                // 更新关键帧预览线和指示块
                _keyframeManager?.UpdatePreviewLines();
            }
        }

        #endregion

        #region 关键帧初始化

        /// <summary>
        /// 初始化关键帧和播放系统
        /// </summary>
        private void InitializeKeyframeSystem()
        {
            try
            {
                // 获取数据库上下文
                var dbContext = _dbManager?.GetDbContext();
                if (dbContext == null)
                {
                    // Console.WriteLine("❌ 数据库上下文未就绪");
                    return;
                }

                // 创建关键帧仓库
                _keyframeRepository = new KeyframeRepository(dbContext);

                // 获取MediaFileRepository
                var mediaFileRepository = App.GetRequiredService<Repositories.Interfaces.IMediaFileRepository>();

                // 创建关键帧管理器
                _keyframeManager = new KeyframeManager(_keyframeRepository, this, mediaFileRepository);
                
                // 从数据库加载滚动速度和缓动函数设置
                LoadScrollSpeedSettings();
                LoadScrollEasingSettings();
                
                // 初始化菜单选中状态
                Dispatcher.InvokeAsync(() =>
                {
                    UpdateScrollSpeedMenuCheck(_scrollDuration);
                    string easingName = _isLinearScrolling ? "Linear" : _scrollEasingType;
                    UpdateScrollEasingMenuCheck(easingName);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // System.Diagnostics.Debug.WriteLine("✅ 关键帧和播放系统初始化完成");
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 关键帧系统初始化异常: {ex.Message}");
                MessageBox.Show($"关键帧系统初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 关键帧按钮事件

        /// <summary>
        /// 添加关键帧按钮点击事件
        /// </summary>
        private async void BtnAddKeyframe_Click(object sender, RoutedEventArgs e)
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
                // 计算滚动位置
                double position = ImageScrollViewer.ScrollableHeight > 0
                    ? ImageScrollViewer.VerticalOffset / ImageScrollViewer.ScrollableHeight
                    : 0;
                int yPosition = (int)ImageScrollViewer.VerticalOffset;

                // 添加关键帧
                bool success = await _keyframeManager.AddKeyframeAsync(
                    _currentImageId, position, yPosition);

                if (success)
                {
                    ShowStatus($"✅ 已添加关键帧");
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
                    ShowStatus("❌ 添加关键帧失败（该位置附近已存在关键帧）");
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 添加关键帧出错: {ex.Message}");
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
                    ShowStatus("✅ 已清除所有关键帧");
                    UpdatePreviewLines();
                }
                catch (Exception ex)
                {
                    ShowStatus($"❌ 清除关键帧出错: {ex.Message}");
                }
            }
        }

        // 🛡️ 防重入标志：防止快速点击导致并发执行
        private volatile bool _isNavigatingKeyframe = false;

        /// <summary>
        /// 上一个关键帧/上一张图/上一个媒体按钮点击事件（四模式支持）
        /// </summary>
        private async void BtnPrevKeyframe_Click(object sender, RoutedEventArgs e)
        {
            // 🛡️ 防重入：如果上一次操作还没完成，直接返回
            if (_isNavigatingKeyframe)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [防重入] 上一帧操作正在执行中，忽略本次点击");
                #endif
                return;
            }

            _isNavigatingKeyframe = true;
            try
            {
                // ⏱️ 性能调试：测量关键帧切换总耗时
                var sw = System.Diagnostics.Stopwatch.StartNew();
                //System.Diagnostics.Debug.WriteLine($"");
                //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 开始上一帧操作 ==========");
                
                // 🎯 模式-1：圣经模式（向上滚动经文）
                if (_isBibleMode && BibleVerseScrollViewer.Visibility == Visibility.Visible)
                {
                    BtnBiblePrevVerse_Click(sender, e);
                    return;
                }
                
                // 🎯 模式0：文本编辑器模式（切换幻灯片）
                if (TextEditorPanel.Visibility == Visibility.Visible)
                {
                    //System.Diagnostics.Debug.WriteLine("📖 文本编辑器模式，切换到上一张幻灯片");
                    NavigateToPreviousSlide();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 幻灯片切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }
                
                if (_currentImageId == 0)
                {
                    ShowStatus("请先选择一张图片");
                    return;
                }

                // 🎯 模式1：媒体播放模式（视频/音频）
                if (IsMediaPlaybackMode())
                {
                    await SwitchToPreviousMediaFile();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 媒体切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }

                // 🎯 模式2：原图标记模式（切换相似图片）
                if (IsOriginalMarkMode())
                {
                    SwitchToPreviousSimilarImage();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 相似图片切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }

                // 🎯 模式3：关键帧模式（默认）
                if (_keyframeManager == null)
                {
                    ShowStatus("关键帧系统未初始化");
                    return;
                }
                
                //System.Diagnostics.Debug.WriteLine("🎬 关键帧模式：上一帧");

                // 如果正在录制，先记录当前帧的时间（跳转前）
                if (_playbackViewModel?.IsRecording == true && _keyframeManager.CurrentKeyframeIndex >= 0)
                {
                    var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                    if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                    {
                        var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                        _ = _playbackViewModel.RecordKeyframeTimeAsync(currentKeyframe.Id); // 异步执行不等待
                        //System.Diagnostics.Debug.WriteLine($"📝 [录制] 离开关键帧 #{_keyframeManager.CurrentKeyframeIndex + 1}，记录停留时间");
                    }
                }
                
                // 如果正在播放，记录手动操作用于实时修正（参考Python版本：keytime.py 第750-786行）
                if (_playbackViewModel?.IsPlaying == true && _keyframeManager.CurrentKeyframeIndex >= 0)
                {
                    var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                    if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                    {
                        var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                        var currentIndex = _keyframeManager.CurrentKeyframeIndex;
                        
                        // 调用播放服务的手动修正方法
                        var playbackService = App.GetRequiredService<Services.PlaybackServiceFactory>()
                            .GetPlaybackService(Database.Models.Enums.PlaybackMode.Keyframe);
                        if (playbackService is Services.Implementations.KeyframePlaybackService kfService)
                        {
                            _ = kfService.RecordManualOperationAsync(currentKeyframe.Id); // 异步执行不等待
                            //System.Diagnostics.Debug.WriteLine($"🕐 [手动跳转] 播放中点击上一帧，记录修正时间: 关键帧#{currentIndex + 1}");
                            
                            // 跳过当前等待，立即播放下一帧（参考Python版本：keyframe_navigation.py 第157-167行）
                            // 注意：上一帧总是回跳，会被Navigator强制直接跳转，所以这里跳过等待是安全的
                            kfService.SkipCurrentWaitAndPlayNext();
                            //System.Diagnostics.Debug.WriteLine($"🔄 [手动跳转] 点击上一帧，跳过当前等待");
                        }
                    }
                }
                
                // 然后执行跳转
                var navStart = sw.ElapsedMilliseconds;
                _keyframeManager.Navigator.StepToPrevKeyframe();
                var navTime = sw.ElapsedMilliseconds - navStart;
                //System.Diagnostics.Debug.WriteLine($"⏱️ [手动跳转] Navigator.StepToPrevKeyframe: {navTime}ms");
                
                sw.Stop();
                //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 关键帧切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
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
            // 🛡️ 防重入：如果上一次操作还没完成，直接返回
            if (_isNavigatingKeyframe)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [防重入] 下一帧操作正在执行中，忽略本次点击");
                #endif
                return;
            }

            _isNavigatingKeyframe = true;
            try
            {
                // ⏱️ 性能调试：测量关键帧切换总耗时
                var sw = System.Diagnostics.Stopwatch.StartNew();
                //System.Diagnostics.Debug.WriteLine($"");
                //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 开始下一帧操作 ==========");
                
                // 🎯 模式-1：圣经模式（向下滚动经文）
                if (_isBibleMode && BibleVerseScrollViewer.Visibility == Visibility.Visible)
                {
                    BtnBibleNextVerse_Click(sender, e);
                    return;
                }
                
                // 🎯 模式0：文本编辑器模式（切换幻灯片）
                if (TextEditorPanel.Visibility == Visibility.Visible)
                {
                    //System.Diagnostics.Debug.WriteLine("📖 文本编辑器模式，切换到下一张幻灯片");
                    NavigateToNextSlide();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 幻灯片切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }
                
                if (_currentImageId == 0)
                {
                    ShowStatus("请先选择一张图片");
                    return;
                }

                // 🎯 模式1：媒体播放模式（视频/音频）
                if (IsMediaPlaybackMode())
                {
                    await SwitchToNextMediaFile();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 媒体切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }

                // 🎯 模式2：原图标记模式（切换相似图片）
                if (IsOriginalMarkMode())
                {
                    SwitchToNextSimilarImage();
                    sw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 相似图片切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                    return;
                }

                // 🎯 模式3：关键帧模式（默认）
                if (_keyframeManager == null)
                {
                    ShowStatus("关键帧系统未初始化");
                    return;
                }
                
                //System.Diagnostics.Debug.WriteLine("🎬 关键帧模式：下一帧");

                // 如果正在录制，先记录当前帧的时间（跳转前）
                if (_playbackViewModel?.IsRecording == true && _keyframeManager.CurrentKeyframeIndex >= 0)
            {
                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                {
                    var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                    _ = _playbackViewModel.RecordKeyframeTimeAsync(currentKeyframe.Id); // 异步执行不等待
                    //System.Diagnostics.Debug.WriteLine($"📝 [录制] 离开关键帧 #{_keyframeManager.CurrentKeyframeIndex + 1}，记录停留时间");
                }
            }
            
            // 🎯 合成播放中：更新TOTAL时间并重新开始循环
            var serviceFactory = App.GetRequiredService<Services.PlaybackServiceFactory>();
            var compositeService = serviceFactory.GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite) 
                as Services.Implementations.CompositePlaybackService;
            
            if (compositeService != null && compositeService.IsPlaying)
            {
                // 合成播放中：更新TOTAL时间为当前已播放时间，并重新开始循环
                await compositeService.UpdateTotalAndRestartAsync();
                ShowStatus($"✅ 已更新TOTAL时间为 {compositeService.GetElapsedSeconds():F1}秒，重新开始循环");
                sw.Stop();
                return;
            }
            
            // 如果正在播放，记录手动操作用于实时修正（参考Python版本：keytime.py 第750-786行）
            if (_playbackViewModel?.IsPlaying == true && _keyframeManager.CurrentKeyframeIndex >= 0)
            {
                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                if (keyframes != null && _keyframeManager.CurrentKeyframeIndex < keyframes.Count)
                {
                    var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                    var currentIndex = _keyframeManager.CurrentKeyframeIndex;
                    
                    // 调用播放服务的手动修正方法
                    var playbackService = serviceFactory.GetPlaybackService(Database.Models.Enums.PlaybackMode.Keyframe);
                    if (playbackService is Services.Implementations.KeyframePlaybackService kfService)
                    {
                        _ = kfService.RecordManualOperationAsync(currentKeyframe.Id); // 异步执行不等待
                        //System.Diagnostics.Debug.WriteLine($"🕐 [手动跳转] 播放中点击下一帧，记录修正时间: 关键帧#{currentIndex + 1}");
                        
                        // 🔧 跳过当前等待，立即结束当前帧（参考Python版本：keyframe_navigation.py 第157-167行）
                        // 播放循环会基于实际关键帧数量判断循环，不会进入错误数据
                        kfService.SkipCurrentWaitAndPlayNext();
                        //System.Diagnostics.Debug.WriteLine($"🔄 [手动跳转] 跳过当前等待，让播放循环立即进入下一帧判断");
                    }
                }
            }
            
                // 然后执行跳转
                var navStart = sw.ElapsedMilliseconds;
                bool shouldRecordTime = _keyframeManager.Navigator.StepToNextKeyframe().Result; // 同步等待结果
                var navTime = sw.ElapsedMilliseconds - navStart;
                //System.Diagnostics.Debug.WriteLine($"⏱️ [手动跳转] Navigator.StepToNextKeyframe: {navTime}ms");
                
                sw.Stop();
                //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ========== 关键帧切换完成，总耗时: {sw.ElapsedMilliseconds}ms ==========");
                //System.Diagnostics.Debug.WriteLine($"");
                
                // shouldRecordTime 用于控制循环停止录制后是否继续记录（通常是false）
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
                var serviceFactory = App.GetRequiredService<Services.PlaybackServiceFactory>();
                var compositeService = serviceFactory.GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite) 
                    as Services.Implementations.CompositePlaybackService;

                if (compositeService == null)
                {
                    ShowStatus("❌ 合成播放服务未初始化");
                    return;
                }

                // 如果正在播放，停止
                if (compositeService.IsPlaying)
                {
                    await compositeService.StopPlaybackAsync();
                    BtnFloatingCompositePlay.Content = "🎬 合成播放";
                    
                    // 停止滚动动画
                    _keyframeManager?.StopScrollAnimation();
                    StopCompositeScrollAnimation();
                    
                    // 重置倒计时显示
                    CountdownText.Text = "倒: --";
                    var countdownService = App.GetRequiredService<Services.Interfaces.ICountdownService>();
                    countdownService?.Stop();
                    
                    //System.Diagnostics.Debug.WriteLine("🛑 [合成播放] 已停止滚动动画和倒计时");
                    ShowStatus("⏹️ 已停止合成播放");
                    return;
                }

                // 🔧 获取关键帧（可以为空或少于2个，支持无关键帧播放）
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

                // 设置播放次数（使用当前的播放次数设置）
                compositeService.PlayCount = _playbackViewModel?.PlayCount ?? -1;

                // 🔧 在开始播放前，根据情况跳转到起始位置
                if (keyframes != null && keyframes.Count >= 2)
                {
                    // 有关键帧：检查是否已经在第一帧位置，避免不必要的刷新
                    var firstKeyframe = keyframes.OrderBy(k => k.OrderIndex).First();
                    var currentOffset = ImageScrollViewer.VerticalOffset;
                    var targetOffset = firstKeyframe.YPosition;

                    // 只有当前位置与目标位置不同时才跳转（容差1像素）
                    if (Math.Abs(currentOffset - targetOffset) > 1)
                    {
                        ImageScrollViewer.ScrollToVerticalOffset(targetOffset);
                    }
                }
                else
                {
                    // 无关键帧：检查是否已经在顶部，避免不必要的刷新
                    var currentOffset = ImageScrollViewer.VerticalOffset;

                    // 只有当前不在顶部时才跳转（容差1像素）
                    if (currentOffset > 1)
                    {
                        ImageScrollViewer.ScrollToVerticalOffset(0);
                    }
                }

                // 开始播放
                await compositeService.StartPlaybackAsync(_currentImageId);
                BtnFloatingCompositePlay.Content = "⏹ 停止";
                ShowStatus("▶️ 开始合成播放");
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 合成播放失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 合成播放异常: {ex}");
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
                    Header = "⏱️ 快捷设置总时长",
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
                var customItem = new MenuItem { Header = "⚙️ 自定义时长..." };
                customItem.Click += (s, args) => OpenScriptEditWindow();
                contextMenu.Items.Add(customItem);

                contextMenu.Items.Add(new Separator());

                // 🆕 全局默认时间设定
                var currentDefaultDuration = _configManager?.CompositePlaybackDefaultDuration ?? 105.0;
                var globalDefaultItem = new MenuItem 
                { 
                    Header = $"🌐 全局默认时间: {currentDefaultDuration:F0} 秒"
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
                System.Diagnostics.Debug.WriteLine($"❌ 显示右键菜单失败: {ex.Message}");
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
                var compositeScriptRepo = App.GetRequiredService<Repositories.Interfaces.ICompositeScriptRepository>();
                
                // 更新TOTAL时长
                await compositeScriptRepo.CreateOrUpdateAsync(_currentImageId, duration, autoCalculate: false);
                
                ShowStatus($"✅ 总时长已设置为 {duration} 秒");
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ 总时长已设置: {duration}秒 (图片ID: {_currentImageId})");
                #endif
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 设置时长失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 设置总时长失败: {ex}");
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
                    ShowStatus("❌ 配置管理器未初始化");
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
                            ShowStatus($"✅ 全局默认时间已设置为 {duration:F0} 秒");
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
                ShowStatus($"❌ 打开设置对话框失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 打开全局默认时间设置对话框失败: {ex}");
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
                var scriptWindow = new ScriptEditWindow(_currentImageId, timings);
                scriptWindow.Owner = this;
                scriptWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 打开脚本窗口失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 打开脚本窗口失败: {ex}");
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

                    // 停止之前的合成滚动动画（如果有）
                    StopCompositeScrollAnimation();

                    // 🔧 如果时长为0，表示直接跳转，不滚动
                    if (e.Duration <= 0)
                    {
                        scrollViewer.ScrollToVerticalOffset(e.EndPosition);
                        
                        // 更新投影
                        if (IsProjectionEnabled)
                        {
                            UpdateProjection();
                        }
                        return;
                    }

                    // 开始FPS监控
                    StartFpsMonitoring();

                    // 使用AnimationHelper执行滚动动画，并保存Storyboard引用
                    _compositeScrollStoryboard = Utils.AnimationHelper.AnimateScroll(
                        scrollViewer,
                        e.StartPosition,
                        e.EndPosition,
                        TimeSpan.FromSeconds(e.Duration),
                        () =>
                        {
                            // 滚动完成回调
                            _compositeScrollStoryboard = null; // 清除引用
                            //System.Diagnostics.Debug.WriteLine($"✅ [合成播放] 滚动完成");
                            
                            // 更新投影
                            if (IsProjectionEnabled)
                            {
                                UpdateProjection();
                            }
                            
                            // 停止FPS监控
                            StopFpsMonitoring();
                        },
                        _keyframeManager?.ScrollEasingType ?? "Bezier",
                        _keyframeManager?.IsLinearScrolling ?? false
                    );
                }
                catch (Exception)
                {
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
                }
            }
        }

        /// <summary>
        /// 合成播放停止滚动请求事件处理
        /// </summary>
        private void OnCompositeScrollStopRequested(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 立即停止合成播放的滚动动画
                    StopCompositeScrollAnimation();
                    
                    // 停止FPS监控
                    StopFpsMonitoring();
                    
                    // 重置倒计时显示
                    CountdownText.Text = "倒: --";
                    var countdownService = App.GetRequiredService<Services.Interfaces.ICountdownService>();
                    countdownService?.Stop();
                    
                    // 恢复正常的关键帧指示块显示
                    _keyframeManager?.UpdatePreviewLines();
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
                BtnFloatingCompositePlay.Content = "🎬 合成播放";
                ShowStatus("✅ 合成播放完成");
                
                // 停止倒计时显示
                var countdownService = App.GetRequiredService<Services.Interfaces.ICountdownService>();
                countdownService?.Stop();
                
                // 恢复正常的关键帧指示块显示
                _keyframeManager?.UpdatePreviewLines();
            });
        }

        /// <summary>
        /// 合成播放进度更新事件处理（显示倒计时）
        /// </summary>
        private void OnCompositeProgressUpdated(object sender, Services.Interfaces.PlaybackProgressEventArgs e)
        {
            // 启动倒计时服务
            var countdownService = App.GetRequiredService<Services.Interfaces.ICountdownService>();
            countdownService?.Start(e.RemainingTime);
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
                    // 重绘关键帧指示块，高亮当前播放的关键帧
                    UpdateCompositePlaybackIndicator(e.KeyframeId, e.YPosition);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 更新合成播放指示块失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 处理获取可滚动高度请求
        /// </summary>
        private void OnScrollableHeightRequested(object sender, Services.Implementations.ScrollableHeightRequestEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 获取ScrollViewer的可滚动高度（ExtentHeight - ViewportHeight）
                    // ExtentHeight: 内容的总高度
                    // ViewportHeight: 可视区域的高度
                    // ScrollableHeight: 可滚动的高度（ExtentHeight - ViewportHeight）
                    double scrollableHeight = ImageScrollViewer.ScrollableHeight;
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📏 ScrollViewer信息:");
                    //System.Diagnostics.Debug.WriteLine($"   ExtentHeight: {ImageScrollViewer.ExtentHeight}");
                    //System.Diagnostics.Debug.WriteLine($"   ViewportHeight: {ImageScrollViewer.ViewportHeight}");
                    //System.Diagnostics.Debug.WriteLine($"   ScrollableHeight: {scrollableHeight}");
                    //#endif
                    
                    // 返回可滚动高度
                    e.ScrollableHeight = scrollableHeight;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 获取可滚动高度失败: {ex.Message}");
                    e.ScrollableHeight = 0; // 失败时返回0，使用默认值
                }
            });
        }

        /// <summary>
        /// 更新浮动合成播放按钮的显示状态
        /// </summary>
        private void UpdateFloatingCompositePlayButton()
        {
            // 🔧 简化逻辑：只判断是否是正常图片文件
            // 原图模式 → 隐藏
            // 媒体文件 → 隐藏
            // 歌词模式 → 隐藏
            // 圣经模式 → 隐藏
            // 正常图片 → 显示
            
            if (_originalMode)
            {
                // 原图模式，隐藏按钮
                BtnFloatingCompositePlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (_isLyricsMode)
            {
                // 歌词模式，隐藏按钮
                BtnFloatingCompositePlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (_isBibleMode)
            {
                // 圣经模式，隐藏按钮
                BtnFloatingCompositePlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (_currentImageId == 0)
            {
                // 没有加载图片，隐藏按钮
                BtnFloatingCompositePlay.Visibility = Visibility.Collapsed;
                return;
            }

            // 🔧 幻灯片状态且非分割模式：隐藏按钮
            if (TextEditorPanel.Visibility == Visibility.Visible && _currentTextProject != null && !IsInSplitMode())
            {
                // 幻灯片状态但非分割模式（正常文本编辑），隐藏按钮
                BtnFloatingCompositePlay.Visibility = Visibility.Collapsed;
                return;
            }

            // 正常文件夹的图片，显示按钮
            BtnFloatingCompositePlay.Visibility = Visibility.Visible;
            
            // 🎨 异步加载合成标记状态并设置按钮颜色
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
                System.Diagnostics.Debug.WriteLine($"❌ 更新按钮颜色失败: {ex.Message}");
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
                var serviceFactory = App.GetRequiredService<Services.PlaybackServiceFactory>();
                var compositeService = serviceFactory.GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite) 
                    as Services.Implementations.CompositePlaybackService;

                if (compositeService != null && compositeService.IsPlaying)
                {
                    await compositeService.StopPlaybackAsync();
                    
                    // 更新UI（必须在UI线程）
                    if (Dispatcher.CheckAccess())
                    {
                        BtnFloatingCompositePlay.Content = "🎬 合成播放";
                        
                        // 停止滚动动画
                        _keyframeManager?.StopScrollAnimation();
                        StopCompositeScrollAnimation();
                        
                        // 重置倒计时显示
                        CountdownText.Text = "倒: --";
                        var countdownService = App.GetRequiredService<Services.Interfaces.ICountdownService>();
                        countdownService?.Stop();
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            BtnFloatingCompositePlay.Content = "🎬 合成播放";
                            
                            // 停止滚动动画
                            _keyframeManager?.StopScrollAnimation();
                            StopCompositeScrollAnimation();
                            
                            // 重置倒计时显示
                            CountdownText.Text = "倒: --";
                            var countdownService = App.GetRequiredService<Services.Interfaces.ICountdownService>();
                            countdownService?.Stop();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 停止合成播放失败: {ex.Message}");
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

                // 清除所有指示块
                ScrollbarIndicatorsCanvas.Children.Clear();

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
                ShowStatus("✅ 已清除时间数据");
            }
        }


        #endregion


        #region 关键帧辅助方法

        /// <summary>
        /// 获取当前图片ID
        /// </summary>
        public int GetCurrentImageId()
        {
            return _currentImageId;
        }

        /// <summary>
        /// 投影是否启用
        /// </summary>
        public bool IsProjectionEnabled => _projectionManager?.IsProjectionActive ?? false;

        /// <summary>
        /// 开始FPS监控
        /// </summary>
        public void StartFpsMonitoring()
        {
            try
            {
                _fpsMonitor?.StartMonitoring();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 启动FPS监控失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 停止FPS监控
        /// </summary>
        public void StopFpsMonitoring()
        {
            try
            {
                _fpsMonitor?.StopMonitoring();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 停止FPS监控失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新关键帧指示器
        /// </summary>
        public void UpdateKeyframeIndicators()
        {
            // TODO: 实现关键帧指示器UI更新
            // 在图片上显示关键帧标记点
        }

        /// <summary>
        /// 更新预览线（图片上的横线 + 滚动条旁的方块）
        /// </summary>
        public void UpdatePreviewLines()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // 清空之前的预览线和指示块
                    KeyframePreviewLinesCanvas.Children.Clear();
                    ScrollbarIndicatorsCanvas.Children.Clear();
                    
                    // 获取当前图片ID
                    int _currentImageId = GetCurrentImageId();
                    if (_currentImageId <= 0) return;

                    // 获取当前图片的所有关键帧
                    var keyframes = _keyframeManager.GetKeyframesAsync(_currentImageId).Result;
                    if (keyframes == null || !keyframes.Any()) return;

                    // 获取尺寸信息
                    double imageCanvasWidth = KeyframePreviewLinesCanvas.ActualWidth;
                    double imageCanvasHeight = KeyframePreviewLinesCanvas.ActualHeight;
                    double scrollbarCanvasHeight = ScrollbarIndicatorsCanvas.ActualHeight;
                    double viewportHeight = ImageScrollViewer.ViewportHeight;
                    
                    if (imageCanvasHeight <= 0 || scrollbarCanvasHeight <= 0) return;

                    // 计算滚动范围
                    double scrollableHeight = Math.Max(0, imageCanvasHeight - viewportHeight);

                    // 绘制每个关键帧
                    for (int i = 0; i < keyframes.Count; i++)
                    {
                        var keyframe = keyframes[i];
                        
                        // 判断是否是当前关键帧
                        bool isCurrentKeyframe = (i == _keyframeManager.CurrentKeyframeIndex);

                        // 1. 在图片上绘制横线
                        var previewLine = new System.Windows.Shapes.Line
                        {
                            X1 = 0,
                            X2 = imageCanvasWidth,
                            Y1 = keyframe.YPosition,
                            Y2 = keyframe.YPosition,
                            StrokeThickness = 4,
                            Opacity = 0.8
                        };
                        
                        // 当前关键帧：绿色实线
                        if (isCurrentKeyframe)
                        {
                            previewLine.Stroke = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0, 255, 0)); // 鲜绿色
                            // 不设置 StrokeDashArray，默认就是实线
                        }
                        else
                        {
                            // 其他关键帧：红色虚线
                            previewLine.Stroke = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromArgb(200, 255, 0, 0)); // 半透明红色
                            previewLine.StrokeDashArray = new System.Windows.Media.DoubleCollection { 10, 5 }; // 虚线
                        }
                        
                        KeyframePreviewLinesCanvas.Children.Add(previewLine);

                        // 2. 在滚动条旁边绘制位置提示方块
                        if (scrollableHeight > 0)
                        {
                            double relativePosition = keyframe.YPosition / imageCanvasHeight;
                            double indicatorY = relativePosition * scrollbarCanvasHeight;

                            // 创建容器来放置方块和数字
                            var indicatorContainer = new Grid();
                            
                            // 方块（放大到 20x20）
                            var indicator = new System.Windows.Shapes.Rectangle
                            {
                                Width = 20,
                                Height = 20,  // 正方形
                                Fill = new System.Windows.Media.SolidColorBrush(
                                    System.Windows.Media.Color.FromRgb(255, 32, 32)), // 鲜艳红色
                                RadiusX = 3,
                                RadiusY = 3,
                                Opacity = 0.45,
                                Cursor = System.Windows.Input.Cursors.Hand,
                                Tag = keyframe.Id  // 保存关键帧ID
                            };

                            indicatorContainer.Children.Add(indicator);

                            // 如果有循环次数提示，显示数字
                            if (keyframe.LoopCount.HasValue && keyframe.LoopCount.Value > 0)
                            {
                                var loopText = new TextBlock
                                {
                                    Text = keyframe.LoopCount.Value.ToString(),
                                    FontSize = 13,  // 放大字体
                                    FontWeight = FontWeights.Bold,
                                    Foreground = new System.Windows.Media.SolidColorBrush(
                                        System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色文字
                                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                                    IsHitTestVisible = false  // 不阻挡鼠标事件
                                };
                                indicatorContainer.Children.Add(loopText);
                            }

                            Canvas.SetTop(indicatorContainer, indicatorY - 10); // 居中（调整偏移）
                            Canvas.SetLeft(indicatorContainer, -2);  // 稍微向左，确保在滚动条区域内
                            ScrollbarIndicatorsCanvas.Children.Add(indicatorContainer);
                        }
                    }

                    // 绘制当前关键帧的绿色高亮指示器
                    DrawCurrentKeyframeIndicator(scrollbarCanvasHeight, imageCanvasHeight);
                });
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 更新预览线失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 绘制当前关键帧的绿色高亮指示器（在滚动条旁边）
        /// </summary>
        private void DrawCurrentKeyframeIndicator(double scrollbarCanvasHeight, double imageCanvasHeight)
        {
            try
            {
                int _currentImageId = GetCurrentImageId();
                if (_currentImageId <= 0 || _keyframeManager.CurrentKeyframeIndex < 0) return;

                var keyframes = _keyframeManager.GetKeyframesAsync(_currentImageId).Result;
                if (keyframes == null || _keyframeManager.CurrentKeyframeIndex >= keyframes.Count) return;

                // 获取当前关键帧
                var currentKeyframe = keyframes[_keyframeManager.CurrentKeyframeIndex];
                
                // 计算在滚动条Canvas上的位置
                double relativePosition = currentKeyframe.YPosition / imageCanvasHeight;
                double indicatorY = relativePosition * scrollbarCanvasHeight;

                // 创建容器
                var currentContainer = new Grid();

                // 创建绿色高亮指示块（比红色稍大一点，22x22）
                var currentIndicator = new System.Windows.Shapes.Rectangle
                {
                    Width = 22,
                    Height = 22,  // 正方形，比红色稍大
                    Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0, 255, 0)), // 鲜绿色
                    RadiusX = 3,
                    RadiusY = 3,
                    Opacity = 0.5,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = currentKeyframe.Id
                };

                currentContainer.Children.Add(currentIndicator);

                // 如果有循环次数提示，显示数字
                if (currentKeyframe.LoopCount.HasValue && currentKeyframe.LoopCount.Value > 0)
                {
                    var loopText = new TextBlock
                    {
                        Text = currentKeyframe.LoopCount.Value.ToString(),
                        FontSize = 14,  // 放大字体
                        FontWeight = FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(255, 255, 255)), // 白色文字
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        IsHitTestVisible = false
                    };
                    currentContainer.Children.Add(loopText);
                }

                Canvas.SetTop(currentContainer, indicatorY - 11); // 居中（调整偏移）
                Canvas.SetLeft(currentContainer, -3);  // 稍微向左
                ScrollbarIndicatorsCanvas.Children.Add(currentContainer);
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 绘制当前关键帧指示块失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新倒计时显示
        /// </summary>
        /// <param name="remainingSeconds">剩余秒数，-1表示隐藏</param>
        public void UpdateCountdownDisplay(double remainingSeconds)
        {
            Dispatcher.Invoke(() =>
            {
                // TODO: 实现倒计时显示更新
                // 例如更新某个TextBlock的Text属性
                // CountdownText.Text = remainingSeconds >= 0 ? $"倒: {remainingSeconds:F1}秒" : "倒: --";
            });
        }

        /// <summary>
        /// 更新暂停倒计时显示
        /// </summary>
        /// <param name="pauseTime">暂停时间（秒）</param>
        /// <param name="remainingTime">剩余时间（秒）</param>
        /// <param name="playedTime">已播放时间（秒）</param>
        public void UpdatePauseCountdownDisplay(double pauseTime, double remainingTime, double playedTime)
        {
            Dispatcher.Invoke(() =>
            {
                // TODO: 实现暂停倒计时显示更新
                // 例如：CountdownText.Text = $"暂停{pauseTime:F1}s | 剩余{remainingTime:F1}s | 已播{playedTime:F1}s";
            });
        }

        #endregion

        #region 关键帧指示块点击跳转

        /// <summary>
        /// 点击关键帧指示块跳转
        /// </summary>
        private void ScrollbarIndicatorsCanvas_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                int _currentImageId = GetCurrentImageId();
                if (_currentImageId <= 0 || _keyframeManager == null)
                {
                    return;
                }

                // 获取点击位置
                var clickPoint = e.GetPosition(ScrollbarIndicatorsCanvas);
                double clickY = clickPoint.Y;
                double canvasHeight = ScrollbarIndicatorsCanvas.ActualHeight;

                // 获取所有关键帧（从缓存，性能优化）
                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                if (keyframes == null || !keyframes.Any())
                {
                    return;
                }

                // 获取图片Canvas高度
                double imageCanvasHeight = KeyframePreviewLinesCanvas.ActualHeight;
                if (imageCanvasHeight <= 0)
                {
                    return;
                }

                // 查找最接近点击位置的关键帧
                int closestIndex = -1;
                Keyframe closestKeyframe = null;
                double minDistance = double.MaxValue;

                for (int i = 0; i < keyframes.Count; i++)
                {
                    var keyframe = keyframes[i];
                    double relativePosition = keyframe.YPosition / imageCanvasHeight;
                    double indicatorY = relativePosition * canvasHeight;
                    double distance = Math.Abs(indicatorY - clickY);

                    if (distance < minDistance && distance < 20) // 20px容差
                    {
                        minDistance = distance;
                        closestIndex = i;
                        closestKeyframe = keyframe;
                    }
                }

                if (closestIndex >= 0 && closestKeyframe != null)
                {
                    // 右键：设置循环次数
                    if (e.ChangedButton == System.Windows.Input.MouseButton.Right)
                    {
                        ShowKeyframeLoopCountMenu(closestKeyframe, e.GetPosition(this));
                        e.Handled = true;
                        return;
                    }

                    // 左键：跳转
                    // 更新当前关键帧索引
                    _keyframeManager.UpdateKeyframeIndex(closestIndex);

                    // 滚动到目标关键帧
                    double scrollableHeight = Math.Max(0, imageCanvasHeight - ImageScrollViewer.ViewportHeight);
                    
                    if (scrollableHeight > 0)
                    {
                        double targetOffset = closestKeyframe.YPosition;
                        ImageScrollViewer.ScrollToVerticalOffset(targetOffset);
                        
                        ShowStatus($"🎯 跳转到关键帧 #{closestIndex + 1}/{keyframes.Count}");
                        
                        // 刷新预览线显示
                        UpdatePreviewLines();
                    }
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 点击跳转失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示关键帧循环次数设置菜单
        /// </summary>
        private void ShowKeyframeLoopCountMenu(Keyframe keyframe, System.Windows.Point position)
        {
            var menu = new ContextMenu();
            
            // 🔑 应用自定义样式
            menu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");
            
            // 菜单标题
            var titleItem = new MenuItem
            {
                Header = $"关键帧 #{keyframe.OrderIndex + 1}",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            };
            menu.Items.Add(titleItem);
            menu.Items.Add(new Separator());
            
            // 设置数字选项（只保留 2、3、4）
            foreach (int i in new[] { 2, 3, 4 })
            {
                int loopCount = i;
                var menuItem = new MenuItem
                {
                    Header = $"{i}",
                    IsChecked = keyframe.LoopCount == i,
                    FontSize = 14
                };
                menuItem.Click += async (s, e) => await SetKeyframeLoopCount(keyframe.Id, loopCount);
                menu.Items.Add(menuItem);
            }
            
            menu.Items.Add(new Separator());
            
            // 清除设置
            var clearItem = new MenuItem
            {
                Header = "清除",
                FontWeight = FontWeights.Bold
            };
            clearItem.Click += async (s, e) => await SetKeyframeLoopCount(keyframe.Id, null);
            menu.Items.Add(clearItem);
            
            // 显示菜单
            menu.IsOpen = true;
            menu.PlacementTarget = this;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
            menu.HorizontalOffset = position.X;
            menu.VerticalOffset = position.Y;
        }

        /// <summary>
        /// 设置关键帧循环次数
        /// </summary>
        private async Task SetKeyframeLoopCount(int keyframeId, int? loopCount)
        {
            try
            {
                if (_keyframeRepository == null)
                {
                    ShowStatus("❌ 关键帧系统未初始化");
                    return;
                }

                bool success = await _keyframeRepository.UpdateLoopCountAsync(keyframeId, loopCount);
                
                if (success)
                {
                    // 清除缓存
                    int _currentImageId = GetCurrentImageId();
                    if (_keyframeManager != null && _currentImageId > 0)
                    {
                        // 强制刷新缓存（异步不等待）
                        _ = _keyframeManager.GetKeyframesAsync(_currentImageId);
                    }
                    
                    // 刷新UI显示
                    UpdatePreviewLines();
                    
                    if (loopCount.HasValue)
                    {
                        ShowStatus($"✅ 已设置循环提示: {loopCount}遍");
                    }
                    else
                    {
                        ShowStatus($"✅ 已清除循环提示");
                    }
                }
                else
                {
                    ShowStatus("❌ 设置失败");
                }
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 设置循环次数失败: {ex.Message}");
                ShowStatus($"❌ 设置失败: {ex.Message}");
            }
        }

        #endregion
        
        #region 滚动速度设置
        
        /// <summary>
        /// 从数据库加载滚动速度设置
        /// </summary>
        private void LoadScrollSpeedSettings()
        {
            try
            {
                var dbContext = _dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "scroll_speed");
                if (setting != null && double.TryParse(setting.Value, out double speed))
                {
                    _scrollDuration = speed;
                    if (_keyframeManager != null)
                    {
                        _keyframeManager.ScrollDuration = speed;
                        // 注意：不在这里设置 ScrollEasingType 和 IsLinearScrolling
                        // 这些应该在 LoadScrollEasingSettings() 中单独加载
                    }
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 加载滚动速度失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 保存滚动速度设置到数据库
        /// </summary>
        private void SaveScrollSpeedSettings()
        {
            try
            {
                var dbContext = _dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "scroll_speed");
                if (setting != null)
                {
                    setting.Value = _scrollDuration.ToString();
                }
                else
                {
                    dbContext.Settings.Add(new Setting
                    {
                        Key = "scroll_speed",
                        Value = _scrollDuration.ToString()
                    });
                }
                
                dbContext.SaveChanges();
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 保存滚动速度失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 设置滚动速度（右键菜单点击事件）
        /// </summary>
        private void SetScrollSpeed_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Tag != null)
                {
                    if (double.TryParse(menuItem.Tag.ToString(), out double speed))
                    {
                        _scrollDuration = speed;
                        
                        // 更新KeyframeManager的滚动时长（仅更新速度，不改变缓动函数设置）
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.ScrollDuration = speed;
                            // 注意：不在这里修改 ScrollEasingType 和 IsLinearScrolling
                            // 这两个属性应该只在用户明确修改缓动函数时才改变
                        }
                        
                        // 更新菜单选中状态
                        UpdateScrollSpeedMenuCheck(speed);
                        
                        // 保存到数据库
                        SaveScrollSpeedSettings();
                        ShowStatus($"✅ 滚动速度已设置为 {speed}秒");
                    }
                }
            }
            catch
            {
                ShowStatus($"❌ 设置滚动速度失败");
            }
        }
        
        /// <summary>
        /// 更新滚动速度菜单的选中状态
        /// </summary>
        private void UpdateScrollSpeedMenuCheck(double currentSpeed)
        {
            try
            {
                var contextMenu = ImageScrollViewer.ContextMenu;
                if (contextMenu == null) return;
                
                foreach (var item in contextMenu.Items)
                {
                    if (item is MenuItem parentMenu && parentMenu.Header.ToString() == "滚动速度")
                    {
                        foreach (var subItem in parentMenu.Items)
                        {
                            if (subItem is MenuItem subMenu)
                            {
                                if (subMenu.Tag != null && double.TryParse(subMenu.Tag.ToString(), out double speed))
                                {
                                    subMenu.IsChecked = Math.Abs(speed - currentSpeed) < 0.01;
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"⚠️ 更新菜单选中状态失败: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 滚动函数设置
        
        /// <summary>
        /// 设置滚动缓动函数（右键菜单点击事件）
        /// </summary>
        private void SetScrollEasing_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Tag != null)
                {
                    string easingName = menuItem.Tag.ToString();
                    
                    // 处理线性滚动（无缓动）
                    if (easingName == "Linear")
                    {
                        _isLinearScrolling = true;
                        
                        // 更新KeyframeManager
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.IsLinearScrolling = true;
                        }
                        
                        ShowStatus($"✅ 滚动函数已设置为 线性滚动");
                    }
                    else
                    {
                        // 其他缓动类型
                        _isLinearScrolling = false;
                        _scrollEasingType = easingName;
                        
                        // 更新KeyframeManager
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.IsLinearScrolling = false;
                            _keyframeManager.ScrollEasingType = easingName;
                        }
                        
                        ShowStatus($"✅ 滚动函数已设置为 {GetEasingDisplayName(easingName)}");
                    }
                    
                    // 更新菜单选中状态
                    UpdateScrollEasingMenuCheck(easingName);
                    SaveScrollEasingSettings();
                }
            }
            catch
            {
                ShowStatus($"❌ 设置滚动函数失败");
            }
        }
        
        /// <summary>
        /// 获取缓动函数显示名称
        /// </summary>
        private string GetEasingDisplayName(string easingName)
        {
            return easingName switch
            {
                "Linear" => "线性滚动",
                "OptimizedCubic" => "优化三次",
                "EaseOutExpo" => "快速启动",
                "Bezier" => "贝塞尔曲线",
                "CssEaseInOut" => "CSS缓入缓出",
                _ => easingName
            };
        }
        
        /// <summary>
        /// 更新滚动函数菜单的选中状态
        /// </summary>
        private void UpdateScrollEasingMenuCheck(string currentEasing)
        {
            try
            {
                var contextMenu = ImageScrollViewer.ContextMenu;
                if (contextMenu == null) return;
                
                foreach (var item in contextMenu.Items)
                {
                    if (item is MenuItem parentMenu && parentMenu.Header.ToString() == "滚动函数")
                    {
                        foreach (var subItem in parentMenu.Items)
                        {
                            if (subItem is MenuItem subMenu && subMenu.Tag != null)
                            {
                                subMenu.IsChecked = subMenu.Tag.ToString() == currentEasing;
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"⚠️ 更新菜单选中状态失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 保存滚动函数设置到数据库
        /// </summary>
        private void SaveScrollEasingSettings()
        {
            try
            {
                var dbContext = _dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                // 保存缓动类型或"Linear"
                string easingValue = _isLinearScrolling ? "Linear" : _scrollEasingType;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "scroll_easing");
                if (setting != null)
                {
                    setting.Value = easingValue;
                }
                else
                {
                    dbContext.Settings.Add(new Setting
                    {
                        Key = "scroll_easing",
                        Value = easingValue
                    });
                }
                
                dbContext.SaveChanges();
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 保存滚动函数失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 从数据库加载滚动函数设置
        /// </summary>
        private void LoadScrollEasingSettings()
        {
            try
            {
                var dbContext = _dbManager?.GetDbContext();
                if (dbContext == null) return;
                
                var setting = dbContext.Settings.FirstOrDefault(s => s.Key == "scroll_easing");
                if (setting != null)
                {
                    // 检查是否是线性滚动
                    if (setting.Value == "Linear")
                    {
                        _isLinearScrolling = true;
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.IsLinearScrolling = true;
                        }
                    }
                    else
                    {
                        _isLinearScrolling = false;
                        _scrollEasingType = setting.Value;
                        if (_keyframeManager != null)
                        {
                            _keyframeManager.IsLinearScrolling = false;
                            _keyframeManager.ScrollEasingType = setting.Value;
                        }
                    }
                }
                else
                {
                    // 如果数据库中没有设置，默认使用线性滚动
                    _isLinearScrolling = true;
                    if (_keyframeManager != null)
                    {
                        _keyframeManager.IsLinearScrolling = true;
                    }
                    
                    // 保存默认设置到数据库
                    dbContext.Settings.Add(new Database.Models.Setting
                    {
                        Key = "scroll_easing",
                        Value = "Linear"
                    });
                    dbContext.SaveChanges();
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("✅ 已设置默认滚动函数为线性");
                    #endif
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 加载滚动函数失败: {ex.Message}");
            }
        }
        
        #endregion

        #region 三模式切换支持方法

        /// <summary>
        /// 判断是否处于媒体播放模式（视频/音频正在播放）
        /// </summary>
        internal bool IsMediaPlaybackMode()
        {
            // 检查是否有媒体播放器且正在播放
            return _videoPlayerManager?.IsPlaying == true;
        }

        /// <summary>
        /// 判断是否处于原图标记模式
        /// </summary>
        private bool IsOriginalMarkMode()
        {
            try
            {
                // 检查当前图片是否被标记为原图模式
                if (_currentImageId == 0 || _dbManager == null)
                    return false;

                var dbContext = _dbManager.GetDbContext();
                if (dbContext == null)
                    return false;

                // 查询当前图片或其所属文件夹是否有原图标记
                var currentFile = dbContext.MediaFiles.FirstOrDefault(m => m.Id == _currentImageId);
                if (currentFile == null)
                    return false;

                // 检查图片本身是否有原图标记
                var imageMark = dbContext.OriginalMarks.FirstOrDefault(
                    m => m.ItemTypeString == "image" && m.ItemId == _currentImageId);
                
                if (imageMark != null)
                    return true;

                // 检查所属文件夹是否有原图标记
                if (currentFile.FolderId.HasValue)
                {
                    var folderMark = dbContext.OriginalMarks.FirstOrDefault(
                        m => m.ItemTypeString == "folder" && m.ItemId == currentFile.FolderId.Value);
                    
                    if (folderMark != null)
                        return true;
                }

                return false;
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 检查原图模式失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 切换到上一个媒体文件
        /// </summary>
        internal Task SwitchToPreviousMediaFile()
        {
            try
            {
                if (_videoPlayerManager == null)
                {
                    ShowStatus("媒体播放器未初始化");
                    return Task.CompletedTask;
                }

                // 调用媒体播放器的上一曲功能
                _videoPlayerManager.PlayPrevious();
                ShowStatus("⏮️ 切换到上一个媒体文件");
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 切换上一个媒体失败: {ex.Message}");
                ShowStatus($"❌ 切换失败: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 切换到下一个媒体文件
        /// </summary>
        internal Task SwitchToNextMediaFile()
        {
            try
            {
                if (_videoPlayerManager == null)
                {
                    ShowStatus("媒体播放器未初始化");
                    return Task.CompletedTask;
                }

                // 调用媒体播放器的下一曲功能
                _videoPlayerManager.PlayNext();
                ShowStatus("⏭️ 切换到下一个媒体文件");
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 切换下一个媒体失败: {ex.Message}");
                ShowStatus($"❌ 切换失败: {ex.Message}");
            }
            return Task.CompletedTask;
        }


        /// <summary>
        /// 显示Toast悬浮提示（自动消失）
        /// </summary>
        /// <param name="message">提示消息</param>
        /// <param name="durationMs">显示时长（毫秒），默认2000ms</param>
        private async void ShowToast(string message, int durationMs = 2000)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // 设置消息
                    ToastMessage.Text = message;
                    
                    // 显示Toast
                    ToastNotification.Visibility = Visibility.Visible;
                    
                    // 淡入动画
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    ToastNotification.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    
                    // 等待指定时长
                    await Task.Delay(durationMs);
                    
                    // 淡出动画
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 1,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };
                    fadeOut.Completed += (s, e) =>
                    {
                        ToastNotification.Visibility = Visibility.Collapsed;
                    };
                    ToastNotification.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Toast显示异常: {ex.Message}");
                }
            });
        }

        #endregion
    }
}

