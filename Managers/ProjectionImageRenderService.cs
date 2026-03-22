using System;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影图像渲染与缓存服务，隔离 ProjectionManager 中的图像处理细节。
    /// </summary>
    public sealed class ProjectionImageRenderService
    {
        private readonly ImageProcessor _imageProcessor;
        private readonly GPUContext _gpuContext;
        private readonly IMemoryCache _cache;

        public ProjectionImageRenderService(ImageProcessor imageProcessor, GPUContext gpuContext, IMemoryCache cache)
        {
            _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
            _gpuContext = gpuContext ?? throw new ArgumentNullException(nameof(gpuContext));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public static string BuildProjectionCacheKey(
            string currentImagePath,
            int width,
            int height,
            bool isColorEffectEnabled,
            bool isOriginalMode,
            OriginalDisplayMode originalDisplayMode,
            int originalTopScalePercent,
            double zoomRatio)
        {
            return $"{currentImagePath}_{width}x{height}_{(isColorEffectEnabled ? "inverted" : "normal")}_{isOriginalMode}_{originalDisplayMode}_{originalTopScalePercent}_{zoomRatio:F2}";
        }

        public BitmapSource GetOrCreateProjectionBitmap(
            SKBitmap sourceImage,
            string cacheKey,
            int newWidth,
            int newHeight,
            bool isColorEffectEnabled,
            Func<SKBitmap, WriteableBitmap> convertToBitmapSource)
        {
            if (sourceImage == null || string.IsNullOrWhiteSpace(cacheKey))
            {
                return null;
            }

            if (_cache.TryGetValue(cacheKey, out BitmapSource cachedBitmap))
            {
                return cachedBitmap;
            }

            var projectionBitmap = RenderProjectionBitmap(
                sourceImage,
                newWidth,
                newHeight,
                isColorEffectEnabled,
                convertToBitmapSource);

            if (projectionBitmap == null)
            {
                return null;
            }

            var entryOptions = new MemoryCacheEntryOptions
            {
                Size = Math.Max(1, (newWidth * newHeight * 4) / (1024 * 1024)),
                Priority = CacheItemPriority.Normal,
                SlidingExpiration = TimeSpan.FromMinutes(10)
            };
            _cache.Set(cacheKey, projectionBitmap, entryOptions);
            return projectionBitmap;
        }

        private BitmapSource RenderProjectionBitmap(
            SKBitmap sourceImage,
            int newWidth,
            int newHeight,
            bool isColorEffectEnabled,
            Func<SKBitmap, WriteableBitmap> convertToBitmapSource)
        {
            var processedImage = _gpuContext.ScaleImageGpu(
                sourceImage,
                newWidth,
                newHeight,
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            if (processedImage == null)
            {
                return null;
            }

            if (isColorEffectEnabled)
            {
                _imageProcessor.ApplyYellowTextEffect(processedImage);
            }

            var projectionBitmap = convertToBitmapSource?.Invoke(processedImage);
            processedImage.Dispose();
            return projectionBitmap;
        }
    }
}
