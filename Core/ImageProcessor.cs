using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using ImageColorChanger.UI;
using Microsoft.Extensions.Caching.Memory;

namespace ImageColorChanger.Core
{

    /// <summary>
    /// 图片处理器 - 负责图片加载、显示、缩放和效果处理
    /// 使用SkiaSharp实现高性能图片处理
    /// </summary>
    public class ImageProcessor : IDisposable
    {
        #region 字段

        private readonly MainWindow mainWindow;
        private readonly ScrollViewer scrollViewer;
        private readonly System.Windows.Controls.Image imageControl;
        private readonly Grid imageContainer; // 图片容器（用于控制滚动区域）
        
        // 图片状态
        private SKBitmap originalImage;
        private SKBitmap currentImage;
        private BitmapSource currentPhoto;
        private string currentImagePath;
        
        // 显示模式
        private bool originalMode = false;
        private OriginalDisplayMode originalDisplayMode = OriginalDisplayMode.Stretch;
        
        // 缩放状态
        private double zoomRatio = Constants.DefaultZoomRatio;
        
        // 效果状态
        private bool isInverted = false;
        
        // 缓存管理
        private readonly Dictionary<string, BitmapSource> imageCache = new Dictionary<string, BitmapSource>();
        private readonly Dictionary<string, DateTime> imageCacheAccessTime = new Dictionary<string, DateTime>(); // ⚡ LRU访问时间
        private readonly IMemoryCache _imageMemoryCache; // ⚡ LRU图片缓存
        
        // 性能优化
        private DateTime lastUpdateTime = DateTime.MinValue;
        private readonly TimeSpan updateThrottleInterval = TimeSpan.FromSeconds(Constants.Fps60Interval);

        #endregion

        #region 构造函数

        public ImageProcessor(MainWindow window, ScrollViewer scrollViewer, System.Windows.Controls.Image imageControl, Grid imageContainer)
        {
            this.mainWindow = window;
            this.scrollViewer = scrollViewer;
            this.imageControl = imageControl;
            this.imageContainer = imageContainer;
            
            // ⚡ 初始化LRU图片缓存
            _imageMemoryCache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 100, // 最多缓存100张图片（基于权重计算）
                CompactionPercentage = 0.25, // 达到上限时清理25%最少使用的项
                ExpirationScanFrequency = TimeSpan.FromMinutes(5) // 每5分钟扫描过期项
            });
        }

        #endregion

        #region 公共属性

        /// <summary>当前图片</summary>
        public SKBitmap CurrentImage => currentImage;
        
        /// <summary>原始图片</summary>
        public SKBitmap OriginalImage => originalImage;
        
        /// <summary>当前图片路径</summary>
        public string CurrentImagePath => currentImagePath;
        
        /// <summary>获取LRU图片缓存实例（用于预加载管理器）</summary>
        public IMemoryCache GetMemoryCache() => _imageMemoryCache;
        
        /// <summary>
        /// 预渲染指定路径的图片到渲染缓存（用于预加载优化）
        /// </summary>
        /// <param name="imagePath">图片路径</param>
        /// <param name="targetWidth">目标宽度</param>
        /// <param name="targetHeight">目标高度</param>
        /// <param name="applyInvert">是否应用变色效果</param>
        /// <returns>是否成功预渲染</returns>
        public bool PreRenderImage(string imagePath, int targetWidth, int targetHeight, bool applyInvert)
        {
            try
            {
                // 生成缓存键
                string cacheKey = $"{imagePath}_{targetWidth}x{targetHeight}_{(applyInvert ? "inverted" : "normal")}";
                
                // 如果已经在渲染缓存中，跳过
                if (imageCache.ContainsKey(cacheKey))
                {
                    return true;
                }
                
                // 尝试从LRU缓存加载原始图片
                if (!_imageMemoryCache.TryGetValue(imagePath, out SKBitmap rawImage))
                {
                    // LRU缓存中没有，无法预渲染
                    return false;
                }
                
                // 计算缩放
                double scaleX = (double)targetWidth / rawImage.Width;
                double scaleY = (double)targetHeight / rawImage.Height;
                double scaleRatio = Math.Min(scaleX, scaleY);
                
                int finalWidth = (int)(rawImage.Width * scaleRatio);
                int finalHeight = (int)(rawImage.Height * scaleRatio);
                
                // 缩放并应用效果
                var resizedImage = ResizeImage(rawImage, finalWidth, finalHeight);
                
                if (resizedImage != null)
                {
                    // 应用效果
                    if (applyInvert)
                    {
                        ApplyYellowTextEffect(resizedImage);
                    }
                    
                    // 转换为BitmapSource
                    var bitmapSource = ConvertToBitmapSource(resizedImage);
                    resizedImage.Dispose();
                    
                    if (bitmapSource != null)
                    {
                        // 加入渲染缓存
                        imageCache[cacheKey] = bitmapSource;
                        imageCacheAccessTime[cacheKey] = DateTime.Now;
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        /// <summary>是否处于原图模式</summary>
        public bool OriginalMode
        {
            get => originalMode;
            set
            {
                if (originalMode != value)
                {
                    originalMode = value;
                    if (currentImage != null)
                    {
                        UpdateImage();
                    }
                }
            }
        }
        
        /// <summary>原图显示模式</summary>
        public OriginalDisplayMode OriginalDisplayModeValue
        {
            get => originalDisplayMode;
            set
            {
                if (originalDisplayMode != value)
                {
                    originalDisplayMode = value;
                    if (originalMode && currentImage != null)
                    {
                        UpdateImage();
                    }
                }
            }
        }
        
        /// <summary>缩放比例</summary>
        public double ZoomRatio
        {
            get => zoomRatio;
            set
            {
                double newZoom = Math.Max(Constants.MinZoomRatio, Math.Min(Constants.MaxZoomRatio, value));
                if (Math.Abs(zoomRatio - newZoom) > 0.001)
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 [ImageProcessor] ZoomRatio变化: {zoomRatio:F2} -> {newZoom:F2}, 原图模式: {originalMode}");
                    zoomRatio = newZoom;
                    if (currentImage != null && !originalMode)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 [ImageProcessor] 触发UpdateImage()更新主屏显示");
                        UpdateImage();
                    }
                    else if (originalMode)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔍 [ImageProcessor] 原图模式下不更新（正常模式才支持缩放）");
                    }
                }
            }
        }
        
        /// <summary>是否应用了变色效果</summary>
        public bool IsInverted
        {
            get => isInverted;
            set
            {
                if (isInverted != value)
                {
                    isInverted = value;
                    
                    // 清空缓存（重要！切换效果时必须清空缓存）
                    ClearCache();
                    
                    if (currentImage != null)
                    {
                        UpdateImage();
                    }
                }
            }
        }

        #endregion

        #region 图片加载功能

        /// <summary>
        /// 加载图片
        /// </summary>
        public bool LoadImage(string path)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // 验证文件
                var validateStart = sw.ElapsedMilliseconds;
                if (!ValidateImageFile(path))
                {
                    throw new Exception("无效的图片文件");
                }
                var validateTime = sw.ElapsedMilliseconds - validateStart;
                System.Diagnostics.Debug.WriteLine($"  ├─ 验证文件: {validateTime}ms");
                
                // 清除当前图片（不清除缓存，只清除当前引用）
                var clearStart = sw.ElapsedMilliseconds;
                ClearCurrentImageOnly();
                var clearTime = sw.ElapsedMilliseconds - clearStart;
                System.Diagnostics.Debug.WriteLine($"  ├─ 清除当前图片: {clearTime}ms");
                
                // ⚡ 先检查LRU缓存
                var cacheCheckStart = sw.ElapsedMilliseconds;
                if (_imageMemoryCache.TryGetValue(path, out SKBitmap cachedImage))
                {
                    var cacheTime = sw.ElapsedMilliseconds - cacheCheckStart;
                    System.Diagnostics.Debug.WriteLine($"  ├─ ⚡ LRU缓存命中: {cacheTime}ms - {System.IO.Path.GetFileName(path)}");
                    
                    // 🔧 性能优化：直接共享引用，不克隆（节省时间）
                    originalImage = cachedImage;
                    currentImage = cachedImage; // 直接共享，不Clone
                    currentImagePath = path;
                }
                else
                {
                    var cacheTime = sw.ElapsedMilliseconds - cacheCheckStart;
                    System.Diagnostics.Debug.WriteLine($"  ├─ 缓存未命中: {cacheTime}ms");
                    
                    // 缓存未命中，从磁盘加载
                    var diskLoadStart = sw.ElapsedMilliseconds;
                    originalImage = LoadImageOptimized(path);
                    var diskLoadTime = sw.ElapsedMilliseconds - diskLoadStart;
                    System.Diagnostics.Debug.WriteLine($"  ├─ 💾 从磁盘加载: {diskLoadTime}ms - {System.IO.Path.GetFileName(path)}");
                    
                    currentImage = originalImage; // 🔧 也不Clone，直接共享
                    currentImagePath = path;
                    
                    // ⚡ 加入LRU缓存
                    var cacheAddStart = sw.ElapsedMilliseconds;
                    var entryOptions = new MemoryCacheEntryOptions
                    {
                        // 按图片大小计算权重（1MB = 1权重单位）
                        Size = Math.Max(1, (originalImage.Width * originalImage.Height * 4) / (1024 * 1024)),
                        Priority = CacheItemPriority.Normal,
                        SlidingExpiration = TimeSpan.FromMinutes(10) // 10分钟未访问则过期
                    };
                    
                    _imageMemoryCache.Set(path, originalImage, entryOptions);
                    var cacheAddTime = sw.ElapsedMilliseconds - cacheAddStart;
                    System.Diagnostics.Debug.WriteLine($"  ├─ 📦 加入缓存: {cacheAddTime}ms (权重: {entryOptions.Size})");
                }
                
                // 🔧 重置节流时间戳，确保新图片能立即显示（不受节流限制）
                lastUpdateTime = DateTime.MinValue;
                
                // 更新显示
                var updateStart = sw.ElapsedMilliseconds;
                bool success = UpdateImage();
                var updateTime = sw.ElapsedMilliseconds - updateStart;
                System.Diagnostics.Debug.WriteLine($"  ├─ 更新显示: {updateTime}ms");
                
                if (success)
                {
                    // 重置滚动条到顶部
                    var scrollStart = sw.ElapsedMilliseconds;
                    scrollViewer.ScrollToTop();
                    scrollViewer.ScrollToLeftEnd();
                    var scrollTime = sw.ElapsedMilliseconds - scrollStart;
                    System.Diagnostics.Debug.WriteLine($"  ├─ 重置滚动条: {scrollTime}ms");
                    
                    // 🔧 重置关键帧索引（参考Python版本）
                    mainWindow.ResetKeyframeIndex();
                    
                    sw.Stop();
                    System.Diagnostics.Debug.WriteLine($"  └─ ImageProcessor.LoadImage 完成: {sw.ElapsedMilliseconds}ms");
                    return true;
                }
                
                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"  └─ ImageProcessor.LoadImage 失败: {sw.ElapsedMilliseconds}ms");
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        /// <summary>
        /// 验证图片文件
        /// </summary>
        private bool ValidateImageFile(string path)
        {
            if (!File.Exists(path))
                return false;
            
            var fileInfo = new FileInfo(path);
            
            // 检查文件大小
            if (fileInfo.Length > Constants.MaxImageFileSizeBytes)
            {
                throw new Exception($"图片文件过大 (>{Constants.MaxImageFileSizeBytes / (1024 * 1024)}MB)");
            }
            
            if (fileInfo.Length == 0)
                return false;
            
            // 检查文件格式
            string ext = fileInfo.Extension.ToLower();
            string[] validExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };
            
            return Array.Exists(validExtensions, e => e == ext);
        }
        
        /// <summary>
        /// 优化的图片加载方法
        /// 对于大图片，自动缩放到合适的显示尺寸，提升加载速度
        /// </summary>
        private SKBitmap LoadImageOptimized(string path)
        {
            try
            {
                // 获取显示区域尺寸
                double canvasWidth = scrollViewer.ActualWidth;
                double canvasHeight = scrollViewer.ActualHeight;
                
                // 如果容器尺寸未初始化，使用屏幕尺寸
                if (canvasWidth <= 0 || canvasHeight <= 0)
                {
                    canvasWidth = SystemParameters.PrimaryScreenWidth;
                    canvasHeight = SystemParameters.PrimaryScreenHeight;
                }
                
                // 计算目标尺寸（显示尺寸的1.5倍，保证质量）
                int targetWidth = (int)(canvasWidth * 1.5);
                int targetHeight = (int)(canvasHeight * 1.5);
                
                // ⚡ 直接加载图片
                var image = SKBitmap.Decode(path);
                
                if (image == null)
                {
                    throw new Exception("无法解码图片");
                }
                
                // 检查是否需要缩放（只对超大图片进行缩放）
                if (image.Width > targetWidth * 2 || image.Height > targetHeight * 2)
                {
                    // 计算缩放比例（保持宽高比）
                    double scaleX = (double)targetWidth / image.Width;
                    double scaleY = (double)targetHeight / image.Height;
                    double scale = Math.Max(scaleX, scaleY); // 使用较大的比例，确保覆盖显示区域
                    
                    int newWidth = (int)(image.Width * scale);
                    int newHeight = (int)(image.Height * scale);
                    
                    // 创建缩放后的图片
                    var resized = ResizeImage(image, newWidth, newHeight);
                    
                    // 释放原始大图
                    image.Dispose();
                    
                    return resized;
                }
                
                // 图片尺寸合适，直接返回
                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 优化加载失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 清除当前图片（仅清除引用，不清除缓存）
        /// </summary>
        private void ClearCurrentImageOnly()
        {
            // ⚠️ 性能优化后的逻辑：
            // - originalImage 和 currentImage 现在共享同一个引用（指向缓存中的图片）
            // - 不能 Dispose 它们，因为会破坏缓存中的图片
            // - 只需清空引用即可，由LRU缓存管理生命周期
            // - ⚡ 重要：不清除 imageCache（渲染缓存），保持渲染结果的缓存！
            
            // 清空引用（不Dispose，交给缓存管理）
            originalImage = null;
            currentImage = null;
            currentPhoto = null;
            currentImagePath = null;
            
            imageControl.Source = null;
            
            // ⚡ 性能优化：不清除 imageCache（保持渲染缓存）
        }
        
        /// <summary>
        /// 清除当前图片（包括所有缓存）
        /// </summary>
        public void ClearCurrentImage()
        {
            // 清除当前引用
            ClearCurrentImageOnly();
            
            // 清除LRU缓存
            ClearImageCache();
        }

        #endregion

        #region 图片显示和更新逻辑

        /// <summary>
        /// 更新图片显示
        /// </summary>
        public bool UpdateImage()
        {
            if (currentImage == null)
                return false;
            
            try
            {
                // 性能节流
                var currentTime = DateTime.Now;
                if (currentTime - lastUpdateTime < updateThrottleInterval)
                {
                    return true;
                }
                lastUpdateTime = currentTime;
                
                // 获取画布尺寸
                double canvasWidth = scrollViewer.ActualWidth;
                double canvasHeight = scrollViewer.ActualHeight;
                
                if (canvasWidth <= 0 || canvasHeight <= 0)
                {
                    // 等待ScrollViewer初始化完成
                    scrollViewer.Loaded += (s, e) => UpdateImage();
                    return true;
                }
                
                // 计算显示尺寸
                var (newWidth, newHeight) = CalculateSizeWithScale(canvasWidth, canvasHeight);
                
                // 获取或创建缓存图片
                var photo = GetOrCreateCachedImage(newWidth, newHeight);
                
                if (photo == null)
                    return false;
                
                // 更新画布显示
                bool success = UpdateCanvasDisplay(photo, newWidth, newHeight, canvasWidth, canvasHeight);
                
                return success;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        /// <summary>
        /// 计算图片显示尺寸（支持原图模式和正常模式）
        /// </summary>
        private (int width, int height) CalculateSizeWithScale(double canvasWidth, double canvasHeight)
        {
            if (originalMode)
            {
                // 原图模式
                return CalculateOriginalModeSize(canvasWidth, canvasHeight);
            }
            else
            {
                // 正常模式
                return CalculateNormalModeSize(canvasWidth, canvasHeight);
            }
        }
        
        /// <summary>
        /// 计算原图模式下的尺寸
        /// </summary>
        private (int width, int height) CalculateOriginalModeSize(double canvasWidth, double canvasHeight)
        {
            double widthRatio = canvasWidth / currentImage.Width;
            double heightRatio = canvasHeight / currentImage.Height;
            
            double scaleRatio;
            
            if (originalDisplayMode == OriginalDisplayMode.Stretch)
            {
                // 拉伸模式：使用高度比例,确保内容完整(宽度会被拉伸填满)
                scaleRatio = heightRatio;
            }
            else
            {
                // 适中模式：选择较小的比例确保完整显示(等比缩放)
                scaleRatio = Math.Min(widthRatio, heightRatio);
            }
            
            // 智能缩放策略
            if (scaleRatio >= 1.0)
            {
                // 图片小于屏幕：智能放大
                double screenArea = canvasWidth * canvasHeight;
                double imageArea = currentImage.Width * currentImage.Height;
                double areaRatio = screenArea / imageArea;
                
                // 根据面积比例动态调整最大放大倍数
                double maxScale;
                if (areaRatio > Constants.AreaRatioThreshold16X)
                    maxScale = Constants.OriginalModeMaxScaleArea16X;
                else if (areaRatio > Constants.AreaRatioThreshold9X)
                    maxScale = Constants.OriginalModeMaxScaleArea9X;
                else if (areaRatio > Constants.AreaRatioThreshold4X)
                    maxScale = Constants.OriginalModeMaxScaleArea4X;
                else
                    maxScale = Constants.OriginalModeMaxScaleDefault;
                
                scaleRatio = Math.Min(scaleRatio, maxScale);
            }
            
            int newWidth, newHeight;
            
            if (originalDisplayMode == OriginalDisplayMode.Stretch)
            {
                // 拉伸模式：宽度填满，高度按比例
                newWidth = (int)canvasWidth;
                newHeight = (int)(currentImage.Height * scaleRatio);
            }
            else
            {
                // 适中模式：等比缩放
                newWidth = (int)(currentImage.Width * scaleRatio);
                newHeight = (int)(currentImage.Height * scaleRatio);
            }
            
            return (newWidth, newHeight);
        }
        
        /// <summary>
        /// 计算正常模式下的尺寸
        /// </summary>
        private (int width, int height) CalculateNormalModeSize(double canvasWidth, double canvasHeight)
        {
            // 基础缩放比例：宽度填满画布
            double baseRatio = canvasWidth / currentImage.Width;
            
            // 应用用户缩放
            double finalRatio = baseRatio * zoomRatio;
            
            // 等比缩放宽度和高度
            int newWidth = (int)(currentImage.Width * finalRatio);
            int newHeight = (int)(currentImage.Height * finalRatio);
            
            return (newWidth, newHeight);
        }
        
        /// <summary>
        /// 获取或创建缓存图片
        /// 关键：效果模式和普通模式使用不同的缓存键！
        /// </summary>
        private BitmapSource GetOrCreateCachedImage(int newWidth, int newHeight)
        {
            // 生成缓存键（包含效果状态）
            string cacheKey = $"{currentImagePath}_{newWidth}x{newHeight}_{(isInverted ? "inverted" : "normal")}";
            
            // 检查缓存
            if (imageCache.TryGetValue(cacheKey, out var cachedPhoto))
            {
                // ⚡ 更新LRU访问时间
                imageCacheAccessTime[cacheKey] = DateTime.Now;
                return cachedPhoto;
            }
            
            // 生成新图片
            var resizedImage = ResizeAndApplyEffects(newWidth, newHeight);
            if (resizedImage == null)
                return null;
            
            // 转换为BitmapSource
            var photo = ConvertToBitmapSource(resizedImage);
            
            // 缓存（效果模式也缓存，但会在切换时清空）
            if (photo != null)
            {
                imageCache[cacheKey] = photo;
                imageCacheAccessTime[cacheKey] = DateTime.Now; // ⚡ 记录访问时间
                
                // 限制缓存大小
                if (imageCache.Count > Constants.RenderCacheCleanupThreshold)
                {
                    ClearOldCache();
                }
            }
            
            resizedImage.Dispose();
            
            return photo;
        }
        
        /// <summary>
        /// 调整图片尺寸并应用效果
        /// </summary>
        private SKBitmap ResizeAndApplyEffects(int newWidth, int newHeight)
        {
            try
            {
                // 第一步：缩放到目标尺寸
                var resizedImage = ResizeImage(currentImage, newWidth, newHeight);
                
                if (resizedImage == null)
                    return null;
                
                // 第二步：在缩放后的图片上应用效果（如果启用）
                if (isInverted)
                {
                    // 应用变色效果（直接修改resizedImage）
                    ApplyYellowTextEffect(resizedImage);
                }
                
                return resizedImage;
            }
            catch (Exception)
            {
                return null;
            }
        }
        
        /// <summary>
        /// 缩放图片（GPU优先，CPU降级）
        /// </summary>
        private SKBitmap ResizeImage(SKBitmap source, int targetWidth, int targetHeight)
        {
            if (source == null || targetWidth <= 0 || targetHeight <= 0)
                return null;
            
            try
            {
                // 🎮 使用GPU加速缩放（如果GPU不可用，自动降级到CPU）
                return GPUContext.Instance.ScaleImageGpu(source, targetWidth, targetHeight, SKFilterQuality.High);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [ImageProcessor] 缩放失败: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 应用黄色文本效果（公共方法，用于外部保存）
        /// </summary>
        public SKBitmap ApplyYellowTextEffectForSave()
        {
            if (currentImage == null)
                return null;
            
            // 克隆当前图片并应用效果
            var result = currentImage.Copy();
            ApplyYellowTextEffect(result);
            return result;
        }

        /// <summary>
        /// 应用黄字效果（智能检测背景类型）
        /// </summary>
        public void ApplyYellowTextEffect(SKBitmap image)
        {
            if (image == null) return;
            
            try
            {
                var startTime = DateTime.Now;
                
                // 1. 检测背景类型
                var bgType = DetectBackgroundType(image);
                
                // 2. 获取黄字颜色设置
                var yellowColor = GetYellowColorSettings();
                
                // 3. 直接处理图片的每个像素
                unsafe
                {
                    var pixels = (uint*)image.GetPixels().ToPointer();
                    int pixelCount = image.Width * image.Height;
                    
                    for (int i = 0; i < pixelCount; i++)
                    {
                        uint pixel = pixels[i];
                        
                        // 提取RGBA（SkiaSharp使用BGRA顺序）
                        byte b = (byte)(pixel & 0xFF);
                        byte g = (byte)((pixel >> 8) & 0xFF);
                        byte r = (byte)((pixel >> 16) & 0xFF);
                        byte a = (byte)((pixel >> 24) & 0xFF);
                        
                        // 计算亮度（使用标准公式）
                        float luminance = 0.299f * r + 0.587f * g + 0.114f * b;
                        
                        // 根据背景类型判断是否是文字
                        bool isText;
                        if (bgType == BackgroundType.Black)
                        {
                            // 深色背景：亮色像素是文字
                            isText = luminance > Constants.LightTextBrightnessDarkBg;
                        }
                        else
                        {
                            // 浅色背景：暗色像素是文字
                            isText = luminance < Constants.DarkTextBrightnessLightBg;
                        }
                        
                        // 设置颜色（BGRA顺序）
                        if (isText)
                        {
                            // 文字：设置为黄色
                            pixels[i] = (uint)(0xFF000000 | ((uint)yellowColor.Red << 16) | ((uint)yellowColor.Green << 8) | (uint)yellowColor.Blue);
                        }
                        else
                        {
                            // 背景：设置为黑色
                            pixels[i] = 0xFF000000; // 黑色 + 完全不透明
                        }
                    }
                }
                
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            }
            catch (Exception)
            {
            }
        }
        
        /// <summary>
        /// 检测背景类型（深色或浅色）
        /// </summary>
        private BackgroundType DetectBackgroundType(SKBitmap image)
        {
            try
            {
                int sampleSize = Math.Min(20, Math.Min(image.Width, image.Height) / 20);
                
                var samples = new List<float>();
                
                // 采样8个位置的小区域
                void SampleRegion(int startX, int startY, int width, int height)
                {
                    for (int y = startY; y < startY + height && y < image.Height; y++)
                    {
                        for (int x = startX; x < startX + width && x < image.Width; x++)
                        {
                            var pixel = image.GetPixel(x, y);
                            float luminance = 0.299f * pixel.Red + 0.587f * pixel.Green + 0.114f * pixel.Blue;
                            samples.Add(luminance);
                        }
                    }
                }
                
                // 四个角
                SampleRegion(0, 0, sampleSize, sampleSize); // 左上
                SampleRegion(image.Width - sampleSize, 0, sampleSize, sampleSize); // 右上
                SampleRegion(0, image.Height - sampleSize, sampleSize, sampleSize); // 左下
                SampleRegion(image.Width - sampleSize, image.Height - sampleSize, sampleSize, sampleSize); // 右下
                
                // 四条边中心
                SampleRegion(0, image.Height / 2, sampleSize, sampleSize); // 左边中
                SampleRegion(image.Width - sampleSize, image.Height / 2, sampleSize, sampleSize); // 右边中
                SampleRegion(image.Width / 2, 0, sampleSize, sampleSize); // 上边中
                SampleRegion(image.Width / 2, image.Height - sampleSize, sampleSize, sampleSize); // 下边中
                
                // 计算平均亮度
                float avgLuminance = samples.Count > 0 ? samples.Average() : 128;
                
                // 判断背景类型
                var bgType = avgLuminance < Constants.DarkBackgroundThreshold ? BackgroundType.Black : BackgroundType.White;
                
                return bgType;
            }
            catch (Exception)
            {
                return BackgroundType.White; // 默认返回浅色背景
            }
        }
        
        /// <summary>
        /// 获取黄字颜色设置
        /// </summary>
        private SKColor GetYellowColorSettings()
        {
            // 从主应用获取当前目标颜色
            if (mainWindow != null)
            {
                var targetColor = mainWindow.GetCurrentTargetColor();
                return targetColor;
            }
            
            // 默认黄字颜色（淡黄）
            return new SKColor(174, 159, 112);
        }
        
        /// <summary>
        /// 转换SKBitmap为WPF BitmapSource（高性能直接转换）
        /// </summary>
        private WriteableBitmap ConvertToBitmapSource(SKBitmap skBitmap)
        {
            try
            {
                if (skBitmap == null)
                    return null;
                
                var info = skBitmap.Info;
                var wb = new WriteableBitmap(info.Width, info.Height, 96, 96, 
                                             PixelFormats.Bgra32, null);
                
                wb.Lock();
                try
                {
                    unsafe
                    {
                        // ⚡ 直接复制像素数据，超快！无需编码/解码
                        var src = skBitmap.GetPixels();
                        var dst = wb.BackBuffer;
                        var size = skBitmap.ByteCount;
                        
                        Buffer.MemoryCopy(
                            src.ToPointer(),
                            dst.ToPointer(),
                            size,
                            size
                        );
                    }
                    
                    wb.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
                }
                finally
                {
                    wb.Unlock();
                }
                
                wb.Freeze(); // 冻结以提高性能
                
                return wb;
            }
            catch (Exception)
            {
                return null;
            }
        }
        
        /// <summary>
        /// 更新画布显示
        /// </summary>
        private bool UpdateCanvasDisplay(BitmapSource photo, int newWidth, int newHeight, 
                                         double canvasWidth, double canvasHeight)
        {
            try
            {
                // 保存当前图片引用
                currentPhoto = photo;
                
                // 更新图片控件
                imageControl.Source = photo;
                imageControl.Width = newWidth;
                imageControl.Height = newHeight;
                
                // 设置对齐方式
                if (originalMode)
                {
                    // 原图模式：水平和垂直都居中
                    imageControl.HorizontalAlignment = HorizontalAlignment.Center;
                    imageControl.VerticalAlignment = VerticalAlignment.Center;
                }
                else
                {
                    // 正常模式：拉伸填满宽度，垂直顶部对齐
                    imageControl.HorizontalAlignment = HorizontalAlignment.Stretch;
                    imageControl.VerticalAlignment = VerticalAlignment.Top;
                }
                
                // 设置滚动区域
                SetScrollRegion(newHeight, canvasHeight);
                
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 滚动区域设置逻辑

        /// <summary>
        /// 设置滚动区域
        /// </summary>
        private void SetScrollRegion(double imageHeight, double canvasHeight)
        {
            try
            {
                double scrollHeight;
                
                if (originalMode)
                {
                    // 原图模式
                    if (imageHeight <= canvasHeight)
                    {
                        // 图片完全适合屏幕，不需要额外空间
                        scrollHeight = canvasHeight;
                        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    }
                    else
                    {
                        // 图片高度超过屏幕，添加额外空间（图片高度 + 屏幕高度）
                        scrollHeight = imageHeight + canvasHeight;
                        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    }
                }
                else
                {
                    // 正常模式
                    if (imageHeight >= canvasHeight)
                    {
                        // 图片高度超过或等于屏幕：图片高度 + 一个屏幕高度的额外空间
                        scrollHeight = imageHeight + canvasHeight;
                        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    }
                    else
                    {
                        // 图片完全适合屏幕
                        scrollHeight = canvasHeight;
                        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    }
                }
                
                // 设置容器高度来控制滚动区域
                imageContainer.Height = scrollHeight;
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region 缓存管理

        /// <summary>
        /// 清除所有缓存（包括LRU图片缓存和BitmapSource缓存）
        /// </summary>
        public void ClearCache()
        {
            imageCache.Clear();
            imageCacheAccessTime.Clear();
            ClearImageCache();
        }
        
        /// <summary>
        /// 清除LRU图片缓存
        /// </summary>
        public void ClearImageCache()
        {
            if (_imageMemoryCache is MemoryCache mc)
            {
                mc.Compact(1.0); // 清除100%的缓存项
            }
        }
        
        /// <summary>
        /// 获取LRU缓存统计信息
        /// </summary>
        public string GetCacheStats()
        {
            if (_imageMemoryCache is MemoryCache mc)
            {
                var stats = mc.GetCurrentStatistics();
                return $"缓存项数: {stats?.CurrentEntryCount ?? 0}, 当前大小: {stats?.CurrentEstimatedSize ?? 0}";
            }
            return "缓存统计不可用";
        }
        
        /// <summary>
        /// 清除旧的BitmapSource缓存（LRU策略）
        /// </summary>
        private void ClearOldCache()
        {
            if (imageCache.Count <= Constants.RenderCacheCleanupThreshold)
                return;
            
            try
            {
                // ⚡ LRU策略：删除最久未使用的50%缓存
                int targetSize = Constants.RenderCacheSize;
                int toRemove = imageCache.Count - targetSize;
                
                if (toRemove <= 0)
                    return;
                
                // 按访问时间排序，删除最旧的
                var itemsToRemove = imageCacheAccessTime
                    .OrderBy(kvp => kvp.Value)
                    .Take(toRemove)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in itemsToRemove)
                {
                    imageCache.Remove(key);
                    imageCacheAccessTime.Remove(key);
                }
            }
            catch (Exception)
            {
                // 失败时简单清空
                imageCache.Clear();
                imageCacheAccessTime.Clear();
            }
        }

        #endregion

        #region 缩放功能

        /// <summary>
        /// 重置缩放
        /// </summary>
        public void ResetZoom()
        {
            ZoomRatio = Constants.DefaultZoomRatio;
            scrollViewer.ScrollToTop();
            scrollViewer.ScrollToLeftEnd();
        }
        
        /// <summary>
        /// 适应视图大小
        /// </summary>
        public void FitToView()
        {
            if (currentImage == null)
                return;
            
            double canvasWidth = scrollViewer.ActualWidth;
            double canvasHeight = scrollViewer.ActualHeight;
            
            if (canvasWidth <= 0 || canvasHeight <= 0)
                return;
            
            double scaleX = (canvasWidth - 40) / currentImage.Width;
            double scaleY = (canvasHeight - 40) / currentImage.Height;
            double scale = Math.Min(scaleX, scaleY);
            
            scale = Math.Max(Constants.MinZoomRatio, Math.Min(1.0, scale));
            
            ZoomRatio = scale;
        }

        #endregion

        #region 资源清理

        public void Dispose()
        {
            ClearCurrentImage();
            imageCache.Clear();
            
            // 释放MemoryCache
            if (_imageMemoryCache is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        #endregion
    }
}

