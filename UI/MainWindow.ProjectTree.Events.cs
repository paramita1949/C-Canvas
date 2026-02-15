using System;
using System.Diagnostics;
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

        private async void ProjectTree_MouseClick(object sender, MouseButtonEventArgs e)
        {
            // 获取点击的项目
            if (e.OriginalSource is FrameworkElement element)
            {
                var treeViewItem = FindParent<TreeViewItem>(element);
                if (treeViewItem != null && treeViewItem.DataContext is ProjectTreeItem selectedItem)
                {
                    // 🆕 圣经模式下的特殊处理
                    if (_isBibleMode && selectedItem.Type == TreeItemType.BibleChapter)
                    {
                        await HandleBibleNodeClickAsync(selectedItem);
                        return;
                    }

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
                            
                            #if DEBUG
                            //Debug.WriteLine($"🎨 [点击图片] 文件: {selectedItem.Name}");
                            //Debug.WriteLine($"   新文件夹ID: {newFolderId}");
                            //Debug.WriteLine($"   当前文件夹ID: {_currentFolderId}");
                            //Debug.WriteLine($"   是否同一文件夹: {isSameFolder}");
                            #endif
                            
                            if (!isSameFolder)
                            {
                                // 切换到不同文件夹：根据标记自动调整变色状态
                                bool hasFolderColorEffectMark = _dbManager.HasFolderAutoColorEffect(newFolderId);
                                
                                #if DEBUG
                                //Debug.WriteLine($"   文件夹有变色标记: {hasFolderColorEffectMark}");
                                //Debug.WriteLine($"   当前变色状态: {_isColorEffectEnabled}");
                                #endif
                                
                                if (hasFolderColorEffectMark && !_isColorEffectEnabled)
                                {
                                    // 文件夹有变色标记，自动启用变色效果
                                    #if DEBUG
                                    //Debug.WriteLine($"🎨 [点击图片] 切换到变色文件夹，自动启用变色效果");
                                    #endif
                                    _isColorEffectEnabled = true;
                                    BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // 金色
                                }
                                else if (!hasFolderColorEffectMark && _isColorEffectEnabled)
                                {
                                    // 文件夹没有变色标记，自动关闭变色效果
                                    #if DEBUG
                                    //Debug.WriteLine($"🎨 [点击图片] 切换到非变色文件夹，自动关闭变色效果");
                                    #endif
                                    _isColorEffectEnabled = false;
                                    BtnColorEffect.Background = Brushes.Transparent;
                                }
                                
                                // 更新当前文件夹ID
                                _currentFolderId = newFolderId;
                            }
                            else
                            {
                                // 🔧 修复：同文件夹内点击图片，但如果之前没有加载过图片，也要检查并应用变色标记
                                // 这种情况发生在：展开文件夹 → 标记变色 → 首次点击图片
                                if (_currentImageId == 0)
                                {
                                    bool hasFolderColorEffectMark = _dbManager.HasFolderAutoColorEffect(newFolderId);
                                    
                                    #if DEBUG
                                    //Debug.WriteLine($"   首次加载图片，检查变色标记");
                                    //Debug.WriteLine($"   文件夹有变色标记: {hasFolderColorEffectMark}");
                                    //Debug.WriteLine($"   当前变色状态: {_isColorEffectEnabled}");
                                    #endif
                                    
                                    if (hasFolderColorEffectMark && !_isColorEffectEnabled)
                                    {
                                        // 文件夹有变色标记，自动启用变色效果
                                        #if DEBUG
                                        //Debug.WriteLine($"🎨 [点击图片] 首次加载，启用文件夹变色效果");
                                        #endif
                                        _isColorEffectEnabled = true;
                                        BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // 金色
                                    }
                                    else if (!hasFolderColorEffectMark && _isColorEffectEnabled)
                                    {
                                        // 文件夹没有变色标记，自动关闭变色效果
                                        #if DEBUG
                                        //Debug.WriteLine($"🎨 [点击图片] 首次加载，关闭变色效果");
                                        #endif
                                        _isColorEffectEnabled = false;
                                        BtnColorEffect.Background = Brushes.Transparent;
                                    }
                                }
                                // else: 同文件夹内切换图片，且已有图片加载，保持当前变色状态不变
                            }
                        }
                        
                        if (System.IO.File.Exists(selectedItem.Path))
                        {
                            // 根据文件类型进行不同处理
                            switch (selectedItem.FileType)
                            {
                                case FileType.Image:
                                    // 🆕 检查是否在文本编辑模式（幻灯片分割模式）
                                    if (TextEditorPanel.Visibility == Visibility.Visible && IsInSplitMode())
                                    {
                                        // 文本编辑模式（包括单画面模式）：加载图片到选中的分割区域
                                        await LoadImageToSplitRegion(selectedItem.Path);
                                        ShowStatus($"📷 已加载: {selectedItem.Name}");
                                    }
                                    // 🎤 检查是否在歌词模式
                                    else if (_isLyricsMode)
                                    {
//#if DEBUG
//                                        System.Diagnostics.Debug.WriteLine($"[歌词-树状图] 在歌词模式下点击图片，切换歌词");
//                                        System.Diagnostics.Debug.WriteLine($"[歌词-树状图] 旧图片ID: {_currentImageId}, 新图片ID: {fileId}");
//                                        System.Diagnostics.Debug.WriteLine($"[歌词-树状图] 图片名称: {selectedItem.Name}");
//#endif
                                        // 更新当前图片ID和路径
                                        _currentImageId = fileId;
                                        _imagePath = selectedItem.Path;
                                        
                                        // 触发歌词切换（会保存当前歌词，加载新图片的歌词）
                                        OnImageChangedInLyricsMode();
                                        
                                        ShowStatus($"🎤 已切换到: {selectedItem.Name} 的歌词");
                                    }
                                    // 🔧 检查是否在幻灯片模式（不在分割编辑状态）
                                    else if (_currentViewMode == NavigationViewMode.Projects)
                                    {
                                        // 幻灯片模式但不在分割编辑：只提示，不加载
                                        ShowStatus($"💡 请先打开幻灯片进入分割模式，或切换到文件视图");
                                    }
                                    else
                                    {
                                        // 文件模式：切换回图片模式（注意：这会清空_currentImageId）
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
                                    
                                    // 🎬 隐藏合成播放按钮面板（媒体文件不需要）
                                    CompositePlaybackPanel.Visibility = Visibility.Collapsed;
                                    
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
                
                // 🆕 如果点击在空白区域（没有TreeViewItem），只在幻灯片项目模式显示新建项目菜单
                if (treeViewItem == null)
                {
                    // 只在幻灯片项目模式（Projects）显示新建项目菜单
                    if (_currentViewMode == NavigationViewMode.Projects)
                    {
                        var contextMenu = new ContextMenu();
                        
                        // 🔑 关键：应用自定义样式（在 MainWindow.xaml 中定义）
                        contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");
                        
                        var newProjectItem = new MenuItem { Header = "📝 新建项目" };
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

                        var exportAllItem = new MenuItem { Header = "📦 导出所有项目" };
                        exportAllItem.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                        exportAllItem.Foreground = Brushes.White;
                        exportAllItem.BorderThickness = new Thickness(0);
                        exportAllItem.BorderBrush = Brushes.Transparent;

                        exportAllItem.Click += async (s, args) => await ExportAllProjectsAsync();
                        contextMenu.Items.Add(exportAllItem);

                        contextMenu.IsOpen = true;
                        contextMenu.PlacementTarget = sender as UIElement;
                        e.Handled = true;
                    }
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

                        // 🆕 创建分割图菜单项（点击后再查找相似图片）
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

                        // 歌词模式菜单项
                        var lyricsModeMenuItem = new MenuItem
                        {
                            Header = "歌词模式",
                            FontSize = 14
                        };
                        lyricsModeMenuItem.Click += (s, args) => EnterLyricsModeFromFile(item);
                        contextMenu.Items.Add(lyricsModeMenuItem);

                        // 🆕 添加到幻灯片菜单项
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
                    else if (item.Type == TreeItemType.Project || item.Type == TreeItemType.TextProject)
                    {
                        // 文本项目右键菜单
                        var renameItem = new MenuItem { Header = "✏️ 重命名" };
                        renameItem.Click += (s, args) => RenameTextProjectAsync(item);
                        contextMenu.Items.Add(renameItem);

                        contextMenu.Items.Add(new Separator());

                        var exportItem = new MenuItem { Header = "📤 导出" };
                        exportItem.Click += async (s, args) => await ExportTextProjectAsync(item);
                        contextMenu.Items.Add(exportItem);

                        contextMenu.Items.Add(new Separator());

                        var deleteItem = new MenuItem { Header = "🗑️ 删除项目" };
                        deleteItem.Click += async (s, args) => await DeleteTextProjectAsync(item);
                        contextMenu.Items.Add(deleteItem);
                    }

                    contextMenu.IsOpen = true;
                }
            }
        }

        private void EnterLyricsModeFromFile(ProjectTreeItem item)
        {
            try
            {
                if (item == null || item.Type != TreeItemType.File)
                {
                    return;
                }

                if (item.FileType != FileType.Image)
                {
                    ShowStatus("⚠️ 歌词模式仅支持图片文件");
                    return;
                }

                if (string.IsNullOrWhiteSpace(item.Path) || !System.IO.File.Exists(item.Path))
                {
                    ShowStatus($"❌ 文件不存在: {item?.Name}");
                    return;
                }

                // 进入歌词模式前，先退出文本编辑态（若有）
                AutoExitTextEditorIfNeeded();

                // 切换歌词关联目标到当前右键文件
                _currentImageId = item.Id;
                _imagePath = item.Path;

                if (_isLyricsMode)
                {
                    OnImageChangedInLyricsMode();
                }
                else
                {
                    EnterLyricsMode();
                }

                ShowStatus($"🎤 已进入歌词模式: {item.Name}");
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [EnterLyricsModeFromFile] 失败: {ex.Message}");
#endif
                ShowStatus($"❌ 进入歌词模式失败: {ex.Message}");
            }
        }
        #endregion
    }
}
