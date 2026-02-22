using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 视频背景管理器
    /// 负责管理幻灯片视频背景的播放、缓存和资源释放
    /// </summary>
    public class VideoBackgroundManager : IVideoBackgroundManager
    {
        #region 字段

        /// <summary>
        /// MediaElement 缓存字典 (视频路径 -> MediaElement)
        /// </summary>
        private Dictionary<string, MediaElement> _mediaCache;

        /// <summary>
        /// 最大缓存数量
        /// </summary>
        private const int MAX_VIDEO_CACHE = 3;

        /// <summary>
        /// LRU访问顺序列表
        /// </summary>
        private LinkedList<string> _lruOrder;

        /// <summary>
        /// 当前正在播放的视频路径
        /// </summary>
        private string _currentVideoPath;

        #endregion

        #region 构造函数

        public VideoBackgroundManager()
        {
            _mediaCache = new Dictionary<string, MediaElement>();
            _lruOrder = new LinkedList<string>();

#if DEBUG
            //System.Diagnostics.Debug.WriteLine(" VideoBackgroundManager 已初始化");
#endif
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 获取或创建 MediaElement（用于主屏编辑器）
        /// </summary>
        /// <param name="videoPath">视频文件路径</param>
        /// <param name="loopEnabled">是否循环播放</param>
        /// <returns>MediaElement 实例</returns>
        public MediaElement GetOrCreateMediaElement(string videoPath, bool loopEnabled = true)
        {
            if (string.IsNullOrEmpty(videoPath) || !System.IO.File.Exists(videoPath))
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [视频背景] 视频文件不存在: {videoPath}");
#endif
                return null;
            }

            // 检查缓存
            if (_mediaCache.TryGetValue(videoPath, out MediaElement cachedMedia))
            {
                // 更新LRU顺序
                _lruOrder.Remove(videoPath);
                _lruOrder.AddFirst(videoPath);

#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [视频背景] 缓存命中: {System.IO.Path.GetFileName(videoPath)}");
#endif
                return cachedMedia;
            }

            // 创建新的 MediaElement
            var mediaElement = CreateMediaElement(videoPath, loopEnabled);

            // 添加到缓存
            AddToCache(videoPath, mediaElement);

#if DEBUG
            System.Diagnostics.Debug.WriteLine($" [视频背景] 创建新MediaElement: {System.IO.Path.GetFileName(videoPath)}");
#endif

            return mediaElement;
        }

        /// <summary>
        /// 播放指定视频
        /// </summary>
        /// <param name="videoPath">视频路径</param>
        public void Play(string videoPath)
        {
            if (string.IsNullOrEmpty(videoPath))
                return;

            var mediaElement = GetOrCreateMediaElement(videoPath);
            if (mediaElement != null)
            {
                mediaElement.Play();
                _currentVideoPath = videoPath;

#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [视频背景] 播放: {System.IO.Path.GetFileName(videoPath)}");
#endif
            }
        }

        /// <summary>
        /// 暂停当前视频
        /// </summary>
        public void Pause()
        {
            if (!string.IsNullOrEmpty(_currentVideoPath) && 
                _mediaCache.TryGetValue(_currentVideoPath, out MediaElement media))
            {
                media.Pause();

#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [视频背景] 暂停: {System.IO.Path.GetFileName(_currentVideoPath)}");
#endif
            }
        }

        /// <summary>
        /// 停止当前视频
        /// </summary>
        public void Stop()
        {
            if (!string.IsNullOrEmpty(_currentVideoPath) && 
                _mediaCache.TryGetValue(_currentVideoPath, out MediaElement media))
            {
                media.Stop();

#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [视频背景] 停止: {System.IO.Path.GetFileName(_currentVideoPath)}");
#endif
            }
        }

        /// <summary>
        /// 设置音量（0.0 - 1.0）
        /// </summary>
        public void SetVolume(double volume)
        {
            volume = Math.Clamp(volume, 0.0, 1.0);

            foreach (var media in _mediaCache.Values)
            {
                media.Volume = volume;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($" [视频背景] 音量设置为: {volume:P0}");
#endif
        }

        /// <summary>
        /// 清除指定视频的缓存
        /// </summary>
        public void RemoveFromCache(string videoPath)
        {
            if (_mediaCache.TryGetValue(videoPath, out MediaElement media))
            {
                media.Stop();
                media.Source = null;
                media.Close();

                _mediaCache.Remove(videoPath);
                _lruOrder.Remove(videoPath);

#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [视频背景] 移除缓存: {System.IO.Path.GetFileName(videoPath)}");
#endif
            }
        }

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public void ClearCache()
        {
            foreach (var media in _mediaCache.Values)
            {
                media.Stop();
                media.Source = null;
                media.Close();
            }

            _mediaCache.Clear();
            _lruOrder.Clear();
            _currentVideoPath = null;

#if DEBUG
            //System.Diagnostics.Debug.WriteLine(" [视频背景] 清除所有缓存");
#endif
        }

        /// <summary>
        /// 获取缓存状态信息
        /// </summary>
        public string GetCacheInfo()
        {
            return $"缓存数量: {_mediaCache.Count}/{MAX_VIDEO_CACHE}, 当前播放: {System.IO.Path.GetFileName(_currentVideoPath ?? "无")}";
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 创建 MediaElement
        /// </summary>
        private MediaElement CreateMediaElement(string videoPath, bool loopEnabled)
        {
            var mediaElement = new MediaElement
            {
                Source = new Uri(videoPath),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Volume = 0.5, // 默认音量50%
                IsMuted = false
            };

            // 循环播放
            if (loopEnabled)
            {
                mediaElement.MediaEnded += (s, e) =>
                {
                    mediaElement.Position = TimeSpan.Zero;
                    mediaElement.Play();
                };
            }

            // MediaOpened 事件
            mediaElement.MediaOpened += (s, e) =>
            {
#if DEBUG
                var duration = mediaElement.NaturalDuration;
                var size = $"{mediaElement.NaturalVideoWidth}x{mediaElement.NaturalVideoHeight}";
                System.Diagnostics.Debug.WriteLine($" [视频背景] MediaOpened - 时长: {duration}, 尺寸: {size}");
#endif
            };

            // MediaFailed 事件
            mediaElement.MediaFailed += (s, e) =>
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [视频背景] MediaFailed: {e.ErrorException?.Message}");
#endif
            };

            return mediaElement;
        }

        /// <summary>
        /// 添加到缓存（LRU策略）
        /// </summary>
        private void AddToCache(string videoPath, MediaElement mediaElement)
        {
            // 如果缓存已满，移除最久未使用的
            while (_mediaCache.Count >= MAX_VIDEO_CACHE && _lruOrder.Count > 0)
            {
                var oldest = _lruOrder.Last.Value;
                RemoveFromCache(oldest);

#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [视频背景] LRU淘汰: {System.IO.Path.GetFileName(oldest)}");
#endif
            }

            // 添加新的
            _mediaCache[videoPath] = mediaElement;
            _lruOrder.AddFirst(videoPath);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            ClearCache();

#if DEBUG
            //System.Diagnostics.Debug.WriteLine(" VideoBackgroundManager 已释放");
#endif
        }

        #endregion
    }
}



