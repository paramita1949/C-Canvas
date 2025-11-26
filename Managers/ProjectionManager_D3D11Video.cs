using System;
using System.Linq;
using System.Windows;
using LibVLCSharp.Shared;
using SkiaSharp;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 的 D3D11 视频渲染部分（部分类）
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 🎬 使用 VLC D3D11 渲染器更新锁定模式视频（同步版本，更稳定）
        /// </summary>
        private void UpdateProjectionWithLockedVideoD3D11(string videoPath, bool loopEnabled, SKBitmap textLayer)
        {
            if (_projectionWindow == null || _projectionContainer == null)
                return;

            _projectionWindow.Dispatcher.Invoke(() =>
            {
                try
                {
#if DEBUG
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    System.Diagnostics.Debug.WriteLine($"🔒 [UpdateProjectionWithLockedVideo-D3D11] 开始");
                    System.Diagnostics.Debug.WriteLine($"   视频路径: {videoPath}");
#endif

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
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔄 视频路径改变（热切换）: {oldVideoPath} → {videoPath}");
#endif
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

                    // 🎬 创建或重用 VLC 播放器
                    if (_projectionVlcPlayer == null)
                    {
                        if (_videoPlayerManager == null)
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($"❌ VideoPlayerManager 未设置");
#endif
                            return;
                        }

                        var libVLC = _videoPlayerManager.GetLibVLC();
                        if (libVLC == null)
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($"❌ LibVLC 未初始化");
#endif
                            return;
                        }

                        // 保存 LibVLC 引用供后续使用
                        _projectionLibVLC = libVLC;
                        
                        _projectionVlcPlayer = new LibVLCSharp.Shared.MediaPlayer(_projectionLibVLC)
                        {
                            EnableHardwareDecoding = true,  // 🚀 启用硬件解码
                            EnableMouseInput = false,
                            EnableKeyInput = false,
                            Volume = 0  // 静音
                        };

#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"✅ VLC 播放器已创建");
#endif
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

#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"✅ D3D11 渲染器已创建: {projWidth}x{projHeight}");
#endif
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

                    // 🎬 加载并播放视频
                    // 🚀 优先使用预解析的 Media（如果有）
                    LibVLCSharp.Shared.Media media = GetPreparsedMedia(videoPath);
                    bool usedPreparse = (media != null);

                    // 如果没有预解析，同步创建新的 Media
                    if (media == null)
                    {
                        if (_projectionLibVLC == null)
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($"❌ LibVLC 未初始化，无法创建 Media");
#endif
                            return;
                        }

#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"⏳ [同步加载] 创建 Media...");
#endif
                        // 🔧 同步创建 Media（简单可靠）
                        media = new LibVLCSharp.Shared.Media(_projectionLibVLC, videoPath, LibVLCSharp.Shared.FromType.FromPath);
                        // 快速解析基本信息（可选，减少延迟）
                        // media.Parse(MediaParseOptions.ParseLocal);
                    }
                    else
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🚀 [使用预解析] 视频切换加速");
#endif
                    }

                    if (media != null)
                    {
                        // 🔧 如果视频路径改变，先停止当前播放（避免 Media 冲突）
                        if (oldVideoPath != videoPath && oldVideoPath != null && _projectionVlcPlayer != null)
                        {
                            try
                            {
                                _projectionVlcPlayer.Stop();
                                // 等待一小段时间，确保停止完成
                                System.Threading.Thread.Sleep(10);
                            }
                            catch (Exception ex)
                            {
#if DEBUG
                                System.Diagnostics.Debug.WriteLine($"⚠️ [停止播放] 错误: {ex.Message}");
#endif
                            }
                        }

                        // 设置循环播放
                        if (loopEnabled)
                        {
                            _projectionVlcPlayer.EndReached -= OnProjectionVideoEndReached;
                            _projectionVlcPlayer.EndReached += OnProjectionVideoEndReached;
                        }
                        else
                        {
                            _projectionVlcPlayer.EndReached -= OnProjectionVideoEndReached;
                        }

                        // 🔧 设置 Media 并播放（Media 由 VLC 管理，不需要手动 Dispose）
                        _projectionVlcPlayer.Media = media;
                        _projectionVlcPlayer.Play();
                    }
                    else
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"❌ Media 创建失败");
#endif
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

#if DEBUG
                    sw.Stop();
                    System.Diagnostics.Debug.WriteLine($"✅ 视频播放已启动，总耗时: {sw.ElapsedMilliseconds}ms");
#endif
                }
                catch (Exception ex)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"❌ [UpdateProjectionWithLockedVideo-D3D11] 错误: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
#endif
                }
            });
        }

        /// <summary>
        /// 视频播放结束事件 - 循环播放
        /// </summary>
        private void OnProjectionVideoEndReached(object sender, EventArgs e)
        {
            _projectionWindow?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_projectionVlcPlayer != null && !string.IsNullOrEmpty(_lockedVideoPath))
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"🔁 [循环播放] 重新播放视频");
#endif
                    try
                    {
                        // 🔧 创建新的 Media（不使用 using，让 VLC 管理生命周期）
                        var media = new LibVLCSharp.Shared.Media(_projectionLibVLC, _lockedVideoPath, LibVLCSharp.Shared.FromType.FromPath);
                        _projectionVlcPlayer.Media = media;
                        _projectionVlcPlayer.Play();
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"❌ [循环播放] 错误: {ex.Message}");
#endif
                    }
                }
            }));
        }

        /// <summary>
        /// 更新投影文本层（叠加在视频上）
        /// </summary>
        private void UpdateProjectionTextLayer(SKBitmap textLayer)
        {
            if (textLayer == null || _projectionVideoContainer == null)
                return;

            try
            {
                // 🚀 优化：检查文本层是否改变（使用时间戳）
                long currentTimestamp = DateTime.Now.Ticks;
                if (_cachedTextLayerBitmap != null && 
                    (currentTimestamp - _cachedTextLayerTimestamp) < TimeSpan.FromMilliseconds(100).Ticks)
                {
                    // 100ms 内的重复更新，使用缓存
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("✅ 文本层使用缓存（100ms内）");
#endif
                    return;
                }

                var bitmapSource = ConvertToBitmapSource(textLayer);
                _cachedTextLayerBitmap = bitmapSource;
                _cachedTextLayerTimestamp = currentTimestamp;
                
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
                    
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ 创建文本层 Image");
#endif
                }
                
                textImage.Source = bitmapSource;
                textImage.Visibility = Visibility.Visible;
                
                // 确保视频 Image 在底层
                System.Windows.Controls.Panel.SetZIndex(_projectionVideoImage, 0);

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ 文本层已叠加: {textLayer.Width}x{textLayer.Height}");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [UpdateProjectionTextLayer] 错误: {ex.Message}");
#endif
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

        #region 🚀 视频预解析优化

        /// <summary>
        /// 🚀 预解析视频（异步）- 提前加载下一个视频
        /// </summary>
        public async System.Threading.Tasks.Task PreparseVideoAsync(string videoPath)
        {
            if (string.IsNullOrEmpty(videoPath) || !System.IO.File.Exists(videoPath))
                return;

            if (_projectionLibVLC == null)
                return;

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    lock (_preparseLock)
                    {
                        // 如果已经预解析了这个视频，跳过
                        if (_preparsedVideoPath == videoPath && _preparsedMedia != null)
                        {
#if DEBUG
                            System.Diagnostics.Debug.WriteLine($"✅ [预解析] 视频已缓存: {System.IO.Path.GetFileName(videoPath)}");
#endif
                            return;
                        }

                        // 清理旧的预解析
                        ClearPreparsedMediaInternal();

#if DEBUG
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        System.Diagnostics.Debug.WriteLine($"🚀 [预解析] 开始: {System.IO.Path.GetFileName(videoPath)}");
#endif

                        // 创建并预解析 Media
                        var media = new LibVLCSharp.Shared.Media(_projectionLibVLC, videoPath, LibVLCSharp.Shared.FromType.FromPath);
                        
                        // 🚀 异步解析（不阻塞）
                        media.Parse(MediaParseOptions.ParseNetwork);

                        _preparsedMedia = media;
                        _preparsedVideoPath = videoPath;

#if DEBUG
                        sw.Stop();
                        System.Diagnostics.Debug.WriteLine($"✅ [预解析] 完成: {sw.ElapsedMilliseconds}ms - {System.IO.Path.GetFileName(videoPath)}");
#endif
                    }
                }
                catch (Exception ex)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"❌ [预解析] 错误: {ex.Message}");
#endif
                }
            });
        }

        /// <summary>
        /// 🚀 获取预解析的 Media（如果有）
        /// </summary>
        private LibVLCSharp.Shared.Media GetPreparsedMedia(string videoPath)
        {
            lock (_preparseLock)
            {
                if (_preparsedVideoPath == videoPath && _preparsedMedia != null)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ [命中预解析] {System.IO.Path.GetFileName(videoPath)}");
#endif
                    var media = _preparsedMedia;
                    _preparsedMedia = null;  // 取出后清空，避免重复使用
                    _preparsedVideoPath = null;
                    return media;
                }
#if DEBUG
                else if (!string.IsNullOrEmpty(videoPath))
                {
                    System.Diagnostics.Debug.WriteLine($"❌ [未命中预解析] 请求: {System.IO.Path.GetFileName(videoPath)}, 缓存: {(_preparsedVideoPath != null ? System.IO.Path.GetFileName(_preparsedVideoPath) : "无")}");
                }
#endif
                return null;
            }
        }

        /// <summary>
        /// 🚀 清除预解析的 Media
        /// </summary>
        private void ClearPreparsedMediaInternal()
        {
            if (_preparsedMedia != null)
            {
                try
                {
                    _preparsedMedia.Dispose();
                }
                catch { }
                _preparsedMedia = null;
                _preparsedVideoPath = null;
            }
        }

        /// <summary>
        /// 🚀 清除预解析的 Media（公开方法）
        /// </summary>
        public void ClearPreparsedMedia()
        {
            lock (_preparseLock)
            {
                ClearPreparsedMediaInternal();
            }
        }

        #endregion
    }
}

