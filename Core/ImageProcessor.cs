using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using ImageColorChanger.UI;

namespace ImageColorChanger.Core
{

    /// <summary>
    /// 图片处理器 - 负责图片加载、显示、缩放和效果处理
    /// </summary>
    public class ImageProcessor : IDisposable
    {
        #region 字段

        private readonly MainWindow mainWindow;
        private readonly ScrollViewer scrollViewer;
        private readonly System.Windows.Controls.Image imageControl;
        private readonly Grid imageContainer; // 图片容器（用于控制滚动区域）
        
        // 图片状态
        private Image<Rgba32> originalImage;
        private Image<Rgba32> currentImage;
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
        }

        #endregion

        #region 公共属性

        /// <summary>当前图片</summary>
        public Image<Rgba32> CurrentImage => currentImage;
        
        /// <summary>原始图片</summary>
        public Image<Rgba32> OriginalImage => originalImage;
        
        /// <summary>当前图片路径</summary>
        public string CurrentImagePath => currentImagePath;
        
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
                    zoomRatio = newZoom;
                    if (currentImage != null && !originalMode)
                    {
                        UpdateImage();
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
            try
            {
                // 验证文件
                if (!ValidateImageFile(path))
                {
                    throw new Exception("无效的图片文件");
                }
                
                // 清除当前图片
                ClearCurrentImage();
                
                // 加载新图片
                originalImage = Image.Load<Rgba32>(path);
                currentImage = originalImage.Clone();
                currentImagePath = path;
                
                // 更新显示
                bool success = UpdateImage();
                
                if (success)
                {
                    // 重置滚动条到顶部
                    scrollViewer.ScrollToTop();
                    scrollViewer.ScrollToLeftEnd();
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载图片失败: {ex.Message}");
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
        /// 清除当前图片
        /// </summary>
        public void ClearCurrentImage()
        {
            originalImage?.Dispose();
            currentImage?.Dispose();
            originalImage = null;
            currentImage = null;
            currentPhoto = null;
            currentImagePath = null;
            
            imageControl.Source = null;
            
            // 清除缓存
            ClearCache();
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新图片失败: {ex.Message}");
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
            // 基础缩放比例：始终填满宽度
            double baseRatio = canvasWidth / currentImage.Width;
            
            // 应用用户缩放
            double finalRatio = baseRatio * zoomRatio;
            
            // 确保宽度填满画布
            int newWidth = (int)canvasWidth;
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
                System.Diagnostics.Debug.WriteLine($"✅ 缓存命中: {newWidth}x{newHeight} ({(isInverted ? "效果" : "正常")})");
                return cachedPhoto;
            }
            
            System.Diagnostics.Debug.WriteLine($"⚡ 生成新图片: {newWidth}x{newHeight} ({(isInverted ? "效果" : "正常")})");
            
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
                
                // 限制缓存大小
                if (imageCache.Count > Constants.MemoryCacheSize)
                {
                    ClearOldCache();
                }
            }
            
            resizedImage.Dispose();
            
            return photo;
        }
        
        /// <summary>
        /// 调整图片尺寸并应用效果
        /// 关键：先缩放，后应用效果！效果不改变图片尺寸！
        /// </summary>
        private Image<Rgba32> ResizeAndApplyEffects(int newWidth, int newHeight)
        {
            try
            {
                // 计算缩放比例
                double scaleX = (double)newWidth / currentImage.Width;
                double scaleY = (double)newHeight / currentImage.Height;
                double scaleRatio = Math.Min(scaleX, scaleY);
                
                // 第一步：克隆并执行缩放到目标尺寸
                var resizedImage = currentImage.Clone();
                
                // 使用智能算法选择执行缩放
                if (scaleRatio > 1.0)
                {
                    // 放大：使用Bicubic，质量好
                    resizedImage.Mutate(x => x.Resize(newWidth, newHeight, KnownResamplers.Bicubic));
                }
                else if (scaleRatio < 0.5)
                {
                    // 大幅缩小：使用Box，性能最佳
                    resizedImage.Mutate(x => x.Resize(newWidth, newHeight, KnownResamplers.Box));
                }
                else
                {
                    // 小幅缩小：使用Bicubic，平衡质量和性能
                    resizedImage.Mutate(x => x.Resize(newWidth, newHeight, KnownResamplers.Bicubic));
                }
                
                // 第二步：在缩放后的图片上应用效果（如果启用）
                if (isInverted)
                {
                    // 应用变色效果（直接修改resizedImage）
                    ApplyYellowTextEffect(resizedImage);
                }
                
                return resizedImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"调整图片尺寸失败: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 应用黄色文本效果（公共方法，用于外部保存）
        /// </summary>
        public Image<Rgba32> ApplyYellowTextEffectForSave()
        {
            if (currentImage == null)
                return null;
            
            // 克隆当前图片并应用效果
            var result = currentImage.Clone();
            ApplyYellowTextEffect(result);
            return result;
        }

        /// <summary>
        /// 应用黄字效果（智能检测背景类型）
        /// 重要：此方法不改变图片尺寸！
        /// </summary>
        public Image<Rgba32> ApplyYellowTextEffect(Image<Rgba32> image)
        {
            if (image == null) return null;
            
            try
            {
                var startTime = DateTime.Now;
                
                // 1. 检测背景类型
                var bgType = DetectBackgroundType(image);
                
                // 2. 获取黄字颜色设置
                var yellowColor = GetYellowColorSettings();
                
                // 3. 直接处理图片的每个像素
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        
                        for (int x = 0; x < row.Length; x++)
                        {
                            var pixel = row[x];
                            
                            // 计算亮度（使用标准公式）
                            float luminance = 0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B;
                            
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
                            
                            // 设置颜色
                            if (isText)
                            {
                                // 文字：设置为黄色
                                row[x] = new Rgba32(yellowColor.R, yellowColor.G, yellowColor.B, 255);
                            }
                            else
                            {
                                // 背景：设置为黑色
                                row[x] = new Rgba32(0, 0, 0, 255);
                            }
                        }
                    }
                });
                
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                System.Diagnostics.Debug.WriteLine($"✨ 变色处理完成: {elapsed:F1}ms ({image.Width}x{image.Height})");
                
                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 应用黄字效果失败: {ex.Message}");
                return image;
            }
        }
        
        /// <summary>
        /// 检测背景类型（深色或浅色）
        /// 通过采样图片边缘区域的亮度来判断
        /// </summary>
        private BackgroundType DetectBackgroundType(Image<Rgba32> image)
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
                            var pixel = image[x, y];
                            float luminance = 0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B;
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
                
                System.Diagnostics.Debug.WriteLine($"🔍 背景检测: 平均亮度={avgLuminance:F1}, 类型={bgType}");
                
                return bgType;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检测背景类型失败: {ex.Message}");
                return BackgroundType.White; // 默认返回浅色背景
            }
        }
        
        /// <summary>
        /// 获取黄字颜色设置
        /// </summary>
        private Rgba32 GetYellowColorSettings()
        {
            // 从主应用获取当前目标颜色
            if (mainWindow != null)
            {
                var targetColor = mainWindow.GetCurrentTargetColor();
                return targetColor;
            }
            
            // 默认黄字颜色（淡黄）
            return new Rgba32(174, 159, 112);
        }
        
        /// <summary>
        /// 转换ImageSharp图片为WPF BitmapSource
        /// </summary>
        private BitmapSource ConvertToBitmapSource(Image<Rgba32> image)
        {
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    image.SaveAsPng(memoryStream);
                    memoryStream.Position = 0;
                    
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memoryStream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // 重要：冻结以提高性能
                    
                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"转换BitmapSource失败: {ex.Message}");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新画布显示失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 滚动区域设置逻辑

        /// <summary>
        /// 设置滚动区域
        /// Python逻辑：scroll_height = new_height + canvas_height（图片高度 + 一个屏幕高度的额外空间）
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
                    // 注意: 即使图片高度等于屏幕高度,也需要额外空间以支持滚动到底部
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
                // 这样滚动到底部时，图片下方会有额外的空间（与Python一致）
                imageContainer.Height = scrollHeight;
                
                System.Diagnostics.Debug.WriteLine($"📏 滚动区域: 图片高度={imageHeight:F0}, 画布高度={canvasHeight:F0}, 滚动高度={scrollHeight:F0}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置滚动区域失败: {ex.Message}");
            }
        }

        #endregion

        #region 缓存管理

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public void ClearCache()
        {
            imageCache.Clear();
        }
        
        /// <summary>
        /// 清除旧缓存
        /// </summary>
        private void ClearOldCache()
        {
            // 简单策略：缓存超过限制时清空所有缓存
            if (imageCache.Count > Constants.CacheCleanupThreshold)
            {
                imageCache.Clear();
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
        }

        #endregion
    }
}

