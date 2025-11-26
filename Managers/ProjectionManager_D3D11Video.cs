using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using SkiaSharp;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 的 D3D11 视频渲染部分（部分类）
    /// </summary>
    public partial class ProjectionManager
    {
        #region 字段

        // ✅ 优化5：竞态条件保护
        private bool _isD3D11Disposed = false;
        
        // ✅ 优化1：复用 Media 实例，避免内存泄漏
        private LibVLCSharp.Shared.Media _currentProjectionMedia;

        #endregion

        #region ✅ 优化2：LibVLC 安全获取

        /// <summary>
        /// 确保 LibVLC 已初始化（线程安全）
        /// </summary>
        private bool EnsureLibVLCInitialized()
        {
            if (_projectionLibVLC != null)
                return true;

            if (_videoPlayerManager == null)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"❌ [LibVLC] VideoPlayerManager 未设置");
//#endif
                return false;
            }

            _projectionLibVLC = _videoPlayerManager.GetLibVLC();
            if (_projectionLibVLC == null)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"❌ [LibVLC] 初始化失败");
//#endif
                return false;
            }

//#if DEBUG
//            System.Diagnostics.Debug.WriteLine($"✅ [LibVLC] 初始化成功");
//#endif
            return true;
        }

        #endregion

        /// <summary>
        /// 🎬 使用 VLC D3D11 渲染器更新锁定模式视频（已优化）
        /// </summary>
        private void UpdateProjectionWithLockedVideoD3D11(string videoPath, bool loopEnabled, SKBitmap textLayer)
        {
            // ✅ 优化5：竞态条件保护
            if (_isD3D11Disposed || _projectionWindow == null || _projectionContainer == null)
                return;

            // ✅ 优化7：使用 BeginInvoke 异步调用，避免 UI 线程死锁
            // 🎯 优化：使用 DispatcherPriority.Normal 确保及时执行
            _projectionWindow.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    // ✅ 优化5：双重检查，防止在调度期间被 Dispose
                    if (_isD3D11Disposed)
                        return;

//#if DEBUG
//                    var sw = System.Diagnostics.Stopwatch.StartNew();
//                    System.Diagnostics.Debug.WriteLine($"🔒 [UpdateProjectionWithLockedVideo-D3D11] 开始");
//                    System.Diagnostics.Debug.WriteLine($"   视频路径: {videoPath}");
//#endif

                    // 检查视频文件
                    if (string.IsNullOrEmpty(videoPath) || !System.IO.File.Exists(videoPath))
                    {
                        ClearLockedVideo();
                        return;
                    }

                    // 🔄 保存旧路径，用于判断是否需要停止
                    string oldVideoPath = _lockedVideoPath;
                    
                    // 🔄 如果视频路径改变，记录日志
                    if (oldVideoPath != videoPath && _projectionVlcPlayer != null)
                    {
//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"🔄 视频路径改变（热切换）: {oldVideoPath} → {videoPath}");
//#endif
                    }
                    
                    _lockedVideoPath = videoPath;

                    // 获取投影窗口尺寸
                    int projWidth = (int)_projectionWindow.ActualWidth;
                    int projHeight = (int)_projectionWindow.ActualHeight;

                    if (projWidth == 0 || projHeight == 0)
                    {
                        projWidth = 1920;
                        projHeight = 1080;
                    }

                    // ✅ 优化2：使用安全的 LibVLC 获取方法
                    if (!EnsureLibVLCInitialized())
                        return;

                    // 🎬 创建或重用 VLC 播放器
                    if (_projectionVlcPlayer == null)
                    {
                        
                        _projectionVlcPlayer = new LibVLCSharp.Shared.MediaPlayer(_projectionLibVLC)
                        {
                            EnableHardwareDecoding = true,  // 🚀 启用硬件解码
                            EnableMouseInput = false,
                            EnableKeyInput = false,
                            Volume = 0  // 静音
                        };

//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"✅ VLC 播放器已创建（硬件解码：{_projectionVlcPlayer.EnableHardwareDecoding}）");
//                        
//                        // ✅ 优化6：检测硬件解码是否真正启用
//                        _projectionVlcPlayer.Playing += (s, args) =>
//                        {
//                            try
//                            {
//                                var hwDecoding = _projectionVlcPlayer.EnableHardwareDecoding;
//                                System.Diagnostics.Debug.WriteLine($"🎬 [播放中] 硬件解码状态: {hwDecoding}");
//                            }
//                            catch (Exception ex)
//                            {
//                                System.Diagnostics.Debug.WriteLine($"⚠️ [硬件解码] 检查失败: {ex.Message}");
//                            }
//                        };
//#endif

                        // ✅ 优化5：监听投影窗口尺寸变化
                        if (_projectionWindow != null && !_projectionWindowSizeChangedRegistered)
                        {
                            _projectionWindow.SizeChanged += OnProjectionWindowSizeChanged;
                            _projectionWindowSizeChangedRegistered = true;
//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"✅ 已注册窗口尺寸监听事件");
//#endif
                        }
                    }

                    // 🎬 创建或更新 D3D11 渲染器
                    if (_projectionVlcRenderer == null)
                    {
                        _projectionVlcRenderer = new VlcD3D11Renderer(
                            _projectionVlcPlayer,
                            projWidth,
                            projHeight,
                            _projectionWindow.Dispatcher
                        );

//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"✅ D3D11 渲染器已创建: {projWidth}x{projHeight}");
//#endif
                    }
                    else
                    {
                        // 更新尺寸
                        _projectionVlcRenderer.UpdateSize(projWidth, projHeight);
                    }

                    // 🎬 绑定 WriteableBitmap 到 Image 控件
                    _projectionVideoImage.Source = _projectionVlcRenderer.WriteableBitmap;

                    // 🎬 显示视频容器
                    _projectionVideoContainer.Visibility = Visibility.Visible;

                    // ✅ 无缝循环优化：复用 Media + 原生 repeat 选项
                    try
                    {
                        // ✅ 修复：使用 oldVideoPath 判断，而不是已更新的 _lockedVideoPath
                        bool needsNewMedia = (_currentProjectionMedia == null || 
                                             oldVideoPath != videoPath);

//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"🔍 [Media判断] oldVideoPath: {oldVideoPath ?? "null"}");
//                        System.Diagnostics.Debug.WriteLine($"🔍 [Media判断] newVideoPath: {videoPath}");
//                        System.Diagnostics.Debug.WriteLine($"🔍 [Media判断] needsNewMedia: {needsNewMedia}");
//#endif

                        if (needsNewMedia)
                        {
                            // 清理旧的 Media
                            if (_currentProjectionMedia != null)
                            {
                                try
                                {
                                    _currentProjectionMedia.Dispose();
//#if DEBUG
//                                    System.Diagnostics.Debug.WriteLine($"🗑️ [Media] 已清理旧 Media");
//#endif
                                }
                                catch { }
                                _currentProjectionMedia = null;
                            }

                            // 创建新的 Media
                            _currentProjectionMedia = new LibVLCSharp.Shared.Media(_projectionLibVLC, videoPath, LibVLCSharp.Shared.FromType.FromPath);
                            
                            // 🎯 核心优化1：使用 LibVLC 原生循环机制（无缝循环）
                            // ⚠️ 注意：VLC 3.0+ 中 :input-repeat=-1 已被弃用，需要使用正整数值
                            if (loopEnabled)
                            {
                                _currentProjectionMedia.AddOption(":input-repeat=65535");  // 65535 表示接近无限循环
//#if DEBUG
//                                System.Diagnostics.Debug.WriteLine($"🔁 [无缝循环] 已启用原生循环: input-repeat=65535");
//#endif
                            }
                            
//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"✅ [播放] 创建新 Media: {System.IO.Path.GetFileName(videoPath)}");
//#endif
                        }
                        else
                        {
//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"♻️ [播放] 复用 Media: {System.IO.Path.GetFileName(videoPath)}");
//#endif
                        }

                        // 🎬 播放视频（依赖 :input-repeat=65535 原生循环）
                        if (needsNewMedia || _projectionVlcPlayer.State == VLCState.Ended || 
                            _projectionVlcPlayer.State == VLCState.Stopped)
                        {
                            // ⚠️ 修复：切换视频时，先停止当前播放
                            if (needsNewMedia && _projectionVlcPlayer.State != VLCState.Stopped)
                            {
                                _projectionVlcPlayer.Stop();
                                // 短暂延迟，确保停止完成
                                System.Threading.Thread.Sleep(10);
//#if DEBUG
//                                System.Diagnostics.Debug.WriteLine($"⏹️ [播放] 先停止旧视频");
//#endif
                            }
                            
                            _projectionVlcPlayer.Media = _currentProjectionMedia;
                            _projectionVlcPlayer.Play();
//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"▶️ [播放] 启动视频（依赖 :input-repeat=65535 原生循环）");
//#endif
                        }

//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"✅ [播放] 视频已启动");
//#endif
                    }
                    catch (Exception ex)
                    {
//#if DEBUG
//                        System.Diagnostics.Debug.WriteLine($"❌ [播放] 视频启动失败: {ex.Message}");
//#else
                        _ = ex; // 避免未使用变量警告
//#endif
                        return;
                    }

                    // 🎨 如果有文本层，叠加显示
                    if (textLayer != null)
                    {
                        UpdateProjectionTextLayer(textLayer);
                    }
                    else
                    {
                        // 隐藏文本层
                        HideProjectionTextLayer();
                    }

//#if DEBUG
//                    sw.Stop();
//                    System.Diagnostics.Debug.WriteLine($"✅ 视频播放已启动，总耗时: {sw.ElapsedMilliseconds}ms");
//#endif
                }
                catch (Exception ex)
                {
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"❌ [UpdateProjectionWithLockedVideo-D3D11] 错误: {ex.Message}");
//                    System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
//#else
                    _ = ex; // 避免未使用变量警告
//#endif
                }
            }));
        }

        /// <summary>
        /// 更新投影文本层（叠加在视频上）
        /// </summary>
        private void UpdateProjectionTextLayer(SKBitmap textLayer)
        {
            // ✅ 优化5：竞态条件保护
            if (_isD3D11Disposed || textLayer == null || _projectionVideoContainer == null)
                return;

            try
            {
                // ✅ 优化4：简化缓存逻辑 - 依赖调用方控制频率
                // 不使用时间戳判断，因为 SKBitmap 是引用类型，时间戳可能不可靠
                // 如果需要防抖，应该在调用方控制
                
                var bitmapSource = ConvertToBitmapSource(textLayer);
                _cachedTextLayerBitmap = bitmapSource;
                _cachedTextLayerTimestamp = DateTime.Now.Ticks;
                
                // 🎨 在视频容器中查找或创建文本层 Image
                var textImage = _projectionVideoContainer.Children.OfType<System.Windows.Controls.Image>()
                    .FirstOrDefault(img => img.Tag?.ToString() == "TextLayer");
                
                if (textImage == null)
                {
                    // 创建新的文本层 Image
                    textImage = new System.Windows.Controls.Image
                    {
                        Tag = "TextLayer",
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                        VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                        Stretch = System.Windows.Media.Stretch.Uniform
                    };
                    
                    _projectionVideoContainer.Children.Add(textImage);
                    System.Windows.Controls.Panel.SetZIndex(textImage, 10);  // 文本层在最上面
                    
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"✅ 创建文本层 Image");
//#endif
                }
                
                textImage.Source = bitmapSource;
                textImage.Visibility = Visibility.Visible;
                
                // 确保视频 Image 在底层
                System.Windows.Controls.Panel.SetZIndex(_projectionVideoImage, 0);

//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"✅ 文本层已叠加: {textLayer.Width}x{textLayer.Height}");
//#endif
            }
            catch (Exception ex)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"❌ [UpdateProjectionTextLayer] 错误: {ex.Message}");
//#else
                _ = ex; // 避免未使用变量警告
//#endif
            }
        }

        /// <summary>
        /// 隐藏投影文本层
        /// </summary>
        private void HideProjectionTextLayer()
        {
            if (_projectionVideoContainer != null)
            {
                // 隐藏视频容器中的文本层
                var textImage = _projectionVideoContainer.Children.OfType<System.Windows.Controls.Image>()
                    .FirstOrDefault(img => img.Tag?.ToString() == "TextLayer");
                
                if (textImage != null)
                {
                    textImage.Visibility = Visibility.Collapsed;
                }
            }
            
            // 也隐藏旧的文本层（如果存在）
            if (_projectionVisualBrushRect != null)
            {
                _projectionVisualBrushRect.Visibility = Visibility.Collapsed;
            }
            if (_projectionImageControl != null)
            {
                _projectionImageControl.Visibility = Visibility.Collapsed;
            }
        }

        #region ❌ 已删除：视频预解析优化（简化为跟主屏幕WPF一样的逻辑）
        
        // 预解析功能已删除，直接播放更简单稳定
        
        #endregion

        #region ✅ 优化5：窗口尺寸变化监听

        private bool _projectionWindowSizeChangedRegistered = false;

        /// <summary>
        /// 投影窗口尺寸变化事件处理
        /// </summary>
        private void OnProjectionWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // ✅ 优化5：竞态条件保护
            if (_isD3D11Disposed || _projectionVlcRenderer == null || _projectionWindow == null)
                return;

            try
            {
                int newWidth = (int)e.NewSize.Width;
                int newHeight = (int)e.NewSize.Height;

                if (newWidth > 0 && newHeight > 0)
                {
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"🔄 [窗口尺寸变化] {(int)e.PreviousSize.Width}x{(int)e.PreviousSize.Height} → {newWidth}x{newHeight}");
//#endif
                    // 再次检查，防止在执行期间被 Dispose
                    if (_isD3D11Disposed || _projectionVlcRenderer == null)
                        return;

                    // 更新渲染器尺寸
                    _projectionVlcRenderer.UpdateSize(newWidth, newHeight);
                    
                    // 重新绑定 WriteableBitmap
                    if (_projectionVideoImage != null && !_isD3D11Disposed)
                    {
                        _projectionVideoImage.Source = _projectionVlcRenderer.WriteableBitmap;
                    }
                }
            }
            catch (Exception ex)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"❌ [窗口尺寸变化] 处理错误: {ex.Message}");
//#else
                _ = ex; // 避免未使用变量警告
//#endif
            }
        }

        #endregion

        #region ✅ 优化4：Dispose 资源清理

        /// <summary>
        /// 清理 D3D11 视频资源（在 ProjectionManager.Dispose 中调用）
        /// </summary>
        public void DisposeD3D11Resources()
        {
            // ✅ 优化5：设置 Dispose 标志，防止竞态条件
            _isD3D11Disposed = true;

            try
            {
                // 注销事件
                if (_projectionWindow != null && _projectionWindowSizeChangedRegistered)
                {
                    _projectionWindow.SizeChanged -= OnProjectionWindowSizeChanged;
                    _projectionWindowSizeChangedRegistered = false;
                }

                // 清理渲染器
                if (_projectionVlcRenderer != null)
                {
                    _projectionVlcRenderer.Dispose();
                    _projectionVlcRenderer = null;
                }

                // 清理播放器
                if (_projectionVlcPlayer != null)
                {
                    try
                    {
                        _projectionVlcPlayer.Stop();
                    }
                    catch { }
                    
                    _projectionVlcPlayer.Dispose();
                    _projectionVlcPlayer = null;
                }

                // ✅ 优化1：清理 Media 实例
                if (_currentProjectionMedia != null)
                {
                    try
                    {
                        _currentProjectionMedia.Dispose();
                    }
                    catch { }
                    _currentProjectionMedia = null;
                }

                // 清空路径
                _lockedVideoPath = null;

//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"✅ [Dispose] D3D11 视频资源已清理");
//#endif
            }
            catch (Exception ex)
            {
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"❌ [Dispose] D3D11 资源清理错误: {ex.Message}");
//#else
                _ = ex; // 避免未使用变量警告
//#endif
            }
        }

        #endregion
    }
}

