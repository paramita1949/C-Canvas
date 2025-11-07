using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Services;

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
            // 初始化圣经服务
            InitializeBibleService();

            // 🔄 静默同步所有文件夹（不显示状态提示）
            await Task.Run(() =>
            {
                try
                {
                    if (_importManager != null)
                    {
                        _importManager.SyncAllFolders();

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

            // 延迟5秒后检查更新，避免影响启动速度
            await Task.Delay(5000);
            await CheckForUpdatesAsync();
        }

        /// <summary>
        /// 检查软件更新
        /// </summary>
        private async Task CheckForUpdatesAsync()
        {
            try
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[MainWindow] 开始检查更新...");
#endif
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
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[MainWindow] 检查更新失败");
#endif
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
                
                // 保存用户设置
                SaveSettings();
                
                // 取消订阅事件，防止内存泄漏
                if (_videoPlayerManager != null)
                {
                    _videoPlayerManager.VideoTrackDetected -= VideoPlayerManager_VideoTrackDetected;
                    _videoPlayerManager.PlayStateChanged -= OnVideoPlayStateChanged;
                    _videoPlayerManager.MediaChanged -= OnVideoMediaChanged;
                    _videoPlayerManager.MediaEnded -= OnVideoMediaEnded;
                    _videoPlayerManager.ProgressUpdated -= OnVideoProgressUpdated;
                }
                
                // 注意：PropertyChanged事件使用匿名方法订阅，无法直接取消订阅
                // ViewModel会随窗口关闭自动释放
                // 如果需要，应在订阅时保存匿名方法引用以便取消订阅
                
                // 停止并清理视频播放器
                if (_videoPlayerManager != null)
                {
                    _videoPlayerManager.Stop();
                    _videoPlayerManager.Dispose();
                }
                
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
                // 如果文本编辑器的DbContext存在，先处理它
                if (_dbContext != null)
                {
                    try
                    {
                        // 执行checkpoint操作，将WAL文件的内容合并回主数据库
                        _dbContext.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");
                        
                        // 释放DbContext
                        _dbContext.Dispose();
                        _dbContext = null;
                    }
                    catch (Exception)
                    {
                        // 忽略错误，确保继续清理
                    }
                }
                
                // 清理DatabaseManager中的主DbContext
                if (_dbManager != null)
                {
                    try
                    {
                        var mainDbContext = _dbManager.GetDbContext();
                        if (mainDbContext != null)
                        {
                            // 执行checkpoint操作
                            mainDbContext.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");
                            
                            // 关闭连接
                            var connection = mainDbContext.Database.GetDbConnection();
                            if (connection != null && connection.State == System.Data.ConnectionState.Open)
                            {
                                connection.Close();
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // 忽略错误
                    }
                }
            }
            catch (Exception)
            {
                // 忽略所有数据库清理错误，不影响程序关闭
            }
        }

        #endregion
    }
}

