using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
                filesMenu.MinWidth = 176;
                filesMenu.FontSize = 14;

                var newLibraryItem = CreateIconMenuItem("新建歌词库", "IconLucideBookPlus", () => CreateLyricsLibrary());
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
            contextMenu.MinWidth = 176;
            contextMenu.FontSize = 14;

            var newProjectItem = CreateIconMenuItem("新建项目", "IconLucidePlus", async () =>
            {
                string projectName = await GenerateDefaultProjectNameAsync();
                await CreateTextProjectAsync(projectName);
            });
            contextMenu.Items.Add(newProjectItem);
            contextMenu.Items.Add(new Separator());

            var exportAllItem = CreateIconMenuItem("导出所有项目", "IconLucideUpload", async () => await ExportAllProjectsAsync());
            contextMenu.Items.Add(exportAllItem);

            contextMenu.IsOpen = true;
            contextMenu.PlacementTarget = placementTarget;
            e.Handled = true;
        }

        private bool BuildFolderContextMenu(ContextMenu contextMenu, ProjectTreeItem item)
        {
            contextMenu.MinWidth = 176;
            contextMenu.FontSize = 14;

            if (item?.IsVirtualFolder == true)
            {
                BuildVirtualFolderContextMenu(contextMenu, item);
                return true;
            }

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
                var unmarkFolderItem = CreateIconMenuItem("取消原图", "IconLucideUndo2", () => UnmarkOriginalFolder(item));
                contextMenu.Items.Add(unmarkFolderItem);
            }
            else
            {
                var markFolderMenuItem = CreateIconSubMenuItem("标记为原图", "IconLucideImage");
                var loopFolderItem = CreateIconMenuItem("循环模式", "IconLucideRepeat", () => MarkFolderAsOriginal(item, MarkType.Loop));
                markFolderMenuItem.Items.Add(loopFolderItem);

                var sequenceFolderItem = CreateIconMenuItem("顺序模式", "IconLucideAlignStartVertical", () => MarkFolderAsOriginal(item, MarkType.Sequence));
                markFolderMenuItem.Items.Add(sequenceFolderItem);

                contextMenu.Items.Add(markFolderMenuItem);
            }

            contextMenu.Items.Add(new Separator());

            if (folderMenuState.HasColorEffectMark)
            {
                var unmarkColorItem = CreateIconMenuItem("取消变色标记", "IconLucideRotateCcw", () => UnmarkFolderColorEffect(item));
                contextMenu.Items.Add(unmarkColorItem);
            }
            else
            {
                var markColorItem = CreateIconMenuItem("标记为变色", "IconLucideWand2", () => MarkFolderColorEffect(item));
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
            var playModeMenuItem = CreateIconSubMenuItem("播放模式", "IconLucidePlay");

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
                var resetSortItem = CreateIconMenuItem("重置排序", "IconLucideUndo2", () => ResetFolderSort(item));
                contextMenu.Items.Add(resetSortItem);
                contextMenu.Items.Add(new Separator());
            }

            var highlightColorItem = CreateIconMenuItem("标记高亮色", "IconLucidePalette", () => SetFolderHighlightColor(item));
            contextMenu.Items.Add(highlightColorItem);

            if (folderMenuState.HasHighlightColor)
            {
                var clearHighlightColorItem = CreateIconMenuItem("取消高亮色", "IconLucideRotateCcw", () => ClearFolderHighlightColor(item));
                contextMenu.Items.Add(clearHighlightColorItem);
            }

            contextMenu.Items.Add(new Separator());

            var deleteItem = CreateIconMenuItem("删除文件夹", "IconLucideX", () => DeleteFolder(item));
            contextMenu.Items.Add(deleteItem);

            var syncItem = CreateIconMenuItem("同步文件夹", "IconLucideRefreshCw", () => SyncFolder(item));
            contextMenu.Items.Add(syncItem);

            AppendFolderHierarchyModeMenu(contextMenu, item);
        }

        private void BuildVirtualFolderContextMenu(ContextMenu contextMenu, ProjectTreeItem item)
        {
            AppendFolderHierarchyModeMenu(contextMenu, item);

            if (contextMenu.Items.Count > 0)
            {
                contextMenu.Items.Add(new Separator());
            }

            var toggleExpandItem = CreateIconMenuItem(
                item.IsExpanded ? "折叠子目录" : "展开子目录",
                "IconLucideFolderOpen",
                () => item.IsExpanded = !item.IsExpanded);
            contextMenu.Items.Add(toggleExpandItem);
        }

        private void AppendFolderHierarchyModeMenu(ContextMenu contextMenu, ProjectTreeItem item)
        {
            int rootFolderId = ResolveRootFolderId(item);
            if (rootFolderId <= 0)
            {
                return;
            }

            var rootFolderItem = _projectTreeItems.FirstOrDefault(i =>
                i.Type == TreeItemType.Folder &&
                !i.IsVirtualFolder &&
                i.Id == rootFolderId);

            bool hierarchyEnabled = IsFolderHierarchyEnabled(rootFolderId);
            bool hasNestedFolders = rootFolderItem?.HasNestedFolders == true;
            if (!hasNestedFolders && !hierarchyEnabled)
            {
                return;
            }

            contextMenu.Items.Add(new Separator());

            var hierarchyMenu = CreateIconSubMenuItem("目录层级", "IconLucideFolderOpen");
            var singleLevelItem = new MenuItem
            {
                Header = "单级显示",
                IsCheckable = true,
                IsChecked = !hierarchyEnabled
            };
            singleLevelItem.Click += (s, args) => SetFolderHierarchyMode(item, false);
            hierarchyMenu.Items.Add(singleLevelItem);

            var multiLevelItem = new MenuItem
            {
                Header = "多级显示",
                IsCheckable = true,
                IsChecked = hierarchyEnabled
            };
            multiLevelItem.Click += (s, args) => SetFolderHierarchyMode(item, true);
            hierarchyMenu.Items.Add(multiLevelItem);
            contextMenu.Items.Add(hierarchyMenu);
        }

        private void BuildFileContextMenu(ContextMenu contextMenu, ProjectTreeItem item)
        {
            contextMenu.MinWidth = 176;
            contextMenu.FontSize = 14;

            var createSplitMenuItem = CreateIconMenuItem("创建分割图", "IconLucideLayoutGrid", async () =>
            {
                await CreateSplitSlideInPraiseProjectFromFile(item.Id);
            });
            contextMenu.Items.Add(createSplitMenuItem);

            var addToSlideMenuItem = CreateIconMenuItem("添加到幻灯片", "IconLucideFileText", async () =>
            {
                await AddSingleSlideToPraiseProjectFromFile(item.Id);
            });
            contextMenu.Items.Add(addToSlideMenuItem);

            contextMenu.Items.Add(new Separator());

            var deleteItem = CreateIconMenuItem("删除文件", "IconLucideX", () => DeleteFile(item));
            contextMenu.Items.Add(deleteItem);
        }

        private void BuildTextProjectContextMenu(ContextMenu contextMenu, ProjectTreeItem item)
        {
            contextMenu.MinWidth = 168;
            contextMenu.FontSize = 14;

            contextMenu.Items.Add(CreateIconMenuItem("重命名", "IconLucidePencil", () => RenameTextProjectAsync(item)));
            contextMenu.Items.Add(CreateIconMenuItem("删除", "IconLucideX", async () => await DeleteTextProjectAsync(item)));
            contextMenu.Items.Add(CreateIconMenuItem("复制", "IconLucideCopy2", async () => await CopyTextProjectAsync(item)));
            contextMenu.Items.Add(CreateIconMenuItem("导出", "IconLucideUpload", async () => await ExportTextProjectAsync(item)));
        }

        private MenuItem CreateIconSubMenuItem(string text, string iconResourceKey)
        {
            var header = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new Path
            {
                Data = TryFindResource(iconResourceKey) as Geometry,
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
                Fill = Brushes.Transparent
            };
            if (TryFindResource("LucideIconPathStyle") is Style iconStyle)
            {
                icon.Style = iconStyle;
            }

            var label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "BrushMenuText");

            header.Children.Add(icon);
            header.Children.Add(label);

            return new MenuItem { Header = header };
        }

        private MenuItem CreateIconMenuItem(string text, string iconResourceKey, Action onClick)
        {
            var header = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new Path
            {
                Data = TryFindResource(iconResourceKey) as Geometry,
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Uniform,
                Fill = Brushes.Transparent
            };
            if (TryFindResource("LucideIconPathStyle") is Style iconStyle)
            {
                icon.Style = iconStyle;
            }

            var label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "BrushMenuText");

            header.Children.Add(icon);
            header.Children.Add(label);

            var item = new MenuItem { Header = header };
            item.Click += (_, _) => onClick?.Invoke();
            return item;
        }

    }
}


