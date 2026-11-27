using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Services.OnlineScores;
using Microsoft.Win32;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private MenuItem BuildOnlineScoreStorageMenuItem()
        {
            var menu = new MenuItem { Header = "存储位置" };
            var choose = new MenuItem { Header = "选择存储点" };
            choose.Click += (_, _) => ChooseOnlineScoreStorageDirectory();
            menu.Items.Add(choose);

            var open = new MenuItem { Header = "打开文件夹" };
            open.Click += (_, _) =>
            {
                try
                {
                    TryOpenFileLocation(GetOnlineScoreStorageSettings().DirectoryPath,
                        "存储路径无效", "存储文件夹不存在");
                }
                catch (Exception ex) { ShowStatus("打开存储文件夹失败：" + ex.Message); }
            };
            menu.Items.Add(open);
            return menu;
        }

        private void ChooseOnlineScoreStorageDirectory()
        {
            try
            {
                StopImportMenuAutoCloseTimer();
                var dialog = new OpenFolderDialog
                {
                    Title = "选择歌本存储点（按歌本建子目录，原文件不会移动）",
                    InitialDirectory = GetOnlineScoreStorageSettings().DirectoryPath,
                    Multiselect = false
                };
                if (dialog.ShowDialog(this) == true) ChangeOnlineScoreStorageDirectory(dialog.FolderName);
            }
            catch (Exception ex) { ShowStatus("选择歌谱文件夹失败：" + ex.Message); }
        }

        private void ChangeOnlineScoreStorageDirectory(string directory)
        {
            try
            {
                var settings = GetOnlineScoreStorageSettings();
                if (string.Equals(settings.DirectoryPath, OnlineScoreStorageSettings.Normalize(directory),
                    StringComparison.OrdinalIgnoreCase)) return;
                // 切换位置只取消旧点击，不清空搜索结果、不更改主画面。
                StopOnlineScoreDownloadFeedback();
                _onlineScoreSelectionCts?.Cancel(); _onlineScoreSelectionCts?.Dispose();
                _onlineScoreSelectionCts = null;
                ++_onlineScoreSelectionVersion;
                settings.ChangeDirectory(directory);
                _onlineScoreLibrary = null;
                ReloadProjectsPreservingTreeState();
                LoadSearchScopes();
                UpdateOnlineScoreResultCount();
                ShowStatus($"歌谱将按歌本保存到 {settings.DirectoryPath} 下；导航只显示歌本，原文件和歌词保留。");
            }
            catch (Exception ex) { ShowStatus("存储位置未切换：" + ex.Message); }
        }
    }
}
