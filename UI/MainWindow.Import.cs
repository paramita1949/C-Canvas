using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Managers;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 导入文件相关

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            // 创建导入菜单
            var contextMenu = new ContextMenu();
            
            // 🔑 应用自定义样式
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");

            // 导入单个文件
            var importFileItem = new MenuItem { Header = "导入文件" };
            importFileItem.Click += (s, args) => ImportSingleFile();
            contextMenu.Items.Add(importFileItem);

            // 导入文件夹
            var importFolderItem = new MenuItem { Header = "导入文件夹" };
            importFolderItem.Click += (s, args) => ImportFolder();
            contextMenu.Items.Add(importFolderItem);

            contextMenu.Items.Add(new Separator());

            // 导入幻灯片
            var importSlideItem = new MenuItem { Header = "导入幻灯片" };
            importSlideItem.Click += async (s, args) => await ImportSlideProjectsAsync();
            contextMenu.Items.Add(importSlideItem);

            contextMenu.Items.Add(new Separator());

            // 另存图片
            var saveImageItem = new MenuItem { Header = "另存图片" };
            saveImageItem.Click += (s, args) => SaveCurrentImage();
            contextMenu.Items.Add(saveImageItem);

            contextMenu.Items.Add(new Separator());

            // 字号设置
            var fontSizeItem = new MenuItem { Header = "字号设置" };
            
            // 文件夹字号子菜单
            var folderFontSizeItem = new MenuItem { Header = "文件夹字号" };
            foreach (var size in new[] { 13.0, 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0, 22.0, 24.0, 26.0, 28.0, 30.0 })
            {
                var menuItem = new MenuItem 
                { 
                    Header = $"{size}",
                    IsCheckable = true,
                    IsChecked = Math.Abs(_configManager.FolderFontSize - size) < 0.1
                };
                menuItem.Click += (s, args) => SetFolderFontSize(size);
                folderFontSizeItem.Items.Add(menuItem);
            }
            fontSizeItem.Items.Add(folderFontSizeItem);

            // 文件字号子菜单
            var fileFontSizeItem = new MenuItem { Header = "文件字号" };
            foreach (var size in new[] { 13.0, 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0, 22.0, 24.0, 26.0, 28.0, 30.0 })
            {
                var menuItem = new MenuItem 
                { 
                    Header = $"{size}",
                    IsCheckable = true,
                    IsChecked = Math.Abs(_configManager.FileFontSize - size) < 0.1
                };
                menuItem.Click += (s, args) => SetFileFontSize(size);
                fileFontSizeItem.Items.Add(menuItem);
            }
            fontSizeItem.Items.Add(fileFontSizeItem);

            // 文件夹标签字号子菜单（搜索结果显示）
            var folderTagFontSizeItem = new MenuItem { Header = "文件夹标签字号" };
            foreach (var size in new[] { 8.0, 9.0, 10.0, 11.0, 12.0, 13.0, 14.0, 15.0, 16.0, 18.0, 20.0 })
            {
                var menuItem = new MenuItem 
                { 
                    Header = $"{size}",
                    IsCheckable = true,
                    IsChecked = Math.Abs(_configManager.FolderTagFontSize - size) < 0.1
                };
                menuItem.Click += (s, args) => SetFolderTagFontSize(size);
                folderTagFontSizeItem.Items.Add(menuItem);
            }
            fontSizeItem.Items.Add(folderTagFontSizeItem);
            
            // 菜单字号子菜单（按照Python版本：18-40）
            var menuFontSizeItem = new MenuItem { Header = "菜单字号" };
            foreach (var size in new[] { 18.0, 20.0, 22.0, 24.0, 26.0, 28.0, 30.0, 35.0, 40.0 })
            {
                var menuItem = new MenuItem 
                { 
                    Header = $"{size}",
                    IsCheckable = true,
                    IsChecked = Math.Abs(_configManager.MenuFontSize - size) < 0.1
                };
                menuItem.Click += (s, args) => SetMenuFontSize(size);
                menuFontSizeItem.Items.Add(menuItem);
            }
            fontSizeItem.Items.Add(menuFontSizeItem);

            contextMenu.Items.Add(fontSizeItem);

            // 显示菜单
            contextMenu.PlacementTarget = BtnImport;
            contextMenu.IsOpen = true;
        }

        /// <summary>
        /// 导入单个文件
        /// </summary>
        private void ImportSingleFile()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = ImportManager.GetFileDialogFilter(),
                Title = "选择媒体文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var mediaFile = _importManager.ImportSingleFile(openFileDialog.FileName);
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
            // 使用 WPF 文件夹选择器
            string selectedPath = WpfFolderBrowserHelper.SelectFolder(
                title: "选择要导入的文件夹",
                owner: this
            );

            if (!string.IsNullOrEmpty(selectedPath))
            {
                var (folder, newFiles, existingFiles) = _importManager.ImportFolder(selectedPath);
                
                if (folder != null)
                {
                    LoadProjects(); // 刷新项目树
                    LoadSearchScopes(); // 刷新搜索范围
                    
                    // 🔧 清除缓存，确保使用最新的数据库数据
                    _originalManager?.ClearCache();
                    
                    // ⚡ 清除图片LRU缓存
                    _imageProcessor?.ClearImageCache();
                    
                    // ⚡ 清除投影缓存
                    _projectionManager?.ClearProjectionCache();
                    
                    //System.Diagnostics.Debug.WriteLine("🔄 文件夹导入完成，已清除所有缓存");
                    
                    ShowStatus($"✅ 已导入文件夹: {folder.Name} (新增 {newFiles.Count} 个文件)");
                }
            }
        }

        /// <summary>
        /// 保存当前图片
        /// </summary>
        private void SaveCurrentImage()
        {
            if (_imageSaveManager != null)
            {
                _imageSaveManager.SaveEffectImage(_imagePath);
            }
        }

        /// <summary>
        /// 导入幻灯片项目
        /// </summary>
        private async System.Threading.Tasks.Task ImportSlideProjectsAsync()
        {
            if (_slideImportManager != null)
            {
                int count = await _slideImportManager.ImportProjectsAsync();
                if (count > 0)
                {
                    LoadProjects(); // 刷新项目树
                    ShowStatus($"✅ 已导入 {count} 个幻灯片项目");
                }
            }
        }

        #endregion
    }
}

