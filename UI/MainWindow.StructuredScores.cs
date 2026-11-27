using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Services.OnlineScores;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private StructuredScoreDocument _structuredScore;
        private IReadOnlyList<string> _structuredScorePages;
        private string _structuredScoreAnchor;
        private long _structuredScoreVersion;
        private long _structuredOpenVersion;
        private CancellationTokenSource _structuredOpenCancellation;
        private bool _structuredScoreForcedOriginal;
        private bool _originalModeBeforeStructuredScore;
        private string _structuredScoreOriginalPath;
        private Core.OriginalDisplayMode _originalDisplayModeBeforeStructuredScore;
        private Core.OriginalDisplayMode? _structuredScoreOriginalDisplayMode;
        private double _zoomBeforeStructuredScore;
        private sealed class StructuredScoreView
        {
            public StructuredScoreDocument Document { get; init; }
            public IReadOnlyList<string> Paths { get; init; }
            public IReadOnlyList<Core.ImageProcessor.PreparedImage> Frames { get; init; }
            public bool CanCombine { get; init; }
        }
        private Dictionary<string, Task<StructuredScoreView>> _structuredViews = new();
        private StructuredScoreView _structuredCurrentView, _structuredPreparedView;
        private string _structuredPreparedAnchor, _structuredPreparedFileKey;
        private long _structuredSwitchVersion;
        private CancellationTokenSource _structuredWarmCancellation;
        private StructuredScoreLayout? _structuredButtonLayout;
        private bool? _structuredButtonCanCombine;
        private SemaphoreSlim _structuredWriteGate = new(1, 1);
        private bool _structuredShowingImage, _structuredImageLoading;
        private CancellationTokenSource _structuredImageCancellation;
        private Core.ImageProcessor.PreparedImage _structuredImageFrame;
        private string _structuredImageFrameAnchor;
        private bool SelectionColorEffectEnabled => _isColorEffectEnabled;

        private bool CanShowStructuredScoreImage()
        {
            if (_structuredScore?.ShigebenId is not > 0 || string.IsNullOrEmpty(_structuredScoreAnchor) ||
                _structuredScoreAnchor != _imagePath) return false;
            if (_onlineScoreStorageSettings == null && _mainWindowServices == null) return false;
            // 来源由在线存储映射判断，不能仅凭文件里的外部ID把自定义导入也当作在线库。
            try { return GetOnlineScoreStorageSettings().IsLibraryDirectory(Path.GetDirectoryName(_structuredScoreAnchor)); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("在线歌谱来源未能确认：" + ex.Message);
                return false;
            }
        }

        private void UpdateStructuredScoreActions()
        {
            bool available = CanShowStructuredScoreImage();
            if (StructuredScoreImageButton != null)
            {
                StructuredScoreImageButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
                StructuredScoreImageButton.IsEnabled = available && !_structuredImageLoading;
                StructuredScoreImageButton.Content = _structuredImageLoading ? "获取图片…" : "显示图片";
                System.Windows.Controls.Primitives.Selector.SetIsSelected(StructuredScoreImageButton, _structuredShowingImage);
            }
            if (StructuredScorePreviousButton != null) StructuredScorePreviousButton.IsEnabled = !_structuredShowingImage;
            if (StructuredScoreNextButton != null) StructuredScoreNextButton.IsEnabled = !_structuredShowingImage;
        }

        private void CancelStructuredScoreImageRequest()
        {
            _structuredImageCancellation?.Cancel();
            _structuredImageLoading = false;
        }

        private void ReleaseStructuredScoreImageFrame()
        {
            // 只有主显示已换成另一帧才释放，避免缩放/投影仍在读取当前位图。
            if (_structuredImageFrame == null || ReferenceEquals(_imageProcessor?.CurrentImage, _structuredImageFrame.Bitmap)) return;
            _structuredImageFrame.Bitmap.Dispose();
            _structuredImageFrame = null; _structuredImageFrameAnchor = null;
        }

        private void StructuredScoreLyrics_Click(object sender, RoutedEventArgs e)
        {
            if (_structuredScore == null || _currentImageId <= 0 || _isLyricsMode) return;
            CancelStructuredScoreImageRequest(); ++_structuredSwitchVersion;
            UpdateStructuredScoreActions();
            EnterImageLyricsByShortcut(_currentImageId);
        }

        private async void StructuredScoreImage_Click(object sender, RoutedEventArgs e)
        {
            await ShowOnlineStructuredScoreImageAsync();
        }

        private async Task<bool> ShowOnlineStructuredScoreImageAsync()
        {
            if (!CanShowStructuredScoreImage() || _structuredImageLoading || !CanChangeStructuredScore()) return false;
            if (_structuredShowingImage)
                return ShowStructuredScorePage(_structuredCurrentView, _structuredScore.Page, sourceChanged: true);
            CancelStructuredScoreImageRequest();
            var cancellation = new CancellationTokenSource(); _structuredImageCancellation = cancellation;
            string anchor = _structuredScoreAnchor;
            int id = _structuredScore.ShigebenId.Value;
            long version = _structuredScoreVersion, request = ++_structuredSwitchVersion;
            Core.ImageProcessor.PreparedImage preparedFrame = null;
            bool IsCurrent() => !cancellation.IsCancellationRequested && !_onlineScoreClosed && !_isLyricsMode &&
                version == _structuredScoreVersion && request == _structuredSwitchVersion && _imagePath == anchor &&
                _structuredScoreAnchor == anchor && _structuredScore?.ShigebenId == id;
            _structuredImageLoading = true; UpdateStructuredScoreActions();
            try
            {
                ShowStatus("正在获取诗歌本图片…");
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CanvasCast", "OnlineScoreImages");
                string path = await OnlineScoreLibrary.GetOrDownloadOriginalImageAsync(id, cache,
                    (directory, ct) => GetOnlineScoreClient().DownloadImageAsync(id, directory, ct), cancellation.Token);
                if (!IsCurrent()) return false;
                // 图像模式复用普通图片的变色/缩放；不再拿相同谱码通过本地解释器生成兜底图。
                bool invert = _isColorEffectEnabled; var color = _imageProcessor.GetTargetColor();
                var frame = _structuredImageFrame;
                if (frame == null || _structuredImageFrameAnchor != anchor || frame.RasterPath != path ||
                    frame.Inverted != invert || frame.TargetColor != color)
                    frame = await Task.Run(() => _imageProcessor.PrepareStructuredPage(path, invert, color), cancellation.Token);
                preparedFrame = frame;
                if (!IsCurrent())
                {
                    if (!ReferenceEquals(frame, _structuredImageFrame)) frame.Bitmap.Dispose();
                    return false;
                }
                await StopCompositePlaybackAsync();
                if (!IsCurrent())
                {
                    if (!ReferenceEquals(frame, _structuredImageFrame)) frame.Bitmap.Dispose();
                    return false;
                }
                if (!ReferenceEquals(frame, _structuredImageFrame)) ReleaseStructuredScoreImageFrame();
                bool shown = ShowStructuredScoreImage(frame);
                if (shown) { _structuredImageFrame = frame; _structuredImageFrameAnchor = anchor; }
                else if (!ReferenceEquals(frame, _structuredImageFrame)) frame.Bitmap.Dispose();
                return shown;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
            catch (Exception ex)
            {
                if (IsCurrent()) ShowStatus("图片兜底未切换，原谱码和画面保留：" + ex.Message);
                return false;
            }
            finally
            {
                if (preparedFrame != null && !ReferenceEquals(preparedFrame, _structuredImageFrame) &&
                    !ReferenceEquals(preparedFrame.Bitmap, _imageProcessor?.CurrentImage)) preparedFrame.Bitmap.Dispose();
                if (_structuredImageCancellation == cancellation)
                {
                    _structuredImageCancellation = null; _structuredImageLoading = false;
                    UpdateStructuredScoreActions();
                }
                cancellation.Dispose();
            }
        }

        private bool ShowStructuredScoreImage(Core.ImageProcessor.PreparedImage frame)
        {
            if (frame == null || !CanShowStructuredScoreImage() || _isLyricsMode) return false;
            var restore = _imageProcessor.CaptureImageRestoreAction();
            var state = (_structuredScoreForcedOriginal, _structuredScoreOriginalPath, _originalModeBeforeStructuredScore,
                _originalDisplayModeBeforeStructuredScore, _zoomBeforeStructuredScore, _originalMode, _originalDisplayMode, _currentZoom);
            try
            {
                var originalView = CloneStructuredScore(_structuredScore); originalView.Layout = StructuredScoreLayout.Paginated;
                ApplyStructuredScoreViewingMode(_structuredScoreAnchor, originalView, false);
                _currentZoom = 1.0;
                if (ImageScaleTransform != null) ImageScaleTransform.ScaleX = ImageScaleTransform.ScaleY = 1.0;
                _imageProcessor.CurrentStructuredScore = null;
                _imageProcessor.SetPreparedColorMode(_isColorEffectEnabled);
                if (!_imageProcessor.LoadPreparedImage(_structuredScoreAnchor, frame)) throw new IOException("图片显示失败。");
            }
            catch
            {
                restore();
                (_structuredScoreForcedOriginal, _structuredScoreOriginalPath, _originalModeBeforeStructuredScore,
                    _originalDisplayModeBeforeStructuredScore, _zoomBeforeStructuredScore, _originalMode, _originalDisplayMode, _currentZoom) = state;
                if (ImageScaleTransform != null) ImageScaleTransform.ScaleX = ImageScaleTransform.ScaleY = _currentZoom;
                ApplyOriginalModeUiState(_originalMode);
                throw;
            }
            _structuredShowingImage = true;
            _structuredButtonLayout = null;
            UpdateStructuredScoreButtons(_structuredScore.Layout, _structuredCurrentView?.CanCombine == true);
            StructuredScorePageText.Text = "图片";
            ApplyStructuredScoreColorDefault(false);
            UpdateImageZoomControlsVisibility();
            _projectionManager?.ClearProjectionCache(); UpdateProjection();
            ShowStatus("已显示诗歌本图片，谱码保留；点击分页、长页或合页可返回。");
            return true;
        }

        private void StructuredScorePin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton pin && _configManager != null)
                _configManager.StructuredScoreToolbarPinned = pin.IsChecked == true;
        }

        private void ApplyStructuredScoreColorDefault(bool structured)
        {
            if (BtnColorEffect != null)
            {
                BtnColorEffect.ToolTip = structured ? "选择歌谱文字颜色（直接绘制）" : "切换颜色效果";
                if (BtnColorEffect.Content is StackPanel colorContent)
                    foreach (var label in colorContent.Children.OfType<TextBlock>()) label.Text = structured ? "字色" : "变色";
            }
        }

        private bool StructuredFrameMatchesColor(Core.ImageProcessor.PreparedImage frame) =>
            frame.NativeTextRendered && frame.Inverted == _isColorEffectEnabled &&
            frame.TargetColor == ResolveStructuredTextColor(_structuredPreparedView?.Document ?? _structuredScore);

        private SkiaSharp.SKColor ResolveStructuredTextColor(StructuredScoreDocument document) =>
            document?.TextColor != null ? StructuredScoreDocument.ParseTextColor(document.TextColor)
                : _imageProcessor.GetTargetColor();

        private string StructuredViewKey(StructuredScoreDocument document) =>
            document.CacheKey() + "|" + _isColorEffectEnabled + "|" + ResolveStructuredTextColor(document);

        private string StructuredEditorDefaultTextColor()
        {
            var color = _imageProcessor.GetTargetColor();
            return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        }

        private async Task RefreshStructuredScoreColorAsync()
        {
            if (_structuredScore == null || _structuredScoreAnchor != _imagePath || _structuredShowingImage) return;
            long request = ++_structuredSwitchVersion, version = _structuredScoreVersion;
            var document = _structuredScore;
            try
            {
                var view = await GetStructuredViewAsync(document);
                if (request == _structuredSwitchVersion && version == _structuredScoreVersion)
                    ShowStructuredScorePage(view, document.Page);
            }
            catch (Exception ex) { ShowStatus("歌谱配色未更新，原文保留：" + ex.Message); }
        }

        private async Task SetStructuredScoreTextColorAsync(string hex)
        {
            if (_structuredScore == null) return;
            var document = CloneStructuredScore(_structuredScore); document.TextColor = hex;
            await ApplyStructuredScoreAsync(document);
        }

        private MenuItem BuildStructuredScoreTextColorMenu()
        {
            var menu = new MenuItem { Header = "代码歌谱" };
            string anchor = _structuredScoreAnchor;
            var color = ResolveStructuredTextColor(_structuredScore);
            foreach (var choice in new[] { ("黄色", "#FFFF00"), ("秋麒麟", "#DAA520"), ("白色", "#FFFFFF"), ("黑色", "#000000") })
            {
                string hex = choice.Item2;
                var item = new MenuItem { Header = choice.Item1, Tag = hex, IsCheckable = true,
                    IsChecked = color == StructuredScoreDocument.ParseTextColor(hex) };
                item.Click += async (_, _) =>
                {
                    if (_structuredScoreAnchor == anchor) await SetStructuredScoreTextColorAsync(hex);
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var more = new MenuItem { Header = "更多色盘…" };
            more.Click += (_, _) => { if (_structuredScoreAnchor == anchor) OpenColorPicker(); };
            menu.Items.Add(more);
            return menu;
        }

        private Task<StructuredScoreView> GetStructuredViewAsync(StructuredScoreDocument document)
        {
            _structuredViews ??= new();
            var snapshot = CloneStructuredScore(document);
            var color = ResolveStructuredTextColor(snapshot);
            bool invert = _isColorEffectEnabled;
            string key = StructuredViewKey(snapshot);
            if (_structuredViews.TryGetValue(key, out var existing) && !existing.IsFaulted && !existing.IsCanceled) return existing;
            var task = Task.Run(() =>
            {
                bool canCombine = ExtendedScoreNotationRenderer.CanCombine(snapshot.Notation, CancellationToken.None);
                if (snapshot.Layout == StructuredScoreLayout.Combined && !canCombine)
                {
                    snapshot.Layout = StructuredScoreLayout.Paginated;
                    snapshot.Page = 0;
                }
                var background = StructuredScoreDocument.TextBackground(color);
                var paths = snapshot.RenderWithColors(color, background, CancellationToken.None);
                var frames = paths.Select(path => _imageProcessor.PrepareStructuredPage(path, invert, color, nativeTextRendered: true)).ToArray();
                return new StructuredScoreView { Document = snapshot, Paths = paths, Frames = frames, CanCombine = canCombine };
            });
            _structuredViews[key] = task;
            return task;
        }

        private static bool UsesStructuredScoreOriginalMode(StructuredScoreDocument document) =>
            document?.Layout is StructuredScoreLayout.Paginated or StructuredScoreLayout.Combined;

        // 只用于新歌打开，不放进通用渲染入口，避免覆盖用户在当前歌里的手动版式切换。
        private static void SelectStructuredScoreOpeningLayout(StructuredScoreDocument document, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            bool singleCombinedPage = false;
            if (ExtendedScoreNotationRenderer.CanCombine(document.Notation, ct))
            {
                // 在副本上兼容旧分页，避免合页探测更新了分页模式尚未迁移的版本/来源。
                var combined = CloneStructuredScore(document); combined.Layout = StructuredScoreLayout.Combined;
                try
                {
                    combined.PreparePageLines(ct);
                    singleCombinedPage = StructuredScoreLayoutRenderer.GetCombinedPageCount(combined.Notation,
                        combined.CombinedPageLines, combined.CombinedPageLinesManual == true, ct) == 1;
                }
                catch (ScoreIntegrationException ex)
                {
                    // 合页脚本无效时仍可先读分页；不删除用户切分，编辑器可继续修正。
                    System.Diagnostics.Debug.WriteLine("默认合页不可用，改用分页：" + ex.Message);
                }
            }
            document.Layout = singleCombinedPage ? StructuredScoreLayout.Combined : StructuredScoreLayout.Paginated;
            document.Page = 0;
        }

        private bool TryGetPreparedStructuredView(string path, out StructuredScoreView view)
        {
            view = _structuredPreparedView;
            return view != null && _structuredPreparedAnchor == path &&
                _structuredPreparedFileKey == Core.ImageFileCacheKey.GetRawKey(path) &&
                StructuredFrameMatchesColor(view.Frames[0]);
        }

        private async void WarmStructuredViews(StructuredScoreDocument document)
        {
            _structuredWarmCancellation?.Cancel();
            var cancellation = new CancellationTokenSource(); _structuredWarmCancellation = cancellation;
            try
            {
                // 先让当前画面和鼠标反馈完成，再顺序预备其余版式；不阻塞UI或同时抢三套CPU。
                await Task.Delay(80, cancellation.Token);
                var current = await GetStructuredViewAsync(document);
                foreach (var layout in new[] { StructuredScoreLayout.Paginated, StructuredScoreLayout.Combined, StructuredScoreLayout.LongPage })
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (layout == document.Layout) continue;
                    if (layout == StructuredScoreLayout.Combined && !current.CanCombine) continue;
                    var other = CloneStructuredScore(document); other.Layout = layout; other.Page = 0;
                    try { await GetStructuredViewAsync(other); } catch (ScoreIntegrationException) { }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("结构谱后台预备失败：" + ex.Message); }
            finally
            {
                if (_structuredWarmCancellation == cancellation) _structuredWarmCancellation = null;
                cancellation.Dispose();
            }
        }

        private bool ShowStructuredScorePage(StructuredScoreView view, int page, bool layoutChanged = false, bool sourceChanged = false)
        {
            if (view == null || _structuredScoreAnchor == null) return false;
            page = (page % view.Paths.Count + view.Paths.Count) % view.Paths.Count;
            ApplyStructuredScoreViewingMode(_structuredScoreAnchor, view.Document, _originalManager?.ShouldUseOriginalMode(_currentImageId) == true);
            if (layoutChanged && !_originalMode) ResetNormalImageZoomState();
            _imageProcessor.CurrentStructuredScore = view.Document;
            _imageProcessor.SetPreparedColorMode(_isColorEffectEnabled);
            if (!_imageProcessor.LoadPreparedImage(_structuredScoreAnchor, view.Frames[page])) return false;
            if (_structuredShowingImage)
            {
                _structuredShowingImage = false; _structuredButtonLayout = null;
                CancelStructuredScoreImageRequest();
                _projectionManager?.ClearProjectionCache();
                ApplyStructuredScoreColorDefault(true);
            }
            _structuredCurrentView = view;
            _structuredScore = view.Document; _structuredScore.Page = page; _structuredScorePages = view.Paths;
            _imageProcessor.CurrentStructuredScore = _structuredScore;
            _structuredPreparedView = view; _structuredPreparedAnchor = _structuredScoreAnchor;
            if (sourceChanged) _structuredPreparedFileKey = Core.ImageFileCacheKey.GetRawKey(_structuredScoreAnchor);
            StructuredScorePageText.Text = $"{page + 1} / {view.Paths.Count}";
            if (layoutChanged || sourceChanged) UpdateStructuredScoreButtons(view.Document.Layout, view.CanCombine);
            UpdateImageZoomControlsVisibility();
            if (layoutChanged)
            {
                if (!_structuredScoreForcedOriginal) _currentZoom = _originalMode ? _originalModeZoomRatio : 1.0;
                ImageScaleTransform.ScaleX = ImageScaleTransform.ScaleY = _currentZoom;
                UpdateImageZoomControlsVisibility(); UpdateFloatingCompositePlayButton();
            }
            UpdateProjection();
            return true;
        }

        private void ApplyStructuredScoreViewingMode(string path, StructuredScoreDocument document, bool markedOriginal)
        {
            if (UsesStructuredScoreOriginalMode(document))
            {
                bool entering = !_structuredScoreForcedOriginal || path != _structuredScoreOriginalPath;
                if (!_structuredScoreForcedOriginal)
                {
                    _originalModeBeforeStructuredScore = _originalMode;
                    _originalDisplayModeBeforeStructuredScore = _originalDisplayMode;
                    _zoomBeforeStructuredScore = _currentZoom;
                }
                _structuredScoreForcedOriginal = true; _structuredScoreOriginalPath = path;
                if (entering)
                {
                    // 分页和合页都直接原图显示；新歌不继承旧缩放，同首翻页保留手动缩放。
                    _originalDisplayMode = _configManager?.StructuredScoreOriginalDisplayMode ??
                        _structuredScoreOriginalDisplayMode ?? Core.OriginalDisplayMode.FitTop;
                    _imageProcessor.SetPreparedOriginalDisplayMode(_originalDisplayMode);
                    _currentZoom = 1.0;
                    if (ImageScaleTransform != null) ImageScaleTransform.ScaleX = ImageScaleTransform.ScaleY = 1.0;
                }
                // 每页都执行：未标记文件夹的选择判断不能把结构谱原图显示关掉。
                _imageProcessor.SetPreparedOriginalMode(true);
                ApplyOriginalModeUiState(true);
            }
            else if (_structuredScoreForcedOriginal)
            {
                bool restoreOriginal = markedOriginal || (path == _structuredScoreOriginalPath && _originalModeBeforeStructuredScore);
                _structuredScoreForcedOriginal = false; _structuredScoreOriginalPath = null;
                _originalDisplayMode = _originalDisplayModeBeforeStructuredScore;
                _imageProcessor.SetPreparedOriginalDisplayMode(_originalDisplayMode);
                _currentZoom = _zoomBeforeStructuredScore;
                if (ImageScaleTransform != null) ImageScaleTransform.ScaleX = ImageScaleTransform.ScaleY = _currentZoom;
                _imageProcessor.SetPreparedOriginalMode(restoreOriginal);
                ApplyOriginalModeUiState(restoreOriginal);
            }
        }

        private async Task<bool> PrepareStructuredImageAsync(string path)
        {
            CancelStructuredScoreImageRequest(); ++_structuredSwitchVersion;
            long version = ++_structuredOpenVersion;
            _structuredOpenCancellation?.Cancel();
            var cancellation = new CancellationTokenSource(); _structuredOpenCancellation = cancellation;
            try
            {
                if (TryGetPreparedStructuredView(path, out _))
                {
                    ApplyStructuredScoreColorDefault(true);
                    return true;
                }
                var document = await Task.Run(() => StructuredScoreDocument.ReadFromFile(path), cancellation.Token);
                if (version != _structuredOpenVersion) return false;
                if (document == null) { ApplyStructuredScoreColorDefault(false); return true; }
                ApplyStructuredScoreColorDefault(true);
                string identity = Core.ImageFileCacheKey.GetRawKey(path);
                if (_structuredPreparedAnchor != path || _structuredPreparedFileKey != identity)
                {
                    _structuredWarmCancellation?.Cancel(); _structuredViews.Clear();
                }
                await Task.Run(() => SelectStructuredScoreOpeningLayout(document, cancellation.Token), cancellation.Token);
                if (version != _structuredOpenVersion) return false;
                var view = await GetStructuredViewAsync(document);
                if (version != _structuredOpenVersion) return false;
                if ((document.Layout == StructuredScoreLayout.Paginated && document.PageLines == null) ||
                    (document.Layout == StructuredScoreLayout.Combined && document.CombinedPageLines == null))
                    if (StructuredScoreDocument.IsScoreFile(path)) await Task.Run(() => view.Document.Save(path), cancellation.Token);
                _structuredPreparedView = view; _structuredPreparedAnchor = path;
                _structuredPreparedFileKey = Core.ImageFileCacheKey.GetRawKey(path);
                WarmStructuredViews(view.Document);
                return version == _structuredOpenVersion;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                if (version == _structuredOpenVersion)
                {
                    ShowStatus("结构谱原文保留，未切换画面：" + ex.Message);
                }
                return false;
            }
            finally
            {
                if (_structuredOpenCancellation == cancellation) _structuredOpenCancellation = null;
                cancellation.Dispose();
            }
        }

        private async void RefreshStructuredScoreBar(string path)
        {
            if (path != _structuredScoreAnchor || _imageProcessor?.CurrentStructuredScore != null)
            {
                CancelStructuredScoreImageRequest(); _structuredShowingImage = false;
                ReleaseStructuredScoreImageFrame(); _structuredButtonLayout = null;
            }
            if (path != null && path == _structuredScoreAnchor && _structuredCurrentView != null &&
                ReferenceEquals(_structuredCurrentView, _structuredPreparedView) &&
                _structuredPreparedFileKey == Core.ImageFileCacheKey.GetRawKey(path))
            { UpdateStructuredScoreButtons(_structuredScore.Layout, _structuredCurrentView.CanCombine); return; }
            long version = ++_structuredScoreVersion;
            if (StructuredScoreBar == null) return;
            _structuredScore = null; _structuredScorePages = null; _structuredScoreAnchor = null;
            StructuredScoreBar.Tag = false; StructuredScoreBar.IsEnabled = true;
            UpdateStructuredScoreActions();
            if (string.IsNullOrEmpty(path))
            {
                ++_structuredOpenVersion; ++_structuredSwitchVersion; _structuredOpenCancellation?.Cancel();
                _structuredWarmCancellation?.Cancel(); _structuredViews.Clear();
                _structuredCurrentView = _structuredPreparedView = null; _structuredPreparedAnchor = null;
                _structuredButtonLayout = null; return;
            }
            try
            {
                StructuredScoreView view;
                if (!TryGetPreparedStructuredView(path, out view))
                {
                    var loaded = await Task.Run(() => StructuredScoreDocument.ReadFromFile(path));
                    if (loaded == null)
                    {
                        _structuredViews.Clear(); _structuredCurrentView = _structuredPreparedView = null;
                        _structuredPreparedAnchor = null; _structuredWarmCancellation?.Cancel(); return;
                    }
                    await Task.Run(() => SelectStructuredScoreOpeningLayout(loaded, CancellationToken.None));
                    view = await GetStructuredViewAsync(loaded);
                }
                var document = view.Document;
                if (version != _structuredScoreVersion || document == null || _imagePath != path) return;
                _structuredScore = document; _structuredScoreAnchor = path;
                _structuredCurrentView = view;
                UpdateStructuredScoreButtons(document.Layout, view.CanCombine);
                StructuredScoreBar.Tag = true;
                document.Page = Math.Min(document.Page, view.Paths.Count - 1);
                _structuredScorePages = view.Paths; StructuredScorePageText.Text = $"{document.Page + 1} / {view.Paths.Count}";
            }
            catch (Exception ex) { if (version == _structuredScoreVersion) ShowStatus("结构谱原文保留；排版失败：" + ex.Message); }
        }

        private bool CanChangeStructuredScore()
        {
            if (_currentImageId <= 0) return true;
            // 像素关键帧依赖版式。第一版不自动搬动或删除用户已经制作的坐标。
            if (DatabaseManagerService.GetDbContext().Keyframes.Any(k => k.ImageId == _currentImageId))
            {
                ShowStatus("此谱已有关键帧，不能直接改变版式或分页坐标；请先复制谱码文件为另一版本，再制作播放脚本。");
                return false;
            }
            return true;
        }

        private async Task<bool> ApplyStructuredScoreAsync(StructuredScoreDocument document)
        {
            if (_structuredScoreAnchor == null || !StructuredScoreBar.IsEnabled || !CanChangeStructuredScore()) return false;
            string anchor = _structuredScoreAnchor; long version = _structuredScoreVersion, request = ++_structuredSwitchVersion;
            string source = StructuredScoreDocument.IsScoreFile(anchor) ? anchor : Path.ChangeExtension(anchor, StructuredScoreDocument.Extension);
            try
            {
                var view = await GetStructuredViewAsync(document);
                if (version != _structuredScoreVersion || request != _structuredSwitchVersion || _imagePath != anchor) return false;
                await StopCompositePlaybackAsync();
                if (version != _structuredScoreVersion || request != _structuredSwitchVersion || _imagePath != anchor) return false;
                int page = Math.Min(document.Page, view.Paths.Count - 1);
                var saveDocument = CloneStructuredScore(view.Document); saveDocument.Page = page;
                await _structuredWriteGate.WaitAsync();
                try
                {
                    if (request != _structuredSwitchVersion) return false;
                    await Task.Run(() =>
                    {
                        if (File.Exists(source) && StructuredScoreDocument.Read(source).Id != document.Id)
                            throw new ScoreIntegrationException("同名谱文属于另一首歌，未覆盖。请使用另一个文件名。");
                        if (StructuredScoreDocument.IsScoreFile(anchor)) saveDocument.Save(anchor);
                        else saveDocument.WritePng(view.Paths[page], anchor, CancellationToken.None);
                    });
                }
                finally { _structuredWriteGate.Release(); }
                if (version != _structuredScoreVersion || request != _structuredSwitchVersion || _imagePath != anchor) return true;
                bool changed = _structuredScore?.Layout != view.Document.Layout;
                bool contentChanged = _structuredScore?.Notation != view.Document.Notation || _structuredScore?.TextColor != view.Document.TextColor ||
                    _structuredScore?.PageLinesManual != view.Document.PageLinesManual || _structuredScore?.CombinedPageLinesManual != view.Document.CombinedPageLinesManual ||
                    System.Text.Json.JsonSerializer.Serialize(_structuredScore?.PageLines) != System.Text.Json.JsonSerializer.Serialize(view.Document.PageLines) ||
                    System.Text.Json.JsonSerializer.Serialize(_structuredScore?.CombinedPageLines) != System.Text.Json.JsonSerializer.Serialize(view.Document.CombinedPageLines);
                if (contentChanged)
                {
                    // 编辑切分后淘汰旧方案会话，保留本次已准备的结果；不积累每一次切割的位图。
                    _structuredWarmCancellation?.Cancel(); _structuredViews.Clear();
                    string key = StructuredViewKey(view.Document);
                    _structuredViews[key] = Task.FromResult(view);
                }
                if (!ShowStructuredScorePage(view, page, changed, sourceChanged: true)) return false;
                WarmStructuredViews(view.Document);
                return true;
            }
            catch (Exception ex) { ShowStatus("结构谱生成失败，原画面保持：" + ex.Message); return false; }
        }

        private void UpdateStructuredScoreButtons(StructuredScoreLayout layout, bool canCombine)
        {
            MediaFileDialogTheme.EnsureResources();
            UpdateStructuredScoreActions();
            if (_structuredButtonLayout == layout && _structuredButtonCanCombine == canCombine) return;
            _structuredButtonLayout = layout;
            _structuredButtonCanCombine = canCombine;
            foreach (var button in new[] { StructuredScorePaginatedButton, StructuredScoreCombinedButton, StructuredScoreLongButton })
            {
                if (button == null) continue;
                bool enabled = button != StructuredScoreCombinedButton || canCombine;
                button.IsEnabled = enabled;
                bool selected = !_structuredShowingImage && enabled && (int)layout == int.Parse((string)button.Tag);
                // 只更新选择状态；主题配色由动态资源驱动，避免缓存局部画刷阻止即时换肤。
                System.Windows.Controls.Primitives.Selector.SetIsSelected(button, selected);
            }
        }

        private async void StructuredScoreLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_structuredScore == null || !StructuredScoreBar.IsEnabled) return;
            if (sender is not System.Windows.Controls.Button button) return;
            if (!button.IsEnabled || button == StructuredScoreCombinedButton && _structuredCurrentView?.CanCombine != true) return;
            var document = CloneStructuredScore(_structuredScore); document.Layout = (StructuredScoreLayout)int.Parse((string)button.Tag); document.Page = 0;
            if (document.Layout == _structuredScore.Layout)
            {
                if (_structuredShowingImage) ShowStructuredScorePage(_structuredCurrentView, _structuredScore.Page, sourceChanged: true);
                else if (_structuredImageLoading) { CancelStructuredScoreImageRequest(); ++_structuredSwitchVersion; UpdateStructuredScoreActions(); }
                return;
            }
            await ApplyStructuredScoreAsync(document);
        }
        private async void StructuredScorePrevious_Click(object sender, RoutedEventArgs e)
        {
            await TryNavigateStructuredScoreAsync(-1);
        }
        private async void StructuredScoreNext_Click(object sender, RoutedEventArgs e)
        {
            await TryNavigateStructuredScoreAsync(1);
        }
        private Task<bool> TryNavigateStructuredScoreAsync(int direction)
        {
            if (_structuredShowingImage || _structuredScore == null || _structuredScore.Layout == StructuredScoreLayout.LongPage) return Task.FromResult(false);
            if (_structuredCurrentView == null) return Task.FromResult(true);
            if (!StructuredFrameMatchesColor(_structuredCurrentView.Frames[0]))
                return NavigateStructuredScoreWithEffectAsync(direction);
            ++_structuredSwitchVersion;
            ShowStructuredScorePage(_structuredCurrentView, _structuredScore.Page + direction);
            return Task.FromResult(true);
        }
        private async Task<bool> NavigateStructuredScoreWithEffectAsync(int direction)
        {
            long request = ++_structuredSwitchVersion;
            var view = await GetStructuredViewAsync(_structuredScore);
            if (request == _structuredSwitchVersion) ShowStructuredScorePage(view, _structuredScore.Page + direction);
            return true;
        }
        private static StructuredScoreDocument CloneStructuredScore(StructuredScoreDocument source) => new()
        {
            Id = source.Id, Title = source.Title, Notation = source.Notation, ShigebenId = source.ShigebenId,
            NotationType = source.NotationType, Layout = source.Layout, Page = source.Page,
            TextColor = source.TextColor, RenderRevision = source.RenderRevision,
            Lyrics = source.Lyrics == null ? null : source.GetLyrics(), LyricsFromNotation = source.LyricsFromNotation,
            PageLinesManual = source.PageLinesManual, CombinedPageLinesManual = source.CombinedPageLinesManual,
            PageLines = source.PageLines?.Select(p => p.ToArray()).ToArray(), CombinedPageLines = source.CombinedPageLines?.ToArray()
        };
        private async void StructuredScoreEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_structuredScore == null || !CanChangeStructuredScore()) return;
            string anchor = _structuredScoreAnchor;
            var editor = new StructuredScoreEditorWindow(CloneStructuredScore(_structuredScore), async document =>
            {
                if (_structuredScoreAnchor != anchor || !await ApplyStructuredScoreAsync(document))
                    throw new ScoreIntegrationException("分页未写入，已保留上一次切分；请检查文件是否可写及当前歌谱状态。");
            }, StructuredEditorDefaultTextColor()) { Owner = this };
            if (editor.ShowDialog() == true) await ApplyStructuredScoreAsync(editor.Result);
        }
        private async void NewStructuredScore(ProjectTreeItem folder)
        {
            if (folder == null || folder.IsOnlineScoreLibraryRoot || !Directory.Exists(folder.Path) || ResolveRootFolderId(folder) <= 0)
            { ShowStatus("请在本地文件夹内新建歌谱。"); return; }
            var editor = new StructuredScoreEditorWindow(new StructuredScoreDocument
            { Notation = "T:新歌谱\n1=C 4/4\nQ:1 2 3 4 |5 - - - ||\nC1:<1.>新 歌 谱 文 演\nC2:<2.>第 二 节 歌 词" }, null, StructuredEditorDefaultTextColor()) { Owner = this };
            if (editor.ShowDialog() != true) return;
            string savedPath = null;
            try
            {
                savedPath = editor.Result.CreateInFolder(folder.Path);
                var image = ImportManagerService.ImportSingleFileIntoFolder(savedPath, ResolveRootFolderId(folder));
                if (image == null) throw new ScoreIntegrationException(ImportManagerService.LastError ?? "歌谱登记失败。");
                ReloadProjectsPreservingTreeState(); LoadSearchScopes();
                await HandleFileNodeClickAsync(new ProjectTreeItem { Id = image.Id, Name = image.Name, Path = image.Path, Type = TreeItemType.File, FileType = image.FileType, RootFolderId = image.FolderId });
            }
            catch (Exception ex) { ShowStatus((savedPath == null ? "新建未完成：" : "谱文已保留在 " + savedPath + "；显示未完成：") + ex.Message); }
        }
    }
}
