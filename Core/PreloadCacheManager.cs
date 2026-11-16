using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 智能预缓存管理器
    /// 根据不同模式精准预缓存即将需要的图片，实现秒切换
    /// </summary>
    public class PreloadCacheManager
    {
        private readonly IMemoryCache _imageMemoryCache;
        private readonly DatabaseManager _dbManager;
        private readonly ImageProcessor _imageProcessor;
        
        // 预缓存配置
        private const int SEQUENCE_PRELOAD_COUNT = 10; // 顺序模式预缓存数量
        private readonly ImagePreloadPipeline _pipeline;
        
        // 预渲染目标尺寸（从主窗口/投影窗口获取）
        private int _prerenderWidth = 1637;  // 默认主窗口尺寸
        private int _prerenderHeight = 955;
        
        // 投影显示器尺寸（动态获取）
        private int _projectionWidth = 1920;  // 默认投影尺寸
        private int _projectionHeight = 1080;
        
        public PreloadCacheManager(IMemoryCache imageMemoryCache, DatabaseManager dbManager, ImageProcessor imageProcessor)
        {
            _imageMemoryCache = imageMemoryCache ?? throw new ArgumentNullException(nameof(imageMemoryCache));
            _dbManager = dbManager ?? throw new ArgumentNullException(nameof(dbManager));
            _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
            _pipeline = new ImagePreloadPipeline(_imageMemoryCache);
        }
        
        /// <summary>
        /// 设置预渲染目标尺寸
        /// </summary>
        public void SetPrerenderSize(int width, int height)
        {
            _prerenderWidth = width;
            _prerenderHeight = height;
            //System.Diagnostics.Debug.WriteLine($" [预渲染] 设置目标尺寸: {width}x{height}");
        }
        
        /// <summary>
        /// 设置投影显示器尺寸
        /// </summary>
        public void SetProjectionSize(int width, int height)
        {
            _projectionWidth = width;
            _projectionHeight = height;
            //System.Diagnostics.Debug.WriteLine($" [预渲染] 设置投影尺寸: {width}x{height}");
        }
        
        #region 原图模式预缓存
        
        /// <summary>
        /// 为原图循环模式预缓存相似图片
        /// </summary>
        public async Task PreloadForLoopModeAsync(int currentImageId, List<(int id, string name, string path)> similarImages)
        {
            using var batch = _pipeline.BeginBatch();
            if (similarImages == null || similarImages.Count <= 1)
            {
                //System.Diagnostics.Debug.WriteLine(" [预缓存] 循环模式: 没有需要预缓存的相似图片");
                return;
            }
            
            
            try
            {
                // 找到当前图片在相似图片列表中的位置
                int currentIndex = similarImages.FindIndex(img => img.id == currentImageId);
                if (currentIndex < 0)
                {
                    //System.Diagnostics.Debug.WriteLine($" [预缓存] 循环模式: 当前图片不在相似列表中 (ID:{currentImageId})");
                    return;
                }
                
                // 预缓存列表：优先缓存下一张，然后是上一张，再是再下一张...
                var preloadList = new List<string>();
                
                // 添加下一张（最高优先级）
                int nextIndex = (currentIndex + 1) % similarImages.Count;
                if (nextIndex != currentIndex)
                {
                    preloadList.Add(similarImages[nextIndex].path);
                }
                
                // 添加上一张
                int prevIndex = (currentIndex - 1 + similarImages.Count) % similarImages.Count;
                if (prevIndex != currentIndex && prevIndex != nextIndex)
                {
                    preloadList.Add(similarImages[prevIndex].path);
                }
                
                // 添加后续的图片（按顺序）
                for (int i = 2; i < similarImages.Count && preloadList.Count < similarImages.Count - 1; i++)
                {
                    int idx = (currentIndex + i) % similarImages.Count;
                    if (idx != currentIndex)
                    {
                        preloadList.Add(similarImages[idx].path);
                    }
                }
                
                //System.Diagnostics.Debug.WriteLine($" [预缓存] 循环模式: 准备预缓存 {preloadList.Count} 张相似图片");
                
                // 异步加载
                await PreloadImagesAsync(preloadList, batch);
            }
            catch (OperationCanceledException)
            {
                //System.Diagnostics.Debug.WriteLine(" [预缓存] 循环模式: 已取消");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" [预缓存] 循环模式失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 为原图顺序模式预缓存接下来的N张图片
        /// </summary>
        public async Task PreloadForSequenceModeAsync(int currentImageId, int folderId, int preloadCount = SEQUENCE_PRELOAD_COUNT)
        {
            using var batch = _pipeline.BeginBatch();
            
            try
            {
                // 获取当前文件信息
                var currentFile = _dbManager.GetMediaFileById(currentImageId);
                if (currentFile == null)
                {
                    //System.Diagnostics.Debug.WriteLine($" [预缓存] 顺序模式: 找不到当前文件 (ID:{currentImageId})");
                    return;
                }
                
                // 获取文件夹中所有图片
                // 优先使用OrderIndex（自定义排序），如果没有OrderIndex则按文件名排序
                var allImages = _dbManager.GetMediaFilesByFolder(folderId)
                    .Where(f => f.FileType == FileType.Image)
                    .OrderBy(f => f.OrderIndex ?? int.MaxValue) // 优先使用OrderIndex
                    .ThenBy(f => f.Name) // OrderIndex相同时按文件名排序
                    .ToList();
                
                // 找到当前图片的位置
                int currentIndex = allImages.FindIndex(f => f.Id == currentImageId);
                if (currentIndex < 0)
                {
                    //System.Diagnostics.Debug.WriteLine($" [预缓存] 顺序模式: 当前图片不在文件夹中 (ID:{currentImageId})");
                    return;
                }
                
                // 计算要预缓存的图片列表（优先下一张，然后往后10张）
                var preloadList = new List<string>();
                
                for (int i = 1; i <= preloadCount && (currentIndex + i) < allImages.Count; i++)
                {
                    preloadList.Add(allImages[currentIndex + i].Path);
                }
                
                if (preloadList.Count == 0)
                {
                    //System.Diagnostics.Debug.WriteLine(" [预缓存] 顺序模式: 已经是最后几张图片，无需预缓存");
                    return;
                }
                
                //System.Diagnostics.Debug.WriteLine($" [预缓存] 顺序模式: 准备预缓存后续 {preloadList.Count} 张图片");
                
                // 异步加载
                await PreloadImagesAsync(preloadList, batch);
            }
            catch (OperationCanceledException)
            {
                //System.Diagnostics.Debug.WriteLine(" [预缓存] 顺序模式: 已取消");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" [预缓存] 顺序模式失败: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 关键帧模式预缓存
        
        /// <summary>
        /// 为关键帧模式预缓存（当前图片已经加载，无需额外预缓存图片）
        /// 关键帧模式下，一张图片的多个关键帧位置都在同一张图上，所以不需要预加载其他图片
        /// 但可以预先获取关键帧列表到缓存中
        /// </summary>
        public async Task PreloadForKeyframeModeAsync(int currentImageId)
        {
            await Task.CompletedTask;
            // 关键帧模式下，当前图片已经加载完毕
            // 不需要预缓存其他图片，因为所有关键帧都在同一张图上
            //System.Diagnostics.Debug.WriteLine($" [预缓存] 关键帧模式: 当前图片已加载 (ID:{currentImageId})");
        }
        
        #endregion
        
        #region 核心预加载逻辑
        
        /// <summary>
        /// 异步预加载图片列表
        /// </summary>
        private async Task PreloadImagesAsync(List<string> imagePaths, ImagePreloadPipeline.Batch batch)
        {
            // 保留DB的下一张/上一张优先顺序。默认不再后台做双尺寸GPU预渲染。
            await _pipeline.LoadAsync(batch, imagePaths).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 取消当前正在进行的预缓存任务
        /// </summary>
        private void CancelCurrentPreload()
        {
            _pipeline.Cancel();
        }
        
        #endregion
        
        #region 资源清理
        
        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            _pipeline.Dispose();
        }
        
        #endregion
    }
}



