using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    /// MainWindow 的项目树操作和辅助方法
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 项目树操作

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
                _dbManager.DeleteFolder(item.Id, forceDelete);
                LoadProjects();           // 刷新项目树
                LoadSearchScopes();       // 刷新搜索范围
                
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
                    // 普通删除失败，询问是否强制删除
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
                        // 用户选择强制删除
                        TryDeleteFolder(item, forceDelete: true);
                    }
                    else
                    {
                        ShowStatus($"❌ 取消删除文件夹: {item.Name}");
                    }
                }
                else
                {
                    // 强制删除也失败了
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
                
                #if DEBUG
                //Debug.WriteLine($"🎨 [变色标记] 标记文件夹: {item.Name} (ID={item.Id})");
                //Debug.WriteLine($"   当前文件夹ID: {_currentFolderId}");
                //Debug.WriteLine($"   当前图片ID: {_currentImageId}");
                //Debug.WriteLine($"   当前图片是否加载: {_imageProcessor.CurrentImage != null}");
                //Debug.WriteLine($"   当前变色状态: {_isColorEffectEnabled}");
                #endif
                
                // 🔧 检查当前显示的图片是否属于这个文件夹
                bool shouldApplyEffect = false;
                
                if (_currentImageId > 0 && _imageProcessor.CurrentImage != null)
                {
                    var currentMediaFile = _dbManager.GetMediaFileById(_currentImageId);
                    if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                    {
                        shouldApplyEffect = (currentMediaFile.FolderId.Value == item.Id);
                        
                        #if DEBUG
                        //Debug.WriteLine($"   当前图片所属文件夹ID: {currentMediaFile.FolderId.Value}");
                        //Debug.WriteLine($"   是否属于标记的文件夹: {shouldApplyEffect}");
                        #endif
                    }
                }
                
                // 如果当前显示的图片属于这个文件夹，立即启用变色效果
                if (shouldApplyEffect)
                {
                    #if DEBUG
                    //Debug.WriteLine($"🎨 [变色标记] 条件满足，开始应用变色效果");
                    #endif
                    
                    _isColorEffectEnabled = true;
                    _imageProcessor.IsInverted = true;
                    BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // 金色
                    
                    // 更新当前文件夹ID
                    _currentFolderId = item.Id;
                    
                    // 刷新当前图片显示
                    _imageProcessor.UpdateImage();
                    
                    // 更新投影
                    UpdateProjection();
                    
                    #if DEBUG
                    //Debug.WriteLine($"🎨 [变色标记] 已启用当前文件夹的变色效果: {item.Name}");
                    #endif
                    
                    ShowStatus($"✅ 已标记文件夹 [{item.Name}] 自动变色（当前图片已应用变色效果）");
                }
                else
                {
                    #if DEBUG
                    //Debug.WriteLine($"🎨 [变色标记] 当前未显示该文件夹的图片，标记已保存");
                    #endif
                    
                    ShowStatus($"✅ 已标记文件夹 [{item.Name}] 自动变色（点击图片时将自动应用）");
                }
            }
            catch (Exception)
            {
                #if DEBUG
                //Debug.WriteLine($"❌ 标记变色失败: {ex.Message}");
                #endif
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
                
                #if DEBUG
                //Debug.WriteLine($"🎨 [取消变色] 取消文件夹标记: {item.Name} (ID={item.Id})");
                //Debug.WriteLine($"   当前文件夹ID: {_currentFolderId}");
                //Debug.WriteLine($"   当前图片ID: {_currentImageId}");
                //Debug.WriteLine($"   当前图片是否加载: {_imageProcessor.CurrentImage != null}");
                //Debug.WriteLine($"   当前变色状态: {_isColorEffectEnabled}");
                #endif
                
                // 🔧 检查当前显示的图片是否属于这个文件夹
                bool shouldRemoveEffect = false;
                
                if (_currentImageId > 0 && _imageProcessor.CurrentImage != null)
                {
                    var currentMediaFile = _dbManager.GetMediaFileById(_currentImageId);
                    if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                    {
                        shouldRemoveEffect = (currentMediaFile.FolderId.Value == item.Id);
                        
                        #if DEBUG
                        //Debug.WriteLine($"   当前图片所属文件夹ID: {currentMediaFile.FolderId.Value}");
                        //Debug.WriteLine($"   是否属于取消标记的文件夹: {shouldRemoveEffect}");
                        #endif
                    }
                }
                
                // 如果当前显示的图片属于这个文件夹，立即关闭变色效果
                if (shouldRemoveEffect)
                {
                    #if DEBUG
                    //Debug.WriteLine($"🎨 [取消变色] 条件满足，开始取消变色效果");
                    #endif
                    
                    _isColorEffectEnabled = false;
                    _imageProcessor.IsInverted = false;
                    BtnColorEffect.Background = Brushes.Transparent;
                    
                    // 刷新当前图片显示
                    _imageProcessor.UpdateImage();
                    
                    // 更新投影
                    UpdateProjection();
                    
                    #if DEBUG
                    //Debug.WriteLine($"🎨 [取消变色] 已关闭当前文件夹的变色效果: {item.Name}");
                    #endif
                    
                    ShowStatus($"✅ 已取消文件夹 [{item.Name}] 的变色标记（当前图片已恢复正常）");
                }
                else
                {
                    #if DEBUG
                    //Debug.WriteLine($"🎨 [取消变色] 当前未显示该文件夹的图片，标记已清除");
                    #endif
                    
                    ShowStatus($"✅ 已取消文件夹 [{item.Name}] 的变色标记");
                }
            }
            catch (Exception)
            {
                #if DEBUG
                //Debug.WriteLine($"❌ 取消变色标记失败: {ex.Message}");
                #endif
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
                        // 🔑 关键：使用物理文件名（从Path获取）进行排序，而不是显示名称
                        var sortedFiles = files
                            .Select(f => new
                            {
                                File = f,
                                OriginalFileName = System.IO.Path.GetFileName(f.Path),
                                SortKey = _sortManager.GetSortKey(System.IO.Path.GetFileName(f.Path))
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

                        // 🔑 恢复显示名称为物理文件名（去掉扩展名，保持与导入时一致）
                        for (int i = 0; i < sortedFiles.Count; i++)
                        {
                            string originalFileName = System.IO.Path.GetFileNameWithoutExtension(sortedFiles[i].Path);
                            sortedFiles[i].Name = originalFileName;
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
