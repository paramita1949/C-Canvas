using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 图片缓存管理器（LRU策略）
    /// 参考技术方案 8.1
    /// </summary>
    public class ImageCache
    {
        private readonly int _maxCacheSize;
        private readonly Dictionary<string, CacheItem> _cache;
        private readonly LinkedList<string> _lruList;
        private readonly object _lock = new object();

        /// <summary>
        /// 缓存项
        /// </summary>
        private class CacheItem
        {
            public BitmapImage Image { get; set; }
            public LinkedListNode<string> LruNode { get; set; }
            public DateTime LastAccessTime { get; set; }
        }

        /// <summary>
        /// 当前缓存大小
        /// </summary>
        public int CurrentSize => _cache.Count;

        /// <summary>
        /// 缓存命中次数
        /// </summary>
        public int HitCount { get; private set; }

        /// <summary>
        /// 缓存未命中次数
        /// </summary>
        public int MissCount { get; private set; }

        /// <summary>
        /// 缓存命中率
        /// </summary>
        public double HitRate => (HitCount + MissCount) > 0 ? (double)HitCount / (HitCount + MissCount) : 0;

        public ImageCache(int maxCacheSize = 50)
        {
            _maxCacheSize = maxCacheSize;
            _cache = new Dictionary<string, CacheItem>();
            _lruList = new LinkedList<string>();
        }

        /// <summary>
        /// 获取图片（同步）
        /// </summary>
        public BitmapImage Get(string imagePath)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(imagePath, out var item))
                {
                    // 缓存命中
                    HitCount++;
                    
                    // 更新LRU（移到链表头部）
                    _lruList.Remove(item.LruNode);
                    item.LruNode = _lruList.AddFirst(imagePath);
                    item.LastAccessTime = DateTime.Now;

                    Logger.Debug("缓存命中: {Path}", Path.GetFileName(imagePath));
                    return item.Image;
                }

                // 缓存未命中
                MissCount++;
                Logger.Debug("缓存未命中: {Path}", Path.GetFileName(imagePath));
                return null;
            }
        }

        /// <summary>
        /// 添加图片到缓存
        /// </summary>
        public void Put(string imagePath, BitmapImage image)
        {
            if (string.IsNullOrEmpty(imagePath) || image == null)
                return;

            lock (_lock)
            {
                // 如果已存在，先移除
                if (_cache.ContainsKey(imagePath))
                {
                    Remove(imagePath);
                }

                // 检查缓存大小，必要时移除最久未使用的项
                while (_cache.Count >= _maxCacheSize)
                {
                    RemoveLeastRecentlyUsed();
                }

                // 添加到缓存
                var node = _lruList.AddFirst(imagePath);
                _cache[imagePath] = new CacheItem
                {
                    Image = image,
                    LruNode = node,
                    LastAccessTime = DateTime.Now
                };

                Logger.Debug("添加到缓存: {Path}, 当前大小={Size}/{Max}", 
                    Path.GetFileName(imagePath), _cache.Count, _maxCacheSize);
            }
        }

        /// <summary>
        /// 异步加载并缓存图片
        /// </summary>
        public async Task<BitmapImage> GetOrLoadAsync(string imagePath)
        {
            // 先尝试从缓存获取
            var cachedImage = Get(imagePath);
            if (cachedImage != null)
                return cachedImage;

            // 缓存未命中，异步加载
            var image = await LoadImageAsync(imagePath);
            if (image != null)
            {
                Put(imagePath, image);
            }

            return image;
        }

        /// <summary>
        /// 异步加载图片
        /// </summary>
        private async Task<BitmapImage> LoadImageAsync(string imagePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(imagePath))
                    {
                        Logger.Warning("图片文件不存在: {Path}", imagePath);
                        return null;
                    }

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze(); // 冻结以便跨线程使用

                    Logger.Debug("加载图片: {Path}", Path.GetFileName(imagePath));
                    return bitmap;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "加载图片失败: {Path}", imagePath);
                    return null;
                }
            });
        }

        /// <summary>
        /// 预加载图片（异步）
        /// </summary>
        public async Task PreloadAsync(string[] imagePaths)
        {
            if (imagePaths == null || imagePaths.Length == 0)
                return;

            var tasks = new List<Task>();
            foreach (var path in imagePaths)
            {
                if (!_cache.ContainsKey(path))
                {
                    tasks.Add(GetOrLoadAsync(path));
                }
            }

            await Task.WhenAll(tasks);
            Logger.Debug("预加载完成: {Count}张图片", tasks.Count);
        }

        /// <summary>
        /// 移除图片
        /// </summary>
        public void Remove(string imagePath)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(imagePath, out var item))
                {
                    _lruList.Remove(item.LruNode);
                    _cache.Remove(imagePath);
                    Logger.Debug("移除缓存: {Path}", Path.GetFileName(imagePath));
                }
            }
        }

        /// <summary>
        /// 移除最久未使用的项（LRU）
        /// </summary>
        private void RemoveLeastRecentlyUsed()
        {
            if (_lruList.Last != null)
            {
                var oldestKey = _lruList.Last.Value;
                _lruList.RemoveLast();
                _cache.Remove(oldestKey);
                Logger.Debug("LRU移除: {Path}", Path.GetFileName(oldestKey));
            }
        }

        /// <summary>
        /// 清空缓存
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
                _lruList.Clear();
                HitCount = 0;
                MissCount = 0;
                Logger.Debug("清空缓存");
            }
        }

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        public string GetStatistics()
        {
            return $"缓存大小: {CurrentSize}/{_maxCacheSize}, " +
                   $"命中率: {HitRate:P2}, " +
                   $"命中: {HitCount}, 未命中: {MissCount}";
        }
    }
}

