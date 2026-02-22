using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 播放控制入口（录制/播放/脚本）
    /// </summary>
    public partial class MainWindow
    {
        private async void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackViewModel == null) return;

            _playbackViewModel.CurrentImageId = _currentImageId;
            _playbackViewModel.CurrentMode = _originalMode
                ? Database.Models.Enums.PlaybackMode.Original
                : Database.Models.Enums.PlaybackMode.Keyframe;

            if (!_playbackViewModel.IsRecording)
            {
                if (_originalMode && _originalManager != null)
                {
                    if (_originalManager.HasSimilarImages() || _originalManager.FindSimilarImages(_currentImageId))
                    {
                        var firstImageResult = _originalManager.GetFirstSimilarImage();
                        if (firstImageResult.success && firstImageResult.firstImageId.HasValue)
                        {
                            if (_currentImageId != firstImageResult.firstImageId.Value)
                            {
                                _currentImageId = firstImageResult.firstImageId.Value;
                                LoadImage(firstImageResult.firstImagePath);
                                await Task.Delay(UI_UPDATE_DELAY_MILLISECONDS);
                                ShowStatus("已跳转到第一张相似图片");
                            }
                        }
                    }
                }
                else if (!_originalMode && _keyframeManager != null)
                {
                    var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                    if (keyframes != null && keyframes.Count > 0)
                    {
                        if (_keyframeManager.CurrentKeyframeIndex != 0)
                        {
                            _keyframeManager.UpdateKeyframeIndex(0);
                            var firstKeyframe = keyframes[0];
                            var targetOffset = firstKeyframe.Position * ImageScrollViewer.ScrollableHeight;
                            ImageScrollViewer.ScrollToVerticalOffset(targetOffset);

                            if (IsProjectionEnabled)
                            {
                                UpdateProjection();
                            }

                            _ = _keyframeManager.UpdateKeyframeIndicatorsAsync();
                            ShowStatus($"关键帧 1/{keyframes.Count}");
                        }
                    }
                }
            }

            bool wasRecording = _playbackViewModel.IsRecording;
            await _playbackViewModel.ToggleRecordingCommand.ExecuteAsync(null);

            if (wasRecording && !_playbackViewModel.IsRecording)
            {
                await Task.Delay(200);

                if (!_originalMode && _keyframeManager != null)
                {
                    bool isCompositeEnabled = await _keyframeManager.GetCompositePlaybackEnabledAsync(_currentImageId);
                    if (isCompositeEnabled)
                    {
                        BtnFloatingCompositePlay.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    else
                    {
                        _playbackViewModel.CurrentImageId = _currentImageId;
                        _playbackViewModel.CurrentMode = Database.Models.Enums.PlaybackMode.Keyframe;
                        await _playbackViewModel.TogglePlaybackCommand.ExecuteAsync(null);
                    }
                }
            }
        }

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackViewModel == null) return;

            if (_originalMode)
            {
                if (_playbackViewModel.IsPlaying)
                {
                    await StopOriginalModePlaybackAsync();
                }
                else
                {
                    await StartOriginalModePlaybackAsync();
                }
            }
            else
            {
                _playbackViewModel.CurrentImageId = _currentImageId;
                _playbackViewModel.CurrentMode = Database.Models.Enums.PlaybackMode.Keyframe;
                await _playbackViewModel.TogglePlaybackCommand.ExecuteAsync(null);
            }
        }

        private async void BtnPauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackViewModel == null) return;
            await _playbackViewModel.TogglePauseCommand.ExecuteAsync(null);
        }

        /// <summary>
        /// 脚本按钮点击事件：显示和编辑脚本（支持关键帧和原图模式）
        /// </summary>
        private async void BtnScript_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImageId == 0)
            {
                ShowStatus("请先选择一张图片");
                return;
            }

            try
            {
                if (ShouldUseOriginalMode())
                {
                    await OpenOriginalModeScriptEditor();
                }
                else
                {
                    await OpenKeyframeModeScriptEditor();
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"显示脚本失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 打开关键帧模式脚本编辑器
        /// </summary>
        private async Task OpenKeyframeModeScriptEditor()
        {
            if (_timingRepository == null)
            {
                ShowStatus("脚本服务未初始化");
                return;
            }

            var timings = await _timingRepository.GetTimingSequenceAsync(_currentImageId);
            var timingList = timings?.ToList() ?? new System.Collections.Generic.List<Database.Models.DTOs.TimingSequenceDto>();

            var scriptWindow = _mainWindowServices
                .GetRequired<Composition.ScriptEditWindowFactory>()
                .CreateForKeyframe(_currentImageId, timingList);
            scriptWindow.Owner = this;

            if (scriptWindow.ShowDialog() == true)
            {
                ShowStatus("脚本已更新");
            }
        }

        /// <summary>
        /// 打开原图模式脚本编辑器
        /// </summary>
        private async Task OpenOriginalModeScriptEditor()
        {
            if (_originalModeRepository == null)
            {
                ShowStatus("原图脚本服务未初始化");
                return;
            }

            var baseImageId = await _originalModeRepository.FindBaseImageIdBySimilarImageAsync(_currentImageId);
            if (!baseImageId.HasValue)
            {
                baseImageId = _currentImageId;
            }

            var timings = await _originalModeRepository.GetOriginalTimingSequenceAsync(baseImageId.Value);
            if (timings == null || timings.Count == 0)
            {
                MessageBox.Show("当前图片没有录制的原图模式时间数据", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var scriptWindow = _mainWindowServices
                .GetRequired<Composition.ScriptEditWindowFactory>()
                .CreateForOriginal(baseImageId.Value, timings);
            scriptWindow.Owner = this;

            if (scriptWindow.ShowDialog() == true)
            {
                ShowStatus("原图模式脚本已更新");
            }
        }
    }
}


