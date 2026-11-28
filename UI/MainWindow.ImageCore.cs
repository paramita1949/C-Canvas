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
            TryLoadImage(path);
        }

        private bool TryLoadImage(string path, bool publishProjection = true, bool showError = true, bool refreshStructuredScore = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(() => TryLoadImage(path, publishProjection, showError, refreshStructuredScore));
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                bool prepared = TryGetPreparedStructuredView(path, out var structuredView);
                var structuredDocument = prepared ? structuredView.Document : Services.OnlineScores.StructuredScoreDocument.ReadFromFile(path);
                ApplyStructuredScoreColorDefault(structuredDocument != null);
                if (!prepared && structuredDocument != null)
                {
                    using var trace = Utils.StructuredScorePerfTrace.Start("sync-open-fallback");
                    // 兼容同步入口也按相同首选版式预备，不让底层再次读取文件切回旧版式。
                    SelectStructuredScoreOpeningLayout(structuredDocument, System.Threading.CancellationToken.None);
                    trace?.Mark("opening-policy");
                    structuredView = GetStructuredViewAsync(structuredDocument).GetAwaiter().GetResult();
                    trace?.Mark("blocking-first-frame");
                    structuredDocument = structuredView.Document;
                    _structuredPreparedView = structuredView; _structuredPreparedAnchor = path;
                    _structuredPreparedFileKey = Core.ImageFileCacheKey.GetRawKey(path);
                    prepared = true;
                }
                _keyframeManager?.StopScrollAnimation();
                if (refreshStructuredScore) _ = StopCompositePlaybackAsync();

                _imagePath = path;

                bool shouldUseOriginalForCurrentImage = _currentImageId > 0 &&
                    _originalManager != null &&
                    _originalManager.ShouldUseOriginalMode(_currentImageId);
                // 原图是分页的加载策略，不是新图发布后的第二次显示操作。
                bool wasForcedOriginal = _structuredScoreForcedOriginal;
                ApplyStructuredScoreViewingMode(path, structuredDocument, shouldUseOriginalForCurrentImage);
                if (wasForcedOriginal && !_originalMode) ResetNormalImageZoomState();
                bool preserveZoom = _originalMode || shouldUseOriginalForCurrentImage || UsesStructuredScoreOriginalMode(structuredDocument);

                if (!preserveZoom)
                {
                    ResetNormalImageZoomState();
                }

                _imageProcessor.CurrentStructuredScore = structuredDocument;
                if (prepared) _imageProcessor.SetPreparedColorMode(_isColorEffectEnabled);
                else _imageProcessor.IsInverted = _isColorEffectEnabled;
                if (prepared)
                {
                    bool useOriginal = UsesStructuredScoreOriginalMode(structuredDocument) ||
                        shouldUseOriginalForCurrentImage || (_originalMode && !_structuredScoreForcedOriginal);
                    _imageProcessor.SetPreparedOriginalMode(useOriginal);
                }
                bool success = prepared
                    ? _imageProcessor.LoadPreparedImage(path, structuredView.Frames[Math.Min(structuredDocument.Page, structuredView.Frames.Count - 1)])
                    : _imageProcessor.LoadImage(path);

                if (success)
                {
                    _imageProcessor.CurrentStructuredScore = structuredDocument;
                    if (_currentImageId > 0)
                    {
                        bool shouldUseOriginal = _originalManager?.ShouldUseOriginalMode(_currentImageId) == true;

                        if (shouldUseOriginal && !_originalMode)
                        {
                            _originalMode = true;
                            _imageProcessor.OriginalMode = true;
                            BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144));
                            ShowStatus("已自动启用原图模式");
                        }

                        if (_originalMode)
                        {
                            _originalManager?.FindSimilarImages(_currentImageId);
                            _ = TriggerSmartPreload();
                        }

                        SelectTreeItemById(_currentImageId);

                        if (_playbackViewModel != null)
                        {
                            var mode = _originalMode ? Database.Models.Enums.PlaybackMode.Original : Database.Models.Enums.PlaybackMode.Keyframe;
                            _ = _playbackViewModel.SetCurrentImageAsync(_currentImageId, mode);
                        }
                    }

                    if (_originalMode && !_structuredScoreForcedOriginal)
                    {
                        _currentZoom = _originalModeZoomRatio;
                        SetZoom(_currentZoom);
                    }

                    UpdateImageZoomControlsVisibility();
                    if (publishProjection) UpdateProjection();
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
                    if (refreshStructuredScore) RefreshStructuredScoreBar(path);
                    return true;
                }
                else
                {
                    throw new Exception("图片加载失败");
                }
            }
            catch (Exception ex)
            {
                if (showError) MessageBox.Show($"无法打开图片: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStatus("加载失败");
                return false;
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

                            ResetCountdownDisplay();
                            _countdownService?.Stop();
                        });
                    }
                    catch (Exception)
                    {
                    }
                });

                _imagePath = null;
                ApplyStructuredScoreColorDefault(false);
                ApplyStructuredScoreViewingMode(null, null, false);
                RefreshStructuredScoreBar(null);
                _currentImageId = 0;
                _imageProcessor.ClearCurrentImage();
                _currentZoom = 1.0;
                UpdateImageZoomControlsVisibility();

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


