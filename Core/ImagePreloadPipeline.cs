using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>原图的机会性预载：跨批次单解码、解码字节预算、取消与入缓存原子化。</summary>
    internal sealed class ImagePreloadPipeline : IDisposable
    {
        private readonly IMemoryCache _cache;
        private readonly long _maxDecodedBytes;
        private readonly Func<string, SKBitmap> _decode;
        private readonly Func<string, long> _estimateBytes;
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _decoderGate = new SemaphoreSlim(1, 1);
        private Batch _current;
        private bool _disposed;
        private int _activeLoads;

        public ImagePreloadPipeline(IMemoryCache cache, long maxDecodedBytes = 96L * 1024 * 1024,
            Func<string, SKBitmap> decoder = null, Func<string, long> estimateBytes = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            if (maxDecodedBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxDecodedBytes));
            _maxDecodedBytes = maxDecodedBytes;
            _decode = decoder ?? (path => SKBitmap.Decode(path));
            _estimateBytes = estimateBytes ?? EstimateDecodedBytes;
        }

        public Batch BeginBatch()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_current != null) _current.Cancelled = true;
                return _current = new Batch(this);
            }
        }

        public void Cancel()
        {
            lock (_gate) { if (_current != null) _current.Cancelled = true; }
        }

        private bool IsCurrent(Batch batch) => !_disposed && !batch.Cancelled && ReferenceEquals(_current, batch);

        public async Task<int> LoadAsync(Batch batch, IReadOnlyList<string> paths)
        {
            if (batch == null || batch.Owner != this) throw new ArgumentException("Batch belongs to another pipeline", nameof(batch));
            lock (_gate)
            {
                if (!IsCurrent(batch)) return 0;
                _activeLoads++;
            }
            bool entered = false;
            try
            {
                await _decoderGate.WaitAsync().ConfigureAwait(false);
                entered = true;
                return await Task.Run(() => LoadCore(batch, paths)).ConfigureAwait(false);
            }
            finally
            {
                if (entered) _decoderGate.Release();
                lock (_gate) { if (--_activeLoads == 0 && _disposed) _decoderGate.Dispose(); }
            }
        }

        private int LoadCore(Batch batch, IReadOnlyList<string> paths)
        {
            int loaded = 0;
            if (paths == null) return 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                lock (_gate) { if (!IsCurrent(batch)) break; }
                SKBitmap bitmap = null;
                try
                {
                    string key = ImageFileCacheKey.GetRawKey(path);
                    if (key == null || !seen.Add(key) || _cache.TryGetValue(key, out _)) continue;
                    long estimate = _estimateBytes(path);
                    if (estimate <= 0 || estimate > _maxDecodedBytes - batch.UsedBytes) continue;
                    bitmap = _decode(path);
                    if (bitmap == null) continue;
                    long bytes = bitmap.ByteCount;
                    if (bytes <= 0 || bytes > _maxDecodedBytes - batch.UsedBytes) continue;
                    // Cancel/BeginBatch/Dispose 也持有同一把锁；旧解码不能在取消返回后发布。
                    lock (_gate)
                    {
                        if (!IsCurrent(batch) || key != ImageFileCacheKey.GetRawKey(path)) continue;
                        if (_cache.TryGetValue(key, out _)) continue;
                        _cache.Set(key, bitmap, new MemoryCacheEntryOptions
                        {
                            Size = Math.Max(1L, (bytes + 1024 * 1024 - 1) / (1024 * 1024)),
                            Priority = CacheItemPriority.Normal,
                            SlidingExpiration = TimeSpan.FromMinutes(10)
                        });
                        batch.UsedBytes += bytes;
                        bitmap = null; // 已转移到既有共享LRU；不改变其裸引用/歌词回滚拥有权。
                        loaded++;
                    }
                }
                catch (Exception) { /* 机会性预载失败不影响前台同步加载。 */ }
                finally { bitmap?.Dispose(); }
            }
            return loaded;
        }

        private static long EstimateDecodedBytes(string path)
        {
            using var stream = SKFileStream.OpenStream(path);
            if (stream == null) return 0;
            using var codec = SKCodec.Create(stream);
            if (codec == null) return 0;
            return checked((long)codec.Info.Width * codec.Info.Height * 4);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (_current != null) _current.Cancelled = true;
                if (_activeLoads == 0) _decoderGate.Dispose();
            }
        }

        internal sealed class Batch : IDisposable
        {
            internal readonly ImagePreloadPipeline Owner;
            internal bool Cancelled;
            internal long UsedBytes;
            internal Batch(ImagePreloadPipeline owner) { Owner = owner; }
            public void Dispose()
            {
                lock (Owner._gate)
                {
                    Cancelled = true;
                    if (ReferenceEquals(Owner._current, this)) Owner._current = null;
                }
            }
        }
    }
}
