using System;
using System.ComponentModel;
using ImageColorChanger.Managers;
using ImageColorChanger.Services.Ndi.Audio;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 服务接线与退订
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 窗口关闭事件：清理服务事件订阅和视频资源
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                if (_countdownService != null && _countdownUpdatedHandler != null)
                {
                    _countdownService.CountdownUpdated -= _countdownUpdatedHandler;
                    _countdownUpdatedHandler = null;
                }

                if (_playbackViewModel != null && _playbackPropertyChangedHandler != null)
                {
                    _playbackViewModel.PropertyChanged -= _playbackPropertyChangedHandler;
                    _playbackPropertyChangedHandler = null;
                }

                if (_keyframePlaybackService != null && _jumpToKeyframeRequestedHandler != null)
                {
                    _keyframePlaybackService.JumpToKeyframeRequested -= _jumpToKeyframeRequestedHandler;
                    _jumpToKeyframeRequestedHandler = null;
                    _keyframePlaybackService = null;
                }

                if (_playbackViewModel is IDisposable disposablePlaybackViewModel)
                {
                    disposablePlaybackViewModel.Dispose();
                }

                if (_projectionManager != null)
                {
                    _projectionManager.ProjectionStateChanged -= OnProjectionStateChanged;
                    _projectionManager.ProjectionVideoViewLoaded -= OnProjectionVideoViewLoaded;
                }

                _ndiRouter?.StopAll();
                _mainWindowServices?.GetRequired<INdiAudioCaptureService>()?.Stop();
                _ndiTransportCoordinator?.StopAll();
                StopVideoNdiTimer();
                StopNdiDiscoveryTimer();

                // 清理视频背景管理器
                _videoBackgroundManager?.Dispose();
                _textEditorProjectionRenderStateService?.ClearCache();
                _textEditorSaveOrchestrator = null;
                _textEditorProjectionRenderStateService = null;
                _textEditorRenderSafetyService = null;
            }
            catch (Exception
#if DEBUG
                ex
#endif
            )
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [资源清理] 清理视频资源失败: {ex.Message}");
#endif
            }
        }

        /// <summary>
        /// 初始化新的PlaybackControlViewModel
        /// </summary>
        private void InitializePlaybackViewModel()
        {
            try
            {
                _playbackViewModel = _mainWindowServices.GetRequired<ViewModels.PlaybackControlViewModel>();
                _playbackServiceFactory = _mainWindowServices.GetRequired<Services.PlaybackServiceFactory>();
                _countdownService = _mainWindowServices.GetRequired<Services.Interfaces.ICountdownService>();
                _timingRepository = _mainWindowServices.GetRequired<Repositories.Interfaces.ITimingRepository>();
                _originalModeRepository = _mainWindowServices.GetRequired<Repositories.Interfaces.IOriginalModeRepository>();
                _compositeScriptRepository = _mainWindowServices.GetRequired<Repositories.Interfaces.ICompositeScriptRepository>();
                _memoryCache = _mainWindowServices.GetRequired<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                _mediaFileRepository = _mainWindowServices.GetRequired<Repositories.Interfaces.IMediaFileRepository>();

                _countdownUpdatedHandler = (s, e) =>
                {
                    Dispatcher.Invoke(() => { SetCountdownDisplay(e.ElapsedTime, e.RemainingTime); });
                };
                _countdownService.CountdownUpdated += _countdownUpdatedHandler;

                _playbackPropertyChangedHandler = (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        switch (e.PropertyName)
                        {
                            case "IsRecording":
                                SetRecordButtonContent(_playbackViewModel.IsRecording);
                                break;
                            case "IsPlaying":
                                SetPlayButtonContent(_playbackViewModel.IsPlaying);
                                BtnPauseResume.IsEnabled = _playbackViewModel.IsPlaying;
                                BtnPlay.Background = _playbackViewModel.IsPlaying
                                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(BUTTON_ACTIVE_COLOR_R, BUTTON_ACTIVE_COLOR_G, BUTTON_ACTIVE_COLOR_B))
                                    : System.Windows.SystemColors.ControlBrush;
                                if (!_playbackViewModel.IsPlaying)
                                {
                                    CountdownText.Text = COUNTDOWN_DEFAULT_TEXT;
                                    _keyframeManager?.StopScrollAnimation();
                                    StopCompositeScrollAnimation();
                                }
                                break;
                            case "IsPaused":
                                SetPauseResumeButtonContent(_playbackViewModel.IsPaused);
                                break;
                            case "PlayCount":
                                string text = _playbackViewModel.PlayCount == -1 ? "∞" : _playbackViewModel.PlayCount.ToString();
                                SetPlayCountButtonContent(text);
                                break;
                            case "HasTimingData":
                                BtnScript.Background = _playbackViewModel.HasTimingData
                                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(BUTTON_ACTIVE_COLOR_R, BUTTON_ACTIVE_COLOR_G, BUTTON_ACTIVE_COLOR_B))
                                    : System.Windows.SystemColors.ControlBrush;
                                break;
                        }
                    });
                };
                _playbackViewModel.PropertyChanged += _playbackPropertyChangedHandler;

                Dispatcher.Invoke(() =>
                {
                    SetRecordButtonContent(_playbackViewModel.IsRecording);
                    SetPlayButtonContent(_playbackViewModel.IsPlaying);
                    SetPauseResumeButtonContent(_playbackViewModel.IsPaused);
                    BtnPauseResume.IsEnabled = _playbackViewModel.IsPlaying;
                    string playCountText = _playbackViewModel.PlayCount == -1 ? "∞" : _playbackViewModel.PlayCount.ToString();
                    SetPlayCountButtonContent(playCountText);
                });

                var keyframePlayback = _playbackServiceFactory.GetPlaybackService(Database.Models.Enums.PlaybackMode.Keyframe);
                if (keyframePlayback is Services.Implementations.KeyframePlaybackService kfService)
                {
                    _keyframePlaybackService = kfService;
                    _jumpToKeyframeRequestedHandler = async (s, e) =>
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (_keyframeManager != null)
                            {
                                if (e.UseDirectJump)
                                {
                                    // 直接跳转前先停掉可能残留的滚动动画，避免继续套用缓动函数
                                    _keyframeManager?.StopScrollAnimation();
                                    StopCompositeScrollAnimation();
                                    ImageScrollViewer.ScrollToVerticalOffset(e.Position * ImageScrollViewer.ScrollableHeight);
                                    if (IsProjectionEnabled)
                                    {
                                        UpdateProjection();
                                    }
                                }
                                else
                                {
                                    _keyframeManager.SmoothScrollTo(e.Position);
                                }

                                var keyframes = _keyframeManager.GetKeyframesFromCache(_currentImageId);
                                if (keyframes != null)
                                {
                                    for (int i = 0; i < keyframes.Count; i++)
                                    {
                                        if (keyframes[i].Id == e.KeyframeId)
                                        {
                                            _keyframeManager.UpdateKeyframeIndex(i);
                                            break;
                                        }
                                    }
                                }

                                _keyframeManager?.UpdatePreviewLines();
                            }
                        });
                    };
                    _keyframePlaybackService.JumpToKeyframeRequested -= _jumpToKeyframeRequestedHandler;
                    _keyframePlaybackService.JumpToKeyframeRequested += _jumpToKeyframeRequestedHandler;
                }
            }
            catch (Exception)
            {
            }
        }
    }
}

