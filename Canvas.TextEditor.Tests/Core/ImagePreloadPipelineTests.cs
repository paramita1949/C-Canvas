using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;
using Xunit;

namespace Canvas.TextEditor.Tests.Core
{
    public class ImagePreloadPipelineTests
    {
        [Fact]
        public async Task CancelledDecodeCannotPublishAndPrivatePixelsAreReleased()
        {
            using var files = new Files(); using var cache = Cache();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SKBitmap decoded = null;
            using var pipeline = new ImagePreloadPipeline(cache, 64, path =>
            {
                decoded = Bitmap(); started.TrySetResult(true); release.Task.GetAwaiter().GetResult(); return decoded;
            }, _ => 8);
            using var batch = pipeline.BeginBatch();
            var work = pipeline.LoadAsync(batch, new[] { files.One });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); pipeline.Cancel(); release.TrySetResult(true);
            Assert.Equal(0, await work);
            Assert.False(cache.TryGetValue(ImageFileCacheKey.GetRawKey(files.One), out _));
            Assert.Equal(IntPtr.Zero, decoded.Handle);
        }

        [Fact]
        public async Task NewBatchInvalidatesOldWorkAndDecodesDoNotOverlap()
        {
            using var files = new Files(); using var cache = Cache();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0, active = 0, maxActive = 0;
            using var pipeline = new ImagePreloadPipeline(cache, 64, path =>
            {
                int concurrent = Interlocked.Increment(ref active); maxActive = Math.Max(maxActive, concurrent);
                if (Interlocked.Increment(ref calls) == 1) { started.TrySetResult(true); release.Task.GetAwaiter().GetResult(); }
                Interlocked.Decrement(ref active); return Bitmap();
            }, _ => 8);
            using var first = pipeline.BeginBatch(); var oldWork = pipeline.LoadAsync(first, new[] { files.One });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var next = pipeline.BeginBatch(); var nextWork = pipeline.LoadAsync(next, new[] { files.Two });
            release.TrySetResult(true);
            Assert.Equal(0, await oldWork); Assert.Equal(1, await nextWork); Assert.Equal(1, maxActive);
            Assert.False(cache.TryGetValue(ImageFileCacheKey.GetRawKey(files.One), out _));
            Assert.True(cache.TryGetValue(ImageFileCacheKey.GetRawKey(files.Two), out _));
        }

        [Fact]
        public async Task HeaderBudgetSkipsLargeInputWithoutDecodingAndAllowsLaterSmallFile()
        {
            using var files = new Files(); using var cache = Cache(); int calls = 0;
            using var pipeline = new ImagePreloadPipeline(cache, 8, path => { calls++; return Bitmap(); }, path => path == files.One ? 16 : 8);
            using var batch = pipeline.BeginBatch();
            Assert.Equal(1, await pipeline.LoadAsync(batch, new[] { files.One, files.Two })); Assert.Equal(1, calls);
            Assert.False(cache.TryGetValue(ImageFileCacheKey.GetRawKey(files.One), out _));
            Assert.True(cache.TryGetValue(ImageFileCacheKey.GetRawKey(files.Two), out _));
        }

        [Fact]
        public async Task TotalDecodedBudgetStopsFurtherFullDecodes()
        {
            using var files = new Files(); using var cache = Cache(); int calls = 0;
            using var pipeline = new ImagePreloadPipeline(cache, 8, path => { calls++; return Bitmap(); }, _ => 8);
            using var batch = pipeline.BeginBatch();
            Assert.Equal(1, await pipeline.LoadAsync(batch, new[] { files.One, files.Two })); Assert.Equal(1, calls);
        }

        [Fact]
        public async Task ActualOversizedBitmapIsNotPublishedAndIsReleased()
        {
            using var files = new Files(); using var cache = Cache(); SKBitmap decoded = null;
            using var pipeline = new ImagePreloadPipeline(cache, 8, path => decoded = new SKBitmap(3, 1), _ => 8);
            using var batch = pipeline.BeginBatch();
            Assert.Equal(0, await pipeline.LoadAsync(batch, new[] { files.One })); Assert.Equal(IntPtr.Zero, decoded.Handle);
        }

        [Fact]
        public async Task DuplicatePathsAndAlreadyCachedInputsDoNotDecodeAgain()
        {
            using var files = new Files(); using var cache = Cache(); int calls = 0;
            using var pipeline = new ImagePreloadPipeline(cache, 64, path => { calls++; return Bitmap(); }, _ => 8);
            using (var batch = pipeline.BeginBatch()) Assert.Equal(1, await pipeline.LoadAsync(batch, new[] { files.One, files.One }));
            using (var batch = pipeline.BeginBatch()) Assert.Equal(0, await pipeline.LoadAsync(batch, new[] { files.One }));
            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task FileChangedDuringDecodeCannotEnterCacheUnderOldIdentity()
        {
            using var files = new Files(); using var cache = Cache(); SKBitmap decoded = null;
            using var pipeline = new ImagePreloadPipeline(cache, 64, path =>
            {
                File.AppendAllText(path, "changed"); return decoded = Bitmap();
            }, _ => 8);
            using var batch = pipeline.BeginBatch();
            Assert.Equal(0, await pipeline.LoadAsync(batch, new[] { files.One })); Assert.Equal(IntPtr.Zero, decoded.Handle);
        }

        [Fact]
        public async Task MissingCorruptAndEmptyInputsRemainSafe()
        {
            using var files = new Files(); using var cache = Cache();
            using var pipeline = new ImagePreloadPipeline(cache);
            using (var batch = pipeline.BeginBatch()) Assert.Equal(0, await pipeline.LoadAsync(batch, Array.Empty<string>()));
            using (var batch = pipeline.BeginBatch()) Assert.Equal(0, await pipeline.LoadAsync(batch, new[] { files.One, files.Root + "/missing", null, " " }));
        }

        [Fact]
        public async Task RealPngDecoderKeepsFullResolutionAndOriginalBytes()
        {
            using var files = new Files(); using var cache = Cache();
            using (var bitmap = new SKBitmap(17, 11)) { bitmap.Erase(SKColors.CornflowerBlue); using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(files.One, png.ToArray()); }
            var original = File.ReadAllBytes(files.One);
            using var pipeline = new ImagePreloadPipeline(cache);
            using var batch = pipeline.BeginBatch(); Assert.Equal(1, await pipeline.LoadAsync(batch, new[] { files.One }));
            Assert.True(cache.TryGetValue(ImageFileCacheKey.GetRawKey(files.One), out SKBitmap decoded));
            Assert.Equal(17, decoded.Width); Assert.Equal(11, decoded.Height); Assert.Equal(original, File.ReadAllBytes(files.One));
        }

        [Fact]
        public void DisposeRefusesNewBatches()
        {
            using var cache = Cache(); var pipeline = new ImagePreloadPipeline(cache); pipeline.Dispose(); pipeline.Dispose();
            Assert.Throws<ObjectDisposedException>(() => pipeline.BeginBatch());
        }

        [Fact]
        public async Task DisposeWhileDecodePendingPreventsPublication()
        {
            using var files = new Files(); using var cache = Cache();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var pipeline = new ImagePreloadPipeline(cache, 64, path => { started.TrySetResult(true); release.Task.GetAwaiter().GetResult(); return Bitmap(); }, _ => 8);
            using var batch = pipeline.BeginBatch(); var work = pipeline.LoadAsync(batch, new[] { files.One });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); pipeline.Dispose(); release.TrySetResult(true);
            Assert.Equal(0, await work);
        }

        [Fact]
        public void RawIdentityChangesOnOverwriteAndDeletionHasNoKey()
        {
            using var files = new Files(); string before = ImageFileCacheKey.GetRawKey(files.One);
            File.AppendAllText(files.One, "more"); Assert.NotEqual(before, ImageFileCacheKey.GetRawKey(files.One));
            File.Delete(files.One); Assert.Null(ImageFileCacheKey.GetRawKey(files.One));
        }

        [Fact]
        public void RenderKeysUseActualDimensionsAndEffectState()
        {
            Assert.NotEqual(ImageFileCacheKey.GetRenderKey("raw", 100, 50, false), ImageFileCacheKey.GetRenderKey("raw", 100, 60, false));
            Assert.NotEqual(ImageFileCacheKey.GetRenderKey("raw", 100, 50, false), ImageFileCacheKey.GetRenderKey("raw", 100, 50, true));
        }

        [Fact]
        public async Task PublicationReadThenEvictedNeverDisposesTheSharedBitmap()
        {
            using var files = new Files(); using var cache = new PublicationCache();
            using var pipeline = new ImagePreloadPipeline(cache, 64, _ => Bitmap(), _ => 8);
            using var batch = pipeline.BeginBatch();
            await pipeline.LoadAsync(batch, new[] { files.One });
            Assert.NotNull(cache.Borrowed);
            Assert.NotEqual(IntPtr.Zero, cache.Borrowed.Handle);
            Assert.Equal(SKColors.Red, cache.Borrowed.GetPixel(0, 0));
        }

        private sealed class PublicationCache : IMemoryCache
        {
            private readonly MemoryCache cache = Cache();
            public SKBitmap Borrowed;
            public bool TryGetValue(object key, out object value) => cache.TryGetValue(key, out value);
            public void Remove(object key) => cache.Remove(key);
            public ICacheEntry CreateEntry(object key) => new Entry(cache.CreateEntry(key), () =>
            {
                // 模拟前台已取得裸引用，然后缓存被Compact/替换。
                cache.TryGetValue(key, out Borrowed); cache.Remove(key);
            });
            public void Dispose() { cache.Dispose(); Borrowed?.Dispose(); }
            private sealed class Entry : ICacheEntry
            {
                private readonly ICacheEntry inner; private readonly Action published;
                public Entry(ICacheEntry inner, Action published) { this.inner=inner;this.published=published; }
                public object Key => inner.Key;
                public object Value { get => inner.Value; set => inner.Value=value; }
                public DateTimeOffset? AbsoluteExpiration { get => inner.AbsoluteExpiration; set => inner.AbsoluteExpiration=value; }
                public TimeSpan? AbsoluteExpirationRelativeToNow { get => inner.AbsoluteExpirationRelativeToNow; set => inner.AbsoluteExpirationRelativeToNow=value; }
                public TimeSpan? SlidingExpiration { get => inner.SlidingExpiration; set => inner.SlidingExpiration=value; }
                public System.Collections.Generic.IList<Microsoft.Extensions.Primitives.IChangeToken> ExpirationTokens => inner.ExpirationTokens;
                public System.Collections.Generic.IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => inner.PostEvictionCallbacks;
                public CacheItemPriority Priority { get => inner.Priority; set => inner.Priority=value; }
                public long? Size { get => inner.Size; set => inner.Size=value; }
                public void Dispose() { inner.Dispose(); published(); }
            }
        }

        private static MemoryCache Cache() => new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        private static SKBitmap Bitmap() { var result = new SKBitmap(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul); result.Erase(SKColors.Red); return result; }
        private sealed class Files : IDisposable
        {
            public readonly string Root = Path.Combine(Path.GetTempPath(), "Canvas-preload-test-" + Guid.NewGuid());
            public string One => Path.Combine(Root, "one.png"); public string Two => Path.Combine(Root, "two.png");
            public Files() { Directory.CreateDirectory(Root); File.WriteAllText(One, "fixture"); File.WriteAllText(Two, "fixture"); }
            public void Dispose() { Directory.Delete(Root, true); }
        }
    }
}
