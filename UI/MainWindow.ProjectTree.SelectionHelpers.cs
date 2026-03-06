using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.UI.Modules;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;
using TreeViewItem = System.Windows.Controls.TreeViewItem;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：选择状态与文件点击辅助
    /// </summary>
    public partial class MainWindow
    {
        private void ApplyFolderSelectionDecision(ProjectTreeItem selectedItem, FolderSelectionDecision folderDecision)
        {
            if (folderDecision.EnableOriginalMode)
            {
                ApplyOriginalModeUiState(true);
                if (folderDecision.ClearCurrentImageDisplay)
                {
                    ClearImageDisplay();
                }
                ShowStatus($"已启用原图模式: {selectedItem.Name}(黄色)");
            }
            else if (folderDecision.DisableOriginalMode)
            {
                ApplyOriginalModeUiState(false);
                if (folderDecision.ClearCurrentImageDisplay)
                {
                    ClearImageDisplay();
                }
                ShowStatus($"已关闭原图模式: {selectedItem.Name}");
            }

            if (folderDecision.EnableColorEffect)
            {
                ApplyColorEffectUiState(true);
                ShowStatus($"已切换到变色文件夹: {selectedItem.Name}");
            }
            else if (folderDecision.DisableColorEffect)
            {
                ApplyColorEffectUiState(false);
                ShowStatus($"已切换到无变色文件夹: {selectedItem.Name}");
            }

            _currentFolderId = folderDecision.NewCurrentFolderId;
        }

        private void ApplyFileSelectionDecision(FileSelectionDecision fileDecision)
        {
            if (fileDecision.EnableOriginalMode)
            {
                ApplyOriginalModeUiState(true);
            }
            else if (fileDecision.DisableOriginalMode)
            {
                ApplyOriginalModeUiState(false);
            }

            if (fileDecision.EnableColorEffect)
            {
                ApplyColorEffectUiState(true);
            }
            else if (fileDecision.DisableColorEffect)
            {
                ApplyColorEffectUiState(false);
            }

            _currentFolderId = fileDecision.NewCurrentFolderId;
        }

        private async Task HandleImageFileSingleClickAsync(ProjectTreeItem selectedItem, int fileId)
        {
            if (TextEditorPanel.Visibility == Visibility.Visible && IsInSplitMode())
            {
                await LoadImageToSplitRegion(selectedItem.Path);
                ShowStatus($"已加载: {selectedItem.Name}");
                return;
            }

            if (_isLyricsMode)
            {
                ShowStatus("歌词模块已独立，图片切换不影响当前歌词");
                return;
            }

            if (_currentViewMode == NavigationViewMode.Projects)
            {
                ShowStatus($"请先打开幻灯片进入分割模式，或切换到文件视图");
                return;
            }

            SwitchToImageMode();
            _currentImageId = fileId;
            LoadImage(selectedItem.Path);
        }

        private void HandleMediaFileSingleClick(ProjectTreeItem selectedItem, int fileId)
        {
            if (IsAppleDoubleSidecarPath(selectedItem.Path))
            {
                ShowStatus($"已跳过系统伴随文件: {selectedItem.Name}");
                return;
            }

            _imagePath = selectedItem.Path;
            _currentImageId = fileId;
            CompositePlaybackPanel.Visibility = Visibility.Collapsed;

            string fileType = selectedItem.FileType == FileType.Video ? "视频" : "音频";
            ShowStatus($"已选中{fileType}: {selectedItem.Name} (双击播放)");
        }

        private void HandleMediaFileDoubleClick(ProjectTreeItem selectedItem)
        {
            if (IsAppleDoubleSidecarPath(selectedItem.Path))
            {
                ShowStatus($"无法播放系统伴随文件: {selectedItem.Name}");
                return;
            }
            if (_projectionManager != null && _projectionManager.IsProjectionActive)
            {
                LoadAndDisplayVideoOnProjection(selectedItem.Path);
            }
            else
            {
                LoadAndDisplayVideo(selectedItem.Path);
            }

            ShowStatus($"正在播放: {selectedItem.Name}");
        }

        private void HandleImageFileDoubleClick(ProjectTreeItem selectedItem, Stopwatch clickTime)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"切换到图片: {selectedItem.Name}");
#endif
            var switchStart = clickTime.ElapsedMilliseconds;
            SwitchToImageMode();

            if (_playbackViewModel != null && _playbackViewModel.IsPlaying)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("停止当前播放");
#endif
                _ = _playbackViewModel.StopPlaybackCommand.ExecuteAsync(null);
            }

            var loadStart = clickTime.ElapsedMilliseconds;
            LoadImage(selectedItem.Path);
            var loadTime = clickTime.ElapsedMilliseconds - loadStart;

            clickTime.Stop();
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[切换图片] 准备耗时: {switchStart}ms, 加载耗时: {loadTime}ms, 总耗时: {clickTime.ElapsedMilliseconds}ms");
            System.Diagnostics.Debug.WriteLine($"========================================\n");
#endif
        }

        private bool TryGetTreeItemFromEvent(MouseButtonEventArgs e, out ProjectTreeItem selectedItem)
        {
            selectedItem = null;
            if (e.OriginalSource is not FrameworkElement element)
            {
                return false;
            }

            var treeViewItem = FindParent<TreeViewItem>(element);
            if (treeViewItem?.DataContext is not ProjectTreeItem item)
            {
                return false;
            }

            selectedItem = item;
            return true;
        }

        /// <summary>
        /// 查找父级元素
        /// </summary>
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                {
                    return parent;
                }

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        private void ApplyOriginalModeUiState(bool enabled)
        {
            _originalMode = enabled;
            _imageProcessor.OriginalMode = enabled;
            BtnOriginal.Background = enabled
                ? new SolidColorBrush(Color.FromRgb(144, 238, 144))
                : Brushes.Transparent;
        }

        private void ApplyColorEffectUiState(bool enabled)
        {
            _isColorEffectEnabled = enabled;
            BtnColorEffect.Background = enabled
                ? new SolidColorBrush(Color.FromRgb(255, 215, 0))
                : Brushes.Transparent;
        }

        private async Task EnterLyricsModeFromSongAsync(int lyricsProjectId)
        {
            try
            {
                if (lyricsProjectId <= 0)
                {
                    return;
                }

                await AutoExitTextEditorIfNeededAsync();
                SetLyricsEntryBySong(lyricsProjectId);

                if (_isLyricsMode)
                {
                    LoadOrCreateLyricsProject();
                    if (_projectionManager != null && _projectionManager.IsProjecting)
                    {
                        RenderLyricsToProjection();
                    }
                }
                else
                {
                    EnterLyricsMode();
                }

                ShowStatus("已进入歌词模式（独立歌曲）");
            }
            catch (Exception ex)
            {
                ShowStatus($"打开歌曲歌词失败: {ex.Message}");
            }
        }
    }
}



