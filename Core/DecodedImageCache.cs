using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 幻灯片合成专用的原分辨率解码缓存。预算只计算驻留像素的 ByteCount，
    /// 被淘汰但仍借用的图片、超预算单次图片不驻留，最后一个借用释放时销毁。
    /// 借用者只能读取 Bitmap；需要变色时先 Copy，不得 Dispose 缓存的 Bitmap。
    /// </summary>
    internal sealed class DecodedImageCache : IDisposable
    {
        private readonly object _gate = new object();
        private readonly long _byteBudget;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<Entry> _lru = new LinkedList<Entry>();
        private long _residentBytes;
        private bool _disposed;

        public DecodedImageCache(long byteBudget = 128L * 1024 * 1024)
        {
            if (byteBudget < 0) throw new ArgumentOutOfRangeException(nameof(byteBudget));
            _byteBudget = byteBudget;
        }

        public long ResidentBytes
        {
            get { lock (_gate) return _residentBytes; }
        }

        public Lease Borrow(string path)
        {
            // Decode 也在锁内：同文件并发 miss 不重复解码；Dispose 与入缓存原子化。
            // 这是同步合成入口的缓存，不向外暴露 Bitmap 的拥有权。
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (string.IsNullOrWhiteSpace(path)) return null;
                var file = new FileInfo(path);
                string fullPath = file.FullName;
                bool readable = TryReadIdentity(file, out long modifiedTicks, out long length);
                if (_entries.TryGetValue(fullPath, out var existing))
                {
                    if (readable && existing.ModifiedTicks == modifiedTicks && existing.FileLength == length)
                    {
                        _lru.Remove(existing.Node);
                        _lru.AddFirst(existing.Node);
                        return CreateLease(existing);
                    }

                    // 删除或同路径覆盖后不能继续使用旧版本，但旧借用可完成当前绘制。
                    Retire(existing);
                }
                if (!readable) return null;

                var bitmap = SKBitmap.Decode(fullPath);
                if (bitmap == null) return null; // 失败不缓存，下一次仍可恢复。

                var entry = new Entry(fullPath, modifiedTicks, length, bitmap);
                bool stableIdentity = TryReadIdentity(file, out long afterTicks, out long afterLength)
                    && afterTicks == modifiedTicks && afterLength == length;

                // 解码期间文件变化、图片超过预算：仅借给本次绘制，不循环解码或挤占驻留。
                if (stableIdentity && entry.ByteCount <= _byteBudget)
                {
                    while (_lru.Last != null && _residentBytes > _byteBudget - entry.ByteCount)
                        Retire(_lru.Last.Value);
                    entry.Node = _lru.AddFirst(entry);
                    _entries.Add(fullPath, entry);
                    _residentBytes += entry.ByteCount;
                }
                return CreateLease(entry);
            }
        }

        private static bool TryReadIdentity(FileInfo file, out long modifiedTicks, out long length)
        {
            modifiedTicks = 0;
            length = 0;
            try
            {
                file.Refresh();
                if (!file.Exists) return false;
                modifiedTicks = file.LastWriteTimeUtc.Ticks;
                length = file.Length;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private Lease CreateLease(Entry entry)
        {
            entry.Borrowers++;
            return new Lease(entry.Bitmap, () => Release(entry));
        }

        private void Release(Entry entry)
        {
            lock (_gate)
            {
                entry.Borrowers--;
                if (entry.Borrowers == 0 && entry.Node == null)
                    entry.Bitmap.Dispose();
            }
        }

        private void Retire(Entry entry)
        {
            _entries.Remove(entry.Path);
            _lru.Remove(entry.Node);
            entry.Node = null;
            _residentBytes -= entry.ByteCount;
            if (entry.Borrowers == 0) entry.Bitmap.Dispose();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                while (_lru.Last != null) Retire(_lru.Last.Value);
            }
        }

        private sealed class Entry
        {
            public readonly string Path;
            public readonly long ModifiedTicks;
            public readonly long FileLength;
            public readonly SKBitmap Bitmap;
            public readonly long ByteCount;
            public int Borrowers;
            public LinkedListNode<Entry> Node;

            public Entry(string path, long modifiedTicks, long fileLength, SKBitmap bitmap)
            {
                Path = path;
                ModifiedTicks = modifiedTicks;
                FileLength = fileLength;
                Bitmap = bitmap;
                ByteCount = bitmap.ByteCount;
            }
        }

        internal sealed class Lease : IDisposable
        {
            private readonly SKBitmap _bitmap;
            private Action _release;

            internal Lease(SKBitmap bitmap, Action release)
            {
                _bitmap = bitmap;
                _release = release;
            }

            public SKBitmap Bitmap
            {
                get
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) == null, this);
                    return _bitmap;
                }
            }

            public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}
