using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 导航视图与项目树加载
    /// </summary>
    public partial class MainWindow
    {
        // 添加标记，用于跟踪是否是第一次进入幻灯片模式
        private static bool _isFirstTimeEnteringProjects = true;

        /// <summary>
        /// 从数据库加载项目树
        /// </summary>
        private void LoadProjects()
        {
            if (_textProjectManager == null) return;
            try
            {
                var dbManager = DatabaseManagerService;
                _projectTreeItems.Clear();

                var folders = dbManager.GetAllFolders();
                var rootFiles = dbManager.GetRootMediaFiles();
                var manualSortFolderIds = dbManager.GetManualSortFolderIds();
#if DEBUG
                System.Diagnostics.Trace.WriteLine(
                    $"[LoadProjects] DbPath={dbManager.GetDatabasePath()}, folders={folders.Count}, rootFiles={rootFiles.Count}");
                foreach (var f in folders)
                {
                    System.Diagnostics.Trace.WriteLine($"[LoadProjects] Folder: Id={f.Id}, Name={f.Name}, Path={f.Path}");
                }
#endif

                foreach (var folder in folders)
                {
                    bool isManualSort = manualSortFolderIds.Contains(folder.Id);
                    var files = dbManager.GetMediaFilesByFolder(folder.Id);
                    bool hasMediaFiles = files.Any(f => f.FileType == FileType.Video || f.FileType == FileType.Audio);
                    string folderPlayMode = dbManager.GetFolderVideoPlayMode(folder.Id);
                    bool hasColorEffectMark = dbManager.HasFolderAutoColorEffect(folder.Id);

                    string iconKind;
                    string iconColor;
                    if (hasMediaFiles)
                    {
                        if (!string.IsNullOrEmpty(folderPlayMode))
                        {
                            (iconKind, iconColor) = GetPlayModeIcon(folderPlayMode);
                        }
                        else
                        {
                            (iconKind, iconColor) = ("Shuffle", "#FF9800");
                        }
                    }
                    else if (!string.IsNullOrEmpty(folderPlayMode))
                    {
                        (iconKind, iconColor) = GetPlayModeIcon(folderPlayMode);
                    }
                    else if (hasColorEffectMark)
                    {
                        (iconKind, iconColor) = ("Palette", "#FF6B6B");
                    }
                    else
                    {
                        (iconKind, iconColor) = _originalManager.GetFolderIconKind(folder.Id, isManualSort);
                    }

                    var folderItem = new ProjectTreeItem
                    {
                        Id = folder.Id,
                        Name = folder.Name,
                        Icon = iconKind,
                        IconKind = iconKind,
                        IconColor = iconColor,
                        Type = TreeItemType.Folder,
                        Path = folder.Path,
                        Children = new ObservableCollection<ProjectTreeItem>()
                    };

                    foreach (var file in files)
                    {
                        string fileIconKind = "File";
                        string fileIconColor = "#95E1D3";
                        if (file.FileType == FileType.Image)
                        {
                            (fileIconKind, fileIconColor) = _originalManager.GetImageIconKind(file.Id);
                        }

                        folderItem.Children.Add(new ProjectTreeItem
                        {
                            Id = file.Id,
                            Name = file.Name,
                            Icon = fileIconKind,
                            IconKind = fileIconKind,
                            IconColor = fileIconColor,
                            Type = TreeItemType.File,
                            Path = file.Path,
                            FileType = file.FileType
                        });
                    }

                    _projectTreeItems.Add(folderItem);
                }

                foreach (var file in rootFiles)
                {
                    string rootFileIconKind = "File";
                    string rootFileIconColor = "#95E1D3";
                    if (file.FileType == FileType.Image)
                    {
                        (rootFileIconKind, rootFileIconColor) = _originalManager.GetImageIconKind(file.Id);
                    }

                    _projectTreeItems.Add(new ProjectTreeItem
                    {
                        Id = file.Id,
                        Name = file.Name,
                        Icon = rootFileIconKind,
                        IconKind = rootFileIconKind,
                        IconColor = rootFileIconColor,
                        Type = TreeItemType.File,
                        Path = file.Path,
                        FileType = file.FileType
                    });
                }

                LoadTextProjectsToTree();
                FilterProjectTree();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 根据当前视图模式过滤项目树
        /// </summary>
        private void FilterProjectTree()
        {
            _filteredProjectTreeItems.Clear();

            if (_currentViewMode == NavigationViewMode.Files)
            {
                foreach (var item in _projectTreeItems)
                {
                    if (item.Type == TreeItemType.Folder || item.Type == TreeItemType.File)
                    {
                        _filteredProjectTreeItems.Add(item);
                    }
                }
            }
            else
            {
                foreach (var item in _projectTreeItems)
                {
                    if (item.Type == TreeItemType.Project || item.Type == TreeItemType.TextProject)
                    {
                        _filteredProjectTreeItems.Add(item);
                    }
                }
            }
        }

        /// <summary>
        /// 文件按钮点击事件
        /// </summary>
        private void BtnShowFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewMode == NavigationViewMode.Files && !_isBibleMode) return;

            bool wasInBibleMode = _isBibleMode;

            _currentViewMode = NavigationViewMode.Files;
            _isBibleMode = false;

            BibleVerseScrollViewer.Visibility = Visibility.Collapsed;
            BibleNavigationPanel.Visibility = Visibility.Collapsed;
            ProjectTree.Visibility = Visibility.Visible;

            if (wasInBibleMode)
            {
                ImageScrollViewer.Visibility = Visibility.Visible;
                VideoContainer.Visibility = Visibility.Visible;
            }

            UpdateViewModeButtons();
            FilterProjectTree();
        }

        /// <summary>
        /// 项目按钮点击事件
        /// </summary>
        private void BtnShowProjects_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewMode == NavigationViewMode.Projects && !_isBibleMode) return;

            bool wasInBibleMode = _isBibleMode;

            _currentViewMode = NavigationViewMode.Projects;
            _isBibleMode = false;

            BibleVerseScrollViewer.Visibility = Visibility.Collapsed;
            BibleNavigationPanel.Visibility = Visibility.Collapsed;
            ProjectTree.Visibility = Visibility.Visible;

            if (wasInBibleMode)
            {
                ClearImageDisplay();
                ImageScrollViewer.Visibility = Visibility.Visible;
                VideoContainer.Visibility = Visibility.Visible;
            }

            UpdateViewModeButtons();
            FilterProjectTree();

            if (_isFirstTimeEnteringProjects)
            {
                _ = LoadFirstProjectAsync();
                _isFirstTimeEnteringProjects = false;
            }
        }

        /// <summary>
        /// 更新切换按钮的视觉状态
        /// </summary>
        private void UpdateViewModeButtons()
        {
            var inactiveBackground = new SolidColorBrush(Colors.White);
            var inactiveForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666"));
            var inactiveBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0"));

            var activeBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3"));
            var activeForeground = new SolidColorBrush(Colors.White);
            var activeBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1976D2"));

            BtnShowFiles.Background = _currentViewMode == NavigationViewMode.Files ? activeBackground : inactiveBackground;
            BtnShowFiles.Foreground = _currentViewMode == NavigationViewMode.Files ? activeForeground : inactiveForeground;
            BtnShowFiles.BorderBrush = _currentViewMode == NavigationViewMode.Files ? activeBorder : inactiveBorder;

            BtnShowProjects.Background = _currentViewMode == NavigationViewMode.Projects ? activeBackground : inactiveBackground;
            BtnShowProjects.Foreground = _currentViewMode == NavigationViewMode.Projects ? activeForeground : inactiveForeground;
            BtnShowProjects.BorderBrush = _currentViewMode == NavigationViewMode.Projects ? activeBorder : inactiveBorder;

            BtnShowBible.Background = _currentViewMode == NavigationViewMode.Bible ? activeBackground : inactiveBackground;
            BtnShowBible.Foreground = _currentViewMode == NavigationViewMode.Bible ? activeForeground : inactiveForeground;
            BtnShowBible.BorderBrush = _currentViewMode == NavigationViewMode.Bible ? activeBorder : inactiveBorder;
        }

        /// <summary>
        /// 加载文本项目到项目树
        /// </summary>
        private void LoadTextProjectsToTree()
        {
            try
            {
                if (_textProjectManager == null)
                {
                    _textProjectManager = new TextProjectManager(_dbContext);
                }

                var textProjects = _textProjectManager.GetAllProjectsAsync().GetAwaiter().GetResult();

                foreach (var project in textProjects)
                {
                    _projectTreeItems.Add(new ProjectTreeItem
                    {
                        Id = project.Id,
                        Name = project.Name,
                        Icon = "FileDocument",
                        IconKind = "FileDocument",
                        IconColor = "#2196F3",
                        Type = TreeItemType.TextProject,
                        Path = null
                    });
                }
            }
            catch (Exception)
            {
            }
        }

        private async Task LoadFirstProjectAsync()
        {
            try
            {
                if (_textProjectManager == null)
                {
                    _textProjectManager = new TextProjectManager(_dbContext);
                }

                var textProjects = await _textProjectManager.GetAllProjectsAsync();

                if (textProjects != null && textProjects.Count > 0)
                {
                    var firstProject = textProjects[0];
                    await LoadTextProjectAsync(firstProject.Id);
                    ShowStatus($"✅ 已打开项目: {firstProject.Name}");
                }
                else
                {
                    ShowStatus("📝 暂无幻灯片项目，请创建新项目");
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"⚠️ 加载项目失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据文件类型获取图标
        /// </summary>
        private string GetFileIcon(FileType fileType)
        {
            return fileType switch
            {
                FileType.Image => "🖼️",
                FileType.Video => "🎬",
                FileType.Audio => "🎵",
                _ => "📄"
            };
        }
    }
}
