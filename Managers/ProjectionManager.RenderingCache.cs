using System;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 渲染缓存与位图转换逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 生成投影缓存键
        /// </summary>
        private string GenerateProjectionCacheKey(int width, int height)
        {
            return ProjectionImageRenderService.BuildProjectionCacheKey(
                _currentImagePath,
                width,
                height,
                _isColorEffectEnabled,
                _isOriginalMode,
                _originalDisplayMode,
                _zoomRatio);
        }

        private BitmapSource GetOrCreateProjectionBitmap(string cacheKey, int newWidth, int newHeight)
        {
            return _projectionImageRenderService.GetOrCreateProjectionBitmap(
                _currentImage,
                cacheKey,
                newWidth,
                newHeight,
                _isColorEffectEnabled,
                ConvertToBitmapSource);
        }

        /// <summary>
        /// 清除投影缓存
        /// </summary>
        public void ClearProjectionCache()
        {
            if (_projectionCache is MemoryCache mc)
            {
                mc.Compact(1.0);
            }
        }

        /// <summary>
        /// 获取投影缓存统计信息
        /// </summary>
        public string GetProjectionCacheStats()
        {
            if (_projectionCache is MemoryCache mc)
            {
                var stats = mc.GetCurrentStatistics();
                return $"投影缓存项数: {stats?.CurrentEntryCount ?? 0}, 当前大小: {stats?.CurrentEstimatedSize ?? 0}";
            }

            return "投影缓存统计不可用";
        }

        /// <summary>
        /// 将 SkiaSharp 图片转换为 WPF BitmapSource
        /// </summary>
        private WriteableBitmap ConvertToBitmapSource(SKBitmap skBitmap)
        {
            try
            {
                if (skBitmap == null)
                {
                    return null;
                }

                var info = skBitmap.Info;
                var bitmap = new WriteableBitmap(
                    info.Width,
                    info.Height,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null);

                bitmap.Lock();
                try
                {
                    unsafe
                    {
                        var src = skBitmap.GetPixels();
                        var dst = bitmap.BackBuffer;
                        var size = skBitmap.ByteCount;
                        Buffer.MemoryCopy(src.ToPointer(), dst.ToPointer(), size, size);
                    }

                    bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, info.Width, info.Height));
                }
                finally
                {
                    bitmap.Unlock();
                }

                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
