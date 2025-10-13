using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Services.Implementations;
using ImageColorChanger.Utils;
using MessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 原图模式功能扩展
    /// 参考Python版本：Canvas/playback/playback_controller.py 行68-238
    /// </summary>
    public partial class MainWindow
    {
        #region 原图模式字段
        
        // 原图模式相关服务已在PlaybackControlViewModel中管理
        // private OriginalRecordingService _originalRecordingService;
        // private OriginalPlaybackService _originalPlaybackService;
        
        #endregion

        #region 原图模式检测

        /// <summary>
        /// 检查当前图片是否需要使用原图模式
        /// 参考Python版本：yuantu.py的should_use_original_mode逻辑
        /// </summary>
        private bool ShouldUseOriginalMode()
        {
            if (currentImageId == 0 || originalManager == null)
                return false;

            return originalManager.ShouldUseOriginalMode(currentImageId);
        }

        /// <summary>
        /// 检查是否有相似图片（至少2张）
        /// </summary>
        private bool HasSimilarImagesForOriginalMode()
        {
            if (currentImageId == 0 || originalManager == null)
                return false;

            // 查找相似图片
            bool found = originalManager.FindSimilarImages(currentImageId);
            if (!found)
                return false;

            // 必须至少有2张图片才能进行录制/播放
            return originalManager.HasSimilarImages();
        }

        #endregion

        #region 原图模式录制

        /// <summary>
        /// 开始录制原图模式（按钮点击事件）
        /// 参考Python版本：playback_controller.py 行68-109
        /// </summary>
        private async Task StartOriginalModeRecordingAsync()
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            // 检查是否有相似图片
            if (!HasSimilarImagesForOriginalMode())
            {
                MessageBox.Show(
                    "当前图片没有相似图片，无法录制原图模式。\n\n" +
                    "提示：相似图片识别规则为文件名模式匹配（如：image1.jpg, image2.jpg）",
                    "无法录制",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"🎬 [原图录制] 开始录制: ImageId={currentImageId}");

                // 使用ViewModel的命令
                _playbackViewModel.CurrentImageId = currentImageId;
                _playbackViewModel.CurrentMode = PlaybackMode.Original;
                
                await _playbackViewModel.StartRecordingCommand.ExecuteAsync(null);

                ShowStatus($"✅ 开始原图模式录制，请使用方向键切换图片");
                System.Diagnostics.Debug.WriteLine("📝 [原图录制] 已开始，等待图片切换...");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [原图录制] 启动失败: {ex.Message}");
                MessageBox.Show($"开始录制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 停止录制原图模式
        /// 参考Python版本：playback_controller.py 行154-166
        /// </summary>
        private async Task StopOriginalModeRecordingAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"⏹️ [原图录制] 停止录制");

                await _playbackViewModel.StopRecordingCommand.ExecuteAsync(null);

                ShowStatus("✅ 原图模式录制完成");
                System.Diagnostics.Debug.WriteLine("📝 [原图录制] 已保存时间数据到数据库");

                // 延迟200ms后自动启动播放（与Python版本一致）
                await Task.Delay(200);
                _ = StartOriginalModePlaybackAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [原图录制] 停止失败: {ex.Message}");
                MessageBox.Show($"停止录制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 录制时处理图片切换（在SwitchSimilarImage成功后调用）
        /// 参考Python版本：keytime.py 行170-214
        /// </summary>
        private async Task RecordOriginalImageSwitchAsync(int targetImageId)
        {
            if (!_playbackViewModel.IsRecording || _playbackViewModel.CurrentMode != PlaybackMode.Original)
                return;

            try
            {
                // 调用录制服务记录时间
                var recordingService = App.GetRequiredService<Services.PlaybackServiceFactory>()
                    .GetRecordingService(PlaybackMode.Original);

                await recordingService.RecordTimingAsync(targetImageId);

                System.Diagnostics.Debug.WriteLine($"📝 [原图录制] 记录切换: → ImageId={targetImageId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [原图录制] 记录失败: {ex.Message}");
            }
        }

        #endregion

        #region 原图模式播放

        /// <summary>
        /// 开始播放原图模式（按钮点击事件）
        /// 参考Python版本：playback_controller.py 行196-238
        /// </summary>
        private async Task StartOriginalModePlaybackAsync()
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            // 检查是否有相似图片
            if (!HasSimilarImagesForOriginalMode())
            {
                MessageBox.Show(
                    "当前图片没有相似图片，无法播放原图模式。",
                    "无法播放",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"▶️ [原图播放] 开始播放: ImageId={currentImageId}");

                // 同步播放次数设置
                var playbackService = App.GetRequiredService<Services.PlaybackServiceFactory>()
                    .GetPlaybackService(PlaybackMode.Original);
                playbackService.PlayCount = _playbackViewModel.PlayCount;

                // 订阅图片切换事件
                if (playbackService is OriginalPlaybackService originalPlayback)
                {
                    // 取消订阅旧的事件（防止重复订阅）
                    originalPlayback.SwitchImageRequested -= OnOriginalPlaybackSwitchImageRequested;
                    
                    // 订阅新事件
                    originalPlayback.SwitchImageRequested += OnOriginalPlaybackSwitchImageRequested;
                    
                    Logger.Info("✅ [原图播放] 已订阅SwitchImageRequested事件");
                }

                // 使用ViewModel的命令
                _playbackViewModel.CurrentImageId = currentImageId;
                _playbackViewModel.CurrentMode = PlaybackMode.Original;

                await _playbackViewModel.StartPlaybackCommand.ExecuteAsync(null);

                ShowStatus($"✅ 开始原图模式播放");
                System.Diagnostics.Debug.WriteLine("📺 [原图播放] 播放已启动");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [原图播放] 启动失败: {ex.Message}");
                MessageBox.Show($"播放失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 停止播放原图模式
        /// </summary>
        private async Task StopOriginalModePlaybackAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"⏹️ [原图播放] 停止播放");

                // 取消订阅事件
                var playbackService = App.GetRequiredService<Services.PlaybackServiceFactory>()
                    .GetPlaybackService(PlaybackMode.Original);
                if (playbackService is OriginalPlaybackService originalPlayback)
                {
                    originalPlayback.SwitchImageRequested -= OnOriginalPlaybackSwitchImageRequested;
                    System.Diagnostics.Debug.WriteLine("🔌 [原图播放] 已取消订阅事件");
                }

                await _playbackViewModel.StopPlaybackCommand.ExecuteAsync(null);
                
                // 恢复倒计时显示为默认状态
                Dispatcher.Invoke(() =>
                {
                    CountdownText.Text = "倒: --";
                    CountdownText.ToolTip = null;
                });

                ShowStatus("⏹ 原图模式播放已停止");
                System.Diagnostics.Debug.WriteLine("📺 [原图播放] 已停止");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [原图播放] 停止失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理原图播放服务的图片切换请求
        /// 参考Python版本：keytime.py 行1830-1900
        /// </summary>
        private void OnOriginalPlaybackSwitchImageRequested(object sender, SwitchImageEventArgs e)
        {
            Logger.Info("🎯 [事件触发] OnOriginalPlaybackSwitchImageRequested: ImageId={ImageId}, Path={Path}", 
                e.ImageId, e.ImagePath ?? "null");
            
            // 必须在UI线程上执行
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Logger.Debug("🔄 [原图播放] 开始切换图片: ImageId={ImageId}", e.ImageId);

                    // 🎯 更新当前图片ID（必须先更新，否则项目树选择逻辑会错乱）
                    currentImageId = e.ImageId;

                    // 如果提供了路径，直接加载
                    if (!string.IsNullOrEmpty(e.ImagePath))
                    {
                        LoadImage(e.ImagePath);
                    }
                    else
                    {
                        // 根据ImageId查找路径并加载
                        var dbContext = dbManager?.GetDbContext();
                        if (dbContext != null)
                        {
                            var mediaFile = dbContext.MediaFiles.FirstOrDefault(m => m.Id == e.ImageId);
                            if (mediaFile != null)
                            {
                                LoadImage(mediaFile.Path);
                            }
                        }
                    }

                    // 更新项目树选中状态
                    var treeItem = FindTreeItemById(e.ImageId);
                    if (treeItem != null)
                    {
                        // 使用SetCurrentValue避免只读问题
                        ProjectTree.SetCurrentValue(System.Windows.Controls.TreeView.SelectedItemProperty, treeItem);
                        // 滚动到可见区域
                        var treeViewItem = ProjectTree.ItemContainerGenerator
                            .ContainerFromItem(treeItem) as System.Windows.Controls.TreeViewItem;
                        treeViewItem?.BringIntoView();
                    }

                    // 强制更新投影窗口
                    if (projectionManager?.IsProjectionActive == true && imageProcessor?.CurrentImage != null)
                    {
                        projectionManager.UpdateProjectionImage(
                            imageProcessor.CurrentImage,
                            isColorEffectEnabled,
                            currentZoom,
                            false,
                            ImageColorChanger.Core.OriginalDisplayMode.Stretch
                        );
                    }

                    Logger.Info("✅ [原图播放] 图片切换完成: ImageId={ImageId}", e.ImageId);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "❌ [原图播放] 切换图片失败");
                }
            });
        }


        /// <summary>
        /// 根据ImageId查找树节点
        /// </summary>
        private ProjectTreeItem FindTreeItemById(int imageId)
        {
            foreach (var root in projectTreeItems)
            {
                var result = FindTreeItemByIdRecursive(root, imageId);
                if (result != null)
                    return result;
            }
            return null;
        }

        /// <summary>
        /// 递归查找树节点
        /// </summary>
        private ProjectTreeItem FindTreeItemByIdRecursive(ProjectTreeItem item, int imageId)
        {
            if (item.Type == TreeItemType.Image && item.Id == imageId)
                return item;

            foreach (var child in item.Children)
            {
                var result = FindTreeItemByIdRecursive(child, imageId);
                if (result != null)
                    return result;
            }

            return null;
        }

        #endregion

        #region 原图模式按钮集成

        /// <summary>
        /// 处理录制按钮点击（集成原图模式）
        /// 这个方法需要在MainWindow.xaml.cs中的按钮点击事件中调用
        /// </summary>
        internal async Task HandleRecordButtonClickAsync()
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            // 判断当前是否正在录制
            if (_playbackViewModel.IsRecording)
            {
                // 停止录制（根据当前模式）
                if (_playbackViewModel.CurrentMode == PlaybackMode.Original)
                {
                    await StopOriginalModeRecordingAsync();
                }
                else
                {
                    // 关键帧模式的停止逻辑（已有实现）
                    await _playbackViewModel.StopRecordingCommand.ExecuteAsync(null);
                }
                return;
            }

            // 检查是否应该使用原图模式
            if (ShouldUseOriginalMode())
            {
                // 原图模式录制
                await StartOriginalModeRecordingAsync();
            }
            else
            {
                // 关键帧模式录制（已有实现）
                _playbackViewModel.CurrentImageId = currentImageId;
                _playbackViewModel.CurrentMode = PlaybackMode.Keyframe;
                await _playbackViewModel.StartRecordingCommand.ExecuteAsync(null);
            }
        }

        /// <summary>
        /// 处理播放按钮点击（集成原图模式）
        /// 这个方法需要在MainWindow.xaml.cs中的按钮点击事件中调用
        /// </summary>
        internal async Task HandlePlayButtonClickAsync()
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            // 判断当前是否正在播放
            if (_playbackViewModel.IsPlaying)
            {
                // 停止播放（根据当前模式）
                if (_playbackViewModel.CurrentMode == PlaybackMode.Original)
                {
                    await StopOriginalModePlaybackAsync();
                }
                else
                {
                    // 关键帧模式的停止逻辑（已有实现）
                    await _playbackViewModel.StopPlaybackCommand.ExecuteAsync(null);
                }
                return;
            }

            // 检查是否应该使用原图模式
            if (ShouldUseOriginalMode())
            {
                // 原图模式播放
                await StartOriginalModePlaybackAsync();
            }
            else
            {
                // 关键帧模式播放（已有实现）
                _playbackViewModel.CurrentImageId = currentImageId;
                _playbackViewModel.CurrentMode = PlaybackMode.Keyframe;
                await _playbackViewModel.StartPlaybackCommand.ExecuteAsync(null);
            }
        }

        /// <summary>
        /// 处理暂停/继续按钮点击（原图模式和关键帧模式共用）
        /// </summary>
        internal async Task HandlePauseResumeButtonClickAsync()
        {
            if (!_playbackViewModel.IsPlaying)
                return;

            if (_playbackViewModel.IsPaused)
            {
                // 继续播放
                await _playbackViewModel.ResumePlaybackCommand.ExecuteAsync(null);
                ShowStatus("▶ 继续播放");
            }
            else
            {
                // 暂停播放
                await _playbackViewModel.PausePlaybackCommand.ExecuteAsync(null);
                ShowStatus("⏸ 已暂停");
            }
        }

        #endregion

        #region 原图模式图片切换集成

        /// <summary>
        /// 增强的图片切换方法（支持录制时记录时间，播放时修正时间）
        /// 在原有的SwitchSimilarImage成功后调用
        /// 参考Python版本：yuantu.py 行680-724
        /// </summary>
        private async Task OnSimilarImageSwitched(int fromImageId, int toImageId, bool isLoopCompleted)
        {
            // 🎯 如果正在播放原图模式，记录手动操作进行时间修正
            if (_playbackViewModel.IsPlaying && _playbackViewModel.CurrentMode == PlaybackMode.Original)
            {
                System.Diagnostics.Debug.WriteLine($"🔧 检测到播放时手动跳转: {fromImageId} -> {toImageId}");
                
                var playbackService = App.GetRequiredService<Services.PlaybackServiceFactory>()
                    .GetPlaybackService(PlaybackMode.Original);
                
                if (playbackService is OriginalPlaybackService originalPlayback)
                {
                    // 记录手动操作进行时间修正
                    await originalPlayback.RecordManualSwitchAsync(fromImageId, toImageId);
                    System.Diagnostics.Debug.WriteLine("✅ 播放时手动跳转已记录，将继续播放下一帧");
                }
            }
            
            // 如果正在录制原图模式，记录切换时间
            if (_playbackViewModel.IsRecording && _playbackViewModel.CurrentMode == PlaybackMode.Original)
            {
                await RecordOriginalImageSwitchAsync(toImageId);

                // 🎯 检测循环完成：如果在循环模式下回到第一张图，自动停止录制并开始播放
                if (isLoopCompleted)
                {
                    System.Diagnostics.Debug.WriteLine("🔄 检测到循环完成，自动停止录制并开始播放");
                    
                    // 停止录制
                    await _playbackViewModel.StopRecordingCommand.ExecuteAsync(null);
                    ShowStatus("✅ 循环录制完成，已停止录制");
                    
                    // 延迟一小段时间，确保录制完全停止
                    await Task.Delay(300);
                    
                    // 🎯 自动开始播放（调用完整的播放方法，确保事件订阅正确）
                    await StartOriginalModePlaybackAsync();
                    
                    System.Diagnostics.Debug.WriteLine("▶️ 循环录制完成，已自动开始播放");
                }
            }
        }

        #endregion

        #region 清除时间数据

        /// <summary>
        /// 清除原图模式时间数据
        /// </summary>
        private async Task ClearOriginalModeTimingDataAsync()
        {
            if (currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            var result = MessageBox.Show(
                "确定要清除当前图片的原图模式时间数据吗？\n此操作不可撤销。",
                "确认清除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var recordingService = App.GetRequiredService<Services.PlaybackServiceFactory>()
                        .GetRecordingService(PlaybackMode.Original);

                    await recordingService.ClearTimingDataAsync(currentImageId, PlaybackMode.Original);

                    ShowStatus("✅ 已清除原图模式时间数据");
                    System.Diagnostics.Debug.WriteLine($"🗑️ [原图] 已清除时间数据: ImageId={currentImageId}");

                    // 更新HasTimingData状态
                    await _playbackViewModel.UpdateTimingDataStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ [原图] 清除时间数据失败: {ex.Message}");
                    MessageBox.Show($"清除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}

