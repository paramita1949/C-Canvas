using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;

// 引入WPF拖拽相关命名空间
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using MouseButtonState = System.Windows.Input.MouseButtonState;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的拖拽事件处理部分
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 拖拽事件处理

        /// <summary>
        /// 鼠标按下事件 - 记录拖拽起始点
        /// </summary>
        private void ProjectTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            
            // 获取点击的TreeViewItem
            var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (treeViewItem != null)
            {
                _draggedItem = treeViewItem.DataContext as ProjectTreeItem;
            }
        }

        /// <summary>
        /// 鼠标移动事件 - 开始拖拽
        /// </summary>
        private void ProjectTree_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedItem != null)
            {
                System.Windows.Point currentPosition = e.GetPosition(null);
                System.Windows.Vector diff = _dragStartPoint - currentPosition;

                // 检查是否移动了足够的距离
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // 允许拖拽文件和文件夹（不允许拖拽Project）
                    if (_draggedItem.Type == TreeItemType.File || _draggedItem.Type == TreeItemType.Folder)
                    {
                        System.Windows.DragDrop.DoDragDrop(ProjectTree, _draggedItem, System.Windows.DragDropEffects.Move);
                    }
                    
                    _draggedItem = null;
                }
            }
        }

        /// <summary>
        /// 拖拽悬停事件 - 显示拖拽效果
        /// </summary>
        private void ProjectTree_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(ProjectTreeItem)))
            {
                // 获取当前悬停的TreeViewItem
                var targetTreeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                if (targetTreeViewItem != null)
                {
                    var targetItem = targetTreeViewItem.DataContext as ProjectTreeItem;
                    
                    _dragOverItem = targetItem;
                    
                    // 获取拖拽源项
                    var sourceItem = e.Data.GetData(typeof(ProjectTreeItem)) as ProjectTreeItem;
                    
                    // 检查是否是有效的拖放目标
                    // 文件只能拖到文件上，文件夹只能拖到文件夹上
                    bool isValidDrop = false;
                    if (sourceItem != null && targetItem != null)
                    {
                        if (sourceItem.Type == TreeItemType.File && targetItem.Type == TreeItemType.File)
                        {
                            isValidDrop = true;
                        }
                        else if (sourceItem.Type == TreeItemType.Folder && targetItem.Type == TreeItemType.Folder)
                        {
                            isValidDrop = true;
                        }
                    }
                    
                    if (isValidDrop)
                    {
                        e.Effects = System.Windows.DragDropEffects.Move;
                        
                        // 显示拖拽插入位置指示器（蓝色横线）
                        ShowDragIndicator(targetTreeViewItem);
                    }
                    else
                    {
                        e.Effects = System.Windows.DragDropEffects.None;
                        HideDragIndicator();
                    }
                }
                else
                {
                    e.Effects = System.Windows.DragDropEffects.None;
                    HideDragIndicator();
                }
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
                HideDragIndicator();
            }
            e.Handled = true;
        }

        /// <summary>
        /// 拖拽离开事件 - 清除高亮
        /// </summary>
        private void ProjectTree_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            ClearDragHighlight();
        }

        /// <summary>
        /// 放置事件 - 执行拖拽排序
        /// </summary>
        private void ProjectTree_Drop(object sender, System.Windows.DragEventArgs e)
        {
            // 清除拖拽高亮
            ClearDragHighlight();
            
            if (e.Data.GetDataPresent(typeof(ProjectTreeItem)))
            {
                var sourceItem = e.Data.GetData(typeof(ProjectTreeItem)) as ProjectTreeItem;
                
                // 获取目标TreeViewItem
                var targetTreeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                if (targetTreeViewItem != null)
                {
                    var targetItem = targetTreeViewItem.DataContext as ProjectTreeItem;
                    
                    if (sourceItem != null && targetItem != null && sourceItem != targetItem)
                    {
                        // 文件排序：只允许在同一文件夹内拖拽排序
                        if (sourceItem.Type == TreeItemType.File && targetItem.Type == TreeItemType.File)
                        {
                            ReorderFiles(sourceItem, targetItem);
                        }
                        // 文件夹排序：只允许根级别文件夹之间排序
                        else if (sourceItem.Type == TreeItemType.Folder && targetItem.Type == TreeItemType.Folder)
                        {
                            ReorderFolders(sourceItem, targetItem);
                        }
                    }
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// TreeViewItem鼠标进入事件 - 显示完整文件名提示
        /// </summary>
        private void TreeViewItem_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (sender is TreeViewItem treeViewItem && treeViewItem.DataContext is ProjectTreeItem item)
                {
                    // 获取显示文本（文件名或文件夹名）
                    string displayText = item.Name;
                    
                    if (!string.IsNullOrEmpty(displayText))
                    {
                        // 设置提示框文本
                        FileNameTooltipText.Text = displayText;
                        
                        // 显示提示框
                        FileNameTooltipPopup.IsOpen = true;
                    }
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"显示文件名提示时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// TreeViewItem鼠标离开事件 - 隐藏提示框
        /// </summary>
        private void TreeViewItem_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                // 隐藏提示框
                FileNameTooltipPopup.IsOpen = false;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"隐藏文件名提示时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// TreeView鼠标移动事件 - 更新提示框位置跟随鼠标
        /// </summary>
        private void ProjectTree_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (FileNameTooltipPopup.IsOpen)
                {
                    // 获取鼠标相对于ProjectTree的位置
                    System.Windows.Point mousePos = e.GetPosition(ProjectTree);
                    
                    // 设置提示框位置（鼠标右下方偏移一点）
                    FileNameTooltipPopup.HorizontalOffset = mousePos.X + 15;
                    FileNameTooltipPopup.VerticalOffset = mousePos.Y + 15;
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"更新提示框位置时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示拖拽插入位置指示器
        /// </summary>
        private void ShowDragIndicator(TreeViewItem targetItem)
        {
            try
            {
                if (DragIndicatorLine == null || targetItem == null) return;

                // 获取TreeViewItem相对于ProjectTree的位置
                var position = targetItem.TranslatePoint(new System.Windows.Point(0, 0), ProjectTree);
                
                // 获取目标项的数据
                var targetData = targetItem.DataContext as ProjectTreeItem;
                if (targetData == null) return;
                
                // 精确计算文件名的起始位置
                // TreeView缩进 + 图标宽度 + 图标右边距 = 文件名起始位置
                double treeViewIndent = targetData.Type == TreeItemType.File ? 19 : 0; // 文件的TreeView缩进
                double iconWidth = 18; // PackIcon宽度
                double iconMargin = 8; // PackIcon右边距
                double textStartPosition = treeViewIndent + iconWidth + iconMargin; // 文件名实际开始位置
                
                // 根据文件名长度智能调整横线长度
                double lineLength;
                if (!string.IsNullOrEmpty(targetData.Name))
                {
                    // 基于文件名长度估算宽度（每个字符约7px，中文字符约12px）
                    double estimatedWidth = 0;
                    foreach (char c in targetData.Name)
                    {
                        estimatedWidth += (c > 127) ? 12 : 7; // 中文字符宽度更大
                    }
                    lineLength = Math.Min(estimatedWidth + 10, 160); // 最大160px，加10px缓冲
                    lineLength = Math.Max(lineLength, 60); // 最小60px
                }
                else
                {
                    lineLength = 80; // 默认长度
                }
                
                // 设置指示线的位置和长度
                Canvas.SetTop(DragIndicatorLine, position.Y);
                DragIndicatorLine.X1 = textStartPosition;
                DragIndicatorLine.X2 = textStartPosition + lineLength;
                DragIndicatorLine.Y1 = 0;
                DragIndicatorLine.Y2 = 0;
                
                // 显示指示线
                DragIndicatorLine.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"显示拖拽指示器失败: {ex}");
            }
        }

        /// <summary>
        /// 隐藏拖拽插入位置指示器
        /// </summary>
        private void HideDragIndicator()
        {
            try
            {
                if (DragIndicatorLine != null)
                {
                    DragIndicatorLine.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"隐藏拖拽指示器失败: {ex}");
            }
        }

        /// <summary>
        /// 清除拖拽高亮效果
        /// </summary>
        private void ClearDragHighlight()
        {
            _dragOverItem = null;
            HideDragIndicator();
        }

        /// <summary>
        /// 递归清除TreeView中所有项的边框
        /// </summary>
        private void ClearTreeViewItemBorders(ItemsControl itemsControl)
        {
            if (itemsControl == null) return;

            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                var item = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (item != null)
                {
                    item.BorderThickness = new Thickness(0);
                    item.BorderBrush = null;
                    
                    // 递归处理子项
                    if (item.HasItems)
                    {
                        ClearTreeViewItemBorders(item);
                    }
                }
            }
        }

        /// <summary>
        /// 查找指定类型的父元素
        /// </summary>
        private T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        /// <summary>
        /// 重新排序文件
        /// </summary>
        private void ReorderFiles(ProjectTreeItem sourceItem, ProjectTreeItem targetItem)
        {
            // 防止重复执行
            if (_isDragInProgress) return;
            _isDragInProgress = true;
            
            try
            {
                // 获取源文件和目标文件所属的文件夹
                int? sourceFolderId = GetFileFolderId(sourceItem);
                int? targetFolderId = GetFileFolderId(targetItem);

                // 只允许在同一文件夹内排序
                if (sourceFolderId != targetFolderId)
                {
                    ShowStatus("❌ 只能在同一文件夹内拖拽排序");
                    return;
                }

                // 如果有文件夹ID，标记为手动排序
                if (sourceFolderId.HasValue)
                {
                    _dbManager.MarkFolderAsManualSort(sourceFolderId.Value);
                }

                // 获取文件夹中的所有文件
                var files = sourceFolderId.HasValue 
                    ? _dbManager.GetMediaFilesByFolder(sourceFolderId.Value)
                    : _dbManager.GetRootMediaFiles();

                // 找到源文件和目标文件的索引
                int sourceIndex = files.FindIndex(f => f.Id == sourceItem.Id);
                int targetIndex = files.FindIndex(f => f.Id == targetItem.Id);

                if (sourceIndex == -1 || targetIndex == -1)
                {
                    ShowStatus("❌ 无法找到文件");
                    return;
                }

                // 移除源文件
                var sourceFile = files[sourceIndex];
                files.RemoveAt(sourceIndex);

                // 插入到目标位置
                if (sourceIndex < targetIndex)
                {
                    files.Insert(targetIndex, sourceFile);
                }
                else
                {
                    files.Insert(targetIndex, sourceFile);
                }

                // 更新所有文件的OrderIndex
                for (int i = 0; i < files.Count; i++)
                {
                    files[i].OrderIndex = i + 1;
                }

                // 保存更改
                _dbManager.UpdateMediaFilesOrder(files);

                // 🔑 关键修复：直接在内存中更新顺序，避免重新加载整个TreeView
                UpdateTreeItemOrder(sourceFolderId, files);
                
                ShowStatus($"✅ 已重新排序: {sourceItem.Name}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"重新排序失败: {ex}");
                ShowStatus($"❌ 排序失败: {ex.Message}");
            }
            finally
            {
                // 确保标志被重置
                _isDragInProgress = false;
            }
        }

        /// <summary>
        /// 重新排序文件夹（拖拽）
        /// </summary>
        private void ReorderFolders(ProjectTreeItem sourceItem, ProjectTreeItem targetItem)
        {
            // 防止重复执行
            if (_isDragInProgress) return;
            _isDragInProgress = true;
            
            try
            {
                // 获取所有文件夹
                var folders = _dbManager.GetAllFolders();
                
                // 找到源文件夹和目标文件夹的索引
                int sourceIndex = folders.FindIndex(f => f.Id == sourceItem.Id);
                int targetIndex = folders.FindIndex(f => f.Id == targetItem.Id);

                if (sourceIndex == -1 || targetIndex == -1)
                {
                    ShowStatus("❌ 无法找到文件夹");
                    return;
                }

                // 移除源文件夹
                var sourceFolder = folders[sourceIndex];
                folders.RemoveAt(sourceIndex);

                // 插入到目标位置
                if (sourceIndex < targetIndex)
                {
                    folders.Insert(targetIndex, sourceFolder);
                }
                else
                {
                    folders.Insert(targetIndex, sourceFolder);
                }

                // 更新所有文件夹的OrderIndex
                for (int i = 0; i < folders.Count; i++)
                {
                    folders[i].OrderIndex = i + 1;
                }

                // 保存更改到数据库
                _dbManager.UpdateFoldersOrder(folders);

                // 更新TreeView中的文件夹顺序
                UpdateFolderTreeItemOrder(folders);
                
                ShowStatus($"✅ 已重新排序文件夹: {sourceItem.Name}");
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"重新排序文件夹失败: {ex}");
                #endif
                ShowStatus($"❌ 文件夹排序失败: {ex.Message}");
            }
            finally
            {
                // 确保标志被重置
                _isDragInProgress = false;
            }
        }

        /// <summary>
        /// 文件夹上移
        /// </summary>
        private void MoveFolderUp(ProjectTreeItem folderItem)
        {
            try
            {
                // 获取所有文件夹
                var folders = _dbManager.GetAllFolders();
                
                // 找到当前文件夹的索引
                int currentIndex = folders.FindIndex(f => f.Id == folderItem.Id);
                
                if (currentIndex == -1)
                {
                    ShowStatus("❌ 无法找到文件夹");
                    return;
                }
                
                // 如果已经是第一个，无法上移
                if (currentIndex == 0)
                {
                    ShowStatus("⚠️ 已经是第一个文件夹");
                    return;
                }
                
                // 与上一个文件夹交换位置
                var currentFolder = folders[currentIndex];
                folders.RemoveAt(currentIndex);
                folders.Insert(currentIndex - 1, currentFolder);
                
                // 更新所有文件夹的OrderIndex
                for (int i = 0; i < folders.Count; i++)
                {
                    folders[i].OrderIndex = i + 1;
                }
                
                // 保存到数据库
                _dbManager.UpdateFoldersOrder(folders);
                
                // 更新UI
                UpdateFolderTreeItemOrder(folders);
                
                ShowStatus($"✅ 已上移: {folderItem.Name}");
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"文件夹上移失败: {ex}");
                #endif
                ShowStatus($"❌ 上移失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 文件夹下移
        /// </summary>
        private void MoveFolderDown(ProjectTreeItem folderItem)
        {
            try
            {
                // 获取所有文件夹
                var folders = _dbManager.GetAllFolders();
                
                // 找到当前文件夹的索引
                int currentIndex = folders.FindIndex(f => f.Id == folderItem.Id);
                
                if (currentIndex == -1)
                {
                    ShowStatus("❌ 无法找到文件夹");
                    return;
                }
                
                // 如果已经是最后一个，无法下移
                if (currentIndex == folders.Count - 1)
                {
                    ShowStatus("⚠️ 已经是最后一个文件夹");
                    return;
                }
                
                // 与下一个文件夹交换位置
                var currentFolder = folders[currentIndex];
                folders.RemoveAt(currentIndex);
                folders.Insert(currentIndex + 1, currentFolder);
                
                // 更新所有文件夹的OrderIndex
                for (int i = 0; i < folders.Count; i++)
                {
                    folders[i].OrderIndex = i + 1;
                }
                
                // 保存到数据库
                _dbManager.UpdateFoldersOrder(folders);
                
                // 更新UI
                UpdateFolderTreeItemOrder(folders);
                
                ShowStatus($"✅ 已下移: {folderItem.Name}");
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"文件夹下移失败: {ex}");
                #endif
                ShowStatus($"❌ 下移失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 轻量级更新TreeView中的文件顺序（避免重新加载整个TreeView）
        /// </summary>
        private void UpdateTreeItemOrder(int? folderId, List<MediaFile> sortedFiles)
        {
            try
            {
                if (folderId.HasValue)
                {
                    // 更新文件夹内的文件顺序
                    var folderItem = _projectTreeItems.FirstOrDefault(f => f.Type == TreeItemType.Folder && f.Id == folderId.Value);
                    if (folderItem?.Children != null)
                    {
                        // 保存当前展开状态
                        bool wasExpanded = folderItem.IsExpanded;
                        
                        // 清空并重新添加文件（保持正确顺序）
                        folderItem.Children.Clear();
                        
                        foreach (var file in sortedFiles)
                        {
                            // 获取图标
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
                        
                        // 恢复展开状态（延迟执行避免绑定冲突）
                        if (wasExpanded)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                folderItem.IsExpanded = true;
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                        
                        // 更新文件夹图标
                        // 检查文件夹是否包含媒体文件
                        bool hasMediaFiles = sortedFiles.Any(f => f.FileType == FileType.Video || f.FileType == FileType.Audio);
                        string folderPlayMode = _dbManager.GetFolderVideoPlayMode(folderId.Value);
                        
                        string iconKind, iconColor;
                        if (hasMediaFiles)
                        {
                            // 媒体文件夹保持播放模式图标
                            if (!string.IsNullOrEmpty(folderPlayMode))
                            {
                                (iconKind, iconColor) = GetPlayModeIcon(folderPlayMode);
                            }
                            else
                            {
                                (iconKind, iconColor) = ("Shuffle", "#FF9800");  // 默认随机播放
                            }
                        }
                        else
                        {
                            // 非媒体文件夹显示手动排序图标
                            (iconKind, iconColor) = _originalManager.GetFolderIconKind(folderId.Value, true);
                        }
                        
                        folderItem.IconKind = iconKind;
                        folderItem.IconColor = iconColor;
                    }
                }
                else
                {
                    // 更新根目录文件顺序 - 这种情况比较复杂，暂时还是用LoadProjects
                    LoadProjects();
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"更新TreeView顺序失败: {ex}");
                // 如果轻量级更新失败，回退到完整刷新
                LoadProjects();
            }
        }

        /// <summary>
        /// 轻量级更新TreeView中的文件夹顺序（避免重新加载整个TreeView）
        /// </summary>
        private void UpdateFolderTreeItemOrder(List<Folder> sortedFolders)
        {
            try
            {
                // 创建一个字典来快速查找新的顺序索引
                var orderDict = sortedFolders.Select((f, index) => new { f.Id, Order = index })
                    .ToDictionary(x => x.Id, x => x.Order);
                
                // 对 _projectTreeItems 中的文件夹进行排序（排除Project节点）
                var folders = _projectTreeItems.Where(item => item.Type == TreeItemType.Folder).ToList();
                var nonFolders = _projectTreeItems.Where(item => item.Type != TreeItemType.Folder).ToList();
                
                // 根据新的OrderIndex排序文件夹
                folders = folders.OrderBy(f => orderDict.ContainsKey(f.Id) ? orderDict[f.Id] : int.MaxValue).ToList();
                
                // 清空并重新添加（保持正确顺序）
                _projectTreeItems.Clear();
                
                // 先添加非文件夹项（如Project节点）
                foreach (var item in nonFolders)
                {
                    _projectTreeItems.Add(item);
                }
                
                // 再添加排序后的文件夹
                foreach (var folder in folders)
                {
                    _projectTreeItems.Add(folder);
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"更新文件夹TreeView顺序失败: {ex}");
                #else
                _ = ex; // 避免未使用变量警告
                #endif
                // 如果轻量级更新失败，回退到完整刷新
                LoadProjects();
            }
        }

        /// <summary>
        /// 获取文件所属的文件夹ID
        /// </summary>
        private int? GetFileFolderId(ProjectTreeItem fileItem)
        {
            // 在_projectTreeItems中查找该文件所属的文件夹
            foreach (var item in _projectTreeItems)
            {
                if (item.Type == TreeItemType.Folder && item.Children != null)
                {
                    if (item.Children.Any(c => c.Id == fileItem.Id))
                    {
                        return item.Id;
                    }
                }
            }
            
            // 如果没找到，说明是根目录文件
            return null;
        }

        #endregion
    }
}
