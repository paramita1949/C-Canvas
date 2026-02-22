using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 图像加载与清空核心流程
    /// </summary>
    public partial class MainWindow
    {
        private void LoadImage(string path)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => LoadImage(path));
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _keyframeManager?.StopScrollAnimation();
                _ = StopCompositePlaybackAsync();

                _imagePath = path;
                _currentZoom = 1.0;
                if (_imageProcessor != null)
                {
                    _imageProcessor.ZoomRatio = 1.0;
                }

                _imageProcessor.IsInverted = _isColorEffectEnabled;
                bool success = _imageProcessor.LoadImage(path);

                if (success)
                {
                    if (_currentImageId > 0)
                    {
                        bool shouldUseOriginal = _originalManager.ShouldUseOriginalMode(_currentImageId);

                        if (shouldUseOriginal && !_originalMode)
                        {
                            _originalMode = true;
                            _imageProcessor.OriginalMode = true;
                            BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144));
                            ShowStatus("已自动启用原图模式");
                        }

                        if (_originalMode)
                        {
                            _originalManager.FindSimilarImages(_currentImageId);
                            _ = TriggerSmartPreload();
                        }

                        SelectTreeItemById(_currentImageId);

                        if (_playbackViewModel != null)
                        {
                            var mode = _originalMode ? Database.Models.Enums.PlaybackMode.Original : Database.Models.Enums.PlaybackMode.Keyframe;
                            _ = _playbackViewModel.SetCurrentImageAsync(_currentImageId, mode);
                        }
                    }

                    UpdateProjection();
                    _keyframeManager?.UpdatePreviewLines();
                    UpdateFloatingCompositePlayButton();

                    if (_playbackViewModel != null && _currentImageId > 0)
                    {
                        _ = _playbackViewModel.SetCurrentImageAsync(
                            _currentImageId,
                            _originalMode ? Database.Models.Enums.PlaybackMode.Original : Database.Models.Enums.PlaybackMode.Keyframe);
                    }

                    sw.Stop();
                    ShowStatus($"已加载：{Path.GetFileName(path)}");
                }
                else
                {
                    throw new Exception("图片加载失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开图片: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStatus("加载失败");
            }
        }

        /// <summary>
        /// 清空图片显示
        /// </summary>
        public void ClearImageDisplay()
        {
            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await StopCompositePlaybackAsync();

                        if (_playbackViewModel != null && _playbackViewModel.IsPlaying)
                        {
                            await _playbackViewModel.StopPlaybackCommand.ExecuteAsync(null);
                        }

                        await StopOriginalModePlaybackAsync();

                        await Dispatcher.InvokeAsync(() =>
                        {
                            _keyframeManager?.StopScrollAnimation();
                            StopCompositeScrollAnimation();

                            CountdownText.Text = "倒: --";
                            _countdownService?.Stop();
                        });
                    }
                    catch (Exception)
                    {
                    }
                });

                _imagePath = null;
                _currentImageId = 0;
                _imageProcessor.ClearCurrentImage();
                _currentZoom = 1.0;

                KeyframePreviewLinesCanvas.Children.Clear();
                ScrollbarIndicatorsCanvas.Children.Clear();
                UpdateFloatingCompositePlayButton();

                ShowStatus("已清空图片显示");
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" 清空图片显示失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
#else
                _ = ex;
#endif
            }
        }
    }
}


