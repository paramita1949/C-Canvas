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
            try
            {
                var settings = GetOnlineScoreStorageSettings();
                menu.Items.Add(new MenuItem
                {
                    Header = "歌本存储点：" + settings.DisplayName,
                    ToolTip = settings.DirectoryPath,
                    IsEnabled = false
                });
                var choose = new MenuItem { Header = "选择歌本存储位置…" };
                choose.Click += (_, _) => ChooseOnlineScoreStorageDirectory();
                menu.Items.Add(choose);
                var open = new MenuItem { Header = "打开当前文件夹" };
                open.Click += (_, _) => TryOpenFileLocation(GetOnlineScoreStorageSettings().DirectoryPath,
                    "存储路径无效", "存储文件夹不存在");
                menu.Items.Add(open);
                menu.Items.Add(new Separator());
                var reset = new MenuItem
                {
                    Header = "使用默认 YM 存储点（导航不显示）",
                    IsEnabled = !string.Equals(settings.DirectoryPath, OnlineScoreStorageSettings.DefaultDirectory,
                        StringComparison.OrdinalIgnoreCase)
                };
                reset.Click += (_, _) => ChangeOnlineScoreStorageDirectory(OnlineScoreStorageSettings.DefaultDirectory);
                menu.Items.Add(reset);
            }
            catch (Exception ex)
            {
                menu.Items.Add(new MenuItem { Header = "读取存储设置失败", ToolTip = ex.Message, IsEnabled = false });
            }
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
