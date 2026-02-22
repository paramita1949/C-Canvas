using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System;
using System.Linq;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.UI.Modules;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：右键菜单构建
    /// </summary>
    public partial class MainWindow
    {
        private void ProjectTree_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 获取右键点击的项目
            if (e.OriginalSource is FrameworkElement element)
            {
                var treeViewItem = FindParent<TreeViewItem>(element);

                // 如果点击在空白区域（没有TreeViewItem），只在幻灯片项目模式显示新建项目菜单
                if (treeViewItem == null)
                {
                    TryShowRootBlankAreaContextMenu(sender as UIElement, e);
                    return;
                }

                if (!TryGetTreeItemFromEvent(e, out var item))
                {
                    return;
                }

                var contextMenu = CreateNoBorderContextMenu();

                switch (item.Type)
                {
                    case TreeItemType.Folder:
                        if (!BuildFolderContextMenu(contextMenu, item))
                        {
                            return;
                        }
                        break;
                    case TreeItemType.File:
                        BuildFileContextMenu(contextMenu, item);
                        break;
                    case TreeItemType.Project:
                    case TreeItemType.TextProject:
                        BuildTextProjectContextMenu(contextMenu, item);
                        break;
                    case TreeItemType.LyricsRoot:
                        if (!IsLyricsLibraryFeatureEnabled)
                        {
                            return;
                        }
                        BuildLyricsRootContextMenu(contextMenu);
                        break;
                    case TreeItemType.LyricsGroup:
                        if (!IsLyricsLibraryFeatureEnabled)
                        {
                            return;
                        }
                        BuildLyricsGroupContextMenu(contextMenu, item);
                        break;
                    case TreeItemType.LyricsSong:
                        if (!IsLyricsLibraryFeatureEnabled)
                        {
                            return;
                        }
                        BuildLyricsSongContextMenu(contextMenu, item);
                        break;
                    default:
                        return;
                }

                contextMenu.IsOpen = true;
            }
        }

        private ContextMenu CreateNoBorderContextMenu()
        {
            var contextMenu = new ContextMenu
            {
                Style = (Style)FindResource("NoBorderContextMenuStyle")
            };
            return contextMenu;
        }

        private void TryShowRootBlankAreaContextMenu(UIElement placementTarget, MouseButtonEventArgs e)
        {
            if (_currentViewMode == NavigationViewMode.Files && IsLyricsLibraryFeatureEnabled)
            {
                var filesMenu = CreateNoBorderContextMenu();

                var newLibraryItem = new MenuItem { Header = " 新建歌词库" };
                newLibraryItem.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                newLibraryItem.Foreground = Brushes.White;
                newLibraryItem.BorderThickness = new Thickness(0);
                newLibraryItem.BorderBrush = Brushes.Transparent;
                newLibraryItem.Click += (s, args) => CreateLyricsLibrary();
                filesMenu.Items.Add(newLibraryItem);

                filesMenu.IsOpen = true;
                filesMenu.PlacementTarget = placementTarget;
                e.Handled = true;
                return;
            }

            if (_currentViewMode != NavigationViewMode.Projects)
            {
                return;
            }

            var contextMenu = CreateNoBorderContextMenu();

            var newProjectItem = new MenuItem { Header = "新建项目" };
            newProjectItem.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            newProjectItem.Foreground = Brushes.White;
            newProjectItem.BorderThickness = new Thickness(0);
            newProjectItem.BorderBrush = Brushes.Transparent;
            newProjectItem.Click += async (s, args) =>
            {
                string projectName = await GenerateDefaultProjectNameAsync();
                await CreateTextProjectAsync(projectName);
            };
            contextMenu.Items.Add(newProjectItem);
            contextMenu.Items.Add(new Separator());

            var exportAllItem = new MenuItem { Header = "导出所有项目" };
            exportAllItem.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            exportAllItem.Foreground = Brushes.White;
            exportAllItem.BorderThickness = new Thickness(0);
            exportAllItem.BorderBrush = Brushes.Transparent;
            exportAllItem.Click += async (s, args) => await ExportAllProjectsAsync();
            contextMenu.Items.Add(exportAllItem);

            contextMenu.IsOpen = true;
            contextMenu.PlacementTarget = placementTarget;
            e.Handled = true;
        }

        private bool BuildFolderContextMenu(ContextMenu contextMenu, ProjectTreeItem item)
        {
            var folderMenuState = _projectTreeFolderMenuStateController?.GetState(item.Id);
            if (folderMenuState == null)
            {
                return false;
            }

            BuildFolderOriginalAndColorMenus(contextMenu, item, folderMenuState);
            BuildFolderPlayModeMenu(contextMenu, item, folderMenuState);
            BuildFolderSortAndActionMenus(contextMenu, item, folderMenuState);

            return true;
        }

        private void BuildFolderOriginalAndColorMenus(ContextMenu contextMenu, ProjectTreeItem item, ProjectTreeFolderMenuState folderMenuState)
        {
            if (!folderMenuState.HasImages)
            {
                return;
            }

            if (folderMenuState.HasFolderOriginalMark)
            {
                var unmarkFolderItem = new MenuItem { Header = "取消原图" };
                unmarkFolderItem.Click += (s, args) => UnmarkOriginalFolder(item);
                contextMenu.Items.Add(unmarkFolderItem);
            }
            else
            {
                var markFolderMenuItem = new MenuItem { Header = "标记为原图" };
                var loopFolderItem = new MenuItem { Header = "循环模式" };
                loopFolderItem.Click += (s, args) => MarkFolderAsOriginal(item, MarkType.Loop);
                markFolderMenuItem.Items.Add(loopFolderItem);

                var sequenceFolderItem = new MenuItem { Header = "顺序模式" };
                sequenceFolderItem.Click += (s, args) => MarkFolderAsOriginal(item, MarkType.Sequence);
                markFolderMenuItem.Items.Add(sequenceFolderItem);

                contextMenu.Items.Add(markFolderMenuItem);
            }

            contextMenu.Items.Add(new Separator());

            if (folderMenuState.HasColorEffectMark)
            {
                var unmarkColorItem = new MenuItem { Header = "取消变色标记" };
                unmarkColorItem.Click += (s, args) => UnmarkFolderColorEffect(item);
                contextMenu.Items.Add(unmarkColorItem);
            }
            else
            {
                var markColorItem = new MenuItem { Header = "标记为变色" };
                markColorItem.Click += (s, args) => MarkFolderColorEffect(item);
                contextMenu.Items.Add(markColorItem);
            }

            contextMenu.Items.Add(new Separator());
        }

        private void BuildFolderPlayModeMenu(ContextMenu contextMenu, ProjectTreeItem item, ProjectTreeFolderMenuState folderMenuState)
        {
            if (!folderMenuState.HasVideoOrAudio)
            {
                return;
            }

            var currentPlayMode = folderMenuState.CurrentPlayMode;
            var playModeMenuItem = new MenuItem { Header = "播放模式" };

            var sequentialItem = new MenuItem
            {
                Header = "顺序",
                IsCheckable = true,
                IsChecked = currentPlayMode == "sequential"
            };
            sequentialItem.Click += (s, args) => SetFolderPlayMode(item, "sequential");
            playModeMenuItem.Items.Add(sequentialItem);

            var randomItem = new MenuItem
            {
                Header = "随机",
                IsCheckable = true,
                IsChecked = currentPlayMode == "random"
            };
            randomItem.Click += (s, args) => SetFolderPlayMode(item, "random");
            playModeMenuItem.Items.Add(randomItem);

            var loopAllItem = new MenuItem
            {
                Header = "循环",
                IsCheckable = true,
                IsChecked = currentPlayMode == "loop_all"
            };
            loopAllItem.Click += (s, args) => SetFolderPlayMode(item, "loop_all");
            playModeMenuItem.Items.Add(loopAllItem);

            var loopOneItem = new MenuItem
            {
                Header = "单曲",
                IsCheckable = true,
                IsChecked = currentPlayMode == "loop_one"
            };
            loopOneItem.Click += (s, args) => SetFolderPlayMode(item, "loop_one");
            playModeMenuItem.Items.Add(loopOneItem);

            contextMenu.Items.Add(playModeMenuItem);
            contextMenu.Items.Add(new Separator());
        }

        private void BuildFolderSortAndActionMenus(ContextMenu contextMenu, ProjectTreeItem item, ProjectTreeFolderMenuState folderMenuState)
        {
            if (folderMenuState.IsManualSort)
            {
                var resetSortItem = new MenuItem { Header = " 重置排序" };
                resetSortItem.Click += (s, args) => ResetFolderSort(item);
                contextMenu.Items.Add(resetSortItem);
                contextMenu.Items.Add(new Separator());
            }

            var deleteItem = new MenuItem { Header = "删除文件夹" };
            deleteItem.Click += (s, args) => DeleteFolder(item);
            contextMenu.Items.Add(deleteItem);

            var syncItem = new MenuItem { Header = "同步文件夹" };
            syncItem.Click += (s, args) => SyncFolder(item);
            contextMenu.Items.Add(syncItem);
        }

        private void BuildFileContextMenu(ContextMenu contextMenu, ProjectTreeItem item)
        {
            var createSplitMenuItem = new MenuItem
            {
                Header = "创建分割图",
                FontSize = 14
            };
            createSplitMenuItem.Click += async (s, args) =>
            {
                await CreateSplitSlideInPraiseProjectFromFile(item.Id);
            };
            contextMenu.Items.Add(createSplitMenuItem);

            var addToSlideMenuItem = new MenuItem
            {
                Header = "添加到幻灯片",
                FontSize = 14
            };
            addToSlideMenuItem.Click += async (s, args) =>
            {
                await AddSingleSlideToPraiseProjectFromFile(item.Id);
            };
            contextMenu.Items.Add(addToSlideMenuItem);

            contextMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "删除文件" };
            deleteItem.Click += (s, args) => DeleteFile(item);
            contextMenu.Items.Add(deleteItem);
        }

        private void BuildTextProjectContextMenu(ContextMenu contextMenu, ProjectTreeItem item)
        {
            var renameItem = new MenuItem { Header = "✏ 重命名" };
            renameItem.Click += (s, args) => RenameTextProjectAsync(item);
            contextMenu.Items.Add(renameItem);

            contextMenu.Items.Add(new Separator());

            var exportItem = new MenuItem { Header = "导出" };
            exportItem.Click += async (s, args) => await ExportTextProjectAsync(item);
            contextMenu.Items.Add(exportItem);

            contextMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = " 删除项目" };
            deleteItem.Click += async (s, args) => await DeleteTextProjectAsync(item);
            contextMenu.Items.Add(deleteItem);
        }

    }
}


