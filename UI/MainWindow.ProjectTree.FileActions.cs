using System.Windows;
using System.Diagnostics;
using ImageColorChanger.Database.Models;

using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：文件操作
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 删除文件
        /// </summary>
        private void DeleteFile(ProjectTreeItem item)
        {
            var result = MessageBox.Show(
                $"确定要删除文件 '{item.Name}' 吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                DatabaseManagerService.DeleteMediaFile(item.Id);
                LoadProjects();
                ShowStatus($"🗑️ 已删除文件: {item.Name}");
            }
        }

        /// <summary>
        /// 打开文件所在位置并选中文件
        /// </summary>
        private void OpenFileLocation(ProjectTreeItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path))
            {
                ShowStatus("❌ 文件路径无效");
                return;
            }

            if (!System.IO.File.Exists(item.Path) && !System.IO.Directory.Exists(item.Path))
            {
                ShowStatus($"❌ 文件不存在: {item.Name}");
                return;
            }

            try
            {
                OpenPathInExplorer(item.Path);
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 打开文件位置失败: {ex.Message}");
            }
        }

        private void OpenPathInExplorer(string path)
        {
            var selectTarget = path;
            if (System.IO.Directory.Exists(path))
            {
                selectTarget = path.TrimEnd('\\');
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{selectTarget}\"",
                UseShellExecute = true
            });
        }
    }
}
