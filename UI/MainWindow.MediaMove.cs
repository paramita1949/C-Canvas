using System;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private async Task RenameImageFile(ProjectTreeItem item)
        {
            try
            {
                var image = DatabaseManagerService.GetMediaFileById(item.Id);
                if (image == null) { ShowStatus("歌曲图片已不存在。"); return; }
                // 退出歌词模式后仍保留文档引用，不能为了重命名而保存隐藏的旧入口。
                if (_isLyricsMode && _currentLyricsDocument != null && !TrySaveLyricsProject("MediaRename")) { ShowStatus("请先完成当前歌词输入并保存，再重命名。"); return; }
                var dialog = new RenameSongWindow(image, async name =>
                {
                    await Task.Delay(40);
                    return new MediaMoveService(_dbContext).Rename(image.Id, name);
                }) { Owner = this };
                if (dialog.ShowDialog() != true) return;
                try { await RefreshRelocatedImage(item, image); }
                catch (Exception ex) { ShowStatus("文件已重命名，界面刷新失败：" + ex.Message); return; }
                ShowStatus($"已重命名：{image.Name}；实际文件名同步更新，歌词及图片关联数据保留。");
            }
            catch (Exception ex) { ShowStatus("重命名未完成：" + ex.Message); }
        }

        private async Task MoveImageToFolder(ProjectTreeItem item)
        {
            try
            {
                var image = DatabaseManagerService.GetMediaFileById(item.Id);
                if (image == null) { ShowStatus("歌曲图片已不存在。"); return; }
                // 仅保存正在编辑的歌词，避免刷新遗漏正文；不提交已退出的旧歌词入口。
                if (_isLyricsMode && _currentLyricsDocument != null && !TrySaveLyricsProject("MediaMove")) { ShowStatus("请先完成当前歌词输入并保存，再移动。"); return; }
                var dialog = new MoveSongWindow(image, DatabaseManagerService.GetAllFolders(), async (folderId, name, rename) =>
                {
                    // 先让 WPF 绘制忙碌状态；同一 UI 线程执行数据库事务，避免共享 DbContext 跨线程。
                    await Task.Delay(40);
                    return new MediaMoveService(_dbContext).Move(image.Id, folderId, rename, name);
                }, GetOnlineScoreStorageSettings().CacheDirectories) { Owner = this };
                if (dialog.ShowDialog() != true) return;
                try { await RefreshRelocatedImage(item, image); }
                catch (Exception ex) { ShowStatus("文件已移动，界面刷新失败：" + ex.Message); return; }
                ShowStatus($"已移动：{image.Name}；歌词及图片关联数据保留。");
            }
            catch (Exception ex) { ShowStatus("移动未完成：" + ex.Message); }
        }

        private async Task RefreshRelocatedImage(ProjectTreeItem item, MediaFile image)
        {
            item.Path = image.Path;
            item.Name = image.Name;
            item.RootFolderId = image.FolderId;
            _imageProcessor?.ClearImageCache();
            ReloadProjectsPreservingTreeState(TreeItemType.File, image.Id);
            SelectTreeItemById(image.Id);
            LoadSearchScopes();
            await HandleFileNodeClickAsync(new ProjectTreeItem
            {
                Id = image.Id, Name = image.Name, Path = image.Path, Type = TreeItemType.File,
                FileType = image.FileType, RootFolderId = image.FolderId
            });
        }
    }
}
