using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 投影生命周期
    /// </summary>
    public partial class MainWindow
    {
        private void BtnProjection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 如果是圣经模式，处理圣经投影
                if (BibleVerseScrollViewer.Visibility == Visibility.Visible && _isBibleMode)
                {
                    if (!_projectionManager.IsProjectionActive)
                    {
                        _projectionManager.ToggleProjection();
                        if (_projectionManager.IsProjectionActive)
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                OnProjectionStateChanged(true);
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                    else
                    {
                        _projectionManager.ToggleProjection();
                    }
                }
                // 如果是歌词模式，处理歌词投影
                else if (LyricsEditorPanel.Visibility == Visibility.Visible)
                {
                    if (!_projectionManager.IsProjectionActive)
                    {
                        _projectionManager.ToggleProjection();
                        if (_projectionManager.IsProjectionActive)
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                OnProjectionStateChanged(true);
                            }, System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                    else
                    {
                        _projectionManager.ToggleProjection();
                    }
                }
                // 如果是文本编辑器模式，先更新投影内容
                else if (TextEditorPanel.Visibility == Visibility.Visible && _currentTextProject != null)
                {
                    if (!_projectionManager.IsProjectionActive)
                    {
                        _projectionManager.ToggleProjection();
                        if (_projectionManager.IsProjectionActive)
                        {
                            UpdateProjectionFromCanvas();
                        }
                    }
                    else
                    {
                        _projectionManager.ToggleProjection();
                    }
                }
                else
                {
                    _projectionManager.ToggleProjection();
                    if (_projectionManager.IsProjectionActive)
                    {
                        UpdateProjection();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"投影操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 投影状态改变事件处理
        /// </summary>
        private void OnProjectionStateChanged(object sender, bool isActive)
        {
            Dispatcher.Invoke(() =>
            {
                if (isActive)
                {
                    SetProjectionButtonContent(true);
                    BtnProjection.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(144, 238, 144)); // 淡绿色
                    ShowStatus("投影已开启");

                    // 投影时自动停止原图/关键帧播放并重置倒计时
                    if (_playbackViewModel != null && _playbackViewModel.IsPlaying)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (_playbackViewModel.CurrentMode == PlaybackMode.Original)
                                {
                                    await StopOriginalModePlaybackAsync();
                                    System.Diagnostics.Debug.WriteLine("[投影] 已自动停止原图播放");
                                }
                                else
                                {
                                    await _playbackViewModel.StopPlaybackCommand.ExecuteAsync(null);
                                    System.Diagnostics.Debug.WriteLine("[投影] 已自动停止关键帧播放");
                                }

                                Dispatcher.Invoke(() =>
                                {
                                    _keyframeManager?.StopScrollAnimation();
                                    StopCompositeScrollAnimation();
                                    System.Diagnostics.Debug.WriteLine("[投影] 已停止滚动动画");
                                });

                                Dispatcher.Invoke(() =>
                                {
                                    CountdownText.Text = COUNTDOWN_DEFAULT_TEXT;
                                    CountdownText.ToolTip = null;
                                });
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($" [投影] 停止播放失败: {ex.Message}");
                            }
                        });
                    }

                    // 投影时自动停止合成播放并重置倒计时
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var compositeService = _playbackServiceFactory?.GetPlaybackService(PlaybackMode.Composite)
                                as Services.Implementations.CompositePlaybackService;

                            if (compositeService != null && compositeService.IsPlaying)
                            {
                                await compositeService.StopPlaybackAsync();
                                System.Diagnostics.Debug.WriteLine(" [投影] 已自动停止合成播放");

                                Dispatcher.Invoke(() =>
                                {
                                    SetCompositePlayButtonContent(false);
                                    BtnCompositePause.Visibility = Visibility.Collapsed;
                                    SetCompositePauseButtonContent(false);
                                    BtnCompositeSpeed.Visibility = Visibility.Collapsed;
                                    _keyframeManager?.StopScrollAnimation();
                                    StopCompositeScrollAnimation();
                                    CountdownText.Text = COUNTDOWN_DEFAULT_TEXT;
                                    CountdownText.ToolTip = null;
                                    _countdownService?.Stop();
                                    System.Diagnostics.Debug.WriteLine("[投影] 已停止合成播放的滚动动画和倒计时");
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($" [投影] 停止合成播放失败: {ex.Message}");
                        }
                    });

                    if (_preloadCacheManager != null && _projectionManager != null)
                    {
                        var (projWidth, projHeight) = _projectionManager.GetCurrentProjectionSize();
                        _preloadCacheManager.SetProjectionSize(projWidth, projHeight);
                    }

                    EnableGlobalHotKeys();

                    if (_videoPlayerManager != null && _videoPlayerManager.IsPlaying)
                    {
                        _projectionManager.ShowVideoProjection();
                    }
                    else if (!string.IsNullOrEmpty(_imagePath) && IsMediaFile(_imagePath))
                    {
                        var projectionVideoView = _projectionManager.GetProjectionVideoView();
                        if (projectionVideoView != null)
                        {
                            VideoContainer.Visibility = Visibility.Collapsed;
                            _projectionManager.ShowVideoProjection();
                            string fileName = Path.GetFileName(_imagePath);
                            _projectionManager.SetProjectionMediaFileName(fileName, false);
                            _pendingProjectionVideoPath = _imagePath;
                            ShowStatus($"准备投影播放: {fileName}");
                        }
                    }
                }
                else
                {
                    SetProjectionButtonContent(false);
                    BtnProjection.Background = System.Windows.Media.Brushes.Transparent;
                    DisableGlobalHotKeys();
                    _projectionNdiOutputManager?.PushTransparentIdleFrame();
                    StopVideoNdiTimer();

                    if (_projectionTimeoutTimer != null)
                    {
                        _projectionTimeoutTimer.Stop();
                        _projectionTimeoutTimer = null;
                    }

                    if (_playbackViewModel != null && _playbackViewModel.IsPlaying)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (_playbackViewModel.CurrentMode == PlaybackMode.Original)
                                {
                                    await StopOriginalModePlaybackAsync();
                                }
                                else
                                {
                                    await _playbackViewModel.StopPlaybackCommand.ExecuteAsync(null);
                                }

                                Dispatcher.Invoke(() =>
                                {
                                    _keyframeManager?.StopScrollAnimation();
                                    StopCompositeScrollAnimation();
                                });

                                Dispatcher.Invoke(() =>
                                {
                                    CountdownText.Text = COUNTDOWN_DEFAULT_TEXT;
                                    CountdownText.ToolTip = null;
                                });
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($" [结束投影] 停止播放失败: {ex.Message}");
                            }
                        });
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var compositeService = _playbackServiceFactory?.GetPlaybackService(PlaybackMode.Composite)
                                as Services.Implementations.CompositePlaybackService;

                            if (compositeService != null && compositeService.IsPlaying)
                            {
                                await compositeService.StopPlaybackAsync();
                                Dispatcher.Invoke(() =>
                                {
                                    SetCompositePlayButtonContent(false);
                                    BtnCompositePause.Visibility = Visibility.Collapsed;
                                    SetCompositePauseButtonContent(false);
                                    BtnCompositeSpeed.Visibility = Visibility.Collapsed;
                                    _keyframeManager?.StopScrollAnimation();
                                    StopCompositeScrollAnimation();
                                    CountdownText.Text = COUNTDOWN_DEFAULT_TEXT;
                                    CountdownText.ToolTip = null;
                                    _countdownService?.Stop();
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($" [结束投影] 停止合成播放失败: {ex.Message}");
                        }
                    });

                    if (_videoPlayerManager != null && _videoPlayerManager.IsPlaying)
                    {
                        _videoPlayerManager.Stop();
                        var mainVideoView = _mediaModuleController?.MainVideoView;
                        if (mainVideoView != null)
                        {
                            _videoPlayerManager.SetMainVideoView(mainVideoView);
                        }
                        MediaPlayerPanel.Visibility = Visibility.Collapsed;
                        VideoContainer.Visibility = Visibility.Collapsed;
                        ShowStatus("视频播放已停止");
                    }

                    _videoPlayerManager?.ResetProjectionMode();

                    // 无论当前是否播放，都确保投影结束后主屏 VideoView 与 MediaPlayer 重新绑定
                    if (_videoPlayerManager != null)
                    {
                        var mainVideoView = _mediaModuleController?.MainVideoView;
                        if (mainVideoView != null)
                        {
                            _videoPlayerManager.SetMainVideoView(mainVideoView);
                        }
                    }
                }

                if (_isBibleMode)
                {
                    OnBibleProjectionStateChanged(isActive);
                }
                else if (_isLyricsMode)
                {
                    OnProjectionStateChanged(isActive);
                }
                else if (isActive && TextEditorPanel.Visibility == Visibility.Visible && _currentTextProject != null)
                {
                    UpdateProjectionFromCanvas();
                }
                else if (isActive)
                {
                    // 非歌词/非圣经模式：开启投影后立即推送当前画面（含 NDI 首帧）。
                    UpdateProjection();
                }
            });
        }
    }
}



