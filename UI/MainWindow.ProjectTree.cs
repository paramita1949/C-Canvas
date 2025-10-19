using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;

// 引入用于项目树操作的命名空间
using TreeViewItem = System.Windows.Controls.TreeViewItem;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的项目树事件处理部分
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 项目树事件

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_searchManager == null) return;

                string searchTerm = SearchBox.Text?.Trim() ?? "";
                string searchScope = (SearchScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";

                // System.Diagnostics.Debug.WriteLine($"🔍 搜索: 关键词='{searchTerm}', 范围='{searchScope}'");

                // 如果搜索词为空，重新加载所有项目
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    LoadProjects();
                    return;
                }

                // 执行搜索
                var searchResults = _searchManager.SearchProjects(searchTerm, searchScope);
                
                // System.Diagnostics.Debug.WriteLine($"📊 搜索结果: {searchResults?.Count ?? 0} 项");

                if (searchResults == null)
                {
                    LoadProjects();
                    return;
                }

                // 🔧 修复：搜索结果需要同时更新 _projectTreeItems 和 _filteredProjectTreeItems
                _projectTreeItems.Clear();
                _filteredProjectTreeItems.Clear();
                
                foreach (var item in searchResults)
                {
                    _projectTreeItems.Add(item);
                    _filteredProjectTreeItems.Add(item); // 🔑 关键：搜索结果直接显示，不需要过滤
                }

                // 不需要重新设置ItemsSource，ObservableCollection会自动通知UI更新
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 搜索失败: {ex}");
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
                if (_searchManager == null) return;

                var scopes = _searchManager.GetSearchScopes();
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
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"加载搜索范围失败: {ex}");
            }
        }

        private async void ProjectTree_MouseClick(object sender, MouseButtonEventArgs e)
        {
            // 获取点击的项目
            if (e.OriginalSource is FrameworkElement element)
            {
                var treeViewItem = FindParent<TreeViewItem>(element);
                if (treeViewItem != null && treeViewItem.DataContext is ProjectTreeItem selectedItem)
                {
                    // 🆕 处理文本项目节点：单击加载项目
                    if (selectedItem.Type == TreeItemType.Project || selectedItem.Type == TreeItemType.TextProject)
                    {
                        int projectId = selectedItem.Id;
                        _ = LoadTextProjectAsync(projectId);
                        return;
                    }

                    // 处理文件夹节点：单击展开/折叠
                    if (selectedItem.Type == TreeItemType.Folder)
                    {
                        // 🆕 只在非文本编辑模式下才自动切换回图片模式
                        // 如果正在编辑幻灯片，不应该自动退出
                        if (TextEditorPanel.Visibility != Visibility.Visible)
                        {
                            AutoExitTextEditorIfNeeded();
                        }
                        
                        // 🆕 新增: 折叠其他所有文件夹节点
                        CollapseOtherFolders(selectedItem);
                        
                        // 切换展开/折叠状态(通过数据绑定的属性,更可靠)
                        selectedItem.IsExpanded = !selectedItem.IsExpanded;
                        
                        // 检查文件夹是否有原图标记,自动开关原图模式
                        bool hasFolderMark = _originalManager.CheckOriginalMark(ItemType.Folder, selectedItem.Id);
                        
                        if (hasFolderMark && !_originalMode)
                        {
                            // 文件夹有原图标记,自动启用原图模式
                            //System.Diagnostics.Debug.WriteLine($"🎯 文件夹有原图标记,自动启用原图模式: {selectedItem.Name}(黄色)");
                            _originalMode = true;
                            _imageProcessor.OriginalMode = true;
                            BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                            
                            // 🔑 关键修复: 检查当前显示的图片是否属于其他文件夹,如果是则清空显示
                            if (_currentImageId > 0 && !string.IsNullOrEmpty(_imagePath))
                            {
                                var currentMediaFile = _dbManager.GetMediaFileById(_currentImageId);
                                if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                                {
                                    // 如果当前图片不属于这个原图文件夹,清空显示
                                    if (currentMediaFile.FolderId.Value != selectedItem.Id)
                                    {
                                        //System.Diagnostics.Debug.WriteLine($"🎯 当前图片不属于原图文件夹,清空显示");
                                        ClearImageDisplay();
                                    }
                                }
                            }
                            
                            ShowStatus($"✅ 已启用原图模式: {selectedItem.Name}(黄色)");
                        }
                        else if (!hasFolderMark && _originalMode)
                        {
                            // 文件夹没有原图标记,自动关闭原图模式
                            //System.Diagnostics.Debug.WriteLine($"🎯 文件夹无原图标记,自动关闭原图模式: {selectedItem.Name}");
                            _originalMode = false;
                            _imageProcessor.OriginalMode = false;
                            BtnOriginal.Background = Brushes.Transparent; // 使用透明背景，让样式生效
                            
                            // 🔑 关键修复: 检查当前显示的图片是否属于其他文件夹,如果是则清空显示
                            if (_currentImageId > 0 && !string.IsNullOrEmpty(_imagePath))
                            {
                                var currentMediaFile = _dbManager.GetMediaFileById(_currentImageId);
                                if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                                {
                                    // 如果当前图片不属于这个非原图文件夹,清空显示
                                    if (currentMediaFile.FolderId.Value != selectedItem.Id)
                                    {
                                        //System.Diagnostics.Debug.WriteLine($"🎯 当前图片不属于非原图文件夹,清空显示");
                                        ClearImageDisplay();
                                    }
                                }
                            }
                            
                            ShowStatus($"✅ 已关闭原图模式: {selectedItem.Name}");
                        }
                        
                        // 🎨 变色功能逻辑：只在切换到不同文件夹时才自动调整变色状态
                        bool isSameFolder = (_currentFolderId == selectedItem.Id);
                        
                        if (!isSameFolder)
                        {
                            // 切换到不同文件夹：检查标记并自动调整变色状态
                            bool hasColorEffectMark = _dbManager.HasFolderAutoColorEffect(selectedItem.Id);
                            
                            if (hasColorEffectMark && !_isColorEffectEnabled)
                            {
                                // 文件夹有变色标记，只更新 MainWindow 状态（不触发 ImageProcessor）
                                //System.Diagnostics.Debug.WriteLine($"🎨 文件夹有变色标记，更新UI状态: {selectedItem.Name}");
                                _isColorEffectEnabled = true;
                                // ⚠️ 关键：不设置 _imageProcessor.IsInverted，因为它的 setter 会自动调用 UpdateImage()
                                // 只在 LoadImage() 时才同步状态到 ImageProcessor
                                BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // 金色
                                
                                ShowStatus($"✅ 已切换到变色文件夹: {selectedItem.Name}");
                            }
                            else if (!hasColorEffectMark && _isColorEffectEnabled)
                            {
                                // 文件夹没有变色标记，只更新 MainWindow 状态（不触发 ImageProcessor）
                                //System.Diagnostics.Debug.WriteLine($"🎨 文件夹无变色标记，更新UI状态: {selectedItem.Name}");
                                _isColorEffectEnabled = false;
                                // ⚠️ 关键：不设置 _imageProcessor.IsInverted，因为它的 setter 会自动调用 UpdateImage()
                                // 只在 LoadImage() 时才同步状态到 ImageProcessor
                                BtnColorEffect.Background = Brushes.Transparent;
                                
                                ShowStatus($"✅ 已切换到无变色文件夹: {selectedItem.Name}");
                            }
                            
                            // 🎯 更新当前文件夹ID
                            _currentFolderId = selectedItem.Id;
                        }
                        // else: 重复点击同一文件夹，保持变色状态不变
                        
                        e.Handled = true; // 阻止默认行为
                    }
                    // 处理文件节点：单击加载
                    else if (selectedItem.Type == TreeItemType.File && !string.IsNullOrEmpty(selectedItem.Path))
                    {
                        // 🆕 只在非文本编辑模式下才自动切换回图片模式
                        // 如果正在编辑幻灯片且处于分割模式，不应该自动退出
                        bool isEditingSlide = TextEditorPanel.Visibility == Visibility.Visible;
                        if (!isEditingSlide)
                        {
                            AutoExitTextEditorIfNeeded();
                        }
                        
                        // 🔧 先获取文件ID（注意：不要立即设置_currentImageId，因为SwitchToImageMode会清空它）
                        int fileId = selectedItem.Id;
                        
                        // 🔑 关键优化: 检查文件所在文件夹的原图标记和变色标记,自动开关模式
                        var mediaFile = _dbManager.GetMediaFileById(fileId);
                        if (mediaFile != null && mediaFile.FolderId.HasValue)
                        {
                            // 检查原图标记
                            bool hasFolderOriginalMark = _originalManager.CheckOriginalMark(ItemType.Folder, mediaFile.FolderId.Value);
                            
                            if (hasFolderOriginalMark && !_originalMode)
                            {
                                // 父文件夹有原图标记,自动启用原图模式
                                //System.Diagnostics.Debug.WriteLine($"🎯 文件所在文件夹有原图标记,自动启用原图模式");
                                _originalMode = true;
                                _imageProcessor.OriginalMode = true;
                                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                            }
                            else if (!hasFolderOriginalMark && _originalMode)
                            {
                                // 父文件夹没有原图标记,自动关闭原图模式
                                //System.Diagnostics.Debug.WriteLine($"🎯 文件所在文件夹无原图标记,自动关闭原图模式");
                                _originalMode = false;
                                _imageProcessor.OriginalMode = false;
                                BtnOriginal.Background = Brushes.Transparent; // 使用透明背景，让样式生效
                            }
                            
                            // 🎨 变色功能逻辑优化：
                            // 1. 如果切换到不同文件夹，根据标记自动开启/关闭
                            // 2. 如果是同文件夹内切换图片，保持当前变色状态不变
                            int newFolderId = mediaFile.FolderId.Value;
                            bool isSameFolder = (_currentFolderId == newFolderId);
                            
                            if (!isSameFolder)
                            {
                                // 切换到不同文件夹：根据标记自动调整变色状态
                                bool hasFolderColorEffectMark = _dbManager.HasFolderAutoColorEffect(newFolderId);
                                
                                if (hasFolderColorEffectMark && !_isColorEffectEnabled)
                                {
                                    // 文件夹有变色标记，自动启用变色效果
                                    //System.Diagnostics.Debug.WriteLine($"🎨 切换到变色文件夹，自动启用变色效果");
                                    _isColorEffectEnabled = true;
                                    BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // 金色
                                }
                                else if (!hasFolderColorEffectMark && _isColorEffectEnabled)
                                {
                                    // 文件夹没有变色标记，自动关闭变色效果
                                    //System.Diagnostics.Debug.WriteLine($"🎨 切换到非变色文件夹，自动关闭变色效果");
                                    _isColorEffectEnabled = false;
                                    BtnColorEffect.Background = Brushes.Transparent;
                                }
                                
                                // 更新当前文件夹ID
                                _currentFolderId = newFolderId;
                            }
                            // else: 同文件夹内切换图片，保持当前变色状态不变
                        }
                        
                        if (System.IO.File.Exists(selectedItem.Path))
                        {
                            // 根据文件类型进行不同处理
                            switch (selectedItem.FileType)
                            {
                                case FileType.Image:
                                    // 🆕 检查是否在文本编辑模式且处于分割模式
                                    if (TextEditorPanel.Visibility == Visibility.Visible && IsInSplitMode())
                                    {
                                        // 加载图片到选中的分割区域
                                        await LoadImageToSplitRegion(selectedItem.Path);
                                        ShowStatus($"📷 已加载到区域: {selectedItem.Name}");
                                    }
                                    else
                                    {
                                        // 切换回图片模式（注意：这会清空_currentImageId）
                                        SwitchToImageMode();
                                        // 🔧 关键修复：在LoadImage之前设置_currentImageId
                                        // LoadImage内部需要_currentImageId来检查录制数据和更新按钮状态
                                        _currentImageId = fileId;
                                        // 加载图片（预缓存已在LoadImage中触发）
                                        LoadImage(selectedItem.Path);
                                        // ShowStatus($"📷 已加载: {selectedItem.Name}");
                                    }
                                    break;
                                
                                case FileType.Video:
                                case FileType.Audio:
                                    // 视频/音频：单击只选中，不播放
                                    // 保存当前选中的视频路径（用于双击播放和投影播放）
                                    _imagePath = selectedItem.Path;
                                    _currentImageId = fileId; // 🔧 同样设置ID
                                    string fileType = selectedItem.FileType == FileType.Video ? "视频" : "音频";
                                    ShowStatus($"✅ 已选中{fileType}: {selectedItem.Name} (双击播放)");
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

        private void ProjectTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ⏱️ 性能调试：测量切换图片响应时间
            var clickTime = System.Diagnostics.Stopwatch.StartNew();
            //System.Diagnostics.Debug.WriteLine($"\n🖱️ ========== 用户双击切换图片 ==========");
            
            // 获取双击的项目
            if (e.OriginalSource is FrameworkElement element)
            {
                var treeViewItem = FindParent<TreeViewItem>(element);
                if (treeViewItem != null && treeViewItem.DataContext is ProjectTreeItem selectedItem)
                {
                    // 只处理文件节点的双击
                    if (selectedItem.Type == TreeItemType.File && !string.IsNullOrEmpty(selectedItem.Path))
                    {
                        if (System.IO.File.Exists(selectedItem.Path))
                        {
                            // 根据文件类型进行处理
                            switch (selectedItem.FileType)
                            {
                                case FileType.Video:
                                case FileType.Audio:
                                    // 检查投影状态
                                    if (_projectionManager != null && _projectionManager.IsProjectionActive)
                                    {
                                        // 投影已开启，直接在投影屏幕播放
                                        LoadAndDisplayVideoOnProjection(selectedItem.Path);
                                    }
                                    else
                                    {
                                        // 投影未开启，在主屏幕播放
                                        LoadAndDisplayVideo(selectedItem.Path);
                                    }
                                    
                                    string fileType = selectedItem.FileType == FileType.Video ? "视频" : "音频";
                                    ShowStatus($"🎬 正在播放: {selectedItem.Name}");
                                    break;
                                    
                                case FileType.Image:
                                    // 图片双击也加载（保持原有行为）
                                    #if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"📷 切换到图片: {selectedItem.Name}");
                                    #endif
                                    var switchStart = clickTime.ElapsedMilliseconds;
                                    
                                    SwitchToImageMode();
                                    
                                    // 🔧 关键修复：手动选择图片时，停止当前播放
                                    if (_playbackViewModel != null && _playbackViewModel.IsPlaying)
                                    {
                                        #if DEBUG
                                        System.Diagnostics.Debug.WriteLine("🛑 停止当前播放");
                                        #endif
                                        _ = _playbackViewModel.StopPlaybackCommand.ExecuteAsync(null);
                                    }
                                    
                                    var loadStart = clickTime.ElapsedMilliseconds;
                                    LoadImage(selectedItem.Path);
                                    var loadTime = clickTime.ElapsedMilliseconds - loadStart;
                                    
                                    clickTime.Stop();
                                    #if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"⏱️ [切换图片] 准备耗时: {switchStart}ms, 加载耗时: {loadTime}ms, 总耗时: {clickTime.ElapsedMilliseconds}ms");
                                    #endif
                                    #if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"========================================\n");
                                    #endif
                                    
                                    // ⚡ 预缓存已在LoadImage中触发，无需重复
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
                
                // 🆕 如果点击在空白区域（没有TreeViewItem），显示新建项目菜单
                if (treeViewItem == null)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("🎯 [ContextMenu] 创建新建项目菜单...");
                    #endif
                    
                    var contextMenu = new ContextMenu();
                    
                    // 🔑 关键：应用自定义样式（在 MainWindow.xaml 中定义）
                    contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"📊 [ContextMenu] Style: {contextMenu.Style}");
                    System.Diagnostics.Debug.WriteLine($"📊 [ContextMenu] Background: {contextMenu.Background}");
                    System.Diagnostics.Debug.WriteLine($"📊 [ContextMenu] BorderThickness: {contextMenu.BorderThickness}");
                    #endif
                    
                    var newProjectItem = new MenuItem { Header = "📝 新建项目" };
                    newProjectItem.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                    newProjectItem.Foreground = Brushes.White;
                    newProjectItem.BorderThickness = new Thickness(0);
                    newProjectItem.BorderBrush = Brushes.Transparent;
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"📊 [MenuItem] Background: {newProjectItem.Background}");
                    System.Diagnostics.Debug.WriteLine($"📊 [MenuItem] Foreground: {newProjectItem.Foreground}");
                    System.Diagnostics.Debug.WriteLine($"📊 [MenuItem] BorderThickness: {newProjectItem.BorderThickness}");
                    #endif
                    
                    newProjectItem.Click += async (s, args) =>
                    {
                        string projectName = await GenerateDefaultProjectNameAsync();
                        await CreateTextProjectAsync(projectName);
                    };
                    contextMenu.Items.Add(newProjectItem);
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("✅ [ContextMenu] 菜单已创建，准备显示");
                    #endif
                    
                    contextMenu.IsOpen = true;
                    contextMenu.PlacementTarget = sender as UIElement;
                    e.Handled = true;
                    return;
                }
                
                if (treeViewItem != null && treeViewItem.DataContext is ProjectTreeItem item)
                {
                    // 创建右键菜单
                    var contextMenu = new ContextMenu();
                    
                    // 🔑 关键：应用自定义样式（在 MainWindow.xaml 中定义）
                    contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");

                    if (item.Type == TreeItemType.Folder)
                    {
                        // 文件夹右键菜单
                        
                        // 检查文件夹是否包含视频/音频文件或图片文件
                        var folderFiles = _dbManager.GetMediaFilesByFolder(item.Id);
                        bool hasVideoOrAudio = folderFiles.Any(f => f.FileType == FileType.Video || f.FileType == FileType.Audio);
                        bool hasImages = folderFiles.Any(f => f.FileType == FileType.Image);
                        
                        // 只有图片文件夹才显示原图标记菜单
                        if (hasImages)
                        {
                            // 文件夹原图标记菜单
                            bool hasFolderMark = _originalManager.CheckOriginalMark(ItemType.Folder, item.Id);
                            
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
                            
                            // 变色效果标记菜单（只有图片文件夹）
                            bool hasColorEffectMark = _dbManager.HasFolderAutoColorEffect(item.Id);
                            
                            if (hasColorEffectMark)
                            {
                                // 已有变色标记，显示"取消变色"
                                var unmarkColorItem = new MenuItem { Header = "🎨 取消变色标记" };
                                unmarkColorItem.Click += (s, args) => UnmarkFolderColorEffect(item);
                                contextMenu.Items.Add(unmarkColorItem);
                            }
                            else
                            {
                                // 没有变色标记，显示"标记为变色"
                                var markColorItem = new MenuItem { Header = "🎨 标记为变色" };
                                markColorItem.Click += (s, args) => MarkFolderColorEffect(item);
                                contextMenu.Items.Add(markColorItem);
                            }
                            
                            contextMenu.Items.Add(new Separator());
                        }
                        
                        // 只有包含视频/音频的文件夹才显示播放模式菜单
                        if (hasVideoOrAudio)
                        {
                            // 视频播放模式菜单
                            var currentPlayMode = _dbManager.GetFolderVideoPlayMode(item.Id);
                            var playModeMenuItem = new MenuItem { Header = "🎵 视频播放模式" };
                            
                            // 顺序播放
                            var sequentialItem = new MenuItem 
                            { 
                                Header = "⬍⬆ 顺序播放",
                                IsCheckable = true,
                                IsChecked = currentPlayMode == "sequential"
                            };
                            sequentialItem.Click += (s, args) => SetFolderPlayMode(item, "sequential");
                            playModeMenuItem.Items.Add(sequentialItem);
                            
                            // 随机播放
                            var randomItem = new MenuItem 
                            { 
                                Header = "🔀 随机播放",
                                IsCheckable = true,
                                IsChecked = currentPlayMode == "random"
                            };
                            randomItem.Click += (s, args) => SetFolderPlayMode(item, "random");
                            playModeMenuItem.Items.Add(randomItem);
                            
                            // 列表循环
                            var loopAllItem = new MenuItem 
                            { 
                                Header = "🔁 列表循环",
                                IsCheckable = true,
                                IsChecked = currentPlayMode == "loop_all"
                            };
                            loopAllItem.Click += (s, args) => SetFolderPlayMode(item, "loop_all");
                            playModeMenuItem.Items.Add(loopAllItem);
                            
                            playModeMenuItem.Items.Add(new Separator());
                            
                            // 清除标记
                            var clearModeItem = new MenuItem { Header = "✖ 清除播放模式" };
                            clearModeItem.Click += (s, args) => ClearFolderPlayMode(item);
                            playModeMenuItem.Items.Add(clearModeItem);
                            
                            contextMenu.Items.Add(playModeMenuItem);
                            contextMenu.Items.Add(new Separator());
                        }
                        
                        // 检查是否为手动排序文件夹
                        bool isManualSort = _dbManager.IsManualSortFolder(item.Id);
                        if (isManualSort)
                        {
                            var resetSortItem = new MenuItem { Header = "🔄 重置排序" };
                            resetSortItem.Click += (s, args) => ResetFolderSort(item);
                            contextMenu.Items.Add(resetSortItem);
                            contextMenu.Items.Add(new Separator());
                        }
                        
                        // 标记高亮色菜单
                        var highlightColorItem = new MenuItem { Header = "🎨 标记高亮色" };
                        highlightColorItem.Click += (s, args) => SetFolderHighlightColor(item);
                        contextMenu.Items.Add(highlightColorItem);
                        
                        contextMenu.Items.Add(new Separator());
                        
                        // 文件夹顺序调整菜单
                        var moveUpItem = new MenuItem { Header = "⬆️ 上移" };
                        moveUpItem.Click += (s, args) => MoveFolderUp(item);
                        contextMenu.Items.Add(moveUpItem);
                        
                        var moveDownItem = new MenuItem { Header = "⬇️ 下移" };
                        moveDownItem.Click += (s, args) => MoveFolderDown(item);
                        contextMenu.Items.Add(moveDownItem);
                        
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
                            bool hasOriginalMark = _originalManager.CheckOriginalMark(ItemType.Image, item.Id);
                            
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
                    else if (item.Type == TreeItemType.Project || item.Type == TreeItemType.TextProject)
                    {
                        // 文本项目右键菜单
                        var renameItem = new MenuItem { Header = "✏️ 重命名" };
                        renameItem.Click += (s, args) => RenameTextProjectAsync(item);
                        contextMenu.Items.Add(renameItem);
                        
                        contextMenu.Items.Add(new Separator());
                        
                        var deleteItem = new MenuItem { Header = "🗑️ 删除项目" };
                        deleteItem.Click += async (s, args) => await DeleteTextProjectAsync(item);
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
                _dbManager.DeleteFolder(item.Id);
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
            var (added, removed, updated) = _importManager.SyncFolder(item.Id);
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
                _dbManager.SetFolderVideoPlayMode(item.Id, playMode);
                
                string[] modeNames = { "顺序播放", "随机播放", "列表循环" };
                string modeName = playMode switch
                {
                    "sequential" => modeNames[0],
                    "random" => modeNames[1],
                    "loop_all" => modeNames[2],
                    _ => "未知"
                };
                
                // 刷新项目树以更新图标
                LoadProjects();
                
                ShowStatus($"✅ 已设置文件夹 [{item.Name}] 的播放模式: {modeName}");
                //System.Diagnostics.Debug.WriteLine($"✅ 文件夹 [{item.Name}] 播放模式: {modeName}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 设置播放模式失败: {ex.Message}");
                MessageBox.Show($"设置播放模式失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 清除文件夹的视频播放模式
        /// </summary>
        private void ClearFolderPlayMode(ProjectTreeItem item)
        {
            try
            {
                _dbManager.ClearFolderVideoPlayMode(item.Id);
                
                // 刷新项目树以更新图标
                LoadProjects();
                
                ShowStatus($"✅ 已清除文件夹 [{item.Name}] 的播放模式");
                //System.Diagnostics.Debug.WriteLine($"✅ 已清除文件夹 [{item.Name}] 的播放模式");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 清除播放模式失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 标记文件夹自动变色
        /// </summary>
        private void MarkFolderColorEffect(ProjectTreeItem item)
        {
            try
            {
                _dbManager.MarkFolderAutoColorEffect(item.Id);
                LoadProjects();
                ShowStatus($"✅ 已标记文件夹 [{item.Name}] 自动变色");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 标记变色失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 取消文件夹变色标记
        /// </summary>
        private void UnmarkFolderColorEffect(ProjectTreeItem item)
        {
            try
            {
                _dbManager.UnmarkFolderAutoColorEffect(item.Id);
                LoadProjects();
                ShowStatus($"✅ 已取消文件夹 [{item.Name}] 的变色标记");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 取消变色标记失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 设置文件夹高亮颜色
        /// </summary>
        private void SetFolderHighlightColor(ProjectTreeItem item)
        {
            try
            {
                // 创建系统颜色选择对话框
                var colorDialog = new System.Windows.Forms.ColorDialog();
                colorDialog.FullOpen = true; // 默认展开自定义颜色面板
                colorDialog.AnyColor = true; // 允许选择任意颜色
                
                // 如果文件夹已有自定义颜色，设置为初始颜色
                string existingColor = _dbManager.GetFolderHighlightColor(item.Id);
                if (!string.IsNullOrEmpty(existingColor))
                {
                    try
                    {
                        var wpfColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(existingColor);
                        colorDialog.Color = System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
                    }
                    catch { }
                }
                
                // 显示颜色选择对话框
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // 将选中的颜色转换为十六进制格式
                    var selectedColor = colorDialog.Color;
                    string colorHex = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
                    
                    // 设置自定义颜色
                    _dbManager.SetFolderHighlightColor(item.Id, colorHex);
                    ShowStatus($"✅ 已设置文件夹 [{item.Name}] 的高亮颜色: {colorHex}");
                    
                    // 刷新项目树
                    LoadProjects();
                    
                    // 如果当前有搜索内容，刷新搜索结果
                    string searchTerm = SearchBox.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(searchTerm))
                    {
                        string searchScope = (SearchScope.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部";
                        var searchResults = _searchManager.SearchProjects(searchTerm, searchScope);
                        
                        if (searchResults != null)
                        {
                            _projectTreeItems.Clear();
                            foreach (var result in searchResults)
                            {
                                _projectTreeItems.Add(result);
                            }
                            // 不需要重新设置ItemsSource，ObservableCollection会自动通知UI更新
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 设置高亮颜色失败: {ex.Message}");
                MessageBox.Show($"设置高亮颜色失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 获取播放模式对应的图标
        /// </summary>
        private (string iconKind, string iconColor) GetPlayModeIcon(string playMode)
        {
            return playMode switch
            {
                "sequential" => ("SortAscending", ICON_COLOR_SEQUENTIAL),  // 顺序播放 - 蓝色
                "random" => ("Shuffle", ICON_COLOR_RANDOM),                // 随机播放 - 橙色
                "loop_all" => ("Repeat", ICON_COLOR_LOOP),                 // 列表循环 - 绿色
                _ => ("Shuffle", ICON_COLOR_RANDOM)                        // 默认随机播放 - 橙色
            };
        }

        /// <summary>
        /// 重置文件夹排序（取消手动排序，恢复自动排序）
        /// </summary>
        private void ResetFolderSort(ProjectTreeItem item)
        {
            var result = MessageBox.Show(
                $"确定要重置文件夹 '{item.Name}' 的排序吗？\n将按照文件名自动排序。",
                "确认重置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 取消手动排序标记
                    _dbManager.UnmarkFolderAsManualSort(item.Id);
                    
                    // 重新应用自动排序规则
                    var files = _dbManager.GetMediaFilesByFolder(item.Id);
                    if (files.Count > 0)
                    {
                        // 使用SortManager的排序键对文件进行排序
                        var sortedFiles = files
                            .Select(f => new
                            {
                                File = f,
                                SortKey = _sortManager.GetSortKey(f.Name + System.IO.Path.GetExtension(f.Path))
                            })
                            .OrderBy(x => x.SortKey.prefixNumber)
                            .ThenBy(x => x.SortKey.pinyinPart)
                            .ThenBy(x => x.SortKey.suffixNumber)
                            .Select(x => x.File)
                            .ToList();

                        // 更新OrderIndex
                        for (int i = 0; i < sortedFiles.Count; i++)
                        {
                            sortedFiles[i].OrderIndex = i + 1;
                        }

                        // 保存更改
                        _dbManager.UpdateMediaFilesOrder(sortedFiles);
                    }
                    
                    LoadProjects();
                    ShowStatus($"✅ 已重置文件夹排序: {item.Name}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"重置排序失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 标记文件夹为原图
        /// </summary>
        private void MarkFolderAsOriginal(ProjectTreeItem item, MarkType markType)
        {
            bool success = _originalManager.AddOriginalMark(ItemType.Folder, item.Id, markType);
            
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
            bool success = _originalManager.RemoveOriginalMark(ItemType.Folder, item.Id);
            
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
            bool success = _originalManager.AddOriginalMark(ItemType.Image, item.Id, markType);
            
            if (success)
            {
                string modeText = markType == MarkType.Loop ? "循环" : "顺序";
                ShowStatus($"✅ 已标记为原图({modeText}): {item.Name}");
                
                // 立即刷新项目树显示
                LoadProjects();
                
                // 如果标记的是当前正在显示的图片,自动启用原图模式
                if (_currentImageId == item.Id && !_originalMode)
                {
                    //System.Diagnostics.Debug.WriteLine($"🎯 自动启用原图模式: {item.Name}");
                    _originalMode = true;
                    _imageProcessor.OriginalMode = true;
                    
                    // 更新按钮样式
                    BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144)); // 浅绿色
                    
                    // 查找相似图片
                    _originalManager.FindSimilarImages(_currentImageId);
                    
                    // 重新显示图片
                    _imageProcessor.UpdateImage();
                    
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
            bool success = _originalManager.RemoveOriginalMark(ItemType.Image, item.Id);
            
            if (success)
            {
                ShowStatus($"✅ 已取消原图标记: {item.Name}");
                
                // 立即刷新项目树显示
                LoadProjects();
                
                // 如果取消的是当前正在显示的图片,关闭原图模式
                if (_currentImageId == item.Id && _originalMode)
                {
                    //System.Diagnostics.Debug.WriteLine($"🎯 自动关闭原图模式: {item.Name}");
                    _originalMode = false;
                    _imageProcessor.OriginalMode = false;
                    
                    // 更新按钮样式
                    BtnOriginal.Background = Brushes.Transparent; // 使用透明背景，让样式生效
                    
                    // 重新显示图片
                    _imageProcessor.UpdateImage();
                    
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
                _dbManager.DeleteMediaFile(item.Id);
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
                // System.Diagnostics.Debug.WriteLine("📁 已折叠所有文件夹节点");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"折叠所有文件夹失败: {ex.Message}");
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
                // System.Diagnostics.Debug.WriteLine($"📁 已折叠除 {exceptFolder.Name} 外的所有文件夹");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"折叠其他文件夹失败: {ex.Message}");
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
    }
}

