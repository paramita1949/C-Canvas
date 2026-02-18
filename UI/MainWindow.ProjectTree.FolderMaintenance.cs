using System;
using System.Windows;
using ImageColorChanger.Database.Models;

using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：文件夹维护与播放模式
    /// </summary>
    public partial class MainWindow : Window
    {
        private void DeleteFolder(ProjectTreeItem item)
        {
            var result = MessageBox.Show(
                $"确定要删除文件夹 '{item.Name}' 吗？\n这将从项目中移除该文件夹及其所有文件。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                TryDeleteFolder(item, forceDelete: false);
            }
        }

        /// <summary>
        /// 尝试删除文件夹（支持强制删除）
        /// </summary>
        private void TryDeleteFolder(ProjectTreeItem item, bool forceDelete)
        {
            try
            {
                DatabaseManagerService.DeleteFolder(item.Id, forceDelete);
                LoadProjects();
                LoadSearchScopes();

                if (forceDelete)
                {
                    ShowStatus($"🔥 已强制删除文件夹: {item.Name}");
                }
                else
                {
                    ShowStatus($"🗑️ 已删除文件夹: {item.Name}");
                }
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[删除文件夹] 数据库异常: {dbEx.Message}");
                if (dbEx.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[删除文件夹] 内部异常: {dbEx.InnerException.Message}");
                }
#else
                _ = dbEx;
#endif

                if (!forceDelete)
                {
                    var forceResult = MessageBox.Show(
                        $"删除文件夹失败：数据库约束冲突\n\n" +
                        $"可能原因：\n" +
                        $"1. 文件夹中存在其他电脑导入的文件\n" +
                        $"2. 数据库状态不同步\n\n" +
                        $"是否强制删除？\n" +
                        $"⚠️ 警告：强制删除会忽略所有约束，直接清除数据库记录",
                        "删除失败 - 是否强制删除？",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning
                    );

                    if (forceResult == MessageBoxResult.Yes)
                    {
                        TryDeleteFolder(item, forceDelete: true);
                    }
                    else
                    {
                        ShowStatus($"❌ 取消删除文件夹: {item.Name}");
                    }
                }
                else
                {
                    MessageBox.Show(
                        $"强制删除失败！\n\n{dbEx.Message}\n\n" +
                        $"建议：\n" +
                        $"- 关闭所有使用该数据库的程序\n" +
                        $"- 重启应用程序后再试",
                        "强制删除失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );

                    ShowStatus($"❌ 强制删除失败: {item.Name}");
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[删除文件夹] 未知异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[删除文件夹] 堆栈: {ex.StackTrace}");
#else
                _ = ex;
#endif

                MessageBox.Show(
                    $"删除文件夹时发生错误：\n{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                ShowStatus($"❌ 删除文件夹失败: {item.Name}");
            }
        }

        /// <summary>
        /// 同步文件夹
        /// </summary>
        private void SyncFolder(ProjectTreeItem item)
        {
            var (added, removed, _) = ImportManagerService.SyncFolder(item.Id);
            LoadProjects();
            ShowStatus($"🔄 同步完成: {item.Name} (新增 {added}, 删除 {removed})");
        }

        /// <summary>
        /// 设置文件夹的视频播放模式
        /// </summary>
        private void SetFolderPlayMode(ProjectTreeItem item, string playMode)
        {
            try
            {
                DatabaseManagerService.SetFolderVideoPlayMode(item.Id, playMode);

                string[] modeNames = { "顺序播放", "随机播放", "列表循环" };
                string modeName = playMode switch
                {
                    "sequential" => modeNames[0],
                    "random" => modeNames[1],
                    "loop_all" => modeNames[2],
                    _ => "未知"
                };

                LoadProjects();
                ShowStatus($"✅ 已设置文件夹 [{item.Name}] 的播放模式: {modeName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"设置播放模式失败: {ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 清除文件夹的视频播放模式
        /// </summary>
        private void ClearFolderPlayMode(ProjectTreeItem item)
        {
            try
            {
                DatabaseManagerService.ClearFolderVideoPlayMode(item.Id);
                LoadProjects();
                ShowStatus($"✅ 已清除文件夹 [{item.Name}] 的播放模式");
            }
            catch (Exception)
            {
            }
        }
    }
}
