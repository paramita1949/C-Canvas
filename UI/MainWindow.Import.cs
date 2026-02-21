using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Managers;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private const string ImportExportLogPrefix = "[导入导出UI]";
        private static void LogImportExportInfo(string message)
        {
            // System.Diagnostics.Debug.WriteLine($"{ImportExportLogPrefix} {message}");
        }

        private static void LogImportExportError(string message)
        {
            // System.Diagnostics.Debug.WriteLine($"{ImportExportLogPrefix} [ERROR] {message}");
        }

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

            // 另存图片
            var saveImageItem = new MenuItem { Header = "另存图片" };
            saveImageItem.Click += (s, args) => SaveCurrentImage();
            contextMenu.Items.Add(saveImageItem);

            contextMenu.Items.Add(new Separator());

            // 数据库
            var migrationItem = new MenuItem { Header = "数据库" };

            // 导入数据库子菜单
            var importDbItem = new MenuItem { Header = "导入数据库" };
            importDbItem.Click += async (s, args) => await ImportDatabaseAsync();
            migrationItem.Items.Add(importDbItem);

            // 导出数据库子菜单
            var exportDbItem = new MenuItem { Header = "导出数据库" };
            exportDbItem.Click += async (s, args) => await ExportDatabaseAsync();
            migrationItem.Items.Add(exportDbItem);

            // 导入幻灯片子菜单
            var importSlideItem = new MenuItem { Header = "导入幻灯片" };
            importSlideItem.Click += async (s, args) => await ImportSlideProjectsAsync();
            migrationItem.Items.Add(importSlideItem);

            if (IsLyricsTransferFeatureEnabled)
            {
                var exportLyricsItem = new MenuItem { Header = "导出歌词" };
                exportLyricsItem.Click += async (s, args) => await ExportLyricsLibraryPackageAsync();
                migrationItem.Items.Add(exportLyricsItem);

                var importLyricsItem = new MenuItem { Header = "导入歌词" };
                importLyricsItem.Click += async (s, args) => await ImportLyricsPackageAsync();
                migrationItem.Items.Add(importLyricsItem);
            }

            contextMenu.Items.Add(migrationItem);

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

            // 菜单字号子菜单（扩展范围：12-40，适配小型笔记本）
            var menuFontSizeItem = new MenuItem { Header = "菜单字号" };
            foreach (var size in new[] { 12.0, 14.0, 16.0, 18.0, 20.0, 22.0, 24.0, 26.0, 28.0, 29.0, 30.0, 35.0, 40.0 })
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


            // 🆕 添加分隔符
            menuFontSizeItem.Items.Add(new Separator());

            // 🆕 自定义字号选项（下拉框，步长 0.5）
            var customFontSizeItem = new MenuItem { Header = "自定义..." };
            customFontSizeItem.Click += (s, args) => ShowCustomMenuFontSizeComboBox();
            menuFontSizeItem.Items.Add(customFontSizeItem);

            fontSizeItem.Items.Add(menuFontSizeItem);

            contextMenu.Items.Add(fontSizeItem);

            contextMenu.Items.Add(new Separator());

            // 版本管理（放在字号设置下方，保持在文件菜单最底部）
            var versionManageItem = new MenuItem { Header = "版本管理" };
            var versionUpgradeItem = new MenuItem { Header = "检查升级" };
            versionUpgradeItem.Click += async (s, args) => await OpenVersionUpgradeAsync();
            versionManageItem.Items.Add(versionUpgradeItem);

            var versionRollbackItem = new MenuItem { Header = "版本回退" };
            versionRollbackItem.Click += (s, args) => BtnRollback_Click(this, new RoutedEventArgs());
            versionManageItem.Items.Add(versionRollbackItem);

            contextMenu.Items.Add(versionManageItem);

            // 显示菜单
            contextMenu.PlacementTarget = BtnImport;
            contextMenu.IsOpen = true;
        }

        private async System.Threading.Tasks.Task OpenVersionUpgradeAsync()
        {
            try
            {
                var versionInfo = UpdateService.GetLastCheckedVersionInfo();
                if (versionInfo == null)
                {
                    versionInfo = await UpdateService.CheckForUpdatesAsync();
                }

                if (versionInfo == null)
                {
                    ShowStatus("✅ 当前已是最新版本");
                    return;
                }

                var updateWindow = new UpdateWindow(versionInfo)
                {
                    Owner = this
                };
                updateWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowStatus($"❌ 检查更新失败: {ex.Message}");
            }
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
                var mediaFile = ImportManagerService.ImportSingleFile(openFileDialog.FileName);
                if (mediaFile != null)
                {
                    LoadProjects(); // 刷新项目树
                    LoadSearchScopes(); // 刷新搜索范围
                    ShowStatus($"✅ 已导入: {mediaFile.Name}");
                }
                else if (!string.IsNullOrWhiteSpace(ImportManagerService.LastError))
                {
                    ShowStatus($"❌ {ImportManagerService.LastError}");
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
                var (folder, newFiles, existingFiles) = ImportManagerService.ImportFolder(selectedPath);
                
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
                else if (!string.IsNullOrWhiteSpace(ImportManagerService.LastError))
                {
                    ShowStatus($"❌ {ImportManagerService.LastError}");
                }
            }
        }

        /// <summary>
        /// 保存当前图片
        /// </summary>
        private void SaveCurrentImage()
        {
            if (_imageProcessor == null)
            {
                return;
            }

            var imageSaveManager = new ImageSaveManager(_imageProcessor);
            var saved = imageSaveManager.SaveEffectImage(_imagePath);
            if (!saved && !string.IsNullOrWhiteSpace(imageSaveManager.LastError))
            {
                ShowStatus($"❌ {imageSaveManager.LastError}");
            }
        }

        /// <summary>
        /// 导入幻灯片项目
        /// </summary>
        private async System.Threading.Tasks.Task ImportSlideProjectsAsync()
        {
            var slideImportManager = _mainWindowServices.GetRequired<SlideImportManager>();
            if (slideImportManager != null)
            {
                int count = await slideImportManager.ImportProjectsAsync();
                if (count > 0)
                {
                    LoadProjects(); // 刷新项目树
                    ShowStatus($"✅ 已导入 {count} 个幻灯片项目");
                }
                else if (!string.IsNullOrWhiteSpace(slideImportManager.LastError))
                {
                    ShowStatus($"❌ {slideImportManager.LastError}");
                }
            }
        }

        /// <summary>
        /// 导出数据库
        /// </summary>
        private async System.Threading.Tasks.Task ExportDatabaseAsync()
        {
            try
            {
                LogImportExportInfo("[ExportDB-Begin] open save dialog");
                // 创建保存文件对话框
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出数据库",
                    Filter = "数据库压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    FileName = $"pyimages_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                    DefaultExt = ".zip"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    LogImportExportInfo($"[ExportDB-Select] target={saveDialog.FileName}");
                    var migrationService = new Services.DatabaseMigrationService();
                    var result = await migrationService.ExportDatabaseAsync(saveDialog.FileName);
                    LogImportExportInfo($"[ExportDB-End] success={result?.Success}, title={result?.Title}");
                    ShowMigrationResult(result);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导出数据库时发生错误：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                LogImportExportError($"[ExportDB-Fail] {ex}");
            }
        }

        /// <summary>
        /// 导入数据库
        /// </summary>
        private async System.Threading.Tasks.Task ImportDatabaseAsync()
        {
            try
            {
                LogImportExportInfo("[ImportDB-Begin] open file dialog");
                // 创建打开文件对话框
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "导入数据库",
                    Filter = "数据库压缩包 (*.zip)|*.zip|数据库文件 (*.db)|*.db|所有文件 (*.*)|*.*",
                    DefaultExt = ".zip"
                };

                if (openDialog.ShowDialog() == true)
                {
                    LogImportExportInfo($"[ImportDB-Select] source={openDialog.FileName}");
                    // 确认导入操作
                    var confirmResult = System.Windows.MessageBox.Show(
                        "导入数据库将覆盖当前数据库（会自动备份当前数据库和缩略图）。\n\n确定要继续吗？",
                        "确认导入",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);

                    if (confirmResult == System.Windows.MessageBoxResult.Yes)
                    {
                        LogImportExportInfo("[ImportDB-Confirm] user confirmed import");
                        var migrationService = new Services.DatabaseMigrationService();
                        var result = await migrationService.ImportDatabaseAsync(openDialog.FileName);
                        LogImportExportInfo($"[ImportDB-End] success={result?.Success}, requiresRestart={result?.RequiresRestart}");
                        ShowMigrationResult(result);

                        if (result.Success && result.RequiresRestart)
                        {
                            var restartResult = System.Windows.MessageBox.Show(
                                "检测到数据库已更新，是否立即重启应用以使更改生效？",
                                "需要重启",
                                System.Windows.MessageBoxButton.YesNo,
                                System.Windows.MessageBoxImage.Question);

                            if (restartResult == System.Windows.MessageBoxResult.Yes)
                            {
                                TryRestartApplication();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导入数据库时发生错误：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                LogImportExportError($"[ImportDB-Fail] {ex}");
            }
        }

        private Services.LyricsTransferService CreateLyricsTransferService()
        {
            return new Services.LyricsTransferService(_dbContext);
        }

        internal async System.Threading.Tasks.Task ExportLyricsSongPackageAsync(int songId)
        {
            if (!IsLyricsTransferFeatureEnabled)
            {
                ShowStatus("⚠️ 歌词导入导出功能已关闭");
                return;
            }

            try
            {
                LogImportExportInfo($"[ExportLyrSong-Begin] songId={songId}");
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出歌曲歌词",
                    Filter = "歌词包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    DefaultExt = ".zip",
                    FileName = $"song_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
                };
                if (saveDialog.ShowDialog() != true)
                {
                    return;
                }

                LogImportExportInfo($"[ExportLyrSong-Select] target={saveDialog.FileName}");
                var service = CreateLyricsTransferService();
                var result = await service.ExportSongAsync(songId, saveDialog.FileName);
                LogImportExportInfo($"[ExportLyrSong-End] success={result?.Success}, msg={result?.Message}");
                ShowStatus(result.Success ? "✅ 歌曲歌词导出成功" : $"❌ {result.Message}");
            }
            catch (Exception ex)
            {
                LogImportExportError($"[ExportLyrSong-Fail] songId={songId}, err={ex}");
                ShowStatus($"❌ 导出歌曲歌词失败: {ex.Message}");
            }
        }

        internal async System.Threading.Tasks.Task ExportLyricsGroupPackageAsync(int groupId)
        {
            if (!IsLyricsTransferFeatureEnabled)
            {
                ShowStatus("⚠️ 歌词导入导出功能已关闭");
                return;
            }

            try
            {
                LogImportExportInfo($"[ExportLyrGroup-Begin] groupId={groupId}");
                string defaultBaseName = $"group_{DateTime.Now:yyyyMMdd_HHmmss}";
                try
                {
                    string groupName = _dbContext?.LyricsGroups
                        .Where(g => g.Id == groupId)
                        .Select(g => g.Name)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(groupName))
                    {
                        defaultBaseName = SanitizeExportBaseName(groupName);
                    }
                }
                catch
                {
                    // 读取分组名失败时回退时间戳文件名
                }

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出歌词分组",
                    Filter = "歌词包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    DefaultExt = ".zip",
                    FileName = $"{defaultBaseName}.zip"
                };
                if (saveDialog.ShowDialog() != true)
                {
                    return;
                }

                LogImportExportInfo($"[ExportLyrGroup-Select] target={saveDialog.FileName}");
                var service = CreateLyricsTransferService();
                var result = await service.ExportGroupAsync(groupId, saveDialog.FileName);
                LogImportExportInfo($"[ExportLyrGroup-End] success={result?.Success}, msg={result?.Message}");
                ShowStatus(result.Success ? "✅ 分组歌词导出成功" : $"❌ {result.Message}");
            }
            catch (Exception ex)
            {
                LogImportExportError($"[ExportLyrGroup-Fail] groupId={groupId}, err={ex}");
                ShowStatus($"❌ 导出分组歌词失败: {ex.Message}");
            }
        }

        private static string SanitizeExportBaseName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return $"group_{DateTime.Now:yyyyMMdd_HHmmss}";
            }

            string trimmed = rawName.Trim();
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            var buffer = new System.Text.StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                bool invalid = false;
                for (int i = 0; i < invalidChars.Length; i++)
                {
                    if (c == invalidChars[i])
                    {
                        invalid = true;
                        break;
                    }
                }

                buffer.Append(invalid ? '_' : c);
            }

            string sanitized = buffer.ToString().Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(sanitized)
                ? $"group_{DateTime.Now:yyyyMMdd_HHmmss}"
                : sanitized;
        }

        internal async System.Threading.Tasks.Task ExportLyricsLibraryPackageAsync()
        {
            if (!IsLyricsTransferFeatureEnabled)
            {
                ShowStatus("⚠️ 歌词导入导出功能已关闭");
                return;
            }

            try
            {
                LogImportExportInfo("[ExportLyrLib-Begin]");
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出歌词库",
                    Filter = "歌词包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    DefaultExt = ".zip",
                    FileName = $"lyrics_library_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
                };
                if (saveDialog.ShowDialog() != true)
                {
                    return;
                }

                LogImportExportInfo($"[ExportLyrLib-Select] target={saveDialog.FileName}");
                var service = CreateLyricsTransferService();
                var result = await service.ExportLibraryAsync(saveDialog.FileName);
                LogImportExportInfo($"[ExportLyrLib-End] success={result?.Success}, msg={result?.Message}");
                ShowStatus(result.Success ? "✅ 歌词库导出成功" : $"❌ {result.Message}");
            }
            catch (Exception ex)
            {
                LogImportExportError($"[ExportLyrLib-Fail] {ex}");
                ShowStatus($"❌ 导出歌词库失败: {ex.Message}");
            }
        }

        internal async System.Threading.Tasks.Task ImportLyricsPackageAsync()
        {
            if (!IsLyricsTransferFeatureEnabled)
            {
                ShowStatus("⚠️ 歌词导入导出功能已关闭");
                return;
            }

            try
            {
                LogImportExportInfo("[ImportLyr-Begin] open file dialog");
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "导入歌词包",
                    Filter = "歌词包 (*.zip)|*.zip",
                    DefaultExt = ".zip"
                };
                if (openDialog.ShowDialog() != true)
                {
                    return;
                }

                var service = CreateLyricsTransferService();
                int conflictCount = await service.CountConflictsAsync(openDialog.FileName);
                Services.LyricsImportConflictStrategy strategy;
                if (conflictCount > 0)
                {
                    var selected = SelectLyricsImportStrategy();
                    if (!selected.HasValue)
                    {
                        return;
                    }

                    strategy = selected.Value;
                }
                else
                {
                    strategy = Services.LyricsImportConflictStrategy.Skip;
                    LogImportExportInfo("[ImportLyr-Precheck] no conflicts, strategy dialog skipped");
                }

                LogImportExportInfo($"[ImportLyr-Select] source={openDialog.FileName}, conflicts={conflictCount}, strategy={strategy}");
                var result = await service.ImportAsync(openDialog.FileName, strategy);
                if (result.Success)
                {
                    LoadProjects();
                }
                LogImportExportInfo($"[ImportLyr-End] success={result?.Success}, imported={result?.Imported}, overwritten={result?.Overwritten}, copied={result?.Copied}, skipped={result?.Skipped}, failed={result?.Failed}");
                ShowStatus(result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}");
                System.Windows.MessageBox.Show(
                    result.Message,
                    result.Success ? "歌词导入结果" : "歌词导入失败",
                    System.Windows.MessageBoxButton.OK,
                    result.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                LogImportExportError($"[ImportLyr-Fail] {ex}");
                ShowStatus($"❌ 导入歌词包失败: {ex.Message}");
            }
        }

        private Services.LyricsImportConflictStrategy? SelectLyricsImportStrategy()
        {
            var dialog = new Window
            {
                Title = "选择冲突策略",
                Width = 420,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            Services.LyricsImportConflictStrategy? result = null;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tip = new TextBlock
            {
                Text = "检测到同名/同ID歌曲时，选择导入策略：",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(tip, 0);
            grid.Children.Add(tip);

            var panel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            var skip = new System.Windows.Controls.Button { Content = "跳过", Width = 96, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
            var overwrite = new System.Windows.Controls.Button { Content = "覆盖", Width = 96, Height = 34, Margin = new Thickness(0, 0, 8, 0) };
            var copy = new System.Windows.Controls.Button { Content = "另存副本", Width = 96, Height = 34 };
            skip.Click += (_, __) => { result = Services.LyricsImportConflictStrategy.Skip; dialog.DialogResult = true; dialog.Close(); };
            overwrite.Click += (_, __) => { result = Services.LyricsImportConflictStrategy.Overwrite; dialog.DialogResult = true; dialog.Close(); };
            copy.Click += (_, __) => { result = Services.LyricsImportConflictStrategy.SaveAsCopy; dialog.DialogResult = true; dialog.Close(); };
            panel.Children.Add(skip);
            panel.Children.Add(overwrite);
            panel.Children.Add(copy);
            Grid.SetRow(panel, 2);
            grid.Children.Add(panel);

            dialog.Content = grid;
            dialog.ShowDialog();
            return result;
        }

        private void ShowMigrationResult(Services.DatabaseMigrationResult result)
        {
            if (result == null)
            {
                return;
            }

            var icon = result.Level switch
            {
                Services.DatabaseMigrationMessageLevel.Error => System.Windows.MessageBoxImage.Error,
                Services.DatabaseMigrationMessageLevel.Warning => System.Windows.MessageBoxImage.Warning,
                _ => System.Windows.MessageBoxImage.Information
            };

            System.Windows.MessageBox.Show(
                result.Message ?? string.Empty,
                string.IsNullOrWhiteSpace(result.Title) ? "提示" : result.Title,
                System.Windows.MessageBoxButton.OK,
                icon);
        }

        private void TryRestartApplication()
        {
            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    System.Windows.MessageBox.Show("无法获取当前程序路径，请手动重启应用。", "重启失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                System.Diagnostics.Process.Start(exePath);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"自动重启失败，请手动重启应用程序。\n\n错误：{ex.Message}", "重启失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 显示自定义菜单字号下拉框（步长 0.5，支持鼠标滚动）
        /// </summary>
        private void ShowCustomMenuFontSizeComboBox()
        {
            try
            {
                // 创建对话框
                var dialog = new Window
                {
                    Title = "自定义菜单字号",
                    Width = 320,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.ToolWindow
                };

                var grid = new Grid { Margin = new Thickness(20) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // 提示文本
                var label = new TextBlock
                {
                    Text = "选择菜单字号（步长 0.5）：",
                    FontSize = 14
                };
                Grid.SetRow(label, 0);
                grid.Children.Add(label);

                // 下拉框（生成 12-40，步长 0.5）
                var comboBox = new System.Windows.Controls.ComboBox
                {
                    FontSize = 16,
                    Height = 36,
                    IsEditable = false
                };

                // 生成选项：12, 12.5, 13, 13.5, ..., 39.5, 40
                for (double size = 12.0; size <= 40.0; size += 0.5)
                {
                    comboBox.Items.Add(size);
                }

                // 设置当前值
                double currentSize = _configManager.MenuFontSize;
                if (comboBox.Items.Contains(currentSize))
                {
                    comboBox.SelectedItem = currentSize;
                }
                else
                {
                    // 如果当前值不在列表中，选择最接近的值
                    double closestSize = 18.0;
                    double minDiff = double.MaxValue;
                    foreach (double size in comboBox.Items)
                    {
                        double diff = Math.Abs(size - currentSize);
                        if (diff < minDiff)
                        {
                            minDiff = diff;
                            closestSize = size;
                        }
                    }
                    comboBox.SelectedItem = closestSize;
                }

                Grid.SetRow(comboBox, 2);
                grid.Children.Add(comboBox);

                // 按钮面板
                var buttonPanel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };
                Grid.SetRow(buttonPanel, 4);

                var okButton = new System.Windows.Controls.Button
                {
                    Content = "确定",
                    Width = 80,
                    Height = 32,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };
                okButton.Click += (s, e) =>
                {
                    if (comboBox.SelectedItem != null)
                    {
                        double size = (double)comboBox.SelectedItem;
                        SetMenuFontSize(size);
                        dialog.DialogResult = true;
                        dialog.Close();
                    }
                };
                buttonPanel.Children.Add(okButton);

                var cancelButton = new System.Windows.Controls.Button
                {
                    Content = "取消",
                    Width = 80,
                    Height = 32,
                    IsCancel = true
                };
                cancelButton.Click += (s, e) =>
                {
                    dialog.DialogResult = false;
                    dialog.Close();
                };
                buttonPanel.Children.Add(cancelButton);

                grid.Children.Add(buttonPanel);
                dialog.Content = grid;

                // 聚焦下拉框
                comboBox.Focus();

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 显示自定义字号下拉框异常: {ex}");
            }
        }

        #endregion
    }
}

