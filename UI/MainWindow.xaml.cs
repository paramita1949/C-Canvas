using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = System.Windows.Media.Color;
using Image = SixLabors.ImageSharp.Image;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Collections.ObjectModel;
using System.Linq;
using ImageColorChanger.Core;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;

namespace ImageColorChanger.UI
{
    public partial class MainWindow : Window
    {
        #region 字段

        // 图像处理相关
        private ImageProcessor imageProcessor;
        private Image<Rgba32> originalImage;
        private Image<Rgba32> currentImage;
        private string imagePath;
        private BackgroundType backgroundType = BackgroundType.White;
        private GPUProcessor gpuProcessor;

        // 图片缩放相关
        private double currentZoom = 1.0;
        private const double MinZoom = Constants.MinZoomRatio;
        private const double MaxZoom = Constants.MaxZoomRatio;
        private const double ZoomStep = 0.05;

        // 图片拖动相关
        private bool isDragging = false;
        private System.Windows.Point dragStartPoint;

        // 变色功能相关
        private bool isColorEffectEnabled = false;
        private Rgba32 currentTargetColor = new Rgba32(174, 159, 112); // 默认颜色

        // 项目数据
        private ObservableCollection<ProjectTreeItem> projectTreeItems = new ObservableCollection<ProjectTreeItem>();
        private int currentImageId = 0; // 当前加载的图片ID

        // 原图模式相关
        private bool originalMode = false;
        private OriginalDisplayMode originalDisplayMode = OriginalDisplayMode.Stretch;

        // 数据库和管理器
        private DatabaseManager dbManager;
        private ConfigManager configManager;
        private ImportManager importManager;
        private ImageSaveManager imageSaveManager;
        private SearchManager searchManager;
        private SortManager sortManager;
        private ProjectionManager projectionManager;
        private OriginalManager originalManager;

        #endregion

        #region 初始化

        public MainWindow()
        {
            InitializeComponent();
            
            // 初始化GPU处理器
            InitializeGpuProcessor();
            
            // 初始化UI
            InitializeUI();
        }

        private void InitializeGpuProcessor()
        {
            gpuProcessor = new GPUProcessor();
            if (gpuProcessor.Initialize())
            {
                ShowStatus("✅ 就绪 (GPU加速已启用 - ComputeSharp)");
            }
            else
            {
                ShowStatus("❌ GPU初始化失败");
                MessageBox.Show(
                    "GPU初始化失败！\n\n" +
                    "可能原因：\n" +
                    "1. 显卡不支持DirectX 12或以上\n" +
                    "2. 显卡驱动过旧\n" +
                    "3. 系统不支持GPU计算\n\n" +
                    "程序将无法运行。",
                    "GPU错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void InitializeUI()
        {
            // 初始化数据库
            InitializeDatabase();
            
            // 初始化图片处理器
            imageProcessor = new ImageProcessor(this, ImageScrollViewer, ImageDisplay, ImageContainer);
            
            // 加载用户设置（必须在 imageProcessor 创建之后）
            LoadSettings();
            
            // 初始化保存管理器
            imageSaveManager = new ImageSaveManager(imageProcessor);
            
            // 初始化投影管理器
            projectionManager = new ProjectionManager(
                this,
                ImageScrollViewer,
                ImageDisplay,
                imageProcessor,
                ScreenSelector
            );
            
            // 订阅投影状态改变事件
            projectionManager.ProjectionStateChanged += OnProjectionStateChanged;
            
            // 初始化原图管理器
            originalManager = new OriginalManager(dbManager, this);
            
            // 初始化项目树
            ProjectTree.ItemsSource = projectTreeItems;
            
            // 初始化屏幕选择器
            InitializeScreenSelector();
            
            // 添加滚动同步
            ImageScrollViewer.ScrollChanged += ImageScrollViewer_ScrollChanged;
            
            // 加载项目
            LoadProjects();
        }
        
        /// <summary>
        /// 滚动事件处理 - 同步投影
        /// </summary>
        private void ImageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            projectionManager?.SyncProjectionScroll();
        }
        
        /// <summary>
        /// 更新投影内容
        /// </summary>
        private void UpdateProjection()
        {
            if (imageProcessor.CurrentImage != null)
            {
                projectionManager?.UpdateProjectionImage(
                    imageProcessor.CurrentImage,
                    isColorEffectEnabled,
                    currentZoom,
                    originalMode,
                    originalDisplayMode  // 传递原图显示模式
                );
            }
        }

        private void InitializeDatabase()
        {
            try
            {
                // 创建配置管理器
                configManager = new ConfigManager();
                
                // 创建数据库管理器
                dbManager = new DatabaseManager("pyimages.db");
                
                // 创建排序和搜索管理器
                sortManager = new SortManager();
                searchManager = new SearchManager(dbManager);
                
            // 创建导入管理器
            importManager = new ImportManager(dbManager, sortManager);
            
            // 加载搜索范围选项
            LoadSearchScopes();
            
            System.Diagnostics.Debug.WriteLine("✅ 数据库初始化成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"数据库初始化失败: {ex}");
            }
        }

        private void InitializeScreenSelector()
        {
            // 屏幕选择器由 ProjectionManager 管理，这里不需要再初始化
            // ProjectionManager 会在初始化时自动填充屏幕列表并选择扩展屏
        }

        /// <summary>
        /// 从数据库加载项目树
        /// </summary>
        private void LoadProjects()
        {
            try
            {
                projectTreeItems.Clear();

                // 获取所有文件夹
                var folders = dbManager.GetAllFolders();

                // 获取根目录的文件
                var rootFiles = dbManager.GetRootMediaFiles();

                // 添加文件夹到项目树
                foreach (var folder in folders)
                {
                    // 获取文件夹 Material Design 图标
                    var (iconKind, iconColor) = originalManager.GetFolderIconKind(folder.Id, false);
                    
                    var folderItem = new ProjectTreeItem
                    {
                        Id = folder.Id,
                        Name = folder.Name,
                        Icon = iconKind,  // 保留用于后备
                        IconKind = iconKind,
                        IconColor = iconColor,
                        Type = TreeItemType.Folder,
                        Path = folder.Path,
                        Children = new ObservableCollection<ProjectTreeItem>()
                    };

                    // 获取文件夹中的文件（添加原图标记图标）
                    var files = dbManager.GetMediaFilesByFolder(folder.Id);
                    foreach (var file in files)
                    {
                        // 获取 Material Design 图标
                        string fileIconKind = "File";
                        string fileIconColor = "#95E1D3";
                        if (file.FileType == FileType.Image)
                        {
                            (fileIconKind, fileIconColor) = originalManager.GetImageIconKind(file.Id);
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

                    projectTreeItems.Add(folderItem);
                }

                // 添加根目录的独立文件
                foreach (var file in rootFiles)
                {
                    // 获取 Material Design 图标
                    string rootFileIconKind = "File";
                    string rootFileIconColor = "#95E1D3";
                    if (file.FileType == FileType.Image)
                    {
                        (rootFileIconKind, rootFileIconColor) = originalManager.GetImageIconKind(file.Id);
                    }
                    
                    projectTreeItems.Add(new ProjectTreeItem
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

                System.Diagnostics.Debug.WriteLine($"📂 加载项目: {folders.Count} 个文件夹, {rootFiles.Count} 个独立文件");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载项目失败: {ex}");
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

        /// <summary>
        /// 加载用户设置 - 从 config.json
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                // 从 ConfigManager 加载原图显示模式
                originalDisplayMode = configManager.OriginalDisplayMode;
                imageProcessor.OriginalDisplayModeValue = originalDisplayMode;
                System.Diagnostics.Debug.WriteLine($"✅ 已加载原图显示模式: {originalDisplayMode}");
                
                // 加载缩放比例
                currentZoom = configManager.ZoomRatio;
                System.Diagnostics.Debug.WriteLine($"✅ 已加载缩放比例: {currentZoom}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 加载设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存用户设置 - 到 config.json
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                // 保存原图显示模式到 ConfigManager
                configManager.OriginalDisplayMode = originalDisplayMode;
                
                // 保存缩放比例
                configManager.ZoomRatio = currentZoom;
                
                System.Diagnostics.Debug.WriteLine($"✅ 已保存设置到 config.json");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 保存设置失败: {ex.Message}");
            }
        }

        #endregion

        #region 顶部菜单栏事件

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            // 创建导入菜单
            var contextMenu = new ContextMenu();
            contextMenu.FontSize = 14;

            // 导入单个文件
            var importFileItem = new MenuItem { Header = "导入单个文件" };
            importFileItem.Click += (s, args) => ImportSingleFile();
            contextMenu.Items.Add(importFileItem);

            // 导入文件夹
            var importFolderItem = new MenuItem { Header = "导入文件夹" };
            importFolderItem.Click += (s, args) => ImportFolder();
            contextMenu.Items.Add(importFolderItem);

            contextMenu.Items.Add(new Separator());

            // 另存图片
            var saveImageItem = new MenuItem { Header = "另存图片" };
            saveImageItem.Click += (s, args) => SaveCurrentImage();
            contextMenu.Items.Add(saveImageItem);

            // 显示菜单
            contextMenu.PlacementTarget = BtnImport;
            contextMenu.IsOpen = true;
        }

        /// <summary>
        /// 导入单个文件
        /// </summary>
        private void ImportSingleFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = ImportManager.GetFileDialogFilter(),
                Title = "选择媒体文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var mediaFile = importManager.ImportSingleFile(openFileDialog.FileName);
                if (mediaFile != null)
                {
                    LoadProjects(); // 刷新项目树
                    LoadSearchScopes(); // 刷新搜索范围
                    ShowStatus($"✅ 已导入: {mediaFile.Name}");
                }
            }
        }

        /// <summary>
        /// 导入文件夹
        /// </summary>
        private void ImportFolder()
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择要导入的文件夹",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var (folder, newFiles, existingFiles) = importManager.ImportFolder(folderDialog.SelectedPath);
                
                if (folder != null)
                {
                    LoadProjects(); // 刷新项目树
                    LoadSearchScopes(); // 刷新搜索范围
                    ShowStatus($"✅ 已导入文件夹: {folder.Name} (新增 {newFiles.Count} 个文件)");
                }
            }
        }

        /// <summary>
        /// 保存当前图片
        /// </summary>
        private void SaveCurrentImage()
        {
            if (imageSaveManager != null)
            {
                imageSaveManager.SaveEffectImage(imagePath);
            }
        }

        /// <summary>
        /// 投影状态改变事件处理
        /// </summary>
        private void OnProjectionStateChanged(object sender, bool isActive)
        {
            Dispatcher.Invoke(() =>
            {
                if (isActive)
                {
                    BtnProjection.Content = "结束";
                    BtnProjection.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 淡绿色
                    ShowStatus("✅ 投影已开启");
                }
                else
                {
                    BtnProjection.Content = "投影";
                    BtnProjection.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // 灰白色
                    ShowStatus("🔴 投影已关闭");
                }
            });
        }

        private void BtnProjection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                projectionManager.ToggleProjection();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"投影操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnSync.IsEnabled = false;
                BtnSync.Content = "同步中...";
                BtnSync.Background = new SolidColorBrush(Colors.LightGreen);

                var (added, removed, updated) = importManager.SyncAllFolders();
                
                LoadProjects(); // 刷新项目树
                LoadSearchScopes(); // 刷新搜索范围
                
                ShowStatus($"🔄 同步完成: 新增 {added}, 删除 {removed}");
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 同步失败: {ex.Message}");
            }
            finally
            {
                BtnSync.IsEnabled = true;
                BtnSync.Content = "同步";
                BtnSync.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ResetView();
        }

        private void BtnOriginal_Click(object sender, RoutedEventArgs e)
        {
            ToggleOriginalMode();
        }
        
        /// <summary>
        /// 切换原图模式
        /// </summary>
        private void ToggleOriginalMode()
        {
            originalMode = !originalMode;
            imageProcessor.OriginalMode = originalMode;
            
            // 更新按钮样式
            if (originalMode)
            {
                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                ShowStatus("✅ 已启用原图模式");
                
                // 在原图模式下,查找相似图片
                if (currentImageId > 0)
                {
                    bool foundSimilar = originalManager.FindSimilarImages(currentImageId);
                    if (foundSimilar)
                    {
                        System.Diagnostics.Debug.WriteLine("✅ 原图模式: 已找到相似图片");
                    }
                }
            }
            else
            {
                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // 默认灰色
                ShowStatus("✅ 已关闭原图模式");
            }
            
            // 重新显示图片
            imageProcessor.UpdateImage();
            
            // 更新投影窗口
            UpdateProjection();
        }

        // 缩放重置按钮已移除

        private void BtnColorEffect_Click(object sender, RoutedEventArgs e)
        {
            ToggleColorEffect();
        }

        #endregion

        #region 关键帧控制栏事件

        private void BtnAddKeyframe_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现添加关键帧
            MessageBox.Show("添加关键帧功能开发中...", "提示");
        }

        private void BtnClearKeyframes_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现清除关键帧
            MessageBox.Show("清除关键帧功能开发中...", "提示");
        }

        private void BtnPrevKeyframe_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现上一个关键帧
            MessageBox.Show("上一个关键帧功能开发中...", "提示");
        }

        private void BtnNextKeyframe_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现下一个关键帧
            MessageBox.Show("下一个关键帧功能开发中...", "提示");
        }

        private void BtnPlayCount_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现播放次数设置
            MessageBox.Show("播放次数设置功能开发中...", "提示");
        }

        private void BtnPlayCount_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // TODO: 实现滚轮调节播放次数
        }

        private void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现时间录制
            MessageBox.Show("时间录制功能开发中...", "提示");
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现自动播放
            MessageBox.Show("自动播放功能开发中...", "提示");
        }

        private void BtnClearTiming_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现清除时间数据
            MessageBox.Show("清除时间数据功能开发中...", "提示");
        }

        private void BtnScript_Click(object sender, RoutedEventArgs e)
        {
            // 临时调试：查询第22张图片信息
            try
            {
                var folders = dbManager.GetAllFolders();
                var debugInfo = new System.Text.StringBuilder();
                
                foreach (var folder in folders)
                {
                    debugInfo.AppendLine($"文件夹: {folder.Name} (ID: {folder.Id})");
                    
                    var files = dbManager.GetMediaFilesByFolder(folder.Id);
                    
                    for (int i = 0; i < files.Count; i++)
                    {
                        var file = files[i];
                        if (i == 21 || file.Name.Contains("22") || file.Name.Contains("生命"))
                        {
                            debugInfo.AppendLine($"  [{i+1}] {file.Name} (OrderIndex={file.OrderIndex})");
                            debugInfo.AppendLine($"      Path: {file.Path}");
                            
                            if (System.IO.File.Exists(file.Path))
                            {
                                try
                                {
                                    using (var img = SixLabors.ImageSharp.Image.Load(file.Path))
                                    {
                                        debugInfo.AppendLine($"      尺寸: {img.Width}x{img.Height}");
                                        
                                        // 计算如果按宽度填满会是多少高度
                                        double canvasWidth = ImageScrollViewer.ActualWidth;
                                        double ratio = canvasWidth / img.Width;
                                        debugInfo.AppendLine($"      画布宽度: {canvasWidth:F0}");
                                        debugInfo.AppendLine($"      缩放比例: {ratio:F3}");
                                        debugInfo.AppendLine($"      预期高度: {img.Height * ratio:F0}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    debugInfo.AppendLine($"      读取失败: {ex.Message}");
                                }
                            }
                            debugInfo.AppendLine();
                        }
                    }
                }
                
                MessageBox.Show(debugInfo.ToString(), "第22张图片信息", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"查询失败: {ex.Message}", "错误");
            }
        }

        private void BtnPauseResume_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现暂停/继续
            MessageBox.Show("暂停/继续功能开发中...", "提示");
        }

        #endregion

        #region 项目树事件

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (searchManager == null) return;

                string searchTerm = SearchBox.Text?.Trim() ?? "";
                string searchScope = (SearchScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";

                System.Diagnostics.Debug.WriteLine($"🔍 搜索: 关键词='{searchTerm}', 范围='{searchScope}'");

                // 如果搜索词为空，重新加载所有项目
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    LoadProjects();
                    return;
                }

                // 执行搜索
                var searchResults = searchManager.SearchProjects(searchTerm, searchScope);
                
                System.Diagnostics.Debug.WriteLine($"📊 搜索结果: {searchResults?.Count ?? 0} 项");

                if (searchResults == null)
                {
                    LoadProjects();
                    return;
                }

                // 更新项目树
                projectTreeItems.Clear();
                foreach (var item in searchResults)
                {
                    projectTreeItems.Add(item);
                }

                ProjectTree.ItemsSource = projectTreeItems;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 搜索失败: {ex}");
                MessageBox.Show($"搜索失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 双击搜索框清空内容
        /// </summary>
        private void SearchBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
            
            // 🆕 新增: 折叠所有展开的文件夹节点
            CollapseAllFolders();
            ShowStatus("✅ 已清除搜索并折叠所有文件夹");
        }

        /// <summary>
        /// 加载搜索范围选项
        /// </summary>
        private void LoadSearchScopes()
        {
            try
            {
                if (searchManager == null) return;

                var scopes = searchManager.GetSearchScopes();
                SearchScope.Items.Clear();
                
                foreach (var scope in scopes)
                {
                    var item = new ComboBoxItem { Content = scope };
                    SearchScope.Items.Add(item);
                }

                // 默认选中"全部"
                if (SearchScope.Items.Count > 0)
                {
                    SearchScope.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载搜索范围失败: {ex}");
            }
        }

        private void ProjectTree_MouseClick(object sender, MouseButtonEventArgs e)
        {
            // 获取点击的项目
            if (e.OriginalSource is FrameworkElement element)
            {
                var treeViewItem = FindParent<TreeViewItem>(element);
                if (treeViewItem != null && treeViewItem.DataContext is ProjectTreeItem selectedItem)
                {
                    // 处理文件夹节点：单击展开/折叠
                    if (selectedItem.Type == TreeItemType.Folder)
                    {
                        // 🆕 新增: 折叠其他所有文件夹节点
                        CollapseOtherFolders(selectedItem);
                        
                        // 切换展开/折叠状态(通过数据绑定的属性,更可靠)
                        selectedItem.IsExpanded = !selectedItem.IsExpanded;
                        
                        // 检查文件夹是否有原图标记,自动开关原图模式
                        bool hasFolderMark = originalManager.CheckOriginalMark(ItemType.Folder, selectedItem.Id);
                        
                        if (hasFolderMark && !originalMode)
                        {
                            // 文件夹有原图标记,自动启用原图模式
                            System.Diagnostics.Debug.WriteLine($"🎯 文件夹有原图标记,自动启用原图模式: {selectedItem.Name}(黄色)");
                            originalMode = true;
                            imageProcessor.OriginalMode = true;
                            BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                            
                            // 🔑 关键修复: 检查当前显示的图片是否属于其他文件夹,如果是则清空显示
                            if (currentImageId > 0 && !string.IsNullOrEmpty(imagePath))
                            {
                                var currentMediaFile = dbManager.GetMediaFileById(currentImageId);
                                if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                                {
                                    // 如果当前图片不属于这个原图文件夹,清空显示
                                    if (currentMediaFile.FolderId.Value != selectedItem.Id)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"🎯 当前图片不属于原图文件夹,清空显示");
                                        ClearImageDisplay();
                                    }
                                }
                            }
                            
                            ShowStatus($"✅ 已启用原图模式: {selectedItem.Name}(黄色)");
                        }
                        else if (!hasFolderMark && originalMode)
                        {
                            // 文件夹没有原图标记,自动关闭原图模式
                            System.Diagnostics.Debug.WriteLine($"🎯 文件夹无原图标记,自动关闭原图模式: {selectedItem.Name}");
                            originalMode = false;
                            imageProcessor.OriginalMode = false;
                            BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // 灰色
                            
                            // 🔑 关键修复: 检查当前显示的图片是否属于其他文件夹,如果是则清空显示
                            if (currentImageId > 0 && !string.IsNullOrEmpty(imagePath))
                            {
                                var currentMediaFile = dbManager.GetMediaFileById(currentImageId);
                                if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                                {
                                    // 如果当前图片不属于这个非原图文件夹,清空显示
                                    if (currentMediaFile.FolderId.Value != selectedItem.Id)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"🎯 当前图片不属于非原图文件夹,清空显示");
                                        ClearImageDisplay();
                                    }
                                }
                            }
                            
                            ShowStatus($"✅ 已关闭原图模式: {selectedItem.Name}");
                        }
                        
                        e.Handled = true; // 阻止默认行为
                    }
                    // 处理文件节点：单击加载
                    else if (selectedItem.Type == TreeItemType.File && !string.IsNullOrEmpty(selectedItem.Path))
                    {
                        // 保存当前图片ID
                        currentImageId = selectedItem.Id;
                        
                        // 🔑 关键优化: 检查文件所在文件夹的原图标记,自动开关原图模式
                        var mediaFile = dbManager.GetMediaFileById(currentImageId);
                        if (mediaFile != null)
                        {
                            bool hasFolderMark = false;
                            if (mediaFile.FolderId.HasValue)
                            {
                                hasFolderMark = originalManager.CheckOriginalMark(ItemType.Folder, mediaFile.FolderId.Value);
                            }
                            
                            if (hasFolderMark && !originalMode)
                            {
                                // 父文件夹有原图标记,自动启用原图模式
                                System.Diagnostics.Debug.WriteLine($"🎯 文件所在文件夹有原图标记,自动启用原图模式");
                                originalMode = true;
                                imageProcessor.OriginalMode = true;
                                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                            }
                            else if (!hasFolderMark && originalMode)
                            {
                                // 父文件夹没有原图标记,自动关闭原图模式
                                System.Diagnostics.Debug.WriteLine($"🎯 文件所在文件夹无原图标记,自动关闭原图模式");
                                originalMode = false;
                                imageProcessor.OriginalMode = false;
                                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // 灰色
                            }
                        }
                        
                        if (System.IO.File.Exists(selectedItem.Path))
                        {
                            // 根据文件类型进行不同处理
                            switch (selectedItem.FileType)
                            {
                                case FileType.Image:
                                    // 加载图片
                                    LoadImage(selectedItem.Path);
                                    ShowStatus($"📷 已加载: {selectedItem.Name}");
                                    break;
                                
                                case FileType.Video:
                                case FileType.Audio:
                                    // TODO: 播放视频/音频
                                    ShowStatus($"🎬 播放媒体文件开发中: {selectedItem.Name}");
                                    break;
                            }
                        }
                        else
                        {
                            ShowStatus($"❌ 文件不存在: {selectedItem.Name}");
                        }
                    }
                }
            }
        }

        private void ProjectTree_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 获取右键点击的项目
            if (e.OriginalSource is FrameworkElement element)
            {
                var treeViewItem = FindParent<TreeViewItem>(element);
                if (treeViewItem != null && treeViewItem.DataContext is ProjectTreeItem item)
                {
                    // 创建右键菜单
                    var contextMenu = new ContextMenu();
                    contextMenu.FontSize = 14;

                    if (item.Type == TreeItemType.Folder)
                    {
                        // 文件夹右键菜单
                        
                        // 文件夹原图标记菜单
                        bool hasFolderMark = originalManager.CheckOriginalMark(ItemType.Folder, item.Id);
                        
                        if (hasFolderMark)
                        {
                            // 如果已有标记,显示"取消原图"
                            var unmarkFolderItem = new MenuItem { Header = "取消原图" };
                            unmarkFolderItem.Click += (s, args) => UnmarkOriginalFolder(item);
                            contextMenu.Items.Add(unmarkFolderItem);
                        }
                        else
                        {
                            // 如果没有标记,显示原图标记选项
                            var markFolderMenuItem = new MenuItem { Header = "标记为原图" };
                            
                            // 循环模式
                            var loopFolderItem = new MenuItem { Header = "循环模式" };
                            loopFolderItem.Click += (s, args) => MarkFolderAsOriginal(item, MarkType.Loop);
                            markFolderMenuItem.Items.Add(loopFolderItem);
                            
                            // 顺序模式
                            var sequenceFolderItem = new MenuItem { Header = "顺序模式" };
                            sequenceFolderItem.Click += (s, args) => MarkFolderAsOriginal(item, MarkType.Sequence);
                            markFolderMenuItem.Items.Add(sequenceFolderItem);
                            
                            contextMenu.Items.Add(markFolderMenuItem);
                        }
                        
                        contextMenu.Items.Add(new Separator());
                        
                        var deleteItem = new MenuItem { Header = "删除文件夹" };
                        deleteItem.Click += (s, args) => DeleteFolder(item);
                        contextMenu.Items.Add(deleteItem);

                        var syncItem = new MenuItem { Header = "同步文件夹" };
                        syncItem.Click += (s, args) => SyncFolder(item);
                        contextMenu.Items.Add(syncItem);
                    }
                    else if (item.Type == TreeItemType.File)
                    {
                        // 文件右键菜单
                        
                        // 原图标记菜单
                        if (item.FileType == FileType.Image)
                        {
                            bool hasOriginalMark = originalManager.CheckOriginalMark(ItemType.Image, item.Id);
                            
                            if (hasOriginalMark)
                            {
                                // 如果已有标记,显示"取消原图"
                                var unmarkItem = new MenuItem { Header = "取消原图" };
                                unmarkItem.Click += (s, args) => UnmarkOriginal(item);
                                contextMenu.Items.Add(unmarkItem);
                            }
                            else
                            {
                                // 如果没有标记,显示原图标记选项
                                var markMenuItem = new MenuItem { Header = "标记为原图" };
                                
                                // 循环模式
                                var loopItem = new MenuItem { Header = "循环模式" };
                                loopItem.Click += (s, args) => MarkAsOriginal(item, MarkType.Loop);
                                markMenuItem.Items.Add(loopItem);
                                
                                // 顺序模式
                                var sequenceItem = new MenuItem { Header = "顺序模式" };
                                sequenceItem.Click += (s, args) => MarkAsOriginal(item, MarkType.Sequence);
                                markMenuItem.Items.Add(sequenceItem);
                                
                                contextMenu.Items.Add(markMenuItem);
                            }
                            
                            contextMenu.Items.Add(new Separator());
                        }
                        
                        var deleteItem = new MenuItem { Header = "删除文件" };
                        deleteItem.Click += (s, args) => DeleteFile(item);
                        contextMenu.Items.Add(deleteItem);
                    }

                    contextMenu.IsOpen = true;
                }
            }
        }

        /// <summary>
        /// 删除文件夹
        /// </summary>
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
                dbManager.DeleteFolder(item.Id);
                LoadProjects();           // 刷新项目树
                LoadSearchScopes();       // 刷新搜索范围
                ShowStatus($"🗑️ 已删除文件夹: {item.Name}");
            }
        }

        /// <summary>
        /// 同步文件夹
        /// </summary>
        private void SyncFolder(ProjectTreeItem item)
        {
            var (added, removed, updated) = importManager.SyncFolder(item.Id);
            LoadProjects();
            ShowStatus($"🔄 同步完成: {item.Name} (新增 {added}, 删除 {removed})");
        }

        /// <summary>
        /// 标记文件夹为原图
        /// </summary>
        private void MarkFolderAsOriginal(ProjectTreeItem item, MarkType markType)
        {
            bool success = originalManager.AddOriginalMark(ItemType.Folder, item.Id, markType);
            
            if (success)
            {
                string modeText = markType == MarkType.Loop ? "循环" : "顺序";
                ShowStatus($"✅ 已标记文件夹为原图({modeText}): {item.Name}");
                
                // 立即刷新项目树显示
                LoadProjects();
            }
            else
            {
                ShowStatus($"❌ 标记文件夹失败: {item.Name}");
            }
        }

        /// <summary>
        /// 取消文件夹原图标记
        /// </summary>
        private void UnmarkOriginalFolder(ProjectTreeItem item)
        {
            bool success = originalManager.RemoveOriginalMark(ItemType.Folder, item.Id);
            
            if (success)
            {
                ShowStatus($"✅ 已取消文件夹原图标记: {item.Name}");
                
                // 刷新项目树显示
                LoadProjects();
            }
            else
            {
                ShowStatus($"❌ 取消文件夹标记失败: {item.Name}");
            }
        }

        /// <summary>
        /// 标记为原图
        /// </summary>
        private void MarkAsOriginal(ProjectTreeItem item, MarkType markType)
        {
            bool success = originalManager.AddOriginalMark(ItemType.Image, item.Id, markType);
            
            if (success)
            {
                string modeText = markType == MarkType.Loop ? "循环" : "顺序";
                ShowStatus($"✅ 已标记为原图({modeText}): {item.Name}");
                
                // 立即刷新项目树显示
                LoadProjects();
                
                // 如果标记的是当前正在显示的图片,自动启用原图模式
                if (currentImageId == item.Id && !originalMode)
                {
                    System.Diagnostics.Debug.WriteLine($"🎯 自动启用原图模式: {item.Name}");
                    originalMode = true;
                    imageProcessor.OriginalMode = true;
                    
                    // 更新按钮样式
                    BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                    
                    // 查找相似图片
                    originalManager.FindSimilarImages(currentImageId);
                    
                    // 重新显示图片
                    imageProcessor.UpdateImage();
                    
                    // 更新投影窗口
                    UpdateProjection();
                    
                    ShowStatus("✅ 已自动启用原图模式");
                }
            }
            else
            {
                ShowStatus($"❌ 标记失败: {item.Name}");
            }
        }

        /// <summary>
        /// 取消原图标记
        /// </summary>
        private void UnmarkOriginal(ProjectTreeItem item)
        {
            bool success = originalManager.RemoveOriginalMark(ItemType.Image, item.Id);
            
            if (success)
            {
                ShowStatus($"✅ 已取消原图标记: {item.Name}");
                
                // 立即刷新项目树显示
                LoadProjects();
                
                // 如果取消的是当前正在显示的图片,关闭原图模式
                if (currentImageId == item.Id && originalMode)
                {
                    System.Diagnostics.Debug.WriteLine($"🎯 自动关闭原图模式: {item.Name}");
                    originalMode = false;
                    imageProcessor.OriginalMode = false;
                    
                    // 更新按钮样式
                    BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // 灰色
                    
                    // 重新显示图片
                    imageProcessor.UpdateImage();
                    
                    // 更新投影窗口
                    UpdateProjection();
                    
                    ShowStatus("✅ 已自动关闭原图模式");
                }
            }
            else
            {
                ShowStatus($"❌ 取消标记失败: {item.Name}");
            }
        }

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
                dbManager.DeleteMediaFile(item.Id);
                LoadProjects();
                ShowStatus($"🗑️ 已删除文件: {item.Name}");
            }
        }

        /// <summary>
        /// 查找父级元素
        /// </summary>
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        /// <summary>
        /// 折叠所有文件夹节点
        /// </summary>
        private void CollapseAllFolders()
        {
            try
            {
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                foreach (var item in treeItems)
                {
                    if (item.Type == TreeItemType.Folder)
                    {
                        CollapseFolder(item);
                    }
                }
                System.Diagnostics.Debug.WriteLine("📁 已折叠所有文件夹节点");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"折叠所有文件夹失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 折叠除指定文件夹外的所有其他文件夹
        /// </summary>
        private void CollapseOtherFolders(ProjectTreeItem exceptFolder)
        {
            try
            {
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                foreach (var item in treeItems)
                {
                    if (item.Type == TreeItemType.Folder && item.Id != exceptFolder.Id)
                    {
                        CollapseFolder(item);
                    }
                }
                System.Diagnostics.Debug.WriteLine($"📁 已折叠除 {exceptFolder.Name} 外的所有文件夹");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"折叠其他文件夹失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 递归折叠文件夹及其子文件夹
        /// </summary>
        private void CollapseFolder(ProjectTreeItem folder)
        {
            if (folder == null) return;
            
            // 折叠当前文件夹
            folder.IsExpanded = false;
            
            // 递归折叠子文件夹
            if (folder.Children != null)
            {
                foreach (var child in folder.Children)
                {
                    if (child.Type == TreeItemType.Folder)
                    {
                        CollapseFolder(child);
                    }
                }
            }
        }

        #endregion

        #region 图像处理核心功能

        private void LoadImage(string path)
        {
            try
            {
                imagePath = path;
                
                // 使用ImageProcessor加载图片
                bool success = imageProcessor.LoadImage(path);
                
                if (success)
                {
                    // 更新原图引用（向后兼容）
                    originalImage?.Dispose();
                    currentImage?.Dispose();
                    originalImage = imageProcessor.OriginalImage?.Clone();
                    currentImage = imageProcessor.CurrentImage?.Clone();
                    
                    DetectBackground();
                    
                    // ⭐ 关键逻辑: 检查当前图片是否有原图标记,自动启用/关闭原图模式
                    if (currentImageId > 0)
                    {
                        bool shouldUseOriginal = originalManager.ShouldUseOriginalMode(currentImageId);
                        
                        if (shouldUseOriginal && !originalMode)
                        {
                            // 图片有原图标记,但原图模式未启用 -> 自动启用
                            System.Diagnostics.Debug.WriteLine($"🎯 自动启用原图模式: 图片ID={currentImageId}");
                            originalMode = true;
                            imageProcessor.OriginalMode = true;
                            
                            // 更新按钮样式
                            BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                            
                            // 查找相似图片
                            originalManager.FindSimilarImages(currentImageId);
                            
                            ShowStatus("✅ 已自动启用原图模式");
                        }
                        else if (!shouldUseOriginal && originalMode)
                        {
                            // 图片没有原图标记,但原图模式已启用 -> 保持原图模式(不自动关闭)
                            // 用户可能在浏览一组原图,中途打开了非原图,应该保持原图模式
                            System.Diagnostics.Debug.WriteLine($"ℹ️ 保持原图模式: 图片ID={currentImageId}");
                        }
                        
                        // 🌲 同步项目树选中状态
                        SelectTreeItemById(currentImageId);
                    }
                    
                    // 如果颜色效果已启用，应用效果
                    if (isColorEffectEnabled)
                    {
                        ApplyColorEffect();
                    }
                    
                    // 更新投影
                    UpdateProjection();
                    
                    ShowStatus($"✅ 已加载：{Path.GetFileName(path)}");
                }
                else
                {
                    throw new Exception("图片加载失败");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开图片: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStatus("❌ 加载失败");
            }
        }

        /// <summary>
        /// 清空图片显示
        /// </summary>
        private void ClearImageDisplay()
        {
            try
            {
                // 清空图片路径
                imagePath = null;
                currentImageId = 0;
                
                // 清空图片对象
                originalImage?.Dispose();
                currentImage?.Dispose();
                originalImage = null;
                currentImage = null;
                
                // 清空ImageProcessor
                imageProcessor.ClearCurrentImage();
                
                // 重置缩放
                currentZoom = 1.0;
                
                ShowStatus("✅ 已清空图片显示");
                System.Diagnostics.Debug.WriteLine("🎯 已清空图片显示");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"清空图片显示失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在项目树中选中指定ID的节点
        /// </summary>
        private void SelectTreeItemById(int itemId)
        {
            try
            {
                // 递归查找并选中节点
                var treeItems = ProjectTree.Items.Cast<ProjectTreeItem>();
                SelectTreeItemRecursive(treeItems, itemId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"选中项目树节点失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 递归查找并选中树节点
        /// </summary>
        private bool SelectTreeItemRecursive(IEnumerable<ProjectTreeItem> items, int targetId)
        {
            foreach (var item in items)
            {
                if (item.Id == targetId && item.Type == TreeItemType.File)
                {
                    // 找到目标节点,设置为选中状态
                    item.IsSelected = true;
                    
                    // 确保父节点展开
                    ExpandParentNodes(item);
                    
                    System.Diagnostics.Debug.WriteLine($"✅ 已选中项目树节点: {item.Name}");
                    return true;
                }
                
                // 递归查找子节点
                if (item.Children != null && item.Children.Count > 0)
                {
                    if (SelectTreeItemRecursive(item.Children, targetId))
                    {
                        // 如果在子节点中找到,展开当前节点
                        item.IsExpanded = true;
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// 展开父节点
        /// </summary>
        private void ExpandParentNodes(ProjectTreeItem item)
        {
            // 在WPF TreeView中,需要通过递归查找父节点
            // 这里简化处理:直接展开所有节点路径
            var allItems = ProjectTree.Items.Cast<ProjectTreeItem>();
            ExpandParentNodesRecursive(allItems, item);
        }

        /// <summary>
        /// 递归展开父节点
        /// </summary>
        private bool ExpandParentNodesRecursive(IEnumerable<ProjectTreeItem> items, ProjectTreeItem target)
        {
            foreach (var item in items)
            {
                if (item == target)
                {
                    return true;
                }
                
                if (item.Children != null && item.Children.Count > 0)
                {
                    if (ExpandParentNodesRecursive(item.Children, target))
                    {
                        item.IsExpanded = true;
                        return true;
                    }
                }
            }
            
            return false;
        }

        private void DetectBackground()
        {
            if (originalImage == null) return;

            int width = originalImage.Width;
            int height = originalImage.Height;

            // 检测四个角的颜色
            var corners = new[]
            {
                originalImage[0, 0],
                originalImage[width - 1, 0],
                originalImage[0, height - 1],
                originalImage[width - 1, height - 1]
            };

            double avgBrightness = 0;
            foreach (var corner in corners)
            {
                avgBrightness += (corner.R + corner.G + corner.B) / 3.0;
            }
            avgBrightness /= corners.Length;

            if (avgBrightness > 127)
            {
                backgroundType = BackgroundType.White;
            }
            else
            {
                backgroundType = BackgroundType.Black;
            }
        }

        private void ToggleColorEffect()
        {
            if (imageProcessor.CurrentImage == null)
            {
                MessageBox.Show("请先打开图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 切换变色效果状态
            imageProcessor.IsInverted = !imageProcessor.IsInverted;
            isColorEffectEnabled = imageProcessor.IsInverted;
            
            // 更新按钮样式
            if (isColorEffectEnabled)
            {
                BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                ShowStatus("✨ 已启用颜色效果");
            }
            else
            {
                BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // 默认灰色
                ShowStatus("✅ 已关闭颜色效果");
            }
            
            // 通过ImageProcessor的UpdateImage来更新显示（包含完整的缩放、居中逻辑）
            imageProcessor.UpdateImage();
            
            // 更新投影
            UpdateProjection();
        }

        private void ApplyColorEffect()
        {
            if (originalImage == null) return;

            try
            {
                ShowStatus("⏳ GPU处理中...");
                
                currentImage?.Dispose();
                currentImage = gpuProcessor.ProcessImage(
                    originalImage, 
                    currentTargetColor, 
                    backgroundType == BackgroundType.White
                );
                
                DisplayImage(currentImage);
                ShowStatus($"✨ 已应用颜色效果 (GPU加速)");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"处理失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowStatus("❌ 处理失败");
            }
        }

        private void OpenColorPicker()
        {
            using (var colorDialog = new System.Windows.Forms.ColorDialog())
            {
                // 设置当前颜色
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    currentTargetColor.R, 
                    currentTargetColor.G, 
                    currentTargetColor.B);
                
                colorDialog.AllowFullOpen = true;
                colorDialog.FullOpen = true;

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var selectedColor = colorDialog.Color;
                    currentTargetColor = new Rgba32(selectedColor.R, selectedColor.G, selectedColor.B);
                    
                    // 如果颜色效果已启用，清除缓存并更新显示
                    if (isColorEffectEnabled)
                    {
                        imageProcessor.ClearCache();
                        imageProcessor.UpdateImage();
                    }
                    
                    ShowStatus($"✨ 已设置自定义颜色: RGB({selectedColor.R}, {selectedColor.G}, {selectedColor.B})");
                }
            }
        }

        private void DisplayImage(Image<Rgba32> image)
        {
            if (image == null) return;

            using (var memoryStream = new MemoryStream())
            {
                image.SaveAsPng(memoryStream);
                memoryStream.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                ImageDisplay.Source = bitmapImage;
                
                // 自动适应显示区域
                FitImageToView();
            }
        }

        #endregion

        #region 图片缩放功能

        private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ImageDisplay.Source == null) return;

            // Ctrl+滚轮 = 缩放
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;

                double delta = e.Delta / 120.0 * 0.05;
                SetZoom(currentZoom + delta);
            }
        }

        // 缩放按钮已移除，缩放功能通过Ctrl+滚轮和双击实现

        private void ResetZoom()
        {
            if (ImageDisplay.Source == null) return;
            
            // 使用ImageProcessor的ResetZoom方法
            imageProcessor?.ResetZoom();
            
            ImageScrollViewer.ScrollToHorizontalOffset(0);
            ImageScrollViewer.ScrollToVerticalOffset(0);
        }

        private void FitImageToView()
        {
            if (ImageDisplay.Source == null) return;
            
            // 使用ImageProcessor的FitToView方法
            imageProcessor?.FitToView();
        }

        private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ImageDisplay.Source != null && currentZoom <= 1.0)
            {
                FitImageToView();
            }
        }

        private void SetZoom(double zoom)
        {
            currentZoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
            ImageScaleTransform.ScaleX = currentZoom;
            ImageScaleTransform.ScaleY = currentZoom;
        }

        #endregion

        #region 图片拖动功能

        private void ImageDisplay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
            {
                ResetZoom();
            }
        }

        private void ImageDisplay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 鼠标中键点击切换原图显示模式(仅在原图模式下有效)
            if (e.ChangedButton == MouseButton.Middle && originalMode)
            {
                ToggleOriginalDisplayMode();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 切换原图显示模式(拉伸/适中)
        /// </summary>
        private void ToggleOriginalDisplayMode()
        {
            if (originalDisplayMode == OriginalDisplayMode.Stretch)
            {
                originalDisplayMode = OriginalDisplayMode.Fit;
                ShowStatus("✅ 原图模式: 适中显示");
            }
            else
            {
                originalDisplayMode = OriginalDisplayMode.Stretch;
                ShowStatus("✅ 原图模式: 拉伸显示");
            }
            
            // 更新ImageProcessor的显示模式
            imageProcessor.OriginalDisplayModeValue = originalDisplayMode;
            
            // 重新显示图片
            imageProcessor.UpdateImage();
            
            // 更新投影窗口
            UpdateProjection();
            
            // 保存设置到数据库
            SaveSettings();
        }

        private void ImageDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (currentZoom > 1.0)
            {
                isDragging = true;
                dragStartPoint = e.GetPosition(ImageScrollViewer);
                ImageDisplay.Cursor = System.Windows.Input.Cursors.SizeAll;
                ImageDisplay.CaptureMouse();
            }
        }

        private void ImageDisplay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isDragging)
            {
                isDragging = false;
                ImageDisplay.Cursor = System.Windows.Input.Cursors.Hand;
                ImageDisplay.ReleaseMouseCapture();
            }
        }

        private void ImageDisplay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPoint = e.GetPosition(ImageScrollViewer);
                var offset = currentPoint - dragStartPoint;

                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset - offset.X);
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset - offset.Y);

                dragStartPoint = currentPoint;
            }
        }

        #endregion

        #region 媒体播放器事件

        private void BtnMediaPrev_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现上一首
        }

        private void BtnMediaPlayPause_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现播放/暂停
        }

        private void BtnMediaNext_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现下一首
        }

        private void BtnMediaStop_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现停止
        }

        private void MediaProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // TODO: 实现进度控制
        }

        private void BtnPlayMode_Click(object sender, RoutedEventArgs e)
        {
            // TODO: 实现播放模式切换
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // TODO: 实现音量控制
        }

        #endregion

        #region 辅助方法

        private void ResetView()
        {
            ResetZoom();
            ShowStatus("✅ 视图已重置");
        }

        private void ShowStatus(string message)
        {
            // TODO: 实现状态栏显示
            Title = $"Canvas Cast V2.5.5 - {message}";
        }

        public Rgba32 GetCurrentTargetColor()
        {
            return currentTargetColor;
        }

        protected override void OnClosed(EventArgs e)
        {
            imageProcessor?.Dispose();
            originalImage?.Dispose();
            currentImage?.Dispose();
            gpuProcessor?.Dispose();
            base.OnClosed(e);
        }

        #endregion

        #region 右键菜单

        private void ImageScrollViewer_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (imageProcessor.CurrentImage == null)
                return;

            // 创建右键菜单
            var contextMenu = new ContextMenu();
            contextMenu.FontSize = 14;

            // 变色颜色子菜单
            var colorMenuItem = new MenuItem { Header = "变色颜色" };
            
            // 预设颜色选项（与Python版本一致）
            var presetColors = new[]
            {
                new { Name = "淡黄", Color = new Rgba32(174, 159, 112) },
                new { Name = "纯黄", Color = new Rgba32(255, 255, 0) },
                new { Name = "秋麒麟", Color = new Rgba32(218, 165, 32) },
                new { Name = "晒黑", Color = new Rgba32(210, 180, 140) },
                new { Name = "结实的树", Color = new Rgba32(222, 184, 135) },
                new { Name = "沙棕色", Color = new Rgba32(244, 164, 96) },
                new { Name = "纯白", Color = new Rgba32(255, 255, 255) }
            };

            foreach (var preset in presetColors)
            {
                var menuItem = new MenuItem 
                { 
                    Header = preset.Name,
                    IsCheckable = true,
                    IsChecked = currentTargetColor.R == preset.Color.R && 
                               currentTargetColor.G == preset.Color.G && 
                               currentTargetColor.B == preset.Color.B
                };
                menuItem.Click += (s, args) =>
                {
                    currentTargetColor = preset.Color;
                    if (isColorEffectEnabled)
                    {
                        // 如果颜色效果已启用，清除缓存并更新显示
                        imageProcessor.ClearCache();
                        imageProcessor.UpdateImage();
                    }
                    ShowStatus($"✨ 已切换颜色: {preset.Name}");
                };
                colorMenuItem.Items.Add(menuItem);
            }

            // 添加分隔线
            colorMenuItem.Items.Add(new Separator());

            // 自定义颜色
            var customColorItem = new MenuItem { Header = "自定义颜色..." };
            customColorItem.Click += (s, args) => OpenColorPicker();
            colorMenuItem.Items.Add(customColorItem);

            contextMenu.Items.Add(colorMenuItem);

            // 原图模式显示切换菜单(仅在原图模式下显示)
            if (originalMode)
            {
                contextMenu.Items.Add(new Separator());
                
                var displayModeMenuItem = new MenuItem { Header = "原图模式" };
                
                // 拉伸模式
                var stretchItem = new MenuItem 
                { 
                    Header = "拉伸", 
                    IsCheckable = true,
                    IsChecked = originalDisplayMode == OriginalDisplayMode.Stretch
                };
                stretchItem.Click += (s, args) =>
                {
                    if (originalDisplayMode != OriginalDisplayMode.Stretch)
                    {
                        originalDisplayMode = OriginalDisplayMode.Stretch;
                        imageProcessor.OriginalDisplayModeValue = originalDisplayMode;
                        imageProcessor.UpdateImage();
                        UpdateProjection();
                        ShowStatus("✅ 原图模式: 拉伸显示");
                    }
                };
                displayModeMenuItem.Items.Add(stretchItem);
                
                // 适中模式
                var fitItem = new MenuItem 
                { 
                    Header = "适中", 
                    IsCheckable = true,
                    IsChecked = originalDisplayMode == OriginalDisplayMode.Fit
                };
                fitItem.Click += (s, args) =>
                {
                    if (originalDisplayMode != OriginalDisplayMode.Fit)
                    {
                        originalDisplayMode = OriginalDisplayMode.Fit;
                        imageProcessor.OriginalDisplayModeValue = originalDisplayMode;
                        imageProcessor.UpdateImage();
                        UpdateProjection();
                        ShowStatus("✅ 原图模式: 适中显示");
                    }
                };
                displayModeMenuItem.Items.Add(fitItem);
                
                contextMenu.Items.Add(displayModeMenuItem);
            }

            // 显示菜单
            contextMenu.IsOpen = true;
        }

        #endregion

        #region 窗口事件处理

        /// <summary>
        /// 窗口关闭事件 - 清理资源
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔚 主窗口正在关闭,清理资源...");
                
                // 保存用户设置
                SaveSettings();
                
                // 关闭投影窗口
                if (projectionManager != null)
                {
                    projectionManager.CloseProjection();
                    projectionManager.Dispose();
                    System.Diagnostics.Debug.WriteLine("✅ 投影管理器已清理");
                }
                
                // 释放图片资源
                originalImage?.Dispose();
                currentImage?.Dispose();
                
                System.Diagnostics.Debug.WriteLine("✅ 资源清理完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 资源清理失败: {ex.Message}");
            }
        }

        #endregion

        #region 键盘事件处理

        /// <summary>
        /// 主窗口键盘事件处理
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ESC键: 关闭投影(优先级最高,不论是否原图模式)
            if (e.Key == Key.Escape)
            {
                if (projectionManager != null)
                {
                    bool wasClosed = projectionManager.CloseProjection();
                    if (wasClosed)
                    {
                        System.Diagnostics.Debug.WriteLine("⌨️ 主窗口热键: ESC - 已关闭投影");
                        e.Handled = true;
                        return;
                    }
                }
            }
            
            // 原图模式下的相似图片切换
            if (originalMode && currentImageId > 0)
            {
                bool handled = false;
                
                switch (e.Key)
                {
                    case Key.PageUp:
                        // 切换到上一张相似图片
                        handled = SwitchSimilarImage(false);
                        break;
                        
                    case Key.PageDown:
                        // 切换到下一张相似图片
                        handled = SwitchSimilarImage(true);
                        break;
                }
                
                if (handled)
                {
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 切换相似图片
        /// </summary>
        private bool SwitchSimilarImage(bool isNext)
        {
            System.Diagnostics.Debug.WriteLine($"🔄 SwitchSimilarImage 被调用: isNext={isNext}, currentImageId={currentImageId}");
            
            var result = originalManager.SwitchSimilarImage(isNext, currentImageId);
            
            System.Diagnostics.Debug.WriteLine($"🔄 SwitchSimilarImage 结果: success={result.success}, newImageId={result.newImageId}");
            
            if (result.success && result.newImageId.HasValue)
            {
                currentImageId = result.newImageId.Value;
                LoadImage(result.newImagePath);
                
                string direction = isNext ? "下一张" : "上一张";
                ShowStatus($"✅ 已切换到{direction}相似图片: {Path.GetFileName(result.newImagePath)}");
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// 切换到下一张相似图片 (公共方法,供投影窗口调用)
        /// </summary>
        public void SwitchToNextSimilarImage()
        {
            // 如果当前在原图模式下,确保已查找相似图片
            if (originalMode && currentImageId > 0)
            {
                // 检查是否需要重新查找相似图片
                if (!originalManager.HasSimilarImages())
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 相似图片列表为空,重新查找...");
                    originalManager.FindSimilarImages(currentImageId);
                }
            }
            
            SwitchSimilarImage(true);
        }

        /// <summary>
        /// 切换到上一张相似图片 (公共方法,供投影窗口调用)
        /// </summary>
        public void SwitchToPreviousSimilarImage()
        {
            // 如果当前在原图模式下,确保已查找相似图片
            if (originalMode && currentImageId > 0)
            {
                // 检查是否需要重新查找相似图片
                if (!originalManager.HasSimilarImages())
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 相似图片列表为空,重新查找...");
                    originalManager.FindSimilarImages(currentImageId);
                }
            }
            
            SwitchSimilarImage(false);
        }

        #endregion
    }

    #region 数据模型

    public class ProjectTreeItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public string IconKind { get; set; }  // Material Design 图标类型
        public string IconColor { get; set; } = "#666666";  // 图标颜色
        public TreeItemType Type { get; set; }
        public string Path { get; set; }
        public FileType FileType { get; set; }
        public ObservableCollection<ProjectTreeItem> Children { get; set; } = new ObservableCollection<ProjectTreeItem>();

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum TreeItemType
    {
        Project,
        Folder,
        File,
        Image,
        Video,
        Audio
    }

    #endregion
}
