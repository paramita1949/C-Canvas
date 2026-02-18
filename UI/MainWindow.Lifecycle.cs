using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using ImageColorChanger.Services;
using ImageColorChanger.Utils;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 窗口生命周期事件

        /// <summary>
        /// 窗口加载完成后执行初始化任务
        /// </summary>
        private async void Window_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            StartupPerfLogger.Mark("MainWindow.WindowLoaded.Begin");
            // 🔍 调试：输出幻灯片按钮的所有间距相关属性（已完成调试，注释掉）
            /*
            System.Diagnostics.Debug.WriteLine("========== 幻灯片按钮间距调试信息 ==========");

            var buttons = new[]
            {
                ("A+", BtnIncreaseFontSize),
                ("A-", BtnDecreaseFontSize),
                ("B", BtnBold),
                ("A", BtnTextColor)
            };

            foreach (var (name, btn) in buttons)
            {
                if (btn != null)
                {
                    System.Diagnostics.Debug.WriteLine($"\n【{name}】按钮:");
                    System.Diagnostics.Debug.WriteLine($"  Margin: {btn.Margin}");
                    System.Diagnostics.Debug.WriteLine($"  Padding: {btn.Padding}");
                    System.Diagnostics.Debug.WriteLine($"  BorderThickness: {btn.BorderThickness}");
                    System.Diagnostics.Debug.WriteLine($"  Width: {btn.Width}, Height: {btn.Height}");
                    System.Diagnostics.Debug.WriteLine($"  ActualWidth: {btn.ActualWidth}, ActualHeight: {btn.ActualHeight}");
                }
            }

            System.Diagnostics.Debug.WriteLine("\n==============================================");
            */

            // 恢复圣经历史记录+按钮区域高度
            if (BibleHistoryAndButtonRow != null && _configManager.BibleHistoryRowHeight > 0)
            {
                // 如果保存了高度值，使用固定高度
                BibleHistoryAndButtonRow.Height = new GridLength(_configManager.BibleHistoryRowHeight);
                // 经文选择表格使用剩余空间
                if (BibleSelectionTableRow != null)
                {
                    BibleSelectionTableRow.Height = new GridLength(1, GridUnitType.Star);
                }
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ 已恢复圣经历史记录+按钮区域高度: {_configManager.BibleHistoryRowHeight}");
#endif
            }
            
            // 初始化圣经服务
            InitializeBibleService();
            StartupPerfLogger.Mark("MainWindow.InitializeBibleService.Completed");

            // 🔄 静默同步所有文件夹（不显示状态提示）
            await Task.Run(() =>
            {
                try
                {
                    var importManager = ImportManagerService;
                    if (importManager != null)
                    {
                        importManager.SyncAllFolders();

                        // 在UI线程刷新项目树和搜索范围
                        Dispatcher.Invoke(() =>
                        {
                            LoadProjects();
                            LoadSearchScopes();
                        });
                    }
                }
                catch (Exception)
                {
                    // 静默失败，不影响用户使用
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[MainWindow] 启动时同步失败");
#endif
                }
            });
            StartupPerfLogger.Mark("MainWindow.StartupFolderSync.Completed");
            StartupPerfLogger.Mark("MainWindow.StartupCoreReady");

            // 延迟5秒后检查更新，避免影响启动速度
            await Task.Delay(5000);
            StartupPerfLogger.Mark("MainWindow.UpdateCheck.DelayElapsed");
            await CheckForUpdatesAsync();
            StartupPerfLogger.Mark("MainWindow.UpdateCheck.Completed");
        }

        /// <summary>
        /// 检查软件更新
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine("[MainWindow] 开始检查更新...");
//#endif
                var versionInfo = await UpdateService.CheckForUpdatesAsync();
                
                if (versionInfo != null)
                {
                    // 在UI线程显示更新窗口
                    Dispatcher.Invoke(() =>
                    {
                        var updateWindow = new UpdateWindow(versionInfo);
                        updateWindow.Owner = this;
                        updateWindow.ShowDialog();
                    });
                }
#if DEBUG
                else
                {
                    System.Diagnostics.Debug.WriteLine("[MainWindow] 当前已是最新版本");
                }
#endif
            }
            catch (Exception)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine("[MainWindow] 检查更新失败");
//#endif
                // 静默失败，不影响用户使用
            }
        }

        /// <summary>
        /// 窗口关闭事件 - 清理资源
        /// </summary>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine("🔚 主窗口正在关闭,清理资源...");
                MainWindow_Closing(sender, e);
                
                // 保存用户设置
                SaveSettings();
                
                // 注意：PropertyChanged事件使用匿名方法订阅，无法直接取消订阅
                // ViewModel会随窗口关闭自动释放
                // 如果需要，应在订阅时保存匿名方法引用以便取消订阅
                
                // 停止并清理视频播放器
                _mediaModuleController?.Shutdown();
                _mediaModuleController = null;
                _videoPlayerManager = null;
                
                // 关闭投影窗口
                if (_projectionManager != null)
                {
                    _projectionManager.CloseProjection();
                    _projectionManager.Dispose();
                }
                
                // 释放全局热键
                if (_globalHotKeyManager != null)
                {
                    _globalHotKeyManager.Dispose();
                }
                
                // 停止并释放FPS监控器
                if (_fpsMonitor != null)
                {
                    _fpsMonitor.StopMonitoring();
                    _fpsMonitor.Dispose();
                }
                
                // 🔐 清理认证服务
                CleanupAuthService();
                
                // 处理圣经历史记录
                HandleBibleHistoryOnClosing();
                
                // 清理数据库连接（关闭WAL文件）
                CleanupDatabase();
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 资源清理失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理圣经历史记录（退出时根据配置保存或清空）
        /// </summary>
        private void HandleBibleHistoryOnClosing()
        {
            try
            {
                if (_configManager.SaveBibleHistory)
                {
                    // 勾选了保存投影记录，保存当前历史记录
                    SaveBibleHistoryToConfig();
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine("[主窗口] 退出时已保存圣经历史记录");
                    //#endif
                }
                else
                {
                    // 没有勾选，清空历史记录
                    ClearAllBibleHistory();
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine("[主窗口] 退出时已清空圣经历史记录");
                    //#endif
                }
            }
            catch (Exception)
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[主窗口] 处理圣经历史记录失败: {ex.Message}");
                #endif
            }
        }
        
        /// <summary>
        /// 清理数据库连接，确保WAL文件被合并
        /// </summary>
        private void CleanupDatabase()
        {
            try
            {
                try
                {
                    DatabaseManagerService.CheckpointAndCloseConnections();
                }
                catch (Exception)
                {
                    // 忽略错误，不影响退出
                }

                // 释放UI持有的上下文引用（生命周期由 DatabaseManager 统一管理）
                _dbContext = null;
            }
            catch (Exception)
            {
                // 忽略所有数据库清理错误，不影响程序关闭
            }
        }

        #endregion
    }
}

