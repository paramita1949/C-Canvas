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
    /// MainWindow 的外部文件拖拽导入功能
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 外部文件/文件夹拖拽导入

        /// <summary>
        /// 外部拖拽进入事件 - 检测是否为文件/文件夹
        /// </summary>
        private void ProjectTree_ExternalDragEnter(object sender, DragEventArgs e)
        {
            try
            {
                // 检查是否包含文件
                if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            catch (Exception)
            {
                e.Effects = DragDropEffects.None;
            }
        }

        /// <summary>
        /// 外部拖拽悬停事件 - 显示拖拽效果
        /// </summary>
        private void ProjectTree_ExternalDragOver(object sender, DragEventArgs e)
        {
            try
            {
                // 检查是否为内部拖拽（ProjectTreeItem）
                if (e.Data.GetDataPresent(typeof(ProjectTreeItem)))
                {
                    // 内部拖拽，使用原有的 ProjectTree_DragOver 逻辑
                    ProjectTree_DragOver(sender, e);
                    return;
                }

                // 外部拖拽，检查是否包含文件
                if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    string[] paths = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
                    
                    if (paths != null && paths.Length > 0)
                    {
                        // 检查是否至少有一个文件夹或支持的文件
                        bool hasValidItem = false;
                        foreach (var path in paths)
                        {
                            if (System.IO.Directory.Exists(path))
                            {
                                hasValidItem = true;
                                break;
                            }
                            else if (System.IO.File.Exists(path))
                            {
                                var extension = System.IO.Path.GetExtension(path).ToLower();
                                if (Managers.ImportManager.AllExtensions.Contains(extension))
                                {
                                    hasValidItem = true;
                                    break;
                                }
                            }
                        }
                        
                        e.Effects = hasValidItem ? DragDropEffects.Copy : DragDropEffects.None;
                    }
                    else
                    {
                        e.Effects = DragDropEffects.None;
                    }
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
                
                e.Handled = true;
            }
            catch (Exception)
            {
                e.Effects = DragDropEffects.None;
            }
        }

        /// <summary>
        /// 外部拖拽离开事件
        /// </summary>
        private void ProjectTree_ExternalDragLeave(object sender, DragEventArgs e)
        {
            // 如果是内部拖拽，调用原有逻辑
            if (e.Data.GetDataPresent(typeof(ProjectTreeItem)))
            {
                ProjectTree_DragLeave(sender, e);
            }
        }

        /// <summary>
        /// 外部文件/文件夹拖放事件 - 执行导入
        /// </summary>
        private void ProjectTree_ExternalDrop(object sender, DragEventArgs e)
        {
            try
            {
                // 检查是否为内部拖拽（ProjectTreeItem）
                if (e.Data.GetDataPresent(typeof(ProjectTreeItem)))
                {
                    // 内部拖拽，使用原有的 ProjectTree_Drop 逻辑
                    ProjectTree_Drop(sender, e);
                    return;
                }

                // 外部拖拽，检查是否包含文件
                if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    string[] paths = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
                    
                    if (paths != null && paths.Length > 0)
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"📁 拖拽导入 {paths.Length} 个项目");
                        #endif

                        int importedFolderCount = 0;
                        int importedFileCount = 0;
                        int totalNewFiles = 0;
                        string lastError = null;

                        foreach (var path in paths)
                        {
                            if (System.IO.Directory.Exists(path))
                            {
                                // 导入文件夹
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"📁 导入文件夹: {path}");
                                #endif

                                var (folder, newFiles, existingFiles) = ImportManagerService.ImportFolder(path);
                                
                                if (folder != null)
                                {
                                    importedFolderCount++;
                                    totalNewFiles += newFiles?.Count ?? 0;
                                    
                                    #if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"✅ 文件夹导入成功: {folder.Name} (新增 {newFiles?.Count ?? 0} 个文件)");
                                    #endif
                                }
                                else if (!string.IsNullOrWhiteSpace(ImportManagerService.LastError))
                                {
                                    lastError = ImportManagerService.LastError;
                                }
                            }
                            else if (System.IO.File.Exists(path))
                            {
                                // 导入单个文件
                                var extension = System.IO.Path.GetExtension(path).ToLower();
                                if (Managers.ImportManager.AllExtensions.Contains(extension))
                                {
                                    #if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"📄 导入文件: {path}");
                                    #endif

                                    var mediaFile = ImportManagerService.ImportSingleFile(path);
                                    
                                    if (mediaFile != null)
                                    {
                                        importedFileCount++;
                                        
                                        #if DEBUG
                                        System.Diagnostics.Debug.WriteLine($"✅ 文件导入成功: {mediaFile.Name}");
                                        #endif
                                    }
                                    else if (!string.IsNullOrWhiteSpace(ImportManagerService.LastError))
                                    {
                                        lastError = ImportManagerService.LastError;
                                    }
                                }
                            }
                        }

                        // 刷新UI
                        if (importedFolderCount > 0 || importedFileCount > 0)
                        {
                            LoadProjects(); // 刷新项目树
                            LoadSearchScopes(); // 刷新搜索范围
                            
                            // 🔧 清除缓存，确保使用最新的数据库数据
                            _originalManager?.ClearCache();
                            
                            // ⚡ 清除图片LRU缓存
                            _imageProcessor?.ClearImageCache();
                            
                            // ⚡ 清除投影缓存
                            _projectionManager?.ClearProjectionCache();

                            // 显示导入结果
                            if (importedFolderCount > 0 && importedFileCount > 0)
                            {
                                ShowStatus($"✅ 已导入 {importedFolderCount} 个文件夹和 {importedFileCount} 个文件 (共 {totalNewFiles + importedFileCount} 个新文件)");
                            }
                            else if (importedFolderCount > 0)
                            {
                                ShowStatus($"✅ 已导入 {importedFolderCount} 个文件夹 (共 {totalNewFiles} 个新文件)");
                            }
                            else
                            {
                                ShowStatus($"✅ 已导入 {importedFileCount} 个文件");
                            }
                        }
                        else
                        {
                            ShowStatus(!string.IsNullOrWhiteSpace(lastError) ? $"❌ {lastError}" : "❌ 没有导入任何文件");
                        }
                    }
                }
                
                e.Handled = true;
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ 外部拖拽导入失败: {ex.Message}");
                #endif
                ShowStatus($"❌ 导入失败: {ex.Message}");
            }
        }

        #endregion
    }
}
