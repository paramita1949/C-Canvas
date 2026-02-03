using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SkiaSharp;
using Color = System.Windows.Media.Color;
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
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using LibVLCSharp.WPF;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Video Playback Module
    /// </summary>
    public partial class MainWindow
    {
        #region 视频播放相关
        
        /// <summary>
        /// 视频播放状态改变事件
        /// </summary>
        private void OnVideoPlayStateChanged(object sender, bool isPlaying)
        {
            Dispatcher.Invoke(() =>
            {
                if (isPlaying)
                {
                    BtnMediaPlayPause.Content = "⏸";
                    
                    // 如果投影已开启且当前在主屏幕播放视频，自动启用视频投影
                    // 但如果已经在投影模式播放，就不要重复调用（避免闪烁）
                    if (_projectionManager != null && _projectionManager.IsProjectionActive)
                    {
                        if (_videoPlayerManager != null && !_videoPlayerManager.IsProjectionEnabled)
                        {
                            //System.Diagnostics.Debug.WriteLine("📹 视频开始播放，自动启用视频投影");
                            EnableVideoProjection();
                        }
                        else
                        {
                            //System.Diagnostics.Debug.WriteLine("✅ 已在投影模式播放，跳过重复启用");
                        }
                    }
                }
                else
                {
                    BtnMediaPlayPause.Content = "▶";
                }
            });
        }
        
        /// <summary>
        /// 视频媒体改变事件
        /// </summary>
        private void OnVideoMediaChanged(object sender, string mediaPath)
        {
            // System.Diagnostics.Debug.WriteLine($"📹 媒体已改变: {System.IO.Path.GetFileName(mediaPath)}");
            
            // 自动选中正在播放的文件
            SelectMediaFileByPath(mediaPath);
        }
        
        /// <summary>
        /// 根据路径选中文件节点
        /// </summary>
        private void SelectMediaFileByPath(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return;
                
                // 在项目树中查找并选中对应的文件
                foreach (var folderItem in _projectTreeItems)
                {
                    if (folderItem.Type == TreeItemType.Folder && folderItem.Children != null)
                    {
                        foreach (var fileItem in folderItem.Children)
                        {
                            if (fileItem.Type == TreeItemType.File && fileItem.Path == filePath)
                            {
                                // 展开父文件夹
                                folderItem.IsExpanded = true;
                                
                                // 取消其他所有选中
                                ClearAllSelections();
                                
                                // 选中当前文件
                                fileItem.IsSelected = true;
                                
                                //System.Diagnostics.Debug.WriteLine($"✅ 已自动选中文件: {fileItem.Name}");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 自动选中文件失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清除所有选中状态
        /// </summary>
        private void ClearAllSelections()
        {
            foreach (var folderItem in _projectTreeItems)
            {
                folderItem.IsSelected = false;
                if (folderItem.Children != null)
                {
                    foreach (var fileItem in folderItem.Children)
                    {
                        fileItem.IsSelected = false;
                    }
                }
            }
        }
        
        /// <summary>
        /// 视频播放结束事件
        /// </summary>
        private void OnVideoMediaEnded(object sender, EventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("🏁 视频播放结束");
        }
        
        /// <summary>
        /// 视频播放进度更新事件
        /// </summary>
        private void OnVideoProgressUpdated(object sender, (float position, long currentTime, long totalTime) progress)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isUpdatingProgress)
                {
                    _isUpdatingProgress = true;
                    
                    // 更新进度条
                    MediaProgressSlider.Value = progress.position * 100;
                    
                    // 更新时间显示
                    var currentSeconds = progress.currentTime / MILLISECONDS_PER_SECOND;
                    var totalSeconds = progress.totalTime / MILLISECONDS_PER_SECOND;
                    
                    var currentStr = $"{currentSeconds / 60:00}:{currentSeconds % 60:00}";
                    var totalStr = $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
                    
                    MediaCurrentTime.Text = currentStr;
                    MediaTotalTime.Text = totalStr;
                    
                    _isUpdatingProgress = false;
                }
            });
        }
        
        /// <summary>
        /// 检查文件是否为媒体文件（视频或音频）
        /// </summary>
        private bool IsMediaFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            
            var ext = System.IO.Path.GetExtension(filePath).ToLower();
            var videoExtensions = new[] { 
                ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg",
                ".rm", ".rmvb", ".3gp", ".f4v", ".ts", ".mts", ".m2ts", ".vob", ".ogv"
            };
            var audioExtensions = new[] { 
                ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac"
            };
            
            return videoExtensions.Contains(ext) || audioExtensions.Contains(ext);
        }
        
        /// <summary>
        /// 加载并显示媒体文件（图片或视频）
        /// </summary>
        private void LoadAndDisplayMedia(string filePath, int mediaId)
        {
            try
            {
                if (IsVideoFile(filePath))
                {
                    // 加载视频
                    LoadAndDisplayVideo(filePath);
                }
                else
                {
                    // 加载图片（使用现有的逻辑）
                    LoadImage(filePath);
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载媒体文件失败: {ex.Message}");
                MessageBox.Show($"加载媒体文件失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 在投影屏幕加载并播放视频（投影状态下使用）
        /// </summary>
        private void LoadAndDisplayVideoOnProjection(string videoPath)
        {
            try
            {
                //System.Diagnostics.Debug.WriteLine($"📹 ===== LoadAndDisplayVideoOnProjection 开始 =====");
                //System.Diagnostics.Debug.WriteLine($"📹 文件: {System.IO.Path.GetFileName(videoPath)}");
                
                var projectionVideoView = _projectionManager.GetProjectionVideoView();
                //System.Diagnostics.Debug.WriteLine($"🔍 投影VideoView: {(projectionVideoView != null ? "存在" : "null")}");
                
                if (projectionVideoView != null)
                {
                    //System.Diagnostics.Debug.WriteLine("步骤1: 隐藏主屏幕视频");
                    VideoContainer.Visibility = Visibility.Collapsed;
                    
                    // 🎬 隐藏合成播放按钮面板（媒体文件不需要）
                    CompositePlaybackPanel.Visibility = Visibility.Collapsed;
                    
                    //System.Diagnostics.Debug.WriteLine("步骤2: 显示投影视频");
                    _projectionManager.ShowVideoProjection();
                    
                    // 🔥 关键修复：检查投影窗口是否已经初始化完成
                    if (_videoPlayerManager != null && _videoPlayerManager.IsProjectionEnabled)
                    {
                        // 投影已经初始化完成，直接播放
                        //System.Diagnostics.Debug.WriteLine("✅ 投影已初始化，直接播放");
                        
                        // 切换到投影模式（如果还没切换）
                        _videoPlayerManager.SwitchToProjectionMode();
                        
                        // 构建播放列表并播放
                        BuildVideoPlaylist(videoPath);
                        _videoPlayerManager.Play(videoPath);
                        
                        var fileName = System.IO.Path.GetFileName(videoPath);
                        ShowStatus($"🎬 正在投影播放: {fileName}");
                    }
                    else
                    {
                        // 投影还未初始化，设置待播放路径，等待初始化完成后播放
                        _pendingProjectionVideoPath = videoPath;
                        //System.Diagnostics.Debug.WriteLine($"🟠 设置待投影播放视频: {System.IO.Path.GetFileName(videoPath)}");
                        ShowStatus($"🎬 准备投影播放: {System.IO.Path.GetFileName(videoPath)}");
                    }
                    
                    //System.Diagnostics.Debug.WriteLine($"📹 ===== LoadAndDisplayVideoOnProjection 完成 =====");
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 投影播放视频失败: {ex.Message}");
                MessageBox.Show($"投影播放视频失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 视频轨道检测事件处理
        /// </summary>
        private void VideoPlayerManager_VideoTrackDetected(object sender, bool hasVideo)
        {
            try
            {
                //System.Diagnostics.Debug.WriteLine($"🎬 收到视频轨道检测结果: HasVideo={hasVideo}");
                
                // 🔥 关键修复：使用 VideoPlayerManager 的当前播放文件，而不是 _imagePath
                string currentPath = _videoPlayerManager?.CurrentMediaPath;
                string fileName = !string.IsNullOrEmpty(currentPath) 
                    ? System.IO.Path.GetFileName(currentPath) 
                    : "未知文件";
                
                // 主窗口：显示或隐藏文件名
                if (!hasVideo)
                {
                    MediaFileNameText.Text = fileName;
                    MediaFileNameBorder.Visibility = Visibility.Visible;
                    //System.Diagnostics.Debug.WriteLine($"🎵 无视频轨道，显示文件名: {fileName}");
                }
                else
                {
                    MediaFileNameBorder.Visibility = Visibility.Collapsed;
                    //System.Diagnostics.Debug.WriteLine($"📹 有视频轨道，隐藏文件名");
                }
                
                // 投影窗口：如果投影已开启，同步显示
                if (_projectionManager != null && _projectionManager.IsProjectionActive)
                {
                    _projectionManager.SetProjectionMediaFileName(fileName, !hasVideo);
                }
                
                // 更新状态栏
                string icon = hasVideo ? "📹" : "🎵";
                string type = hasVideo ? "视频" : "音频";
                ShowStatus($"{icon} {type}: {fileName}");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 处理视频轨道检测失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 加载并显示视频
        /// </summary>
        private void LoadAndDisplayVideo(string videoPath)
        {
            try
            {
                // 显示视频播放区域
                VideoContainer.Visibility = Visibility.Visible;
                
                // 🎬 隐藏合成播放按钮（媒体文件不需要）
                BtnFloatingCompositePlay.Visibility = Visibility.Collapsed;
                
                // 先隐藏文件名，等视频轨道检测完成后再决定是否显示
                MediaFileNameBorder.Visibility = Visibility.Collapsed;
                
                // 隐藏媒体控制栏（改用快捷键控制）
                // MediaPlayerPanel.Visibility = Visibility.Visible;
                
                // 强制刷新布局，确保VideoView就绪
                VideoContainer.UpdateLayout();
                
                // 构建播放列表（获取当前文件所在文件夹的所有视频文件）
                BuildVideoPlaylist(videoPath);
                
                // 加载并播放视频（视频轨道检测会在播放开始后自动触发）
                if (_videoPlayerManager != null)
                {
                    _videoPlayerManager.Play(videoPath);
                }
                
                // 如果投影已开启，视频投影会在OnVideoPlayStateChanged事件中自动启用
                
                string fileName = System.IO.Path.GetFileName(videoPath);
                ShowStatus($"📹 正在加载: {fileName}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载视频失败: {ex.Message}");
                MessageBox.Show($"加载视频失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        
        /// <summary>
        /// 构建视频播放列表
        /// </summary>
        private void BuildVideoPlaylist(string currentVideoPath)
        {
            try
            {
                if (_videoPlayerManager == null || _dbManager == null) return;
                
                // 方法1: 通过路径在所有文件夹中查找
                MediaFile currentMediaFile = null;
                
                // 先在根目录查找
                var rootFiles = _dbManager.GetRootMediaFiles();
                currentMediaFile = rootFiles.FirstOrDefault(f => f.Path == currentVideoPath);
                
                // 如果根目录没找到，遍历所有文件夹查找
                if (currentMediaFile == null)
                {
                    var folders = _dbManager.GetAllFolders();
                    foreach (var folder in folders)
                    {
                        var folderFiles = _dbManager.GetMediaFilesByFolder(folder.Id);
                        currentMediaFile = folderFiles.FirstOrDefault(f => f.Path == currentVideoPath);
                        if (currentMediaFile != null)
                            break;
                    }
                }
                
                if (currentMediaFile == null)
                {
                    //System.Diagnostics.Debug.WriteLine("❌ 未找到当前视频文件信息");
                    return;
                }
                
                List<string> playlist = new List<string>();
                
                // 获取同一文件夹下的所有视频文件
                if (currentMediaFile.FolderId.HasValue)
                {
                    var folderFiles = _dbManager.GetMediaFilesByFolder(currentMediaFile.FolderId.Value);
                    
                    // 筛选出视频文件
                    var videoFiles = folderFiles
                        .Where(f => f.FileType == FileType.Video)
                        .OrderBy(f => f.OrderIndex ?? 0)
                        .ThenBy(f => f.Name)
                        .ToList();
                    
                    playlist = videoFiles.Select(f => f.Path).ToList();
                    
                    //System.Diagnostics.Debug.WriteLine($"📋 构建播放列表: 文件夹 [{currentMediaFile.Folder?.Name}] 中有 {playlist.Count} 个视频");
                }
                else
                {
                    // 根目录文件
                    var videoFiles = rootFiles
                        .Where(f => f.FileType == FileType.Video)
                        .OrderBy(f => f.OrderIndex ?? 0)
                        .ThenBy(f => f.Name)
                        .ToList();
                    
                    playlist = videoFiles.Select(f => f.Path).ToList();
                    
                    //System.Diagnostics.Debug.WriteLine($"📋 构建播放列表: 根目录中有 {playlist.Count} 个视频");
                }
                
                // 设置播放列表到VideoPlayerManager
                if (playlist.Count > 0)
                {
                    _videoPlayerManager.SetPlaylist(playlist);
                    
                    // 找到当前视频在播放列表中的索引
                    int currentIndex = playlist.IndexOf(currentVideoPath);
                    if (currentIndex >= 0)
                    {
                        //System.Diagnostics.Debug.WriteLine($"📹 当前视频索引: {currentIndex + 1}/{playlist.Count}");
                    }
                    
                    // 根据文件夹标记自动设置播放模式
                    if (currentMediaFile.FolderId.HasValue)
                    {
                        string folderPlayMode = _dbManager.GetFolderVideoPlayMode(currentMediaFile.FolderId.Value);
                        if (!string.IsNullOrEmpty(folderPlayMode))
                        {
                            PlayMode mode = folderPlayMode switch
                            {
                                "sequential" => PlayMode.Sequential,
                                "random" => PlayMode.Random,
                                "loop_all" => PlayMode.LoopAll,
                                _ => PlayMode.Sequential
                            };
                            
                            _videoPlayerManager.SetPlayMode(mode);
                            
                            string[] modeNames = { "顺序", "随机", "单曲", "列表" };
                            //System.Diagnostics.Debug.WriteLine($"🎵 根据文件夹标记自动设置播放模式: {modeNames[(int)mode]}");
                            ShowStatus($"🎵 播放模式: {modeNames[(int)mode]}");
                        }
                    }
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine("⚠️ 播放列表为空");
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 构建播放列表失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 切换回图片显示模式
        /// </summary>
        internal void SwitchToImageMode()
        {
            // 停止视频播放
            if (_videoPlayerManager != null && _videoPlayerManager.IsPlaying)
            {
                _videoPlayerManager.Stop();
            }
            
            // 隐藏视频播放区域
            VideoContainer.Visibility = Visibility.Collapsed;
            
            // 隐藏媒体控制栏
            MediaPlayerPanel.Visibility = Visibility.Collapsed;
            
            // 清空图片显示（避免回到之前的图片）
            ClearImageDisplay();
        }
        
        /// <summary>
        /// 启用视频投屏
        /// </summary>
        private void EnableVideoProjection()
        {
            try
            {
                if (_videoPlayerManager == null || _projectionManager == null) return;
                
                //System.Diagnostics.Debug.WriteLine("📹 启用视频投屏");
                
                // 隐藏主屏幕的视频容器
                VideoContainer.Visibility = Visibility.Collapsed;
                
                // 切换到视频投影模式
                _projectionManager.ShowVideoProjection();
                
                // 启用视频投影（VideoView已在Loaded事件中绑定）
                _videoPlayerManager.EnableProjection();
                
                ShowStatus("✅ 视频投屏已启用");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 启用视频投屏失败: {ex.Message}");
                MessageBox.Show($"启用视频投屏失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 禁用视频投屏
        /// </summary>
        private void DisableVideoProjection()
        {
            try
            {
                if (_videoPlayerManager == null) return;
                
                //System.Diagnostics.Debug.WriteLine("📹 禁用视频投屏");
                
                // 禁用视频投影
                _videoPlayerManager.DisableProjection();
                
                // 如果投影窗口还在，切换回图片投影模式
                if (_projectionManager != null && _projectionManager.IsProjectionActive)
                {
                    _projectionManager.ShowImageProjection();
                }
                
                ShowStatus("🔴 视频投屏已禁用");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 禁用视频投屏失败: {ex.Message}");
            }
        }
        
        #endregion

    }
}