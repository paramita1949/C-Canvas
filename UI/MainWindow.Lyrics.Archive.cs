using System;
using MessageBox = System.Windows.MessageBox;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Services.Lyrics;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        // 档案是内部数据；只在已有图片右键菜单提供维护入口，不参与文件/歌曲树。
        private MenuItem BuildImageLyricsArchiveMenu(int imageId)
        {
            if (_dbContext == null || imageId <= 0) return null;
            var image = _dbContext.MediaFiles.AsNoTracking().SingleOrDefault(m => m.Id == imageId && m.FileTypeString == "image");
            if (image == null) return null;
            var archives = _dbContext.ImageLyricsArchives.AsNoTracking().ToList()
                .Where(a => a.Reason != "RecoveryChosen" && (a.OriginalImageId == imageId ||
                    string.Equals(a.OriginalImagePath, image.Path, StringComparison.OrdinalIgnoreCase) || a.Reason == "LegacyUnowned"))
                .OrderByDescending(a => a.CreatedTime).ToList();
            if (archives.Count == 0) return null;
            var menu = new MenuItem { Header = $"图片歌词归档（{archives.Count}）" };
            var export = new MenuItem { Header = "导出完整归档报告…" };
            export.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "图片歌词归档报告|*.json", FileName = "图片歌词归档.json" };
                if (dialog.ShowDialog() != true) return;
                try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(archives, new JsonSerializerOptions { WriteIndented = true })); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "归档导出失败", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            menu.Items.Add(export);
            menu.Items.Add(new Separator());
            foreach (var archive in archives)
            {
                string label = archive.Reason switch { "LegacyConflict" => "旧绑定冲突", "LegacyUnowned" => "归属不明", "DeletedImage" => "删除前保存", "ImportConflict" => "导入冲突", _ => "迁移原始备份" };
                var item = new MenuItem { Header = $"{label} · {archive.CreatedTime:yyyy-MM-dd HH:mm:ss} · #{archive.Id}" };
                item.Click += (s, e) =>
                {
                    try
                    {
                        var document = ImageLyricsService.ReadArchiveDocument(archive);
                        string content = document.Content ?? "";
                        string preview = content.Length > 2000 ? content.Substring(0, 2000) + "\n（预览截断，完整内容可导出）" : content;
                        if (MessageBox.Show($"原图片：{archive.OriginalImagePath}\n归档编号：{archive.ExternalId}\n\n{preview}\n\n是否明确选择此版本恢复到当前图片？\n不会覆盖已有歌词，其他归档仍保留。归属不明的记录须自行核对。", "图片歌词归档", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                        new ImageLyricsService(_dbContext).RecoverArchive(archive.ExternalId, imageId);
                        MessageBox.Show("恢复完成，原始归档仍保留。请重新进入图片歌词编辑器。", "图片歌词恢复", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(ex.Message, "未恢复图片歌词", MessageBoxButton.OK, MessageBoxImage.Warning); }
                };
                menu.Items.Add(item);
            }
            return menu;
        }

        private void NotifyImageLyricsMigrationArchives()
        {
            var uncertain = _dbContext.ImageLyricsArchives.AsNoTracking().Where(a => a.Reason == "LegacyUnowned" || a.Reason == "LegacyConflict").ToList();
            int unresolved = uncertain.Count(a => !a.OriginalImageId.HasValue || !_dbContext.ImageLyrics.Any(p => p.ImageId == a.OriginalImageId));
            if (unresolved == 0) return;
            MessageBox.Show($"旧图片歌词迁移保留了 {unresolved} 条冲突或归属不明记录，未自动选择正文。\n它们不会显示为根目录歌曲。\n右键图片 → 图片歌词归档，可检查、导出和明确选择恢复；已有歌词不会被覆盖。", "图片歌词迁移提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
