using System;
using System.Linq;
using System.Windows;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Services.Lyrics;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private bool _isSwitchingImageLyrics;

        /// <summary>图片私有歌词跟随图片；独立歌曲不跟随，普通图片模式不自动进入歌词。</summary>
        private void HandleImageSelectionInLyricsMode(ProjectTreeItem item, int imageId)
        {
            if (_lyricsEntryKind != LyricsEntryKind.ImageBound || imageId == _lyricsEntryId || _isSwitchingImageLyrics)
                return;

            if (IsLyricsImeCompositionActive() || _isLoadingLyricsProject)
            {
                ReportImageLyricsNavigationFailure("请先完成当前歌词输入，再切换图片。");
                return;
            }

            LyricsDocument target;
            int targetRecordId;
            bool hasLyrics;
            try
            {
                _imageLyricsService ??= new ImageLyricsService(_dbContext);
                target = _imageLyricsService.Load(imageId);
                if (target == null) throw new InvalidOperationException("对应图片已不存在或不是图片。");
                targetRecordId = _imageLyricsService.GetRecordId(imageId);
                hasLyrics = HasImageLyricsBody(target.Content);
            }
            catch (Exception ex)
            {
                ReportImageLyricsNavigationFailure(ex.Message);
                return;
            }

            // 保存仍使用旧的 immutable entry；失败绝不重绑定、不丢正文。
            if (!TrySaveLyricsProject("ImageNavigation", suppressUserError: true))
            {
                ReportImageLyricsNavigationFailure("当前图片歌词未能保存，请重试保存后再切换。原正文仍保留。");
                return;
            }

            int oldEntryId = _lyricsEntryId;
            int oldImageId = _currentImageId;
            int oldRecordId = _loadedImageLyricsId;
            string oldPath = _imagePath;
            var oldDocument = _currentLyricsDocument;
            var restoreImage = _imageProcessor.CaptureImageRestoreAction();
            var oldDisplayState = (_originalMode, _isColorEffectEnabled, _currentFolderId, _currentZoom, _originalModeZoomRatio);
            var oldTransform = (ImageScaleTransform.ScaleX, ImageScaleTransform.ScaleY);
            var oldButtons = (BtnOriginal.Background, BtnColorEffect.Background);
            bool editorChanged = false;
            bool committed = false;
            _isSwitchingImageLyrics = true;
            StopAutoSaveTimer();
            try
            {
                var folderId = _dbContext.MediaFiles.Where(m => m.Id == imageId).Select(m => m.FolderId).SingleOrDefault();
                if (folderId.HasValue && _projectTreeSelectionStateController != null)
                {
                    var decision = _projectTreeSelectionStateController.EvaluateFileSelection(
                        folderId.Value, oldImageId, _originalMode, _isColorEffectEnabled, _currentFolderId);
                    ApplyFileSelectionDecision(decision);
                }
                // 准备正确的目标bitmap，但不投影中间图片帧；F6退出后也是目标图。
                _currentImageId = imageId;
                if (!TryLoadImage(item.Path, publishProjection: false, showError: false))
                    throw new InvalidOperationException("目标图片加载失败。");

                editorChanged = true;
                if (hasLyrics)
                {
                    BindLyricsEntry(LyricsEntryKind.ImageBound, imageId);
                    _loadedImageLyricsId = targetRecordId;
                    LoadLyricsProjectCore(target, throwOnError: true);
                    if (_currentLyricsDocument == null) throw new InvalidOperationException("目标歌词加载失败。");
                }
                else
                {
                    BindLyricsEntry(LyricsEntryKind.None, 0);
                    _loadedImageLyricsId = 0;
                    _isLoadingLyricsProject = true;
                    try { ResetLyricsEditorForProjectLoad(); }
                    finally { _isLoadingLyricsProject = false; }
                    CompleteLyricsModeExit();
                }
                committed = true;
            }
            catch (Exception ex)
            {
                _currentImageId = oldImageId;
                _imagePath = oldPath;
                _isLyricsMode = true;
                restoreImage();
                (_originalMode, _isColorEffectEnabled, _currentFolderId, _currentZoom, _originalModeZoomRatio) = oldDisplayState;
                (ImageScaleTransform.ScaleX, ImageScaleTransform.ScaleY) = oldTransform;
                (BtnOriginal.Background, BtnColorEffect.Background) = oldButtons;
                if (editorChanged)
                {
                    BindLyricsEntry(LyricsEntryKind.ImageBound, oldEntryId);
                    _loadedImageLyricsId = oldRecordId;
                    LoadLyricsProjectCore(oldDocument, throwOnError: true);
                    LyricsEditorPanel.Visibility = Visibility.Visible;
                    ImageScrollViewer.Visibility = Visibility.Collapsed;
                }
                SelectTreeItemById(oldImageId);
                ReportImageLyricsNavigationFailure(ex.Message);
            }
            finally
            {
                _isSwitchingImageLyrics = false;
                if (_isLyricsMode) StartAutoSaveTimer();
            }

            // 一次提交主屏/投影/NDI的新内容，失败时只重新发布旧内容。
            if (_isLyricsMode)
            {
                if (_projectionManager?.IsProjecting == true || _imageNdiModule?.IsEnabled() == true)
                    RenderLyricsToProjection();
            }
            else UpdateProjection();
            if (committed) ShowStatus(hasLyrics ? $"已切换图片歌词：{item.Name}" : $"该图片暂无歌词，已显示图片：{item.Name}");
        }

        /// <summary>只判断当前展示模式的正文，配置JSON或保留的非当前模式正文不算正在展示的歌词。</summary>
        private bool HasImageLyricsBody(string content)
        {
            bool Body(LyricsSplitContentData data) => data?.Regions?
                .Take(GetSplitRegionCount((ViewSplitMode)data.SplitMode)).Any(s => !string.IsNullOrWhiteSpace(s)) == true;
            if (string.IsNullOrWhiteSpace(content)) return false;
            if (content.StartsWith(LyricsModeContentPrefix, StringComparison.Ordinal))
            {
                if (!TryParseModeLyricsContent(content, out var mode)) throw new InvalidOperationException("图片歌词格式无效，未切换。请检查内部归档。");
                return mode.ActiveMode == (int)ViewSplitMode.Single
                    ? !string.IsNullOrWhiteSpace(mode.SingleContent)
                    : Body(mode.SplitContents.FirstOrDefault(p => p.SplitMode == mode.ActiveMode));
            }
            if (content.StartsWith(LyricsPagesContentPrefix, StringComparison.Ordinal))
            {
                if (!TryParsePagesLyricsContent(content, out var pages)) throw new InvalidOperationException("图片歌词分页格式无效，未切换。");
                // 共用加载器将legacy分页收敛到首个分屏，未加载旧页不能让当前投影变空白。
                return Body(pages.Pages.FirstOrDefault());
            }
            if (content.StartsWith(LyricsSplitContentPrefix, StringComparison.Ordinal))
            {
                if (!TryParseSplitLyricsContent(content, out var split)) throw new InvalidOperationException("图片歌词分屏格式无效，未切换。");
                return Body(split);
            }
            return true;
        }

        private void ReportImageLyricsNavigationFailure(string message)
        {
            ShowStatus(message);
            // 初始加载/未显示窗口不弹对话框；运行中的窗口必须显式报告失败。
            if (IsLoaded) System.Windows.MessageBox.Show(message, "图片歌词未切换", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
