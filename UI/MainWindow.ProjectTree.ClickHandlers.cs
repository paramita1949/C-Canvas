using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：单击/双击处理
    /// </summary>
    public partial class MainWindow
    {
        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogTreeExpandDebug(string phase, ProjectTreeItem item, string extra = "")
        {
            if (item == null)
            {
                System.Diagnostics.Debug.WriteLine($"[TreeExpandDebug] {phase} item=<null> {extra}");
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[TreeExpandDebug] {phase} id={item.Id} name='{item.Name}' virtual={item.IsVirtualFolder} expanded={item.IsExpanded} children={item.Children?.Count ?? 0} stateKey='{item.StateKey}' {extra}");
        }

        private async void ProjectTree_MouseClick(object sender, MouseButtonEventArgs e)
        {
            if (!TryGetTreeItemFromEvent(e, out var selectedItem))
            {
                return;
            }

            LogTreeExpandDebug(
                "MouseClick.Hit",
                selectedItem,
                $"src={e?.OriginalSource?.GetType().Name ?? "<null>"}");

            if (_isBibleMode && selectedItem.Type == TreeItemType.BibleChapter)
            {
                await HandleBibleNodeClickAsync(selectedItem);
                return;
            }

            if (selectedItem.Type == TreeItemType.Project || selectedItem.Type == TreeItemType.TextProject)
            {
                int projectId = selectedItem.Id;
                _ = LoadTextProjectAsync(projectId);
                return;
            }

            if (IsLyricsLibraryFeatureEnabled && selectedItem.Type == TreeItemType.LyricsSong)
            {
                await EnterLyricsModeFromSongAsync(selectedItem.Id);
                return;
            }

            if (IsLyricsLibraryFeatureEnabled && selectedItem.Type == TreeItemType.LyricsGroup)
            {
                selectedItem.IsExpanded = !selectedItem.IsExpanded;
                e.Handled = true;
                return;
            }

            if (selectedItem.Type == TreeItemType.Folder)
            {
                await HandleFolderNodeClickAsync(selectedItem, e);
            }
            else if (selectedItem.Type == TreeItemType.File && !string.IsNullOrEmpty(selectedItem.Path))
            {
                await HandleFileNodeClickAsync(selectedItem);
            }
        }

        private async Task HandleFolderNodeClickAsync(ProjectTreeItem selectedItem, MouseButtonEventArgs e)
        {
            LogTreeExpandDebug(
                "HandleFolderNodeClick.Begin",
                selectedItem,
                $"src={e?.OriginalSource?.GetType().Name ?? "<null>"}");

            if (selectedItem?.IsVirtualFolder == true)
            {
                // 点击 +/- 展开器时，避免再执行一次手动切换导致“展开后又折叠”。
                if (e?.OriginalSource is DependencyObject source &&
                    FindParent<ToggleButton>(source) != null)
                {
                    var treeViewItem = FindParent<TreeViewItem>(source);
                    if (treeViewItem != null)
                    {
                        bool before = selectedItem.IsExpanded;
                        selectedItem.IsExpanded = treeViewItem.IsExpanded;
                        LogTreeExpandDebug(
                            "Virtual.ToggleButton.SyncFromTreeViewItem",
                            selectedItem,
                            $"before={before} treeViewItemExpanded={treeViewItem.IsExpanded}");
                    }
                    else
                    {
                        LogTreeExpandDebug("Virtual.ToggleButton.TreeViewItemMissing", selectedItem);
                    }

                    e.Handled = true;
                    LogTreeExpandDebug("HandleFolderNodeClick.End.ToggleButton", selectedItem);
                    return;
                }

                bool beforeToggle = selectedItem.IsExpanded;
                selectedItem.IsExpanded = !selectedItem.IsExpanded;
                LogTreeExpandDebug("Virtual.RowClick.Toggle", selectedItem, $"before={beforeToggle} after={selectedItem.IsExpanded}");
                e.Handled = true;
                LogTreeExpandDebug("HandleFolderNodeClick.End.Virtual", selectedItem);
                return;
            }

            // 分割模式下需要保持编辑态，允许继续从文件树选择图片填充区域。
            if (TextEditorPanel.Visibility == Visibility.Visible && !IsInSplitMode())
            {
                await AutoExitTextEditorIfNeededAsync();
            }

            LogTreeExpandDebug("TopFolder.BeforeCollapseOtherFolders", selectedItem);
            CollapseOtherFolders(selectedItem);
            bool beforeTopToggle = selectedItem.IsExpanded;
            selectedItem.IsExpanded = !selectedItem.IsExpanded;
            LogTreeExpandDebug("TopFolder.AfterToggle", selectedItem, $"before={beforeTopToggle} after={selectedItem.IsExpanded}");

            var folderDecision = _projectTreeSelectionStateController?.EvaluateFolderSelection(
                selectedItem.Id,
                _currentImageId,
                !string.IsNullOrEmpty(_imagePath),
                _originalMode,
                _isColorEffectEnabled,
                _currentFolderId);

            if (folderDecision != null)
            {
                ApplyFolderSelectionDecision(selectedItem, folderDecision);
            }

            e.Handled = true;
            LogTreeExpandDebug("HandleFolderNodeClick.End", selectedItem);
        }

        private async Task HandleFileNodeClickAsync(ProjectTreeItem selectedItem)
        {
            bool isEditingSlide = TextEditorPanel.Visibility == Visibility.Visible;
            bool keepEditorForSplitImage = isEditingSlide &&
                                           IsInSplitMode() &&
                                           selectedItem.FileType == FileType.Image;
            if (isEditingSlide && !keepEditorForSplitImage)
            {
                await AutoExitTextEditorIfNeededAsync();
            }

            int fileId = selectedItem.Id;
            var mediaFile = DatabaseManagerService.GetMediaFileById(fileId);
            if (mediaFile != null && mediaFile.FolderId.HasValue)
            {
                var fileDecision = _projectTreeSelectionStateController?.EvaluateFileSelection(
                    mediaFile.FolderId.Value,
                    _currentImageId,
                    _originalMode,
                    _isColorEffectEnabled,
                    _currentFolderId);

                if (fileDecision != null)
                {
                    ApplyFileSelectionDecision(fileDecision);
                }
            }

            if (!System.IO.File.Exists(selectedItem.Path))
            {
                ShowStatus($"文件不存在: {selectedItem.Name}");
                return;
            }

            switch (selectedItem.FileType)
            {
                case FileType.Image:
                    await HandleImageFileSingleClickAsync(selectedItem, fileId);
                    break;
                case FileType.Video:
                case FileType.Audio:
                    HandleMediaFileSingleClick(selectedItem, fileId);
                    break;
            }
        }

        private void ProjectTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var clickTime = System.Diagnostics.Stopwatch.StartNew();

            if (!TryGetTreeItemFromEvent(e, out var selectedItem))
            {
                return;
            }

            if (selectedItem.Type != TreeItemType.File || string.IsNullOrEmpty(selectedItem.Path))
            {
                return;
            }

            if (!System.IO.File.Exists(selectedItem.Path))
            {
                ShowStatus($"文件不存在: {selectedItem.Name}");
                return;
            }

            switch (selectedItem.FileType)
            {
                case FileType.Video:
                case FileType.Audio:
                    HandleMediaFileDoubleClick(selectedItem);
                    break;
                case FileType.Image:
                    HandleImageFileDoubleClick(selectedItem, clickTime);
                    break;
            }
        }
    }
}


